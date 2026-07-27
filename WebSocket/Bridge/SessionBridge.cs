using System.Collections.Concurrent;
using Pandora.Interfaces;
using Pandora.Models;
using Pandora.WebSocket.Server;

namespace Pandora.WebSocket.Bridge
{
    public class SessionBridge
    {
        private readonly ISession _session;
        private readonly WsConnection _conn;
        private string _currentMessageId = "";
        private string _currentContent = "";

        // Static: TCS 字典，跨实例共享，供 WsMessageHandler 回调
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> FileAccessPending = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> BashAccessPending = new();
        public static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> AskUserQuestionPending = new();

        public SessionBridge(ISession session, WsConnection conn)
        {
            _session = session;
            _conn = conn;
        }

        /// <summary>订阅 EventBus 事件，绑定到当前 WebSocket 连接</summary>
        public void Subscribe()
        {
            _session.EventBus.Subscribe<AssistantMessageStartEvent>(OnAssistantMessageStart);
            _session.EventBus.Subscribe<AgentTextOutputEvent>(OnTextOutput);
            _session.EventBus.Subscribe<ReasoningEndEvent>(OnReasoningEnd);
            _session.EventBus.Subscribe<ContentEndEvent>(OnContentEnd);
            _session.EventBus.Subscribe<ToolCallEndEvent>(OnToolCallEnd);
            _session.EventBus.Subscribe<AgentInfoEvent>(OnInfo);
            _session.EventBus.Subscribe<AgentUsageChangedEvent>(OnUsageChanged);
            _session.EventBus.Subscribe<FileAccessConfirmEvent, bool>(OnFileAccessConfirm);
            _session.EventBus.Subscribe<BashConfirmEvent, bool>(OnBashConfirm);
            _session.EventBus.Subscribe<SessionTitleChangedEvent>(OnSessionTitleChanged);
            _session.EventBus.Subscribe<AskUserQuestionEvent, string>(OnAskUserQuestion);
        }

        /// <summary>取消订阅（CompleteChat 结束后调用，避免影响其他连接）</summary>
        public void Unsubscribe()
        {
            // EventBus.Subscribe 会覆盖，所以不需要显式 Unsubscribe
            // 但保留此方法供未来使用
        }

        /// <summary>供 WsMessageHandler 调用：解析文件访问批准</summary>
        public static void ResolveFileAccess(string requestId, bool approved)
        {
            if (FileAccessPending.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(approved);
            }
        }

        /// <summary>供 WsMessageHandler 调用：解析 Bash 批准</summary>
        public static void ResolveBashAccess(string requestId, bool approved)
        {
            if (BashAccessPending.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(approved);
            }
        }

        /// <summary>供 WsMessageHandler 调用：解析用户问题回答</summary>
        public static void ResolveAskUserQuestion(string requestId, string answer)
        {
            if (AskUserQuestionPending.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(answer);
            }
        }

        // ============ Event Handlers ============

        private void OnAssistantMessageStart(AssistantMessageStartEvent e)
        {
            _currentMessageId = e.MessageId;
            _currentContent = "";
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.AssistantMessageStart(e.SessionId, e.MessageId)));
        }

        private void OnTextOutput(AgentTextOutputEvent e)
        {
            if (!string.IsNullOrEmpty(e.Reasoning))
            {
                _conn.SendFireAndForget(
                    Protocol.WsProtocol.Serialize(
                        Protocol.WsProtocol.StreamToken(e.SessionId, "reasoning", e.Reasoning)));
            }
            if (!string.IsNullOrEmpty(e.Content))
            {
                _currentContent += e.Content;
                _conn.SendFireAndForget(
                    Protocol.WsProtocol.Serialize(
                        Protocol.WsProtocol.StreamToken(e.SessionId, "content", e.Content)));
            }
        }

        private void OnReasoningEnd(ReasoningEndEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.ReasoningEnd(e.SessionId)));
        }

        private void OnContentEnd(ContentEndEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.ContentEnd(e.SessionId, e.FullContent)));
        }

        private void OnToolCallEnd(ToolCallEndEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.ToolCall(
                        e.SessionId, e.MessageId, e.ToolCallId,
                        e.ToolName, e.Success ? "completed" : "failed",
                        e.Arguments, e.Success)));
        }

        private void OnInfo(AgentInfoEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.Info(e.SessionId, e.Message)));
        }

        private void OnUsageChanged(AgentUsageChangedEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.UsageUpdate(
                        e.SessionId, e.PromptTokens, e.CompletionTokens,
                        e.TotalTokens, e.CachedTokens, e.ReasoningTokens,
                        e.RoundCount, e.CacheHitRate, e.ContextLength)));
        }

        private void OnSessionTitleChanged(SessionTitleChangedEvent e)
        {
            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.SessionTitleChanged(e.SessionId, e.Title)));
        }

        private string OnAskUserQuestion(AskUserQuestionEvent e)
        {
            var tcs = new TaskCompletionSource<string>();
            AskUserQuestionPending[e.RequestId] = tcs;

            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.AskUserQuestion(e.SessionId, e.RequestId, e.Question, e.Options)));

            return tcs.Task.GetAwaiter().GetResult();
        }

        private bool OnFileAccessConfirm(FileAccessConfirmEvent e)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<bool>();
            FileAccessPending[requestId] = tcs;

            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.FileAccessRequest(e.SessionId, requestId, e.File)));

            // 同步阻塞，等待 WebSocket 客户端响应
            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        private bool OnBashConfirm(BashConfirmEvent e)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<bool>();
            BashAccessPending[requestId] = tcs;

            _conn.SendFireAndForget(
                Protocol.WsProtocol.Serialize(
                    Protocol.WsProtocol.BashAccessRequest(e.SessionId, requestId, e.Command)));

            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }
    }
}
