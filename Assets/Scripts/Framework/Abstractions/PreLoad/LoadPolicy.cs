namespace Framework.Abstractions
{
    //加载策略
    public enum LoadPolicy
    {
        /// <summary>
        /// 预加载，进入关联场景时，自动异步预加载到内存
        /// </summary>
        Preload,
        /// <summary>
        /// 不预加载，等代码需要时再加载
        /// </summary>
        OnDemand,
    }
}
