using OpenAI.Models.Chat;
using OpenAI.Models.Shared;
using Pandora.Agent;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Pandora.Interfaces
{
    public interface IDataManager
    {
        public Task AppendMessageAsync(ChatMessage msg);
        public void SetWorkingDirectory(string workingDirectory);
        public void SetToolFullLoad(List<string> toolNames);
        public void SetUsage(Usage? usage);
        public void SetModel(string? providerId, string? modelName);
        public void DeleteSession();
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
        [JsonPropertyName("tool_fullLoad")]
        public List<string>? ToolFullLoad { get; set; }
        [JsonPropertyName("session_workMode")]
        public required WorkMode WorkMode { get; set; }
        [JsonPropertyName("aiService_providerId")]
        public string? AiServiceProviderId { get; set; }
        [JsonPropertyName("aiService_modelName")]
        public string? AiServiceModelName { get; set; }
        [JsonIgnore]
        public long LastUpdateTime { get; set; }
        public long GetLastUpdateTime()
        {
            for (int i = MessageFiles.Count - 1; i >= 0; i--)
            {
                string path = DataManagerStatic.GetMessageFilePath(SessionId, MessageFiles[i]);
                if (File.Exists(path))
                {
                    return File.GetLastWriteTime(path).ToFileTimeUtc();
                }
            }
            return -1;
        }
    }
}
