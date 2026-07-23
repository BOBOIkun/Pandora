using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class FileEditT : IAgentTool
    {
        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "edit_file",
                FullDescription = "Use the method of replacing text to make local edits to the text, ensuring that the file content has been read before editing"
                    + "The edit will FAIL if `old_string` is not unique in the file. Either provide a larger string with more surrounding context to make it unique or use `replace_all` to change every instance of `old_string`"
                    + "Use `replace_all` for replacing and renaming strings across the file. This parameter is useful if you want to rename a variable for instance",
                Parameters = [
                    new() { Name = "file_path",      Type = AgentParametersType.STRING, Description = "Absolute path to the file", Required = true },
                    new() { Name = "old_string",      Type = AgentParametersType.STRING, Description = "Old string", Required = true },
                    new() { Name = "new_string", Type = AgentParametersType.STRING,    Description = "New string", Required = true },
                    new() { Name = "replace_all",   Type = AgentParametersType.BOOL,    Description = "Replace all matches", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = false,
                SupportedModes = WorkMode.Working|WorkMode.Coding,
                ToolFunction = Execute
            };
        }
        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param)
        {
            string? path = param.GetString("file_path");
            string? oldString = param.GetString("old_string");
            string? newString = param.GetString("new_string");
            bool replaceAll = false;
            if(newString==oldString)
                return (new MessageContent("old_string and new_string cannot be the same"), ToolsResult.ParametersError);
            if(session.TextFileState.GetStatus(path,false)!=FileStateStatus.NotChanged)
                return (new MessageContent("file never read or may be changed"), ToolsResult.ParametersError);
            if (param.Has("replace_all"))
                replaceAll = param.GetBool("replace_all");
            if (path == null || oldString == null || newString == null)
                return (new MessageContent("file_path and old_string and new_string required"), ToolsResult.ParametersError);
            if (!File.Exists(path))
                return (new MessageContent("File not found"), ToolsResult.ParametersError);
            if (!session.SafetyManager.GetFileAccessInfo(path).write)
                return (new MessageContent("Access denied"), ToolsResult.ParametersError);
            if (!SingeleMatche(File.ReadAllText(path), oldString) && !replaceAll)
                return (new MessageContent("old_string is not unique in the file"), ToolsResult.ParametersError);
            long oldTime = File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
            string text = File.ReadAllText(path);
            session.TextFileState.Locked = true;
            File.WriteAllText(path, text.Replace(oldString, newString));
            session.TextFileState.SetLock(path, oldTime);
            //session.TextFileState.FileChangeCheck(path);
            return (new MessageContent($"Successfully edited {path}"), ToolsResult.Success);
        }
        private static bool SingeleMatche(string text, string str)
        { 
            int index = text.IndexOf(str);
            if(index == -1) return true;
            return index == text.LastIndexOf(str);
        }
        public void Init(ISession session)
        {
            return;
        }
    }
}
