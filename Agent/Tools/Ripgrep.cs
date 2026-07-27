using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pandora.Agent.Tools
{
    public enum RipgrepEventType
    {
        Start,
        End,
        Summary,
        Unknown,
        Match
    }
    public class Ripgrep
    {
        public string _rgPath;
        public Ripgrep(string rgPath)
        {
            if (!File.Exists(rgPath))
            {
                throw new PandoraException(ErrorCode.RipgrepNotFound);
            }
            _rgPath = rgPath;
        }
        private static string BuildArgumentList(string pattern, string path,RipgrepOption option)
        {
            string ic = option.IgnoreCase ? "--ignore-case" : "";
            string fs = option.FixedStrings ? "--fixed-strings" : "";
            return $"--json --line-buffered --max-count {option.MaxCount} --max-filesize 10M --encoding utf-8 {ic} {fs} {EscapeArg(pattern)} {EscapeArg(path)}";
        }
        private static string EscapeArg(string arg)
        {
            return "\"" + arg.Replace("\"", "\"\"") + "\"";
        }
        public IEnumerable<RipgrepMatchItem> Search(string pattern, string path, RipgrepOption option)
        {
            Process process = new Process();
            process.StartInfo = new ProcessStartInfo()
            {
                WorkingDirectory = path,
                FileName = _rgPath,
                Arguments = BuildArgumentList(pattern,path, option),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            process.Start();
            while (!process.StandardOutput.EndOfStream)
            {
                string? line = process.StandardOutput.ReadLine();
                if (line == null) continue;
                RipgrepRet? root = null;
                try
                {
                    if (GetEventType(line) == RipgrepEventType.Match)
                    {
                        root = JsonSerializer.Deserialize<RipgrepRet>(line)!;
                        if (root.Data.Lines.Bytes!=null || root.Data.Path.Bytes != null)
                        {
                            continue;
                        }
                    }
                }
                catch { }
                if (root != null)
                {
                    if (option.ShortFileName)
                    {
                        root.Data.Path.Text = root.Data.Path.Text!.Replace(path,"~");
                    }
                    yield return root.Data;
                }
            }
            process.WaitForExitAsync().Wait(8000);
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            process.Dispose();
        }
        private static RipgrepEventType GetEventType(byte[] line)
        {
            Utf8JsonReader reader = new Utf8JsonReader(line);
            RipgrepEventType ripgrepEventType = RipgrepEventType.Unknown;
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return ripgrepEventType;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                string prop = reader.GetString()!;
                if (prop == "type")
                {
                    // 读取 type 对应字符串值
                    reader.Read();
                    string type = reader.GetString()!;
                    ripgrepEventType = type switch
                    {
                        "start" => RipgrepEventType.Start,
                        "end" => RipgrepEventType.End,
                        "summary" => RipgrepEventType.Summary,
                        "match" => RipgrepEventType.Match,
                        _ => RipgrepEventType.Unknown,
                    };
                    return ripgrepEventType;
                }
                reader.Skip();
            }
            return ripgrepEventType;
        }
        private RipgrepEventType GetEventType(string line) 
        {
            return GetEventType(Encoding.UTF8.GetBytes(line));
        }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
    public class RipgrepTextChuck 
    {
        [JsonPropertyName("bytes")]
        public string? Bytes { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
    public class RipgrepOption
    {
        public int MaxDepth { get; set; } = 99999999;
        public int MaxColumns { get; set; } = 800;
        public bool FixedStrings { get; set; } = false;
        public bool IgnoreCase { get; set; } = false;
        public bool ShortFileName { get; set; } = false;
        public int MaxCount { get; set; } = 9999;
    }
    public class RipgrepMatchItem
    {
        [JsonPropertyName("path")]
        public required RipgrepTextChuck Path { get; set; }
        [JsonPropertyName("lines")]
        public required RipgrepTextChuck Lines { get; set; }
        [JsonPropertyName("line_number")]
        public int LineNumber { get; set; }
    }

    public class RipgrepRet
    {
        [JsonPropertyName("data")]
        public required RipgrepMatchItem Data { get; set; }
    }

}
