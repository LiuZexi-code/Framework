using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Scenes;
using Game.Scenes.Config;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 场景配置的**编辑器同步工具**：按 <see cref="SceneConfigAssetSO.registration"/> 把场景一键登记到
    /// Addressables 或 Build Settings，并跑一遍配置校验。
    /// <para>
    /// 这是配置化最实际的收益 —— 从此不用再手工登记场景（漏登记会在运行期加载失败）。
    /// 要求**幂等**：重复执行不产生重复条目、不改动既有顺序。
    /// </para>
    /// <para>
    /// ⚠ Unity 强制二者互斥：场景一旦标记为 Addressable，Unity 会自动把它在 Build Settings 中**禁用**。
    /// 因此本工具不会「两边都写」，而是按配置的登记方式只走一条路。
    /// </para>
    /// </summary>
    public static class SceneConfigSynchronizer
    {
        private const string CatalogAddress = "Assets/Scripts/Game/Scenes/Config/SceneCatalog.asset";

        [MenuItem("Tools/我的工具/场景配置/同步场景登记")]
        private static void SyncFromMenu()
        {
            var data = FindCatalogAsset();
            if (data == null)
            {
                Debug.LogError($"[Scenes] 未找到场景配置总表：{CatalogAddress}");
                return;
            }
            Sync(data);
        }

        /// <summary>按配置的登记方式同步场景登记（幂等）。</summary>
        public static void Sync(SceneConfigAssetSO data)
        {
            if (data == null) return;

            int added = 0;
            int restored = 0;

            if (data.registration == SceneRegistration.BuildSettings)
                SyncToBuildSettings(data, ref added, ref restored);
            else
                SyncToAddressables(data, ref added);

            AssetDatabase.SaveAssets();

            string detail = data.registration == SceneRegistration.BuildSettings
                ? $"Build Settings 新增 {added} 个、重新启用 {restored} 个"
                : $"Addressables 新增 {added} 个";
            Debug.Log($"[Scenes] 同步完成（{data.registration}）：{detail}");

            Validate(data);
        }

        /// <summary>按当前配置跑一遍校验（与运行期同一套规则）。</summary>
        public static void Validate(SceneConfigAssetSO data)
        {
            if (data == null) return;

            var catalog = new SceneCatalog
            {
                EntrySceneName = data.entrySceneName,
                LoadingSceneName = data.loadingSceneName,
                TransitionPolicy = data.transitionPolicy,
                Strict = data.strict,
            };

            foreach (var item in data.scenes)
            {
                var definition = item != null ? item.ToDefinition() : null;
                if (definition != null) catalog.Register(definition);
            }

            var result = SceneConfigValidation.Validate(catalog, data);
            Debug.Log($"[Scenes] 校验完成：错误 {result.Errors.Count} 条，警告 {result.Warnings.Count} 条" +
                      (result.HasErrors ? "（存在错误，请查看上面的日志）" : string.Empty));
        }

        // ---------- Build Settings 路径 ----------

        private static void SyncToBuildSettings(SceneConfigAssetSO data, ref int added, ref int restored)
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0]);

            foreach (var (sceneName, _) in EnumerateSceneEntries(data))
            {
                var path = SceneNameUtility.FindSceneAssetPath(sceneName);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning($"[Scenes] 找不到场景资产，跳过 Build Settings 登记：{sceneName}");
                    continue;
                }

                int index = scenes.FindIndex(s => s != null && s.path == path);
                if (index < 0)
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                    added++;
                }
                else if (!scenes[index].enabled)
                {
                    // 之前被 Addressables 自动禁用过 → 重新启用，而不是新增重复条目
                    scenes[index].enabled = true;
                    restored++;
                }

                WarnIfAddressable(path, sceneName);
            }

            if (added > 0 || restored > 0) EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Addressables 与 Build Settings 互斥：已标记为 Addressable 的场景会被 Unity 自动禁用。</summary>
        private static void WarnIfAddressable(string scenePath, string sceneName)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) == null) return;

            Debug.LogWarning($"[Scenes] {sceneName} 已标记为 Addressable —— Unity 会自动在 Build Settings 中禁用它。" +
                             "若要使用 UnitySceneService，请先在 Addressables 中移除该场景条目（或把配置的登记方式改为 Addressables）。");
        }

        // ---------- Addressables 路径 ----------

        private static void SyncToAddressables(SceneConfigAssetSO data, ref int added)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[Scenes] 项目未启用 Addressables，已跳过 Addressables 登记");
                return;
            }

            foreach (var (sceneName, address) in EnumerateSceneEntries(data))
            {
                var path = SceneNameUtility.FindSceneAssetPath(sceneName);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning($"[Scenes] 找不到场景资产，跳过 Addressables 登记：{sceneName}");
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(path);
                bool existed = settings.FindAssetEntry(guid) != null;

                var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
                entry.address = string.IsNullOrWhiteSpace(address) ? sceneName : address.Trim();

                if (!existed) added++;
            }

            // 配置总表自身也要可加载（框架靠地址取它）
            var catalogGuid = AssetDatabase.AssetPathToGUID(CatalogAddress);
            if (!string.IsNullOrEmpty(catalogGuid))
            {
                var catalogEntry = settings.CreateOrMoveEntry(catalogGuid, settings.DefaultGroup);
                catalogEntry.address = CatalogAddress;
            }

            EditorUtility.SetDirty(settings);
        }

        // ---------- 辅助 ----------

        private static IEnumerable<(string sceneName, string address)> EnumerateSceneEntries(SceneConfigAssetSO data)
        {
            var seen = new HashSet<string>();

            foreach (var name in new[] { data.entrySceneName, data.loadingSceneName })
            {
                var trimmed = Trim(name);
                if (trimmed != null && seen.Add(trimmed)) yield return (trimmed, null);
            }

            foreach (var item in data.scenes)
            {
                if (item == null) continue;

                var trimmed = Trim(item.sceneName);
                if (trimmed == null || !seen.Add(trimmed)) continue;

                yield return (trimmed, item.address);
            }
        }

        private static string Trim(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static SceneConfigAssetSO FindCatalogAsset()
            => AssetDatabase.LoadAssetAtPath<SceneConfigAssetSO>(CatalogAddress);
    }
}
