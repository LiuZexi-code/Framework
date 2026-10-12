using System;

namespace Framework.Abstractions.Events
{
    /// <summary>事件订阅句柄，Dispose 即取消订阅。</summary>
    public interface IEventSubscription : IDisposable
    {
        bool IsDisposed { get; }
    }
}