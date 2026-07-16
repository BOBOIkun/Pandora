using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora
{
    public class PandoraException: Exception
    {
        public PandoraException(string message): base(message)
        {
        }
    }
}
