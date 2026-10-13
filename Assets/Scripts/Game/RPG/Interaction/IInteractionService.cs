using Game.RPG.Players;
using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 交互服务契约（**场景级**：交互目标都是场景内物体，生命周期随场景）。
    /// <para>
    /// 职责：**状态与规则**——维护「悬停目标」「已下达意图」与状态机
    /// （<c>Idle → Moving → Ready</c>），并决定「能否执行 / 按钮文案」。
    /// </para>
    /// <para>
    /// 不读输入、不做射线：那些由场景里的 <c>InteractionDriver</c>（MonoBehaviour）负责，
    /// 它每帧把 <see cref="SetHover"/> 喂进来并调用 <see cref="Tick"/>。
    /// 这样切分后，本服务的全部规则都能在 EditMode 里用替身断言。
    /// </para>
    /// </summary>
    public interface IInteractionService
    {
        /// <summary>鼠标悬停目标（null = 没悬停到任何东西）。</summary>
        HoverInfo? Hover { get; }

        /// <summary>已下达的交互意图。</summary>
        PendingIntent? Intent { get; }

        InteractionState State { get; }

        /// <summary>当前是否可以执行交互（在范围内且有目标）。</summary>
        bool CanExecute { get; }

        /// <summary>交互按钮文案：「拾取」「对话」「攻击」；无意图时为空串。</summary>
        string ExecuteLabel { get; }

        /// <summary>绑定玩家位移实现（由驱动在 Start 时调用）。</summary>
        void AttachPlayer(IPlayerLocomotion locomotion);

        /// <summary>更新悬停目标（由驱动每帧喂入）。</summary>
        void SetHover(HoverInfo? hover);

        /// <summary>点地：移动，并取消已有交互意图。</summary>
        bool OrderMoveTo(Vector3 worldPoint);

        /// <summary>点目标：走过去（已在范围内则原地）并记录意图。</summary>
        bool OrderInteraction(IInteractable target);

        /// <summary>执行当前意图（HUD 按钮 / 快捷键）。</summary>
        bool TryExecute();

        /// <summary>取消当前意图。</summary>
        void ClearIntent();

        /// <summary>由驱动每帧调用：推进「移动中 → 到达」状态、清理失效目标。</summary>
        void Tick();

        /// <summary>状态或悬停变化时触发（HUD 交互按钮订阅）。</summary>
        event System.Action Changed;
    }
}
