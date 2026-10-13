using Framework.Abstractions.Audio;
using Framework.Abstractions.Modules;
using Framework.Core;

namespace Framework.Adapters.Audio
{
    /// <summary>
    /// 框架内置音频模块：注册分组音量、<c>AudioSource</c> 后端与音频服务，
    /// 保证 <c>IAudioService</c> **永远可解析**（业务代码无需判空）。
    /// <para>
    /// 与 <c>InputServiceModule</c>（Order = 10）同一范式：业务若要替换后端/设置实现，
    /// 只需再写一个 <see cref="IGlobalServiceModule"/>，<c>Order</c> 取更大值覆盖注册即可。
    /// </para>
    /// <para>
    /// 注册是**惰性**的（首次 Resolve 才构造），因此不会给启动流程增加副作用。
    /// </para>
    /// </summary>
    public sealed class AudioServiceModule : IGlobalServiceModule
    {
        public int Order => 20;

        public void RegisterGlobal(IServiceRegistry registry)
        {
            registry.Register<IAudioSettings, AudioVolumeSettings>();
            registry.Register<IAudioBackend, UnityAudioBackend>();
            registry.Register<IAudioService, AudioService>();
        }
    }
}
