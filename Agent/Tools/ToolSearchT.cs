using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class ToolSearchT : IAgentTool
    {
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "tool_search",
                FullDescription = "Manage task list. "
                    + "Supported operations: add (add tasks), get (view task list), finish (complete current task). "
                    + "Use 'add' with 'tasks' parameter to set multiple tasks separated by '|'.",
                Parameters = [
                    new() { Name = "tool_name",  Type = AgentParametersType.STRING, Description = "Operation type: add, get, finish", Required = true },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = true,
                SupportedModes = WorkMode.All,
                ToolFunction = Execute
            };
        }

        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue value)
        {
            if (!value.Has("tool_name"))
            {
                return (new MessageContent("Parameter 'tool_name' is required"), ToolsResult.ParametersError);
            }
            string toolName = value.GetString("tool_name");
            session.AgentToolManager.FullLoadTool(toolName);
            return (new MessageContent($"success load tool {toolName}"), ToolsResult.Success);
        }

        public void Init(ISession session)
        {
            return;
        }
    }
}
