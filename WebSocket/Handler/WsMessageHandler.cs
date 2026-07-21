using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.Models.Chat;
using Pandora.Agent;
using Pandora.Interfaces;
using Pandora.Models;
using Pandora.WebSocket.Bridge;
using Pandora.WebSocket.Protocol;
using Pandora.WebSocket.Server;

namespace Pandora.WebSocket.Handler
{
    public class WsMessageHandler
    {
        private readonly Agent.Core _core;
        private readonly Dictionary<WsConnection, string?> _connectionSessions = new();

        // 每个 session 正在运行的 CancellationTokenSource，用于 stop
        private readonly Dictionary<string, CancellationTokenSource> _sessionCts = new();

        // 防止首条消息后重复触发生成标题
        private readonly HashSet<string> _titleGenerating = new();
        private readonly object _titleLock = new();

        public WsMessageHandler(Agent.Core core)
        {
            _core = core;
        }

        /// <summary>获取连接当前绑定的 sessionId</summary>
        public string? GetSessionForConnection(WsConnection conn)
        {
            _connectionSessions.TryGetValue(conn, out var sid);
            return sid;
        }

        /// <summary>连接断开时清理</summary>
        public void OnDisconnected(WsConnection conn)
        {
            _connectionSessions.Remove(conn);
            Logger.Instance.Log(LogLevel.Info, "Connection mapping cleaned up");
        }

        /// <summary>主消息入口</summary>
        public async Task HandleMessageAsync(string json, WsConnection conn, CancellationToken ct)
        {
            ClientMessage? msg;
            try
            {
                msg = WsProtocol.Deserialize(json);
                if (msg == null) return;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"Deserialize error: {ex}", nameof(HandleMessageAsync));
                return;
            }

            try
            {
                switch (msg.Type)
                {
                    case "create_session": await HandleCreateSession(msg, conn); break;
                    case "delete_session": await HandleDeleteSession(msg, conn); break;
                    case "rename_session": await HandleRenameSession(msg, conn); break;
                    case "select_session": await HandleSelectSession(msg, conn); break;
                    case "list_sessions": await HandleListSessions(conn); break;
                    case "send_message": await HandleSendMessage(msg, conn); break;
                    case "get_history": await HandleGetHistory(msg, conn); break;
                    case "get_task": await HandleGetTask(msg, conn); break;
                    case "stop": await HandleStop(msg, conn); break;
                    case "file_access_response": HandleFileAccessResponse(msg); break;
                    case "bash_access_response": HandleBashAccessResponse(msg); break;
                    case "set_safety_mode": await HandleSetSafetyMode(msg, conn); break;
                    case "get_providers": await HandleGetProviders(conn); break;
                    case "save_provider": await HandleSaveProvider(msg, conn); break;
                    case "delete_provider": await HandleDeleteProvider(msg, conn); break;
                    case "save_default_models": await HandleSaveDefaultModels(msg, conn); break;
                    case "switch_model": await HandleSwitchModel(msg, conn); break;
                    case "audio_input": await HandleAudioInput(msg, conn); break;
                    case "set_workspace": await HandleSetWorkspace(msg, conn); break;
                    case "list_directory": await HandleListDirectory(msg, conn); break;
                    case "get_common_folders": await HandleGetCommonFolders(msg, conn); break;
                    case "search_models": await HandleSearchModels(msg, conn); break;
                    case "ask_user_response": HandleAskUserResponse(msg); break;
                    default:
                        Logger.Instance.Log(LogLevel.Warning, $"Unknown message type: {msg.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"Error handling '{msg.Type}': {ex}", nameof(HandleMessageAsync));
                try
                {
                    await conn.SendAsync(WsProtocol.Serialize(
                        WsProtocol.Error(msg.SessionId, ex.Message)), ct);
                }
                catch { }
            }
        }

        // ============ Session 管理 ============

        private async Task HandleCreateSession(ClientMessage msg, WsConnection conn)
        {
            string sessionId = msg.SessionId ?? Guid.NewGuid().ToString();
            var mode = ResolveWorkMode(msg.WorkMode);
            var session = _core.CreateSession(sessionId, mode);
            _connectionSessions[conn] = sessionId;
            Logger.Instance.Log(LogLevel.Info, $"Session created: {sessionId} ({mode})");

            // 指定了工作区路径 → 设置
            if (!string.IsNullOrEmpty(msg.Workspace))
            {
                try { session.AgentEnvironment.SetWorkingDirectory(msg.Workspace); }
                catch (PandoraException) { /* 目录不存在则保持默认 */ }
            }

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.SessionCreated(sessionId, msg.Prompt ?? mode.ToString(), mode.ToString().ToLower())));

