using OpenAI;
using OpenAI.Models.Chat;
using Pandora.Event;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Pandora.Agent.AiService;

namespace Pandora.Agent
{
    public class Session : ISession
    {
        public string SessionId { get; private set; }
        public string Title { get; set; } = "";
        public ICore Core { get; private set; }
        public WorkMode WorkMode { get; private set; }
        public IMessageManager MessageManager { get; private set; }
        public IUsageManager UsageManager { get; private set; }
        public IAgentToolManager AgentToolManager { get; private set; }
        public IAiService AiService { get; private set; }
        public IEventBus EventBus { get; private set; }
        public IDataManager DataManager { get; private set; }
        public ISafetyManager SafetyManager { get; private set; }
        public IAgentEnvironment AgentEnvironment { get; private set; }
        public long CreatedTime { get; private set; }
        public long UpdatedTime {  get; private set; }
        public FileState TextFileState { get; private set; }
        public bool IsSubAgent { get; set; } = false;
        public SessionChangeInfo ChangeInfo { get; set; }
        public Session(ICore core, SessionInfo info)
        {
            Core = core;
            EventBus = new EventBus();
            ChangeInfo = new SessionChangeInfo();
            TextFileState = new FileState(this);
            TextFileState.OnChanged = () =>
            {
                ChangeInfo.TextFile = TextFileState.GetChangedFilesStr();
            };
            WorkMode = info.WorkMode;
            SessionId = info.SessionId;
            Title = info.Title?? "";
            AgentToolManager = new AgentToolManager(this);
            AgentToolManager.LoadTools();
            if (info.ToolFullLoad != null) AgentToolManager.FullLoadTool(info.ToolFullLoad);
            MessageManager = new MessageManager(this, DataManagerStatic.ReadMessages(info));
            DataManager = new DataManager(this);
            AgentEnvironment = new AgentEnvironment(this);
            AgentEnvironment.SetWorkingDirectory(info.WorkingDirectory, true);
            UsageManager = new UsageManager(this);
            UsageManager.Accumulate(info.Usage);
            SafetyManager = new SafetyManager(this);
            AiService = new AiService(this, Core.ProviderManager);
            AiService.LoadDefaultModel();
            if (info.AiServiceModelName!=null && info.AiServiceProviderId!=null)
            {
                AiService.SwitchModel(info.AiServiceProviderId, info.AiServiceModelName);
            }
            
        }
        public Session(ICore core, string sessionId, WorkMode workMode)
        {
            ChangeInfo = new SessionChangeInfo();
            TextFileState = new FileState(this);
            TextFileState.OnChanged = () =>
            {
                ChangeInfo.TextFile = TextFileState.GetChangedFilesStr();
            };
            Core = core;
            SessionId = sessionId;
            WorkMode = workMode;
            EventBus = new EventBus();
            AgentEnvironment = new AgentEnvironment(this);
            DataManager = new DataManager(this);
            SafetyManager = new SafetyManager(this);
            UsageManager = new UsageManager(this);
            AgentToolManager = new AgentToolManager(this);
            AgentToolManager.LoadTools();
            AiService = new AiService(this, Core.ProviderManager);
            AiService.LoadDefaultModel(true);
            MessageManager = new MessageManager(this);
            AgentEnvironment.SetWorkingDirectory(AgentEnvironment.WorkingDirectory,true);
        }
        
