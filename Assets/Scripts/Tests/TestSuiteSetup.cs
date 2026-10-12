using Framework.Adapters.Logging;
using NUnit.Framework;

/// <summary>
/// 整个测试程序集的一次性准备。
/// <para>
/// 存在的唯一原因：<see cref="Log"/> 是**静态门面**，而 <c>BootstrapRunner</c> 在 Play 启动时会
/// <c>Log.Init(UnityLogger)</c>。如果退出 Play 没有重载域（Enter Play Mode Options / 域名残留），
/// 这个静态实例会**残留到后续的 EditMode 测试运行**里 —— 于是凡是被测代码调用 <c>Log.Error</c> 的用例
/// 都会因为「Unhandled log message」假失败（实测 4 例：<c>SceneConfigTests</c> 1 例、<c>SceneFlowTests</c> 3 例，
/// 与代码改动完全无关，白查半天）。
/// </para>
/// <para>
/// 这里在每轮测试开始前把它复位成 <see cref="NullLogger"/>，让测试结果只取决于代码本身，
/// 而不是「上一次 Play 会话留下了什么」。
/// </para>
/// <para>
/// ⚠️ 经验：**跑过 Play 之后再跑 EditMode 测试，若出现莫名失败，先怀疑静态残留**，
/// 而不是先怀疑刚改的代码。
/// </para>
/// </summary>
[SetUpFixture]
public sealed class TestSuiteSetup
{
    [OneTimeSetUp]
    public void ResetStaticLogFacade() => Log.Clear();
}
