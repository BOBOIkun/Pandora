using System;
using System.Collections.Generic;
using System.Text;
using OpenAI.Models.Chat;
using Pandora.Agent;
using Pandora.Event;
using static Pandora.Agent.AiService;

namespace Pandora.Interfaces
{
    public interface IAiService
    {
        void LoadDefaultModel();
        bool SwitchModel(string providerId, string modelName);
        void LoadDefaultAsrModel();
        bool SwitchAsrModel(string providerId, string modelName);

        CurrentModelInfo ChatModel { get; }
        CurrentModelInfo AsrModel { get; }

        ReasoningEffort CurrentReasoningEffort { get; set; }
        public Task<CompletionResult> CompletionAsync(ChatCompletionRequest request);
        public Task<CompletionResult> StreamCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
        Task<string> TranscribeAsync(byte[] audioBytes, string format = "webm");
    }
    public interface IToolCall
    {
        public string ToolName { get; set; }
        public string Parameters { get; set; }
        public string ToolCallId { get; set; }
        public ToolCall ToToolCall();
        public IList<JsonFieldChunk>? AddArguments(string arguments);
        public void EnableStreamOutput();
    }
}
