
using Pandora.Agent.Safety;
using Pandora.Interfaces;
using Pandora.Models;

namespace Pandora.Agent
{
    public class SafetyManager : ISafetyManager
    {
        private readonly ISession _session;
        public SafetyManager(ISession session)
        {
            _session = session;
        }
        public SafetyMode SafetyMode { get; set; } = SafetyMode.Partial;

        public bool ConfirmBashCommand(string command)
        {
            // Full → 全部放行
            if (SafetyMode == SafetyMode.Full) return true;
            // Partial → 安全命令放行，其余弹确认
            if (SafetyMode == SafetyMode.Partial && BashCommandIsAllowed(command)) return true;
            // Restricted / Partial(非安全) → 弹确认
            if (_session.EventBus.HasHandler<BashConfirmEvent>() == false)
            {
                //使用控制台确认
                Console.WriteLine($"确认执行命令：{command}?(y/n)");
                return Console.ReadLine() == "y";
            }
            return _session.EventBus.Publish<BashConfirmEvent, bool>(new BashConfirmEvent { Command = command, SessionId = _session.SessionId });
        }

        public FileAccessInfo GetFileAccessInfo(string file)
        {
            string workPath = _session.AgentEnvironment.WorkingDirectory;
            string baseDirectory = AppContext.BaseDirectory;
            FileAccessInfo fileAccessInfo = new FileAccessInfo();
            if (SafetyMode == SafetyMode.Full)
            {
                fileAccessInfo.write = true;
                fileAccessInfo.read = true;
                return fileAccessInfo;
            }
            fileAccessInfo.read = Utils.IsSubdirectoryOf(file, workPath) && !Utils.IsSubdirectoryOf(file, baseDirectory);
            fileAccessInfo.write = Utils.IsSubdirectoryOf(file, workPath) && !Utils.IsSubdirectoryOf(file, baseDirectory);
            if (!fileAccessInfo.write || !fileAccessInfo.read)
            {
                // 优先使用回调，回退到 Console
                if (_session.EventBus.HasHandler<FileAccessConfirmEvent>())
                {
                    // 同步等待异步回调（Safety 本身是同步方法）
                    bool approved = _session.EventBus.Publish<FileAccessConfirmEvent, bool>(new FileAccessConfirmEvent { SessionId = _session.SessionId, File = file });
                    if (approved)
                    {
                        fileAccessInfo.write = true;
                        fileAccessInfo.read = true;
                    }
                }
                else
                {
                    Console.WriteLine("");
                    Console.Write($"Agent wants to access {file} (y/n):");
                    string? input = Console.ReadLine();
                    if (input != null && input.Equals("y", StringComparison.CurrentCultureIgnoreCase))
                    {
                        fileAccessInfo.write = true;
                        fileAccessInfo.read = true;
                    }
                }
            }
            return fileAccessInfo;
        }
        private static bool BashCommandIsAllowed(string command)
        {
            var info = BashCommand.GetBashCommandInfo(command);
            foreach (var cmd in info.commands)
            {
                if (!BashCommand.allowCommands.Contains(cmd))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
