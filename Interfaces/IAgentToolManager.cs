using OpenAI.Models.Chat;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface IAgentToolManager
    {
        public Dictionary<string, AgentTool> Tools { get; }
        public int LoadTools();
        public string GetToolsListStr();
        public void FullLoadTool(string toolName);
        public IList<ToolDefinition> GetOpenAiToolDefinitions();
        public IList<ToolDefinition> GetOpenAiToolDefinitions(WorkMode workMode,bool fullLoad = false);
        public IList<ToolDefinition> GetAgentFullToolDefinitions(WorkMode workMode);
    }
}
