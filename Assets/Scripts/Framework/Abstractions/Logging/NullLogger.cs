using System;

namespace Framework.Abstractions.Logging
{
    /// <summary>空实现，用于测试或关闭日志。</summary>
    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        public LogLevel Level { get; set; } = LogLevel.None;

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception exception = null) { }
    }
}