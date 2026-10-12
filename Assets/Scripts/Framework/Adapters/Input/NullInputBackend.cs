using System;
using System.Collections.Generic;
using Framework.Abstractions.Input;
using Framework.Abstractions.Logging;

namespace Framework.Adapters.Input
{
    /// <summary>
    /// 空输入后端：所有查询恒为 false / 0。
    /// <para>
    /// 作为框架的**默认后端**注册，保证 <c>IInputService</c> 永远可解析（玩法代码无需判空）。
    /// 业务用更大的 <c>Order</c> 覆盖 <c>IInputBackend</c> 注册即可换成真实后端——
    /// 覆盖后本类**不会被构造**，因此不会产生告警。
    /// </para>
    /// </summary>
    public sealed class NullInputBackend : IInputBackend
    {
        private readonly ILogger _logger;
        private bool _warned;

        public NullInputBackend(ILogger logger = null)
        {
            _logger = logger;
        }

        /// <summary>空后端永不触发事件（显式空实现，避免 CS0067 警告）。</summary>
        public event Action<InputActionEvent> ActionTriggered
        {
            add { }
            remove { }
        }

        public void Initialize() => WarnOnce();

        public void ApplyContexts(IReadOnlyList<InputContext> activeContexts) { }

        public void Tick(float deltaTime) { }

        public bool ReadButton(string actionId, int playerId = 0) => false;
        public bool ReadButtonDown(string actionId, int playerId = 0) => false;
        public bool ReadButtonUp(string actionId, int playerId = 0) => false;
        public float ReadAxis(string actionId, int playerId = 0) => 0f;
        public UnityEngine.Vector2 ReadVector2(string actionId, int playerId = 0) => default;

        public void Dispose() { }

        private void WarnOnce()
        {
            if (_warned) return;
            _warned = true;

            const string message =
                "[Input] 当前使用的是空输入后端（所有输入恒为 false）。若项目需要真实输入，" +
                "请注册一个 IInputBackend 实现（如 InputSystemBackend），Order 大于 10 即可覆盖默认注册。";

            if (_logger != null) _logger.Warn(message);
            else UnityEngine.Debug.LogWarning(message);
        }
    }
}
