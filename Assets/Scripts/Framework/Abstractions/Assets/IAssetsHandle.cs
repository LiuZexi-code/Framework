using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Framework.Abstractions.Assets
{
    /// <summary>资源句柄，持有资源引用，Dispose 时释放。</summary>
    public interface IAssetsHandle<out T> : IDisposable where T : class
    {
        IReadOnlyList<T> Asset { get; }
        bool IsValid { get; }
        bool IsDone { get; }
    }
}