using System;
using UnityEngine;

namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 框架音频服务：负责**地址解析 + 句柄生命周期 + 语义化播放**（BGM / SFX / Voice / Ducking），
    /// 真正的发声交给 <see cref="IAudioBackend"/>。
    /// <para>
    /// 边界：框架只提供「播放能力」，**不含任何具体音效语义**（不出现 Click / Hit / Pickup 这类名字）。
    /// </para>
    /// <para>
    /// 资源约定：播放前应确保地址已预热（<see cref="Preload"/>）；
    /// 未预热的地址会被跳过并记一次警告，**不做同步阻塞加载**（避免首个
    /// <c>WaitForCompletion</c> 造成的启动期互锁）。
    /// </para>
    /// </summary>
    public interface IAudioService : IDisposable
    {
        /// <summary>分组音量设置（持久化由业务侧负责）。</summary>
        IAudioSettings Settings { get; }

        /// <summary>当前 BGM 地址；无 BGM 时为 null。</summary>
        string CurrentBgm { get; }

        /// <summary>播放背景音乐（同地址重复调用**不会重启**）。</summary>
        void PlayBgm(string address, float fadeInSeconds = 0.5f, bool loop = true);

        /// <summary>停止背景音乐。</summary>
        void StopBgm(float fadeOutSeconds = 0.5f);

        /// <summary>
        /// 播放音效。<paramref name="position"/> 为 null 时按 2D 播放，否则 3D 空间化。
        /// </summary>
        AudioHandle PlaySfx(string address, Vector3? position = null, float volumeScale = 1f);

        /// <summary>播放语音 / 对话配音（2D，按行播放）。</summary>
        AudioHandle PlayVoice(string address, float volumeScale = 1f);

        /// <summary>停止全部语音。</summary>
        void StopVoice(float fadeOutSeconds = 0.15f);

        /// <summary>临时压低 BGM（对话 / 过场用），<paramref name="ratio"/> = 1 表示不压低。</summary>
        void DuckBgm(float ratio = 0.35f, float seconds = 0.25f);

        /// <summary>恢复 BGM 音量（不改用户设定值）。</summary>
        void UnduckBgm(float seconds = 0.25f);

        /// <summary>整体暂停 / 恢复（暂停菜单）。注意：暂停期间仍可发起播放，只是听不到。</summary>
        void SetPaused(bool paused);

        /// <summary>预热一个地址（非阻塞）。</summary>
        void Preload(string address);

        /// <summary>释放一个地址的预热引用。</summary>
        void Release(string address);

        /// <summary>一次非循环播放自然结束时触发（参数为资源地址）。</summary>
        event Action<string> PlaybackFinished;
    }
}
