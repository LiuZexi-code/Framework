using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Scenes;
using NUnit.Framework;

/// <summary>
/// <see cref="SceneCatalog"/> 的单元测试：登记 / 查询 / 同名覆盖 / 边界输入。
/// </summary>
public class SceneCatalogTests
{
    [Test]
    public void Register_ThenTryGet_ReturnsDefinition()
    {
        var catalog = new SceneCatalog();
        catalog.Register(new SceneDefinition("Level2", tags: new[] { "level" }));

        Assert.IsTrue(catalog.TryGet("Level2", out var definition));
        Assert.AreEqual("Level2", definition.Name);
        Assert.AreEqual("Level2", definition.Address, "address 未填时应回退为场景名");
        Assert.IsTrue(definition.HasTag("level"));
        Assert.AreEqual(1, catalog.All.Count);
    }

    [Test]
    public void TryGet_ForUnknownScene_ReturnsFalse()
    {
        var catalog = new SceneCatalog();

        Assert.IsFalse(catalog.TryGet("Nope", out var definition));
        Assert.IsNull(definition);
        Assert.IsNull(catalog.Get("Nope"), "Get 对未登记场景返回 null（不抛异常）");
        Assert.IsFalse(catalog.TryGet(null, out _), "null 场景名不应抛异常");
        Assert.IsFalse(catalog.TryGet(string.Empty, out _));
    }

    [Test]
    public void Register_SameNameTwice_OverwritesInPlaceAndKeepsOrder()
    {
        var catalog = new SceneCatalog();
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));
        catalog.Register(new SceneDefinition("A", persistent: true));   // 覆盖

        Assert.AreEqual(2, catalog.All.Count, "同名覆盖不应新增条目");
        CollectionAssert.AreEqual(new[] { "A", "B" }, Names(catalog), "覆盖应保留原位置");
        Assert.IsTrue(catalog.Get("A").Persistent, "应取到后登记的定义");
    }

    [Test]
    public void Register_NullOrEmptyName_IsIgnored()
    {
        var catalog = new SceneCatalog();
        catalog.Register(null);
        catalog.Register(new SceneDefinition(null));
        catalog.Register(new SceneDefinition(string.Empty));

        Assert.AreEqual(0, catalog.All.Count);
    }

    [Test]
    public void Defaults_ArePermissive()
    {
        var catalog = new SceneCatalog();

        Assert.AreEqual(SceneTransitionPolicy.SingleDirect, catalog.TransitionPolicy,
            "默认策略：直接 Single 加载（无共存窗口，不需要过渡补丁）");
        Assert.IsFalse(catalog.Strict, "默认宽松：未登记场景只告警不拦截（迁移友好）");
        Assert.IsTrue(string.IsNullOrEmpty(catalog.EntrySceneName), "默认无入口场景（由启动流程自行决定）");
        Assert.IsTrue(string.IsNullOrEmpty(catalog.LoadingSceneName), "默认无 Loading 场景");
        Assert.IsEmpty(catalog.All);
    }

    private static string[] Names(ISceneCatalog catalog)
    {
        var names = new List<string>();
        foreach (var definition in catalog.All) names.Add(definition.Name);
        return names.ToArray();
    }
}
