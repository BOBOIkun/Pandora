using Pandora.Interfaces;

namespace Pandora.Agent
{
    public class AgentEnvironment : IAgentEnvironment
    {
        public string WorkingDirectory { get; private set; } = AppDomain.CurrentDomain.BaseDirectory;
        public string BinDirectory { get; private set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
        public void SetWorkingDirectory(string workingDirectory)
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new PandoraException($"Working directory {workingDirectory} not found.");
            }
            WorkingDirectory = workingDirectory;
        }
    }
}