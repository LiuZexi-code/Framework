using System;
using Framework.Abstractions.Audio;

namespace Framework.Adapters.Audio
{
    /// <summary>
    /// 分组音量设置的默认实现（纯 C#，无 Unity 依赖，可单测）。
    /// <para>
    /// 只保存**用户设定值**；有效音量按固定语义计算：
    /// <c>Master</c> = 自身，其它 = <c>Master × 分组</c>，<see cref="Muted"/> 时恒为 0。
    /// </para>
    /// <para>
    /// ⚠️ 类名刻意不是 <c>AudioSettings</c>：Unity 自带 <c>UnityEngine.AudioSettings</c>，
    /// 同名会让任何「<c>using UnityEngine;</c> + <c>using Framework.Adapters.Audio;</c>」的外部文件遭遇 CS0104。
    /// </para>
    /// </summary>
    public sealed class AudioVolumeSettings : IAudioSettings
    {
        private const int BusCount = 4;
        private const float Epsilon = 0.0001f;

        private readonly float[] _volumes = { 1f, 1f, 1f, 1f };
        private bool _muted;

        /// <summary>任一分组音量或静音状态变化时触发（值未实际变化则不触发）。</summary>
        public event Action Changed;

        public bool Muted
        {
            get => _muted;
            set
            {
                if (_muted == value) return;
                _muted = value;
                Changed?.Invoke();
            }
        }

        public float GetVolume(AudioBus bus) => _volumes[Index(bus)];

        public void SetVolume(AudioBus bus, float value01)
        {
            var index = Index(bus);
            var clamped = Clamp01(value01);
            if (Math.Abs(_volumes[index] - clamped) < Epsilon) return;

            _volumes[index] = clamped;
            Changed?.Invoke();
        }

        public float GetEffectiveVolume(AudioBus bus)
        {
            if (_muted) return 0f;

            var master = _volumes[(int)AudioBus.Master];
            return bus == AudioBus.Master ? master : master * _volumes[Index(bus)];
        }

        private static int Index(AudioBus bus)
        {
            var index = (int)bus;
            return index >= 0 && index < BusCount ? index : 0;
        }

        private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
