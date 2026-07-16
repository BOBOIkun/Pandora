using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class WriteFileT : IAgentTool
    {
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "write_file",
                FullDescription = "Write text content to a file. Overwrites if exists, creates if not. "
                    + "Use append=true to append content instead of overwriting.",
                Parameters = [
                    new() { Name = "path",      Type = AgentParametersType.STRING, Description = "Absolute path to the target file", Required = true },
                    new() { Name = "content",   Type = AgentParametersType.STRING, Description = "Text content to write", Required = true },
                    new() { Name = "append",    Type = AgentParametersType.BOOL,   Description = "Append to file instead of overwrite", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = false,
                SupportedModes = WorkMode.Working | WorkMode.Coding,
                ToolFunction = Execute
            };
        }

        public void Init(ISession session)
        {
            return;
        }

        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param)
        {
            string path = param.GetString("path");
            string content = param.GetString("content");
            bool append = param.Has("append") && param.GetBool("append");

            try
            {
                if (!session.SafetyManager.GetFileAccessInfo(path).write)
                    return (new MessageContent($"Access denied: {path}"), ToolsResult.ParametersError);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (append)
                {
                    File.AppendAllText(path, content, Encoding.UTF8);
                }
                else
                {
                    File.WriteAllText(path, content, Encoding.UTF8);
                }

                return (new MessageContent($"Successfully {(append ? "appended to" : "wrote")} {path}"), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Write error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }
    }
}