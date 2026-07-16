using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class ReadFileT : IAgentTool
       {
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "read_file",
                FullDescription = "Read a file from the local filesystem. "
                    + "For text files, specify startLine/endLine to read a range (1-based, inclusive, max 100 lines). "
                    + "Omit startLine/endLine to read the entire file. "
                    + "Images are automatically detected by extension and loaded into chat context.",
                Parameters = [
                    new() { Name = "path",      Type = AgentParametersType.STRING, Description = "Absolute path to the file", Required = true },
                    new() { Name = "startLine", Type = AgentParametersType.INT,    Description = "Start line number (1-based, optional)", Required = false },
                    new() { Name = "endLine",   Type = AgentParametersType.INT,    Description = "End line number (1-based, optional)", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = true,
                SupportedModes = WorkMode.All,
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
            if (!File.Exists(path))
                return (new MessageContent($"File not found: {path}"), ToolsResult.ParametersError);

            var ext = Path.GetExtension(path);

            if (!session.SafetyManager.GetFileAccessInfo(path).read)
                return (new MessageContent($"Access denied: {path}"), ToolsResult.ParametersError);
            if (ImageExtensions.Contains(ext))
            {
                return ReadImage(session, path);
            }
            bool hasRange = param.Has("startLine") || param.Has("endLine");
            if (hasRange)
            {
                int startLine = Math.Max(1, param.Has("startLine") ? param.GetInt("startLine") : 1);
                int endLine = param.Has("endLine") ? param.GetInt("endLine") : int.MaxValue;
                if (endLine - startLine > 100)
                    return (new MessageContent($"Line range too large ({endLine - startLine + 1}), max 100 lines"), ToolsResult.ParametersError);
                return ReadTextRange(path, startLine, endLine);
            }

            return ReadFullText(path);
        }

        private static (MessageContent?, ToolsResult) ReadImage(ISession session, string path)
        {
            try
            {
                if (new FileInfo(path).Length > 5 * 1024 * 1024)
                    return (new MessageContent($"File size too large ({new FileInfo(path).Length / 1024 / 1024}MB), max 5MB"), ToolsResult.ParametersError);
                List<ContentPart> parts = [];
                parts.Add(new TextContentPart($"Read file: {path}"));
                parts.Add(ImageContentPart.FromFile(path));
                return (new MessageContent(parts), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Read image error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }

        private static (MessageContent?, ToolsResult) ReadTextRange(string path, int startLine, int endLine)
        {
            try
            {
                var resultLines = new List<string>();
                int currentLine = 0;

                using var reader = new StreamReader(path);
                while (reader.ReadLine() is { } line)
                {
                    currentLine++;
                    if (currentLine >= startLine && currentLine <= endLine)
                        resultLines.Add(line);
                    if (currentLine > endLine) break;
                }

                if (resultLines.Count == 0)
                    return (new MessageContent($"File has fewer than {endLine} lines (total: {currentLine})"), ToolsResult.ParametersError);
                var ret= string.Join("\n", resultLines);
                if (ret.Length > 30000)
                    return (new MessageContent($"Text content too large ({ret.Length} chars), max 30000 chars"), ToolsResult.ParametersError);
                return (new MessageContent(ret), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Read error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }

        private static (MessageContent?, ToolsResult) ReadFullText(string path)
        {
            try
            {
                string content = File.ReadAllText(path);
                return (new MessageContent(content), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Read error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif", ".svg"
        };
    }
}