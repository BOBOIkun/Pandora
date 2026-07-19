namespace Pandora.Agent
{
    /// <summary>单个模型定义</summary>
    public class ModelInfo
    {
        public string Name { get; set; } = "";
        public string Model { get; set; } = "";
        public int? ContextSize { get; set; }
        public string Type { get; set; } = "chat"; // "chat" | "asr"
        public List<string> InputModalities { get; set; } = new(); // "text" | "image" | "audio" | "video"
    }

    /// <summary>供应商完整配置（对应 config/provider/xxx.json）</summary>
    public class ProviderConfig
    {
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public bool UseProxy { get; set; } = false;
        public List<ModelInfo> Models { get; set; } = new();
    }

    /// <summary>供应商摘要（含 id + 模型列表，不含 apiKey）</summary>
    public class ProviderInfo
    {
        public string Id { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public bool UseProxy { get; set; } = false;
        public int ModelCount => Models?.Count ?? 0;
        public List<ModelInfo> Models { get; set; } = new();
    }

    /// <summary>模型选择（provider + model name）</summary>
    public class ModelSelection
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
    }

    /// <summary>解析后的模型完整信息（baseUrl + apiKey + model）</summary>
    public class ResolvedModel
    {
        public string ProviderId { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public bool UseProxy { get; set; } = false;
    }
}
