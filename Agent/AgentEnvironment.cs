using Pandora.Interfaces;

namespace Pandora.Agent
{
    public class AgentEnvironment : IAgentEnvironment
    {
        private readonly ISession _session;
        public string WorkingDirectory { get; private set; } = null!;
        public string SessionDataDirectory { get; private set; }= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data","sessions");
        public string BinDirectory { get; private set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
        public AgentEnvironment(ISession session)
        {
            _session = session;
            SetWorkingDirectory(Environment.CurrentDirectory);
        }
        public void SetWorkingDirectory(string workingDirectory)
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new PandoraException($"Working directory {workingDirectory} not found.");
            }
            WorkingDirectory = workingDirectory;
            _session.DataManager.SetWorkingDirectory(workingDirectory);
            _session.ChangeInfo.WorkingDirectory = "Now workingDirectory is " + workingDirectory;
        }
    }
}