using UnityEngine;

namespace Game.RPG.Cameras
{
    /// <summary>
    /// 俯视角环绕相机的可调参数（配置资产，策划可改）。
    /// <para>
    /// 设计取舍：相机**只围绕角色转**，不改变角色朝向（角色朝向由移动方向决定，见 A4 的 PlayerMotor）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Game/RPG/Camera Settings", fileName = "RpgCameraSettings")]
    public sealed class RpgCameraSettingsSO : ScriptableObject
    {
        [Header("视角")]
        [Tooltip("俯角（度）：90 = 垂直俯视；55 为常用斜俯视")]
        public float pitchDegrees = 55f;

        [Tooltip("A/D 环绕速度（度/秒）")]
        public float yawSpeedDegrees = 180f;

        [Header("缩放")]
        [Tooltip("滚轮灵敏度：每 1 单位滚轮输入改变的距离（米）")]
        public float zoomSpeed = 2f;

        [Tooltip("默认距离（米）")]
        public float defaultDistance = 12f;

        [Tooltip("最近距离（米）")]
        public float minDistance = 7f;

        [Tooltip("最远距离（米）")]
        public float maxDistance = 20f;

        [Header("跟随")]
        [Tooltip("跟随阻尼（秒）：0 = 硬跟随，数值越大越「拖沓」（有滞后感）")]
        public float followDamping = 0.15f;

        [Tooltip("注视点相对角色根节点的偏移（一般抬高到胸口）")]
        public Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);

        [Header("镜头")]
        public float fieldOfView = 50f;
        public float nearClip = 0.3f;
        public float farClip = 300f;
    }
}
