using Pandora.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface IAgentTool
    {
        public void Init(ISession session);
        public AgentTool GetToolDefinition(ISession session);

    }
}
