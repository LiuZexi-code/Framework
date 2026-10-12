using System;
using System.Threading.Tasks;

namespace Framework.Abstractions.UI
{
    /// <summary>面板实例化工厂</summary>
    public interface IPanelFactory
    {
        IUIPanel Create(PanelDefinition definition); //同步
        Task<IUIPanel> CreateAsync(PanelDefinition definition);  // 异步
        void Destroy(IUIPanel panel);
    }
}