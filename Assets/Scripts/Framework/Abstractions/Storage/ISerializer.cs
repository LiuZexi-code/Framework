namespace Framework.Abstractions.Storage
{
    /// <summary>序列化抽象。</summary>
    public interface ISerializer
    {
        /// <summary>文件扩展名，例如 ".json" / ".bin"</summary>
        string FileExtension { get; }

        byte[] Serialize<T>(T value);

        bool TryDeserialize<T>(byte[] data, out T value);
    }
}