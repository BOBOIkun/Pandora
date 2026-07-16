using OpenAI.Models.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface IUsageManager
    {
        public int CachedTokens { get; }
        public int ReasoningTokens { get; }
        public int TotalTokens { get; }
        public int PromptTokens { get; }
        public int CompletionTokens { get; }
        public int RoundCount { get;}
        public double CacheHitRate { get; }
        public void Accumulate(Usage? usage);
    }
}
