using UnityEngine;

namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 一次播放请求（服务 → 后端）。
    /// <para>
    /// 刻意用**结构体**承载：后端签名不会随「音调 / 优先级 / Mixer 分组 / 空间衰减曲线」等
    /// 后续需求而膨胀（加字段即可，不改接口）。
    /// </para>
    /// </summary>
    public readonly struct AudioPlayRequest
    {
        /// <summary>资源地址（用于日志/去重/完成回调）。</summary>
        public readonly string Address;

        /// <summary>要播放的剪辑（由服务解析好后传入，后端不做资源加载）。</summary>
        public readonly AudioClip Clip;

        public readonly AudioBus Bus;

        /// <summary>世界坐标（仅 <see cref="Spatial"/> 为真时有意义）。</summary>
        public readonly Vector3 Position;

        /// <summary>是否 3D 空间化。</summary>
        public readonly bool Spatial;

        public readonly bool Loop;

        /// <summary>单次播放的音量倍率（会再乘分组音量与 Master 音量）。</summary>
        public readonly float VolumeScale;

        /// <summary>淡入时长（秒，&lt;= 0 表示立即到目标音量）。</summary>
        public readonly float FadeInSeconds;

        public AudioPlayRequest(
            string address,
            AudioClip clip,
            AudioBus bus,
            Vector3 position,
            bool spatial,
            bool loop,
            float volumeScale,
            float fadeInSeconds)
        {
            Address = address;
            Clip = clip;
            Bus = bus;
            Position = position;
            Spatial = spatial;
            Loop = loop;
            VolumeScale = volumeScale < 0f ? 0f : volumeScale;
            FadeInSeconds = fadeInSeconds < 0f ? 0f : fadeInSeconds;
        }
    }
}
