using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;

namespace Pandora.Agent.Tools
{
    public class AskUserQuestionT : IAgentTool
    {
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "ask_user_question",
                ShortDescription = "Ask the user a question when you need clarification or multiple options to choose from",
                FullDescription = "Ask the user a clarifying question. "
                    + "Use this when you are uncertain about requirements, need to choose between approaches, "
                    + "or want the user to confirm a decision. "
                    + "Provide a clear question and optional predefined options.",
                Parameters = [
                    new() { Name = "question", Type = AgentParametersType.STRING, Description = "The question to ask the user", Required = true },
                    new() { Name = "options", Type = AgentParametersType.STRING, Description = "JSON array of predefined options for the user to choose from, e.g. [\"Option A\",\"Option B\"]", Required = false },
                ],
                FullLoad = false,
                ParametersTypeCheck = true,
                ReadOnly = true,
                SupportedModes = WorkMode.All,
                ToolFunction = Ask
            };
        }

        public void Init(ISession session) { }

        private (MessageContent? ret, ToolsResult retSatus) Ask(ISession session, AgentToolParameterValue param)
        {
            string question = param.GetString("question");
            string[]? options = null;
            if (param.Has("options"))
            {
                try
                {
                    var json = System.Text.Json.JsonSerializer.Deserialize<string[]>(param.GetString("options"));
                    options = json;
                }
                catch { /* ignore parse error */ }
            }

            var requestId = Guid.NewGuid().ToString();
            var answer = session.EventBus.Publish<AskUserQuestionEvent, string>(new AskUserQuestionEvent
            {
                SessionId = session.SessionId,
                Question = question,
                Options = options,
                RequestId = requestId
            });

            return (new MessageContent(answer ?? ""), ToolsResult.Success);
        }
    }
}
