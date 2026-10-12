using System;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.Abstractions.Input
{
    /// <summary>
    /// 输入后端：隔离具体输入实现（Input System / 旧 Input Manager / 测试桩）。
    /// <para>
    /// 实现方**必须**能容忍：<see cref="Initialize"/> 时尚未提供绑定资源；
    /// 以及 <see cref="ApplyContexts"/> 传入空列表（约定为「不限制」，即放行全部输入）。
    /// </para>
    /// <para>
    /// <b>实现约束</b>：<see cref="ApplyContexts"/> 收到的列表由调用方复用，实现方**不得长期持有**该引用。
    /// </para>
    /// </summary>
    public interface IInputBackend : IDisposable
    {
        /// <summary>初始化。可重复调用（实现需幂等）。</summary>
        void Initialize();

        /// <summary>
        /// 应用当前**有效**上下文集合（已由 <c>InputService</c> 按优先级筛选）。
        /// 后端据此启用/禁用对应的输入分组。空列表 = 不限制。
        /// </summary>
        void ApplyContexts(IReadOnlyList<InputContext> activeContexts);

        /// <summary>
        /// 可选的手动驱动钩子。默认由后端自身（引擎的逐帧更新）驱动，**框架不会调用本方法**；
        /// 仅在项目改用确定性/手动输入更新时才需要自行调用。
        /// </summary>
        void Tick(float deltaTime);

        /// <summary>当前是否按住。</summary>
        bool ReadButton(string actionId, int playerId = 0);

        /// <summary>本帧是否刚按下（边缘检测）。</summary>
        bool ReadButtonDown(string actionId, int playerId = 0);

        /// <summary>本帧是否刚松开（边缘检测）。</summary>
        bool ReadButtonUp(string actionId, int playerId = 0);

        /// <summary>读取单轴值；不存在的动作返回 0。</summary>
        float ReadAxis(string actionId, int playerId = 0);

        /// <summary>读取二维向量值；不存在的动作返回 (0,0)。</summary>
        Vector2 ReadVector2(string actionId, int playerId = 0);

        /// <summary>动作触发事件（辅）。实现方需保证单个订阅者异常不影响其它订阅者。</summary>
        event Action<InputActionEvent> ActionTriggered;
    }
}
