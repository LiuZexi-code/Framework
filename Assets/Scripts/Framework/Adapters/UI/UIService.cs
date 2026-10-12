using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;
using Framework.Abstractions.Scenes;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;

namespace Framework.UI
{
    public sealed class UIService : IUIService
    {
        private readonly Dictionary<Type, PanelDefinition> _definitions = new();   // key = 面板脚本类型(PanelType)
        private readonly Dictionary<Type, IUIPanelLifecycle> _instances = new();    // key = 面板脚本类型(PanelType)
        private readonly List<IUIPanelLifecycle> _openOrder = new();
        private IPanelFactory _factory;
        private IEventBus _eventBus;
        private ILogger _logger;
        private readonly ISceneService _sceneService;
        private readonly bool _enforceSceneTags;

        public UIService(IPanelFactory factory,IEventBus eventBus, ILogger logger,
                         ISceneService sceneService = null, UISettings settings = null)
        {
            _factory = factory;
            _eventBus = eventBus;
            _logger = logger;
            _sceneService = sceneService;
            _enforceSceneTags = settings?.EnforceSceneTags ?? true;
        }

        public void Register(PanelDefinition definition)
        {
            if (definition?.PanelType == null)
            {
                _logger?.Warn("[UIService] Register 收到无效 definition（PanelType 为空）");
                return;
            }
            _definitions[definition.PanelType] = definition;
        }

        public void Unregister(Type panelType)
        {
            if (panelType == null) return;
            _definitions.Remove(panelType);
        }

