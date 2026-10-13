using Framework.Abstractions.Input;
using Framework.Bootstrap;
using Game.RPG.Interaction;
using UnityEngine;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// 光标**外壳**：只决定「当前该显示哪张图」（<see cref="CursorIconResolver"/> + 6 状态图标），
    /// 具体「怎么落到画面上」交给 <see cref="ICursorPresenter"/> 的某个实现。
    /// <list type="bullet">
    /// <item><see cref="CursorPresenterKind.SoftwareFollowMouse"/>：软件光标跟随真实鼠标（需求第 9 条的默认方案）</item>
    /// <item><see cref="CursorPresenterKind.UnityCursor"/>：传统 `UnityEngine.Cursor` API（硬件光标）</item>
    /// <item><see cref="CursorPresenterKind.VirtualMouse"/>：新输入系统 `VirtualMouseInput`（手柄驱动软件光标）</item>
    /// </list>
    /// <para>
    /// 职责边界：本类**不碰** Canvas、**不碰** `Cursor` API、**不碰** `VirtualMouseInput` —— 那些全在各自的 presenter 里。
    /// 也**不读**输入位置（除了解析服务）：位置由 presenter 自己管（三种方案的指针来源本来就不同）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CursorView : MonoBehaviour
    {
        [Tooltip("光标图标 + 呈现方案配置（由 RpgCursorIconGenerator 生成）")]
        [SerializeField] private RpgCursorSettingsSO settings;

        private ICursorPresenter _presenter;
        private SoftwareCursorPresenter _software;

        private IInputService _input;
        private IInteractionService _interaction;
        private bool _subscribed;
        private bool _warnedNoSettings;
        private bool _warnedNoInput;
        private CursorIcon _icon = CursorIcon.Default;
        private Sprite _sprite;

        // ---------- 诊断（供 Play 实测 / 排障读取） ----------

        /// <summary>当前生效的方案。</summary>
        public CursorPresenterKind PresenterKind
            => _presenter != null
                ? _presenter.Kind
                : settings != null ? settings.presenter : CursorPresenterKind.SoftwareFollowMouse;

        /// <summary>本方案是否自己驱动光标位置（软件方案 true；硬件方案 false）。</summary>
        public bool DrivesPosition => _presenter != null && _presenter.DrivesPosition;

        /// <summary>系统光标是否处于隐藏状态（软件方案 = true，传统方案 = false）。</summary>
        public bool IsSystemCursorHidden => DrivesPosition;

        public bool HasInputService => _input != null;

        public bool HasInteractionService => _interaction != null;

        /// <summary>当前图标状态。</summary>
        public CursorIcon Icon => _icon;

        /// <summary>当前图标 Sprite。</summary>
        public Sprite CurrentSprite => _sprite;

        /// <summary>虚拟鼠标设备是否就绪（仅 VirtualMouse 方案有意义）。</summary>
        public bool VirtualMouseReady => _software != null && _software.VirtualMouseReady;

        /// <summary>本帧摆放用的屏幕坐标（软件方案；像素，原点左下）。</summary>
        public Vector2 PointerScreenPosition => _software != null ? _software.LastCenter : default;

        /// <summary>向当前方案要指针位置（两种方案的来源不同，故走抽象）。</summary>
        public bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;
            return _presenter != null && _presenter.TryGetPointerScreenPosition(out screenPosition);
        }

        // ---------- 生命周期 ----------

        private void OnEnable()
        {
            BuildPresenter();
            _presenter?.SetVisible(true);
        }

        private void OnDisable()
        {
            UnsubscribeServices();

            // 先让方案自己收尾（软件方案要交回系统光标），再丢掉引用
            _presenter?.Dispose();
            _presenter = null;
            _software = null;
        }

        private void Update()
        {
            if (_presenter == null) return;

            TryResolveServices();

            if (_interaction != null && !_subscribed)
            {
                _interaction.Changed += RefreshIcon;
                _subscribed = true;
            }

            RefreshIcon();

            // 软件方案自己驱动位置；硬件方案由操作系统管（DrivesPosition = false）
            _software?.Tick();
        }

        // ---------- 内部 ----------

        /// <summary>
        /// 运行时切换呈现方案。
        /// <para>
        /// 未来的「按最近使用的输入设备自动切换」（手柄一动→虚拟鼠标、鼠标一动→传统/跟鼠标）就调它 ——
        /// 所以这是**正式 API**，不是测试后门。
        /// </para>
        /// <para>⚠️ 关键顺序：先把旧方案 <c>Dispose</c>（交回系统光标），再建立并启用新方案 —— 否则会残留一个可见的硬件光标。</para>
        /// </summary>
        public void SetPresenter(CursorPresenterKind kind)
        {
            _presenter?.Dispose();
            _presenter = null;
            _software = null;

            BuildPresenter(kind);

            _presenter?.BindInput(_input);
            _presenter?.SetVisible(true);
            RefreshIcon();
        }

        private void BuildPresenter(CursorPresenterKind? overrideKind = null)
        {
            if (settings == null && !_warnedNoSettings)
            {
                _warnedNoSettings = true;
                Debug.LogWarning($"[Cursor] 未指派 {nameof(RpgCursorSettingsSO)}，将退化为「软件光标跟真实鼠标」" +
                                 "（建议先运行菜单 Tools/我的工具/RPG/生成软件光标图标，再装配）");
            }

            var kind = overrideKind
                       ?? (settings != null ? settings.presenter : CursorPresenterKind.SoftwareFollowMouse);

            if (kind == CursorPresenterKind.UnityCursor)
            {
                _presenter = new UnityCursorPresenter(settings);
                return;
            }

            _software = new SoftwareCursorPresenter(settings, kind);
            _software.Initialize();
            _presenter = _software;
        }

        /// <summary>按当前悬停信息换图（悬停变化时由 <c>Changed</c> 触发；也逐帧兜底，代价可忽略）。</summary>
        private void RefreshIcon()
        {
            if (_presenter == null) return;

            var icon = CursorIconResolver.Resolve(_interaction != null ? _interaction.Hover : null);
            _icon = icon;

            if (settings != null)
            {
                // 未配置的槽位返回 null → 保留上一张，避免「突然消失」的闪烁
                var sprite = settings.Get(icon);
                if (sprite != null) _sprite = sprite;
            }

            _presenter.SetIcon(icon, _sprite);
        }

        /// <summary>服务在启动流程里才注册 → 逐帧重试直到就绪（不阻塞），拿到后注入给方案。</summary>
        private void TryResolveServices()
        {
            if (_input == null)
            {
                IInputService resolvedInput;
                if (ServiceLocator.TryGet(out resolvedInput) && resolvedInput != null)
                {
                    _input = resolvedInput;
                    _presenter?.BindInput(_input);
                }
                else if (!_warnedNoInput)
                {
                    _warnedNoInput = true;
                    Debug.Log("[Cursor] 等待 IInputService 注册（框架启动完成后自动恢复）");
                }
            }

            if (_interaction == null)
            {
                IInteractionService resolvedInteraction;
                if (ServiceLocator.TryGet(out resolvedInteraction) && resolvedInteraction != null)
                    _interaction = resolvedInteraction;
            }
        }

        private void UnsubscribeServices()
        {
            if (_subscribed && _interaction != null) _interaction.Changed -= RefreshIcon;
            _subscribed = false;
        }
    }
}
