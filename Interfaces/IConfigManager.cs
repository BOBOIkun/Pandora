namespace Pandora.Interfaces
{
    public interface IConfigManager
    {
        T? GetValue<T>(string name);
        T GetValue<T>(string name, T defaultValue);
        void SetValue<T>(string name, T value);
        bool Remove(string name);
    }
}