        /// <summary>支持按接口类型查找定义（如 ILoadingPanel → LoadingPanel）。</summary>
        private bool TryGetDefinition(Type type, out PanelDefinition definition)
        {
            if (_definitions.TryGetValue(type, out definition)) return true;

            foreach (var kv in _definitions)
            {
                if (kv.Value.PanelType != null && type.IsAssignableFrom(kv.Value.PanelType))
                {
                    definition = kv.Value;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public T Open<T>() where T : class, IUIPanel => (T)OpenCore(typeof(T));

        public async Task<T> OpenAsync<T>() where T : class, IUIPanel => (T)await OpenAsyncCore(typeof(T));

        public Task PreloadAsync<T>() where T : class, IUIPanel => PreloadAsync(typeof(T));

        public async Task PreloadAsync(Type panelType)
        {
            if (panelType == null) return;
            if (!TryGetDefinition(panelType, out var definition)) return;
            if (_instances.ContainsKey(definition.PanelType)) return;

            var panel = await _factory.CreateAsync(definition);
            var lifecycle = CreateLifecycle(panel, definition);
            if (lifecycle == null) return;

            // 仅实例化并缓存，不显示
            if (panel is UnityEngine.MonoBehaviour mb) mb.gameObject.SetActive(false);
            _instances[definition.PanelType] = lifecycle;
        }

        public void Close<T>() where T : class, IUIPanel
        {
            if (!TryGetDefinition(typeof(T), out var definition)) return;
            if (!_instances.TryGetValue(definition.PanelType, out var panel)) return;

            panel.Hide();
            _openOrder.Remove(panel);

            // 根据缓存策略决定是否销毁
            if (!definition.CacheOnClose)
            {
                panel.Destroy();
                _factory.Destroy(panel as IUIPanel);
                _instances.Remove(definition.PanelType);
            }
        }

        public void CloseAll()
        {
            for (int i = _openOrder.Count - 1; i >= 0; i--)
            {
                var panel = _openOrder[i];
                panel.Hide();

                var type = panel.GetType();
                if (_definitions.TryGetValue(type, out var def) && !def.CacheOnClose)
                {
                    panel.Destroy();
                    _factory.Destroy(panel as IUIPanel);
                    _instances.Remove(type);
                }
            }
            _openOrder.Clear();
        }

        public IReadOnlyList<PanelDefinition> GetDefinitions() => new List<PanelDefinition>(_definitions.Values);

        public bool IsOpen<T>() where T : class, IUIPanel
        {
            return TryGetDefinition(typeof(T), out var def)
                   && _instances.TryGetValue(def.PanelType, out var p)
                   && _openOrder.Contains(p);
        }

        public bool IsCached<T>() where T : class, IUIPanel
            => TryGetDefinition(typeof(T), out var def) && _instances.ContainsKey(def.PanelType);

        public T Get<T>() where T : class, IUIPanel
            => TryGetDefinition(typeof(T), out var def) && _instances.TryGetValue(def.PanelType, out var p) ? p as T : null;

        public void DisposeScenePanels()
        {
            // 销毁所有 PerScene 面板实例（保留定义，便于再次打开时重建）
            var toRemove = new List<Type>();
            foreach (var kv in _definitions)
            {
                if (kv.Value.IsGlobal) continue;
                if (_instances.TryGetValue(kv.Key, out var panel))
                {
                    panel.Hide();
                    panel.Destroy();
                    _factory.Destroy(panel as IUIPanel);
                    toRemove.Add(kv.Key);
                }
            }

            foreach (var t in toRemove) _instances.Remove(t);
            _openOrder.RemoveAll(p => !_instances.ContainsKey(p.GetType()));
        }

        // ---------- 内部 ----------

        private IUIPanel OpenCore(Type requested)
        {
            if (!TryGetDefinition(requested, out var definition)) { LogNotRegistered(requested); return null; }
            if (!CheckSceneTag(definition)) return null;
            if (_instances.TryGetValue(definition.PanelType, out var cached))
            {
                EnsureOpen(cached);
                return cached as IUIPanel;
            }

            return Attach(_factory.Create(definition), definition);
        }

        private async Task<IUIPanel> OpenAsyncCore(Type requested)
        {
            if (!TryGetDefinition(requested, out var definition)) { LogNotRegistered(requested); return null; }
            if (!CheckSceneTag(definition)) return null;
            if (_instances.TryGetValue(definition.PanelType, out var cached))
            {
                EnsureOpen(cached);
                return cached as IUIPanel;
            }

            return Attach(await _factory.CreateAsync(definition), definition);
        }

        private IUIPanel Attach(IUIPanel panel, PanelDefinition definition)
        {
            var lifecycle = CreateLifecycle(panel, definition);
            if (lifecycle == null) return null;

            _instances[definition.PanelType] = lifecycle;
            EnsureOpen(lifecycle);
            return panel;
        }

        private IUIPanelLifecycle CreateLifecycle(IUIPanel panel, PanelDefinition definition)
        {
            if (panel == null) return null;

            if (panel is IUIPanelLifecycle lifecycle)
            {
                lifecycle.Create(_eventBus, _logger);
                return lifecycle;
            }

            _logger?.Error($"[UIService] {definition?.PanelType?.Name ?? panel.GetType().Name} 未实现 IUIPanelLifecycle（请继承 UIPanelBase）");
            return null;
        }

        private void EnsureOpen(IUIPanelLifecycle lifecycle)
        {
            if (lifecycle == null) return;
            if (!_openOrder.Contains(lifecycle)) _openOrder.Add(lifecycle);
            lifecycle.Show();
        }

        private void LogNotRegistered(Type type) => _logger?.Error($"[UIService] Panel not registered: {type.Name}");

        /// <summary>
        /// 场景标签校验（只在「打开」时生效）：
        /// 未声明 SceneTags / 全局面板 / 拿不到当前场景 → 放行；
        /// 否则当前场景必须在 SceneTags 内，否则按 <see cref="UISettings.EnforceSceneTags"/> 拒绝或仅警告。
        /// </summary>
        /// <remarks>
        /// 注意：不用于 <c>PreloadAsync</c> —— 场景预加载是在**切换过程中**发生的，
        /// 那时 ActiveScene 还是旧场景，必须用目标场景名判定（见 <c>PreloadPanelsStep</c>）。
        /// </remarks>
        private bool CheckSceneTag(PanelDefinition definition)
        {
            var tags = definition.SceneTags;
            if (tags == null || tags.Count == 0) return true;   // 未声明 = 所有场景通用
            if (definition.IsGlobal) return true;               // 全局面板与场景无关（SceneTags 对其无效）

            var scene = _sceneService?.ActiveSceneName;
            if (string.IsNullOrEmpty(scene)) return true;       // 拿不到当前场景 → 不拦截
            if (tags.Contains(scene)) return true;

            string message = $"[UIService] 面板 {definition.PanelType.Name} 的场景标签不匹当前场景：" +
                             $"当前={scene}，声明={string.Join(", ", tags)}";

            if (_enforceSceneTags)
            {
                _logger?.Warn(message + " —— 已拒绝打开（UISettings.EnforceSceneTags=false 可改为仅警告）");
                return false;
            }

            _logger?.Warn(message + " —— 仅警告，仍然打开");
            return true;
        }
    }
}