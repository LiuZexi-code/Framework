using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace Framework.Abstractions.UI
{
    public interface IUIService
    {
        
        void Register(PanelDefinition definition);
        void Unregister(Type panelType);
        // 同步：仅用于已缓存面板
        T Open<T>() where T : class, IUIPanel;
        void Close<T>() where T : class, IUIPanel;
        // 异步：按需加载
        Task<T> OpenAsync<T>() where T : class, IUIPanel;
        Task PreloadAsync<T>() where T : class, IUIPanel;

        // 非泛型版：供「按运行时类型驱动」的代码使用（如弹窗栈 IPopupService）
        IUIPanel Open(Type panelType);
        Task<IUIPanel> OpenAsync(Type panelType);
        void Close(Type panelType);
        bool IsOpen(Type panelType);
        bool IsCached(Type panelType);

        /// <summary>按面板类型预加载（非泛型版，供按策略/配置驱动的代码使用）。</summary>
        Task PreloadAsync(Type panelType);

        /// <summary>全部已注册的面板定义（供场景预加载等按策略筛选）。</summary>
        IReadOnlyList<PanelDefinition> GetDefinitions();
        // 查询
        bool IsOpen<T>() where T : class, IUIPanel;
        bool IsCached<T>() where T : class, IUIPanel;
        void CloseAll();
        T Get<T>() where T : class, IUIPanel;

        // 场景容器销毁时清理「不属于新场景」的面板
        /// <summary>
        /// 场景退出时清理面板实例。
        /// <para>
        /// <paramref name="enteringScene"/> 是**即将进入**的场景名，**必须传**：
        /// 判据是「不属于新场景的实例才销毁」（为进入场景预加载的面板要保留）。
        /// 不要用 <c>ISceneService.ActiveSceneName</c> —— 切换过程中它还是旧场景。
        /// </para>
        /// </summary>
        void DisposeScenePanels(string enteringScene);
    }
}