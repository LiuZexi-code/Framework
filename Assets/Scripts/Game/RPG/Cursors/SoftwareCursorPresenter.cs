using Framework.Abstractions.Input;
using Game.Inputs;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// 软件光标（UI Image + 隐藏系统光标），位置由**本方案**驱动。两种位置来源：
    /// <list type="bullet">
    /// <item><see cref="CursorPresenterKind.SoftwareFollowMouse"/>：跟随**真实鼠标**（`&lt;Mouse&gt;/position`，经框架输入服务）——
    /// 需求第 9 条的默认方案。</item>
    /// <item><see cref="CursorPresenterKind.VirtualMouse"/>：**新输入方案** —— 自建 `VirtualMouseInput` 造虚拟 `Mouse` 设备，
    /// 由手柄摇杆驱动；位置取虚拟 `Mouse` 的 `position`。</item>
    /// </list>
    /// <para>
    /// ⚠️ `VirtualMouseInput` 的接线刻意只给 <c>cursorGraphic</c>、**不给** <c>cursorTransform</c>：
    /// <list type="bullet">
    /// <item>给 `cursorGraphic` → 组件会 `GetComponentInParent` 找到我们的 Canvas，从而用 `Canvas.pixelRect` 夹取坐标；</item>
    /// <item>不给 `cursorTransform` → 组件**不去写** RectTransform（源码两处都有 `!= null` 守卫，这是受支持的用法），
    /// 于是位置统一由本类摆放，两个软件方案共用同一个「中心对齐 + hotspotOffset」语义。</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ `cursorMode` 固定 <c>SoftwareCursor</c>。`HardwareCursorIfAvailable` 会 `DisableDevice` 平台鼠标并用
    /// `WarpCursorPosition` 移动系统光标 —— 那是「另一种硬件方案」，与 <see cref="UnityCursorPresenter"/> 互斥。
    /// </para>
    /// </summary>
    public sealed class SoftwareCursorPresenter : ICursorPresenter
    {
        private const string VirtualMouseHostName = "[CursorInput]";
        private const string DefaultStickBinding = "<Gamepad>/leftStick";

        private readonly RpgCursorSettingsSO _settings;
        private readonly CursorImageLayer _layer = new CursorImageLayer();

        private IInputService _input;
        private GameObject _virtualMouseHost;
        private VirtualMouseInput _virtualMouse;
        private CursorIcon _icon = CursorIcon.Default;
        private bool _visible = true;
        private bool _warnedNoInput;

        public SoftwareCursorPresenter(RpgCursorSettingsSO settings, CursorPresenterKind kind)
        {
            _settings = settings;
            Kind = kind == CursorPresenterKind.VirtualMouse
                ? CursorPresenterKind.VirtualMouse
                : CursorPresenterKind.SoftwareFollowMouse;
        }

        public CursorPresenterKind Kind { get; }

        public bool DrivesPosition => true;

        /// <summary>当前图标状态（诊断用）。</summary>
        public CursorIcon Icon => _icon;

        /// <summary>本帧摆放用的屏幕坐标（像素，原点左下）—— 诊断用，也是「两种方案位置来源不同」的直接证据。</summary>
        public Vector2 LastCenter { get; private set; }

        /// <summary>虚拟鼠标设备是否已就绪（仅 VirtualMouse 方案有意义）。</summary>
        public bool VirtualMouseReady => _virtualMouse != null && _virtualMouse.virtualMouse != null;

        /// <summary>UI 层根物体（诊断用）。</summary>
        public GameObject Root => _layer.Root;

        public void BindInput(IInputService input) => _input = input;

        /// <summary>建立 UI 层（并视方案建立虚拟鼠标设备）。</summary>
        public void Initialize()
        {
            _layer.Ensure(_settings != null ? _settings.size : 0f);

            if (Kind != CursorPresenterKind.VirtualMouse) return;

            // ⚠️ 必须先建成「未激活」→ 配置 → 最后激活。原因（读了包源码）：
            // VirtualMouseInput 在 OnEnable 里统一 Enable 各动作；而 stickAction 的 setter 只在
            // 「**旧**动作处于启用状态」时才 Enable 新动作。若先 AddComponent（OnEnable 立即执行、此时 stickAction
            // 还是 null）再赋动作，动作会永远不被启用 → 摇杆推不动光标（实测过：enabled=False）。
            _virtualMouseHost = new GameObject(VirtualMouseHostName);
            _virtualMouseHost.SetActive(false);
            _virtualMouseHost.transform.SetParent(null, false);
            Object.DontDestroyOnLoad(_virtualMouseHost);

            _virtualMouse = _virtualMouseHost.AddComponent<VirtualMouseInput>();
            _virtualMouse.cursorMode = VirtualMouseInput.CursorMode.SoftwareCursor;
            // 只借它获得「按 Canvas.pixelRect 夹取」；位置由本类统一摆放（故不接 cursorTransform）
            _virtualMouse.cursorGraphic = _layer.Graphic;
            _virtualMouse.cursorSpeed = _settings != null ? _settings.virtualMouseSpeed : 1000f;
            _virtualMouse.stickAction = new InputActionProperty(CreateStickAction());

            _virtualMouseHost.SetActive(true);   // 此刻 OnEnable 会把 stickAction 启用起来
        }

        /// <summary>由 <see cref="CursorView"/> 每帧调用：把图标摆到当前指针位置。</summary>
        public void Tick()
        {
            if (!_visible || _layer.Rect == null) return;
            if (!TryGetPointerScreenPosition(out var center)) return;

            LastCenter = center;
            var size = _settings != null ? _settings.size : 0f;
            var offset = _settings != null ? _settings.hotspotOffset : Vector2.zero;
            _layer.SetScreenPosition(center, offset, size);
        }

        public bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;

            if (Kind == CursorPresenterKind.VirtualMouse)
            {
                if (!VirtualMouseReady) return false;

                // 虚拟鼠标的 position 与 Canvas.pixelRect 是同一套坐标（像素、原点左下）
                screenPosition = _virtualMouse.virtualMouse.position.value;
                return true;
            }

            if (_input == null)
            {
                if (!_warnedNoInput)
                {
                    _warnedNoInput = true;
                    Debug.LogWarning("[Cursor] 尚未拿到 IInputService，软件光标暂时无法跟随（会在服务注册后自动恢复）");
                }
                return false;
            }

            screenPosition = _input.GetVector2(GameActions.PointerPosition);
            return true;
        }

        public void SetIcon(CursorIcon icon, Sprite sprite)
        {
            _icon = icon;
            if (_layer.Graphic == null) return;

            // 未配置的槽位保留上一张图标，避免「突然消失」的闪烁
            if (sprite != null) _layer.Graphic.sprite = sprite;
            _layer.Graphic.enabled = _visible && _layer.Graphic.sprite != null;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;

            // 软件方案的前提：藏掉系统光标（恢复时交回）
            Cursor.visible = !visible;

            if (_layer.Graphic != null)
                _layer.Graphic.enabled = visible && _layer.Graphic.sprite != null;
        }

        public void Dispose()
        {
            Cursor.visible = true;
            _layer.Destroy();

            if (_virtualMouseHost != null)
            {
                Object.Destroy(_virtualMouseHost);
                _virtualMouseHost = null;
                _virtualMouse = null;
            }
        }

        /// <summary>
        /// 虚拟鼠标的摇杆动作。刻意用**配置里的绑定路径**内联建一个 `InputAction`（而不是 `InputActionReference` 资产）：
        /// 少一个资产、少一处装配，且仍然可配。代价是**暂时不进输入重绑定体系**（重绑定阶段再换成引用工程资产里的动作）。
        /// <para>Enable/Disable 由 `VirtualMouseInput` 自己在 OnEnable/OnDisable 里成对调用，这里不用管。</para>
        /// </summary>
        private InputAction CreateStickAction()
        {
            var binding = _settings != null && !string.IsNullOrEmpty(_settings.cursorNavigateBinding)
                ? _settings.cursorNavigateBinding
                : DefaultStickBinding;

            return new InputAction("CursorNavigate", InputActionType.Value, binding);
        }
    }
}
