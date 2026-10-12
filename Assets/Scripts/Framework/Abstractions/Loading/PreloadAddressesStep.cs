using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Framework.Abstractions.Assets;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 异步预热一批 Addressables 地址（启动阶段的配置资产 / 进入场景前的场景资源通用）。
    /// 已去重；对已预热的地址也会得到回调（依赖 <c>IAssetService.PreloadAsync</c> 的契约）。
    /// </summary>
    public sealed class PreloadAddressesStep : ILoadStep
    {
        private readonly List<string> _addresses = new();
        private readonly int _timeoutFrames;

        public string Name { get; }

        public int Count => _addresses.Count;

        /// <param name="name">阶段名（日志/进度显示用）。</param>
        /// <param name="addresses">要预加载的地址集合（自动去重、忽略空值）。</param>
        /// <param name="timeoutFrames">兜底超时帧数（默认 600 帧≈10 秒），超时后记警告并继续。</param>
        public PreloadAddressesStep(string name, IEnumerable<string> addresses, int timeoutFrames = 600)
        {
            Name = string.IsNullOrEmpty(name) ? "预加载资源" : name;
            _timeoutFrames = timeoutFrames > 0 ? timeoutFrames : 600;

            if (addresses == null) return;
            foreach (var address in addresses)
            {
                if (string.IsNullOrEmpty(address)) continue;
                if (!_addresses.Contains(address)) _addresses.Add(address);
            }
        }

        public IEnumerator Run(ILoadContext context)
        {
            if (_addresses.Count == 0) yield break;

            IAssetService assets;
            try { assets = context.Services.Resolve<IAssetService>(); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Load] {Name}：无法解析 IAssetService（{e.Message}）");
                yield break;
            }

            int pending = _addresses.Count;
            int completed = 0;

            UnityEngine.Debug.Log($"[Load] {Name}：{_addresses.Count} 个地址（{string.Join(", ", _addresses.ToArray())}）");

            foreach (var address in _addresses)
            {
                assets.PreloadAsync(address, ok =>
                {
                    Interlocked.Decrement(ref pending);
                    if (ok) Interlocked.Increment(ref completed);
                });
            }

            int waited = 0;
            while (Volatile.Read(ref pending) > 0)
            {
                context.Report(Name, (float)Volatile.Read(ref completed) / _addresses.Count);

                if (waited++ >= _timeoutFrames)
                {
                    UnityEngine.Debug.LogWarning($"[Load] {Name} 超时（{_timeoutFrames} 帧），剩余 {Volatile.Read(ref pending)} 个地址未完成，继续后续流程");
                    break;
                }

                yield return null;
            }

            context.Report(Name, 1f);
        }
    }
}
