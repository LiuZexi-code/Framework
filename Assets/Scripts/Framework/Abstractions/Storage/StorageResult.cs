using System.ComponentModel;

namespace Framework.Abstractions.Storage
{
    public readonly struct StorageResult<T>
    {
        public bool Success { get;}
        public T Value { get;}
        public StorageError Error { get;}

        public StorageResult(bool success,T value,StorageError storageError){
            Success = success;
            Value = value;
            Error = storageError;
        }

        public static StorageResult<T> Ok(T value) => new StorageResult<T>(true, value, StorageError.None);
        public static StorageResult<T> Fail(StorageError error) => new StorageResult<T>(false, default, error);
    }
}