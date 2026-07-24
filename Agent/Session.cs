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
        public string Title { get; set; } = "对话";
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
            SafetyManager = new SafetyManager(this);
            UsageManager = new UsageManager();
            AgentToolManager = new AgentToolManager(this);
            AgentToolManager.LoadTools();
            AiService = new AiService(this, Core.ProviderManager);
            AiService.LoadDefaultModel();
            DataManager = new DataManager(this);
            MessageManager = new MessageManager(this);
        }
        public async Task CompleteChat(CompleteChatOptions options, CancellationToken cancellationToken)
        {
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
                    EventBus.Publish(new AgentErrorEvent { Message = ret.Exception.Message, SessionId = SessionId });
                    break;
                }
                EventBus.Publish(new ContentEndEvent { SessionId = SessionId, FullContent = ret.Content });
                MessageManager.AddAssistantMessageByCompletionResult(ret, ret.ToolsCalls.Count > 0, true);
                UsageManager.Accumulate(ret.Usage);
                EventBus.Publish(new AgentUsageChangedEvent
                {
                    SessionId = SessionId,
                    CachedTokens = UsageManager.CachedTokens,
                    ReasoningTokens = UsageManager.ReasoningTokens,
                    TotalTokens = UsageManager.TotalTokens,
                    PromptTokens = UsageManager.PromptTokens,
                    CompletionTokens = UsageManager.CompletionTokens,
                    RoundCount = UsageManager.RoundCount,
                    CacheHitRate = UsageManager.CacheHitRate
                });
                if (ret.ToolsCalls.Count == 0)
                    break;
                for (int i = 0; i < ret.ToolsCalls.Count; i++)
                {
                    if (toolError >= options.MaxToolError)
                        throw new PandoraException("Too Many Tools Error");

                    if (toolUse >= options.MaxToolsUse)
                        throw new PandoraException("Too Many Tools Use");
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
                        throw new Exception("Tool Use Error");
                    }
                }
                TextFileState.Locked = false;
            }
            DataManager.Flush();
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
