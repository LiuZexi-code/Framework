using System;
using System.Collections.Generic;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Audio;
using Framework.Abstractions.Logging;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Adapters.Audio
{
    /// <summary>
    /// 音频服务：只负责**地址解析 + 播放语义**，发声细节全部交给 <see cref="IAudioBackend"/>。
    /// <para>
    /// 资源策略：地址必须**先预热**（<see cref="Preload"/>）。播放时只做「查已加载」，
    /// 查不到就跳过 + 记一次警告 —— **不做同步阻塞加载**（避免首个 <c>WaitForCompletion</c>
    /// 造成的启动期「Addressables 初始化互锁」）。
    /// </para>
    /// <para>
    /// 与输入服务一致的取舍：契约层不依赖具体实现，因此本类可以在纯 C# 测试里用假后端完整验证。
    /// </para>
    /// </summary>
    public sealed class AudioService : IAudioService
    {
        private readonly IAudioBackend _backend;
        private readonly IAssetService _assets;
        private readonly ILogger _logger;

        private readonly HashSet<string> _warnedMissing = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _preloaded = new Dictionary<string, bool>(StringComparer.Ordinal);

        private string _currentBgm;
        private bool _disposed;

        public AudioService(IAudioBackend backend, IAssetService assets = null, ILogger logger = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _assets = assets;
            _logger = logger;

            _backend.PlaybackFinished += OnPlaybackFinished;
            _backend.Initialize();
        }

        public IAudioSettings Settings => _backend.Settings;

        public string CurrentBgm => _currentBgm;

        public event Action<string> PlaybackFinished;

        // ---------- BGM ----------

        public void PlayBgm(string address, float fadeInSeconds = 0.5f, bool loop = true)
        {
            if (_disposed) return;

            if (string.IsNullOrEmpty(address))
            {
                Warn("PlayBgm 收到空地址，已忽略");
                return;
            }

            // 同地址重复调用不重启（切场景时无脑调用 PlayBgm 也不会打断音乐）
            if (string.Equals(_currentBgm, address, StringComparison.Ordinal)) return;

            var clip = Resolve(address, AudioBus.Bgm);
            if (clip == null) return;

            _backend.StopBus(AudioBus.Bgm, 0f);   // 直接停旧的，避免「旧淡出 + 新淡入」叠加
            _currentBgm = address;

            _backend.Play(new AudioPlayRequest(
                address, clip, AudioBus.Bgm, default, spatial: false, loop: loop,
                volumeScale: 1f, fadeInSeconds: fadeInSeconds));
        }

        public void StopBgm(float fadeOutSeconds = 0.5f)
        {
            if (_disposed) return;

            _currentBgm = null;
            _backend.StopBus(AudioBus.Bgm, fadeOutSeconds);
        }

        // ---------- SFX / Voice ----------

        public AudioHandle PlaySfx(string address, Vector3? position = null, float volumeScale = 1f)
        {
            if (_disposed) return AudioHandle.None;

            var clip = Resolve(address, AudioBus.Sfx);
            if (clip == null) return AudioHandle.None;

            return _backend.Play(new AudioPlayRequest(
                address, clip, AudioBus.Sfx, position ?? default, spatial: position.HasValue,
                loop: false, volumeScale: volumeScale, fadeInSeconds: 0f));
        }

        public AudioHandle PlayVoice(string address, float volumeScale = 1f)
        {
            if (_disposed) return AudioHandle.None;

            var clip = Resolve(address, AudioBus.Voice);
            if (clip == null) return AudioHandle.None;

            return _backend.Play(new AudioPlayRequest(
                address, clip, AudioBus.Voice, default, spatial: false,
                loop: false, volumeScale: volumeScale, fadeInSeconds: 0f));
        }

        public void StopVoice(float fadeOutSeconds = 0.15f)
        {
            if (_disposed) return;
            _backend.StopBus(AudioBus.Voice, fadeOutSeconds);
        }

        // ---------- Ducking / 暂停 ----------

        public void DuckBgm(float ratio = 0.35f, float seconds = 0.25f)
        {
            if (_disposed) return;
            _backend.SetDuck(AudioBus.Bgm, ratio, seconds);
        }

        public void UnduckBgm(float seconds = 0.25f)
        {
            if (_disposed) return;
            _backend.SetDuck(AudioBus.Bgm, 1f, seconds);
        }

        public void SetPaused(bool paused)
        {
            if (_disposed) return;
            _backend.SetPaused(paused);
        }

        // ---------- 资源 ----------

        public void Preload(string address)
        {
            if (_disposed || string.IsNullOrEmpty(address) || _assets == null) return;
            if (_preloaded.ContainsKey(address)) return;

            _preloaded[address] = false;
            _assets.PreloadAsync(address, ok =>
            {
                _preloaded[address] = ok;
                if (!ok) WarnOnce($"音频地址预热失败：{address}");
            });
        }

        public void Release(string address)
        {
            if (string.IsNullOrEmpty(address) || _assets == null) return;

            _preloaded.Remove(address);
            _warnedMissing.Remove(address);
            _assets.ReleasePreloaded(address);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _backend.PlaybackFinished -= OnPlaybackFinished;
            _backend.Dispose();
            _preloaded.Clear();
            _warnedMissing.Clear();
            _currentBgm = null;
        }

        // ---------- 内部 ----------

        private AudioClip Resolve(string address, AudioBus bus)
        {
            if (string.IsNullOrEmpty(address))
            {
                Warn($"播放 {bus} 时收到空地址，已忽略");
                return null;
            }

            if (_assets == null) return null;

            if (_assets.TryGetLoaded<AudioClip>(address, out var clip) && clip != null) return clip;

            // 未预热：主动预热一次（下次可播），本次跳过 —— 绝不在此同步阻塞加载
            Preload(address);
            WarnOnce($"音频地址尚未预热，本次播放已跳过：{address}（已请求预热，下次生效）");
            return null;
        }

        private void OnPlaybackFinished(string address) => PlaybackFinished?.Invoke(address);

        private void Warn(string message)
        {
            if (_logger != null) _logger.Warn(message);
            else Debug.LogWarning(message);
        }

        private void WarnOnce(string message)
        {
            // 以「消息文本」为键去重，保证同一个坏地址只刷一条
            if (!_warnedMissing.Add(message)) return;
            Warn(message);
        }
    }
}
