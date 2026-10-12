using System;
using System.Threading.Tasks;

namespace Framework.Abstractions.Assets
{
    public interface IAssetService
    {
        // 同步加载（仅适合已加载或小资源）
        IAssetHandle<T> Load<T>(string address) where T : class;

        // 同步，尝试获取预加载资源
        bool TryGetLoaded<T>(string address, out T asset) where T : class;

        // 异步加载（推荐）
        Task<IAssetHandle<T>> LoadAsync<T>(string address) where T : class;
        void LoadAsync<T>(string address,Action<IAssetHandle<T>> onCompleted) where T : class;

        // 预加载（不返回句柄，内部缓存）
        void PreloadAsync(string address,Action<bool> callback);

        // 释放预加载
        void ReleasePreloaded(string address);

        // 卸载未使用资源
        void UnloadUnused();

        // 清理所有
        void Dispose();
    }
}