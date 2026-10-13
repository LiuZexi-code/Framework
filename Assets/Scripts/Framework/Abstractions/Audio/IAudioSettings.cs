using System;

namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 音频分组音量设置（运行期状态；持久化由业务侧负责，框架层**不依赖 <c>IStorage</c>**）。
    /// <para>
    /// 「有效音量」的语义固定为：<c>Master</c> 分组 = 自身；其它分组 = <c>Master × 分组</c>；
    /// <see cref="Muted"/> 为真时全部为 0。
    /// </para>
    /// <para>
    /// ⚠️ 「临时压低」（Ducking，如对话时压低 BGM）**不改变用户设定值**，由
    /// <see cref="IAudioBackend.SetDuck"/> 另行维护，避免污染设置面板。
    /// </para>
    /// </summary>
    public interface IAudioSettings
    {
        /// <summary>读取某一分组的**用户设定值**（0..1）。</summary>
        float GetVolume(AudioBus bus);

        /// <summary>设置某一分组的用户设定值（自动 clamp 到 0..1；值变化时抛 <see cref="Changed"/>）。</summary>
        void SetVolume(AudioBus bus, float value01);

        /// <summary>总静音（不改变各分组设定值，仅让有效音量归零）。</summary>
        bool Muted { get; set; }

        /// <summary>该分组当前的有效音量（已计入 Master 与 <see cref="Muted"/>）。</summary>
        float GetEffectiveVolume(AudioBus bus);

        /// <summary>任一分组音量或 <see cref="Muted"/> 变化时触发。</summary>
        event Action Changed;
    }
}
