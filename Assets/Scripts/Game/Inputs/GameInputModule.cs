using Framework.Abstractions.Input;
using Framework.Abstractions.Modules;
using Framework.Adapters.Input;
using Framework.Core;

namespace Game.Inputs
{
    /// <summary>
    /// 项目层输入模块：把默认的空后端覆盖为 **Input System 后端**。
    /// <para>
    /// <see cref="Order"/>（100）大于框架的 <c>InputServiceModule</c>（10），
    /// 因此这里注册的 <c>IInputBackend</c> 生效（后注册者覆盖先注册者）。
    /// <c>IInputService</c> 的注册保持不变——容器会用被覆盖后的后端来构造它。
    /// </para>
    /// <para>
    /// 绑定资源不在这里加载（模块注册发生得很早，资源还没载入），
    /// 而由 <see cref="GameInputAssetStepProvider"/> 在启动加载管线里载入后 <c>Attach</c>。
    /// </para>
    /// </summary>
    public sealed class GameInputModule : IGlobalServiceModule
    {
        public int Order => 100;

        public void RegisterGlobal(IServiceRegistry registry)
        {
            registry.Register<IInputBackend>(new InputSystemBackend());
        }
    }
}
