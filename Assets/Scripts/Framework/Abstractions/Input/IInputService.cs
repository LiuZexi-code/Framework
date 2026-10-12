using System;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.Abstractions.Input
{
    /// <summary>
    /// 框架输入基础设施服务：只做**生命周期 / 上下文 / 查询 / 后端适配**，
    /// 不包含任何具体操作语义（不出现 Jump / Attack / Build 等）。
    /// <para>
    /// 查询以**轮询为主**（<see cref="IsPressed"/> / <see cref="GetAxis"/> / <see cref="GetVector2"/>），
    /// 事件为辅（<see cref="ActionTriggered"/>）。
    /// </para>
    /// <para>
    /// 本接口**不继承** tick 契约：Input System 默认在引擎逐帧更新中自行刷新，
    /// 且其更新插在逻辑 Update 之前，因此业务读取天然安全。
    /// </para>
    /// </summary>
    public interface IInputService : IDisposable
    {
        // ---------- 上下文 ----------

        /// <summary>推入上下文（同名可重复推入，需等次数 <see cref="PopContext"/> 才真正移除）。</summary>
        void PushContext(string contextId, int priority = 0);

        /// <summary>弹出上下文；未推入时记警告并忽略。</summary>
        void PopContext(string contextId);

        /// <summary>该上下文当前是否处于「有效」（即优先级最高的那一批）。</summary>
        bool IsContextActive(string contextId);

        /// <summary>当前有效上下文（优先级最高的那一批）。</summary>
        IReadOnlyList<InputContext> ActiveContexts { get; }

        // ---------- 查询（轮询为主） ----------

        bool IsPressed(string actionId, int playerId = 0);
        bool WasPressedThisFrame(string actionId, int playerId = 0);
        bool WasReleasedThisFrame(string actionId, int playerId = 0);

        float GetAxis(string actionId, int playerId = 0);
        Vector2 GetVector2(string actionId, int playerId = 0);

        // ---------- 事件（为辅） ----------

        event Action<InputActionEvent> ActionTriggered;
    }
}
