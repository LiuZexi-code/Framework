using System;

namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 音频后端：真正发声的一侧（框架默认实现基于 <c>AudioSource</c> 池）。
    /// <para>
    /// 把后端抽出来的目的与输入服务一致：**契约层不依赖任何具体实现**，
    /// 从而可以在纯 C# 测试里用假后端断言「播放了什么 / 音量怎么算 / 何时 duck」。
    /// </para>
    /// <para>
    /// 框架**不调用** <see cref="Tick"/>：淡入淡出的推进由实现自己在内部主机（MonoBehaviour.Update）驱动，
    /// 与「框架无 tick 契约」的既有架构保持一致。
    /// </para>
    /// </summary>
    public interface IAudioBackend : IDisposable
    {
        /// <summary>本后端使用的分组音量设置。</summary>
        IAudioSettings Settings { get; }

        /// <summary>创建内部播放资源（幂等）。</summary>
        void Initialize();

        /// <summary>播放一次；返回可停止的句柄（无法播放时返回 <see cref="AudioHandle.None"/>）。</summary>
        AudioHandle Play(AudioPlayRequest request);

        /// <summary>停止某个句柄（<paramref name="fadeOutSeconds"/> &gt; 0 时淡出）。</summary>
        void Stop(AudioHandle handle, float fadeOutSeconds);

        /// <summary>停止某一分组的全部播放。</summary>
        void StopBus(AudioBus bus, float fadeOutSeconds);

        /// <summary>
        /// 临时压低/恢复某一分组（<paramref name="ratio"/> = 1 表示不压低）。
        /// 与用户设定值相互独立 —— 这是 Ducking 不污染设置面板的关键。
        /// </summary>
        void SetDuck(AudioBus bus, float ratio, float seconds);

        /// <summary>整体暂停 / 恢复（用于暂停菜单）。</summary>
        void SetPaused(bool paused);

        /// <summary>推进淡入淡出（由实现内部的主机调用，框架与业务都不应调用）。</summary>
        void Tick(float deltaTime);

        /// <summary>一次非循环播放自然结束时触发（参数为资源地址）。</summary>
        event Action<string> PlaybackFinished;
    }
}
