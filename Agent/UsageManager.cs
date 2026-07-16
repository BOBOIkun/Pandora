using OpenAI.Models.Shared;
using Pandora.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent
{
    public class UsageManager : IUsageManager
    {
        public int CachedTokens {  get; private set; }
        public int ReasoningTokens { get; private set; }
        public int TotalTokens { get; private set; }
        public int PromptTokens { get; private set; }
        public int CompletionTokens { get; private set; }
        public int RoundCount { get; private set; }
        public double CacheHitRate =>
            PromptTokens > 0 ? (double)CachedTokens / PromptTokens : 0;

        public void Accumulate(Usage? usage)
        {
            if (usage == null) return;
            TotalTokens = usage.TotalTokens;
            PromptTokens += usage.PromptTokens;
            CompletionTokens += usage.CompletionTokens;
            CachedTokens += usage.PromptTokensDetails?.CachedTokens ?? 0;
            ReasoningTokens += usage.CompletionTokensDetails?.ReasoningTokens ?? 0;
            RoundCount++;
        }
    }
}
