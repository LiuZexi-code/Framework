using UnityEngine;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 框架启动入口：任何场景运行游戏时（首个场景加载前）自动创建常驻根节点并开始装配。
    /// <para>
    /// 业务**不需要**再写自己的入口脚本；要定制引导流程，请继承 <see cref="BootstrapRunner"/>
    /// （会被自动发现，见其注释）。
    /// </para>
    /// </summary>
    public static class FrameworkStartEntry
    {
        private const string RootName = "[GameFramework]";

        private static bool _initialized;

        /// <summary>
        /// 每次进入 Play 时复位静态状态。
        /// <para>
        /// 关闭「Enter Play Mode Options」的**域重载**后，静态字段会跨 Play 残留，
        /// 而 <c>[GameFramework]</c> 根节点会在退出 Play 时被销毁 —— 两者叠加会让第二次 Play
        /// **直接跳过装配**（无根节点、无服务，且没有任何报错）。在 <c>SubsystemRegistration</c>
        /// 阶段（早于下述 <c>BeforeSceneLoad</c>）显式复位，使「开/关域重载」两种模式行为一致。
        /// </para>
        /// <para>
        /// 约定：框架层新增**跨 Play 存活**的静态字段时，要么放在这里复位，
        /// 要么由持有者在自身入口（如 <see cref="GameRoot.Dispose"/>）清理。
        /// 纯缓存（如类型表）无需处理 —— 关闭域重载时程序集/类型都未变，缓存仍然有效。
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // 双保险：即使静态标志因故残留，只要根节点不存在就必须重新装配
            if (_initialized && GameObject.Find(RootName) != null) return;
            _initialized = true;

            Debug.Log("框架启动中...");

            var root = new GameObject(RootName);
            Object.DontDestroyOnLoad(root);
            root.AddComponent<GameRoot>();
        }
    }
}
