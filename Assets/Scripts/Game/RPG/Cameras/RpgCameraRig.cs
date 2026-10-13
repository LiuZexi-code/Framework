using Cinemachine;
using Framework.Abstractions.Input;
using Framework.Bootstrap;
using Game.Inputs;
using UnityEngine;

namespace Game.RPG.Cameras
{
    /// <summary>
    /// 俯视角环绕相机（RPG 主相机）。
    /// <para>
    /// 职责：**A/D → 水平环绕（Yaw）**，**滚轮 → 距离（Zoom）**，跟随与注视交给 Cinemachine。
    /// 不触碰角色朝向 —— 角色朝向由移动方向决定。
    /// </para>
    /// <para>
    /// 实现要点：Body 用 <see cref="CinemachineTransposer"/> + <see cref="CinemachineTransposer.BindingMode.WorldSpace"/>，
    /// 由本类**自己计算** <c>FollowOffset</c>（俯角/距离/朝向 → 世界偏移向量）。
    /// 之所以不用 <c>CinemachineOrbitalTransposer</c>：它的 heading 会与跟随目标的朝向/绑定模式互相影响，
    /// 而本项目的角色**会随移动方向旋转**，用自己算偏移可以让「相机朝向」与「角色朝向」彻底解耦、行为完全确定。
    /// </para>
    /// <para>
    /// 提供 <see cref="SetYaw"/>/<see cref="SetDistance"/>/<see cref="SetTarget"/> 作为正式 API：
    /// 存档恢复相机状态、以及测试驱动都走它（不是测试专用后门）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public sealed class RpgCameraRig : MonoBehaviour
    {
        [SerializeField] private RpgCameraSettingsSO settings;
        [SerializeField] private Transform target;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private CinemachineTransposer body;
        [SerializeField] private CinemachineComposer aim;

        [Tooltip("初始水平角（度）")]
        [SerializeField] private float initialYaw;

        private IInputService _input;
        private RpgCameraSettingsSO _runtimeSettings;

        /// <summary>当前水平角（度，0~360）。</summary>
        public float Yaw { get; private set; }

        /// <summary>当前相机到角色的距离（米）。</summary>
        public float Distance { get; private set; }

        /// <summary>诊断用：上一帧读到的环绕输入（-1..1）。</summary>
        public float LastRotateInput { get; private set; }

        /// <summary>诊断用：上一帧读到的缩放输入。</summary>
        public float LastZoomInput { get; private set; }

        /// <summary>诊断用：是否已拿到输入服务（未就绪时 Update 会逐帧重试）。</summary>
        public bool HasInputService => _input != null;

        /// <summary>参数来源（未指派资产时回退到内置默认值，避免 NRE）。</summary>
        private RpgCameraSettingsSO Settings
        {
            get
            {
                if (settings != null) return settings;
                if (_runtimeSettings == null) _runtimeSettings = ScriptableObject.CreateInstance<RpgCameraSettingsSO>();
                return _runtimeSettings;
            }
        }

        private void Awake()
        {
            if (virtualCamera == null) virtualCamera = GetComponent<CinemachineVirtualCamera>();
            if (virtualCamera != null)
            {
                if (body == null) body = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
                if (aim == null) aim = virtualCamera.GetCinemachineComponent<CinemachineComposer>();
            }

            if (settings == null)
                Debug.LogWarning("[RpgCameraRig] 未指派 RpgCameraSettingsSO，使用内置默认参数");

            ApplyLensAndAim();

            var s = Settings;
            Yaw = Mathf.Repeat(initialYaw, 360f);
            Distance = Mathf.Clamp(s.defaultDistance, s.minDistance, s.maxDistance);
            ApplyOffset();
        }

        private void Update()
        {
            // 服务在启动流程里才注册 → 逐帧重试直到就绪（不做任何阻塞/超时假死）
            if (_input == null)
            {
                IInputService resolved;
                if (!ServiceLocator.TryGet(out resolved) || resolved == null) return;
                _input = resolved;
            }

            var s = Settings;

            LastRotateInput = _input.GetAxis(GameActions.CameraRotate);
            LastZoomInput = _input.GetAxis(GameActions.Zoom);

            var changed = false;

            if (Mathf.Abs(LastRotateInput) > 0.0001f)
            {
                Yaw = Mathf.Repeat(Yaw + LastRotateInput * s.yawSpeedDegrees * Time.deltaTime, 360f);
                changed = true;
            }

            // 滚轮向上为正 = 期望拉近 → 距离减小
            if (Mathf.Abs(LastZoomInput) > 0.0001f)
            {
                Distance = Mathf.Clamp(Distance - LastZoomInput * s.zoomSpeed, s.minDistance, s.maxDistance);
                changed = true;
            }

            if (changed) ApplyOffset();
        }

        // ---------- 正式 API（存档恢复 / 测试驱动 / 换目标） ----------

        /// <summary>设置水平环绕角（度）。</summary>
        public void SetYaw(float degrees)
        {
            Yaw = Mathf.Repeat(degrees, 360f);
            ApplyOffset();
        }

        /// <summary>设置相机距离（米，自动 clamp 到配置范围）。</summary>
        public void SetDistance(float distance)
        {
            var s = Settings;
            Distance = Mathf.Clamp(distance, s.minDistance, s.maxDistance);
            ApplyOffset();
        }

        /// <summary>切换跟随目标（同时作为注视目标）。</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (virtualCamera == null) return;
            virtualCamera.Follow = newTarget;
            virtualCamera.LookAt = newTarget;
        }

        /// <summary>按当前 Yaw / 俯角 / Distance 计算的世界偏移（相机相对角色的位移）。</summary>
        public Vector3 ComputeOffset()
        {
            var s = Settings;
            var pitchRad = s.pitchDegrees * Mathf.Deg2Rad;
            var horizontal = Distance * Mathf.Cos(pitchRad);
            var height = Distance * Mathf.Sin(pitchRad);

            var planar = Quaternion.Euler(0f, Yaw, 0f) * new Vector3(0f, 0f, -horizontal);
            return new Vector3(planar.x, height, planar.z);
        }

        private void ApplyOffset()
        {
            if (body != null) body.m_FollowOffset = ComputeOffset();
        }

        private void ApplyLensAndAim()
        {
            var s = Settings;

            if (virtualCamera != null)
            {
                var lens = virtualCamera.m_Lens;
                lens.FieldOfView = s.fieldOfView;
                lens.NearClipPlane = s.nearClip;
                lens.FarClipPlane = s.farClip;
                virtualCamera.m_Lens = lens;
            }

            if (aim != null) aim.m_TrackedObjectOffset = s.lookOffset;

            if (body != null)
            {
                body.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
                body.m_XDamping = s.followDamping;
                body.m_YDamping = s.followDamping;
                body.m_ZDamping = s.followDamping;
            }
        }
    }
}
