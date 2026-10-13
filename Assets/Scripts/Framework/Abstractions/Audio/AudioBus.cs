namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 音频总线（音量分组）。
    /// <para>
    /// 框架只定义「分组」这一概念，**不定义任何具体音效名 / 资源地址**（与输入服务的边界一致）。
    /// </para>
    /// <para>
    /// 实际音量 = <c>播放时的 volumeScale × 该分组音量 × Master 音量</c>；
    /// <see cref="IAudioSettings.Muted"/> 为真时恒为 0。
    /// </para>
    /// </summary>
    public enum AudioBus
    {
        /// <summary>主音量（总开关），作用于其它所有分组。</summary>
        Master = 0,

        /// <summary>背景音乐（通常单实例、循环、跨场景不断）。</summary>
        Bgm = 1,

        /// <summary>音效（可多实例、可 2D/3D 空间化）。</summary>
        Sfx = 2,

        /// <summary>语音 / 对话配音（通常 2D、按行播放、可打断）。</summary>
        Voice = 3,
    }
}
