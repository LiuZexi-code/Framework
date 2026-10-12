using Framework.Abstractions.Input;
using Framework.Abstractions.Modules;
using Framework.Core;

namespace Framework.Adapters.Input
{
    /// <summary>
    /// 框架内置输入模块：注册默认的输入服务与**空后端**，保证 <c>IInputService</c> 永远可解析。
    /// <para>
    /// 业务接入真实输入：再写一个 <see cref="IGlobalServiceModule"/>，<c>Order</c> 取大于 10 的值，
    /// 在其中 <c>registry.Register&lt;IInputBackend&gt;(new InputSystemBackend(...))</c> 覆盖默认后端即可
    /// （后注册者覆盖先注册者）。
    /// </para>
    /// </summary>
    public sealed class InputServiceModule : IGlobalServiceModule
    {
        public int Order => 10;

        public void RegisterGlobal(IServiceRegistry registry)
        {
            registry.Register<IInputBackend, NullInputBackend>();
            registry.Register<IInputService, InputService>();
        }
    }
}