        public async Task CompleteChatBackOff(CompleteChatOptions options, CancellationToken cancellationToken, BackOffOptions backOffOptions)
        {
            int attempt=0;
            while (!cancellationToken.IsCancellationRequested) 
            {
                attempt++;
                if (attempt > backOffOptions.MaxAttemptCount)
                {
                    throw new PandoraException(ErrorCode.RetryExhausted, errorData: new { Attempts = backOffOptions.MaxAttemptCount });
                }
                CompleteChatResult ret;
                try
                {
                    ret=await CompleteChat(options, cancellationToken);
                    if (ret.AiServiceException != null)
                    {
                        throw ret.AiServiceException;
                    }
                    return;
                }
                catch (ApiException a) 
                {
                    if (Utils.OpenAIIsStopStatusCode(a.StatusCode))
                    {
                        return;
                    }
                    int delay = a.RetryAfter != -1
                        ? a.RetryAfter + (int)backOffOptions.BaseTime.TotalMilliseconds / 2
                        : (int)backOffOptions.BaseTime.TotalMilliseconds + (int)Math.Pow(2, attempt) * 1000;
                    await DelayAndPublish(delay, cancellationToken);
                    Logger.Instance.Log(LogLevel.Warning, $"API Retry after {a.RetryAfter} ms, attempt={attempt}");
                    continue;
                }
                catch (JsonException)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    await DelayAndPublish((int)backOffOptions.BaseTime.TotalMilliseconds + (int)Math.Pow(2, attempt) * 1000, cancellationToken);
                    continue;
                }
            }
        }
        private async Task DelayAndPublish(int ms,CancellationToken token)
        {
            EventBus.Publish(new AgentInfoEvent { Message = $"重试中，等待 {(ms / 1000.0):F1} 秒...", SessionId = SessionId });
            await Task.Delay(ms, token);
        }
        public async Task<CompleteChatResult> CompleteChat(CompleteChatOptions options, CancellationToken cancellationToken)
        {
            CompleteChatResult result = new();
            await TextFileState.FindcChangedFiles();
            uint toolUse = 0;
            uint toolError = 0;
            while (true)
            {
                CompletionResult ret;
                var messageId = Guid.NewGuid().ToString();
                EventBus.Publish(new AssistantMessageStartEvent { SessionId = SessionId, MessageId = messageId });
                var request = new ChatCompletionRequest
                {
                    Messages = MessageManager.GetMessages(),
                    Tools = AgentToolManager.GetAgentFullToolDefinitions(WorkMode),
                    ToolChoice = "auto",
                    Model = AiService.ChatModel.ModelName,
                    ReasoningEffort = AiService.CurrentReasoningEffort,
                    Temperature = 0,
                };
                ret = await AiService.StreamCompletionAsync(request, cancellationToken);
                if (ret.Exception != null)
                {
                    result.AiServiceException = ret.Exception;
                    break;
                }
                EventBus.Publish(new ContentEndEvent { SessionId = SessionId, FullContent = ret.Content });
                MessageManager.AddAssistantMessageByCompletionResult(ret, ret.ToolsCalls.Count > 0, true);
                UsageManager.Accumulate(ret.Usage,true);
                EventBus.Publish(new AgentUsageChangedEvent
                {
                    SessionId = SessionId,
                    CachedTokens = UsageManager.CachedTokens,
                    ReasoningTokens = UsageManager.ReasoningTokens,
                    TotalTokens = UsageManager.TotalTokens,
                    PromptTokens = UsageManager.PromptTokens,
                    CompletionTokens = UsageManager.CompletionTokens,
                    ContextLength = UsageManager.ContextLength,
                    RoundCount = UsageManager.RoundCount,
                    CacheHitRate = UsageManager.CacheHitRate
                });
                if (ret.ToolsCalls.Count == 0)
                    break;
                for (int i = 0; i < ret.ToolsCalls.Count; i++)
                {
                    if (toolError >= options.MaxToolError)
                        throw new PandoraException(ErrorCode.TooManyToolErrors);

                    if (toolUse >= options.MaxToolsUse)
                        throw new PandoraException(ErrorCode.TooManyToolUses);
                    var toolCall = ToolUse(ret.ToolsCalls[i].ToolName, ret.ToolsCalls[i].Parameters);
                    bool success = toolCall.retSatus == ToolsResult.Success;
                    EventBus.Publish(new ToolCallEndEvent
                    {
                        SessionId = SessionId,
                        MessageId = messageId,
                        ToolCallId = ret.ToolsCalls[i].ToolCallId,
                        ToolName = ret.ToolsCalls[i].ToolName,
                        Arguments = ret.ToolsCalls[i].Parameters,
                        Result = toolCall.ret?.Text ?? "",
                        Success = success
                    });
                    if (success)
                    {
                        toolUse++;
                        toolError = 0;
                        MessageManager.AddToolCall(ret.ToolsCalls[i].ToolCallId, toolCall.ret);
                    }
                    else if (toolCall.retSatus == ToolsResult.ParametersError)
                    {
                        toolError++;
                        MessageManager.AddToolCall(ret.ToolsCalls[i].ToolCallId, toolCall.ret);
                    }
                    else if (toolCall.retSatus == ToolsResult.UnKnownError)
                    {
                        Logger.Instance.Log(LogLevel.Error, $"Tool Use Error: {ret.ToolsCalls[i].ToolName} return {toolCall.ret?.Text}",nameof(CompleteChat));
                        throw new PandoraException(ErrorCode.ToolUseError, errorData: new { ToolName = ret.ToolsCalls[i].ToolName });
                    }
                }
                TextFileState.Locked = false;
            }
            return result;
        }
        public (MessageContent? ret, ToolsResult retSatus) ToolUse(string toolName, string parameters)
        {
            JsonObject? jObj;
            jObj = TryParseJsonObject(parameters);
            if (jObj == null)
                return (new MessageContent("parameters error"), ToolsResult.ParametersError);
            if (AgentToolManager.Tools.TryGetValue(toolName, out AgentTool? tool) && tool != null)
            {
                (MessageContent? ret, ToolsResult retSatus) k;
                try
                {
                    k = tool.ToolFunction(this, new AgentToolParameterValue(jObj, tool.Parameters, tool.ParametersTypeCheck));
                }
                catch (Exception e)
                {
                    Logger.Instance.Log(LogLevel.Error, $"Tool Use Error: {toolName} catch {e}",nameof(ToolUse));
                    return (new MessageContent($"tool error: {e.Message}"), ToolsResult.UnKnownError);
                }
                
                if (k.ret == null)
                    return (new MessageContent("tool return null"), k.retSatus);
                return (k.ret, k.retSatus);
            }
            else
            {
                return (new MessageContent("tool not found"), ToolsResult.ParametersError);
            }
        }
        private static JsonObject? TryParseJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var node = JsonNode.Parse(json);
            return node as JsonObject;
        }
    }
}
