using OpenAI.Models.Chat;
using OpenAI.Models.Shared;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Pandora.Interfaces
{
    public interface IDataManager
    {
        public Task AppendMessageAsync(ChatMessage msg);
        public (string id,string title) GetSessionBasicInfo(string path);
        public SessionInfo GetSessionInfo(string id);
        public void SetWorkingDirectory(string workingDirectory);
        public void Flush();
    }
    public class SessionInfo
    {
        [JsonPropertyName("session_sessionId")]
        public required string SessionId { get; set; }
        [JsonPropertyName("session_title")]
        public string? Title { get; set; }
        [JsonPropertyName("usage_usage")]
        public Usage? Usage { get; set; }
        [JsonPropertyName("message_messageFiles")]
        public required List<string> MessageFiles { get; set; }
        [JsonPropertyName("env_workingDirectory")]
        public required string WorkingDirectory { get; set; }
        [JsonPropertyName("tool_full_load")]
        public List<string>? ToolFullLoad { get; set; }
    }
}
