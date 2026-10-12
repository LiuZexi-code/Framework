using System;
using System.Collections.Generic;
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;
using Framework.Adapters.Logging;

namespace Framework.Adapters.Events
{
    public sealed class EventBus : IEventBus, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly Dictionary<Type, List<Delegate>> _pendingAdd = new();
        private readonly Dictionary<Type, List<Delegate>> _pendingRemove = new();
        private bool _iterating;

        /// <param name="logger">未注入时回退到静态门面 <c>Log</c>（兼容手动 new 的场景）。</param>
        public EventBus(ILogger logger = null)
        {
            _logger = logger;
        }

        public void Publish<T>(T evt)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                return;

            _iterating = true;
            // 复制一份，避免订阅者修改列表
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    ((Action<T>)snapshot[i]).Invoke(evt);
                }
                catch (Exception e)
                {
                    // 单个订阅者异常不影响其他订阅者
                    ReportError($"[EventBus] Handler failed for {typeof(T).Name}", e);
                }
            }
            _iterating = false;
            Flush();
        }

        public IEventSubscription Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var type = typeof(T);
            if (_iterating)
            {
                if (!_pendingAdd.TryGetValue(type, out var addList))
                    _pendingAdd[type] = addList = new List<Delegate>();
                addList.Add(handler);
            }
            else
            {
                if (!_handlers.TryGetValue(type, out var list))
                    _handlers[type] = list = new List<Delegate>();
                list.Add(handler);
            }

            return new Subscription(() => RemoveHandler(type, handler));
        }

        private void RemoveHandler(Type type, Delegate handler)
        {
            if (_iterating)
            {
                if (!_pendingRemove.TryGetValue(type, out var removeList))
                    _pendingRemove[type] = removeList = new List<Delegate>();
                removeList.Add(handler);
            }
            else
            {
                if (_handlers.TryGetValue(type, out var list))
                    list.Remove(handler);
            }
        }

        private void Flush()
        {
            foreach (var kv in _pendingRemove)
            {
                if (_handlers.TryGetValue(kv.Key, out var list))
                    foreach (var d in kv.Value) list.Remove(d);
            }
            _pendingRemove.Clear();

            foreach (var kv in _pendingAdd)
            {
                if (!_handlers.TryGetValue(kv.Key, out var list))
                    _handlers[kv.Key] = list = new List<Delegate>();
                list.AddRange(kv.Value);
            }
            _pendingAdd.Clear();
        }

        public void Clear()
        {
            _handlers.Clear();
            _pendingAdd.Clear();
            _pendingRemove.Clear();
        }

        private void ReportError(string message, Exception e)
        {
            if (_logger != null) _logger.Error(message, e);
            else Log.Error(message, e);   // 兜底：未注入日志时用静态门面
        }

        public void Dispose() => Clear();

        private sealed class Subscription : IEventSubscription
        {
            private Action _unsubscribe;
            public bool IsDisposed { get; private set; }

            public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _unsubscribe?.Invoke();
                _unsubscribe = null;
            }
        }
    }
}