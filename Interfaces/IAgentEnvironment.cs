using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface IAgentEnvironment
    {
        public string WorkingDirectory { get; }
        public string BinDirectory { get; }
        public void SetWorkingDirectory(string workingDirectory);
        public string SessionDataDirectory { get; }
    }
}