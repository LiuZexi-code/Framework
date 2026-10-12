namespace Framework.Abstractions.Storage
{
    /// <summary>
    /// 通用键值存储。具体实现可以是文件、PlayerPrefs、内存等。
    /// </summary>
    public interface IStorage
    {
        bool Contains(string key);
        StorageResult<T> Load<T>(string key);
        bool Save<T>(string key, T value);
        bool Delete(string key);
        void Clear(string prefix = null);
    }
}