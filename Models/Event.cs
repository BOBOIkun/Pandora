using Pandora.Agent;
using Pandora.Interfaces;

namespace Pandora.Models
{
    public class EventBase : IAgentEvent
    {
        public required string SessionId { get; set; }
    }
    public class EventBaseWithResult<TResult> : IAgentEventWithResult<TResult>
    {
        public required string SessionId { get; set; }
    }
    public class AgentTextOutputEvent : EventBase
    {
        public string? Reasoning { get; set; }
        public string? Content { get; set; }
    }
    public class AgentToolCallEvent : EventBase
    {
        public required string CallId { get; set; }
        public string? ToolName { get; set; }
        public IList<JsonFieldChunk>? Arguments { get; set; }
    }
    public class AgentErrorEvent : EventBase
    {
        public required string Message { get; set; }
    }
    public class AgentUsageChangedEvent : EventBase
    {
        public int CachedTokens {  get;  set; }
        public int ReasoningTokens { get;  set; }
        public int TotalTokens { get;  set; }
        public int PromptTokens { get;  set; }
        public int CompletionTokens { get;  set; }
        public int RoundCount { get;  set; }
        public double CacheHitRate { get; set; }
    }
    public class FileAccessConfirmEvent : EventBaseWithResult<bool>
    {
        public required string File { get; set; }
    }
    public class BashConfirmEvent : EventBaseWithResult<bool>
    {
        public required string Command { get; set; }
    }
    /// <summary>Assistant 消息开始——fire before AI streaming starts</summary>
    public class AssistantMessageStartEvent : EventBase
    {
        public required string MessageId { get; set; }
    }
    /// <summary>推理阶段结束——from reasoning tokens to content tokens</summary>
    public class ReasoningEndEvent : EventBase
    {
    }
    /// <summary>内容生成完成——after streaming, with full text</summary>
    public class ContentEndEvent : EventBase
    {
        public required string FullContent { get; set; }
    }
    /// <summary>工具调用完成——after ToolUse executes</summary>
    public class ToolCallEndEvent : EventBase
    {
        public required string MessageId { get; set; }
        public required string ToolCallId { get; set; }
        public required string ToolName { get; set; }
        public string? Arguments { get; set; }
        public required string Result { get; set; }
        public bool Success { get; set; }
    }
    public class SessionTitleChangedEvent : EventBase
    {
        public required string Title { get; set; }
    }
}
