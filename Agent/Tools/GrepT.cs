using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class GrepT : IAgentTool
    {
        private const int MAX_HIT = 10;
        private Ripgrep _ripgrep = null!;
        private readonly static RipgrepOption _ripgrepOption = new(){ShortFileName=true};
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "grep",
                FullDescription = $"Use Ripgrep for text matching. By default, files larger than 10MB will be skipped. Each file will display up to {MAX_HIT} hits.Do not use this to list files!",
                Parameters = [
                    new() { Name = "pattern", Type = AgentParametersType.STRING, Description = "The content to be searched,supports regular expressions", Required = true },
                    new() { Name = "path", Type = AgentParametersType.STRING, Description = "Directory to search", Required = true },
                    new() { Name = "max_depth", Type = AgentParametersType.INT, Description = "Maximum depth of search.The default setting is unlimited", Required = false },
                    new() { Name = "ignore_case", Type = AgentParametersType.BOOL, Description = "Whether to ignore case, the default is not to ignore", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = true,
                SupportedModes = WorkMode.All,
                ToolFunction = Execute
            };
        }
        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param) 
        { 
            if(!param.Has("pattern") || !param.Has("path")) 
                return (new MessageContent("pattern and path required"), ToolsResult.ParametersError);
            string pattern = param.GetString("pattern")!;
            string path = param.GetString("path")!;
            if(!Directory.Exists(path))
                return (new MessageContent("path not found"), ToolsResult.ParametersError);
            _ripgrepOption.MaxDepth = param.Has("max_depth") ? param.GetInt("max_depth") : _ripgrepOption.MaxDepth;
            _ripgrepOption.IgnoreCase = param.Has("ignore_case") ? param.GetBool("ignore_case") : _ripgrepOption.IgnoreCase;
            Dictionary<string, List<RipgrepMatchItem>> k = [];
            foreach (var item in _ripgrep.Search(pattern,path,_ripgrepOption))
            {
                if (!k.ContainsKey(item.Path.Text!))
                {
                    k[item.Path.Text!] = [];
                }
                k[item.Path.Text!].Add(item);
            }
            StringBuilder stringBuilder = new StringBuilder();
            int count = 0;
            foreach (var item in k)
            {
                count = 0;
                stringBuilder.AppendLine($"{item.Key}({item.Value.Count} hits {Math.Min(item.Value.Count, MAX_HIT)} left):");
                foreach (var item2 in item.Value)
                {
                    if (count >= MAX_HIT)
                    {
                        break;
                    }
                    count++;
                    stringBuilder.Append($"{item2.LineNumber,5}\t{item2.Lines.Text}");
                }
            }
            return (new MessageContent(stringBuilder.ToString()), ToolsResult.Success);
        }
        public void Init(ISession session)
        {
            string rgPath = Path.Combine(session.AgentEnvironment.BinDirectory, "rg.exe");
            if (!File.Exists(rgPath))
            {
                throw new PandoraException("GlobT rg.exe not found");
            }
            _ripgrep = new Ripgrep(rgPath);
        }
    }
}
