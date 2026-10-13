using System;
using System.Collections.Generic;
using Framework.Abstractions.Audio;
using Framework.Abstractions.Logging;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Adapters.Audio
{
    /// <summary>
    /// 基于 <c>AudioSource</c> 池的音频后端。
    /// <para>
    /// 结构：BGM × 1（循环、2D） + SFX × 8（可 2D/3D） + Voice × 2（2D）。
    /// 所有播放源挂在 <c>DontDestroyOnLoad</c> 的 <c>[AudioRoot]</c> 下 → **BGM 跨场景不断**。
    /// </para>
    /// <para>
    /// ⚠️ <c>[AudioRoot]</c> **刻意不带 AudioListener**：监听器由场景相机提供，
    /// 避免与框架既有的「过渡期双 AudioListener」约束叠加。
    /// </para>
    /// <para>
    /// 每个源的实际音量为 <c>淡入淡出值 × volumeScale × 分组有效音量 × Duck 倍率</c>，
    /// 每帧统一重算（源数量很少），因此用户改音量/静音/duck 都能**立即生效且不重启播放**。
    /// </para>
    /// </summary>
    public sealed class UnityAudioBackend : IAudioBackend
    {
        private const int SfxVoiceCount = 8;
        private const int VoiceVoiceCount = 2;
        private const float MaxDistance = 30f;
        private const string RootName = "[AudioRoot]";

        /// <summary>一个播放声道（= 一个 AudioSource + 它的音量状态）。</summary>
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioBus Bus;
            public float VolumeScale = 1f;
            public string Address;
            public bool StoppingWhenSilent;
            public readonly VolumeRamp Ramp = new VolumeRamp(0f);
        }

        private readonly ILogger _logger;

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly List<Voice> _sfxVoices = new List<Voice>();
        private readonly List<Voice> _voiceVoices = new List<Voice>();
        private readonly VolumeRamp[] _duckRamps = { new VolumeRamp(1f), new VolumeRamp(1f), new VolumeRamp(1f), new VolumeRamp(1f) };

        private Voice _bgmVoice;
        private GameObject _root;
        private int _sfxCursor;
        private int _voiceCursor;
        private bool _paused;
        private bool _disposed;
        private bool _warnedBusy;

        public UnityAudioBackend(ILogger logger = null)
        {
            _logger = logger;
        }

        public IAudioSettings Settings { get; } = new AudioVolumeSettings();

        public event Action<string> PlaybackFinished;

        public void Initialize()
        {
            if (_disposed || _root != null) return;

            // 兜底：Object.Destroy 是**延迟到帧末**执行的，所以「同一帧内 Dispose 后立刻重建」
            // 会短暂出现两个 [AudioRoot] → 两组声道同时发声。这里立即清掉残留。
            var stale = GameObject.Find(RootName);
            if (stale != null)
            {
                Warn($"发现残留的 {RootName}（上次未正常释放），已立即销毁以避免声道叠加");
                UnityEngine.Object.DestroyImmediate(stale);
            }

            _root = new GameObject(RootName);
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var host = _root.AddComponent<AudioHost>();
            host.Bind(this);

            _bgmVoice = CreateVoice("Bgm", AudioBus.Bgm);
            for (int i = 0; i < SfxVoiceCount; i++) _sfxVoices.Add(CreateVoice($"Sfx_{i}", AudioBus.Sfx));
            for (int i = 0; i < VoiceVoiceCount; i++) _voiceVoices.Add(CreateVoice($"Voice_{i}", AudioBus.Voice));
        }

        // ---------- 播放 / 停止 ----------

        public AudioHandle Play(AudioPlayRequest request)
        {
            if (_disposed) return AudioHandle.None;
            if (request.Clip == null)
            {
                Warn($"音频剪辑为空，已跳过播放：{request.Address}");
                return AudioHandle.None;
            }

            var voice = TakeVoice(request.Bus);
            if (voice == null)
            {
                WarnOnce($"[Audio] 声道耗尽（分组 {request.Bus}），本次播放被丢弃：{request.Address}");
                return AudioHandle.None;
            }

            var source = voice.Source;
            source.clip = request.Clip;
            source.loop = request.Loop;
            source.spatialBlend = request.Spatial ? 1f : 0f;
            if (request.Spatial) source.transform.position = request.Position;

            voice.Bus = request.Bus;
            voice.VolumeScale = request.VolumeScale;
            voice.Address = request.Address;
            voice.StoppingWhenSilent = false;
            voice.Ramp.Retarget(1f, request.FadeInSeconds);

            source.volume = 0f;
            source.Play();
            ApplyVolumes();

            var captured = voice;
            return new AudioHandle(true, request.Bus, request.Address, fade => StopVoice(captured, fade));
        }

        public void Stop(AudioHandle handle, float fadeOutSeconds)
        {
            if (!handle.IsValid) return;
            handle.Stop(fadeOutSeconds);
        }

        public void StopBus(AudioBus bus, float fadeOutSeconds)
        {
            if (_disposed) return;

            for (int i = 0; i < _voices.Count; i++)
                if (_voices[i].Bus == bus)
                    StopVoice(_voices[i], fadeOutSeconds);
        }

        public void SetDuck(AudioBus bus, float ratio, float seconds)
        {
            if (_disposed) return;

            var clamped = ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio);
            _duckRamps[Index(bus)].Retarget(clamped, seconds);
        }

        public void SetPaused(bool paused)
        {
            if (_disposed) return;
            _paused = paused;
            AudioListener.pause = paused;
        }

        // ---------- 每帧推进 ----------

        public void Tick(float deltaTime)
        {
            if (_disposed) return;

            for (int i = 0; i < _duckRamps.Length; i++) _duckRamps[i].Tick(deltaTime);

            for (int i = 0; i < _voices.Count; i++)
            {
                var voice = _voices[i];
                if (voice.Source == null) continue;

                voice.Ramp.Tick(deltaTime);

                if (voice.StoppingWhenSilent && voice.Ramp.IsDone)
                {
                    voice.Source.Stop();
                    voice.Source.clip = null;
                    voice.Address = null;
                    voice.StoppingWhenSilent = false;
                    continue;
                }

                RaiseFinishedIfEnded(voice);
            }

            ApplyVolumes();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            AudioListener.pause = false;

            for (int i = 0; i < _voices.Count; i++)
            {
                var source = _voices[i].Source;
                if (source != null) source.Stop();
            }

            _voices.Clear();
            _sfxVoices.Clear();
            _voiceVoices.Clear();
            _bgmVoice = null;

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // ---------- 内部 ----------

        private Voice CreateVoice(string name, AudioBus bus)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.maxDistance = MaxDistance;
            source.dopplerLevel = 0f;
            source.volume = 0f;

            var voice = new Voice { Source = source, Bus = bus, Address = null };
            _voices.Add(voice);
            return voice;
        }

        private Voice TakeVoice(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Bgm:
                    return _bgmVoice;

                case AudioBus.Voice:
                    return TakeFrom(_voiceVoices, ref _voiceCursor);

                default:
                    return TakeFrom(_sfxVoices, ref _sfxCursor);
            }
        }

        /// <summary>优先空闲声道；全部在用时按轮转**抢占**（避免同时音效过多时静默丢音）。</summary>
        private static Voice TakeFrom(List<Voice> pool, ref int cursor)
        {
            if (pool.Count == 0) return null;

            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].Source.isPlaying && !pool[i].StoppingWhenSilent)
                    return pool[i];

            var voice = pool[cursor % pool.Count];
            cursor = (cursor + 1) % pool.Count;
            return voice;
        }

        private void StopVoice(Voice voice, float fadeOutSeconds)
        {
            if (voice?.Source == null) return;

            if (fadeOutSeconds <= 0f)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
                voice.Address = null;
                voice.Ramp.Retarget(0f, 0f);
                voice.StoppingWhenSilent = false;
                return;
            }

            voice.StoppingWhenSilent = true;
            voice.Ramp.Retarget(0f, fadeOutSeconds);
        }

        private void ApplyVolumes()
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                var voice = _voices[i];
                if (voice.Source == null) continue;

                var duck = _duckRamps[Index(voice.Bus)].Value;
                voice.Source.volume = voice.Ramp.Value * voice.VolumeScale * Settings.GetEffectiveVolume(voice.Bus) * duck;
            }
        }

        private void RaiseFinishedIfEnded(Voice voice)
        {
            if (string.IsNullOrEmpty(voice.Address)) return;
            if (voice.Source.loop) return;
            if (voice.Source.isPlaying) return;

            // 非循环播放自然停下 → 通知一次并清空地址（避免重复触发）
            var address = voice.Address;
            voice.Address = null;
            voice.Ramp.Retarget(0f, 0f);
            PlaybackFinished?.Invoke(address);
        }

        private static int Index(AudioBus bus)
        {
            var index = (int)bus;
            return index >= 0 && index < 4 ? index : 0;
        }

        private void Warn(string message)
        {
            if (_logger != null) _logger.Warn(message);
            else Debug.LogWarning(message);
        }

        private void WarnOnce(string message)
        {
            if (_warnedBusy) return;
            _warnedBusy = true;
            Warn(message);
        }
    }
}
