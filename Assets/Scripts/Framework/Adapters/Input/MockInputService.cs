using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Abstractions.Input;
using UnityEngine;

namespace Framework.Adapters.Input
{
    /// <summary>
    /// 可编程的假输入服务，供玩法逻辑测试与原型使用（不依赖任何输入包）。
    /// <para>
    /// 上下文会被记录（<see cref="PushContext"/> / <see cref="PopContext"/> 可查询），
    /// 但**不参与**查询过滤——测试通过直接设定值来控制查询结果，最为直白可预测。
    /// 需要验证「上下文阻断」的行为时，请针对真实的 <c>InputService</c> + 假后端编写用例。
    /// </para>
    /// </summary>
    public sealed class MockInputService : IInputService
    {
        private readonly Dictionary<string, bool> _buttons = new Dictionary<string, bool>();
        private readonly HashSet<string> _down = new HashSet<string>();
        private readonly HashSet<string> _up = new HashSet<string>();
        private readonly Dictionary<string, float> _axes = new Dictionary<string, float>();
        private readonly Dictionary<string, Vector2> _vectors = new Dictionary<string, Vector2>();
        private readonly List<InputContext> _contexts = new List<InputContext>();

        public event Action<InputActionEvent> ActionTriggered;

        public IReadOnlyList<InputContext> ActiveContexts => _contexts;

        // ---------- 上下文（仅记录） ----------

        public void PushContext(string contextId, int priority = 0)
        {
            if (string.IsNullOrEmpty(contextId) || IsContextActive(contextId)) return;
            _contexts.Add(new InputContext(contextId, priority));
        }

        public void PopContext(string contextId)
        {
            for (int i = _contexts.Count - 1; i >= 0; i--)
                if (string.Equals(_contexts[i].ContextId, contextId, StringComparison.Ordinal))
                    _contexts.RemoveAt(i);
        }

        public bool IsContextActive(string contextId)
            => _contexts.Any(c => string.Equals(c.ContextId, contextId, StringComparison.Ordinal));

        // ---------- 查询 ----------

        public bool IsPressed(string actionId, int playerId = 0)
            => _buttons.TryGetValue(actionId, out var pressed) && pressed;

        public bool WasPressedThisFrame(string actionId, int playerId = 0) => _down.Contains(actionId);

        public bool WasReleasedThisFrame(string actionId, int playerId = 0) => _up.Contains(actionId);

        public float GetAxis(string actionId, int playerId = 0)
            => _axes.TryGetValue(actionId, out var value) ? value : 0f;

        public Vector2 GetVector2(string actionId, int playerId = 0)
            => _vectors.TryGetValue(actionId, out var value) ? value : default;

        // ---------- 控制 API（测试/原型用） ----------

        /// <summary>设定按住状态；同时自动置起本帧的 down/up 边缘标记。</summary>
        public void SetButton(string actionId, bool pressed)
        {
            if (string.IsNullOrEmpty(actionId)) return;

            _buttons[actionId] = pressed;

            if (pressed) { _down.Add(actionId); _up.Remove(actionId); }
            else { _up.Add(actionId); _down.Remove(actionId); }
        }

        public void SetAxis(string actionId, float value)
        {
            if (!string.IsNullOrEmpty(actionId)) _axes[actionId] = value;
        }

        public void SetVector2(string actionId, Vector2 value)
        {
            if (!string.IsNullOrEmpty(actionId)) _vectors[actionId] = value;
        }

        /// <summary>清空本帧的 down/up 边缘标记（模拟进入下一帧）。</summary>
        public void ClearFrameFlags()
        {
            _down.Clear();
            _up.Clear();
        }

        /// <summary>手动抛出一次动作触发事件（用于测试事件驱动逻辑）。</summary>
        public void Raise(string actionId, InputTriggerPhase phase = InputTriggerPhase.Performed,
                          float scalar = 0f, Vector2 vector = default, int playerId = 0)
        {
            ActionTriggered?.Invoke(new InputActionEvent(actionId, playerId, phase, scalar, vector));
        }

        public void Dispose()
        {
            _buttons.Clear();
            _down.Clear();
            _up.Clear();
            _axes.Clear();
            _vectors.Clear();
            _contexts.Clear();
            ActionTriggered = null;
        }

        /// <summary>诊断用：当前已记录的状态快照。</summary>
        public override string ToString()
        {
            var sb = new StringBuilder("MockInputService[");
            sb.Append("contexts=").Append(string.Join(",", _contexts.Select(c => c.ToString())));
            sb.Append("; buttons=").Append(string.Join(",", _buttons.Where(kv => kv.Value).Select(kv => kv.Key)));
            sb.Append(']');
            return sb.ToString();
        }
    }
}
