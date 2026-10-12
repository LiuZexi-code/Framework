using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 收集「项目里已知的场景名」（Build Settings + Addressables），
    /// 供场景名下拉与登记校验使用。
    /// </summary>
    public static class SceneNameUtility
    {
        /// <summary>已知场景名（去重；仅文件名，不含路径与扩展名）。</summary>
        public static List<string> GetKnownSceneNames()
        {
            var names = new List<string>();

            // Build Settings
            var buildScenes = EditorBuildSettings.scenes;
            if (buildScenes != null)
            {
                foreach (var scene in buildScenes)
                {
                    if (scene == null || string.IsNullOrEmpty(scene.path)) continue;
                    Add(names, Path.GetFileNameWithoutExtension(scene.path));
                }
            }

            // Addressables
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry == null) continue;
                        if (entry.MainAssetType != typeof(SceneAsset)) continue;
                        Add(names, Path.GetFileNameWithoutExtension(entry.AssetPath));
                    }
                }
            }

            names.Sort();
            return names;
        }

        /// <summary>场景资源的资产路径（找不到返回 null）。</summary>
        public static string FindSceneAssetPath(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;

            var guids = AssetDatabase.FindAssets("t:Scene");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName) return path;
            }
            return null;
        }

        private static void Add(List<string> names, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!names.Contains(name)) names.Add(name);
        }
    }
}
