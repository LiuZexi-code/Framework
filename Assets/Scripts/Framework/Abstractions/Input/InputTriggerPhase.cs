namespace Framework.Abstractions.Input
{
    /// <summary>
    /// 输入触发的阶段（轮询为主、事件为辅中的「事件」部分）。
    /// <para>
    /// 刻意**不**直接暴露 <c>UnityEngine.InputSystem.InputActionPhase</c>，
    /// 以保证核心抽象（本程序集）不引用任何具体输入包。
    /// </para>
    /// </summary>
    public enum InputTriggerPhase
    {
        /// <summary>交互开始（按键按下、摇杆离开死区）。</summary>
        Started = 0,

        /// <summary>完整触发（按钮按下 / 轴达到阈值）。</summary>
        Performed = 1,

        /// <summary>触发结束（按钮松开 / 轴回落）。</summary>
        Canceled = 2,
    }
}
