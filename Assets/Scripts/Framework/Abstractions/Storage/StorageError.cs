namespace Framework.Abstractions.Storage
{
    public enum StorageError
{
    None,
    NotFound,
    TypeMismatch,
    SerializeFailed,
    DeserializeFailed,
    IOError,
    Unknown
}
}