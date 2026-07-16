# Pandora 项目记忆

## 架构
- C# .NET 10.0 控制台项目，AI Agent 框架
- 分层：Program → Core → Session（Agent Loop）→ AiService（LLM流式调用）、MessageManager（对话历史）、AgentToolManager（工具注册）、SafetyManager（安全控制）、UsageManager（token统计）
- 事件驱动：EventBus 发布/订阅（同步 Action 和 Func 两种模式），每个事件仅一个 handler

## WebSocket 适配（2026-07-14）
- 新增 WebSocket/ 层（不改变原架构）
- 协议：JSON over WebSocket text frames，30+ 消息类型
- 新增 5 种事件：AssistantMessageStartEvent、ReasoningEndEvent、ContentEndEvent、ToolCallEndEvent、AgentUsageChangedEvent（补充 CacheHitRate）
- 服务端监听 `http://localhost:9527/`，web-client 通过 Vite 代理 `/ws` → `ws://localhost:9527`
- 安全确认流程：FileAccessConfirmEvent/BashConfirmEvent → TCS 同步阻塞 → WebSocket 请求客户端批准
- web 端创建会话支持 WorkMode 选择（编程/聊天/工作），通过 `create_session.workMode` 字段传递

## 配置
- 供应商配置：`config/provider/*.json`
- 默认模型：`config/config.json`
- 系统提示：`config/prompt/code.txt`（Coding 模式）