            // 推送初始 usage、safety 状态和工作区
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.SafetyModeChanged(sessionId,
                    session.SafetyManager.SafetyMode.ToString().ToLower())));

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.WorkspaceChanged(sessionId, session.AgentEnvironment.WorkingDirectory)));

            await PushUsage(session, conn);
        }

        /// <summary>将客户端 workMode 字符串映射为 WorkMode 枚举</summary>
        private static Interfaces.WorkMode ResolveWorkMode(string? workMode) => workMode?.ToLower() switch
        {
            "chatting" => Interfaces.WorkMode.Chatting,
            "working" => Interfaces.WorkMode.Working,
            "coding" => Interfaces.WorkMode.Coding,
            "all" => Interfaces.WorkMode.All,
            _ => Interfaces.WorkMode.Coding // 默认 Coding
        };

        private async Task HandleDeleteSession(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid)) return;

            if (_core.Sessions.Remove(sid))
            {
                Logger.Instance.Log(LogLevel.Info, $"Session deleted: {sid}");
                _sessionCts.Remove(sid);
                if (_connectionSessions.TryGetValue(conn, out var cur) && cur == sid)
                    _connectionSessions[conn] = null;
                await conn.SendAsync(WsProtocol.Serialize(WsProtocol.SessionDeleted(sid)));
                await PushSessionList(conn);
            }
        }

        private async Task HandleRenameSession(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            var title = msg.Title;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(title)) return;
            if (!_core.Sessions.TryGetValue(sid, out var session)) return;

            session.Title = title;
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.SessionTitleChanged(sid, title)));
            await PushSessionList(conn);
        }

        private async Task HandleSetWorkspace(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            var ws = msg.Workspace;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(ws)) return;
            if (!_core.Sessions.TryGetValue(sid, out var session)) return;

            try
            {
                session.AgentEnvironment.SetWorkingDirectory(ws);
                var newWs = session.AgentEnvironment.WorkingDirectory;
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.WorkspaceChanged(sid, newWs)));
                await PushSessionList(conn);
            }
            catch (PandoraException ex)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, ex.Message)));
            }
        }

        private async Task HandleListDirectory(ClientMessage msg, WsConnection conn)
        {
            var path = msg.Path;
            var requestId = msg.RequestId ?? "";

            // 空路径 → 列出驱动器
            if (string.IsNullOrEmpty(path))
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DirectoryEntry
                    {
                        Name = d.Name.TrimEnd('\\'),
                        Path = d.RootDirectory.FullName,
                        HasChildren = true
                    }).ToArray();
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.DirectoryList(requestId, "我的电脑", drives, null)));
                return;
            }

            if (!Directory.Exists(path))
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(null, $"路径不存在: {path}")));
                return;
            }

            var parentPath = Directory.GetParent(path)?.FullName;
            var dirs = GetSafeDirectories(path)
                .Select(d => new DirectoryEntry
                {
                    Name = Path.GetFileName(d),
                    Path = d,
                    HasChildren = HasSafeChildren(d)
                })
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.DirectoryList(requestId, path, dirs, parentPath)));
        }

        private static string[] GetSafeDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch (UnauthorizedAccessException) { return []; }
            catch (IOException) { return []; }
            catch (System.Security.SecurityException) { return []; }
        }

        private static bool HasSafeChildren(string path)
        {
            try { return Directory.EnumerateDirectories(path).Any(); }
            catch { return false; }
        }

        private async Task HandleGetCommonFolders(ClientMessage msg, WsConnection conn)
        {
            var folders = new[]
            {
                ("桌面", Environment.SpecialFolder.Desktop),
                ("文档", Environment.SpecialFolder.MyDocuments),
                ("下载", Environment.SpecialFolder.UserProfile),
                ("图片", Environment.SpecialFolder.MyPictures),
                ("音乐", Environment.SpecialFolder.MyMusic),
                ("视频", Environment.SpecialFolder.MyVideos),
                ("用户目录", Environment.SpecialFolder.UserProfile),
            };

            var entries = new List<DirectoryEntry>();
            foreach (var (label, sf) in folders)
            {
                try
                {
                    var path = Environment.GetFolderPath(sf);
                    if (sf == Environment.SpecialFolder.UserProfile && label == "下载")
                        path = Path.Combine(path, "Downloads");

                    if (!Directory.Exists(path)) continue;
                    entries.Add(new DirectoryEntry
                    {
                        Name = label,
                        Path = path,
                        HasChildren = HasSafeChildren(path)
                    });
                }
                catch { /* skip inaccessible */ }
            }

            // 去重（用户目录可能与其他重复）
            var deduped = entries
                .GroupBy(e => e.Path)
                .Select(g => g.First())
                .ToArray();

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.CommonFolders(deduped)));
        }

        // ============ 模型搜索 ============

        private class RawModelEntry
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("input_modalities")] public List<string> InputModalities { get; set; } = new();
            [JsonPropertyName("output_modalities")] public List<string> OutputModalities { get; set; } = new();
            [JsonPropertyName("context_length")] public int ContextLength { get; set; }
        }

        private static List<RawModelEntry>? _modelCatalogCache;
        private static readonly object _catalogLock = new();

        private static List<RawModelEntry> LoadModelCatalog()
        {
            lock (_catalogLock)
            {
                if (_modelCatalogCache != null) return _modelCatalogCache;
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "models.json");
                if (!File.Exists(path)) { _modelCatalogCache = new(); return _modelCatalogCache; }
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _modelCatalogCache = JsonSerializer.Deserialize<List<RawModelEntry>>(json, opts) ?? new();
                return _modelCatalogCache;
            }
        }

        private async Task HandleSearchModels(ClientMessage msg, WsConnection conn)
        {
            var keyword = (msg.Content ?? "").Trim().ToLowerInvariant();
            var requestId = msg.RequestId ?? "";
            var catalog = LoadModelCatalog();

            var filtered = string.IsNullOrEmpty(keyword)
                ? catalog
                : catalog.Where(e =>
                    e.Id.ToLowerInvariant().Contains(keyword) ||
                    e.Name.ToLowerInvariant().Contains(keyword)).ToList();

            var total = filtered.Count;
            var results = filtered.Take(50).Select(e => new ModelSearchEntry
            {
                Id = e.Id,
                Name = e.Name,
                InputModalities = e.InputModalities,
                ContextLength = e.ContextLength
            }).ToArray();

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.ModelSearchResult(requestId, results, total)));
        }

        private static void HandleAskUserResponse(ClientMessage msg)
        {
            var requestId = msg.RequestId;
            var answer = msg.Content ?? "";
            if (!string.IsNullOrEmpty(requestId))
            {
                SessionBridge.ResolveAskUserQuestion(requestId, answer);
            }
        }

        private async Task HandleSelectSession(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid)) return;

            if (_core.Sessions.TryGetValue(sid, out var session))
            {
                _connectionSessions[conn] = sid;
                await PushUsage(session, conn);
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.SafetyModeChanged(sid,
                        session.SafetyManager.SafetyMode.ToString().ToLower())));

                // 推送当前模型信息
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.SessionModelChanged(sid,
                        session.AiService.ChatModel.ProviderId,
                        session.AiService.ChatModel.ModelName,
                        session.AiService.ChatModel.ModelName,
                        "")));
            }
        }

        private async Task HandleListSessions(WsConnection conn)
        {
            await PushSessionList(conn);
        }

        // ============ 核心：send_message ============

        private async Task HandleSendMessage(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid) || !_core.Sessions.TryGetValue(sid, out var session))
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, "Session not found")));
                return;
            }

            _connectionSessions[conn] = sid;

            // 设置 reasoning_effort
            if (!string.IsNullOrEmpty(msg.ReasoningEffort) &&
                Enum.TryParse<OpenAI.Models.Chat.ReasoningEffort>(msg.ReasoningEffort, true, out var effort))
            {
                session.AiService.CurrentReasoningEffort = effort;
            }

            // 添加用户消息
            var content = msg.Content ?? "";
            var infoPrefix = $@"<information>
