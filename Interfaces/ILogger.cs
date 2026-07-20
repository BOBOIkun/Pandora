using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface ILogger:IDisposable
    {
        void Log(LogLevel level,string text, string? fun = null);
    }
    public enum LogLevel
    {
        Error,
        Warning,
        Info,
        Debug,
        Trace
    }
    public struct LogMessage(LogLevel level, string text,string? fun=null)
    {
        public LogLevel Level = level;
        public string Text = text;
        public string? Function = fun;
        public readonly override string ToString()
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Function ?? "~"}] [{Level}]:{Text}";
        }
    }
}
