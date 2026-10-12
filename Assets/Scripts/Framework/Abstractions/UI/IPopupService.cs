using System;
using System.Threading.Tasks;

namespace Framework.Abstractions.UI
{
    /// <summary>
    /// 弹窗栈（全局单栈 + 模态链 + 层内优先级）。设计见仓库根目录 <c>UI_POPUP_DESIGN.md</c>。
    /// <para>要点：</para>
    /// <list type="bullet">
    /// <item><description><c>Push</c> = 打开并入栈；<c>Pop</c> = 关闭栈顶并恢复显示下层。</description></item>
    /// <item><description>层内排序为 <c>(Priority, 打开序)</c>：优先级越大越靠上，同优先级「后开的在上」。见 <see cref="UILayerOrder"/>。</description></item>
    /// <item><description>标记了 <see cref="PanelDefinition.IsModal"/> 的面板，会用一个共享遮罩挡住**它下面的**面板（Toast 之类轻提示不应标模态）。</description></item>
    /// <item><description>重复 <c>Push</c> 同一个面板**不会重复入栈**，只会把它提到栈顶。</description></item>
    /// <item><description>
    /// <b>异步队列</b>：<see cref="PushAsync{T}"/> 串行执行（调用顺序 = 栈顺序）；
    /// 单项超过 <see cref="UISettings.PopupQueueTimeoutSeconds"/> 会被跳过（避免一次卡住堵死整条队列）。
    /// <b>取消</b>是隐式的：<see cref="Pop"/> / <see cref="PopAll"/> / <see cref="Clear"/> / <see cref="PopTo{T}"/> / 场景卸载
    /// 会作废所有待处理项 —— 已取消的项**不会显示、不会入栈**，其 Task 以 <c>null</c> 完成。
    /// </description></item>
    /// <item><description>⚠️ **本服务不含任何输入概念**：返回键请由业务自己绑定到 <see cref="CanPop"/> / <see cref="Pop"/>。</description></item>
    /// </list>
    /// </summary>
    public interface IPopupService
    {
        /// <summary>当前栈深度。</summary>
        int Depth { get; }

        /// <summary>是否还有可弹出的弹窗（业务路由「返回键」时最常用）。</summary>
        bool CanPop { get; }

        /// <summary>打开并入栈（要求面板已预加载/已缓存）。失败返回 null。</summary>
        IUIPanel Push<T>() where T : class, IUIPanel;

        /// <summary>
        /// 异步打开并入栈（内部**串行队列**：保证「调用顺序 = 栈顺序」）。
        /// <para>失败 / 被取消 / 超时都返回 <c>null</c>（不抛异常），方便调用方统一处理。</para>
        /// </summary>
        Task<IUIPanel> PushAsync<T>() where T : class, IUIPanel;

        /// <summary>异步打开并入栈，并覆盖注册时的层内优先级。</summary>
        Task<IUIPanel> PushAsync<T>(int priority) where T : class, IUIPanel;

        /// <summary>
        /// 还没处理完的异步 Push 数（队列中 + 正在打开的那一项）。0 表示队列已空闲。
        /// </summary>
        int PendingCount { get; }

        /// <summary>关闭栈顶并恢复显示下层；栈空时返回 false（不抛异常）。</summary>
        bool Pop();

        /// <summary>依次弹出全部弹窗。</summary>
        void PopAll();

        /// <summary>弹出直到指定面板成为栈顶（该面板保留）。成功返回 true。</summary>
        bool PopTo<T>() where T : class, IUIPanel;

        /// <summary>查看栈顶（不移除）。</summary>
        bool TryPeek(out IUIPanel panel);

        /// <summary>查看栈顶并转型；栈空或类型不符返回 null。</summary>
        T Peek<T>() where T : class, IUIPanel;

        /// <summary>直接清空栈（与 <see cref="PopAll"/> 同义，语义更强调「重置」）。</summary>
        void Clear();

        event Action<IUIPanel> Pushed;
        event Action<IUIPanel> Popped;
    }
}
