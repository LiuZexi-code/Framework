using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions;
using Framework.Abstractions.Events;
using Framework.Abstractions.Input;
using Framework.Abstractions.Logging;
using Framework.Abstractions.Scenes;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;
using Framework.UI;
using NUnit.Framework;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

/// <summary>
/// UIService 的注册/打开/关闭/缓存策略测试（使用假工厂，不依赖 Addressables）。
/// </summary>
public class UIServiceTests
{
    private interface ITestPanel : IUIPanel { }

    private sealed class TestPanel : ITestPanel, IUIPanelLifecycle
    {
        public bool Created, Shown, Hidden, Destroyed;

        void IUIPanelLifecycle.Create(IEventBus bus, ILogger logger) => Created = true;
        void IUIPanelLifecycle.Show() => Shown = true;
        void IUIPanelLifecycle.Hide() => Hidden = true;
        void IUIPanelLifecycle.Destroy() => Destroyed = true;
    }

    /// <summary>为「进入的场景」声明的面板（回归：<c>DisposeScenePanels</c> 不应销毁它）。</summary>
    private sealed class TaggedPanel : IUIPanel, IUIPanelLifecycle
    {
        void IUIPanelLifecycle.Create(IEventBus bus, ILogger logger) { }
        void IUIPanelLifecycle.Show() { }
        void IUIPanelLifecycle.Hide() { }
        void IUIPanelLifecycle.Destroy() { }
    }

    /// <summary>只为「离开的场景」声明的面板（应被销毁）。</summary>
    private sealed class OtherPanel : IUIPanel, IUIPanelLifecycle
    {
        void IUIPanelLifecycle.Create(IEventBus bus, ILogger logger) { }
        void IUIPanelLifecycle.Show() { }
        void IUIPanelLifecycle.Hide() { }
        void IUIPanelLifecycle.Destroy() { }
    }

    private sealed class FakeFactory : IPanelFactory
    {
        public int CreatedCount;
        public int DestroyedCount;

        /// <summary>按 <c>definition.PanelType</c> 实例化，以支持一个测试里注册多种面板类型。</summary>
        public IUIPanel Create(PanelDefinition definition)
        {
            CreatedCount++;
            return (IUIPanel)Activator.CreateInstance(definition.PanelType);
        }

        public Task<IUIPanel> CreateAsync(PanelDefinition definition)
            => Task.FromResult(Create(definition));

        public void Destroy(IUIPanel panel) => DestroyedCount++;
    }

