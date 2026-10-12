using System;

namespace Framework.Abstractions.Logging
{
    public interface ILogger
    {
        LogLevel Level { get; set; }

        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception = null);
    }
}