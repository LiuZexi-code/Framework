using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Adapters.Assets
{
    public sealed class AddressablesAssetService : IAssetService,IAddressablesCapability, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, AsyncOperationHandle> _preloaded = new();

        public AddressablesAssetService(ILogger logger)
        {
            _logger = logger;
        }

        // ---------- 同步 ----------

        public IAssetHandle<T> Load<T>(string address) where T : class
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            handle.WaitForCompletion();

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                _logger.Error($"[Assets] Load failed: {address}");
                return null;
            }

            return new AddressablesAssetHandle<T>(handle);
        }

        public bool TryGetLoaded<T>(string address, out T asset) where T : class
        {
            if (_preloaded.TryGetValue(address, out var handle)
                && handle.IsValid()
                && handle.Status == AsyncOperationStatus.Succeeded
                && handle.Result is T typed)
            {
                asset = typed;
                return true;
            }
            asset = null;
            return false;
        }

        // ---------- 异步 ----------

        public async Task<IAssetHandle<T>> LoadAsync<T>(string address) where T : class
        {
            var handle = Addressables.LoadAssetAsync<T>(address);

            // 必须等待完成，否则调用方拿到的句柄 Status 还是 None，IsValid/Asset 都不可用
            try
            {
                await handle.Task;
            }
            catch (Exception e)
            {
                _logger.Error($"[Assets] Load failed: {address}\n{e}");
                return null;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                _logger.Error($"[Assets] Load failed: {address}");
                return null;
            }

            return new AddressablesAssetHandle<T>(handle);
        }

        public void LoadAsync<T>(string address, Action<IAssetHandle<T>> onCompleted) where T : class
        {
            var handle = Addressables.LoadAssetAsync<T>(address);

            handle.Completed += (op) =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    _logger.Error($"[Assets] Load failed: {address}");
                    onCompleted?.Invoke(null);
                    return;
                }

                onCompleted?.Invoke(new AddressablesAssetHandle<T>(op));
            };
        }

        public void LoadAsyncByLable<T>(string lable,Action<IAssetsHandle<T>> onCompleted, Action<T> callback = null) where T : class
        {
            var handle = Addressables.LoadAssetsAsync(lable,callback);
            handle.Completed += (op) =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    _logger.Error($"[Assets] Lable Load failed: {lable}");
                    onCompleted?.Invoke(null);
                    return;
                }
                onCompleted?.Invoke(new AddressablesAssetsHandle<T>(op));
            };
        }

        // ---------- 预加载 ----------

        public void PreloadAsync(string address,Action<bool> callback)
        {
            // 已预热过也要回调，否则调用方等待“预热完成”时会永远收不到通知
            if (_preloaded.ContainsKey(address))
            {
                callback?.Invoke(true);
                return;
            }

            var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(address);

            handle.Completed += op =>
            {
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    _logger.Error($"[Assets] Preload failed: {address}");
                    callback?.Invoke(false);
                    return;
                }
                _preloaded[address] = handle;
                callback?.Invoke(true);
            };
        }

        public void ReleasePreloaded(string address)
        {
            if (_preloaded.TryGetValue(address, out var handle))
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                _preloaded.Remove(address);
            }
        }

        // ---------- 卸载 ----------

        public void UnloadUnused()
        {
            if (_preloaded.Count == 0)
                return;

            // 1. 先拷贝一份句柄，再清空字典。
            //    避免 Release 触发的回调（如 Addressables 内部事件）在遍历中修改 _preloaded。
            var handles = new List<AsyncOperationHandle>(_preloaded.Count);
            foreach (var kvp in _preloaded)
                handles.Add(kvp.Value);

            _preloaded.Clear();

            // 2. 释放服务持有的所有预加载引用。
            //    如果某个资源同时被外部（通过 Load/LoadAsync 得到的句柄）引用，
            //    它的引用计数不会归零，不会被真正卸载。
            foreach (var handle in handles)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            // 3. 触发 Unity 底层的未使用资源回收。
            //    ⚠️ 高开销操作，仅应在玩家不敏感的安全时机调用，
            //    绝不可在游戏进行中每帧或频繁调用。
            Resources.UnloadUnusedAssets();
        }

        public void Dispose()
        {
            foreach (var handle in _preloaded.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            _preloaded.Clear();
        }
    }
}