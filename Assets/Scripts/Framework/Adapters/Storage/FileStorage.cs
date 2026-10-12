using System;
using System.IO;
using System.Text;
using Framework.Abstractions.Storage;
using UnityEngine;

namespace Framework.Adapters.Storage
{
    public sealed class FileStorage : IStorage
    {
        private readonly string _rootDir;
        private readonly string _extension;
        private readonly ISerializer _serializer;
        private readonly object _lock = new object();

        public FileStorage(ISerializer serializer, string rootDir = null)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

            if (string.IsNullOrEmpty(rootDir))
                rootDir = Path.Combine(Application.persistentDataPath, "Storage");

            _rootDir = rootDir;
            _extension = serializer.FileExtension;

            Directory.CreateDirectory(_rootDir);
        }

        public string RootDir => _rootDir;

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return File.Exists(GetFilePath(key));
        }

        public StorageResult<T> Load<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                return StorageResult<T>.Fail(StorageError.NotFound);

            string path = GetFilePath(key);

            lock (_lock)
            {
                if (!File.Exists(path))
                    return StorageResult<T>.Fail(StorageError.NotFound);

                byte[] data;
                try
                {
                    data = File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FileStorage] Read failed: {path}\n{e}");
                    return StorageResult<T>.Fail(StorageError.IOError);
                }

                if (_serializer.TryDeserialize<T>(data, out var value))
                    return StorageResult<T>.Ok(value);

                return StorageResult<T>.Fail(StorageError.DeserializeFailed);
            }
        }

        public bool Save<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) return false;

            byte[] data = _serializer.Serialize(value);
            if (data == null || data.Length == 0)
                return false;

            string path = GetFilePath(key);

            lock (_lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, data);

                    if (File.Exists(path))
                    
                        File.Replace(tmp, path, null);
                    else
                        File.Move(tmp, path);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FileStorage] Save failed: {path}\n{e}");
                    return false;
                }
            }
        }

        public bool Delete(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            string path = GetFilePath(key);

            lock (_lock)
            {
                if (!File.Exists(path)) return false;
                try
                {
                    File.Delete(path);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FileStorage] Delete failed: {path}\n{e}");
                    return false;
                }
            }
        }

        public void Clear(string prefix = null)
        {
            if (!Directory.Exists(_rootDir)) return;

            lock (_lock)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(_rootDir, "*" + _extension, SearchOption.AllDirectories);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FileStorage] Clear enumerate failed.\n{e}");
                    return;
                }

                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (!TryDecodeKey(fileName, out var originalKey)) continue;
                        if (!originalKey.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    }

                    try { File.Delete(file); }
                    catch (Exception e) { Debug.LogWarning($"[FileStorage] Clear skip: {file}\n{e}"); }
                }
            }
        }

        // ---------- 内部工具 ----------

        private string GetFilePath(string key)
            => Path.Combine(_rootDir, EncodeKey(key) + _extension);

        private static string EncodeKey(string key)
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static bool TryDecodeKey(string encoded, out string key)
        {
            key = null;
            try
            {
                string s = encoded.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                    case 1: return false;
                }
                key = Encoding.UTF8.GetString(Convert.FromBase64String(s));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
