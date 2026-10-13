using UnityEngine;

namespace Framework.Adapters.Audio
{
    /// <summary>
    /// <c>[AudioRoot]</c> 上的驱动器：把 Unity 的 <c>Update</c> 转成 <see cref="UnityAudioBackend.Tick"/>。
    /// <para>
    /// 框架没有 tick 契约（见输入服务的同类取舍），因此淡入淡出的推进需要一个极小的 MonoBehaviour 宿主；
    /// 它是**运行时动态添加**的私有实现细节，不暴露给业务。
    /// </para>
    /// </summary>
    internal sealed class AudioHost : MonoBehaviour
    {
        private UnityAudioBackend _backend;

        public void Bind(UnityAudioBackend backend) => _backend = backend;

        private void Update()
        {
            // 用 unscaledDeltaTime：暂停/时停时音量过渡仍应平滑推进
            _backend?.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy() => _backend = null;
    }
}
