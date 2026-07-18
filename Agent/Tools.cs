using Pandora.Interfaces;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
namespace Pandora.Agent
{
    public sealed class ReverseLineReader : IDisposable
    {
        private readonly FileStream _fs;
        private readonly int _bufferSize;
        private readonly byte[] _pooledBuffer;
        private long _position;   // 下一次搜索的起始字节偏移
        private bool _disposed;

        private static readonly byte CR = (byte)'\r';
        private static readonly byte LF = (byte)'\n';

        public bool End => _position == 0;

        /// <summary>
        /// 创建一个从文件末尾向前逐行读取的读取器。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="bufferSize">内部缓冲区大小（字节），默认4096</param>
        public ReverseLineReader(string path, int bufferSize = 4096)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            if (bufferSize < 256)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be at least 256 bytes.");

            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _bufferSize = bufferSize;
            _position = _fs.Length;
            // 从共享池租用缓冲区，减少分配
            _pooledBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        }

        /// <summary>
        /// 读取文件中上一行（从当前读取位置向前）。返回 null 表示已无更多行。
        /// </summary>
        public string? ReadLine()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ReverseLineReader));
            if (End) return null;

            long lineEnd = _position;
            long lineStart = 0;
            long newLinePos = -1;          // 换行符所在的文件偏移量
            long searchStart = _position;

            // 从后向前搜索换行符
            while (searchStart > 0 && newLinePos < 0)
            {
                int bytesToRead = (int)Math.Min(_bufferSize, searchStart);
                long readPosition = searchStart - bytesToRead;
                _fs.Seek(readPosition, SeekOrigin.Begin);
                int bytesRead = _fs.Read(_pooledBuffer, 0, bytesToRead);

                var span = new Span<byte>(_pooledBuffer, 0, bytesRead);
                int lfIndex = span.LastIndexOf(LF);
                if (lfIndex >= 0)
                {
                    newLinePos = readPosition + lfIndex;   // 记录换行符位置
                    lineStart = newLinePos + 1;            // 行首 = 换行符之后
                }
                else
                {
                    searchStart = readPosition;
                }
            }

            // 未找到换行符 → 已到文件开头
            if (newLinePos < 0)
            {
                lineStart = 0;
                _position = 0;              // 下次 End 为 true
            }
            else
            {
                _position = newLinePos;     // 下次搜索从换行符之前开始
            }

            // 提取行内容（从 lineStart 到 lineEnd）
            long lineLength = lineEnd - lineStart;
            if (lineLength == 0) return string.Empty;

            _fs.Seek(lineStart, SeekOrigin.Begin);
            byte[] lineBuffer = ArrayPool<byte>.Shared.Rent((int)lineLength);
            try
            {
                int totalRead = 0;
                while (totalRead < lineLength)
                {
                    int read = _fs.Read(lineBuffer, totalRead, (int)(lineLength - totalRead));
                    if (read == 0) break;
                    totalRead += read;
                }
                var lineSpan = new Span<byte>(lineBuffer, 0, totalRead);
                // 处理 Windows 风格行尾的 \r（仅在行末出现且前一次搜索未通过 \n 截断时可能出现）
                if (lineSpan.Length > 0 && lineSpan[^1] == CR)
                    lineSpan = lineSpan.Slice(0, lineSpan.Length - 1);

                return Encoding.UTF8.GetString(lineSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(lineBuffer);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _fs?.Dispose();
                if (_pooledBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_pooledBuffer);
                }
                _disposed = true;
            }
        }
    }

    public class Utils
    {
        public static string GetFullPath(string spath,IAgentEnvironment agentEnvironment)
        {
            return Path.GetFullPath(Path.Combine(agentEnvironment.WorkingDirectory, spath)); ;
        }
        public static string GetSubstringBetween(string str, string start, string end,string empty="")
        {
            int startIndex = str.IndexOf(start);
            if (startIndex == -1)
                return empty;
            startIndex += start.Length;
            int endIndex = str.IndexOf(end, startIndex);
            if (endIndex == -1)
                return empty;
            return str[startIndex..endIndex];
        }
        public static StringBuilder GetSystemInfoStrB()
        {
            CultureInfo uiCulture = CultureInfo.InstalledUICulture;
            StringBuilder stringBuilder = new();
            stringBuilder.Append("System:");
            stringBuilder.Append(RuntimeInformation.OSDescription);
            stringBuilder.Append("Language:");
            stringBuilder.Append(uiCulture.Name);
            return stringBuilder;
        }
        public static bool IsSubdirectoryOf(string childPath, string parentPath)
        {
            string normalizedChild = Path.GetFullPath(childPath);
            string normalizedParent = Path.GetFullPath(parentPath);

            if (normalizedChild == normalizedParent)
                return true;

            string relativePath = Path.GetRelativePath(normalizedParent, normalizedChild);

            return !relativePath.StartsWith("..") && !Path.IsPathRooted(relativePath);
        }
    }
    public sealed class JsonFieldChunk
    {
        public string Field { get; }
        public string Value { get; }

        public JsonFieldChunk(string field, string value)
        {
            Field = field;
            Value = value;
        }

        public override string ToString() => $"\"{Field}\" += \"{Value}\"";
    }

    /// <summary>
    /// 增量式 JSON 字段解析器，专为大模型 ToolCall 的 Arguments 分片到达场景设计。
    /// 每次喂入一段（可能不完整的）JSON 文本，返回该段新解析出的字段内容增量。
    /// </summary>
    /// <remarks>
    /// 1. 字符串值：按字符增量输出（每段只输出本段新增字符）。
    /// 2. 字面量值（数字/布尔/null）：同样按字符增量输出，支持跨段拼接。
    /// 3. 嵌套对象/数组：作为原始文本整体按增量输出（不递归解析内部字段）。
    /// 4. 转义：始终识别转义结构以保证字符串边界正确；构造时指定是否还原为实际字符。
    ///    依据约定不处理 Unicode 转义（\uXXXX），保留原样。
    /// 5. 转义序列保证在同一分片内完整给出（不会被截断到两段）。
    /// </remarks>
    public sealed class IncrementalJsonFieldParser
    {
        private enum State
        {
            Start,            // 初始 / {
            ExpectingKey,     // 期待键
            InKey,            // 读取键字符串
            AfterKey,         // 键闭合，期待 :
            AfterColon,       // : 之后，期待值
            InStringValue,    // 读取字符串值
            InLiteralValue,   // 读取数字/布尔/null
            InRawNested,      // 读取嵌套对象/数组（原始文本）
            AfterValue,       // 值结束，期待 , 或 }
            End               // 对象结束
        }

        private readonly bool _processEscapes;

        private State _state = State.Start;
        private readonly StringBuilder _keyBuilder = new();
        private readonly StringBuilder _valueBuilder = new();    // 字符串值 / 嵌套原始值
        private readonly StringBuilder _literalBuilder = new();  // 字面量值
        private string _currentField = string.Empty;
        private int _emittedLength;   // 当前值已输出的字符数
        private bool _inEscape;       // 字符串中是否处于转义待续
        private int _nestedDepth;     // 嵌套深度

        public IncrementalJsonFieldParser(bool processEscapes)
        {
            _processEscapes = processEscapes;
        }

        /// <summary>是否已解析到对象结束。</summary>
        public bool IsCompleted => _state == State.End;

        /// <summary>
        /// 喂入一段（可能不完整的）JSON 文本，返回本次新增的字段内容片段列表。
        /// </summary>
        public List<JsonFieldChunk> Feed(string chunk)
        {
            var results = new List<JsonFieldChunk>();
            if (string.IsNullOrEmpty(chunk))
                return results;

            foreach (var c in chunk)
                ProcessChar(c, results);

            // 分片结束：若仍处于值读取中，输出当前累积的增量
            FlushActiveValue(results);
            return results;
        }

        /// <summary>重置解析器状态，以便复用。</summary>
        public void Reset()
        {
            _state = State.Start;
            _keyBuilder.Clear();
            _valueBuilder.Clear();
            _literalBuilder.Clear();
            _currentField = string.Empty;
            _emittedLength = 0;
            _inEscape = false;
            _nestedDepth = 0;
        }

        private void ProcessChar(char c, List<JsonFieldChunk> results)
        {
            switch (_state)
            {
                case State.Start:
                    if (char.IsWhiteSpace(c)) return;
                    if (c == '{') _state = State.ExpectingKey;
                    return;

                case State.ExpectingKey:
                    if (char.IsWhiteSpace(c)) return;
                    if (c == '"') { _keyBuilder.Clear(); _state = State.InKey; return; }
                    if (c == '}') _state = State.End;   // 空对象 {}
                    return;

                case State.InKey:
                    HandleStringChar(c, _keyBuilder, isKey: true, results);
                    return;

                case State.AfterKey:
                    if (char.IsWhiteSpace(c)) return;
                    if (c == ':') _state = State.AfterColon;
                    return;

                case State.AfterColon:
                    if (char.IsWhiteSpace(c)) return;
                    if (c == '"')
                    {
                        _valueBuilder.Clear();
                        _emittedLength = 0;
                        _state = State.InStringValue;
                        return;
                    }
                    if (c == '{' || c == '[')
                    {
                        _valueBuilder.Clear();
                        _emittedLength = 0;
                        _nestedDepth = 1;
                        _valueBuilder.Append(c);
                        _state = State.InRawNested;
                        return;
                    }
                    // 数字 / true / false / null
                    _literalBuilder.Clear();
                    _emittedLength = 0;
                    _literalBuilder.Append(c);
                    _state = State.InLiteralValue;
                    return;

                case State.InStringValue:
                    HandleStringChar(c, _valueBuilder, isKey: false, results);
                    return;

                case State.InLiteralValue:
                    if (c == ',' || c == '}' || c == ']')
                    {
                        FlushBuilder(_literalBuilder, results);
                        _literalBuilder.Clear();
                        _emittedLength = 0;
                        _state = (c == ',') ? State.ExpectingKey : State.End;
                        return;
                    }
                    _literalBuilder.Append(c);
                    return;

                case State.InRawNested:
                    _valueBuilder.Append(c);
                    if (c == '{' || c == '[') _nestedDepth++;
                    else if (c == '}' || c == ']') _nestedDepth--;
                    if (_nestedDepth == 0)
                    {
                        FlushBuilder(_valueBuilder, results);
                        _valueBuilder.Clear();
                        _emittedLength = 0;
                        _state = State.AfterValue;
                    }
                    return;

                case State.AfterValue:
                    if (char.IsWhiteSpace(c)) return;
                    if (c == ',') { _state = State.ExpectingKey; return; }
                    if (c == '}') _state = State.End;
                    return;

                case State.End:
                    return;
            }
        }

        /// <summary>处理字符串（键或字符串值）中的单个字符，含转义结构识别。</summary>
        private void HandleStringChar(char c, StringBuilder builder, bool isKey, List<JsonFieldChunk> results)
        {
            if (_inEscape)
            {
                // 转义序列的第二个字符（条件 3 保证与反斜杠在同一分片）
                AppendEscaped(builder, c);
                _inEscape = false;
                return;
            }

            if (c == '\\')
            {
                _inEscape = true;
                return;
            }

            if (c == '"')
            {
                // 字符串闭合
                if (isKey)
                {
                    _currentField = builder.ToString();
                    builder.Clear();
                    _state = State.AfterKey;
                }
                else
                {
                    // 字符串值闭合：输出剩余增量
                    FlushBuilder(builder, results);
                    // 空字符串值：补充一次空增量以标识字段存在
                    if (_emittedLength == 0)
                        results.Add(new JsonFieldChunk(_currentField, string.Empty));
                    _state = State.AfterValue;
                }
                return;
            }

            builder.Append(c);
        }

        /// <summary>输出当前值构建器中尚未输出的增量。</summary>
        private void FlushBuilder(StringBuilder builder, List<JsonFieldChunk> results)
        {
            if (builder.Length > _emittedLength)
            {
                results.Add(new JsonFieldChunk(_currentField,
                    builder.ToString(_emittedLength, builder.Length - _emittedLength)));
                _emittedLength = builder.Length;
            }
        }

        /// <summary>分片结束时，对仍处于读取中的值输出增量。</summary>
        private void FlushActiveValue(List<JsonFieldChunk> results)
        {
            switch (_state)
            {
                case State.InStringValue:
                case State.InRawNested:
                    FlushBuilder(_valueBuilder, results);
                    break;
                case State.InLiteralValue:
                    FlushBuilder(_literalBuilder, results);
                    break;
            }
        }

        /// <summary>追加转义字符。依据构造参数决定是否还原为实际字符；\u 不处理（保留原样）。</summary>
        private void AppendEscaped(StringBuilder builder, char c)
        {
            if (!_processEscapes)
            {
                // 不处理转义：原样保留反斜杠与字符
                builder.Append('\\').Append(c);
                return;
            }

            switch (c)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'u': builder.Append('\\').Append('u'); break; // 不处理 Unicode 转义
                default: builder.Append('\\').Append(c); break;    // 未知转义原样保留
            }
        }
    }

}