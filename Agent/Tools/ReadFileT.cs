using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class ReadFileT : IAgentTool
    {
        public const int MAX_IMAGE_FILE_SIZE = 3 * 1024 * 1024;
        public const int MAX_TEXT_LENGTH = 30000;
        public FileState fileState = null!;

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
                    new() { Name = "endLine",   Type = AgentParametersType.INT,    Description = "End line number (1-based, optional). Omit endLine to read to the end of the file.", Required = false },
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
            fileState = session.TextFileState;
        }

        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param)
        {
            if (fileState == null)
                return (new MessageContent("FileState not initialized"), ToolsResult.UnKnownError);

            string? path = param.GetString("path");
            if (string.IsNullOrEmpty(path))
                return (new MessageContent("Invalid path"), ToolsResult.ParametersError);

            if (!File.Exists(path))
                return (new MessageContent($"File not found: {path}"), ToolsResult.ParametersError);

            if (!session.SafetyManager.GetFileAccessInfo(path).read)
                return (new MessageContent($"Access denied: {path}"), ToolsResult.ParametersError);

            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext))
                return ReadImage(session, path);

            bool hasStart = param.Has("startLine");
            bool hasEnd = param.Has("endLine");

            int actualStart, actualEnd;
            if (!hasStart && !hasEnd)
            {
                actualStart = -1;
                actualEnd = -1;
            }
            else
            {
                actualStart = hasStart ? Math.Max(1, param.GetInt("startLine")) : 1;
                actualEnd = hasEnd ? param.GetInt("endLine") : int.MaxValue;
                if (hasEnd && actualEnd - actualStart + 1 > 100)
                    return (new MessageContent($"Line range too large ({actualEnd - actualStart + 1} lines), max 100 lines"), ToolsResult.ParametersError);
            }

            FileStateStatus status = fileState.GetStatus(path, actualStart, actualEnd);
            if (status == FileStateStatus.NotChanged)
                return (new MessageContent("File has already been read and has not changed."), ToolsResult.Success);

            (MessageContent? ret, ToolsResult result) readResult;
            if (actualStart == -1)
                readResult = ReadFullText(path);
            else
                readResult = ReadTextRange(path, actualStart, actualEnd);

            if (readResult.result == ToolsResult.Success)
                fileState.Update(path, actualStart, actualEnd);

            return readResult;
        }

        private static (MessageContent?, ToolsResult) ReadImage(ISession session, string path)
        {
            try
            {
                long fileSize = new FileInfo(path).Length;
                if (fileSize > MAX_IMAGE_FILE_SIZE)
                    return (new MessageContent($"File size too large ({fileSize / 1024 / 1024}MB), max {MAX_IMAGE_FILE_SIZE / 1024 / 1024}MB"), ToolsResult.ParametersError);

                List<ContentPart> parts = new()
                {
                    new TextContentPart($"Read file: {path}"),
                    ImageContentPart.FromFile(path)
                };
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
                var sb = new StringBuilder();
                int currentLine = 0;

                using var reader = new StreamReader(path);
                while (reader.ReadLine() is { } line)
                {
                    currentLine++;
                    if (currentLine >= startLine && currentLine <= endLine)
                        sb.AppendLine($"{currentLine,6}\t{line}");
                    if (currentLine > endLine)
                        break;
                }

                string result = sb.ToString();
                if (result.Length > MAX_TEXT_LENGTH)
                    return (new MessageContent($"Text content too large ({result.Length} chars), max {MAX_TEXT_LENGTH} chars"), ToolsResult.ParametersError);

                return (new MessageContent(result), ToolsResult.Success);
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
                string content = ReadAllTextWithLineNumbers(path);
                if (content.Length > MAX_TEXT_LENGTH)
                    return (new MessageContent($"Text content too large ({content.Length} chars), max {MAX_TEXT_LENGTH} chars"), ToolsResult.ParametersError);

                return (new MessageContent(content), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Read error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }

        private static string ReadAllTextWithLineNumbers(string path, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;
            var sb = new StringBuilder();
            using var reader = new StreamReader(path, encoding);
            int lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                sb.AppendLine($"{lineNumber,6}\t{line}");
            }
            return sb.ToString();
        }

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
        };
    }
}