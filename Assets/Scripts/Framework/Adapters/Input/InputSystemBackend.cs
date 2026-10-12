using System;
using System.Collections.Generic;
using Framework.Abstractions.Input;
using Framework.Abstractions.Logging;
using UnityEngine.InputSystem;

namespace Framework.Adapters.Input
{
    /// <summary>
    /// 基于 Unity Input System 的输入后端。
    /// <para>
    /// 映射约定：
    /// <list type="bullet">
    /// <item><description><c>contextId</c> → <c>InputActionMap</c> 名（默认同名，可用 <c>contextMap</c> 覆盖）。</description></item>
    /// <item><description><c>actionId</c> → <c>InputAction</c> 名（在当前启用的 map 中查找）。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 绑定资源通过 <see cref="Attach"/> 提供（可在加载管线里异步载入后再接上），
    /// 因此本类在「尚未有 <c>InputActionAsset</c>」时也可以安全构造与初始化。
    /// </para>
    /// <para>
    /// 单玩家：<c>playerId</c> 统一透传为 0。多玩家设备分配（PlayerInput / InputUser）留待后续扩展。
    /// </para>
    /// </summary>
    public sealed class InputSystemBackend : IInputBackend
    {
        private const int SinglePlayerId = 0;

        private readonly ILogger _logger;
        private readonly Dictionary<string, string> _contextMap;
        private readonly Dictionary<string, InputAction> _lookup = new Dictionary<string, InputAction>();
        private readonly List<InputAction> _subscribed = new List<InputAction>();
        private readonly List<InputContext> _lastContexts = new List<InputContext>();
        private readonly List<string> _enabledMaps = new List<string>();

        private readonly Action<InputAction.CallbackContext> _onStarted;
        private readonly Action<InputAction.CallbackContext> _onPerformed;
        private readonly Action<InputAction.CallbackContext> _onCanceled;

        private InputActionAsset _asset;
        private IDisposable _assetHandle;
        private bool _initialized;
        private bool _disposed;

        /// <param name="contextMap">可选：<c>contextId → InputActionMap 名</c> 映射；未命中时按同名处理。</param>
        /// <param name="logger">可选日志。</param>
        public InputSystemBackend(IReadOnlyDictionary<string, string> contextMap = null, ILogger logger = null)
        {
            _logger = logger;
            _contextMap = new Dictionary<string, string>();
            if (contextMap != null)
                foreach (var kv in contextMap)
                    if (!string.IsNullOrEmpty(kv.Key)) _contextMap[kv.Key] = kv.Value;

            _onStarted = ctx => Raise(ctx, InputTriggerPhase.Started);
            _onPerformed = ctx => Raise(ctx, InputTriggerPhase.Performed);
            _onCanceled = ctx => Raise(ctx, InputTriggerPhase.Canceled);
        }

        public event Action<InputActionEvent> ActionTriggered;

        /// <summary>是否已接入绑定资源。</summary>
        public bool HasAsset => _asset != null;

        /// <summary>
        /// 当前**可解析**的动作数量（诊断用）。为 0 表示所有 ActionMap 都被上下文阻断——
        /// 无需真实设备即可断言上下文阻断是否生效。
        /// </summary>
        public int ActiveActionCount => _lookup.Count;

        /// <summary>当前已启用的 ActionMap 名（诊断用）。</summary>
        public IReadOnlyList<string> EnabledMapNames => _enabledMaps;

        /// <summary>
        /// 接入绑定资源。<paramref name="handle"/>（如 Addressables 句柄）非空时由本后端负责释放，
        /// 避免句柄泄漏。
        /// </summary>
        public void Attach(InputActionAsset asset, IDisposable handle = null)
        {
            if (_disposed) { handle?.Dispose(); return; }
            if (ReferenceEquals(_asset, asset)) { handle?.Dispose(); return; }

            ReleaseAsset();

            _asset = asset;
            _assetHandle = handle;

            if (_asset == null) return;

            // 初始全部禁用，交由 ApplyContexts 决定启用哪些
            foreach (var map in _asset.actionMaps)
                if (map != null) map.Disable();

            Subscribe();

            if (_initialized) ApplyMaps();

            _logger?.Info($"[Input] InputSystem 后端已接入绑定资源：{_asset.name}（{_asset.actionMaps.Count} 个 ActionMap）");
        }

        public void Initialize()
        {
            if (_disposed || _initialized) return;
            _initialized = true;

            if (_asset == null)
            {
                _logger?.Info("[Input] InputSystem 后端已就绪，等待 InputActionAsset（通过 Attach 提供）");
                return;
            }

            Subscribe();
            ApplyMaps();
        }

        public void ApplyContexts(IReadOnlyList<InputContext> activeContexts)
        {
            _lastContexts.Clear();
            if (activeContexts != null)
                for (int i = 0; i < activeContexts.Count; i++)
                    _lastContexts.Add(activeContexts[i]);

            if (_initialized) ApplyMaps();
        }

        /// <summary>
        /// 默认由引擎逐帧更新驱动，无需手动调用。
        /// 仅当项目把 Input System 切到手动更新模式（<c>ProcessEventsManually</c>）时才需要实现真实的刷新。
        /// </summary>
        public void Tick(float deltaTime) { }

        public bool ReadButton(string actionId, int playerId = 0) => Find(actionId)?.IsPressed() ?? false;

        public bool ReadButtonDown(string actionId, int playerId = 0) => Find(actionId)?.WasPressedThisFrame() ?? false;

        public bool ReadButtonUp(string actionId, int playerId = 0) => Find(actionId)?.WasReleasedThisFrame() ?? false;

