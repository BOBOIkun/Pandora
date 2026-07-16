using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent.Tools
{
    public enum TaskStatus
    {
        NotStart,
        Running,
        Completed,
    }

    public class TaskItem
    {
        public string TaskContent { get; set; } = string.Empty;
        public TaskStatus TaskStatus { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public interface ITaskManager
    {
        string UpdateTaskList(IList<string> tasks);
        string FinishTask();
        string GetFormattedTaskList();
        IReadOnlyList<TaskItem> GetTaskList();
        int CurrentTaskIndex { get; }
        bool HasTasks { get; }
    }

    public class TaskManager : ITaskManager
    {
        private readonly List<TaskItem> _tasks = [];
        private int _index;

        public int CurrentTaskIndex => _index;

        public bool HasTasks => _tasks.Count > 0;

        public string UpdateTaskList(IList<string> tasks)
        {
            _tasks.Clear();
            foreach (var item in tasks)
            {
                _tasks.Add(new TaskItem { TaskContent = item, TaskStatus = TaskStatus.NotStart });
            }
            _index = 0;
            if (_tasks.Count > 0)
            {
                _tasks[0].TaskStatus = TaskStatus.Running;
                return GetFormattedTaskList();
            }
            return "No task added";
        }

        public string FinishTask()
        {
            if (_tasks.Count == 0)
            {
                return "There are no tasks in the task list";
            }
            if (_index >= _tasks.Count)
            {
                return "All tasks completed";
            }
            _tasks[_index].TaskStatus = TaskStatus.Completed;
            _tasks[_index].CompletedAt = DateTime.Now;
            string completedTask = _tasks[_index].TaskContent;
            _index++;
            if (_index < _tasks.Count)
            {
                _tasks[_index].TaskStatus = TaskStatus.Running;
                return $"Finished: {completedTask}\nNext task: {_tasks[_index].TaskContent}";
            }
            return $"Finished: {completedTask}\nAll tasks completed!";
        }

        public string GetFormattedTaskList()
        {
            if (_tasks.Count == 0)
                return "Task list is empty";

            var sb = new StringBuilder();
            sb.AppendLine("=== Task List ===");
            for (int i = 0; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                char statusChar = task.TaskStatus switch
                {
                    TaskStatus.Running => '>',
                    TaskStatus.Completed => '[',
                    _ => ' ',
                };
                string statusText = task.TaskStatus switch
                {
                    TaskStatus.Running => "running",
                    TaskStatus.Completed => "done",
                    _ => "pending",
                };
                sb.AppendLine($"{statusChar}{i + 1}. [{statusText}] {task.TaskContent}");
            }
            sb.AppendLine("=== End Task List ===");
            return sb.ToString();
        }

        public IReadOnlyList<TaskItem> GetTaskList()
        {
            return _tasks.AsReadOnly();
        }
    }
}