using System.Collections.Generic;
using System.IO;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Scenes.Config
{
    /// <summary>配置校验结果：既写入日志，也供测试断言（避免测试去解析日志文本）。</summary>
    public sealed class SceneConfigValidationResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// 场景配置的启动期校验（设计稿 §6：V1–V8）。
    /// <para>原则：**能自动发现的问题尽早报出来**，而不是等运行期加载场景时才失败。</para>
    /// </summary>
    public static class SceneConfigValidation
    {
        /// <param name="checkBuildSettings">
        /// 是否检查「场景是否已启用地列在 Build Settings 中」。
        /// 传 <c>null</c>（默认）时按配置的 <see cref="SceneConfigAssetSO.registration"/> 自动决定 ——
        /// 只有「用 Build Settings 加载」时这项才适用（Addressables 路径下 Unity 本就会禁用 Build Settings 条目）。
        /// </param>
        public static SceneConfigValidationResult Validate(
            ISceneCatalog catalog,
            SceneConfigAssetSO data,
            bool? checkBuildSettings = null)
        {
            var result = new SceneConfigValidationResult();
            if (catalog == null || data == null) return result;

            bool checkBuild = checkBuildSettings ?? (data.registration == SceneRegistration.BuildSettings);

            ValidateSceneEntries(catalog, data, result, checkBuild);
            ValidateEndpoints(catalog, data, result);

            // V8：策略前置条件 —— 这两种策略都依赖「旧场景释放后的独立画面」，必须配 Loading 场景。
            //     （相机是否存在只能运行期检查，见 SceneFlow.WarnIfLoadingSceneHasNoCamera）
            if ((data.transitionPolicy == SceneTransitionPolicy.ThroughLoadingScene ||
                 data.transitionPolicy == SceneTransitionPolicy.AdditiveUnloadFirst) &&
                Trimmed(data.loadingSceneName) == null)
            {
                result.Warnings.Add($"transitionPolicy = {data.transitionPolicy} 需要配置 loadingSceneName" +
                                    "（旧场景释放后要有独立画面；否则过渡期无场景/无相机渲染）");
            }

            // V9：Single 系策略不支持常驻场景（Single 会摧毁其它所有场景）
            if (data.transitionPolicy == SceneTransitionPolicy.SingleDirect ||
                data.transitionPolicy == SceneTransitionPolicy.ThroughLoadingScene)
            {
                var persistent = new List<string>();
                foreach (var item in data.scenes)
                {
                    if (item == null) continue;
                    var name = Trimmed(item.sceneName);
                    if (name != null && item.persistent && !persistent.Contains(name)) persistent.Add(name);
                }

                if (persistent.Count > 0)
                    result.Warnings.Add($"transitionPolicy = {data.transitionPolicy} 不支持常驻（persistent）场景：" +
                                        $"{string.Join("、", persistent.ToArray())}（Single 会摧毁它们；请改用 Additive / AdditiveUnloadFirst）");
            }

            foreach (var error in result.Errors) Log.Error($"[Scenes] {error}");
            foreach (var warning in result.Warnings) Log.Warn($"[Scenes] {warning}");

            return result;
        }

        /// <summary>V5 / V6：条目级校验（重复名、场景名为空、Build Settings 缺失）。</summary>
        private static void ValidateSceneEntries(
            ISceneCatalog catalog,
            SceneConfigAssetSO data,
            SceneConfigValidationResult result,
            bool checkBuildSettings)
        {
            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            var missingInBuildSettings = new List<string>();
            bool hasEmptyName = false;

            foreach (var item in data.scenes)
            {
                if (item == null) continue;

                var name = Trimmed(item.sceneName);
                if (name == null)
                {
                    hasEmptyName = true;
                    continue;
                }

                if (!seen.Add(name) && !duplicates.Contains(name)) duplicates.Add(name);

                if (checkBuildSettings && !IsInBuildSettings(name) && !missingInBuildSettings.Contains(name))
                    missingInBuildSettings.Add(name);
            }

            if (hasEmptyName)
                result.Warnings.Add("存在 sceneName 为空的场景配置项（已跳过）");

            if (duplicates.Count > 0)
                result.Errors.Add($"存在重复的场景名：{string.Join("、", duplicates.ToArray())}（同名条目后者会覆盖前者）");

            if (missingInBuildSettings.Count > 0)
                result.Warnings.Add($"以下场景未在 Build Settings 中启用：{string.Join("、", missingInBuildSettings.ToArray())}" +
                                    "（UnitySceneService 路径下加载会失败；可用编辑器同步工具一键补齐）");
        }

        /// <summary>V1 / V2 / V4：入口与 Loading 场景的登记情况。</summary>
        private static void ValidateEndpoints(
            ISceneCatalog catalog,
            SceneConfigAssetSO data,
            SceneConfigValidationResult result)
        {
            var entry = Trimmed(data.entrySceneName);
            if (entry == null)
                result.Warnings.Add("未配置入口场景，将由启动流程回退到 Build Settings 第 0 个场景");
            else if (!catalog.TryGet(entry, out _))
                result.Warnings.Add($"入口场景未在场景目录中登记：{entry}（将使用默认策略）");

            var loading = Trimmed(data.loadingSceneName);
            if (loading != null && !catalog.TryGet(loading, out _))
                result.Warnings.Add($"Loading 场景未在场景目录中登记：{loading}（将使用默认策略）");
        }

        /// <summary>
        /// 场景名是否已在 Build Settings 中**启用**（按文件名比对，忽略路径与扩展名）。
        /// <para>
        /// ⚠️ 这是**运行期**判定（基于 <c>SceneManager.sceneCountInBuildSettings</c>，打包后准确）。
        /// 编辑器下它**不反映**每个条目的 <c>enabled</c> 标志（已标记为 Addressable 的场景会被 Unity
        /// 自动置为 <c>enabled=false</c>，但这里仍会算上）—— 如果你需要编辑器下的精确判定，
        /// 请用编辑器侧的 `SceneNameUtility` / `SceneConfigSynchronizer`（它们直接读 `EditorBuildSettings`）。
        /// </para>
        /// <para>因此本项校验只在 <c>registration == BuildSettings</c> 时才执行，见 <see cref="Validate"/>。</para>
        /// </summary>
        public static bool IsInBuildSettings(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                if (Path.GetFileNameWithoutExtension(path) == sceneName) return true;
            }
            return false;
        }

        private static string Trimmed(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
