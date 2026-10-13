using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Audio;
using Framework.Adapters.Audio;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 音频服务（分组音量 / BGM 去重 / SFX 预热策略 / Ducking 不污染设定值 / 事件转发 / 释放幂等）
/// 与 <see cref="VolumeRamp"/> 的单元测试。
/// <para>
/// 全部为**同步**用例：本项目 UTF 版本不支持 <c>async Task</c> 测试方法。
/// </para>
/// <para>
/// 刻意**不**测「淡入淡出的时间推进」之外的 Unity 播放行为 —— 那属于 Play 实测范畴，
/// 在 EditMode 里断言「不抛异常」只会得到永不失败的假测试。
/// </para>
/// </summary>
public class AudioServiceTests
{
    private AudioClip _clip;

    [SetUp]
    public void SetUp()
    {
        _clip = AudioClip.Create("test_clip", 128, 1, 44100, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
        _clip = null;
    }

    // ---------------------------------------------------------------- 测试替身

    /// <summary>记录调用序列的假后端。</summary>
    private sealed class FakeBackend : IAudioBackend
    {
        public readonly List<AudioPlayRequest> Plays = new List<AudioPlayRequest>();
        public readonly List<AudioBus> BusStops = new List<AudioBus>();
        public readonly List<float> BusStopFades = new List<float>();
        public readonly List<KeyValuePair<AudioBus, float>> Ducks = new List<KeyValuePair<AudioBus, float>>();

        public bool Initialized;
        public bool Paused;
        public int DisposeCount;

        public IAudioSettings Settings { get; } = new AudioVolumeSettings();

        public event Action<string> PlaybackFinished;

        public void Initialize() => Initialized = true;

        public AudioHandle Play(AudioPlayRequest request)
        {
            Plays.Add(request);
            return new AudioHandle(true, request.Bus, request.Address, _ => { });
        }

        public void Stop(AudioHandle handle, float fadeOutSeconds) { }

        public void StopBus(AudioBus bus, float fadeOutSeconds)
        {
            BusStops.Add(bus);
            BusStopFades.Add(fadeOutSeconds);
        }

        public void SetDuck(AudioBus bus, float ratio, float seconds)
            => Ducks.Add(new KeyValuePair<AudioBus, float>(bus, ratio));

        public void SetPaused(bool paused) => Paused = paused;

        public void Tick(float deltaTime) { }

        public void Dispose() => DisposeCount++;

        public void RaiseFinished(string address) => PlaybackFinished?.Invoke(address);
    }

    /// <summary>只支持 AudioClip 的假资源服务：可控制「哪些地址已加载」。</summary>
    private sealed class FakeAssets : IAssetService
    {
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        public readonly List<string> PreloadRequests = new List<string>();
        public readonly List<string> Released = new List<string>();
        public bool PreloadSucceeds = true;

        public void Add(string address, AudioClip clip) => _clips[address] = clip;

        public bool TryGetLoaded<T>(string address, out T asset) where T : class
        {
            if (_clips.TryGetValue(address, out var clip) && clip is T typed)
            {
                asset = typed;
                return true;
            }

            asset = null;
            return false;
        }

        public IAssetHandle<T> Load<T>(string address) where T : class => null;

        public Task<IAssetHandle<T>> LoadAsync<T>(string address) where T : class
            => Task.FromResult<IAssetHandle<T>>(null);

        public void LoadAsync<T>(string address, Action<IAssetHandle<T>> onCompleted) where T : class
            => onCompleted?.Invoke(null);

        public void PreloadAsync(string address, Action<bool> callback)
        {
            PreloadRequests.Add(address);
            callback?.Invoke(PreloadSucceeds);
        }

        public void ReleasePreloaded(string address) => Released.Add(address);

        public void UnloadUnused() { }

        public void Dispose() { }
    }

    // ---------------------------------------------------------------- 分组音量

    [Test]
    public void Settings_EffectiveVolume_CombinesMasterAndBus()
    {
        var settings = new AudioVolumeSettings();

        settings.SetVolume(AudioBus.Master, 0.5f);
        settings.SetVolume(AudioBus.Bgm, 0.5f);
        settings.SetVolume(AudioBus.Sfx, 1f);

        Assert.AreEqual(0.25f, settings.GetEffectiveVolume(AudioBus.Bgm), 0.0001f);
        Assert.AreEqual(0.5f, settings.GetEffectiveVolume(AudioBus.Sfx), 0.0001f);
        // Master 组不叠加自己
        Assert.AreEqual(0.5f, settings.GetEffectiveVolume(AudioBus.Master), 0.0001f);
    }

    [Test]
    public void Settings_Muted_ZeroesEffectiveVolume_ButKeepsUserValues()
    {
        var settings = new AudioVolumeSettings();
        settings.SetVolume(AudioBus.Bgm, 0.8f);

        settings.Muted = true;

        Assert.AreEqual(0f, settings.GetEffectiveVolume(AudioBus.Bgm), 0.0001f);
        Assert.AreEqual(0f, settings.GetEffectiveVolume(AudioBus.Master), 0.0001f);
        // 设定值不被静音破坏
        Assert.AreEqual(0.8f, settings.GetVolume(AudioBus.Bgm), 0.0001f);

        settings.Muted = false;
        Assert.AreEqual(0.8f, settings.GetEffectiveVolume(AudioBus.Bgm), 0.0001f);
    }

    [Test]
    public void Settings_SetVolume_Clamps_AndNotifiesOnlyOnRealChange()
    {
        var settings = new AudioVolumeSettings();
        var changed = 0;
        settings.Changed += () => changed++;

        // 默认各组音量均为 1，因此必须**先降到 0.5** 才能制造一次真实变化
        settings.SetVolume(AudioBus.Sfx, 0.5f);
        Assert.AreEqual(0.5f, settings.GetVolume(AudioBus.Sfx), 0.0001f);
        Assert.AreEqual(1, changed);

        settings.SetVolume(AudioBus.Sfx, 1.5f);   // clamp 到 1（相对 0.5 是真实变化）
        Assert.AreEqual(1f, settings.GetVolume(AudioBus.Sfx), 0.0001f);
        Assert.AreEqual(2, changed);

        settings.SetVolume(AudioBus.Sfx, 2f);     // clamp 到 1，与当前值相同 → 不通知
        Assert.AreEqual(2, changed);

        settings.SetVolume(AudioBus.Sfx, -1f);    // clamp 到 0
        Assert.AreEqual(0f, settings.GetVolume(AudioBus.Sfx), 0.0001f);
        Assert.AreEqual(3, changed);

        settings.Muted = true;
        settings.Muted = true;                    // 未变化
        Assert.AreEqual(4, changed);
    }

    // ---------------------------------------------------------------- BGM

    [Test]
    public void PlayBgm_SameAddressTwice_DoesNotRestart()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();
        assets.Add("BGM/Field", _clip);

        var service = new AudioService(backend, assets);
        service.PlayBgm("BGM/Field");
        service.PlayBgm("BGM/Field");

        Assert.AreEqual(1, backend.Plays.Count);
        Assert.AreEqual("BGM/Field", service.CurrentBgm);
    }