        /// <summary>
        /// 读取标量。对非标量动作（如 Vector2 的 Move）返回 0——
        /// <c>InputAction.ReadValue&lt;float&gt;()</c> 在值类型不匹配时会**抛异常**，必须先判类型。
        /// </summary>
        public float ReadAxis(string actionId, int playerId = 0)
        {
            TryReadValue(Find(actionId), out var scalar, out _);
            return scalar;
        }

        public UnityEngine.Vector2 ReadVector2(string actionId, int playerId = 0)
        {
            TryReadValue(Find(actionId), out _, out var vector);
            return vector;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseAsset();
            _lookup.Clear();
            _lastContexts.Clear();
            ActionTriggered = null;
        }

        // ---------- 内部 ----------

        private void Subscribe()
        {
            if (_asset == null) return;

            foreach (var map in _asset.actionMaps)
            {
                if (map == null) continue;
                foreach (var action in map.actions)
                {
                    if (action == null) continue;
                    action.started += _onStarted;
                    action.performed += _onPerformed;
                    action.canceled += _onCanceled;
                    _subscribed.Add(action);
                }
            }
        }

        private void Unsubscribe()
        {
            foreach (var action in _subscribed)
            {
                if (action == null) continue;
                action.started -= _onStarted;
                action.performed -= _onPerformed;
                action.canceled -= _onCanceled;
            }
            _subscribed.Clear();
        }

        private void ReleaseAsset()
        {
            Unsubscribe();

            if (_asset != null)
                foreach (var map in _asset.actionMaps)
                    if (map != null) map.Disable();

            _asset = null;
            _lookup.Clear();
            _enabledMaps.Clear();

            var handle = _assetHandle;
            _assetHandle = null;
            handle?.Dispose();
        }

        /// <summary>
        /// 依据 <see cref="_lastContexts"/> 启停 ActionMap，并重建 actionId → InputAction 查找表。
        /// <para>空上下文集合 = 不限制 → 启用全部 map。</para>
        /// </summary>
        private void ApplyMaps()
        {
            _lookup.Clear();
            _enabledMaps.Clear();

            if (_asset == null) return;

            bool unrestricted = _lastContexts.Count == 0;

            foreach (var map in _asset.actionMaps)
            {
                if (map == null) continue;

                bool enable = unrestricted || IsWanted(map.name);

                if (enable) map.Enable();
                else map.Disable();

                // 只把「启用中的 map」放进查找表 → 被上下文阻断的动作查询不到，天然返回默认值
                if (!enable) continue;

                _enabledMaps.Add(map.name);

                foreach (var action in map.actions)
                {
                    if (action == null || string.IsNullOrEmpty(action.name)) continue;
                    if (!_lookup.ContainsKey(action.name)) _lookup[action.name] = action;
                }
            }
        }

        private bool IsWanted(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return false;

            for (int i = 0; i < _lastContexts.Count; i++)
            {
                var id = _lastContexts[i].ContextId;
                if (string.IsNullOrEmpty(id)) continue;

                var mapped = _contextMap.TryGetValue(id, out var name) ? name : id;
                if (string.Equals(mapped, mapName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private InputAction Find(string actionId)
        {
            if (_disposed || string.IsNullOrEmpty(actionId)) return null;
            return _lookup.TryGetValue(actionId, out var action) ? action : null;
        }

        /// <summary>
        /// 是否为「标量通道」的值类型。
        /// <para>
        /// 🔴 **公开以便单测**：这是修 <c>ReadValue&lt;T&gt;()</c> 类型不匹配抛异常的关键判据——
        /// 对 <c>Vector2</c> 动作调用 <c>ReadValue&lt;float&gt;()</c> 会抛
        /// <c>InvalidOperationException: Cannot read value of type 'Single' from composite ...</c>。
        /// </para>
        /// </summary>
        public static bool IsScalarValueType(Type valueType)
            => valueType == typeof(float) || valueType == typeof(double);

        /// <summary>是否为「二维向量通道」的值类型（<c>Vector3</c> 取其 xy）。</summary>
        public static bool IsVector2ValueType(Type valueType)
            => valueType == typeof(UnityEngine.Vector2) || valueType == typeof(UnityEngine.Vector3);

        /// <summary>
        /// 按动作的**实际值类型**读取，避免 <c>ReadValue&lt;T&gt;()</c> 类型不匹配抛异常。
        /// 返回 false 表示「读不到」（动作不存在 / 未启用 / 类型不支持），此时 scalar=0、vector=(0,0)。
        /// </summary>
        private bool TryReadValue(InputAction action, out float scalar, out UnityEngine.Vector2 vector)
        {
            scalar = 0f;
            vector = default;

            if (_disposed || action == null) return false;

            var valueType = action.activeValueType;
            if (valueType == null) return false;

            try
            {
                if (IsScalarValueType(valueType))
                {
                    scalar = valueType == typeof(double)
                        ? (float)action.ReadValue<double>()
                        : action.ReadValue<float>();
                    return true;
                }
                if (IsVector2ValueType(valueType))
                {
                    if (valueType == typeof(UnityEngine.Vector3))
                    {
                        var v3 = action.ReadValue<UnityEngine.Vector3>();
                        vector = new UnityEngine.Vector2(v3.x, v3.y);
                    }
                    else
                    {
                        vector = action.ReadValue<UnityEngine.Vector2>();
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                _logger?.Debug($"[Input] 读取动作值失败（{action.name}，{valueType.Name}）：{e.Message}");
            }

            return false;
        }

        private void Raise(InputAction.CallbackContext ctx, InputTriggerPhase phase)
        {
            if (_disposed) return;

            var action = ctx.action;
            if (action == null) return;

            TryReadValue(action, out var scalar, out var vector);

            ActionTriggered?.Invoke(new InputActionEvent(action.name, SinglePlayerId, phase, scalar, vector));
        }
    }
}
