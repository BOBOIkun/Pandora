using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pandora.Interfaces;

namespace Pandora.Agent
{
    /// <summary>
    /// 多供应商管理器：扫描 config/provider/*.json，提供模型解析和默认模型选择。
    /// </summary>
    public class ProviderManager
    {
        private readonly string _providerDir;
        private readonly string _configPath;
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public Dictionary<string, ProviderConfig> Providers { get; } = [];
        public ModelSelection DefaultChatModel { get; private set; } = new();
        public ModelSelection DefaultAsrModel { get; private set; } = new();

        public ProviderManager(string baseDir)
        {
            _providerDir = Path.Combine(baseDir, "config", "provider");
            _configPath = Path.Combine(baseDir, "config", "config.json");
            Directory.CreateDirectory(_providerDir);
            ReloadAll();
            LoadDefaults();
        }

        /// <summary>重新扫描所有供应商文件</summary>
        public void ReloadAll()
        {
            Providers.Clear();
            foreach (var file in Directory.GetFiles(_providerDir, "*.json"))
            {
                try
                {
                    var id = Path.GetFileNameWithoutExtension(file);
                    var config = JsonSerializer.Deserialize<ProviderConfig>(File.ReadAllText(file), _jsonOpts);
                    if (config == null) continue;
                    Providers[id] = config;
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(LogLevel.Warning, $"跳过 {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        /// <summary>从 config.json 加载默认模型选择（直接读文件，绕过 ByteConfigManager）</summary>
        public void LoadDefaults()
        {
            if (!File.Exists(_configPath)) return;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
                var root = doc.RootElement;
                DefaultChatModel = ParseModelSelectionFromElement(root, "defaultChatModel") ?? new();
                DefaultAsrModel = ParseModelSelectionFromElement(root, "defaultAsrModel") ?? new();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"加载默认模型失败: {ex}", nameof(LoadDefaults));
            }
        }

        private static ModelSelection? ParseModelSelectionFromElement(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el)) return null;
            return JsonSerializer.Deserialize<ModelSelection>(el.GetRawText(), _jsonOpts);
        }

        /// <summary>获取供应商完整配置</summary>
        public ProviderConfig? GetProvider(string id)
        {
            Providers.TryGetValue(id, out var p);
            return p;
        }

        /// <summary>获取供应商摘要（含模型列表，不含 apiKey）</summary>
        public ProviderInfo? GetProviderInfo(string id)
        {
            var p = GetProvider(id);
            if (p == null) return null;
            return new ProviderInfo { Id = id, BaseUrl = p.BaseUrl, UseProxy = p.UseProxy, Models = p.Models };
        }

        /// <summary>列出所有供应商</summary>
        public List<ProviderInfo> ListProviders()
        {
            var list = new List<ProviderInfo>();
            foreach (var kv in Providers)
                list.Add(new ProviderInfo { Id = kv.Key, BaseUrl = kv.Value.BaseUrl, UseProxy = kv.Value.UseProxy, Models = kv.Value.Models });
            return list;
        }

        /// <summary>解析模型选择 → 完整连接信息</summary>
        public ResolvedModel? ResolveModel(ModelSelection sel)
        {
            if (string.IsNullOrEmpty(sel.Provider) || string.IsNullOrEmpty(sel.Model))
                return null;
            var p = GetProvider(sel.Provider);
            if (p == null) return null;
            var m = p.Models.Find(x => x.Model == sel.Model || x.Name == sel.Model);
            if (m == null) return null;
            return new ResolvedModel
            {
                ProviderId = sel.Provider,
                ModelName = m.Model,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                UseProxy = p.UseProxy
            };
        }

        /// <summary>保存供应商配置文件</summary>
        public void SaveProvider(string id, ProviderConfig config)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            // apiKey 为空 => 从现有配置继承（前端未修改则不传此字段）
            if (string.IsNullOrWhiteSpace(config.ApiKey) && Providers.TryGetValue(id, out var existing))
            {
                config.ApiKey = existing.ApiKey;
            }

            Providers[id] = config;
            var path = Path.Combine(_providerDir, id + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(config, _jsonOpts), Encoding.UTF8);
        }

        /// <summary>删除供应商配置文件</summary>
        public bool DeleteProvider(string id)
        {
            Providers.Remove(id);
            var path = Path.Combine(_providerDir, id + ".json");
            if (File.Exists(path)) { File.Delete(path); return true; }
            return false;
        }

        /// <summary>设置默认模型并持久化到 config.json</summary>
        public void SetDefaults(ModelSelection? chat, ModelSelection? asr)
        {
            if (chat != null) DefaultChatModel = chat;
            if (asr != null) DefaultAsrModel = asr;

            // 读取现有 config.json（或创建空对象），保留其他键
            JsonObject root;
            if (File.Exists(_configPath))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(_configPath))?.AsObject() ?? new();
                }
                catch { root = new(); }
            }
            else { root = new(); }

            root["defaultChatModel"] = JsonSerializer.SerializeToNode(DefaultChatModel);
            root["defaultAsrModel"] = JsonSerializer.SerializeToNode(DefaultAsrModel);
            File.WriteAllText(_configPath, root.ToJsonString(_jsonOpts), Encoding.UTF8);
        }

        /// <summary>按类型过滤所有可用的模型列表（跨供应商）</summary>
        public List<(string providerId, string providerName, ModelInfo model)> GetAllModelsByType(string type = "chat")
        {
            var result = new List<(string, string, ModelInfo)>();
            foreach (var (id, p) in Providers)
            {
                foreach (var m in p.Models)
                {
                    if (string.Equals(m.Type, type, StringComparison.OrdinalIgnoreCase))
                        result.Add((id, id, m));
                }
            }
            return result;
        }
    }
}
