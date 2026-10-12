using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Events;
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

    private sealed class FakeFactory : IPanelFactory
    {
        public int CreatedCount;
        public int DestroyedCount;

        public IUIPanel Create(PanelDefinition definition)
        {
            CreatedCount++;
            return new TestPanel();
        }

        public Task<IUIPanel> CreateAsync(PanelDefinition definition)
            => Task.FromResult(Create(definition));

        public void Destroy(IUIPanel panel) => DestroyedCount++;
    }

    private static PanelDefinition Def(bool cacheOnClose = true)
        => new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, cacheOnClose, false, null);

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

    private static PanelDefinition TaggedDef(bool isGlobal, params string[] tags)
        => new PanelDefinition(typeof(TestPanel), "addr", PanelLayer.Normal, true, isGlobal, new List<string>(tags));

    [Test]
    public void Open_WithMatchingSceneTag_Opens()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "SampleScene" });
        svc.Register(TaggedDef(isGlobal: false, "SampleScene"));

        Assert.IsNotNull(svc.Open<ITestPanel>());
        Assert.AreEqual(1, f.CreatedCount);
    }

    [Test]
    public void Open_WithMismatchedSceneTag_IsRefused()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "Level2" });
        svc.Register(TaggedDef(isGlobal: false, "SampleScene"));

        Assert.IsNull(svc.Open<ITestPanel>(), "当前场景不在 SceneTags 内时应拒绝打开");
        Assert.AreEqual(0, f.CreatedCount, "被拒绝时不应创建实例");
        Assert.IsFalse(svc.IsCached<ITestPanel>());
    }

    [Test]
    public void Open_GlobalPanel_IgnoresSceneTags()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null, new StubSceneService { ActiveSceneName = "Level2" });
        // isGlobal = true → 按约定 SceneTags 对其无效（常驻面板与场景无关）
        svc.Register(TaggedDef(isGlobal: true, "SampleScene"));

        Assert.IsNotNull(svc.Open<ITestPanel>());
    }

    [Test]
    public void Open_WithMismatchedSceneTag_WhenNotEnforced_StillOpens()
    {
        var f = new FakeFactory();
        var svc = new UIService(f, null, null,
            new StubSceneService { ActiveSceneName = "Level2" },
            new UISettings { EnforceSceneTags = false });
        svc.Register(TaggedDef(isGlobal: false, "SampleScene"));

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
