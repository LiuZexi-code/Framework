using Framework.Abstractions.Modules;
using Framework.Core;
using Game.RPG.Interaction;

namespace Game.RPG.Modules
{
    /// <summary>
    /// RPG 的**场景级**服务模块：交互服务（每个场景一份，随场景容器释放）。
    /// <para>
    /// 放在 <c>Game</c> 程序集 + 公开无参构造 → 被 <c>ServiceModuleRegistry</c> 自动发现，
    /// **不需要改框架或启动代码**。
    /// </para>
    /// </summary>
    public sealed class RpgSceneModule : ISceneServiceModule
    {
        public int Order => 100;

        public void RegisterScene(IServiceRegistry registry, string sceneName)
        {
            // 按类型注册 → 容器自动注入 ILogger
            registry.Register<IInteractionService, InteractionService>();
        }
    }
}
