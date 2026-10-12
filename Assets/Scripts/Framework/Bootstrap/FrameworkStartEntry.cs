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
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_initialized) return;
            _initialized = true;

            Debug.Log("框架启动中...");

            var root = new GameObject("[GameFramework]");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<GameRoot>();
        }
    }
}
