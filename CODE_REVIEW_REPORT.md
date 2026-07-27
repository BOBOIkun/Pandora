# Pandora 项目全面审查报告

**审查时间**: 2026-07-26
**项目**: Pandora Web Client - AI Agent 服务端
**技术栈**: C# / .NET 10 / WebSocket / OpenAI API

---

## 🔴 严重安全漏洞（P0 - 必须立即修复）

### 1. API 密钥硬编码泄露
**位置**: 
- `bin/Debug/net10.0/config/config.json` (TavilyKey)
- `bin/Debug/net10.0/config/provider/qiniu.json` (apiKey)
- `bin/Debug/net10.0/config/provider/deepSeek.json` (apiKey)

**问题**:
```json
// config.json
"TavilyKey": "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"

// qiniu.json
"apiKey": "sk-06432992bda68d42012e2555fe34025251bbd8812f824c1685dceca613235054"
```

**风险**: 代码仓库一旦泄露，攻击者可直接使用这些密钥调用付费 API，造成经济损失。

**修复建议**:
- 使用环境变量或密钥管理服务（如 Azure Key Vault、AWS Secrets Manager）
- 配置文件加入 `.gitignore`
- 提供配置模板文件（如 `config.example.json`）

---

### 2. WebSocket 无身份验证
**位置**: `WebSocket/Server/WsServer.cs`, `WebSocket/Server/WsConnection.cs`

**问题**:
- 任何人都可以连接到 `ws://localhost:9527/ws`
- 没有 Token 验证、用户认证、来源检查
- 可以执行任意命令、读写文件、访问所有 Session

**风险**: 完全暴露的内部服务，攻击者可远程控制服务器。

**修复建议**:
- 实现 JWT Token 或 API Key 认证
- 添加来源 IP 白名单
- 实现速率限制防止暴力破解

---

### 3. 命令注入与执行策略风险
**位置**: `Agent/Tools/BashT.cs`

**问题**:
```csharp
psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {Convert.ToBase64String(bytes)}";
```

- 使用 `ExecutionPolicy Bypass` 绕过安全策略
- `SafetyMode.Full` 模式下所有命令都被允许
- 白名单检查依赖 `bash.json` 和 `aliases.txt`，但可能存在绕过

**风险**: 攻击者可执行任意系统命令，完全控制服务器。

**修复建议**:
- 默认使用 `Restricted` 执行策略
- 增强命令解析器，防止编码绕过
- 添加命令审计日志

---

### 4. 路径遍历与任意文件访问
**位置**: `Agent/Tools/ReadFileT.cs`, `Agent/Tools/WriteFileT.cs`, `Agent/Tools/FileEditT.cs`

**问题**:
- `SafetyMode.Full` 模式下可读写任意文件
- `LoadSessionFromDirectory` 没有验证目录合法性
- 符号链接可能被利用访问非预期文件

**风险**: 读取系统敏感文件（如 `/etc/passwd`）、覆盖关键系统文件。

**修复建议**:
- 默认限制只能访问工作目录
- 解析符号链接并验证最终路径
- 添加文件访问审计日志

---

### 5. 敏感信息泄露到客户端
**位置**: `WebSocket/Handler/WsMessageHandler.cs` (HandleGetProviders)

**问题**:
```csharp
// 也返回带 apiKey 的完整配置（前端设置页需要）
foreach (var p in providers)
{
    var full = pm.GetProvider(p.Id);
    if (full != null) p.ApiKey = full.ApiKey;  // 直接返回 API Key
}
```

**风险**: API Key 暴露给所有 WebSocket 客户端。

**修复建议**:
- 默认不返回 API Key
- 如需编辑，提供单独的加密传输通道

---

## 🟠 架构设计问题（P1 - 高优先级修复）

### 1. EventBus 设计缺陷
**位置**: `Event/EventBus.cs`

**问题**:
- 每个事件类型只能注册一个处理器（单播）
- 没有取消订阅机制
- 不支持异步事件处理

**影响**: 扩展性差，多个 WebSocket 连接无法同时监听同一 Session。

**修复建议**:
- 使用多播委托或事件处理器列表
- 实现 `Unsubscribe` 方法
- 支持异步事件处理器

---

### 2. Session 管理不线程安全
**位置**: `Agent/Core.cs`

**问题**:
```csharp
public Dictionary<string, ISession> Sessions {get;}= [];  // 非线程安全
```

**影响**: 多线程并发访问可能导致数据损坏。

**修复建议**:
- 使用 `ConcurrentDictionary<string, ISession>`
- 或添加读写锁保护

---

### 3. 同步阻塞异步代码（死锁风险）
**位置**: 
- `Agent/Tools/TavilySearchT.cs`
- `WebSocket/Bridge/SessionBridge.cs`

**问题**:
```csharp
// TavilySearchT.cs
var task = _client.PostJsonAsync(...);
task.Wait();  // 同步阻塞
var json = task.Result;

// SessionBridge.cs
return tcs.Task.GetAwaiter().GetResult();  // 同步阻塞异步操作
```

**风险**: 在 UI 线程或 ASP.NET 上下文中可能导致死锁。

**修复建议**:
- 全程使用 `async/await`
- 使用 `ConfigureAwait(false)` 避免上下文捕获

---

### 4. 缺乏依赖注入
**位置**: 整个项目

**问题**:
- 所有依赖手动创建（`new`）
- 没有使用 DI 容器
- 难以进行单元测试

**修复建议**:
- 引入 Microsoft.Extensions.DependencyInjection
- 注册服务生命周期（Singleton/Scoped/Transient）

---

