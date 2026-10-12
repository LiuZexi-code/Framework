namespace Framework.Abstractions.Assets
{
    /// <summary>
    /// 资源引用抽象。项目层用 Addressables 的 AssetReference 实现，
    /// 避免业务代码直接引用 Addressables 类型。
    /// </summary>
    public interface IAssetReference
    {
        string AssetGuid { get; }
    }
}