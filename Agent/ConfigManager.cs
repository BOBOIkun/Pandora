using Pandora.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Pandora.Agent
{
    public class ConfigManager : IConfigManager
    {
        private readonly string _configPath;
        private readonly Dictionary<string, JsonElement> _config = [];
        private readonly ReaderWriterLockSlim _rwLock = new();

        public ConfigManager()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(basePath, "config");
            _configPath = Path.Combine(configDir, "config.json");
            Directory.CreateDirectory(configDir);
            Load();
        }

        public T? GetValue<T>(string name)
        {
            _rwLock.EnterReadLock();
            try
            {
                if (_config.TryGetValue(name, out var element))
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText());
                }
                return default;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public T GetValue<T>(string name, T defaultValue)
        {
            _rwLock.EnterReadLock();
            try
            {
                if (_config.TryGetValue(name, out var element))
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText()) ?? defaultValue;
                }
                return defaultValue;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public void SetValue<T>(string name, T value)
        {
            _rwLock.EnterWriteLock();
            try
            {
                _config[name] = JsonSerializer.SerializeToElement(value);
                Save();
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public bool Remove(string name)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (_config.Remove(name))
                {
                    Save();
                    return true;
                }
                return false;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        private void Save()
        {
            using var stream = File.Create(_configPath);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var kvp in _config)
            {
                writer.WritePropertyName(kvp.Key);
                kvp.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        private void Load()
        {
            if (!File.Exists(_configPath))
            {
                Save();
                return;
            }

            string json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            _config.Clear();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                _config[prop.Name] = prop.Value.Clone();
            }
        }
    }
}
