using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 可交互目标契约。
    /// <para>
    /// 抽成接口是为了让 <see cref="InteractionService"/> **不依赖 MonoBehaviour**，
    /// 从而能在纯 C# 测试里用替身完整驱动状态机（与框架「契约 + 替身」的既有风格一致）。
    /// </para>
    /// </summary>
    public interface IInteractable
    {
        InteractionKind Kind { get; }

        /// <summary>显示名（HUD / 提示用）。</summary>
        string DisplayName { get; }

        /// <summary>交互判定半径（米）：角色进入该距离即可执行。</summary>
        float InteractionRange { get; }

        /// <summary>角色要站过去的位置（一般是目标身前的可达点）。</summary>
        Vector3 ApproachPosition { get; }

        /// <summary>真正执行交互（由 <see cref="InteractionService.TryExecute"/> 调用）。</summary>
        void Interact();
    }
}
