using OpenAI.Models.Chat;
using OpenAI.Models.Shared;
using Pandora.Interfaces;
using Pandora.JsonC;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace Pandora.Agent
{
    public static class DataManagerStatic
    {
        private const ushort MAX_READ_MESSAGE = 500;

        // ─── 路径工具 ────────────────────────

        public static string GetSessionDirectory(string sessionId)
            => Path.Combine(AgentEnvironment.GetSessionDataDirectory(), sessionId);

        public static string GetSessionInfoPath(string sessionId)
            => Path.Combine(GetSessionDirectory(sessionId), "session.json");

        public static string GetSessionInfoPathFromDir(string directoryPath)
            => Path.Combine(directoryPath, "session.json");

        public static string GetMessageFilePath(string sessionId, string messageFile)
            => Path.Combine(GetSessionDirectory(sessionId), messageFile);

        // ─── 读取 ────────────────────────

        public static List<ChatMessage> ReadMessages(SessionInfo info, int messageCount = MAX_READ_MESSAGE)
        {
            List<ChatMessage> messages = [];
            string p2 = "";
            for (int i = info.MessageFiles.Count - 1; i >= 0; i--)
            {
                if (messages.Count >= messageCount)
                    break;

                p2 = GetMessageFilePath(info.SessionId, info.MessageFiles[i]);
                if (!File.Exists(p2))
                {
                    throw new PandoraException($"Invalid message file: {p2}");
                }
                using ReverseLineReader reverseLineReader=new(p2);
                while (!reverseLineReader.End)
                {
                    ReadOnlySpan<byte> line = reverseLineReader.ReadLine();
                    if (line.IsEmpty)
                    {
                        continue;
                    }
                    try
                    {
                        var msg = ChatMessageJ.FromSingleJSON(line);
                        messages.Add(msg);
                        if (messages.Count >= messageCount)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Log(LogLevel.Error, $"[Pandora] Invalid message: {ex.Message}");
                        throw new PandoraException($"[Pandora] Invalid message: {ex.Message}");
                    }
                }
            }
            messages.Reverse();
            return messages;
        }
        public static SessionInfo? GetSessionInfoByFile(string file)
        {
            if (!File.Exists(file))
            {
                return null;
            }
            return JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(file, Encoding.UTF8)) ?? null;
        }
        public static SessionInfo? GetSessionInfoById(string id)
        {
            return GetSessionInfoByFile(GetSessionInfoPath(id));
        }
        public static IList<SessionInfo> GetSessionsInfo()
        {
            List<SessionInfo> list = new List<SessionInfo>();
            string[] d=Directory.GetDirectories(AgentEnvironment.GetSessionDataDirectory());
            foreach (var item in d)
            {
                string? q = Path.GetFileName(item);
                if (string.IsNullOrEmpty(q))
                {
                    continue;
                }
                var info = GetSessionInfoById(q);
                if (info != null)
                {
                    list.Add(info);
                }
            }
            return list;
        }
    }
    public class DataManager : IDataManager,IDisposable
    {
        private const uint MAX_MESSAGE_FILE_SIZE = 10*1024*1024;
        private readonly string _path;
        private SessionInfo _sessionInfo=null!;
        private StreamWriter _writer;
        private readonly ISession _session;
        private ArrayBufferWriter<byte> _bufferWriter = new();
        private Utf8JsonWriter _utf8JsonWriter;
        private SemaphoreSlim _semaphoreSlim = new(1);
        public DataManager(ISession session)
        {
            _utf8JsonWriter=new Utf8JsonWriter(_bufferWriter);
            _session = session;
            _path = DataManagerStatic.GetSessionDirectory(_session.SessionId);
            Directory.CreateDirectory(_path);
            var infoPath = DataManagerStatic.GetSessionInfoPathFromDir(_path);
            if (!File.Exists(infoPath)) 
            {
                _sessionInfo = new SessionInfo() { MessageFiles = [], SessionId = _session.SessionId, Title = _session.Title ,WorkingDirectory=_session.AgentEnvironment.WorkingDirectory,WorkMode=session.WorkMode};
            }
            else { Read();}
            if (!FileCheck())
            {
                Logger.Instance.Log(LogLevel.Error, $"[Pandora] Session data is corrupted, rebuilding...");
                _sessionInfo.MessageFiles = [];
            }
            if (_sessionInfo.MessageFiles.Count == 0 || CheckFileRoll())
            {
                string newName = FindNewMessageFileName();
                _sessionInfo.MessageFiles.Add(newName);
            }
            _writer = new StreamWriter(new FileStream(Path.Combine(_path, _sessionInfo.MessageFiles[_sessionInfo.MessageFiles.Count - 1]),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read));
        }
        private bool CheckFileRoll()
        {
            return new FileInfo(Path.Combine(_path, _sessionInfo.MessageFiles[_sessionInfo.MessageFiles.Count - 1])).Length >= MAX_MESSAGE_FILE_SIZE;
        }
        private string FindNewMessageFileName()
        {
            int i = 1;
            while (true) {
                var name = $"message_{i}.jsonl";
                if (!File.Exists(Path.Combine(_path, name)))
                {
                    return name;
                }
                i++;
            }
        }
        private bool FileCheck()
        {
            foreach (var item in _sessionInfo.MessageFiles)
            {
                if (!File.Exists(DataManagerStatic.GetMessageFilePath(_sessionInfo.SessionId,item)))
                {
                    return false;
                }
            }
            return true;
        }
        private void Read()
        {
            SessionInfo? t = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(DataManagerStatic.GetSessionInfoPathFromDir(_path), Encoding.UTF8)) ?? throw new PandoraException("Invalid session info");
            _sessionInfo = t;
        }
        public async Task AppendMessageAsync(ChatMessage msg)
        {
            await _semaphoreSlim.WaitAsync();
            try
            {
                _bufferWriter.Clear();
                _utf8JsonWriter.Reset(_bufferWriter);
                ChatMessageJ.WriteMessage(_utf8JsonWriter, msg, true);
                await _utf8JsonWriter.FlushAsync();
                _writer.BaseStream.Write(_bufferWriter.WrittenSpan);
                _writer.BaseStream.Write("\n"u8);
                await _writer.BaseStream.FlushAsync();
                await _writer.FlushAsync();
                if (CheckFileRoll())
                {
                    string newName = FindNewMessageFileName();
                    _sessionInfo.MessageFiles.Add(newName);
                    _writer?.Dispose();
                    _writer = new StreamWriter(new FileStream(Path.Combine(_path, _sessionInfo.MessageFiles[_sessionInfo.MessageFiles.Count - 1]), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read));
                    Flush();
                }
            }
            finally { _semaphoreSlim.Release(); }
        }
        public void Flush()
        {
            File.WriteAllText(DataManagerStatic.GetSessionInfoPathFromDir(_path), JsonSerializer.Serialize(_sessionInfo),Encoding.UTF8);
        }
        public void Dispose()
        {
            Flush();
            _writer?.Dispose();
            _semaphoreSlim.Dispose();
            _utf8JsonWriter.Dispose();
        }

        public void SetWorkingDirectory(string workingDirectory)
        {
            _sessionInfo.WorkingDirectory = workingDirectory;
            Flush();
        }

        public void SetToolFullLoad(List<string> toolNames)
        {
            _sessionInfo.ToolFullLoad = toolNames;
            Flush();
        }

        public void DeleteSession()
        {
            Dispose();
            Directory.Delete(_path, true);
        }

        public void SetUsage(Usage? usage)
        {
            _sessionInfo.Usage = usage;
            Flush();
        }

        public void SetModel(string? providerId, string? modelName)
        {
            _sessionInfo.AiServiceProviderId = providerId;
            _sessionInfo.AiServiceModelName = modelName;
            Flush();
        }
    }
}
