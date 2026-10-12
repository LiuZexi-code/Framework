using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using UnityEngine;

namespace Game.Scenes.Config
{
    /// <summary>
    /// 场景的登记方式。
    /// <para>
    /// ⚠ Unity 强制二者**互斥**：场景一旦标记为 Addressable，Unity 会**自动把它在 Build Settings 中禁用**
    /// （保留条目但 <c>enabled=false</c>）。所以一个项目只能选一条路。
    /// </para>
    /// </summary>
    public enum SceneRegistration
    {
        /// <summary>用 Addressables（对应 <c>AddressablesSceneService</c>，支持远端/热更）。</summary>
        Addressables = 0,

        /// <summary>用 Build Settings（对应 <c>UnitySceneService</c>）。</summary>
        BuildSettings = 1,
    }

    /// <summary>
    /// 场景配置总表：入口场景 / Loading 场景 / 切换策略 + 全部场景条目。
    /// <para>
    /// 与 <c>UIConfigAssetSO</c> 完全对称：由 <see cref="SceneConfigFactory"/> 注册，
    /// <c>GameConfigRegistry</c> 自动发现，启动管线加载后由 <c>SceneCatalogSetupStep</c> 转换为运行期目录。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Config/Scene Catalog", fileName = "SceneCatalog")]
    public sealed class SceneConfigAssetSO : ScriptableObject
    {
        [SceneName]
        [Tooltip("入口场景（启动后进入）；留空则回退到 Build Settings 第 0 个场景")]
        public string entrySceneName;

        [SceneName]
        [Tooltip("常驻 Loading 场景；留空表示不使用 Loading 场景")]
        public string loadingSceneName;

        [Tooltip("切换策略（全局）：统一决定「旧场景何时释放」与「是否经过中间 Loading 场景」。" +
                 "SingleDirect=直接 Single（推荐，无共存窗口）；ThroughLoadingScene=经 Loading 场景（A→Loading→B，峰值最低）；" +
                 "Additive / AdditiveUnloadFirst = 逃生舱（只有它们支持常驻场景/子场景）")]
        public SceneTransitionPolicy transitionPolicy = SceneTransitionPolicy.SingleDirect;

        [Tooltip("场景登记方式（Build Settings 与 Addressables 互斥，只能选一条）")]
        public SceneRegistration registration = SceneRegistration.Addressables;

        [Tooltip("未登记场景是否视为错误；推荐稳定后勾选（尽早发现拼写错误）。迁移期可先不勾")]
        public bool strict;

        [Tooltip("全部场景条目")]
        public List<SceneConfigSO> scenes = new();
    }
}
