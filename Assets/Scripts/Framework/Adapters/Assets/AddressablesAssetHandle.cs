using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Framework.Adapters.Assets 
{
    internal sealed class AddressablesAssetHandle<T> : IAssetHandle<T> where T : class
    {
        private readonly AsyncOperationHandle<T> _handle;

        public T Asset => _handle.IsValid() ? _handle.Result : null;
        public bool IsValid => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded;
        public bool IsDone => _handle.IsDone;

        public AddressablesAssetHandle(AsyncOperationHandle<T> handle)
        {
            _handle = handle;
        }

        public void Dispose()
        {
            if (_handle.IsValid())
                Addressables.Release(_handle);
        }
    }
}