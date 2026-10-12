using System;
using System.Collections;
using Framework.Core;
using UnityEngine;

namespace Framework.Bootstrap
{
    [DefaultExecutionOrder(-10000)]
    public class GameRoot : MonoBehaviour, ICoroutineRunner
    {

        private static GameRoot _instance;
        private ServiceCollection _root;
        private IBootstrapRunner _runner;
        private bool _disposed;

        public static GameRoot Instance => _instance;
        public IServiceResolver Global => _root;

        private void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
            _instance = this;
            StartCoroutine(StartupRoutine());
        }

        private IEnumerator StartupRoutine()
        {
            _root = new ServiceCollection();

            _runner = CreateRunner();
            if (_runner == null)
                throw new InvalidOperationException("CreateRunner 返回 null");

            yield return _runner.Run(_root, this);
        }

        // ICoroutineRunner：把外部协程接到 MonoBehaviour 上执行
        public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);

        /// <summary>
        /// 子类可覆写以指定引导流程；默认反射发现业务自定义的 <see cref="IBootstrapRunner"/>，
        /// 找不到就用框架通用实现 <see cref="BootstrapRunner"/>。
        /// </summary>
        protected virtual IBootstrapRunner CreateRunner() => BootstrapRunner.CreateDefault();

        private void OnDestroy()
        {
            try { _runner?.Dispose(); }
            catch (Exception e) { Debug.LogError($"[GameRoot] Runner dispose failed: {e}"); }
            Dispose();
        }

        private void OnApplicationQuit() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _root?.Dispose(); }
            catch (Exception e) { Debug.LogError($"[GameRoot] Root dispose failed: {e}"); }
            _root = null;
        }
    }
}