[APP]Pandora Web Client
[TimeNow]{DateTime.Now:yyyy-MM-dd HH:mm:ss}
</information>
{session.ChangeInfo}
<user>
{content}
</user>";

            ChatMessage userMessage;
            if (msg.Images != null && msg.Images.Length > 0)
            {
                // 多模态消息：文本 + 图片
                var parts = new List<ContentPart>
                {
                    new TextContentPart(infoPrefix)
                };
                foreach (var img in msg.Images)
                {
                    parts.Add(new ImageContentPart
                    {
                        ImageUrl = new ImageUrl { Url = img }
                    });
                }
                userMessage = ChatMessage.FromUser([.. parts]);
            }
            else
            {
                userMessage = ChatMessage.FromUser(infoPrefix);
            }
            session.MessageManager.AddMessage(userMessage);

            // 首条消息 → 生成会话标题（防重复触发）
            if (string.IsNullOrEmpty(session.Title) && !string.IsNullOrWhiteSpace(content))
            {
                bool shouldGenerate;
                lock (_titleLock)
                {
                    shouldGenerate = _titleGenerating.Add(sid);
                }
                if (shouldGenerate)
                    _ = GenerateSessionTitle(session, content, conn);
            }

            // 创建 CancellationTokenSource 用于 stop
            var cts = new CancellationTokenSource();
            _sessionCts[sid] = cts;

            // 创建 SessionBridge 并订阅事件
            var bridge = new SessionBridge(session, conn);
            bridge.Subscribe();

            try
            {
                await session.CompleteChat(new CompleteChatOptions(), cts.Token);
                if (string.IsNullOrEmpty(session.Title))
                {
                    string title = await session.CreateSessionTitle(content);
                    session.Title = title;
                    session.EventBus.Publish(new SessionTitleChangedEvent() { SessionId = sid, Title = title });
                }
            }
            catch (OperationCanceledException)
            {
                // stop 触发的取消，正常
            }
            catch (PandoraException ex)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, ex.Message)));
            }
            catch (Exception ex)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, ex.Message)));
            }
            finally
            {
                bridge.Unsubscribe();
                _sessionCts.Remove(sid);
            }
        }

        // ============ History ============

        private async Task HandleGetHistory(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid) || !_core.Sessions.TryGetValue(sid, out var session))
                return;

            var messages = session.MessageManager.GetMessages();
            var history = new List<Protocol.HistoryMessage>();

            // 第一遍：收集 tool 消息的 result，按 toolCallId 索引
            var toolResults = new Dictionary<string, string>();
            foreach (var m in messages)
            {
                if (m.Role == "tool" && m.ToolCallId != null)
                    toolResults[m.ToolCallId] = m.Content?.Text ?? "";
            }

            foreach (var m in messages)
            {
                if (m.Role == "system" || m.Role == "tool") continue;

                var hm = new Protocol.HistoryMessage
                {
                    Role = m.Role,
                    Content = m.Content?.Text
                };
                if (m.Role == "user")
                {
                    // 多模态消息：从 ContentParts 提取文本和图片
                    if (m.Content?.Parts?.Count > 0)
                    {
                        var textParts = new List<string>();
                        var imageUrls = new List<string>();
                        foreach (var part in m.Content.Parts)
                        {
                            if (part is TextContentPart textPart && textPart.Text != null)
                                textParts.Add(textPart.Text);
                            else if (part is ImageContentPart imagePart && imagePart.ImageUrl?.Url != null)
                                imageUrls.Add(imagePart.ImageUrl.Url);
                        }
                        hm.Content = Utils.GetSubstringBetween(string.Join("", textParts), "<user>", "</user>");
                        if (imageUrls.Count > 0)
                            hm.Images = [.. imageUrls];
                    }
                    else
                    {
                        hm.Content = Utils.GetSubstringBetween(hm.Content ?? "", "<user>", "</user>", hm.Content ?? "");
                    }
                }
                if (m.Role == "assistant")
                {
                    hm.Reasoning = m.ReasoningContent;
                    hm.Reasoning ??= m.Extension?.ReasoningExtension;
                    if (m.ToolCalls?.Count > 0)
                    {
                        hm.ToolCalls = m.ToolCalls.Select(tc =>
                        {
                            toolResults.TryGetValue(tc.Id ?? "", out var result);
                            return new Protocol.HistoryToolCall
                            {
                                ToolCallId = tc.Id ?? "",
                                ToolName = tc.FunctionCall?.Name ?? "",
                                Arguments = tc.FunctionCall?.Arguments,
                                Result = result
                            };
                        }).ToArray();
                    }
                }

                history.Add(hm);
            }

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.History(sid, [.. history])));
        }

        // ============ Task ============

        private async Task HandleGetTask(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid)) return;
            // Pandora 暂不支持 task list，返回空数组
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.TaskList(sid, [])));
        }

        // ============ Stop ============

        private async Task HandleStop(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid)) return;

            if (_sessionCts.TryGetValue(sid, out var cts))
            {
                cts.Cancel();
            }
        }

        // ============ 安全确认 ============

        private void HandleFileAccessResponse(ClientMessage msg)
        {
            if (!string.IsNullOrEmpty(msg.RequestId))
                SessionBridge.ResolveFileAccess(msg.RequestId, msg.Approved ?? false);
        }

        private void HandleBashAccessResponse(ClientMessage msg)
        {
            if (!string.IsNullOrEmpty(msg.RequestId))
                SessionBridge.ResolveBashAccess(msg.RequestId, msg.Approved ?? false);
        }

        // ============ Safety Mode ============

        private async Task HandleSetSafetyMode(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid) || !_core.Sessions.TryGetValue(sid, out var session))
                return;

            var mode = msg.Mode?.ToLower() switch
            {
                "full" => SafetyMode.Full,
                _ => SafetyMode.Partial  // "partial" 和 "restricted" 都映射为 Partial
            };

            session.SafetyManager.SafetyMode = mode;
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.SafetyModeChanged(sid, mode.ToString().ToLower())));
        }

        // ============ Providers ============

        private async Task HandleGetProviders(WsConnection conn)
        {
            var pm = _core.ProviderManager;
            var providers = pm.ListProviders().Select(p => new ProviderItem
            {
                Id = p.Id,
                BaseUrl = p.BaseUrl,
                ApiKey = "",
                UseProxy = p.UseProxy,
                Models = p.Models.Select(m => new ProviderModelItem
                {
                    Name = m.Name,
                    Model = m.Model,
                    ContextSize = m.ContextSize ?? 0,
                    Type = m.Type,
                    InputModalities = m.InputModalities ?? new List<string>()
                }).ToArray()
            }).ToArray();

            // 也返回带 apiKey 的完整配置（前端设置页需要）
            foreach (var p in providers)
            {
                var full = pm.GetProvider(p.Id);
                if (full != null) p.ApiKey = full.ApiKey;
            }

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.ProviderList(providers,
                    pm.DefaultChatModel, pm.DefaultAsrModel)));
        }

        private async Task HandleSaveProvider(ClientMessage msg, WsConnection conn)
        {
            var id = msg.Id;
            if (string.IsNullOrEmpty(id) || msg.Config == null)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.ProviderSaved(id ?? "", false, "id or config missing")));
                return;
            }

            try
            {
                var config = JsonSerializer.Deserialize<Agent.ProviderConfig>(
                    msg.Config.Value.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (config == null)
                {
                    await conn.SendAsync(WsProtocol.Serialize(
                        WsProtocol.ProviderSaved(id, false, "deserialize failed")));
                    return;
                }

                _core.ProviderManager.SaveProvider(id, config);
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.ProviderSaved(id, true)));
            }
            catch (Exception ex)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.ProviderSaved(id, false, ex.Message)));
            }
        }

        private async Task HandleDeleteProvider(ClientMessage msg, WsConnection conn)
        {
            var id = msg.Id;
            if (string.IsNullOrEmpty(id)) return;

            var ok = _core.ProviderManager.DeleteProvider(id);
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.ProviderDeleted(id, ok)));
        }

        private async Task HandleSaveDefaultModels(ClientMessage msg, WsConnection conn)
        {
            try
            {
                Agent.ModelSelection? chat = null;
                Agent.ModelSelection? asr = null;

                if (msg.Chat != null)
                {
                    chat = JsonSerializer.Deserialize<Agent.ModelSelection>(
                        msg.Chat.Value.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                if (msg.Asr != null)
                {
                    asr = JsonSerializer.Deserialize<Agent.ModelSelection>(
                        msg.Asr.Value.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                _core.ProviderManager.SetDefaults(chat, asr);
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.DefaultModelsUpdated(chat, asr)));
            }
            catch (Exception ex)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(null, $"save_default_models: {ex.Message}")));
            }
        }

        // ============ Model Switch ============

        private async Task HandleSwitchModel(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            var providerId = msg.ProviderId;
            var modelName = msg.ModelName;

            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(modelName))
                return;

            if (!_core.Sessions.TryGetValue(sid, out var session)) return;

            bool ok = session.AiService.SwitchModel(providerId, modelName);
            if (ok)
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.SessionModelChanged(sid, providerId, modelName, modelName, "")));
            }
            else
            {
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, $"Switch model failed: {providerId}/{modelName}")));
            }
        }

        // ============ Audio Input ============

        private async Task HandleAudioInput(ClientMessage msg, WsConnection conn)
        {
            var sid = msg.SessionId;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(msg.Audio))
                return;

            if (!_core.Sessions.TryGetValue(sid, out var session)) return;

            try
            {
                var audioBytes = Convert.FromBase64String(msg.Audio);
                var format = msg.AudioFormat ?? "webm";
                var text = await session.AiService.TranscribeAsync(audioBytes, format);
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.TranscriptionResult(sid, text)));
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"Audio transcription failed: {ex}", nameof(HandleAudioInput));
                await conn.SendAsync(WsProtocol.Serialize(
                    WsProtocol.Error(sid, $"Transcription failed: {ex.Message}")));
            }
        }

        // ============ 辅助方法 ============

        private async Task GenerateSessionTitle(ISession session, string prompt, WsConnection conn)
        {
            try
            {
                var title = await session.CreateSessionTitle(prompt);
                if (!string.IsNullOrEmpty(title))
                {
                    session.Title = title;
                    _ = conn.SendAsync(WsProtocol.Serialize(
                        WsProtocol.SessionTitleChanged(session.SessionId, title)));
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"Generate title failed: {ex}", nameof(GenerateSessionTitle));
            }
            finally
            {
                lock (_titleLock) _titleGenerating.Remove(session.SessionId);
            }
        }

        private async Task PushSessionList(WsConnection conn)
        {
            var sessions = _core.Sessions.Values.Select(s => new SessionSummary
            {
                SessionId = s.SessionId,
                Prompt = s.WorkMode.ToString(),
                Title = string.IsNullOrEmpty(s.Title) ? "新建对话" : s.Title,
                Workspace = s.AgentEnvironment.WorkingDirectory,
                MessageCount = s.MessageManager.GetMessages().Count
            }).ToArray();

            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.SessionList(sessions)));
        }

        private async Task PushUsage(ISession session, WsConnection conn)
        {
            var u = session.UsageManager;
            await conn.SendAsync(WsProtocol.Serialize(
                WsProtocol.UsageUpdate(
                    session.SessionId,
                    u.PromptTokens, u.CompletionTokens, u.TotalTokens,
                    u.CachedTokens, u.ReasoningTokens,
                    u.RoundCount, u.CacheHitRate)));
        }
    }
}
