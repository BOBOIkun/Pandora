using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.Models;
using Pandora.Network;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Pandora.Agent.Tools
{
    public class WebSearchItem
    {
        public required string URL { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; }
    }

    public class TavilySearchT : IAgentTool
    {
        private PandoraHttpClient? _client;

        public AgentTool GetToolDefinition(ISession session)
        {
            return new AgentTool()
            {
                ToolName = "web_search",
                FullDescription = "Search the web for information using Tavily API. "
                    + "Returns search results with URLs, titles, and descriptions. "
                    + "The answer field provides a concise summary of the search results.",
                Parameters = [
                    new() { Name = "query", Type = AgentParametersType.STRING, Description = "The query to search for", Required = true },
                    new() { Name = "count", Type = AgentParametersType.INT, Description = "The number of results to return (max 5)", Required = false },
                ],
                FullLoad = true,
                ParametersTypeCheck = true,
                ReadOnly = true,
                SupportedModes = WorkMode.All,
                ToolFunction = Search
            };
        }

        public void Init(ISession session)
        {
            _client = session.Core.HttpClientFactoryProxy.CreatePandoraClient();
        }

        private (MessageContent? ret, ToolsResult retSatus) Search(ISession session, AgentToolParameterValue param)
        {
            string query = param.GetString("query");
            int count = param.Has("count") ? Math.Min(param.GetInt("count"), 5) : 5;

            try
            {
                var apiKey = session.Core.ConfigManager.GetValue<string>("TavilyKey");
                if (string.IsNullOrEmpty(apiKey))
                    return (new MessageContent("Tavily API key not configured"), ToolsResult.ParametersError);

                _client ??= session.Core.HttpClientFactory.CreatePandoraClient();

                _client.Headers.Clear();
                _client.Headers.Add("Authorization", $"Bearer {apiKey}");

                var task = _client.PostJsonAsync("https://api.tavily.com/search", new
                {
                    query,
                    count,
                    include_raw_content = false,
                    include_usage = true,
                    include_favicon = false,
                    include_answer = "basic",
                    search_depth = "advanced",
                });
                task.Wait();
                var json = task.Result;

                var items = new List<WebSearchItem>();
                if (json.TryGetProperty("answer", out var answer))
                {
                    items.Add(new WebSearchItem
                    {
                        URL = "Answer",
                        Title = "Search Summary",
                        Description = answer.ToString(),
                    });
                }

                if (json.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var result in resultsElement.EnumerateArray())
                    {
                        string url = result.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                        string title = result.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        string description = result.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;

                        items.Add(new WebSearchItem
                        {
                            URL = url,
                            Title = title,
                            Description = description
                        });
                    }
                }

                var resultBuilder = new StringBuilder();
                foreach (var item in items)
                {
                    resultBuilder.AppendLine($"[{item.Title}]");
                    resultBuilder.AppendLine($"URL: {item.URL}");
                    resultBuilder.AppendLine($"Content: {item.Description}");
                    resultBuilder.AppendLine();
                }

                return (new MessageContent(resultBuilder.ToString()), ToolsResult.Success);
            }
            catch (Exception ex)
            {
                return (new MessageContent($"Search error: {ex.Message}"), ToolsResult.UnKnownError);
            }
        }
    }
}