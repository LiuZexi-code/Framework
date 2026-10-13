using UnityEngine;

namespace Game.RPG
{
    /// <summary>
    /// RPG 用到的 Layer 名与掩码（**集中一处**，避免各系统散落硬编码的字符串与位运算）。
    /// <para>
    /// Layer 在 A2 阶段创建：Ground=8 / Interactable=9 / Obstacle=10。
    /// </para>
    /// </summary>
    public static class RpgLayers
    {
        public const string GroundName = "Ground";
        public const string InteractableName = "Interactable";
        public const string ObstacleName = "Obstacle";

        private static int _ground = -1;
        private static int _interactable = -1;
        private static int _obstacle = -1;

        /// <summary>可行走地面。</summary>
        public static int Ground => _ground >= 0 ? _ground : (_ground = Resolve(GroundName));

        /// <summary>可交互物（物品 / NPC / 敌人）的碰撞体。</summary>
        public static int Interactable => _interactable >= 0 ? _interactable : (_interactable = Resolve(InteractableName));

        /// <summary>障碍静态几何（只用于挡相机/NavMesh 烘焙）。</summary>
        public static int Obstacle => _obstacle >= 0 ? _obstacle : (_obstacle = Resolve(ObstacleName));

        /// <summary>点击射线要命中的层：地面 + 可交互物。</summary>
        public static int ClickMask => (1 << Ground) | (1 << Interactable);

        private static int Resolve(string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) return layer;

            Debug.LogError($"[RpgLayers] 未找到 Layer「{layerName}」，请在其 ProjectSettings/TagManager.asset 中创建（RPG 场景依赖它做射线与导航）");
            return 0;
        }
    }
}
