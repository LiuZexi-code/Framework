using Framework.Abstractions.Modules;
using Framework.Core;

namespace Game.Modules
{
    /// <summary>示例业务服务（全局）。</summary>
    public interface IGreetingService
    {
        string Hello();
    }

    /// <summary>示例场景级数据（每个场景各自一份）。</summary>
    public interface ISceneContext
    {
        string SceneName { get; }
    }

    internal sealed class GreetingService : IGreetingService
    {
        public string Hello() => "Hello from DemoGlobalModule";
    }

    internal sealed class SceneContext : ISceneContext
    {
        public SceneContext(string sceneName) => SceneName = sceneName;
        public string SceneName { get; }
    }

    /// <summary>
    /// 示例模块：同时演示「全局服务注册」与「场景服务注册」。
    /// 只要实现模块接口并位于被扫描的程序集（Game），启动/建场景时会自动注册，
    /// 无需改动框架或启动代码。
    /// </summary>
    public sealed class DemoGlobalModule : IGlobalServiceModule, ISceneServiceModule
    {
        public int Order => 100;   // 大于框架核心模块，必要时可用于覆盖默认实现

        public void RegisterGlobal(IServiceRegistry registry)
        {
            registry.Register<IGreetingService>(new GreetingService());
        }

        public void RegisterScene(IServiceRegistry registry, string sceneName)
        {
            registry.Register<ISceneContext>(new SceneContext(sceneName));
        }
    }
}
