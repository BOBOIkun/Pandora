using OpenAI.Models.Chat;
using Pandora.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pandora.Models
{
    public struct FileAccessInfo
    {
        public bool read = false;
        public bool write = false;
        public FileAccessInfo(bool read, bool write)
        {
            this.read = read;
            this.write = write;
        }
    }
    public class AgentToolParameter
    {
        public required string Name { get; set; }
        public required string Type { get; set; }
        public string? Description { get; set; }
        public bool Required { get; set; }
    }
    public static class AgentParametersType
    {
        public const string STRING = "string";
        public const string INT = "int";
        public const string FLOAT = "float";
        public const string BOOL = "bool";
        //public const string DICT = "dict";
    }
    public enum ToolsResult
    {
        Success,
        UnKnownError,
        ParametersError
    }
    public class AgentTool
    {
        public required string ToolName { get; set; }
        [JsonIgnore]
        public string? ShortDescription { get; set; }
        public required string FullDescription { get; set; }
        [JsonIgnore]
        public bool FullLoad { get; set; } = false;
        [JsonIgnore]
        public bool ParametersTypeCheck { get; set; } = true;
        public required AgentToolParameter[] Parameters { get; set; }
        [JsonIgnore]
        public bool Enabled { get; set; } = true;
        [JsonIgnore]
        public WorkMode SupportedModes { get; set; } = WorkMode.None;
        [JsonIgnore]
        public bool ReadOnly { get; set; } = false;//工具是否只读不写
        [JsonIgnore]
        public bool ParametersStreamOutput { get; set; } = false;//工具是否异步输出参数,用IncrementalJsonFieldParser解析
        [JsonIgnore]
        public Func<ISession, AgentToolParameterValue, (MessageContent? ret, ToolsResult retSatus)> ToolFunction { get; set; } = null!;
    }
    public class AgentToolParameterValue
    {
        private readonly Dictionary<string, object?> _value;

        public int GetInt(string name)
        {
            if (_value.TryGetValue(name, out var value))
            {
                return (int)value!;
            }
            throw new ArgumentNullException(name);
        }
        public string GetString(string name)
        {
            if (_value.TryGetValue(name, out var value))
            {
                return (string)value!;
            }
            throw new ArgumentNullException(name);
        }
        public float GetFloat(string name)
        {
            if (_value.TryGetValue(name, out var value))
            {
                return (float)value!;
            }
            throw new ArgumentNullException(name);
        }
        public bool GetBool(string name)
        {
            if (_value.TryGetValue(name, out var value))
            {
                return (bool)value!;
            }
            throw new ArgumentNullException(name);
        }
        public bool Has(string name)
        {
            return _value.TryGetValue(name, out var v) && v != null;
        }
        public AgentToolParameterValue(JsonObject jobj, AgentToolParameter[] param, bool parametersTypeCheck)
        {
            _value = new Dictionary<string, object?>();
            foreach (var p in param)
            {
                if (!jobj.TryGetPropertyValue(p.Name, out var value) || value is null)
                {
                    if (p.Required)
                    {
                        throw new ArgumentNullException(p.Name);
                    }
                    _value.Add(p.Name, null);
                    continue;
                }

                object? obj;
                if (parametersTypeCheck)
                {
                    // 按声明的类型转换，System.Text.Json 默认不自动将数字转 int/float
                    try
                    {
                        obj = p.Type switch
                        {
                            AgentParametersType.STRING => value.ToString(),
                            AgentParametersType.INT => value.GetValue<int>(),
                            AgentParametersType.FLOAT => value.GetValue<float>(),
                            AgentParametersType.BOOL => (object)value.GetValue<bool>(),
                            _ => value.GetValue<JsonNode?>(),// 回退：直接用 CLR 类型
                        };
                    }
                    catch
                    {
                        throw new Exception($"{p.Name} is not {p.Type}");
                    }
                }
                else
                {
                    // 不做类型检查，原样存 JsonNode
                    obj = value;
                }
                _value.Add(p.Name, obj);
            }
        }
    }
    public struct BashCommandInfo
    {
        public IList<string> commands;
        public IList<string> errorMessages;
    }
}
