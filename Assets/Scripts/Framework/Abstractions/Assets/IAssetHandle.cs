using System;
using System.Threading.Tasks;

namespace Framework.Abstractions.Assets
{
    /// <summary>资源句柄，持有资源引用，Dispose 时释放。</summary>
    public interface IAssetHandle<out T> : IDisposable where T : class
    {
        T Asset { get; }
        bool IsValid { get; }
        bool IsDone { get; }
    }
}