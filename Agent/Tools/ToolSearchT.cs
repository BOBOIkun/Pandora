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
                FullDescription = "将工具的完整定义加载入系统信息,仅针对<tool-list>的工具",
                Parameters = [
                    new() { Name = "tool_name",  Type = AgentParametersType.STRING, Description = "需要加载的工具名", Required = true },
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
