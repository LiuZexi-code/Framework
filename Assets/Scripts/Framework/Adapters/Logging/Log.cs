using System;
using Framework.Abstractions.Logging;

namespace Framework.Adapters.Logging
{
    /// <summary>
    /// 静态日志门面。仅在无法使用依赖注入的地方使用，例如纯静态工具类。
    /// 业务代码优先构造函数注入 ILogger。
    /// </summary>
    public static class Log
    {

        private static ILogger _logger = NullLogger.Instance;
        private static ILogger Logger
        {
            get
            {
                return _logger;
            }
        }
        private static bool _initialized = false;

        public static void Init(ILogger logger)
        {
            // 已初始化则忽略重复调用，避免覆盖宿主已注入的日志实现
            if (_initialized) return;

            if (logger == null)
            {
                UnityEngine.Debug.LogWarning("[Log] Init(null) 被忽略，仍使用 NullLogger");
                return;
            }

            _logger = logger;
            _initialized = true;
        }

        public static void Debug(string message)
        {
            Logger.Debug(message);
        }
        public static void Info(string message)
        {
            Logger.Info(message);
        }
        public static void Warn(string message)
        {
            Logger.Warn(message);
        }
        public static void Error(string message, Exception exception = null)
        {
            Logger.Error(message, exception);
        }

        public static void Clear()
        {
            // 复位（含 _initialized），以便下次 Init 能重新注入
            _logger = NullLogger.Instance;
            _initialized = false;
        }
    }
}