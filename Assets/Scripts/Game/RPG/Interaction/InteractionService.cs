using System;
using Game.RPG.Players;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 交互服务默认实现：**只做状态与规则**，不读输入、不做射线（那些属于 <c>InteractionDriver</c>）。
    /// <para>
    /// 状态机：<c>Idle</c>（无目标）→ <c>Moving</c>（已下达意图，走过去）→ <c>Ready</c>（进入范围，可执行）。
    /// 「是否到达」按**平面距离**判定（而非代理的到达事件）：这样目标交互半径与代理停止距离可以各自独立调整，
    /// 也避免依赖 NavMesh 的时序。
    /// </para>
    /// </summary>
    public sealed class InteractionService : IInteractionService
    {
        private readonly ILogger _logger;

        private IPlayerLocomotion _locomotion;
        private HoverInfo? _hover;
        private PendingIntent? _intent;
        private InteractionState _state = InteractionState.Idle;

        public InteractionService(ILogger logger = null)
        {
            _logger = logger;
        }

        public event Action Changed;

        public HoverInfo? Hover => _hover;

        public PendingIntent? Intent => _intent;

        public InteractionState State => _state;

        public bool CanExecute
            => _state == InteractionState.Ready && _intent.HasValue && IsAlive(_intent.Value.Target);

        public string ExecuteLabel => _intent.HasValue ? LabelOf(_intent.Value.Kind) : string.Empty;

        /// <summary>各交互种类的按钮文案（HUD 与提示共用一处）。</summary>
        public static string LabelOf(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.Pickup: return "拾取";
                case InteractionKind.Npc: return "对话";
                case InteractionKind.Enemy: return "攻击";
                default: return string.Empty;
            }
        }

        public void AttachPlayer(IPlayerLocomotion locomotion)
        {
            _locomotion = locomotion;
            var before = _state;
            RefreshState();
            if (before != _state) Raise();
        }

        public void SetHover(HoverInfo? hover)
        {
            if (Nullable.Equals(_hover, hover)) return;

            _hover = hover;
            Raise();
        }

        public bool OrderMoveTo(Vector3 worldPoint)
        {
            if (_locomotion == null) return false;

            // 点地 = 放弃当前交互意图
            var hadIntent = _intent.HasValue;
            _intent = null;

            var moved = _locomotion.TryMoveTo(worldPoint);

            var before = _state;
            RefreshState();
            if (hadIntent || moved || before != _state) Raise();

            return moved;
        }

        public bool OrderInteraction(IInteractable target)
        {
            if (_locomotion == null || !IsAlive(target)) return false;

            var approach = target.ApproachPosition;

            if (IsInRange(target, approach))
            {
                // 已经在范围内 → 不必移动（也避免「走过来又走回去」的抖动）
                _locomotion.Stop();
            }
            else if (!_locomotion.TryMoveTo(approach))
            {
                if (_logger != null) _logger.Warn($"[Interaction] 无法接近目标：{target.DisplayName}（{approach}）");
                return false;
            }

            _intent = new PendingIntent(target, approach);

            var before = _state;
            RefreshState();
            if (before != _state) Raise();

            return true;
        }

        public bool TryExecute()
        {
            if (!CanExecute) return false;

            var target = _intent.Value.Target;

            _intent = null;
            RefreshState();
            Raise();

            // 先清意图再执行：执行过程里若触发「打开面板/移动」不会与旧意图打架
            target.Interact();
            return true;
        }

        public void ClearIntent()
        {
            if (!_intent.HasValue) return;

            _intent = null;
            RefreshState();
            Raise();
        }

        public void Tick()
        {
            if (_locomotion == null) return;

            var before = _state;
            RefreshState();
            if (before != _state) Raise();
        }

        // ---------- 内部 ----------

        private void RefreshState()
        {
            if (!_intent.HasValue || !IsAlive(_intent.Value.Target) || _locomotion == null)
            {
                _intent = _intent.HasValue && !IsAlive(_intent.Value.Target) ? null : _intent;
                _state = InteractionState.Idle;
                return;
            }

            _state = IsInRange(_intent.Value.Target, _intent.Value.ApproachPoint)
                ? InteractionState.Ready
                : InteractionState.Moving;
        }

        private bool IsInRange(IInteractable target, Vector3 approachPoint)
        {
            if (_locomotion == null) return false;

            var a = _locomotion.Position;
            var b = approachPoint;
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b) <= target.InteractionRange;
        }

        /// <summary>
        /// 目标是否仍然有效。
        /// <para>
        /// 注意 Unity 的「伪 null」：被销毁的 MonoBehaviour 引用不为 null 但 <c>== null</c> 为真。
        /// 而纯 C# 替身不是 <see cref="UnityEngine.Object"/>，应视为存活。
        /// </para>
        /// </summary>
        private static bool IsAlive(IInteractable target)
        {
            if (target == null) return false;

            var unityObject = target as UnityEngine.Object;
            if (unityObject != null) return true;

            // 不是 UnityEngine.Object（测试替身）→ 存活
            return !(target is UnityEngine.Object);
        }

        private void Raise() => Changed?.Invoke();
    }
}
