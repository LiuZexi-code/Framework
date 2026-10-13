using Framework.Abstractions.Input;
using Game.Inputs;
using UnityEngine;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// **传统方案**：`UnityEngine.Cursor` API（硬件光标）。
    /// <list type="bullet">
    /// <item>`Cursor.visible = true` —— 把系统光标交还操作系统；</item>
    /// <item>换图标用 `Cursor.SetCursor(texture, hotspot, cursorMode)`（唯一重载：`(Texture2D, Vector2, CursorMode)`）；</item>
    /// <item>**位置不跟随**（<see cref="DrivesPosition"/> = <c>false</c>）—— 由操作系统管，我们没有可摆放的对象。</item>
    /// </list>
    /// <para>
    /// ⚠️ 两个硬要求，缺一个就**静默失效**：
    /// ① 贴图必须**可读**（`TextureImporter.isReadable`）且格式合规（RGBA32 / 无压缩）；
    /// ② 热点原点在**左上角**（Unity 约定），与软件方案的中心对齐语义不同 → 用 <c>settings.hardwareHotspot</c> 单独配置。
    /// 我们的图标由 `RpgCursorIconGenerator` 生成，导入设置已按此要求处理。
    /// </para>
    /// </summary>
    public sealed class UnityCursorPresenter : ICursorPresenter
    {
        private readonly RpgCursorSettingsSO _settings;
        private IInputService _input;
        private CursorIcon _icon = CursorIcon.Default;
        private bool _visible = true;
        private bool _warnedUnreadable;

        public UnityCursorPresenter(RpgCursorSettingsSO settings) => _settings = settings;

        public CursorPresenterKind Kind => CursorPresenterKind.UnityCursor;

        public bool DrivesPosition => false;

        /// <summary>当前图标状态（诊断用）。</summary>
        public CursorIcon Icon => _icon;

        /// <summary>硬件光标的位置就是真实鼠标位置（由输入服务提供）。</summary>
        public void BindInput(IInputService input) => _input = input;

        public bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;
            if (_input == null) return false;

            screenPosition = _input.GetVector2(GameActions.PointerPosition);
            return true;
        }

        public void SetIcon(CursorIcon icon, Sprite sprite)
        {
            _icon = icon;
            if (sprite == null) return;

            var texture = sprite.texture;
            if (texture == null) return;

            if (!texture.isReadable)
            {
                if (!_warnedUnreadable)
                {
                    _warnedUnreadable = true;
                    Debug.LogWarning($"[Cursor] 贴图「{texture.name}」不可读 → Cursor.SetCursor 不生效。" +
                                     "请执行菜单 Tools/我的工具/RPG/生成软件光标图标 重新导入（会打开 Read/Write）");
                }
                return;
            }

            var hotspot = _settings != null ? _settings.hardwareHotspot : new Vector2(8f, 2f);
            Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
            Cursor.visible = _visible;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            // 硬件方案下「隐藏」就是直接隐藏系统光标（没有软件替身）
            Cursor.visible = visible;
            if (!visible) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>恢复系统光标：换回其它方案 / 退出时必须调用，否则会留下残留的硬件光标。</summary>
        public void Dispose()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
        }
    }
}
