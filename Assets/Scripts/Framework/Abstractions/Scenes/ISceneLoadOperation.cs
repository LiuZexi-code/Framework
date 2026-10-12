namespace Framework.Abstractions.Scenes
{
    /// <summary>
    /// 一次场景加载过程：
    ///   Prepared 之前一直在加载
    ///   Prepared 之后可以插入自定义逻辑（资源预加载、过场动画……）
    ///   调用 Activate() 才真正把新场景切换到前台
    /// </summary>
    public interface ISceneLoadOperation
    {
        float Progress { get; }
        bool  IsPrepared { get; }
        bool  IsDone     { get; }
        void  Activate();
    }
}