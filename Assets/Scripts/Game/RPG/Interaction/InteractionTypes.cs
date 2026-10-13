using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>可交互物的种类（决定光标图标、交互按钮文案与执行方式）。</summary>
    public enum InteractionKind
    {
        /// <summary>无目标。</summary>
        None = 0,

        /// <summary>可行走地面（点击 = 移动）。</summary>
        Ground = 1,

        /// <summary>可拾取物品。</summary>
        Pickup = 2,

        /// <summary>NPC（对话 / 任务）。</summary>
        Npc = 3,

        /// <summary>敌人（走近后攻击）。</summary>
        Enemy = 4,
    }

    /// <summary>交互状态机。</summary>
    public enum InteractionState
    {
        /// <summary>没有目标。</summary>
        Idle = 0,

        /// <summary>已下达目标，正在走过去。</summary>
        Moving = 1,

        /// <summary>已进入交互范围，可以执行。</summary>
        Ready = 2,
    }

    /// <summary>鼠标当前悬停到的目标（由驱动每帧喂给 <c>IInteractionService</c>）。</summary>
    public readonly struct HoverInfo
    {
        public readonly InteractionKind Kind;

        /// <summary>命中点的名字（地面时为空）。</summary>
        public readonly string Name;

        /// <summary>命中的可交互物；命中地面时为 null。</summary>
        public readonly IInteractable Target;

        /// <summary>世界命中点。</summary>
        public readonly Vector3 Point;

        /// <summary>落点是否可达（已通过 NavMesh 采样校验）。</summary>
        public readonly bool Reachable;

        public HoverInfo(InteractionKind kind, string name, IInteractable target, Vector3 point, bool reachable)
        {
            Kind = kind;
            Name = name;
            Target = target;
            Point = point;
            Reachable = reachable;
        }

        public bool HasTarget => Target != null;

        public override string ToString() => $"{Kind}:{Name ?? "-"}{(Reachable ? "" : "(不可达)")}";
    }

    /// <summary>已下达、正在执行的交互意图。</summary>
    public readonly struct PendingIntent
    {
        public readonly IInteractable Target;

        public readonly InteractionKind Kind;

        /// <summary>角色要站过去的位置。</summary>
        public readonly Vector3 ApproachPoint;

        public PendingIntent(IInteractable target, Vector3 approachPoint)
        {
            Target = target;
            Kind = target != null ? target.Kind : InteractionKind.None;
            ApproachPoint = approachPoint;
        }
    }
}
