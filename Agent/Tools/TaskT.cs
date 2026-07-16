using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public class TaskT : IAgentTool
    {
        private readonly ITaskManager _taskManager;
        private static readonly string[] _supportedTypes = ["add", "get", "finish"];

        public TaskT()
        {
            _taskManager = new TaskManager();
        }

        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "task",
                FullDescription = "Manage task list. "
                    + "Supported operations: add (add tasks), get (view task list), finish (complete current task). "
                    + "Use 'add' with 'tasks' parameter to set multiple tasks separated by '|'.",
                Parameters = [
                    new() { Name = "type",  Type = AgentParametersType.STRING, Description = "Operation type: add, get, finish", Required = true },
                    new() { Name = "tasks", Type = AgentParametersType.STRING, Description = "Tasks separated by '|' (required for add)", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = false,
                SupportedModes = WorkMode.Working|WorkMode.Coding,
                ToolFunction = Execute
            };
        }

        public void Init(ISession session)
        {
            return;
        }

        private (MessageContent? ret, ToolsResult retSatus) Execute(ISession session, AgentToolParameterValue param)
        {
            string type = param.GetString("type").ToLower();

            if (!_supportedTypes.Contains(type))
            {
                return (new MessageContent($"Invalid type: '{type}'. Must be one of: {string.Join(", ", _supportedTypes)}"),
                        ToolsResult.ParametersError);
            }

            switch (type)
            {
                case "add":
                    if (!param.Has("tasks"))
                    {
                        return (new MessageContent("Parameter 'tasks' is required when type is 'add'"),
                                ToolsResult.ParametersError);
                    }
                    string tasksStr = param.GetString("tasks");
                    if (string.IsNullOrWhiteSpace(tasksStr))
                    {
                        return (new MessageContent("Tasks cannot be empty"), ToolsResult.ParametersError);
                    }
                    var tasks = tasksStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (tasks.Length == 0)
                    {
                        return (new MessageContent("No valid tasks provided"), ToolsResult.ParametersError);
                    }
                    string addResult = _taskManager.UpdateTaskList(tasks);
                    return (new MessageContent(addResult), ToolsResult.Success);

                case "get":
                    return (new MessageContent(_taskManager.GetFormattedTaskList()), ToolsResult.Success);

                case "finish":
                    string finishResult = _taskManager.FinishTask();
                    return (new MessageContent(finishResult), ToolsResult.Success);

                default:
                    return (new MessageContent("Unknown error"), ToolsResult.UnKnownError);
            }
        }
    }
}