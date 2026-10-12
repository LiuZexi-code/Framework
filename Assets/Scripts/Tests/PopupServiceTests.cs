using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions;
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;
using Framework.UI;
using NUnit.Framework;
using UnityEngine.TestTools;
using ILogger = Framework.Abstractions.Logging.ILogger;

/// <summary>
/// 弹窗栈（<see cref="PopupService"/>）的单元测试。
/// <para>
/// 用**纯 C# 面板替身**（非 MonoBehaviour）驱动 → 走「无排序句柄 / 无遮罩父节点」的**降级路径**；
/// 真实 Canvas 排序与模态遮罩的落盘效果由 Play 验证（见 <c>UI_POPUP_DESIGN.md</c> §7）。
/// </para>
/// </summary>
public class PopupServiceTests
{
    private interface IPanelA : IUIPanel { }
    private interface IPanelB : IUIPanel { }
    private interface IPanelC : IUIPanel { }
    private interface IPanelD : IUIPanel { }   // 故意不注册

    private abstract class TestPanelBase : IUIPanelLifecycle
    {
        public bool Shown, Hidden;
        public void Create(IEventBus bus, ILogger logger) { }
        public void Show() => Shown = true;
        public void Hide() => Hidden = true;
        public void Destroy() { }
    }

    private sealed class PanelA : TestPanelBase, IPanelA { }
    private sealed class PanelB : TestPanelBase, IPanelB { }
    private sealed class PanelC : TestPanelBase, IPanelC { }
    private sealed class PanelD : TestPanelBase, IPanelD { }

    private sealed class FakeFactory : IPanelFactory
    {
        public IUIPanel Create(PanelDefinition definition) => Make(definition);
        public Task<IUIPanel> CreateAsync(PanelDefinition definition) => Task.FromResult(Make(definition));
        public void Destroy(IUIPanel panel) { }

        private static IUIPanel Make(PanelDefinition definition)
            => definition?.PanelType == typeof(PanelA) ? new PanelA()
             : definition?.PanelType == typeof(PanelB) ? new PanelB()
             : definition?.PanelType == typeof(PanelC) ? new PanelC()
             : null;
    }

    private static UIService MakeUI()
    {
        var ui = new UIService(new FakeFactory(), null, null);
        ui.Register(Def(typeof(PanelA)));
        ui.Register(Def(typeof(PanelB)));
        ui.Register(Def(typeof(PanelC)));
        return ui;
    }

    /// <summary>
    /// 面板定义。⚠️ 阶段 B 后「是否全局」由 <paramref name="sceneTags"/> 派生：
    /// 传 null/空 ⇒ 全局（跨场景常驻），非空 ⇒ 按场景（会被场景释放销毁）。
    /// </summary>
    private static PanelDefinition Def(Type type, string[] sceneTags = null, bool isModal = false, int priority = 0)
        => new PanelDefinition(type, "addr", PanelLayer.Popup, true,
                               sceneTags == null ? null : new List<string>(sceneTags),
                               LoadPolicy.OnDemand, priority, isModal);

    private static PopupService MakePopups(UIService ui) => new PopupService(ui, null);

    private static void PreloadAll(UIService ui)
    {
        foreach (var t in new[] { typeof(PanelA), typeof(PanelB), typeof(PanelC) })
            ui.PreloadAsync(t).GetAwaiter().GetResult();
    }

    [Test]
    public void Push_ThenDepthAndPeek()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        Assert.AreEqual(0, popups.Depth);
        Assert.IsFalse(popups.CanPop);