### 5. HttpClient 管理不当
**位置**: `Network/Http.cs`

**问题**:
- 没有配置证书验证回调
- 没有连接数限制
- `PooledConnectionLifetime` 设置过短（2 分钟）

**修复建议**:
- 配置 `ServerCertificateCustomValidationCallback`
- 设置 `MaxConnectionsPerServer`
- 使用 IHttpClientFactory 管理生命周期

---

## 🟡 错误处理和异常风险（P1）

### 1. 未处理的异常导致资源泄漏
**位置**: `Agent/Tools.cs` (ReverseLineReader)

**问题**:
```csharp
byte[] lineBuffer = ArrayPool<byte>.Shared.Rent((int)lineLength);
try { ... }
finally { ArrayPool<byte>.Shared.Return(lineBuffer); }
```

如果 `lineLength` 过大，可能导致 `OutOfMemoryException`。

---

### 2. 异步方法异常被吞没
**位置**: `Agent/AiService.cs`

**问题**:
```csharp
catch (Exception ex)
{
    return new CompletionResult(reasoning.ToString(), content.ToString(), [], null, ex);
}
```

异常被转换为返回结果，调用者可能忽略检查 `Exception` 属性。

---

### 3. CancellationToken 未正确传递
**位置**: `Agent/Session.cs`

**问题**:
```csharp
public async Task CompleteChat(CompleteChatOptions options, CancellationToken cancellationToken)
{
    //  cancellationToken 未传递给某些内部调用
}
```

---

### 4. 空引用风险
**位置**: 多处

**问题**:
- `msg.SessionId` 可能为 null
- `ChatMessage.Content?.Text` 可能为 null
- 很多字典访问没有检查 Key 是否存在

---

## 🔵 性能和可扩展性问题（P2）

### 1. 内存泄漏风险
**位置**: `Agent/FileState.cs`

**问题**:
- `_fileState` 字典无限增长
- 没有实现文件状态过期清理
- `FindcChangedFiles` 循环遍历所有文件

---

### 2. 消息历史无分页
**位置**: `Agent/DataManager.cs`

**问题**:
- 所有消息加载到内存
- 没有分页或懒加载机制
- 长对话可能导致内存溢出

---

### 3. 缺乏限流和熔断
**位置**: 整个项目

**问题**:
- 没有 API 调用频率限制
- 没有熔断机制
- AI 服务故障可能导致级联失败

---

## 🟣 配置和部署问题（P2）

### 1. 硬编码路径
**位置**: 多处

**问题**:
- `AppDomain.CurrentDomain.BaseDirectory` 依赖部署结构
- 没有使用配置文件或环境变量

---

### 2. 缺乏健康检查
**位置**: `Program.cs`

**问题**:
- 没有健康检查端点
- 没有监控和告警机制

---

### 3. 日志配置不当
**位置**: `Logger.cs`

**问题**:
- 日志级别固定为 `Trace`
- 没有日志脱敏
- 敏感信息可能写入日志

---

## 🟢 代码质量问题（P3）

### 1. 命名不一致
- 方法命名：`FindcChangedFiles`（拼写错误，应为 `FindChangedFiles`）
- 变量命名：`_re`, `_wr` 含义不明

---

### 2. 魔法数字
- `maxOutputChars = 8000`
- `MAX_TEXT_LENGTH = 30000`
- `timeoutSec * 1000`

---

### 3. 缺乏单元测试
- 没有测试项目
- 核心逻辑没有测试覆盖

---

## 📋 上线前必须修复清单

### 阻塞上线（必须修复）
- [ ] 移除所有硬编码 API 密钥，改用环境变量
- [ ] 实现 WebSocket 身份验证
- [ ] 修复同步阻塞异步代码（TavilySearchT、SessionBridge）
- [ ] 默认禁用 `SafetyMode.Full`
- [ ] 修复 API Key 泄露到客户端问题

### 高优先级（上线前修复）
- [ ] 实现 Session 超时清理
- [ ] 使用 `ConcurrentDictionary` 保护 Sessions
- [ ] 添加 API 调用限流
- [ ] 实现日志脱敏
- [ ] 添加健康检查端点
- [ ] 修复 EventBus 支持多播

### 中优先级（上线后尽快修复）
- [ ] 引入依赖注入
- [ ] 实现消息历史分页
- [ ] 添加单元测试
- [ ] 实现配置外部化
- [ ] 添加熔断机制

---

## 🎯 架构改进建议

### 短期（1-2 周）
1. 实现配置加密和密钥管理
2. 添加身份验证中间件
3. 修复所有同步阻塞异步代码
4. 添加全局异常处理

### 中期（1-2 月）
1. 重构 EventBus 支持多播
2. 引入依赖注入容器
3. 实现消息分页和懒加载
4. 添加监控和告警

### 长期（3-6 月）
1. 实现多租户支持
2. 添加插件系统
3. 实现分布式部署
4. 完善测试覆盖

---

## 📊 风险评估

| 风险类别 | 严重程度 | 可能性 | 风险等级 |
|---------|---------|--------|---------|
| API 密钥泄露 | 高 | 高 | 🔴 严重 |
| 未授权访问 | 高 | 高 | 🔴 严重 |
| 命令注入 | 高 | 中 | 🟠 高 |
| 死锁 | 中 | 中 | 🟡 中 |
| 内存泄漏 | 中 | 中 | 🟡 中 |

---

**结论**: 当前项目存在多个严重安全漏洞，**不建议直接上线**。必须优先修复 P0 和 P1 级别的问题，特别是身份验证、密钥管理和命令执行安全。
