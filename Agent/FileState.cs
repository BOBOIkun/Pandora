using Pandora.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Pandora.Agent
{
    public class FileState:IDisposable
    {
        private readonly ConcurrentDictionary<string, ChangeType> _changedFiles = new();
        private readonly ISession _session;
        private readonly ConcurrentDictionary<string, FileStateInfo> _fileState = new();
        public bool Locked=false;
        public int FileCount => _fileState.Count;
        private bool _autoTaskRun=false;
        public Action? OnChanged;
        public void SetLock(string path,long time)
        {
            if(_fileState.TryGetValue(path, out var info))
            {
                info.lockTime = time;
            }
        }
        public string GetChangedFilesStr(bool clear = false)
        {
            string str = string.Join("\n", _changedFiles.Select(x => x.Value switch
            {
                ChangeType.Modified => $"{x.Key} - modified",
                ChangeType.Deleted => $"{x.Key} - deleted",
                _ => ""
            }));
            if (clear)
            {
                _changedFiles.Clear();
            }
            return str;
        }
        public bool FileChangeCheck(string path)
        {
            if (!File.Exists(path))
            {
                _changedFiles[path] = ChangeType.Deleted;
                return true;
            }
            long timestamp = File.GetLastWriteTime(path).ToFileTimeUtc();
            if (timestamp != _fileState[path].versionTimeStamp)
            {
                _changedFiles[path] = ChangeType.Modified;
                return true;
            }
           return false;
        }
        public FileState(ISession session,bool autoFind = false)
        { 
            _session = session;
            if (autoFind)
            {
                _autoTaskRun = true;
                Task.Run(async () =>
                {
                    while (_autoTaskRun)
                    {
                        await Task.Delay(10000);
                        await FindcChangedFiles();
                    }
                });
            }
        }
        public async Task FindcChangedFiles()
        {
            bool changed = false;
            foreach (var file in _fileState)
            {
                changed = changed || FileChangeCheck(file.Key);
            }
            if (changed)
            {
                OnChanged?.Invoke();
            }
        }

        public FileStateStatus GetStatus(string path, bool lineCheck, int startLine = -1, int endLine = -1)
        {
            string full = Utils.GetFullPath(path, _session.AgentEnvironment);
            var info = _fileState.GetOrAdd(full, _ => new FileStateInfo());
            lock (info.@lock)
            {
                long currentTs = File.GetLastWriteTime(full).ToFileTimeUtc();
                if (info.versionTimeStamp != currentTs)
                {
                    if (Locked && info.lockTime == info.versionTimeStamp) { return FileStateStatus.NotChanged; };
                    return FileStateStatus.Changed;
                }
                if (!lineCheck)
                    return FileStateStatus.NotChanged;
                if (info.lines == null)
                    return FileStateStatus.NotChanged;
                List<FileLine> linesCopy = new List<FileLine>(info.lines);
                Merge(linesCopy);
                int index = linesCopy.Count <= 20
                    ? Find(startLine, linesCopy)
                    : FindFast(startLine, linesCopy);

                if (index == -1 || linesCopy[index].endLine < endLine)
                    return FileStateStatus.OK;

                return FileStateStatus.NotChanged;
            }
        }

        public void Update(string path, int startLine = -1, int endLine = -1)
        {
            string full = Utils.GetFullPath(path, _session.AgentEnvironment);
            var info = _fileState.GetOrAdd(full, _ => new FileStateInfo());
            lock (info.@lock)
            {
                long currentTs = File.GetLastWriteTime(full).ToFileTimeUtc();
                if (info.versionTimeStamp != currentTs)
                {
                    info.versionTimeStamp = currentTs;
                    if (startLine == -1)
                    {
                        info.lines = null;
                    }
                    else
                    {
                        info.lines ??= new List<FileLine>();
                        info.lines.Clear();
                        info.lines.Add(new FileLine { startLine = startLine, endLine = endLine });
                    }
                    return;
                }

                if (startLine == -1 || info.lines == null)
                    return;

                Merge(info.lines);
                int index = info.lines.Count <= 20
                    ? Find(startLine, info.lines)
                    : FindFast(startLine, info.lines);

                if (index == -1 || info.lines[index].endLine < endLine)
                {
                    info.lines.Add(new FileLine { startLine = startLine, endLine = endLine });
                }
            }
        }

        public static int Find(int start, List<FileLine> info)
        {
            for (int i = 0; i < info.Count; i++)
            {
                if (info[i].startLine <= start && info[i].endLine >= start)
                    return i;
            }
            return -1;
        }

        public static int FindFast(int start, List<FileLine> info)
        {
            if (info == null || info.Count == 0)
                return -1;
            int left = 0;
            int right = info.Count - 1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (info[mid].startLine <= start && info[mid].endLine >= start)
                    return mid;
                if (info[mid].startLine > start)
                    right = mid - 1;
                else
                    left = mid + 1;
            }
            return -1;
        }

        public static void Merge(List<FileLine> lst)
        {
            lst.Sort((x, y) => x.startLine.CompareTo(y.startLine));
            int slow = 0;
            for (int fast = 1; fast < lst.Count; fast++)
            {
                if (lst[fast].startLine <= lst[slow].endLine)
                    lst[slow].endLine = Math.Max(lst[slow].endLine, lst[fast].endLine);
                else
                {
                    slow++;
                    lst[slow] = lst[fast];
                }
            }
            lst.RemoveRange(slow + 1, lst.Count - slow - 1);
        }

        public void Dispose()
        {
            _autoTaskRun = false;
        }
    }

    public class FileLine
    {
        public int startLine;
        public int endLine;
    }

    public class FileStateInfo
    {
        public long versionTimeStamp;
        public List<FileLine>? lines;
        public readonly object @lock = new();
        public long lockTime=-1;
    }

    public enum FileStateStatus
    {
        NotChanged,
        Changed,
        OK
    }


    public enum ChangeType
    {
        Modified,
        Deleted
    }
}