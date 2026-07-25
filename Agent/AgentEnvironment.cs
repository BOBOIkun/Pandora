using Pandora.Interfaces;

namespace Pandora.Agent
{
    public class AgentEnvironment : IAgentEnvironment
    {
        public static string GetSessionDataDirectory() 
        { 
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data","sessions");
        }
        public static string GetBinDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
        }
        private readonly ISession _session;
        public string WorkingDirectory { get; private set; } = null!;
        public AgentEnvironment(ISession session)
        {
            _session = session;
            SetWorkingDirectory(Environment.CurrentDirectory,false);
        }
        public void SetWorkingDirectory(string workingDirectory,bool flush = false)
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new PandoraException($"Working directory {workingDirectory} not found.");
            }
            WorkingDirectory = workingDirectory;
            if(flush) _session.DataManager.SetWorkingDirectory(workingDirectory);
            _session.ChangeInfo.WorkingDirectory = "Now workingDirectory is " + workingDirectory;
        }
    }
}