namespace Framework.Abstractions.Input
{
    /// <summary>
    /// 一个输入上下文（例如 Gameplay / UI / Cutscene / Vehicle）。
    /// <para>
    /// 框架**不定义任何具体上下文名**：<see cref="ContextId"/> 的取值与 <see cref="Priority"/> 的语义
    /// 完全由项目层决定。后端把 <c>ContextId</c> 映射到自己的「输入分组」（例如 Input System 的 ActionMap）。
    /// </para>
    /// </summary>
    public readonly struct InputContext
    {
        /// <summary>上下文标识（框架不解释其含义，只做传递与优先级比较）。</summary>
        public string ContextId { get; }

        /// <summary>优先级：数值越大越优先。有效上下文 = 栈中优先级最高的那一批。</summary>
        public int Priority { get; }

        public InputContext(string contextId, int priority = 0)
        {
            ContextId = contextId;
            Priority = priority;
        }

        public override string ToString() => $"{ContextId}({Priority})";
    }
}
