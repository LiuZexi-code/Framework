using System;
using System.Collections.Generic;
using Framework.Abstractions.Input;
using Framework.Abstractions.Logging;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Adapters.Input
{
    /// <summary>
    /// 默认输入服务实现：上下文栈 + 查询/事件转发。
    /// <para>
    /// <b>上下文语义（有效集 = 优先级最高的那一批）</b>：同一时刻只有优先级最高的上下文「有效」，
    /// 同优先级可并存（并集）。这样「Push UI(高优先) → Gameplay 输入被阻断」自然成立；
    /// Push/Pop 后自动回落到次高优先级。空栈 = 不限制（后端放行全部）。
    /// </para>
    /// <para>
    /// 相同 <c>contextId</c> 可重复 Push（引用计数），需等次数 Pop 才真正移除。
    /// </para>
    /// </summary>
    public sealed class InputService : IInputService
    {
        private sealed class ContextEntry
        {
            public string Id;
            public int Priority;
            public int Count;
        }

        private readonly IInputBackend _backend;
        private readonly ILogger _logger;
        private readonly Action<InputActionEvent> _onBackendTriggered;

        private readonly List<ContextEntry> _stack = new List<ContextEntry>();
        private readonly List<InputContext> _effective = new List<InputContext>();
        private InputContext[] _applied = Array.Empty<InputContext>();

        private bool _disposed;

        public InputService(IInputBackend backend, ILogger logger = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger;

            _onBackendTriggered = RaiseTriggered;
            _backend.ActionTriggered += _onBackendTriggered;
            _backend.Initialize();

            Recompute();
            Apply(force: true);   // 初始（空栈）也要通知后端一次：空 = 不限制
        }

        public event Action<InputActionEvent> ActionTriggered;

        public IReadOnlyList<InputContext> ActiveContexts => _effective;

        // ---------- 上下文 ----------

        public void PushContext(string contextId, int priority = 0)
        {
            if (string.IsNullOrEmpty(contextId))
            {
                Warn("PushContext 收到空的 contextId，已忽略");
                return;
            }

            var entry = Find(contextId);
            if (entry != null)
            {
                entry.Count++;   // 同名重复推入：引用计数
            }
            else
            {
                _stack.Add(new ContextEntry { Id = contextId, Priority = priority, Count = 1 });
            }

            Recompute();
            Apply(force: false);
        }

        public void PopContext(string contextId)
        {
            var entry = Find(contextId);
            if (entry == null)
            {
                Warn($"PopContext(\"{contextId}\")：该上下文当前未推入，已忽略");
                return;
            }

            if (--entry.Count <= 0) _stack.Remove(entry);

            Recompute();
            Apply(force: false);
        }

        public bool IsContextActive(string contextId)
        {
            if (string.IsNullOrEmpty(contextId)) return false;
            for (int i = 0; i < _effective.Count; i++)
                if (string.Equals(_effective[i].ContextId, contextId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ---------- 查询转发 ----------

        public bool IsPressed(string actionId, int playerId = 0)
            => !_disposed && _backend.ReadButton(actionId, playerId);

        public bool WasPressedThisFrame(string actionId, int playerId = 0)
            => !_disposed && _backend.ReadButtonDown(actionId, playerId);

        public bool WasReleasedThisFrame(string actionId, int playerId = 0)
            => !_disposed && _backend.ReadButtonUp(actionId, playerId);

        public float GetAxis(string actionId, int playerId = 0)
            => _disposed ? 0f : _backend.ReadAxis(actionId, playerId);

        public Vector2 GetVector2(string actionId, int playerId = 0)
            => _disposed ? default : _backend.ReadVector2(actionId, playerId);

        // ---------- 释放 ----------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_backend != null)
            {
                _backend.ActionTriggered -= _onBackendTriggered;
                _backend.Dispose();   // 后端 Dispose 需幂等（容器可能也会释放）
            }

            _stack.Clear();
            _effective.Clear();
            _applied = Array.Empty<InputContext>();
            ActionTriggered = null;
        }

        // ---------- 内部 ----------

        private ContextEntry Find(string contextId)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (string.Equals(_stack[i].Id, contextId, StringComparison.Ordinal))
                    return _stack[i];
            return null;
        }

        /// <summary>重算有效集：取优先级最高的那一批（保持推入顺序）。</summary>
        private void Recompute()
        {
            _effective.Clear();
            if (_stack.Count == 0) return;

            int max = int.MinValue;
            for (int i = 0; i < _stack.Count; i++)
                if (_stack[i].Priority > max) max = _stack[i].Priority;

            for (int i = 0; i < _stack.Count; i++)
                if (_stack[i].Priority == max)
                    _effective.Add(new InputContext(_stack[i].Id, _stack[i].Priority));
        }

        /// <summary>有效集变化时才通知后端（避免无谓的 Enable/Disable 抖动）。</summary>
        private void Apply(bool force)
        {
            if (!force && SameSet(_effective, _applied)) return;

            // 传快照数组：后端不得长期持有调用方复用的列表
            _applied = _effective.ToArray();
            _backend.ApplyContexts(_applied);
        }

        private static bool SameSet(List<InputContext> a, InputContext[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].ContextId, b[i].ContextId, StringComparison.Ordinal)) return false;
                if (a[i].Priority != b[i].Priority) return false;
            }
            return true;
        }

        private void RaiseTriggered(InputActionEvent e)
        {
            var handler = ActionTriggered;
            if (handler == null) return;

            // 单个订阅者异常不影响其它订阅者（与 EventBus 一致）
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<InputActionEvent>)d).Invoke(e); }
                catch (Exception ex) { Warn($"ActionTriggered 订阅者抛出异常：{ex}"); }
            }
        }

        private void Warn(string message) => _logger?.Warn($"[Input] {message}");
    }
}