    [Test]
    public void PlayBgm_DifferentAddress_StopsBgmBusThenPlays()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();
        assets.Add("BGM/Field", _clip);
        assets.Add("BGM/Boss", _clip);

        var service = new AudioService(backend, assets);
        service.PlayBgm("BGM/Field");
        service.PlayBgm("BGM/Boss");

        Assert.AreEqual(2, backend.Plays.Count);
        CollectionAssert.Contains(backend.BusStops, AudioBus.Bgm);
        Assert.AreEqual("BGM/Boss", service.CurrentBgm);
        Assert.IsFalse(backend.Plays[1].Spatial, "BGM 应为 2D");
        Assert.IsTrue(backend.Plays[1].Loop, "BGM 应循环");
    }

    // ---------------------------------------------------------------- SFX

    [Test]
    public void PlaySfx_WithPosition_MarksRequestSpatial()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();
        assets.Add("SFX/Hit", _clip);

        var service = new AudioService(backend, assets);
        var handle = service.PlaySfx("SFX/Hit", new Vector3(1f, 2f, 3f));

        Assert.IsTrue(handle.IsValid);
        Assert.AreEqual(1, backend.Plays.Count);
        Assert.IsTrue(backend.Plays[0].Spatial);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), backend.Plays[0].Position);
    }

    [Test]
    public void PlaySfx_WithoutPosition_Is2D()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();
        assets.Add("SFX/Click", _clip);

        var service = new AudioService(backend, assets);
        service.PlaySfx("SFX/Click");

        Assert.AreEqual(1, backend.Plays.Count);
        Assert.IsFalse(backend.Plays[0].Spatial);
    }

    [Test]
    public void PlaySfx_UnpreloadedAddress_SkipsPlayback_AndRequestsPreload()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();   // 故意不注册任何地址

        var service = new AudioService(backend, assets);

        AudioHandle handle = default;
        Assert.DoesNotThrow(() => handle = service.PlaySfx("SFX/Missing"));

        Assert.IsFalse(handle.IsValid);
        Assert.AreEqual(0, backend.Plays.Count, "未预热地址不应播放");
        CollectionAssert.Contains(assets.PreloadRequests, "SFX/Missing");
    }

    [Test]
    public void Preload_ThenPlay_Works_AndReleaseForwards()
    {
        var backend = new FakeBackend();
        var assets = new FakeAssets();

        var service = new AudioService(backend, assets);
        service.Preload("SFX/Later");
        CollectionAssert.Contains(assets.PreloadRequests, "SFX/Later");

        // 预热只是请求；真正可用取决于资源服务（这里模拟预热完成后可查到）
        assets.Add("SFX/Later", _clip);
        var handle = service.PlaySfx("SFX/Later");
        Assert.IsTrue(handle.IsValid);

        service.Release("SFX/Later");
        CollectionAssert.Contains(assets.Released, "SFX/Later");
    }

    // ---------------------------------------------------------------- Ducking

    [Test]
    public void DuckBgm_ForwardsToBackend_WithoutChangingUserVolume()
    {
        var backend = new FakeBackend();
        var service = new AudioService(backend, new FakeAssets());
        service.Settings.SetVolume(AudioBus.Bgm, 0.9f);

        service.DuckBgm(0.3f, 0.2f);

        Assert.AreEqual(1, backend.Ducks.Count);
        Assert.AreEqual(AudioBus.Bgm, backend.Ducks[0].Key);
        Assert.AreEqual(0.3f, backend.Ducks[0].Value, 0.0001f);
        // ★ 关键：用户设定值不被 duck 污染（否则设置面板会被写坏）
        Assert.AreEqual(0.9f, service.Settings.GetVolume(AudioBus.Bgm), 0.0001f);
    }

    [Test]
    public void UnduckBgm_SetsDuckBackToOne()
    {
        var backend = new FakeBackend();
        var service = new AudioService(backend, new FakeAssets());

        service.UnduckBgm();

        Assert.AreEqual(1, backend.Ducks.Count);
        Assert.AreEqual(1f, backend.Ducks[0].Value, 0.0001f);
    }

    // ---------------------------------------------------------------- 其它

    [Test]
    public void PlaybackFinished_IsForwardedFromBackend()
    {
        var backend = new FakeBackend();
        var service = new AudioService(backend, new FakeAssets());

        string received = null;
        service.PlaybackFinished += address => received = address;

        backend.RaiseFinished("SFX/Done");

        Assert.AreEqual("SFX/Done", received);
    }

    [Test]
    public void Dispose_IsIdempotent_AndDisposesBackendOnce()
    {
        var backend = new FakeBackend();
        var service = new AudioService(backend, new FakeAssets());

        Assert.IsTrue(backend.Initialized);

        service.Dispose();
        service.Dispose();

        Assert.AreEqual(1, backend.DisposeCount);
        Assert.IsNull(service.CurrentBgm);
    }

    [Test]
    public void SetPaused_ForwardsToBackend()
    {
        var backend = new FakeBackend();
        var service = new AudioService(backend, new FakeAssets());

        service.SetPaused(true);
        Assert.IsTrue(backend.Paused);

        service.SetPaused(false);
        Assert.IsFalse(backend.Paused);
    }

    // ---------------------------------------------------------------- VolumeRamp

    [Test]
    public void VolumeRamp_InterpolatesLinearly()
    {
        var ramp = new VolumeRamp(0f);
        Assert.IsTrue(ramp.IsDone, "初始应视为已到达");

        ramp.Retarget(1f, 1f);
        Assert.IsFalse(ramp.IsDone);
        Assert.AreEqual(0f, ramp.Value, 0.0001f);

        ramp.Tick(0.5f);
        Assert.AreEqual(0.5f, ramp.Value, 0.0001f);
        Assert.IsFalse(ramp.IsDone);

        ramp.Tick(0.5f);
        Assert.AreEqual(1f, ramp.Value, 0.0001f);
        Assert.IsTrue(ramp.IsDone);
    }

    [Test]
    public void VolumeRamp_ZeroDuration_IsInstant()
    {
        var ramp = new VolumeRamp(0f);
        ramp.Retarget(0.7f, 0f);

        Assert.IsTrue(ramp.IsDone);
        Assert.AreEqual(0.7f, ramp.Value, 0.0001f);
    }

    [Test]
    public void VolumeRamp_Retarget_StartsFromCurrentValue()
    {
        var ramp = new VolumeRamp(0f);
        ramp.Retarget(1f, 1f);
        ramp.Tick(0.25f);          // → 0.25

        ramp.Retarget(0f, 1f);     // 从 0.25 开始降到 0

        Assert.AreEqual(0.25f, ramp.Value, 0.0001f);
        ramp.Tick(0.5f);           // 走一半 → 0.125
        Assert.AreEqual(0.125f, ramp.Value, 0.0001f);
    }

    [Test]
    public void VolumeRamp_Finish_JumpsToTarget()
    {
        var ramp = new VolumeRamp(0f);
        ramp.Retarget(1f, 10f);

        ramp.Finish();

        Assert.IsTrue(ramp.IsDone);
        Assert.AreEqual(1f, ramp.Value, 0.0001f);
    }
}
