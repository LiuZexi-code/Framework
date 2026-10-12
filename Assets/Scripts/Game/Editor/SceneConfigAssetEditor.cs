using Game.Scenes.Config;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>场景配置总表的检视面板：加两个按钮，避免每次都要去菜单里找。</summary>
    [CustomEditor(typeof(SceneConfigAssetSO))]
    public sealed class SceneConfigAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            var data = (SceneConfigAssetSO)target;

            if (GUILayout.Button("同步场景登记（Build Settings + Addressables）"))
                SceneConfigSynchronizer.Sync(data);

            if (GUILayout.Button("仅校验配置"))
                SceneConfigSynchronizer.Validate(data);

            EditorGUILayout.HelpBox(
                "同步工具是幂等的：重复执行不会产生重复条目。\n" +
                "场景名下拉来自 Build Settings 与 Addressables 的并集。",
                MessageType.Info);
        }
    }
}
