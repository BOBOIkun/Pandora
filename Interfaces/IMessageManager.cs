using OpenAI.Models.Chat;
using System;
using System.Collections.Generic;
using System.Text;
using static Pandora.Agent.AiService;

namespace Pandora.Interfaces
{
    public interface IMessageManager
    {
        public IList<ChatMessage> GetMessages();
        //public IList<ChatMessage> GetMessages(int limit);
        public int AddMessage(ChatMessage message);
        public void AddAssistantMessageByCompletionResult(CompletionResult rusult, bool withReasoning = true, bool withToolCall = true);
        public void AddToolCall(string callId, MessageContent? ret);
    }
}
