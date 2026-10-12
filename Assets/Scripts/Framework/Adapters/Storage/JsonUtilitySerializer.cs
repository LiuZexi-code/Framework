using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Framework.Abstractions.Storage;
using Framework.Adapters.Storage;
using UnityEngine;

namespace Framework.Adapters.Storage
{
    public sealed class JsonUtilitySerializer : ISerializer
    {
        private readonly bool _prettyPrint;
        private readonly Encoding _encoding = new UTF8Encoding(false);

        public JsonUtilitySerializer(bool prettyPrint = true)
        {
            _prettyPrint = prettyPrint;
        }

        public string FileExtension => ".json";

        public byte[] Serialize<T>(T value)
        {
            try
            {
                string json = JsonUtility.ToJson(new Wrapper<T> { value = value }, _prettyPrint);
                return string.IsNullOrEmpty(json) ? null : _encoding.GetBytes(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonUtilitySerializer] Serialize failed: {e}");
                return null;
            }
        }

        public bool TryDeserialize<T>(byte[] data, out T value)
        {
            value = default;
            if (data == null || data.Length == 0) return false;

            try
            {
                string json = _encoding.GetString(data);
                var wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
                if (wrapper == null) return false;
                value = wrapper.value;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonUtilitySerializer] Deserialize failed: {e}");
                return false;
            }
        }

        [Serializable]
        private class Wrapper<T> { public T value; }
    }
}
