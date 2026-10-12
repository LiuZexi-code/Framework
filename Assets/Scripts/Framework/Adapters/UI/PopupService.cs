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
    /// ⚠️ 本服务**不含任何输入概念**：返回键由业务绑定到 <see cref="CanPop"/> / <see cref="Pop"/>。
    /// </para>
    /// </summary>
    public sealed class PopupService : IPopupService, IDisposable
    {
        /// <summary>未提供 <see cref="UISettings"/> 时使用的默认队列单项超时（秒）。</summary>
        public const float DefaultQueueTimeoutSeconds = 10f;

        private sealed class Entry
        {
            public PanelDefinition Definition;
            public IUIPanel Panel;
            public int Order;
            public bool IsModal;
        }

        /// <summary>一次待处理的异步 Push。
        /// <para>
        /// <b>取消是协作式的</b>：只打标记，不直接完成 Task ——
        /// 队列中的项会当场被摘出队列并完成；**正在打开**的那一项要等它的打开操作落地（或被超时截断）
        /// 才能确定「面板有没有被创建出来」，否则无法决定要不要把它关掉。
        /// </para>
        /// </summary>
        private sealed class PendingPush
        {
            public PanelDefinition Definition;
            public int? PriorityOverride;

            /// <summary>
            /// ⚠ 不要加 <c>RunContinuationsAsynchronously</c>：本服务运行在 Unity 主线程，
            /// 调用方的延续应该**内联/回到主线程**，否则 await 之后会在线程池上碰 Transform。
            /// </summary>
            public readonly TaskCompletionSource<IUIPanel> Completion = new TaskCompletionSource<IUIPanel>();

            /// <summary>已作废：不得显示、不得入栈。</summary>
            public bool Cancelled;
        }

        private readonly IUIService _ui;
        private readonly ILogger _logger;
        private readonly ISceneService _sceneService;
        private readonly float _queueTimeoutSeconds;

        private readonly List<Entry> _stack = new();
        private readonly Queue<PendingPush> _pending = new();

        private PendingPush _current;   // 正在「打开中」的那一项（取消时要能标记到它）
        private bool _pumping;

        private GameObject _blocker;
        private Transform _blockerParent;
        private int _blockerOrder = int.MinValue;

        private bool _disposed;

        public PopupService(IUIService uiService, ILogger logger,
                            ISceneService sceneService = null, UISettings settings = null)
        {
            _ui = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _logger = logger;
            _sceneService = sceneService;
            _queueTimeoutSeconds = settings != null ? settings.PopupQueueTimeoutSeconds : DefaultQueueTimeoutSeconds;

            // 场景卸载会销毁非全局面板 → 栈里必须同步剔除（见 Prune），并作废待处理项
            if (_sceneService != null) _sceneService.SceneUnloaded += OnSceneUnloaded;
        }

        public int Depth { get { Prune(); return _stack.Count; } }

        public bool CanPop { get { Prune(); return _stack.Count > 0; } }

        public int PendingCount => _pending.Count + (_current != null ? 1 : 0);

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
            if (_disposed) return Task.FromResult<IUIPanel>(null);

            // 先解析定义：类型不对就**当场**失败，不必占着队列
            if (!TryResolveDefinition(panelType, out var definition))
                return Task.FromResult<IUIPanel>(null);

            var item = new PendingPush { Definition = definition, PriorityOverride = priorityOverride };
            _pending.Enqueue(item);
            StartPump();
            return item.Completion.Task;
        }

        private void StartPump()
        {
            if (_pumping) return;
            _pumping = true;
            _ = DrainQueueAsync();
        }

        /// <summary>
        /// 串行泵：一次只处理一项 → 保证「调用顺序 = 栈顺序」。
        /// <para>
        /// 用 <c>async</c> + <c>await</c> 串联（**不是** <c>ContinueWith</c>）：这样当底层打开同步完成时，
        /// 整条队列也在**同一帧同步**跑完 —— 既便于测试，也避免在无帧泵场景下挂起。
        /// </para>
        /// </summary>
        private async Task DrainQueueAsync()
        {
            try
            {
                while (_pending.Count > 0)
                {
                    var item = _pending.Dequeue();
                    _current = item;
                    try
                    {
                        if (!item.Cancelled) await ProcessAsync(item);
                    }
                    catch (Exception e)
                    {
                        _logger?.Error($"[Popup] PushAsync 处理失败：{item.Definition.PanelType.Name}：{e.Message}");
                    }
                    finally
                    {
                        _current = null;
                        // 幂等：成功路径已先设过结果；失败 / 取消 / 异常都让调用方拿到 null
                        item.Completion.TrySetResult(null);
                    }
                }
            }
            finally
            {
                _pumping = false;
                // 收尾与入队之间的竞态：期间又有新项就再起一轮
                if (_pending.Count > 0) StartPump();
            }
        }

        /// <summary>处理单项：打开 → 检查是否已被取消 → 入栈。</summary>
        private async Task ProcessAsync(PendingPush item)
        {
            var panel = await OpenAndWaitAsync(item);
            if (panel == null)
            {
                _logger?.Error($"[Popup] 异步打开失败：{item.Definition.PanelType.Name}");
                return;
            }

            // 打开期间被取消（Pop / PopAll / Clear / PopTo / 场景切换）→ 不显示、不入栈
            if (item.Cancelled)
            {
                _ui.Close(item.Definition.PanelType);
                _logger?.Warn($"[Popup] 已取消（打开期间遇到 Pop / 清空 / 切场景），不显示：{item.Definition.PanelType.Name}");
                return;
            }

            RecordPush(item.Definition, panel, item.PriorityOverride);
            Pushed?.Invoke(panel);
            item.Completion.TrySetResult(panel);
        }

        /// <summary>
        /// 打开面板并等待完成，带**单项超时**兜底（避免一次卡住的加载把整条队列堵死）。
        /// <para>
        /// ⚠ 底层已同步完成时**直接返回、不创建计时器** —— 这条性质让「已缓存面板」的
        /// <c>PushAsync</c> 在调用帧内同步完成，Unity 与测试都依赖它。
        /// </para>
        /// </summary>
        private async Task<IUIPanel> OpenAndWaitAsync(PendingPush item)
        {
            var openTask = _ui.OpenAsync(item.Definition.PanelType);

            if (openTask.IsCompleted || _queueTimeoutSeconds <= 0f) return await openTask;

            int timeoutMs = Math.Max(1, (int)(_queueTimeoutSeconds * 1000f));
            var finished = await Task.WhenAny(openTask, Task.Delay(timeoutMs));
            if (finished == openTask) return await openTask;

            _logger?.Error($"[Popup] 打开超时（{_queueTimeoutSeconds:0.##}s），已跳过：{item.Definition.PanelType.Name}");
            _ = AbandonLateOpenAsync(openTask, item.Definition.PanelType);
            return null;
        }

        /// <summary>
        /// 超时后那次打开**最终仍可能成功** —— 届时 <c>UIService</c> 已经把它创建并 <c>Show()</c> 了出来，
        /// 但它不在弹窗栈里，会变成一个「无主的可见面板」。这里等它落地后关掉。
        /// <para>
        /// ⚠ 延续必须回到 Unity 主线程（所以用 <c>await</c> 而不是 <c>ContinueWith</c>），
        /// 否则会在线程池上碰 Transform。
        /// </para>
        /// </summary>
        private async Task AbandonLateOpenAsync(Task<IUIPanel> openTask, Type panelType)
        {
            try
            {
                if (await openTask == null) return;
                if (_disposed) return;

                _logger?.Warn($"[Popup] 超时的打开后来还是成功了，已关闭：{panelType.Name}");
                _ui.Close(panelType);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[Popup] 超时的打开最终失败（符合预期）：{panelType.Name}：{e.Message}");
            }
        }

        /// <summary>
        /// 作废所有待处理的 <see cref="PushAsyncCore"/>：
        /// 队列里的**当场**摘出队列并以 <c>null</c> 完成；
        /// 正在打开的那一项只打标记（见 <see cref="PendingPush"/>），稍后也不会入栈/显示。
        /// <para>由 <c>Pop</c> / <c>PopAll</c> / <c>Clear</c> / <c>PopTo</c> / 场景卸载 触发。</para>
        /// </summary>
        private void CancelPending()
        {
            while (_pending.Count > 0)
            {
                var item = _pending.Dequeue();
                item.Cancelled = true;
                item.Completion.TrySetResult(null);
            }

            if (_current != null) _current.Cancelled = true;
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
            // 取消是隐式的：按了「返回 / 关闭」，就不该再冒出此前排队要开的弹窗
            CancelPending();

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
            CancelPending();
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
            CancelPending();   // 切场景：还没打开的弹窗就别开了
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

            CancelPending();
            // 飞行中的项无法从队列里摘 —— 直接终结，否则调用方会永远等不到结果
            _current?.Completion.TrySetResult(null);

            if (_sceneService != null) _sceneService.SceneUnloaded -= OnSceneUnloaded;
            if (_blocker != null) { UnityEngine.Object.Destroy(_blocker); _blocker = null; }
            _stack.Clear();
        }
    }
}
