using System;
using System.Collections.Generic;
using Framework.Abstractions.Storage;

namespace Framework.Adapters.Storage
{
    /// <summary>内存存储，用于测试或临时数据。</summary>
    public sealed class InMemoryStorage : IStorage
    {
        private readonly Dictionary<string, object> _data = new();

        public bool Contains(string key) => _data.ContainsKey(key);

        public StorageResult<T> Load<T>(string key)
        {
            T value;
            if (_data.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return StorageResult<T>.Ok(value);
            }
            value = default;
            return StorageResult<T>.Fail(StorageError.NotFound);
        }

        public bool Save<T>(string key, T value)
        {
            _data[key] = value;
            return true;
        }

        public bool Delete(string key)
        {
            return _data.Remove(key);
        }

        public void Clear(string prefix)
        {
            // 空前缀 = 清空全部
            if (string.IsNullOrEmpty(prefix))
            {
                _data.Clear();
                return;
            }

            List<string> matched = null;
            foreach (var key in _data.Keys)
            {
                if (key != null && key.StartsWith(prefix, StringComparison.Ordinal))
                    (matched ??= new List<string>()).Add(key);
            }

            if (matched == null) return;
            foreach (var key in matched) _data.Remove(key);
        }
    }
}