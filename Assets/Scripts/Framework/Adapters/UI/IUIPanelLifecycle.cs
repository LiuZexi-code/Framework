
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;

namespace Framework.Adapters.UI
{
    /// <summary>
    /// 面板生命周期契约。面板脚本需实现本接口才会被 UIService 托管
    /// （推荐直接继承 UIPanelBase）。必须为 public，业务程序集才能实现。
    /// </summary>
    public interface IUIPanelLifecycle
    {
        void Create(IEventBus eventBus, ILogger logger);   // 首次实例化后
        void Show();                                        // 显示
        void Hide();                                        // 隐藏
        void Destroy();                                     // 销毁前
    }
}
