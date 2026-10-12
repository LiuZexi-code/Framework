using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions;
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;
using Framework.UI;
using NUnit.Framework;
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

    private static UIService MakeUI(bool global = true)
    {
        var ui = new UIService(new FakeFactory(), null, null);
        ui.Register(Def(typeof(PanelA), global));
        ui.Register(Def(typeof(PanelB), global));
        ui.Register(Def(typeof(PanelC), global));
        return ui;
    }

    private static PanelDefinition Def(Type type, bool isGlobal, bool isModal = false, int priority = 0)
        => new PanelDefinition(type, "addr", PanelLayer.Popup, true, isGlobal, null,
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
        ui.Register(Def(typeof(PanelA), isGlobal: false));   // PerScene
        ui.Register(Def(typeof(PanelB), isGlobal: true));    // 全局 → 存活
        PreloadAll(ui);

        var popups = MakePopups(ui);
        popups.Push<IPanelA>();
        popups.Push<IPanelB>();
        Assert.AreEqual(2, popups.Depth);

        ui.DisposeScenePanels();   // 只销毁非全局面板

        Assert.AreEqual(1, popups.Depth, "被销毁的 PerScene 面板应被剔除，全局面板保留");
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
}
