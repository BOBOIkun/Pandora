using Pandora.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Pandora.Agent.Tools
{
    public class FileState(ISession session)
    {
        private readonly ConcurrentDictionary<string, ChangeType> _changedFiles = new();
        private readonly ISession _session = session;
        private readonly ConcurrentDictionary<string, FileStateInfo> _fileState = new();
        private readonly ConcurrentDictionary<string, object> _locks = new();

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

        public async Task FindcChangedFiles()
        {
            foreach (var file in _fileState)
            {
                if (File.Exists(file.Key))
                {
                    _changedFiles[file.Key]= ChangeType.Deleted;
                    continue;
                }
                if (File.GetLastWriteTime(file.Key).ToFileTimeUtc() != file.Value.versionTimeStamp)
                {
                    _changedFiles[file.Key] = ChangeType.Modified;
                }
            }
        }

        public FileStateStatus GetStatus(string path, int startLine = -1, int endLine = -1)
        {
            string full = Utils.GetFullPath(path, _session.AgentEnvironment);
            var fileLock = _locks.GetOrAdd(full, _ => new object());

            lock (fileLock)
            {
                if (_fileState.TryGetValue(full, out FileStateInfo? state) && state != null)
                {
                    FileStateInfo info = state;
                    long currentTs = File.GetLastWriteTime(full).ToFileTimeUtc();
                    if (info.versionTimeStamp != currentTs)
                        return FileStateStatus.Changed;

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

                return FileStateStatus.OK;
            }
        }

        public void Update(string path, int startLine = -1, int endLine = -1)
        {
            string full = Utils.GetFullPath(path, _session.AgentEnvironment);
            var fileLock = _locks.GetOrAdd(full, _ => new object());

            lock (fileLock)
            {
                if (_fileState.TryGetValue(full, out FileStateInfo? state) && state != null)
                {
                    FileStateInfo info = state;
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

                    if (info.lines == null)
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
                else
                {
                    var newInfo = new FileStateInfo
                    {
                        versionTimeStamp = File.GetLastWriteTime(full).ToFileTimeUtc(),
                        lines = startLine == -1
                            ? null
                            : new List<FileLine> { new() { startLine = startLine, endLine = endLine } }
                    };
                    _fileState[full] = newInfo;
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