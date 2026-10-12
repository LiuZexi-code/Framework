using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Events;
using Framework.Abstractions.Input;
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

        /// <summary>可选：用于「面板打开/关闭时自动推/弹输入上下文」。为空则本能力整体关闭。</summary>
        private readonly IInputService _input;

        /// <summary>当前已由 UI 自动推入的输入上下文（用于自愈式对齐，不会泄漏）。</summary>
        private readonly HashSet<string> _pushedInputContexts = new();

        public UIService(IPanelFactory factory,IEventBus eventBus, ILogger logger,
                         ISceneService sceneService = null, UISettings settings = null,
                         IInputService inputService = null)
        {
            _factory = factory;
            _eventBus = eventBus;
            _logger = logger;
            _sceneService = sceneService;
            _enforceSceneTags = settings?.EnforceSceneTags ?? true;
            _input = inputService;
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

        public IUIPanel Open(Type panelType) => OpenCore(panelType);

        public async Task<T> OpenAsync<T>() where T : class, IUIPanel => (T)await OpenAsyncCore(typeof(T));

        public Task<IUIPanel> OpenAsync(Type panelType) => OpenAsyncCore(panelType);

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

        public void Close<T>() where T : class, IUIPanel => CloseCore(typeof(T));

        public void Close(Type panelType) => CloseCore(panelType);

        private void CloseCore(Type requested)
        {
            if (!TryGetDefinition(requested, out var definition)) return;
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

            SyncInputContexts();
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

            SyncInputContexts();
        }

        public IReadOnlyList<PanelDefinition> GetDefinitions() => new List<PanelDefinition>(_definitions.Values);

        public bool IsOpen<T>() where T : class, IUIPanel => IsOpen(typeof(T));

        public bool IsOpen(Type panelType)
        {
            return TryGetDefinition(panelType, out var def)
                   && _instances.TryGetValue(def.PanelType, out var p)
                   && _openOrder.Contains(p);
        }

        public bool IsCached<T>() where T : class, IUIPanel => IsCached(typeof(T));

        public bool IsCached(Type panelType)
            => TryGetDefinition(panelType, out var def) && _instances.ContainsKey(def.PanelType);

        public T Get<T>() where T : class, IUIPanel
            => TryGetDefinition(typeof(T), out var def) && _instances.TryGetValue(def.PanelType, out var p) ? p as T : null;

        /// <summary>
        /// 场景退出时清理「不属于新场景」的面板实例（保留定义，便于再次打开时重建）。
        /// <para>
        /// <b>必须传「进入的场景名」</b>：判据是 <see cref="SurvivesScene"/>，
        /// 这样「为**进入场景**预加载的面板」不会被这一步误销毁。
        /// ⚠️ 不要用 <see cref="ISceneService.ActiveSceneName"/> —— 切换过程中它还是**旧场景**
        /// （同一个坑见 <see cref="CheckSceneTag"/> 的注释）。
        /// </para>
        /// </summary>
        public void DisposeScenePanels(string enteringScene)
        {
            // 销毁不属于新场景的面板实例（全局 / 标签命中新场景的实例保留）
            var toRemove = new List<Type>();
            foreach (var kv in _definitions)
            {
                if (SurvivesScene(kv.Value, enteringScene)) continue;
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

            SyncInputContexts();
        }

        /// <summary>
        /// 面板实例在「进入 <paramref name="enteringScene"/>」之后是否应继续存活。
        /// <para>
        /// 判据：无标签（= 全局，所有场景通用）→ 存活；否则标签需包含新场景。
        /// 拿不到场景名时**保守存活**（宁可不销毁，也不要误删）。
        /// </para>
        /// </summary>
        internal static bool SurvivesScene(PanelDefinition definition, string enteringScene)
        {
            if (definition == null) return true;                       // 定义缺失 → 不销毁
            if (definition.IsGlobal) return true;                      // 无标签 = 所有场景通用
            if (string.IsNullOrEmpty(enteringScene)) return true;      // 拿不到目标场景 → 不销毁
            return definition.SceneTags.Contains(enteringScene);
        }

        // ---------- 内部 ----------

        private IUIPanel OpenCore(Type requested)
        {
            if (!TryGetDefinition(requested, out var definition)) { LogNotRegistered(requested); return null; }
            if (!CheckSceneTag(definition)) return null;
            if (_instances.TryGetValue(definition.PanelType, out var cached))
            {
                EnsureOpen(cached, definition);
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
                EnsureOpen(cached, definition);
                return cached as IUIPanel;
            }

            return Attach(await _factory.CreateAsync(definition), definition);
        }

        private IUIPanel Attach(IUIPanel panel, PanelDefinition definition)
        {
            var lifecycle = CreateLifecycle(panel, definition);
            if (lifecycle == null) return null;

            _instances[definition.PanelType] = lifecycle;
            EnsureOpen(lifecycle, definition);
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

        private void EnsureOpen(IUIPanelLifecycle lifecycle, PanelDefinition definition = null)
        {
            if (lifecycle == null) return;
            if (!_openOrder.Contains(lifecycle)) _openOrder.Add(lifecycle);
            ApplyOrder(lifecycle, definition);
            lifecycle.Show();

            SyncInputContexts();
        }

        /// <summary>
        /// 把「UI 自动推入的输入上下文」对齐到当前**已打开**面板集合（打开/关闭/销毁后都调）。
        /// <para>
        /// 采用「按现状推导」而非「计数器增减」：即使某条路径漏调，下一次对齐也会自愈，
        /// 不会出现「上下文被永久推着、玩法输入再也不响应」的泄漏。
        /// </para>
        /// <para>面板声明 <see cref="PanelDefinition.InputContextId"/> 即参与；同 id 多面板时只在 0→1 / 1→0 时真正 Push / Pop。</para>
        /// </summary>
        private void SyncInputContexts()
        {
            if (_input == null) return;

            // 1) 期望集合：已打开面板声明的输入上下文（同 id 取最大优先级）
            Dictionary<string, int> desired = null;
            for (int i = 0; i < _openOrder.Count; i++)
            {
                var panel = _openOrder[i];
                if (panel == null) continue;
                if (!_definitions.TryGetValue(panel.GetType(), out var def) || def == null) continue;
                if (string.IsNullOrEmpty(def.InputContextId)) continue;

                if (desired == null) desired = new Dictionary<string, int>();

                if (desired.TryGetValue(def.InputContextId, out var existing))
                    desired[def.InputContextId] = Math.Max(existing, def.InputContextPriority);
                else
                    desired[def.InputContextId] = def.InputContextPriority;
            }

            // 2) 撤掉不再需要的（先撤后推，保证不会瞬时存在两个同优先级上下文）
            if (_pushedInputContexts.Count > 0)
            {
                List<string> stale = null;
                foreach (var id in _pushedInputContexts)
                {
                    if (desired != null && desired.ContainsKey(id)) continue;
                    if (stale == null) stale = new List<string>();
                    stale.Add(id);
                }
                if (stale != null)
                    foreach (var id in stale)
                    {
                        _pushedInputContexts.Remove(id);
                        _input.PopContext(id);
                    }
            }

            // 3) 推上新增的
            if (desired == null) return;
            foreach (var kv in desired)
            {
                if (_pushedInputContexts.Contains(kv.Key)) continue;
                _pushedInputContexts.Add(kv.Key);
                _input.PushContext(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// 应用层内排序：写**面板自己的** <c>Canvas.sortingOrder</c>（层基址 + 优先级），
        /// 并用兄弟顺序做「同优先级时后开的在上」的 tie-break。
        /// <para>
        /// ⚠ 降级：非 <see cref="UnityEngine.Component"/> 的面板（如纯 C# 测试替身）没有 Transform/Canvas，
        /// 直接跳过 —— 排序是增强能力，不应让托管的纯逻辑面板打不开。
        /// </para>
        /// </summary>
        private void ApplyOrder(IUIPanelLifecycle lifecycle, PanelDefinition definition)
        {
            if (definition == null) return;
            if (!(lifecycle is UnityEngine.Component component) || component == null) return;

            var sortTarget = component.GetComponentInParent<PanelSortTarget>(true);
            sortTarget?.SetOrder(UILayerOrder.ForPanel(definition.Layer, definition.Priority));

            component.transform.SetAsLastSibling();   // 同 order 时靠兄弟顺序让「后开的」在上
        }

        private void LogNotRegistered(Type type) => _logger?.Error($"[UIService] Panel not registered: {type.Name}");

        /// <summary>
        /// 场景标签校验（只在「打开」时生效）：
        /// 未声明 SceneTags（= 全局）/ 拿不到当前场景 → 放行；
        /// 否则当前场景必须在 SceneTags 内，否则按 <see cref="UISettings.EnforceSceneTags"/> 拒绝或仅警告。
        /// </summary>
        /// <remarks>
        /// 注意：不用于 <c>PreloadAsync</c> —— 场景预加载是在**切换过程中**发生的，
        /// 那时 ActiveScene 还是旧场景，必须用目标场景名判定（见 <c>PreloadPanelsStep</c>）。
        /// </remarks>
        private bool CheckSceneTag(PanelDefinition definition)
        {
            var tags = definition.SceneTags;
            // 未声明 = 所有场景通用。
            // ⚠ 曾经这里还跟了一句 `if (definition.IsGlobal) return true;` —— 阶段 B 之后
            //   IsGlobal 就是「SceneTags 为空」的派生属性，那句已成死代码，故删除。
            if (tags == null || tags.Count == 0) return true;

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