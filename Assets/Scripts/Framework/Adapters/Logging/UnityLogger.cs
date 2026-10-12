using System;
using Framework.Abstractions.Logging;

namespace Framework.Adapters.Logging
{
    public sealed class UnityLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;

        public void Debug(string message)
        {
            if (Level <= LogLevel.Debug) UnityEngine.Debug.Log(message);
        }

        public void Info(string message)
        {
            if (Level <= LogLevel.Info) UnityEngine.Debug.Log(message);
        }

        public void Warn(string message)
        {
            if (Level <= LogLevel.Warn) UnityEngine.Debug.LogWarning(message);
        }

        public void Error(string message, Exception exception = null)
        {
            if (Level <= LogLevel.Error)
                UnityEngine.Debug.LogError(exception == null ? message : $"{message}\n{exception}");
        }
    }
}