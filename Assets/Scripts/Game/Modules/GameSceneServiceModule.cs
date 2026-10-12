using Framework.Abstractions.Modules;
using Framework.Abstractions.Scenes;
using Framework.Adapters;
using Framework.Core;

namespace Game.Modules
{
    /// <summary>
    /// 示例：把场景加载切换为 **Addressables 版本**。
    /// <para>
    /// 原理：模块 <see cref="Order"/>（0）大于框架核心模块（-1000），后注册者覆盖先注册者，
    /// 因此 <c>ISceneService</c> 会指向 <see cref="AddressablesSceneService"/>。
    /// 前提：要加载的场景资产已标记为 Addressable，且**地址与场景名一致**（本工程中 SampleScene / LoadingScene 即如此）。
    /// </para>
    /// <para>想改回基于 Build Settings 的实现，删掉本类或把 Order 调小即可。</para>
    /// </summary>
    public sealed class GameSceneServiceModule : IGlobalServiceModule
    {
        public int Order => 0;

        public void RegisterGlobal(IServiceRegistry registry)
            => registry.Register<ISceneService, AddressablesSceneService>();
    }
}
