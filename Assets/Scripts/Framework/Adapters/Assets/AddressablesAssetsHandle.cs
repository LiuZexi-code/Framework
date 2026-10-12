using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Framework.Adapters.Assets 
{
    internal sealed class AddressablesAssetsHandle<T> : IAssetsHandle<T> where T : class
    {
        private readonly AsyncOperationHandle<IList<T>> _handle;
        public IReadOnlyList<T> Asset => _handle.IsValid() ? _handle.Result as IReadOnlyList<T> : null;
        public bool IsValid => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded;
        public bool IsDone => _handle.IsDone;

        public AddressablesAssetsHandle(AsyncOperationHandle<IList<T>> handle)
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