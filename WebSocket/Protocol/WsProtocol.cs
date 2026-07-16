using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pandora.WebSocket.Protocol
{
    /// <summary>客户端发来的消息基类</summary>
    public class ClientMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("images")]
        public string[]? Images { get; set; }

        [JsonPropertyName("audio")]
        public string? Audio { get; set; }

        [JsonPropertyName("audioFormat")]
        public string? AudioFormat { get; set; }

        [JsonPropertyName("reasoning_effort")]
        public string? ReasoningEffort { get; set; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("approved")]
        public bool? Approved { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("workMode")]
        public string? WorkMode { get; set; }

        [JsonPropertyName("providerId")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("modelName")]
        public string? ModelName { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("config")]
        public JsonElement? Config { get; set; }

        [JsonPropertyName("chat")]
        public JsonElement? Chat { get; set; }

        [JsonPropertyName("asr")]
        public JsonElement? Asr { get; set; }
    }

    // ============ 工具函数 ============

    public static class WsProtocol
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static ClientMessage? Deserialize(string json)
        {
            return JsonSerializer.Deserialize<ClientMessage>(json, JsonOpts);
        }

        public static string Serialize(object obj)
        {
            return JsonSerializer.Serialize(obj, obj.GetType(), JsonOpts);
        }

        // ============ Session ============

        public static object SessionCreated(string sessionId, string? prompt = null, string? workMode = null) => new
        {
            type = "session_created",
            sessionId,
            prompt,
            workMode
        };

        public static object SessionTitleChanged(string sessionId, string title) => new
        {
            type = "session_title_changed",
            sessionId,
            title
        };

        public static object SessionDeleted(string sessionId) => new
        {
            type = "session_deleted",
            sessionId
        };

        public static object SessionList(SessionSummary[] sessions) => new
        {
            type = "session_list",
            sessions
        };

        // ============ Messages / Streaming ============

        public static object AssistantMessageStart(string sessionId, string messageId) => new
        {
            type = "assistant_message_start",
            sessionId,
            messageId
        };

        public static object StreamToken(string sessionId, string contentType, string token) => new
        {
            type = "stream_token",
            sessionId,
            contentType,
            token
        };

        public static object ReasoningEnd(string sessionId) => new
        {
            type = "reasoning_end",
            sessionId
        };

        public static object ContentEnd(string sessionId, string fullContent) => new
        {
            type = "content_end",
            sessionId,
            fullContent
        };

        // ============ Tool Call ============

        public static object ToolCall(string sessionId, string messageId, string toolCallId,
            string toolName, string status, string? arguments, string? result, bool success) => new
        {
            type = "tool_call",
            sessionId,
            messageId,
            toolCallId,
            toolName,
            status,
            arguments,
            result,
            success
        };

        // ============ File / Bash Access ============

        public static object FileAccessRequest(string sessionId, string requestId, string filePath) => new
        {
            type = "file_access_request",
            sessionId,
            requestId,
            filePath
        };

        public static object BashAccessRequest(string sessionId, string requestId, string command) => new
        {
            type = "bash_access_request",
            sessionId,
            requestId,
            command
        };

        // ============ Safety ============

        public static object SafetyModeChanged(string sessionId, string mode) => new
        {
            type = "safety_mode_changed",
            sessionId,
            mode
        };

        // ============ Usage ============

        public static object UsageUpdate(string sessionId, int promptTokens, int completionTokens,
            int totalTokens, int cachedTokens, int reasoningTokens, int roundCount, double cacheHitRate) => new
        {
            type = "usage_update",
            sessionId,
            promptTokens,
            completionTokens,
            totalTokens,
            cachedTokens,
            reasoningTokens,
            roundCount,
            cacheHitRate
        };

        // ============ History ============

        public static object History(string sessionId, HistoryMessage[] messages) => new
        {
            type = "history",
            sessionId,
            messages
        };

        public static object AllHistory(AllHistorySession[] sessions) => new
        {
            type = "all_history",
            sessions
        };

        // ============ Task List ============

        public static object TaskList(string sessionId, TaskItem[] tasks) => new
        {
            type = "task_list",
            sessionId,
            tasks
        };

        // ============ Providers ============

        public static object ProviderList(ProviderItem[] providers, object? defaultChatModel, object? defaultAsrModel) => new
        {
            type = "provider_list",
            providers,
            defaultChatModel,
            defaultAsrModel
        };

        public static object ProviderSaved(string id, bool success, string? error = null) => new
        {
            type = "provider_saved",
            id,
            success,
            error
        };

        public static object ProviderDeleted(string id, bool success) => new
        {
            type = "provider_deleted",
            id,
            success
        };

        public static object DefaultModelsUpdated(object? chat, object? asr) => new
        {
            type = "default_models_updated",
            chat,
            asr
        };

        // ============ Model Switch ============

        public static object SessionModelChanged(string sessionId, string providerId,
            string modelDisplayName, string modelApiName, string baseUrl) => new
        {
            type = "session_model_changed",
            sessionId,
            providerId,
            modelDisplayName,
            modelApiName,
            baseUrl
        };

        // ============ Transcription ============

        public static object TranscriptionResult(string sessionId, string text) => new
        {
            type = "transcription_result",
            sessionId,
            text
        };

        // ============ Error ============

        public static object Error(string? sessionId, string message) => new
        {
            type = "error",
            sessionId,
            message
        };
    }

    // ============ 辅助类型 ============

    public class SessionSummary
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("lastActive")]
        public string? LastActive { get; set; }

        [JsonPropertyName("messageCount")]
        public int MessageCount { get; set; }
    }

    public class HistoryMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("images")]
        public string[]? Images { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }

        [JsonPropertyName("toolCalls")]
        public HistoryToolCall[]? ToolCalls { get; set; }
    }

    public class HistoryToolCall
    {
        [JsonPropertyName("toolCallId")]
        public string ToolCallId { get; set; } = "";

        [JsonPropertyName("toolName")]
        public string ToolName { get; set; } = "";

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }

    public class AllHistorySession
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("messages")]
        public HistoryMessage[] Messages { get; set; } = [];
    }

    public class TaskItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = ""; // pending / in_progress / completed
    }

    public class ProviderItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "";

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("useProxy")]
        public bool UseProxy { get; set; } = false;

        [JsonPropertyName("models")]
        public ProviderModelItem[] Models { get; set; } = [];
    }

    public class ProviderModelItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("contextSize")]
        public int ContextSize { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "chat";
    }
}
