using System;
using System.Text;
using Framework.Abstractions.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Framework.Adapters
{


    public sealed class NewtonsoftJsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Encoding _encoding = new UTF8Encoding(false);

        public NewtonsoftJsonSerializer() : this(null) { }

        public NewtonsoftJsonSerializer(JsonSerializerSettings settings)
        {
            _settings = settings ?? CreateDefaultSettings();
        }

        public string FileExtension => ".json";

        public byte[] Serialize<T>(T value)
        {
            try
            {
                string json = JsonConvert.SerializeObject(value, _settings);
                if (string.IsNullOrEmpty(json)) return null;
                return _encoding.GetBytes(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewtonsoftJsonSerializer] Serialize<{typeof(T).Name}> failed: {e}");
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
                value = JsonConvert.DeserializeObject<T>(json, _settings);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewtonsoftJsonSerializer] Deserialize<{typeof(T).Name}> failed: {e}");
                return false;
            }
        }

        // ---------------- 默认设置 ----------------

        private static JsonSerializerSettings CreateDefaultSettings()
        {
            var settings = new JsonSerializerSettings
            {
                // 输出：缩进、UTF-8
                Formatting = Formatting.Indented,

                // null 字段是否写进 JSON
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include,

                // 循环引用：忽略而不是抛异常（Unity 对象常有引用回环）
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,

                // 反序列化时忽略 JSON 里多出来的字段（版本升级友好）
                MissingMemberHandling = MissingMemberHandling.Ignore,

                // 未知类型（多态）尽量推断
                TypeNameHandling = TypeNameHandling.None,   // 见下方“安全性”说明

                // 日期格式统一 ISO8601
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,

                // 私有字段也参与序列化（可选，看你数据类习惯）
                ContractResolver = new DefaultContractResolver
                {
                    // 若想序列化私有字段：把下面 NamingStrategy 换成下面注释那种
                    // SerializeCompilerGeneratedMembers = true,
                },

                // Unity 特殊类型转换器
                Converters =
                {
                    new Vector2Converter(),
                    new Vector3Converter(),
                    new Vector4Converter(),
                    new QuaternionConverter(),
                    new ColorConverter(),
                    new Color32Converter(),
                    new RectConverter(),
                    new BoundsConverter(),
                },

                // 出错时不要静默吞异常，方便排查
                Error = (sender, args) =>
                {
                    Debug.LogError($"[NewtonsoftJsonSerializer] Error at '{args.ErrorContext.Path}': {args.ErrorContext.Error.Message}");
                    args.ErrorContext.Handled = false;
                }
            };

            return settings;
        }
    }
}
