using UnityEngine;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// 软件光标的外观参数（配置资产，策划可改）。
    /// <para>
    /// 6 张图标由编辑器工具 <c>RpgCursorIconGenerator</c> 程序生成（需求第 15 条：资源全部占位），
    /// 不引入任何外部素材。生成后把结果拖到本资产的对应槽位即可。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Game/RPG/Cursor Settings", fileName = "RpgCursorSettings")]
    public sealed class RpgCursorSettingsSO : ScriptableObject
    {
        [Header("六个状态的图标")]
        [Tooltip("悬停在 UI 上 / 射线无命中")]
        public Sprite defaultIcon;

        [Tooltip("命中地面（点击 = 移动）")]
        public Sprite groundIcon;

        [Tooltip("命中可拾取物品")]
        public Sprite pickupIcon;

        [Tooltip("命中 NPC")]
        public Sprite npcIcon;

        [Tooltip("命中敌人")]
        public Sprite enemyIcon;

        [Tooltip("射线命中但 NavMesh 采样失败")]
        public Sprite unreachableIcon;

        [Header("呈现方案")]
        [Tooltip("软件光标跟真实鼠标（默认）/ 传统 Unity Cursor API（硬件光标）/ 新输入系统虚拟鼠标（手柄）")]
        public CursorPresenterKind presenter = CursorPresenterKind.SoftwareFollowMouse;

        [Tooltip("传统方案（Cursor.SetCursor）的热点：**原点在左上角**（Unity 约定），单位像素")]
        public Vector2 hardwareHotspot = new Vector2(8f, 2f);

        [Tooltip("新输入方案：手柄摇杆驱动虚拟鼠标的速度（像素/秒）")]
        public float virtualMouseSpeed = 1000f;

        [Tooltip("新输入方案：摇杆绑定路径（内联 InputAction，暂不进重绑定体系）")]
        public string cursorNavigateBinding = "<Gamepad>/leftStick";

        [Header("显示")]
        [Tooltip("图标显示尺寸（像素，正方形）")]
        public float size = 32f;

        [Tooltip("软件方案：图标**中心**相对指针的偏移（像素，原点左下）：(0,0) = 指针在图标正中")]
        public Vector2 hotspotOffset = Vector2.zero;

        /// <summary>按状态取图标；未配置的槽位返回 null（调用方保留上一张，避免闪烁）。</summary>
        public Sprite Get(CursorIcon icon)
        {
            switch (icon)
            {
                case CursorIcon.Ground:      return groundIcon;
                case CursorIcon.Pickup:      return pickupIcon;
                case CursorIcon.Npc:         return npcIcon;
                case CursorIcon.Enemy:       return enemyIcon;
                case CursorIcon.Unreachable: return unreachableIcon;
                default:                     return defaultIcon;
            }
        }
    }
}
