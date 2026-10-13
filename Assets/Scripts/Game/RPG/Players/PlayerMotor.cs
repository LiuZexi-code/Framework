using System;
using UnityEngine;
using UnityEngine.AI;

namespace Game.RPG.Players
{
    /// <summary>
    /// 玩家移动：点击地面 → NavMesh 寻路走过去。
    /// <para>
    /// 设计决定：<c>agent.updateRotation = true</c> → **角色朝向随移动方向**（相机只环绕、不改角色朝向，见 RpgCameraRig）。
    /// </para>
    /// <para>
    /// 职责边界：本类只管「移动到某个世界点 / 是否到达 / 停止 / 瞬移」，
    /// **不认识输入、也不认识交互目标**（点击与目标识别在 InteractionDriver / InteractionService）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PlayerMotor : MonoBehaviour, IPlayerLocomotion
    {
        [Header("移动参数")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float angularSpeed = 720f;
        [SerializeField] private float acceleration = 20f;
        [SerializeField] private float stoppingDistance = 0.2f;

        [Tooltip("导航代理半径（略小于胶囊半径，避免蹭墙卡住）")]
        [SerializeField] private float agentRadius = 0.4f;

        [Tooltip("导航代理高度")]
        [SerializeField] private float agentHeight = 1.8f;

        [Tooltip("落点吸附到 NavMesh 的最大距离（超出则视为不可达）")]
        [SerializeField] private float sampleMaxDistance = 1.5f;

        [Tooltip("角色胶囊中心相对 NavMesh 的高度（胶囊高 2 → 取 1 让脚底贴地）")]
        [SerializeField] private float baseOffset = 1f;

        [Header("落点标记")]
        [SerializeField] private GameObject clickMarkerPrefab;

        private ClickMarkerView _marker;

        /// <summary>是否正在前往某个目标点。</summary>
        public bool HasDestination { get; private set; }

        /// <summary>当前目标点（已吸附到 NavMesh）。</summary>
        public Vector3 Destination { get; private set; }

        /// <summary>当前世界坐标（<see cref="IPlayerLocomotion"/>）。</summary>
        public Vector3 Position => transform.position;

        /// <summary>是否正在前往目标点（<see cref="IPlayerLocomotion"/>）。</summary>
        public bool IsMoving => HasDestination;

        /// <summary>脚下导航代理（供交互系统判定距离用）。</summary>
        public NavMeshAgent Agent => agent;

        /// <summary>到达目标点时触发（参数为落点）。</summary>
        public event Action<Vector3> DestinationReached;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            ApplyAgentSettings();
        }

        private void Start()
        {
            EnsureOnNavMesh();
        }

        private void Update()
        {
            if (!HasDestination) return;
            if (!HasArrived()) return;

            HasDestination = false;
            DestinationReached?.Invoke(Destination);
        }

        /// <summary>
        /// 移动到世界坐标（自动吸附到最近的 NavMesh 点）。
        /// 返回 false 表示目标点附近没有可站立的地面（不可达）。
        /// </summary>
        public bool TryMoveTo(Vector3 worldPoint)
        {
            if (agent == null || !agent.enabled) return false;

            EnsureOnNavMesh();
            if (!agent.isOnNavMesh) return false;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(worldPoint, out hit, sampleMaxDistance, NavMesh.AllAreas))
                return false;

            Destination = hit.position;
            HasDestination = true;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
            ShowMarker(hit.position);
            return true;
        }

        /// <summary>停止移动并清空目标（不改变位置）。</summary>
        public void Stop()
        {
            HasDestination = false;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>瞬移到指定位置（存档恢复 / 死亡回退用）。</summary>
        public bool TeleportTo(Vector3 worldPoint)
        {
            Stop();
            if (agent == null || !agent.enabled) return false;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(worldPoint, out hit, 5f, NavMesh.AllAreas)) return false;

            return agent.Warp(hit.position);
        }

        /// <summary>是否已到达当前目标点。</summary>
        public bool HasArrived()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
            if (agent.pathPending) return false;
            if (!agent.hasPath) return true;

            var remaining = agent.remainingDistance;
            // 路径刚设置时 remainingDistance 可能为 Infinity → 视为「还没到」
            if (float.IsInfinity(remaining) || float.IsNaN(remaining)) return false;

            return remaining <= agent.stoppingDistance + 0.05f;
        }

        private void ApplyAgentSettings()
        {
            if (agent == null) return;

            agent.speed = moveSpeed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = stoppingDistance;
            agent.radius = agentRadius;
            agent.height = agentHeight;
            agent.baseOffset = baseOffset;
            agent.autoBraking = true;
            agent.updateRotation = true;   // ★ 朝向随移动方向
        }

        /// <summary>把代理贴到脚下的 NavMesh（场景摆放位置可能略高于地面）。</summary>
        private void EnsureOnNavMesh()
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh) return;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                Debug.LogWarning($"[PlayerMotor] 角色不在 NavMesh 上，且 5m 内找不到可站立点：{transform.position}");
        }

        private void ShowMarker(Vector3 position)
        {
            if (clickMarkerPrefab == null) return;

            if (_marker == null)
            {
                var go = Instantiate(clickMarkerPrefab, position, Quaternion.identity);
                _marker = go.GetComponent<ClickMarkerView>();
                if (_marker == null) _marker = go.AddComponent<ClickMarkerView>();
            }

            _marker.Play(position);
        }
    }
}
