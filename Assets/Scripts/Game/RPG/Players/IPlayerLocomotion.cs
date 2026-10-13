using UnityEngine;

namespace Game.RPG.Players
{
    /// <summary>
    /// 「玩家位移」契约：让上层系统（交互 / 后续的战斗、AI）**不依赖 NavMesh 与 MonoBehaviour**，
    /// 从而可以在纯 C# 测试里用替身驱动。
    /// <para>
    /// 由 <see cref="PlayerMotor"/> 实现。
    /// </para>
    /// </summary>
    public interface IPlayerLocomotion
    {
        /// <summary>当前世界坐标。</summary>
        Vector3 Position { get; }

        /// <summary>是否正在前往某个目标点。</summary>
        bool IsMoving { get; }

        /// <summary>移动到世界坐标；返回 false 表示不可达。</summary>
        bool TryMoveTo(Vector3 worldPoint);

        /// <summary>
        /// 该世界点是否「走得到」（**只做 NavMesh 采样，不移动**）。
        /// <para>光标用它在悬停阶段就提示「不可达」，避免玩家点了才发现走不过去。</para>
        /// </summary>
        bool IsReachable(Vector3 worldPoint);

        /// <summary>停止移动（不改变位置）。</summary>
        void Stop();

        /// <summary>瞬移到指定位置（存档恢复 / 死亡回退）。</summary>
        bool TeleportTo(Vector3 worldPoint);
    }
}
