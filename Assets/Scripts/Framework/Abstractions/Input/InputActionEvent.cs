using UnityEngine;

namespace Framework.Abstractions.Input
{
    /// <summary>
    /// 一次输入动作触发的事件负载。
    /// <para>
    /// 与 <c>UnityEngine.InputSystem</c> 无关（只用引擎内核的 <see cref="Vector2"/>），
    /// 因此核心抽象不引用具体输入包。
    /// </para>
    /// </summary>
    public readonly struct InputActionEvent
    {
        /// <summary>项目层定义的动作标识（框架不解释其含义）。</summary>
        public string ActionId { get; }

        /// <summary>玩家序号；单玩家时为 0。</summary>
        public int PlayerId { get; }

        /// <summary>触发阶段。</summary>
        public InputTriggerPhase Phase { get; }

        /// <summary>标量值（按钮 0/1，单轴为其值）；非标量动作为 0。</summary>
        public float Scalar { get; }

        /// <summary>二维向量值（如 Move）；非向量动作为 (0,0)。</summary>
        public Vector2 Vector { get; }

        public InputActionEvent(string actionId, int playerId, InputTriggerPhase phase,
                                float scalar = 0f, Vector2 vector = default)
        {
            ActionId = actionId;
            PlayerId = playerId;
            Phase = phase;
            Scalar = scalar;
            Vector = vector;
        }

        public override string ToString() =>
            $"{ActionId}[P{PlayerId}] {Phase} scalar={Scalar:0.###} vector={Vector}";
    }
}
