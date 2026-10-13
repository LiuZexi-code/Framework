using System;

namespace Framework.Abstractions.Audio
{
    /// <summary>
    /// 一次播放的句柄：可查询有效性，也可单独停止（可选淡出）。
    /// <para>
    /// 由后端创建，内部只持有一个停止委托 —— 因此句柄**不持有任何 Unity 对象**，
    /// 可以在纯 C# 测试里用假后端构造。
    /// </para>
    /// </summary>
    public readonly struct AudioHandle
    {
        /// <summary>无效句柄（未播放 / 播放失败 / 地址未预热）。</summary>
        public static readonly AudioHandle None = default;

        private readonly Action<float> _stop;

        /// <summary>是否有效（有效仅代表「后端接受了这次播放」，不保证还在响）。</summary>
        public bool IsValid { get; }

        /// <summary>所属分组。</summary>
        public AudioBus Bus { get; }

        /// <summary>播放时使用的资源地址。</summary>
        public string Address { get; }

        public AudioHandle(bool isValid, AudioBus bus, string address, Action<float> stop)
        {
            IsValid = isValid;
            Bus = bus;
            Address = address;
            _stop = stop;
        }

        /// <summary>停止本次播放（<paramref name="fadeOutSeconds"/> &gt; 0 时淡出）。对无效句柄为空操作。</summary>
        public void Stop(float fadeOutSeconds = 0f)
        {
            if (!IsValid || _stop == null) return;
            _stop(fadeOutSeconds < 0f ? 0f : fadeOutSeconds);
        }

        public override string ToString() => IsValid ? $"{Address}({Bus})" : "<none>";
    }
}
