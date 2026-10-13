using UnityEngine;

namespace Game.RPG.Cursors
{
    /// <summary>光标呈现方案（对应需求「兼容传统 Cursor API 与新输入系统的 VirtualMouseInput」）。</summary>
    public enum CursorPresenterKind
    {
        /// <summary>软件光标（UI Image）跟随**真实鼠标** —— 需求第 9 条的默认方案。</summary>
        SoftwareFollowMouse = 0,

        /// <summary>**传统方案**：`UnityEngine.Cursor` API（`Cursor.SetCursor` + `Cursor.visible`），即硬件光标。</summary>
        UnityCursor = 1,

        /// <summary>**新输入方案**：`VirtualMouseInput` 造的虚拟 `Mouse` 设备，由手柄摇杆驱动软件光标。</summary>
        VirtualMouse = 2,
    }

    /// <summary>
    /// 「光标怎么呈现」的抽象：把 <see cref="CursorIconResolver"/> 算出的 6 状态图标与「落到画面上」解耦，
    /// 于是同一套图标与状态机可以跑在互不相同的底层方案上
    /// —— 与框架 `IInputService`/`IAudioService` 的「契约 + 适配器」是同一种写法。
    /// <para>
    /// ⚠️ **方案间互斥点**：系统光标可见性。传统方案要求 `Cursor.visible = true`，两个软件方案要求 `false`；
    /// 因此切换 presenter 必须成对交接（旧方案先 <see cref="System.IDisposable.Dispose"/>，再启用新方案），
    /// 否则会残留一个可见的硬件光标。
    /// </para>
    /// <para>
    /// ⚠️ 另一种硬件路线（`VirtualMouseInput.cursorMode = HardwareCursorIfAvailable`，会 `DisableDevice` 平台鼠标并用
    /// `Mouse.WarpCursorPosition` 移动系统光标）**不得**与 <see cref="UnityCursorPresenter"/> 叠加 —— 两者会争抢系统光标。
    /// </para>
    /// </summary>
    public interface ICursorPresenter : System.IDisposable
    {
        CursorPresenterKind Kind { get; }

        /// <summary>
        /// 是否由本方案自己驱动光标位置。
        /// <para>软件方案（UI Image）为 <c>true</c>；传统 `UnityEngine.Cursor` 为 <c>false</c>（位置由操作系统管，我们不跟随）。</para>
        /// </summary>
        bool DrivesPosition { get; }

        /// <summary>
        /// 取当前指针的屏幕坐标（像素，原点左下）。
        /// <para>
        /// 之所以放进抽象：**两种方案的指针来源本来就不同** —— 传统/跟随鼠标方案是真实鼠标（`&lt;Mouse&gt;/position`），
        /// 虚拟鼠标方案是 `VirtualMouseInput` 造出的虚拟 `Mouse` 设备。玩法（射线拾取）需要位置时，向当前方案要即可，
        /// 不必假设 `<Mouse>/position` 在多 `Mouse` 设备并存时的解析结果。
        /// </para>
        /// </summary>
        bool TryGetPointerScreenPosition(out Vector2 screenPosition);

        /// <summary>
        /// 注入输入服务。
        /// <para>框架服务在启动协程里才注册 → 由外壳逐帧重试解析后注入（不做阻塞等待）。</para>
        /// </summary>
        void BindInput(Framework.Abstractions.Input.IInputService input);

        /// <summary>切换当前外观。<paramref name="sprite"/> 为 null 时保留上一张（避免闪烁）。</summary>
        void SetIcon(CursorIcon icon, Sprite sprite);

        /// <summary>显示/隐藏光标（含系统光标可见性）。</summary>
        void SetVisible(bool visible);
    }
}
