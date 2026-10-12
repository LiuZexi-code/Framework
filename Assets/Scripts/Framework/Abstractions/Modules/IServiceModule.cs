using Framework.Core;

namespace Framework.Abstractions.Modules
{
    /// <summary>
    /// 服务模块，可实现此接口注册服务
    /// </summary>
    public interface IServiceModule
    {
        int Order { get; }
    }
    /// <summary>
    /// 实现此接口，用于注册全局服务
    /// </summary>
    public interface IGlobalServiceModule:IServiceModule
    {
        void RegisterGlobal(IServiceRegistry registry);
    }

    /// <summary>
    /// 实现此接口，用于注册场景服务
    /// </summary>
    public interface ISceneServiceModule:IServiceModule
    {
        void RegisterScene(IServiceRegistry registry,string sceneName);
    }
}