    private static PanelDefinition Def(bool cacheOnClose = true)
        => new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, cacheOnClose, null);

    [Test]
    public void Open_Unregistered_ReturnsNull()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        Assert.IsNull(svc.Open<ITestPanel>());
    }

    [Test]
    public void Open_ByInterface_ResolvesRegisteredConcreteType()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        svc.Register(Def());

        var panel = svc.Open<ITestPanel>();

        Assert.IsNotNull(panel);
        Assert.IsTrue(svc.IsOpen<ITestPanel>());
    }

    [Test]
    public void Open_Twice_ReusesSameInstance()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null);
        svc.Register(Def());

        svc.Open<ITestPanel>();
        svc.Open<ITestPanel>();

        Assert.AreEqual(1, f.CreatedCount);
    }

    [Test]
    public void Close_WithCacheOnClose_KeepsInstanceButHides()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null);
        svc.Register(Def(true));

        svc.Open<ITestPanel>();
        svc.Close<ITestPanel>();

        Assert.IsFalse(svc.IsOpen<ITestPanel>());
        Assert.IsTrue(svc.IsCached<ITestPanel>());
        Assert.AreEqual(0, f.DestroyedCount);
    }

    [Test]
    public void Close_WithoutCache_DestroysInstance()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null);
        svc.Register(Def(false));

        svc.Open<ITestPanel>();
        svc.Close<ITestPanel>();

        Assert.IsFalse(svc.IsCached<ITestPanel>());
        Assert.AreEqual(1, f.DestroyedCount);
    }

    [Test]
    public void Close_Unregistered_DoesNotThrow()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        Assert.DoesNotThrow(() => svc.Close<ITestPanel>());
    }

    [Test]
    public void Register_NullDefinition_DoesNotThrow()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        Assert.DoesNotThrow(() => svc.Register(null));
    }

    // ---------------- 输入上下文自动 Push/Pop ----------------

    /// <summary>记录 Push/Pop 的 IInputService 替身（不需要触发事件，故用空访问器避开 CS0067）。</summary>
    private sealed class RecordingInputService : IInputService
    {
        public readonly List<string> Pushed = new List<string>();
        public readonly List<string> Popped = new List<string>();
        public readonly List<InputContext> Active = new List<InputContext>();

        public IReadOnlyList<InputContext> ActiveContexts => Active;

        public event Action<InputActionEvent> ActionTriggered { add { } remove { } }

        public void PushContext(string contextId, int priority = 0)
        {
            Pushed.Add($"{contextId}:{priority}");
            Active.Add(new InputContext(contextId, priority));
        }

        public void PopContext(string contextId)
        {
            Popped.Add(contextId);
            Active.RemoveAll(c => c.ContextId == contextId);
        }

        public bool IsContextActive(string contextId) => Active.Exists(c => c.ContextId == contextId);

        public bool IsPressed(string actionId, int playerId = 0) => false;
        public bool WasPressedThisFrame(string actionId, int playerId = 0) => false;
        public bool WasReleasedThisFrame(string actionId, int playerId = 0) => false;
        public float GetAxis(string actionId, int playerId = 0) => 0f;
        public Vector2 GetVector2(string actionId, int playerId = 0) => default;
        public void Dispose() { }
    }

    private static PanelDefinition InputDef(string contextId, int priority = 0)
        => new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, true, null,
                               LoadPolicy.OnDemand, 0, false, contextId, priority);

    [Test]
    public void Open_WithInputContext_PushesOnOpen_PopsOnClose()
    {
        var input = new RecordingInputService();
        var svc = new UIService(new FakeFactory(), null, null, null, null, input);
        svc.Register(InputDef("UI", 100));

        svc.Open<ITestPanel>();

        CollectionAssert.AreEqual(new[] { "UI:100" }, input.Pushed);
        Assert.IsTrue(input.IsContextActive("UI"));

        svc.Close<ITestPanel>();

        CollectionAssert.AreEqual(new[] { "UI" }, input.Popped);
        Assert.IsFalse(input.IsContextActive("UI"));
    }

    [Test]
    public void Open_WithoutInputContext_DoesNotTouchInput()
    {
        var input = new RecordingInputService();
        var svc = new UIService(new FakeFactory(), null, null, null, null, input);
        svc.Register(Def());

        svc.Open<ITestPanel>();
        svc.Close<ITestPanel>();

        Assert.AreEqual(0, input.Pushed.Count);
        Assert.AreEqual(0, input.Popped.Count);
    }

    [Test]
    public void Open_SamePanelTwice_PushesOnlyOnce()
    {
        var input = new RecordingInputService();
        var svc = new UIService(new FakeFactory(), null, null, null, null, input);
        svc.Register(InputDef("UI", 100));

        svc.Open<ITestPanel>();
        svc.Open<ITestPanel>();   // 复用缓存实例

        Assert.AreEqual(1, input.Pushed.Count, "重复打开不应重复 Push");
    }

    [Test]
    public void CloseAll_PopsInputContexts()
    {
        var input = new RecordingInputService();
        var svc = new UIService(new FakeFactory(), null, null, null, null, input);
        svc.Register(InputDef("UI", 100));

        svc.Open<ITestPanel>();
        svc.CloseAll();

        Assert.AreEqual(0, input.Active.Count);
        Assert.AreEqual(1, input.Popped.Count);
    }

    [Test]
    public void DisposeScenePanels_PopsInputContextOfDestroyedPanel()
    {
        var input = new RecordingInputService();
        var svc = new UIService(new FakeFactory(), null, null, null, null, input);
        svc.Register(new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, true,
                                        new List<string> { "SampleScene" },
                                        LoadPolicy.OnDemand, 0, false, "UI", 100));

        svc.Open<ITestPanel>();
        Assert.IsTrue(input.IsContextActive("UI"));

        svc.DisposeScenePanels("Level2");   // 进入别的场景 → 面板被销毁 → 上下文应被自动弹掉

        Assert.IsFalse(input.IsContextActive("UI"), "面板被销毁时不应把输入上下文泄漏在栈上");
    }

    [Test]
    public void Open_WithNoInputService_DoesNotThrow()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        svc.Register(InputDef("UI", 100));

        Assert.DoesNotThrow(() => svc.Open<ITestPanel>());
        Assert.DoesNotThrow(() => svc.Close<ITestPanel>());
    }

    // ---------------- 场景标签校验（SceneTags）----------------

#pragma warning disable 0067   // 替身不触发事件
    /// <summary>最小 ISceneService 替身：只提供 ActiveSceneName。</summary>
    private sealed class StubSceneService : ISceneService
    {
        public string ActiveSceneName { get; set; } = "SampleScene";

        public IReadOnlyList<string> LoadedSceneNames => new List<string> { ActiveSceneName };

        public event Action<SceneLoadedEvent> SceneLoaded;
        public event Action<SceneUnloadedEvent> SceneUnloaded;

        public ISceneLoadOperation LoadAsync(string sceneName, SceneLoadMode mode) => throw new NotSupportedException();
        public IEnumerator UnloadAsync(string sceneName) => throw new NotSupportedException();
        public bool SetActiveScene(string sceneName) => throw new NotSupportedException();
        public GameObject[] GetRootGameObjects(string sceneName) => Array.Empty<GameObject>();
        public bool IsSceneLoaded(string sceneName) => false;
        public void Dispose() { }
    }
