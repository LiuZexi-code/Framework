using System.Collections.Generic;
using Game.RPG.Interaction;
using Game.RPG.Players;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// RPG 场景装配工具：把交互组件挂到场景物体上（幂等，可重复执行）。
    /// <para>
    /// 之所以做成菜单：某些会话里 AI 侧的 <c>execute_code</c> 工具可能不可用，
    /// 而「给场景物体挂组件」是必须碰编辑器的活。做成一个菜单项后，
    /// **AI 写代码、人点一次菜单**，配合起来不需要放宽工具权限。
    /// </para>
    /// <para>
    /// 幂等保证：已存在的组件不重复添加；只补齐缺失项。执行后会标记场景为脏并保存。
    /// </para>
    /// </summary>
    public static class RpgSceneWirer
    {
        private const string MenuRoot = "Tools/我的工具/RPG/";

        [MenuItem(MenuRoot + "装配交互组件到当前场景")]
        private static void WireInteraction()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[RpgSceneWirer] 当前没有有效场景，已中止");
                return;
            }

            var player = GameObject.Find("Player");
            if (player == null || player.GetComponent<PlayerMotor>() == null)
            {
                Debug.LogError($"[RpgSceneWirer] 当前场景（{scene.name}）里找不到挂着 PlayerMotor 的 Player —— " +
                               "请先打开 RPGScene，或确认角色叫 Player。已中止，避免误改其它场景。");
                return;
            }

            var report = new List<string>();

            // ① NPC：交互半径略大（对话不需要贴身）
            Wire<NpcInteractable>("Npc", "村民", 2.5f, report, "dialogueId", "Dialog_Placeholder", "Dialog_Npc_Intro");

            // ② 拾取物：半径小（要走到脚边）
            Wire<PickupItem>("Pickup_1", "治疗药水", 1.5f, report, "itemId", "Item_Placeholder", "Potion_HP");
            Wire<PickupItem>("Pickup_2", "史莱姆粘液", 1.5f, report, "itemId", "Item_Placeholder", "Material_Slime");

            // ③ 敌人：半径 = 攻击距离
            Wire<EnemyInteractable>("Enemy_Green", "绿史莱姆", 2.0f, report, "enemyId", "Enemy_Placeholder", "Enemy_Green");
            Wire<EnemyInteractable>("Enemy_Blue", "蓝史莱姆", 2.0f, report, "enemyId", "Enemy_Placeholder", "Enemy_Blue");
            Wire<EnemyInteractable>("Enemy_Red", "红史莱姆", 2.0f, report, "enemyId", "Enemy_Placeholder", "Enemy_Red");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[RpgSceneWirer] 交互组件装配完成（{report.Count} 项）：" + string.Join("；", report));
        }

        [MenuItem(MenuRoot + "检查交互组件是否齐全")]
        private static void CheckInteraction()
        {
            var names = new[] { "Npc", "Pickup_1", "Pickup_2", "Enemy_Green", "Enemy_Blue", "Enemy_Red" };
            var lines = new List<string>();

            for (int i = 0; i < names.Length; i++)
            {
                var go = GameObject.Find(names[i]);
                if (go == null) { lines.Add($"{names[i]}=缺失"); continue; }

                var interactable = go.GetComponent<Interactable>();
                lines.Add($"{names[i]}={(interactable != null ? interactable.Kind + "/" + interactable.DisplayName + "/" + interactable.InteractionRange : "未挂")}");
            }

            Debug.Log("[RpgSceneWirer] 检查结果：" + string.Join("；", lines));
        }

        // ---------- 内部 ----------

        /// <summary>
        /// 确保目标物体上挂有 <typeparamref name="T"/>，并设置显示名、交互半径与业务 id
        /// （只补缺失，不覆盖已填值；「未填」= 空串，或仍是 id 的占位符 <paramref name="idPlaceholder"/>）。
        /// </summary>
        private static void Wire<T>(string objectName, string displayName, float range, List<string> report,
            string idFieldName, string idPlaceholder, string idValue)
            where T : Interactable
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                report.Add($"{objectName}=跳过（场景里没有）");
                return;
            }

            var component = go.GetComponent<T>();
            var added = false;
            if (component == null)
            {
                component = go.AddComponent<T>();
                added = true;
            }

            var so = new SerializedObject(component);

            var nameProperty = so.FindProperty("displayName");
            if (nameProperty == null)
                report.Add($"{objectName}‼ 未找到序列化字段「displayName」");
            else if (string.IsNullOrEmpty(nameProperty.stringValue))
                nameProperty.stringValue = displayName;

            var rangeProperty = so.FindProperty("interactionRange");
            if (rangeProperty != null && (added || Mathf.Approximately(rangeProperty.floatValue, 1.8f)))
                rangeProperty.floatValue = range;

            var idProperty = so.FindProperty(idFieldName);
            if (idProperty == null)
                report.Add($"{objectName}‼ 未找到序列化字段「{idFieldName}」（{typeof(T).Name} 字段名改了？id 未设置）");
            else if (string.IsNullOrEmpty(idProperty.stringValue) || idProperty.stringValue == idPlaceholder)
                idProperty.stringValue = idValue;

            so.ApplyModifiedPropertiesWithoutUndo();

            report.Add($"{objectName}={(added ? "新增" : "已存在")}{typeof(T).Name}({range})");
        }
    }
}
