using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Scenes;
using Game.Scenes.Config;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 场景配置的单元测试：SO → <see cref="SceneDefinition"/> 映射 + 启动期校验规则（设计稿 §6 V1–V7）。
/// <para>校验里「是否已加入 Build Settings」那条默认关闭，避免测试结果随项目 Build Settings 变化。</para>
/// </summary>
public class SceneConfigTests
{
    private readonly List<Object> _spawned = new();

    [TearDown]
    public void CleanupSpawnedAssets()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
        }
        _spawned.Clear();
    }

    // ---------------- SO → SceneDefinition ----------------

    [Test]
    public void ToDefinition_MapsAllFields()
    {
        var so = MakeSceneConfig("Level2", persistent: true, showLoading: false);
        so.address = "Scenes/Level2";
        so.preloadAddresses = new List<string> { "Cube" };
        so.tags = new List<string> { "level", "combat" };
        so.waitTimeoutSeconds = 3f;

        var definition = so.ToDefinition();

        Assert.AreEqual("Level2", definition.Name);
        Assert.AreEqual("Scenes/Level2", definition.Address);
        Assert.IsTrue(definition.Persistent);
        Assert.IsFalse(definition.ShowLoading);
        CollectionAssert.AreEqual(new[] { "Cube" }, definition.PreloadAddresses);
        Assert.IsTrue(definition.HasTag("combat"));
        Assert.AreEqual(3f, definition.WaitTimeoutSeconds);
    }

    [Test]
    public void ToDefinition_WhenAddressEmpty_FallsBackToSceneName()
    {
        var so = MakeSceneConfig("SampleScene");
        so.address = "   ";

        Assert.AreEqual("SampleScene", so.ToDefinition().Address);
    }

    [Test]
    public void ToDefinition_WhenSceneNameEmpty_ReturnsNull()
    {
        var so = MakeSceneConfig(string.Empty);
        Assert.IsNull(so.ToDefinition(), "场景名为空的条目应被调用方跳过");

        var blank = MakeSceneConfig("   ");
        Assert.IsNull(blank.ToDefinition());
    }

    // ---------------- 校验规则 ----------------

    [Test]
    public void Validation_DuplicateSceneName_IsAnError()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"), MakeSceneConfig("A"));

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.IsTrue(result.HasErrors, "重复场景名应记为错误");
        Assert.That(result.Errors[0], Does.Contain("A"));
    }

    [Test]
    public void Validation_EntrySceneNotRegistered_IsAWarning()
    {
        var (catalog, data) = MakeCatalog("NotRegistered", MakeSceneConfig("A"));

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.IsFalse(result.HasErrors, "入口场景未登记只警告（可回退默认策略）");
        Assert.That(result.Warnings, Has.Some.Contains("入口场景未在场景目录中登记"));
    }

    [Test]
    public void Validation_EmptyEntryScene_IsAWarningAboutBuildSettingsFallback()
    {
        var (catalog, data) = MakeCatalog(null, MakeSceneConfig("A"));

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.IsFalse(result.HasErrors);
        Assert.That(result.Warnings, Has.Some.Contains("Build Settings 第 0 个场景"));
    }

    [Test]
    public void Validation_LoadingSceneNotRegistered_IsAWarning()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"));
        data.loadingSceneName = "LoadingScene";

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.IsFalse(result.HasErrors);
        Assert.That(result.Warnings, Has.Some.Contains("Loading 场景未在场景目录中登记"));
    }

    [Test]
    public void Validation_EmptySceneNameEntry_IsReported()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"), MakeSceneConfig(string.Empty));

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.That(result.Warnings, Has.Some.Contains("sceneName 为空的场景配置项"));
    }

    [Test]
    public void Validation_ThroughLoadingSceneWithoutLoadingScene_Warns()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"));
        data.transitionPolicy = SceneTransitionPolicy.ThroughLoadingScene;

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.That(result.Warnings, Has.Some.Contains("需要配置 loadingSceneName"));
    }

    [Test]
    public void Validation_ThroughLoadingSceneWithLoadingScene_DoesNotWarnForThat()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"), MakeSceneConfig("LoadingScene"));
        data.transitionPolicy = SceneTransitionPolicy.ThroughLoadingScene;
        data.loadingSceneName = "LoadingScene";

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.That(result.Warnings, Has.None.Contains("需要配置 loadingSceneName"));
    }

    [Test]
    public void Validation_SinglePolicyWithPersistentScene_WarnsAboutUnsupported()
    {
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A", persistent: true));
        data.transitionPolicy = SceneTransitionPolicy.SingleDirect;

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: false);

        Assert.That(result.Warnings, Has.Some.Contains("不支持常驻"),
            "Single 系策略会摧毁常驻场景 → 必须给出告警");
    }

    [Test]
    public void Validation_CheckBuildSettings_FindsMissingScene()
    {
        var missing = "DefinitelyNotInBuildSettings_" + System.Guid.NewGuid().ToString("N");
        var (catalog, data) = MakeCatalog("A", MakeSceneConfig("A"), MakeSceneConfig(missing));

        var result = SceneConfigValidation.Validate(catalog, data, checkBuildSettings: true);

        Assert.That(result.Warnings, Has.Some.Contains("未在 Build Settings 中启用"));
        Assert.That(result.Warnings, Has.Some.Contains(missing));
    }

    [Test]
    public void IsInBuildSettings_IsFalseForUnknownScene()
    {
        Assert.IsFalse(SceneConfigValidation.IsInBuildSettings("DefinitelyNotARealScene"));
        Assert.IsFalse(SceneConfigValidation.IsInBuildSettings(null));
    }

    // ---------------- 构造辅助 ----------------

    private SceneConfigSO MakeSceneConfig(
        string sceneName,
        bool persistent = false,
        bool showLoading = true)
    {
        var so = ScriptableObject.CreateInstance<SceneConfigSO>();
        _spawned.Add(so);
        so.sceneName = sceneName;
        so.persistent = persistent;
        so.showLoading = showLoading;
        return so;
    }

    private (ISceneCatalog, SceneConfigAssetSO) MakeCatalog(string entryScene, params SceneConfigSO[] scenes)
    {
        var catalog = new SceneCatalog();
        var data = ScriptableObject.CreateInstance<SceneConfigAssetSO>();
        _spawned.Add(data);

        data.entrySceneName = entryScene;
        data.scenes = new List<SceneConfigSO>(scenes);

        // 模拟 SceneCatalogSetupStep 的填充过程（校验依赖已登记的目录）
        foreach (var so in scenes)
        {
            var definition = so != null ? so.ToDefinition() : null;
            if (definition != null) catalog.Register(definition);
        }

        return (catalog, data);
    }
}
