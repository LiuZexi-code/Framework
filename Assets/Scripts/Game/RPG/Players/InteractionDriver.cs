using Framework.Abstractions.Input;
using Framework.Bootstrap;
using Game.Inputs;
using Game.RPG.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.RPG.Players
{
    /// <summary>一次「屏幕点击」被解释成的指令。</summary>
    public enum InteractionCommand
    {
        /// <summary>什么都没发生（没打中 / 不可达）。</summary>
        None = 0,

        /// <summary>点地 → 移动。</summary>
        Move = 1,

        /// <summary>点到可交互物 → 已下达交互意图。</summary>
        Interact = 2,
    }

    /// <summary>
    /// 交互驱动：**只做「读输入 + 射线」**，把结果喂给场景级 <see cref="IInteractionService"/>
    /// （状态与规则全在服务里，因此服务可在 EditMode 用替身完整单测）。
    /// <list type="bullet">
    /// <item>悬停：按 <c>hoverInterval</c> 节流，用 <c>PointerPosition</c> 射线 → <c>SetHover</c>（A6 光标 / A7 HUD 按钮都读它）</item>
    /// <item>左键：命中可交互物 → 下达交互意图；命中地面 → 移动；悬停在 UI 上则不处理</item>
    /// <item>快捷键 <c>Interact</c>（F）→ 执行当前意图</item>
    /// </list>
    /// <para>
    /// A5 场景装配时由 <c>ClickToMoveDriver</c> 更名而来（<b>脚本 GUID 保持不变</b> → RPGScene 里 Player 上的
    /// 组件引用未断）。
    /// </para>
    /// <para>
    /// <see cref="TryIssueCommandAt"/> 是**正式 API**（不是测试后门）：把「屏幕点 → 世界指令」这条链路
    /// 暴露成可自动化驱动的入口。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionDriver : MonoBehaviour
    {
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private Camera rayCamera;
        [SerializeField] private float maxRayDistance = 500f;

        [Tooltip("悬停检测节流（秒）")]
        [SerializeField] private float hoverInterval = 0.05f;

        private IInteractionService _interaction;
        private IInputService _input;
        private float _hoverTimer;
        private bool _playerAttached;

        /// <summary>诊断用：是否已拿到输入服务。</summary>
        public bool HasInputService => _input != null;

        /// <summary>诊断用：是否已拿到交互服务。</summary>
        public bool HasInteractionService => _interaction != null;

        /// <summary>鼠标是否悬停在 UI 上（悬停 UI 时不应触发世界点击）。</summary>
        public static bool IsPointerOverUi()
            => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>
        /// 按屏幕坐标下达指令：命中可交互物 → 交互意图；命中地面 → 移动。
        /// <paramref name="point"/> 为世界命中点。
        /// </summary>
        public InteractionCommand TryIssueCommandAt(Vector2 screenPosition, out Vector3 point)
        {
            point = default;
            if (_interaction == null) return InteractionCommand.None;

            var cam = rayCamera != null ? rayCamera : Camera.main;
            if (cam == null) return InteractionCommand.None;

            RaycastHit hit;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPosition), out hit, maxRayDistance, RpgLayers.ClickMask))
                return InteractionCommand.None;

            point = hit.point;

            var interactable = hit.collider.GetComponentInParent<Interactable>();
            if (interactable != null)
                return _interaction.OrderInteraction(interactable) ? InteractionCommand.Interact : InteractionCommand.None;

            if (hit.collider.gameObject.layer != RpgLayers.Ground) return InteractionCommand.None;

            return _interaction.OrderMoveTo(hit.point) ? InteractionCommand.Move : InteractionCommand.None;
        }

        /// <summary>刷新悬停目标（由 Update 按节流调用；也可被自动化探针直接调用）。</summary>
        public void RefreshHover(Vector2 screenPosition)
        {
            if (_interaction == null) return;

            var cam = rayCamera != null ? rayCamera : Camera.main;
            if (cam == null) return;

            RaycastHit hit;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPosition), out hit, maxRayDistance, RpgLayers.ClickMask))
            {
                _interaction.SetHover(null);
                return;
            }

            var interactable = hit.collider.GetComponentInParent<Interactable>();
            if (interactable != null)
            {
                // 可达性用目标的**站位点**判定（与 OrderInteraction 交出的点是同一个），
                // 而 HoverInfo.Point 仍是鼠标实际命中的位置（供落点特效等使用）。
                var aim = interactable.ApproachPosition;
                _interaction.SetHover(new HoverInfo(interactable.Kind, interactable.DisplayName, interactable, hit.point, IsReachable(aim)));
                return;
            }

            _interaction.SetHover(new HoverInfo(InteractionKind.Ground, null, null, hit.point, IsReachable(hit.point)));
        }

        /// <summary>
        /// 该世界点能否走到（驱动不可达时 → 光标显示 <c>Unreachable</c>）。
        /// <para>玩家实现尚未就绪时**保守视为可达**，避免开局把一切目标都误报成不可达。</para>
        /// </summary>
        private bool IsReachable(Vector3 worldPoint)
            => motor == null || motor.IsReachable(worldPoint);

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        private void Update()
        {
            if (!TryResolveServices()) return;

            if (!_playerAttached && motor != null)
            {
                _interaction.AttachPlayer(motor);
                _playerAttached = true;
            }

            // 推进「移动中 → 到达」并清理已销毁的目标
            _interaction.Tick();

            _hoverTimer -= Time.deltaTime;
            if (_hoverTimer <= 0f)
            {
                _hoverTimer = Mathf.Max(0f, hoverInterval);

                if (IsPointerOverUi()) _interaction.SetHover(null);
                else RefreshHover(_input.GetVector2(GameActions.PointerPosition));
            }

            if (IsPointerOverUi()) return;

            if (_input.WasPressedThisFrame(GameActions.Click))
            {
                Vector3 point;
                TryIssueCommandAt(_input.GetVector2(GameActions.PointerPosition), out point);
            }

            if (_input.WasPressedThisFrame(GameActions.Interact))
                _interaction.TryExecute();
        }

        /// <summary>服务在启动流程里才注册 → 逐帧重试直到就绪（不做阻塞等待）。</summary>
        private bool TryResolveServices()
        {
            if (_input == null)
            {
                IInputService resolvedInput;
                if (ServiceLocator.TryGet(out resolvedInput) && resolvedInput != null) _input = resolvedInput;
            }

            if (_interaction == null)
            {
                IInteractionService resolvedInteraction;
                if (ServiceLocator.TryGet(out resolvedInteraction) && resolvedInteraction != null) _interaction = resolvedInteraction;
            }

            return _input != null && _interaction != null;
        }
    }
}
