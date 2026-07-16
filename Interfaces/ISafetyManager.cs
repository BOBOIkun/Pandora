using Pandora.Models;

namespace Pandora.Interfaces
{
    public interface ISafetyManager
    {
        FileAccessInfo GetFileAccessInfo(string file);
        bool ConfirmBashCommand(string command);
        SafetyMode SafetyMode { get; set; }
    }
    public enum SafetyMode
    {
        Partial,
        Full,
    }
}