        var a = popups.Push<IPanelA>();
        Assert.IsNotNull(a);
        Assert.AreEqual(1, popups.Depth);
        Assert.IsTrue(popups.CanPop);
        Assert.IsTrue(popups.TryPeek(out var top));
        Assert.AreSame(a, top);
        Assert.IsNotNull(popups.Peek<IPanelA>());
    }

    [Test]
    public void Push_TwiceSamePanel_DoesNotDuplicateInStack()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        popups.Push<IPanelA>();
        popups.Push<IPanelB>();
        popups.Push<IPanelA>();   // 再次打开 A → 提到栈顶，但不重复入栈

        Assert.AreEqual(2, popups.Depth, "重复 Push 同一面板不应重复入栈");
        Assert.IsNotNull(popups.Peek<IPanelA>(), "重复 Push 应把它提到栈顶");
    }

    [Test]
    public void Pop_HidesTopAndRevealsBelow()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        popups.Push<IPanelA>();
        var b = (PanelB)popups.Push<IPanelB>();

        Assert.IsTrue(popups.Pop());

        Assert.AreEqual(1, popups.Depth);
        Assert.IsNotNull(popups.Peek<IPanelA>(), "Pop 后栈顶应回到下层");
        Assert.IsTrue(b.Hidden, "栈顶应被隐藏");
        Assert.IsTrue(ui.IsOpen(typeof(PanelA)), "下层应被恢复显示");
    }

    [Test]
    public void Pop_OnEmptyStack_ReturnsFalse()
    {
        var popups = MakePopups(MakeUI());
        Assert.IsFalse(popups.Pop());
    }

    [Test]
    public void PopAll_EmptiesStackAndFiresPoppedForEach()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        var popped = new List<IUIPanel>();
        popups.Popped += p => popped.Add(p);

        popups.Push<IPanelA>();
        popups.Push<IPanelB>();
        popups.Push<IPanelC>();
        popups.PopAll();

        Assert.AreEqual(0, popups.Depth);
        Assert.IsFalse(popups.CanPop);
        Assert.AreEqual(3, popped.Count, "每个被弹出的面板都应触发 Popped");
    }

    [Test]
    public void PopTo_PopsAboveTargetAndKeepsTargetOnTop()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        popups.Push<IPanelA>();
        popups.Push<IPanelB>();
        popups.Push<IPanelC>();

        Assert.IsTrue(popups.PopTo<IPanelB>());
        Assert.AreEqual(2, popups.Depth);
        Assert.IsNotNull(popups.Peek<IPanelB>(), "目标面板应留在栈顶");

        Assert.IsFalse(popups.PopTo<IPanelC>(), "已不在栈内的面板 → 返回 false");
    }

    [Test]
    public void Push_UnregisteredPanel_ReturnsNullAndLeavesStackUntouched()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        popups.Push<IPanelA>();

        Assert.IsNull(popups.Push<IPanelD>(), "未注册的面板应返回 null（不抛异常）");
        Assert.AreEqual(1, popups.Depth, "失败的 Push 不应改变栈");
    }

    [Test]
    public void Pushed_EventFires()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        int count = 0;
        popups.Pushed += _ => count++;
        popups.Push<IPanelA>();
        popups.Push<IPanelB>();

        Assert.AreEqual(2, count);
    }

    /// <summary>
    /// 场景切换会把**非全局**面板实例销毁（<c>DisposeScenePanels</c>）→ 栈必须同步剔除失效项。
    /// （本用例用 <c>DisposeScenePanels</c> 模拟那次销毁，无需真实 MonoBehaviour。）
    /// </summary>
    [Test]
    public void Stack_IsPruned_WhenSceneDisposalDestroysPanels()
    {
        var ui = new UIService(new FakeFactory(), null, null);
        ui.Register(Def(typeof(PanelA), new[] { "SceneX" }));   // 按场景声明
        ui.Register(Def(typeof(PanelB)));                       // sceneTags 为空 → 全局，存活
        PreloadAll(ui);

        var popups = MakePopups(ui);
        popups.Push<IPanelA>();
        popups.Push<IPanelB>();
        Assert.AreEqual(2, popups.Depth);

        ui.DisposeScenePanels("SceneY");   // 进入 SceneY：只保留全局 + 标签命中 SceneY 的实例

        Assert.AreEqual(1, popups.Depth, "被销毁的按场景面板应被剔除，全局面板保留");
        Assert.IsNotNull(popups.Peek<IPanelB>());
    }

    /// <summary>
    /// 连续两次 <c>PushAsync</c> 必须**按调用顺序**入栈（内部串行队列的语义保证）。
    /// <para>
    /// ⚠️ 这里刻意写成**同步**断言，绝不阻塞主线程（`.Wait()` / `Result` / `WaitAll` 会死锁：
    /// EditMode 测试跑在主线程，而被 await 的延续排在 Unity 的 SynchronizationContext 上，
    /// 主线程被阻塞就永远没人执行它 —— 实测会**卡死整个测试运行并让编辑器失去响应**）。
    /// </para>
    /// <para>
    /// 之所以能不等待直接断言，是因为队列实现要求：**当底层打开是同步完成时，整条链也必须同步完成**
    /// （用 <c>await</c> 而非 <c>ContinueWith</c> 串联）。这既是可测性，也是 EditMode / 无帧泵
    /// 场景下不挂起的前提。另有 <c>PushAsync_UnregisteredPanel_FaultsQueueDoesNotBlockNext</c>
    /// 覆盖「前一项失败不阻断后一项」。
    /// </para>
    /// </summary>
    [Test]
    public void PushAsync_PushesInCallOrder_AndCompletesSynchronously()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        var t1 = popups.PushAsync<IPanelA>();
        var t2 = popups.PushAsync<IPanelB>();

        Assert.IsTrue(t1.IsCompleted, "已缓存面板的异步打开应同步完成（不得依赖帧泵）");
        Assert.IsTrue(t2.IsCompleted, "队列前驱同步完成时，后续项也应同步完成");
        Assert.AreEqual(2, popups.Depth);
        Assert.IsNotNull(popups.Peek<IPanelB>(), "后调用的 PushAsync 应在栈顶（调用顺序 = 栈顺序）");
    }

    /// <summary>队列的一项失败（面板未注册）不得阻断其后入队的项。</summary>
    [Test]
    public void PushAsync_UnregisteredPanel_FaultsQueueDoesNotBlockNext()
    {
        var ui = MakeUI();
        PreloadAll(ui);
        var popups = MakePopups(ui);

        var bad = popups.PushAsync<IPanelD>();   // 未注册 → 返回 null（不抛）
        var good = popups.PushAsync<IPanelA>();

        Assert.IsTrue(bad.IsCompleted && good.IsCompleted);
        Assert.AreEqual(1, popups.Depth, "失败项不入栈，后续项正常入栈");
        Assert.IsNotNull(popups.Peek<IPanelA>());
    }

    // ---------------- 异步队列：串行 / 取消 / 超时 ----------------
    //
    // ⚠ 这些用例需要「等待中被打断 / 过期后完成」，所以必须用 [UnityTest] + yield：
    //   本项目的 UTF **不支持 async Task 测试方法**（报 “Method has non-void return value”），
    //   而 EditMode 的 [UnityTest] 能驱动被 post 到 UnitySynchronizationContext 的 await 延续。
    //   ⚠ 绝不能用 .Wait()/.Result/WaitAll 等待 —— 主线程被阻塞后延续永远跑不到，会卡死编辑器。

    /// <summary>让「打开」可以被人为卡住的工厂：只有 <see cref="Gate"/> 过的面板会挂起，其余立即完成。</summary>
    private sealed class GatedFactory : IPanelFactory
    {
        private readonly HashSet<Type> _gated = new();
        private readonly Dictionary<Type, TaskCompletionSource<IUIPanel>> _pending = new();

        /// <summary>把某个面板的打开卡住，直到 <see cref="Release"/>。</summary>
        public void Gate(Type panelType) => _gated.Add(panelType);

        public IUIPanel Create(PanelDefinition definition) => MakePanel(definition?.PanelType);

        public Task<IUIPanel> CreateAsync(PanelDefinition definition)
        {
            var t = definition?.PanelType;
            if (!_gated.Contains(t)) return Task.FromResult(MakePanel(t));

            var gate = new TaskCompletionSource<IUIPanel>();
            _pending[t] = gate;
            return gate.Task;
        }

        public void Destroy(IUIPanel panel) { }

        /// <summary>让被卡住的面板打开成功落地。</summary>
        public bool Release(Type panelType)
        {
            if (!_pending.TryGetValue(panelType, out var gate)) return false;
            _pending.Remove(panelType);
            return gate.TrySetResult(MakePanel(panelType));
        }

        private static IUIPanel MakePanel(Type t)
            => t == typeof(PanelA) ? new PanelA()
             : t == typeof(PanelB) ? new PanelB()
             : t == typeof(PanelC) ? new PanelC()
             : null;
    }

    private static (UIService Ui, GatedFactory Factory, PopupService Popups) MakeGated(bool gateA = true, float timeoutSeconds = 0f)
    {
        var factory = new GatedFactory();
        if (gateA) factory.Gate(typeof(PanelA));

        var ui = new UIService(factory, null, null);
        ui.Register(Def(typeof(PanelA)));
        ui.Register(Def(typeof(PanelB)));

        var settings = new UISettings { PopupQueueTimeoutSeconds = timeoutSeconds };
        return (ui, factory, new PopupService(ui, null, null, settings));
    }

    [Test]
    public void PendingCount_CountsQueuedAndInFlightItems()
    {
        var (_, _, popups) = MakeGated();

        Assert.AreEqual(0, popups.PendingCount);

        var a = popups.PushAsync<IPanelA>();   // 卡在「打开中」
        Assert.IsFalse(a.IsCompleted);
        Assert.AreEqual(1, popups.PendingCount, "正在打开的那一项也要算进去");

        popups.PushAsync<IPanelB>();           // 排队
        Assert.AreEqual(2, popups.PendingCount);
    }

    [UnityTest]
    public IEnumerator PushAsync_WhenOpenIsSlow_KeepsCallOrder()
    {
        var (_, factory, popups) = MakeGated();

        var a = popups.PushAsync<IPanelA>();
        var b = popups.PushAsync<IPanelB>();
        Assert.IsFalse(b.IsCompleted, "A 的打开未完成前，B 不应开始处理");

        factory.Release(typeof(PanelA));

        for (int i = 0; i < 120 && !b.IsCompleted; i++) yield return null;

        Assert.IsTrue(a.IsCompleted && b.IsCompleted);
        Assert.IsNotNull(a.Result);
        Assert.AreEqual(2, popups.Depth);
        Assert.IsNotNull(popups.Peek<IPanelB>(), "后调用的 PushAsync 应在栈顶（调用顺序 = 栈顺序）");
        Assert.AreEqual(0, popups.PendingCount);
    }

    [UnityTest]
    public IEnumerator PopAll_CancelsQueuedAndInFlightItems()
    {
        var (_, factory, popups) = MakeGated();

        var a = popups.PushAsync<IPanelA>();   // 飞行中
        var b = popups.PushAsync<IPanelB>();   // 排队
        Assert.AreEqual(2, popups.PendingCount);

        popups.PopAll();

        Assert.IsTrue(b.IsCompleted, "队列里的项会被当场摘出队列并以 null 完成");
        Assert.IsNull(b.Result);
        Assert.AreEqual(1, popups.PendingCount, "A 仍在飞行（已标记取消，等它的打开落地）");

        factory.Release(typeof(PanelA));

        for (int i = 0; i < 120 && !a.IsCompleted; i++) yield return null;

        Assert.IsTrue(a.IsCompleted);
        Assert.IsNull(a.Result, "被取消的项以 null 完成（不抛异常）");
        Assert.AreEqual(0, popups.Depth, "被取消的项不得入栈、不得显示");
        Assert.AreEqual(0, popups.PendingCount);
    }

    [UnityTest]
    public IEnumerator Pop_WhileInFlight_CancelsPendingPush()
    {
        var (ui, factory, popups) = MakeGated();

        popups.Push<IPanelB>();                       // B 未被卡住 → 同步打开，栈里先有一个
        Assert.AreEqual(1, popups.Depth);

        var pending = popups.PushAsync<IPanelA>();    // A 被卡住 → 飞行中
        Assert.IsFalse(pending.IsCompleted);
        Assert.AreEqual(1, popups.PendingCount, "只有异步 Push 进队列（同步 Push 不占队列）");

        popups.Pop();                                 // 返回键：栈顶出栈，且应作废待处理项

        Assert.AreEqual(0, popups.Depth, "Pop 本身仍然弹出栈顶");

        factory.Release(typeof(PanelA));
        for (int i = 0; i < 120 && !pending.IsCompleted; i++) yield return null;

        Assert.IsTrue(pending.IsCompleted);
        Assert.IsNull(pending.Result, "被 Pop 作废的 PushAsync 以 null 完成");
        Assert.IsTrue(ui.IsCached(typeof(PanelA)), "迟到的打开确实把面板建出来了");
        Assert.IsFalse(ui.IsOpen(typeof(PanelA)), "但被取消 → 必须关掉，不能留在屏幕上");
        Assert.AreEqual(0, popups.Depth, "被取消的项不得入栈");
        Assert.AreEqual(0, popups.PendingCount);
    }

    [UnityTest]
    public IEnumerator Timeout_SkipsHungOpen_AndQueueKeepsGoing()
    {
        var (_, _, popups) = MakeGated(gateA: true, timeoutSeconds: 0.02f);

        var hung = popups.PushAsync<IPanelA>();   // 永远不完成
        var next = popups.PushAsync<IPanelB>();   // 不能被卡住的 A 堵死

        for (int i = 0; i < 900 && !next.IsCompleted; i++) yield return null;

        Assert.IsTrue(hung.IsCompleted, "超时后应以 null 完成");
        Assert.IsNull(hung.Result);
        Assert.IsNotNull(next.Result, "超时兜底的意义：整条队列不能被一个卡住的项堵死");
        Assert.IsNotNull(popups.Peek<IPanelB>());
        Assert.AreEqual(1, popups.Depth);
        Assert.AreEqual(0, popups.PendingCount);
    }

    [UnityTest]
    public IEnumerator Timeout_ClosesPanelThatArrivesLate()
    {
        var (ui, factory, popups) = MakeGated(gateA: true, timeoutSeconds: 0.02f);

        var hung = popups.PushAsync<IPanelA>();

        for (int i = 0; i < 900 && !hung.IsCompleted; i++) yield return null;
        Assert.IsNull(hung.Result);
        Assert.AreEqual(0, popups.Depth);

        // 超时之后那次打开才姗姗来迟：它已经把面板创建并 Show 出来了，但不在弹窗栈里
        factory.Release(typeof(PanelA));

        for (int i = 0; i < 120 && !ui.IsCached(typeof(PanelA)); i++) yield return null;
        yield return null;
        yield return null;

        Assert.IsTrue(ui.IsCached(typeof(PanelA)), "迟到的打开确实把面板实例建出来了");
        Assert.IsFalse(ui.IsOpen(typeof(PanelA)), "但它必须被关掉，不能变成一个无主的可见面板");
        Assert.AreEqual(0, popups.Depth);
    }
}
