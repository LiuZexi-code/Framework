using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Scenes;
using Framework.Abstractions.UI;
using UnityEngine;
using UnityEngine.UI;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.UI
{
    /// <summary>
    /// <see cref="IPopupService"/> 的默认实现：**全局单栈** + 模态链 + 层内优先级。
    /// <para>设计见仓库根目录 <c>UI_POPUP_DESIGN.md</c>。由 <c>CoreServiceModule</c> 注册。</para>
    /// <para>
    /// 关键约定：
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>排序</b>：<c>order = UILayerOrder.ForPanel(Layer, Priority)</c> 写到「面板自己的 Canvas」；
    /// 同优先级由**兄弟顺序**决定（打开时 <c>SetAsLastSibling</c> → 后开的在上）。</description></item>
    /// <item><description><b>模态</b>：一个**共享遮罩**放在「最上面的模态面板」正下方（<c>order - 1</c>），
    /// 全透明但 <c>raycastTarget = true</c> → 吃掉下层的点击。点遮罩**不会**关闭弹窗。</description></item>
    /// <item><description><b>场景切换</b>：面板实例被 <c>DisposeScenePanels</c> 销毁后，栈会**自动剔除**失效项并重算模态。</description></item>
    /// <item><description><b>重复 Push 同一面板</b>：不重复入栈，只提到栈顶。</description></item>
    /// <item><description>⚠️ 本服务**不含任何输入概念**：返回键由业务绑定到 <see cref="CanPop"/> / <see cref="Pop"/>。</description></item>
    /// </list>
    /// <para>
    /// ⚠️ 尚未实现（见设计稿 §8）：异步队列的**取消**能力（当前只做串行化，保证「调用顺序 = 栈顺序」）。
    /// </para>
    /// </summary>
    public sealed class PopupService : IPopupService, IDisposable
    {
        private sealed class Entry
        {
            public PanelDefinition Definition;
            public IUIPanel Panel;
            public int Order;
            public bool IsModal;
        }

        private readonly IUIService _ui;
        private readonly ILogger _logger;
        private readonly ISceneService _sceneService;

        private readonly List<Entry> _stack = new();

        private GameObject _blocker;
        private Transform _blockerParent;
        private int _blockerOrder = int.MinValue;

        private Task _pushChain = Task.CompletedTask;
        private bool _disposed;

        public PopupService(IUIService uiService, ILogger logger, ISceneService sceneService = null)
        {
            _ui = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _logger = logger;
            _sceneService = sceneService;

            // 场景卸载会销毁非全局面板 → 栈里必须同步剔除（见 Prune）
            if (_sceneService != null) _sceneService.SceneUnloaded += OnSceneUnloaded;
        }

        public int Depth { get { Prune(); return _stack.Count; } }

        public bool CanPop { get { Prune(); return _stack.Count > 0; } }

        public event Action<IUIPanel> Pushed;
        public event Action<IUIPanel> Popped;

        // ---------- Push ----------

        public IUIPanel Push<T>() where T : class, IUIPanel => PushCore(typeof(T), null);

        public Task<IUIPanel> PushAsync<T>() where T : class, IUIPanel => PushAsyncCore(typeof(T), null);

        public Task<IUIPanel> PushAsync<T>(int priority) where T : class, IUIPanel
            => PushAsyncCore(typeof(T), priority);

        // ---------- Pop ----------

        public bool Pop() => PopCore(popAll: false);

        public void PopAll() => PopCore(popAll: true);

        public void Clear() => PopAll();

        public bool PopTo<T>() where T : class, IUIPanel => PopToCore(typeof(T));

        // ---------- 查询 ----------

        public bool TryPeek(out IUIPanel panel)
        {
            Prune();
            if (_stack.Count == 0) { panel = null; return false; }

            panel = _stack[_stack.Count - 1].Panel;
            return panel != null;
        }

        public T Peek<T>() where T : class, IUIPanel
            => TryPeek(out var panel) && panel is T typed ? typed : null;

        // ---------- 内部：Push ----------

        private IUIPanel PushCore(Type panelType, int? priorityOverride)
        {
            if (!TryResolveDefinition(panelType, out var definition)) return null;

            var panel = _ui.Open(definition.PanelType);
            if (panel == null)
            {
                _logger?.Error($"[Popup] 打开失败（同步打开要求面板已预加载）：{definition.PanelType.Name}");
                return null;
            }

            RecordPush(definition, panel, priorityOverride);
            Pushed?.Invoke(panel);
            return panel;
        }

        private Task<IUIPanel> PushAsyncCore(Type panelType, int? priorityOverride)
        {
            // 串行化：接在上一次 PushAsync 之后 → 「调用顺序 = 栈顺序」
            var task = RunAfterAsync(_pushChain, () => PushAsyncInternal(panelType, priorityOverride));

            // ⚠ 这里必须用 await 生成的「吞异常的已完成任务」，**不要**用 ContinueWith：
            // ContinueWith 默认会排到线程池，会把「本该同步完成」的链条凭空变成异步，
            // 在 EditMode / 无帧泵场景下会让调用方无法在不阻塞主线程的情况下拿到结果。
            _pushChain = SwallowAsync(task);
            return task;
        }

        private static async Task<IUIPanel> RunAfterAsync(Task previous, Func<Task<IUIPanel>> work)
        {
            try { await previous; } catch { /* 前一项失败不应阻断后一项 */ }
            return await work();
        }

        /// <summary>把任意任务转成「永不失败」的完成标记任务，用作队列的前驱。</summary>
        private static async Task SwallowAsync(Task task)
        {
            try { await task; } catch { /* 前一项失败不应阻断后一项 */ }
        }

        private async Task<IUIPanel> PushAsyncInternal(Type panelType, int? priorityOverride)
        {
            if (_disposed) return null;
            if (!TryResolveDefinition(panelType, out var definition)) return null;

            var panel = await _ui.OpenAsync(definition.PanelType);
            if (panel == null)
            {
                _logger?.Error($"[Popup] 异步打开失败：{definition.PanelType.Name}");
                return null;
            }

            RecordPush(definition, panel, priorityOverride);
            Pushed?.Invoke(panel);
            return panel;
        }

        private void RecordPush(PanelDefinition definition, IUIPanel panel, int? priorityOverride)
        {
            Prune();

            // 重复 Push 同一面板：不重复入栈，只提到栈顶
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Definition.PanelType == definition.PanelType) { _stack.RemoveAt(i); break; }
            }

            int order = UILayerOrder.ForPanel(definition.Layer, priorityOverride ?? definition.Priority);
            ApplyOrder(panel, order);

            _stack.Add(new Entry
            {
                Definition = definition,
                Panel = panel,
                Order = order,
                IsModal = definition.IsModal,
            });

            UpdateBlocker();
        }

        // ---------- 内部：Pop ----------

        private bool PopCore(bool popAll)
        {
            Prune();
            if (_stack.Count == 0) return false;

            do
            {
                var top = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);

                _ui.Close(top.Definition.PanelType);   // cacheOnClose=true → 隐藏保活；false → 销毁
                Popped?.Invoke(top.Panel);
            }
            while (popAll && _stack.Count > 0);

            RestoreTop();
            return true;
        }

        private bool PopToCore(Type target)
        {
            Prune();

            // ⚠ 必须先把「请求的类型」解析成注册定义，再比 Definition.PanelType：
            // 调用方传进来的常常是**接口**（Push<IPanelA>/PopTo<IPanelA>），
            // 而栈里存的是解析后的**具体面板类型**（PanelA），直接比接口永远不相等。
            if (!TryResolveDefinition(target, out var definition)) return false;

            int index = -1;
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Definition.PanelType == definition.PanelType) { index = i; break; }
            }
            if (index < 0) return false;

            while (_stack.Count - 1 > index)
            {
                var top = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);
                _ui.Close(top.Definition.PanelType);
                Popped?.Invoke(top.Panel);
            }

            RestoreTop();
            return true;
        }

        /// <summary>让「新的栈顶」重新可见并置顶（通常已是可见状态，这里是幂等的兜底）。</summary>
        private void RestoreTop()
        {
            if (_stack.Count > 0)
            {
                var below = _stack[_stack.Count - 1];
                var reopened = _ui.Open(below.Definition.PanelType);
                if (reopened != null) below.Panel = reopened;
                ApplyOrder(below.Panel, below.Order);
            }

            UpdateBlocker();
        }

        // ---------- 内部：排序 ----------

        private void ApplyOrder(IUIPanel panel, int order)
        {
            if (!(panel is Component component) || component == null) return;

            // 面板自己的 Canvas（由 PanelFactory 挂在实例根）
            component.GetComponentInParent<PanelSortTarget>(true)?.SetOrder(order);

            // 同 order 时靠兄弟顺序让「后开的」在上（= 打开序 tie-break）
            component.transform.SetAsLastSibling();
        }

        // ---------- 内部：模态遮罩 ----------

        private void UpdateBlocker()
        {
            int modalIndex = -1;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].IsModal) { modalIndex = i; break; }
            }

            // 无模态，或模态已在最底（下面没有东西可挡）→ 隐藏遮罩
            if (modalIndex <= 0)
            {
                if (_blocker != null && _blocker.activeSelf) _blocker.SetActive(false);
                return;
            }

            var modal = _stack[modalIndex];
            var parent = (modal.Panel as Component)?.transform.parent;
            if (parent == null)
            {
                if (_blocker != null) _blocker.SetActive(false);
                return;
            }

            EnsureBlocker(parent, modal.Order - 1);
            if (!_blocker.activeSelf) _blocker.SetActive(true);
        }

        private void EnsureBlocker(Transform parent, int order)
        {
            if (_blocker == null)
            {
                _blocker = new GameObject("PopupBlocker", typeof(RectTransform));

                // ⚠ 先挂父级、再配 Canvas（顺序反了会被静默重置，见 NestedCanvas）
                NestedCanvas.Configure(_blocker, parent, order);
                _blockerParent = parent;
                _blockerOrder = order;

                // 嵌套 Canvas 需要自己的 GraphicRaycaster，否则它吃不到点击
                _blocker.AddComponent<GraphicRaycaster>();

                var image = _blocker.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);   // 全透明：只挡交互，不做压暗
                image.raycastTarget = true;                // 关键：吃掉下层的点击

                var rect = (RectTransform)_blocker.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return;
            }

            if (_blockerParent != parent)
            {
                // 重挂父级会重置 Canvas 排序 → 下面的 SetOrder 会重新拉正
                _blocker.transform.SetParent(parent, false);
                _blockerParent = parent;
                _blockerOrder = int.MinValue;
            }

            if (_blockerOrder != order)
            {
                NestedCanvas.SetOrder(_blocker.GetComponent<Canvas>(), order);
                _blockerOrder = order;
            }
        }

        // ---------- 内部：定义 / 场景 ----------

        private bool TryResolveDefinition(Type requested, out PanelDefinition definition)
        {
            definition = null;
            if (requested == null) return false;

            var all = _ui.GetDefinitions();
            if (all == null)
            {
                _logger?.Error($"[Popup] 面板未注册：{requested.Name}");
                return false;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].PanelType == requested) { definition = all[i]; return true; }
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i]?.PanelType != null && requested.IsAssignableFrom(all[i].PanelType))
                {
                    definition = all[i];
                    return true;
                }
            }

            _logger?.Error($"[Popup] 面板未注册：{requested.Name}");
            return false;
        }

        private void OnSceneUnloaded(SceneUnloadedEvent _)
        {
            Prune();
            UpdateBlocker();
        }

        /// <summary>
        /// 剔除已失效的栈项。判据只要「实例还在 + 仍被 UIService 缓存」——
        /// 这样**无需**在这里重复判断场景归属（那是 <c>DisposeScenePanels</c> 的职责，见设计稿 §5.3）。
        /// </summary>
        private void Prune()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var entry = _stack[i];
                bool alive = entry.Panel is UnityEngine.Object obj ? obj != null : entry.Panel != null;
                if (!alive || !_ui.IsCached(entry.Definition.PanelType)) _stack.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_sceneService != null) _sceneService.SceneUnloaded -= OnSceneUnloaded;
            if (_blocker != null) { UnityEngine.Object.Destroy(_blocker); _blocker = null; }
            _stack.Clear();
        }
    }
}
