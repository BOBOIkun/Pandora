using System;

namespace Pandora
{
    public enum ErrorCode
    {
        // 会话
        SessionAlreadyExists,
        SessionDirectoryNotFound,
        InvalidSessionDirectory,
        InvalidSessionInfo,
        // 消息
        InvalidMessageFile,
        InvalidMessageJson,
        // 工具
        ToolNotFound,
        TooManyToolErrors,
        TooManyToolUses,
        ToolUseError,
        // 环境
        WorkingDirectoryNotFound,
        PromptFileNotFound,
        RipgrepNotFound,
        GrepNotFound,
        // 重试
        RetryExhausted,
        // 通用
        Internal
    }

    public class PandoraException : Exception
    {
        public ErrorCode Code { get; }
        public object? ErrorData { get; }

        public PandoraException(ErrorCode code, string? message = null, object? errorData = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            ErrorData = errorData;
        }
    }
}
