using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.JsonC;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using static Pandora.Agent.AiService;

namespace Pandora.Agent
{
    public class MessageManager : IMessageManager
    {

        private int _systemMessageIndex = -1;
        private readonly List<ChatMessage> _messages=[];
        private ISession _session;
        public MessageManager(ISession session,List<ChatMessage>? chatMessages=null)
        {
            _session = session;
            if (chatMessages!=null)
            {
                _messages.AddRange(chatMessages);
            }
            _systemMessageIndex =AddMessage(ChatMessage.FromSystem(GetAgentPrompt().ToString()),false);
        }
        private string GetAgentPrompt()
        {
            StringBuilder sb = new();
            string promptFileName= _session.WorkMode switch
            {
                WorkMode.Chatting => "chat.txt",
                WorkMode.Working => "work.txt",
                WorkMode.Coding => "code.txt",
                _ => "chat.txt"
            };
            sb.AppendLine(GetPromptFromFile(promptFileName));
            sb.AppendLine("<system-info>");
            sb.Append(Utils.GetSystemInfoStrB());
            sb.AppendLine("</system-info>");
            sb.AppendLine("<tool-list>");
            sb.Append(_session.AgentToolManager.GetToolsListStr());
            sb.AppendLine("</tool-list>");
            return sb.ToString();
        }
        public int AddMessage(ChatMessage message,bool appendData=true)
        {
            int index = _messages.Count;
            _messages.Add(message);
            if (appendData)
            {
                _ = _session.DataManager.AppendMessageAsync(message);
            }
            return index;
        }
        public IList<ChatMessage> GetMessages()
        {
            return _messages;
        }
        private static string GetPromptFromFile(string promptFileName)
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config", "prompt");
            string promptFilePath = Path.Combine(filePath, promptFileName);
            if (!File.Exists(promptFilePath))
            {
                throw new PandoraException ($"Prompt file {promptFileName} not found.");
            }
            return File.ReadAllText(promptFilePath);
        }
        public void AddAssistantMessageByCompletionResult(CompletionResult rusult, bool withReasoning = true, bool withToolCall = true)
        {
            ChatMessage chatMessage = new("assistant", rusult.Content);
            if (withToolCall && rusult.ToolsCalls.Count > 0)
            {
                chatMessage.ToolCalls = [.. rusult.ToToolCalls()];
            }
            if (!string.IsNullOrEmpty(rusult.Reasoning))
            {
                if (withReasoning)
                {
                    chatMessage.ReasoningContent = rusult.Reasoning;
                }
                else
                {
                    chatMessage.Extension.ReasoningExtension = rusult.Reasoning;
                }
                
            }
            AddMessage(chatMessage);
        }

        public void AddToolCall(string toolCallId, MessageContent? ret)
        {
            AddMessage(ChatMessage.FromTool(ret, toolCallId));
        }
    }
}
