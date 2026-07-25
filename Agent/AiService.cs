using System.Net.Http;
using System.Text;
using OpenAI;
using OpenAI.Models.Audio;
using OpenAI.Models.Chat;
using OpenAI.Models.Shared;
using Pandora.Event;
using Pandora.Interfaces;
using Pandora.Models;

namespace Pandora.Agent
{
    public struct CurrentModelInfo
    {
        public string ProviderId { get; set; }
        public string ModelName { get; set; }

        public readonly bool IsEmpty => string.IsNullOrEmpty(ProviderId) && string.IsNullOrEmpty(ModelName);
    }

    public class AiService : IAiService
    {
        private readonly ISession _session;
        private OpenAIClient? _client;
        private readonly ProviderManager _providerManager;

        public CurrentModelInfo ChatModel { get; private set; }
        public CurrentModelInfo AsrModel { get; private set; }

        public ReasoningEffort CurrentReasoningEffort { get; set; } = ReasoningEffort.Medium;

        public AiService(ISession session, ProviderManager providerManager)
        {
            _session = session;
            _providerManager = providerManager;
        }
        public async Task<CompletionResult> StreamCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("OpenAIClient not initialized");
            }
            StringBuilder reasoning = new();
            StringBuilder content = new();
            List<IToolCall> toolCalls = [];
            Usage? usage = null;
            bool reasoningStarted = false;
            bool reasoningEndSent = false;
            try
            {
                await foreach (var chunk in _client.Chat.StreamCompleteAsync(request, cancellationToken))
                {
                    if (chunk.Usage != null)
                    {
                        usage = chunk.Usage;
                        continue;
                    }
                    var c = chunk.Choices.FirstOrDefault();
                    if (c?.Delta == null) { continue; }
                    if (!string.IsNullOrEmpty(c.Delta.Content))
                    {
                        if (reasoningStarted && !reasoningEndSent)
                        {
                            _session.EventBus.Publish(new ReasoningEndEvent() { SessionId = _session.SessionId });
                            reasoningEndSent = true;
                        }
                        content.Append(c.Delta.Content);
                        _session.EventBus.Publish(new AgentTextOutputEvent()
                        {
                            SessionId = _session.SessionId,
                            Content = c.Delta.Content
                        });
                    }
                    if (!string.IsNullOrEmpty(c.Delta.ReasoningContent))
                    {
                        reasoningStarted = true;
                        reasoning.Append(c.Delta.ReasoningContent);
                        _session.EventBus.Publish(new AgentTextOutputEvent()
                        {
                            SessionId = _session.SessionId,
                            Reasoning = c.Delta.ReasoningContent
                        });
                    }
                    if (c.Delta.ToolCalls != null)
                    {
                        if (reasoningStarted && !reasoningEndSent)
                        {
                            _session.EventBus.Publish(new ReasoningEndEvent() { SessionId = _session.SessionId });
                            reasoningEndSent = true;
                        }
                        for (int i = 0; i < c.Delta.ToolCalls.Count; i++)
                        {
                            if (!string.IsNullOrEmpty(c.Delta.ToolCalls[i].Id))
                            {
                                toolCalls.Add(new StreamToolCall()
                                {
                                    ToolCallId = c.Delta.ToolCalls[i].Id!
                                });
                                _session.EventBus.Publish(new AgentToolCallEvent()
                                {
                                    SessionId = _session.SessionId,
                                    CallId = toolCalls[i].ToolCallId,
                                    ToolName = null,
                                    Arguments = null
                                });
                            }
                            var t = c.Delta.ToolCalls[i].FunctionCall;
                            int index = c.Delta.ToolCalls[i].Index;
                            if (t == null) { continue; }
                            if (!string.IsNullOrEmpty(t.Name))
                            {
                                toolCalls[index].ToolName = t.Name;
                                if (_session.AgentToolManager.Tools.TryGetValue(t.Name, out var tool) && tool.ParametersStreamOutput)
                                {
                                    toolCalls[index].EnableStreamOutput();
                                }
                                _session.EventBus.Publish(new AgentToolCallEvent()
                                {
                                    SessionId = _session.SessionId,
                                    CallId = toolCalls[index].ToolCallId,
                                    ToolName = t.Name,
                                    Arguments = null
                                });
                            }
                            if (!string.IsNullOrEmpty(t.Arguments))
                            {
                                var parsed = toolCalls[index].AddArguments(t.Arguments);
                                if (parsed != null)
                                {
                                    _session.EventBus.Publish(new AgentToolCallEvent()
                                    {
                                        SessionId = _session.SessionId,
                                        CallId = toolCalls[index].ToolCallId,
                                        ToolName = t.Name,
                                        Arguments = parsed
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new CompletionResult(reasoning.ToString(), content.ToString(), [], null, ex);
            }
            // Ensure reasoning_end is sent even if no content/toolcalls after reasoning
            if (reasoningStarted && !reasoningEndSent)
            {
                _session.EventBus.Publish(new ReasoningEndEvent() { SessionId = _session.SessionId });
            }
            return new CompletionResult(reasoning.ToString(), content.ToString(), toolCalls, usage);
        }
        /// <summary>从 ProviderManager 加载默认 chat 模型</summary>
        public void LoadDefaultModel(bool flush = false)
        {
            var resolved = _providerManager.ResolveModel(_providerManager.DefaultChatModel)
                ?? throw new InvalidOperationException("No default chat model configured in config.json");
            var resolvedAudio = _providerManager.ResolveModel(_providerManager.DefaultAsrModel);
            ApplyResolved(resolved, resolvedAudio);
            if (flush)
            {
                _session.DataManager.SetModel(resolved.ProviderId, resolved.ModelName);
            }
        }

        /// <summary>运行时切换供应商和模型</summary>
        public bool SwitchModel(string providerId, string modelName,bool flush = false)
        {
            var resolved = _providerManager.ResolveModel(new ModelSelection { Provider = providerId, Model = modelName });
            if (resolved == null) return false;
            ApplyResolved(resolved);
            if (flush)
            {
                _session.DataManager.SetModel(resolved.ProviderId, resolved.ModelName);
            }
            return true;
        }

        private void ApplyResolved(ResolvedModel chatResolved,ResolvedModel? audioResolved=null)
        {
            if (audioResolved==null)
            {
                audioResolved= _providerManager.ResolveModel(_providerManager.DefaultAsrModel);
            }
            _client = new OpenAIClient(
                GetHttpClient(chatResolved), chatResolved.ApiKey, chatResolved.BaseUrl,
                audio: audioResolved != null
                    ? new OpenAIClient.ServiceConfig { HttpClient = GetHttpClient(audioResolved), ApiKey = audioResolved.ApiKey, BaseUrl = audioResolved.BaseUrl }
                    : null);
            ChatModel = new CurrentModelInfo { ProviderId = chatResolved.ProviderId, ModelName = chatResolved.ModelName };
            AsrModel = audioResolved != null ? new CurrentModelInfo { ProviderId = audioResolved.ProviderId, ModelName = audioResolved.ModelName } : default;
        }

        private HttpClient GetHttpClient(ResolvedModel resolved) =>
            resolved.UseProxy
                ? _session.Core.HttpClientFactoryProxy.CreateClient()
                : _session.Core.HttpClientFactory.CreateClient();

        public void LoadDefaultAsrModel()
        {
            var resolved = _providerManager.ResolveModel(_providerManager.DefaultAsrModel);
            if (resolved == null)
            {
                AsrModel = new CurrentModelInfo();
            return;
        }
        AsrModel = new CurrentModelInfo { ProviderId = resolved.ProviderId, ModelName = resolved.ModelName };
        }

        public bool SwitchAsrModel(string providerId, string modelName)
        {
            var resolved = _providerManager.ResolveModel(new ModelSelection { Provider = providerId, Model = modelName });
            if (resolved == null)
            {
                AsrModel = new CurrentModelInfo();
            return false;
        }
        AsrModel = new CurrentModelInfo { ProviderId = resolved.ProviderId, ModelName = resolved.ModelName };
        return true;
        }

        /// <summary>调用 ASR 模型转写音频</summary>
        public async Task<string> TranscribeAsync(byte[] audioBytes, string format = "webm")
        {
            if (_client == null)
                throw new InvalidOperationException("OpenAIClient not initialized");
            if (AsrModel.IsEmpty)
                LoadDefaultAsrModel();
            if (AsrModel.IsEmpty)
                throw new InvalidOperationException("No ASR model configured");

            using var stream = new MemoryStream(audioBytes);
            var response = await _client.Audio.CreateTranscriptionAsync(
                new AudioTranscriptionRequest
                {
                    File = stream,
                    FileName = $"audio.{format}",
                    Model = AsrModel.ModelName
                });
            return response.Text;
        }

        public async Task<CompletionResult> CompletionAsync(ChatCompletionRequest request)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("OpenAIClient not initialized");
            }
            List<IToolCall> chatToolCalls = new List<IToolCall>();
            var ret = (await _client.Chat.CompleteAsync(request));
            var t = ret.Choices.FirstOrDefault();
            if (t == null || t.Message==null)
                    return new CompletionResult("", "", [],null, new PandoraException("No message returned"));
            if (t.Message.ToolCalls != null)
            {
                foreach (var toolCall in t.Message.ToolCalls)
                {
                    ChatToolCall chatToolCall = new ChatToolCall();
                    if (toolCall.Id!=null) { chatToolCall.ToolCallId = toolCall.Id; }
                    if (toolCall.FunctionCall==null) { continue; }
                    if(toolCall.FunctionCall.Name!=null) { chatToolCall.ToolName = toolCall.FunctionCall.Name; }
                    if(toolCall.FunctionCall.Arguments!=null) { chatToolCall.Parameters = toolCall.FunctionCall.Arguments; }
                    chatToolCalls.Add(chatToolCall);
                }
            }
            t.Message.ReasoningContent ??= "";
            CompletionResult completionResult = new CompletionResult(t.Message.ReasoningContent, t.Message.Content.Text,chatToolCalls,ret.Usage);
            return completionResult;
        }
        public class CompletionResult
        {
            public string Reasoning;
            public string Content;
            public List<IToolCall> ToolsCalls;
            public Usage? Usage;
            public Exception? Exception;
            public CompletionResult(string reasoning, string content, List<IToolCall> toolCalls, Usage? usage, Exception? exception = null)
            {
                Reasoning = reasoning;
                Content = content;
                ToolsCalls = toolCalls;
                Usage = usage;
                Exception = exception;
            }
            public IList<ToolCall> ToToolCalls()
            {
                return [.. ToolsCalls.Select(t => t.ToToolCall())];
            }
        }
    }
}