#pragma warning restore 0067

    private static PanelDefinition TaggedDef(params string[] tags)
        => new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, true, new List<string>(tags));

    /// <summary>
    /// 阶段 B 后 <c>IsGlobal</c> 是**派生**属性（SceneTags 为空 ⇒ 全局），不再有独立开关。
    /// 这里锁住这个合并语义，防止以后又添回一个会和 SceneTags 打架的字段。
    /// </summary>
    [Test]
    public void IsGlobal_IsDerived_FromSceneTags()
    {
        Assert.IsTrue(new PanelDefinition(typeof(TestPanel), "a", PanelLayer.Normal, true, null).IsGlobal,
            "SceneTags == null ⇒ 全局");
        Assert.IsTrue(new PanelDefinition(typeof(TestPanel), "a", PanelLayer.Normal, true, new List<string>()).IsGlobal,
            "SceneTags 为空集合 ⇒ 全局");
        Assert.IsFalse(new PanelDefinition(typeof(TestPanel), "a", PanelLayer.Normal, true, new List<string> { "SampleScene" }).IsGlobal,
            "声明了场景标签 ⇒ 非全局");
    }

    [Test]
    public void Open_WithMatchingSceneTag_Opens()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "SampleScene" });
        svc.Register(TaggedDef("SampleScene"));

        Assert.IsNotNull(svc.Open<ITestPanel>());
        Assert.AreEqual(1, f.CreatedCount);
    }

    [Test]
    public void Open_WithMismatchedSceneTag_IsRefused()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "Level2" });
        svc.Register(TaggedDef("SampleScene"));

        Assert.IsNull(svc.Open<ITestPanel>(), "当前场景不在 SceneTags 内时应拒绍打开");
        Assert.AreEqual(0, f.CreatedCount, "被拒绍时不应创建实例");
        Assert.IsFalse(svc.IsCached<ITestPanel>());
    }

    /// <summary>
    /// 回归：<c>DisposeScenePanels(enteringScene)</c> 必须**只**销毁「不属于新场景」的实例。
    /// 之前无差别销毁所有非全局面板 → 为进入场景预加载的面板被紧接着的「离开场景释放」白白销毁。
    /// </summary>
    [Test]
    public void DisposeScenePanels_KeepsGlobalAndEnteringScenePanels_DestroysOthers()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        svc.Register(Def());                                     // 无标签 → 全局，永远活
        svc.Register(new PanelDefinition(typeof(TaggedPanel), "a", PanelLayer.Normal, true,
                                          new List<string> { "B" }));   // 为**进入**的场景 B 声明
        svc.Register(new PanelDefinition(typeof(OtherPanel), "c", PanelLayer.Normal, true,
                                          new List<string> { "A" }));   // 只为**离开**的场景 A 声明

        svc.Open<ITestPanel>();
        svc.Open<TaggedPanel>();
        svc.Open<OtherPanel>();
        Assert.IsTrue(svc.IsCached<ITestPanel>() && svc.IsCached<TaggedPanel>() && svc.IsCached<OtherPanel>());

        svc.DisposeScenePanels("B");   // A → B

        Assert.IsTrue(svc.IsCached<ITestPanel>(), "无标签（全局）面板必须保留");
        Assert.IsTrue(svc.IsCached<TaggedPanel>(), "为**进入场景**预加载的面板必须保留（本次修复的核心）");
        Assert.IsFalse(svc.IsCached<OtherPanel>(), "只为离开场景声明的面板应被销毁");
    }

    /// <summary>拿不到目标场景名时**保守存活**（宁可不销毁，也不要误删）。</summary>
    [Test]
    public void DisposeScenePanels_WhenEnteringSceneUnknown_KeepsEverything()
    {
        var svc = new UIService(new FakeFactory(), null, null);
        svc.Register(new PanelDefinition(typeof(OtherPanel), "c", PanelLayer.Normal, true,
                                          new List<string> { "A" }));
        svc.Open<OtherPanel>();

        svc.DisposeScenePanels(null);

        Assert.IsTrue(svc.IsCached<OtherPanel>(), "目标场景未知时应保守存活");
    }

    [Test]
    public void Open_WithMismatchedSceneTag_WhenNotEnforced_StillOpens()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null,
            new StubSceneService { ActiveSceneName = "Level2" },
            new UISettings { EnforceSceneTags = false });
        svc.Register(TaggedDef("SampleScene"));

        Assert.IsNotNull(svc.Open<ITestPanel>(), "关闭强制校验后只警告、仍然打开");
    }

    [Test]
    public void Open_WithoutSceneTags_AlwaysOpens()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "AnyScene" });
        svc.Register(Def());   // sceneTags = null → 所有场景通用

        Assert.IsNotNull(svc.Open<ITestPanel>());
    }
}
