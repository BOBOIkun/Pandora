using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Models
{
    public class SessionData
    {
        public string? Title { get; set; }
        public required string SessionId { get; set; }
        public long UpdateTime { get; set; }
        public long CreateTime { get; set; }
    }

}
