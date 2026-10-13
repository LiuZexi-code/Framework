using Game.RPG.Interaction;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// 软件光标的 6 个状态（对应 6 张图标）。
    /// <para>与需求规格第 9 条一致：光标是**软件光标**（UI Image 跟随鼠标 + 隐藏系统光标），不是硬件光标。</para>
    /// </summary>
    public enum CursorIcon
    {
        /// <summary>悬停在 UI 上 / 射线无命中。</summary>
        Default = 0,

        /// <summary>命中地面（点击 = 移动）。</summary>
        Ground = 1,

        /// <summary>命中可拾取物品。</summary>
        Pickup = 2,

        /// <summary>命中 NPC。</summary>
        Npc = 3,

        /// <summary>命中敌人。</summary>
        Enemy = 4,

        /// <summary>射线命中但 NavMesh 采样失败（走不过去）。</summary>
        Unreachable = 5,
    }

    /// <summary>
    /// 「悬停信息 → 光标图标」的映射规则。
    /// <para>
    /// 刻意做成**纯函数**（不碰 Unity 对象、不读输入）：这样 6 个状态的判定可以在 EditMode 直接断言，
    /// 而 <see cref="CursorView"/> 只负责「跟随鼠标 + 换图」这类无法单测的表现工作。
    /// </para>
    /// <para>判定优先级：**不可达优先于具体类型** —— 走不过去的目标不应显示「攻击/拾取」图标误导玩家。</para>
    /// </summary>
    public static class CursorIconResolver
    {
        public static CursorIcon Resolve(HoverInfo? hover)
        {
            if (!hover.HasValue) return CursorIcon.Default;

            var h = hover.Value;

            // 命中 UI（驱动用 Kind=None 表示）或无目标
            if (h.Kind == InteractionKind.None) return CursorIcon.Default;

            // 走不过去：无论原本是什么类型，都先提示「不可达」
            if (!h.Reachable) return CursorIcon.Unreachable;

            switch (h.Kind)
            {
                case InteractionKind.Ground: return CursorIcon.Ground;
                case InteractionKind.Pickup: return CursorIcon.Pickup;
                case InteractionKind.Npc:    return CursorIcon.Npc;
                case InteractionKind.Enemy:  return CursorIcon.Enemy;
                default:                     return CursorIcon.Default;
            }
        }
    }
}
