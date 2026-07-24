using OpenAI.Models.Chat;
using Pandora.Interfaces;
using Pandora.JsonC;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Pandora.Agent
{
    public class DataManager : IDataManager,IDisposable
    {
        private const int MAX_MESSAGE_FILE_SIZE = 10*1024*1024;
        private readonly string _path;
        private SessionInfo _sessionInfo=null!;
        private StreamWriter _writer;
        private readonly ISession _session;
        private ArrayBufferWriter<byte> _bufferWriter = new();
        private Utf8JsonWriter _utf8JsonWriter;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);
        public DataManager(ISession session)
        {
            _utf8JsonWriter=new Utf8JsonWriter(_bufferWriter);
            _session = session;
            _path = Path.Combine(_session.AgentEnvironment.SessionDataDirectory, _session.SessionId);
            Directory.CreateDirectory(_path);
            if (!File.Exists(Path.Combine(_path,"session.json"))) 
            {
                _sessionInfo = new SessionInfo() { MessageFiles = [], SessionId = _session.SessionId, Title = _session.Title ,WorkingDirectory=_session.AgentEnvironment.WorkingDirectory};
                
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
                if (!File.Exists(item))
                {
                    return false;
                }
            }
            return true;
        }
        private void Read()
        {
            SessionInfo? t = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(Path.Combine(_path, "session.json"), Encoding.UTF8)) ?? throw new PandoraException("Invalid session info");
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
            File.WriteAllText(Path.Combine(_path,"session.json"), JsonSerializer.Serialize(_sessionInfo),Encoding.UTF8);
        }

        public (string id, string title) GetSessionBasicInfo(string path)
        {
            throw new NotImplementedException();
        }

        public SessionInfo GetSessionInfo(string id)
        {
            throw new NotImplementedException();
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
    }
}
