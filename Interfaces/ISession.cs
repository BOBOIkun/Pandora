using Pandora.Agent;
using Pandora.Event;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface ISession
    {
        public bool IsSubAgent { get; }
        public long CreatedTime { get;}
        public long UpdatedTime { get;}
        public string SessionId { get;}
        public string Title { get; set; }
        public WorkMode WorkMode { get;}
        public IEventBus EventBus { get;}
        public IDataManager DataManager { get;}
        public ICore Core { get;}
        public IMessageManager MessageManager { get;}
        public IUsageManager UsageManager { get;}
        public IAgentToolManager AgentToolManager { get;}
        public ISafetyManager SafetyManager { get;}
        public IAgentEnvironment AgentEnvironment { get;}
        public IAiService AiService { get;}
        public FileState TextFileState { get; }
        public SessionChangeInfo ChangeInfo { get; }
        public Task CompleteChat(CompleteChatOptions options, CancellationToken cancellationToken);
    }
    [Flags]
    public enum WorkMode
    {
        None = 0,
        Chatting = 1,
        Working = 2,
        Coding = 4,
        All = Chatting | Working | Coding
    }
    public class CompleteChatOptions
    {
        public uint MaxToolsUse { get; set; } = 9999;
        public uint MaxToolError { get; set; } = 999;
    }
    public class SessionChangeInfo 
    {
        public string? TextFile { get; set; }
        public string? WorkingDirectory { get; set; }
        public override string ToString()
        {
            string str = $"<change>\n{TextFile}\n{WorkingDirectory}\n<change>";
            TextFile = null;
            WorkingDirectory = null;
            return str;
        }
    }
}
