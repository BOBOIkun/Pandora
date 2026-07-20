using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System.Reflection;
using System.Text;

namespace Pandora.Agent
{
    public class AgentToolManager : IAgentToolManager
    {
        public Dictionary<string, AgentTool> Tools => _tools;
        private readonly ISession _session;
        private readonly Dictionary<string, AgentTool> _tools=[];
        private IList<ToolDefinition>? _toolsCache;
        public AgentToolManager(ISession session)
        {
            _session = session;
        }
        public void FullLoadTool(string toolName)
        {
            _toolsCache = null;
            if (_tools.TryGetValue(toolName, out AgentTool? tool) && tool != null)
            { 
                tool.FullLoad=true;
            }else
            {
                throw new PandoraException($"tool {toolName} not found");
            }
        }

        public IList<ToolDefinition> GetOpenAiToolDefinitions()
        {
            return [.. _tools.Values.Select(tool => ToOpenAiTool(tool))];
        }
        public IList<ToolDefinition> GetAgentFullToolDefinitions(WorkMode workMode)
        {
            var toolDefinitions =GetOpenAiToolDefinitions(workMode, true);
            return toolDefinitions;
        }
        public IList<ToolDefinition> GetOpenAiToolDefinitions(WorkMode workMode,bool fullLoad = false)
        {
            if (_toolsCache != null)
            {
                return [.._toolsCache];
            }
            var result = new List<ToolDefinition>();
            foreach (var tool in _tools.Values)
            {
                if (tool.SupportedModes.HasFlag(workMode) && (!fullLoad || tool.FullLoad))
                {
                    result.Add(ToOpenAiTool(tool));
                }
            }
            _toolsCache = result;
            return [..result];
        }

        public string GetToolsListStr()
        {
            StringBuilder sb = new();
            foreach (var tool in _tools)
            {
                if (tool.Value.FullLoad)
                {
                    continue;
                }
                string? t = tool.Value.ShortDescription;
                t ??= tool.Value.FullDescription;
                sb.AppendLine($"{tool.Value.ToolName}: {t}");
            }
            return sb.ToString();
        }

        public int LoadTools()
        {
            _toolsCache = null;
            _tools.Clear();
            var l= Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IAgentTool).IsAssignableFrom(t))
            .ToList();
            foreach (var t in l)
            {
                var tool = (IAgentTool)Activator.CreateInstance(t)!;
                tool.Init(_session);
                var toolDefinition = tool.GetToolDefinition(_session);
                _tools.Add(toolDefinition.ToolName, toolDefinition);
            }
            return l.Count;
        }
        private static ToolDefinition ToOpenAiTool(AgentTool tool)
        {
            var builder = new FunctionDefinitionBuilder(tool.ToolName, tool.FullDescription);
            foreach (var parameter in tool.Parameters)
            {
                var property = parameter.Type switch
                {
                    AgentParametersType.INT => PropertyDefinition.DefineInteger(parameter.Description),
                    AgentParametersType.FLOAT => PropertyDefinition.DefineNumber(parameter.Description),
                    AgentParametersType.BOOL => PropertyDefinition.DefineBoolean(parameter.Description),
                    _ => PropertyDefinition.DefineString(parameter.Description),
                };
                builder.AddParameter(parameter.Name, property, parameter.Required);
            }
            return ToolDefinition.DefineFunction(builder.Build());
        }
    }
}
