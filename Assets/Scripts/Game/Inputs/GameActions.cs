namespace Game.Inputs
{
    /// <summary>
    /// 项目层的动作 / 上下文标识（**框架不定义任何具体操作名**，这里是项目自己的语义）。
    /// <para>
    /// 新增一个动作 = 这里加一个常量 + 在 <c>GameInputActions.inputactions</c> 里加同名 Action，
    /// **不需要改框架代码**。
    /// </para>
    /// <para>
    /// 命名空间刻意用 <c>Game.Inputs</c> 而非 <c>Game.Input</c>：
    /// 后者会与 <c>UnityEngine.Input</c> 在类型解析上打架。
    /// </para>
    /// </summary>
    public static class GameActions
    {
        /// <summary>InputActionMap 名（= 上下文名，默认同名映射）。</summary>
        public static class Maps
        {
            public const string Gameplay = "Gameplay";
            public const string UI = "UI";
        }

        /// <summary>上下文标识（与 <see cref="Maps"/> 同名）。</summary>
        public static class Contexts
        {
            public const string Gameplay = Maps.Gameplay;
            public const string UI = Maps.UI;
        }

        /// <summary>上下文优先级：UI 高于 Gameplay，推入 UI 即阻断玩法输入。</summary>
        public static class Priorities
        {
            public const int Gameplay = 0;
            public const int UI = 100;
        }

        // ---------- Gameplay ----------
        public const string Move = "Move";
        public const string Jump = "Jump";
        public const string Attack = "Attack";
        public const string Pause = "Pause";

        // ---------- UI ----------
        public const string Cancel = "Cancel";
    }
}
