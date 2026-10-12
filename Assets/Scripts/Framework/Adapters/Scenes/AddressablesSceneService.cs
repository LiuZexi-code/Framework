// Framework/Adapters/Scenes/AddressablesSceneService.cs
using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Scene = UnityEngine.SceneManagement.Scene;

namespace Framework.Adapters
{
    /// <summary>
    /// 基于 Addressables 的场景服务：用**地址**加载/卸载场景（默认地址与场景名相同），
    /// 适合需要远端打包 / 热更的场景。前提：场景资产已标记为 Addressable。
    /// <para>
    /// 与 <see cref="UnitySceneService"/> 的差异：不依赖 Build Settings 分组，而依赖 Addressables 组；
    /// 若某场景不是本服务加载的（例如编辑器里初始打开的场景），卸载时会回退到 <c>SceneManager</c>。
    /// </para>
    /// <para>契约：<c>LoadAsync</c> 采用 <c>activateOnLoad:false</c> —— 先准备好（<see cref="ISceneLoadOperation.IsPrepared"/>），
    /// 再由框架调用 <c>Activate()</c>，激活完成才算 <c>IsDone</c>。</para>
    /// </summary>
    public sealed class AddressablesSceneService : ISceneService
    {
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _handles = new();
        private bool _disposed;

        public string ActiveSceneName => SceneManager.GetActiveScene().name;

        public IReadOnlyList<string> LoadedSceneNames
        {
            get
            {
                var names = new List<string>();
                int count = SceneManager.sceneCount;
                for (int i = 0; i < count; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded) names.Add(scene.name);
                }
                return names;
            }
        }

        public event Action<SceneLoadedEvent> SceneLoaded;
        public event Action<SceneUnloadedEvent> SceneUnloaded;

        public AddressablesSceneService()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        public ISceneLoadOperation LoadAsync(string sceneName, SceneLoadMode mode)
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new ArgumentException("sceneName 不能为空", nameof(sceneName));

            var unityMode = mode == SceneLoadMode.Additive ? LoadSceneMode.Additive : LoadSceneMode.Single;

            var handle = Addressables.LoadSceneAsync(sceneName, unityMode, activateOnLoad: false);
            if (!handle.IsValid())
                throw new InvalidOperationException(
                    $"场景加载失败：{sceneName}（该地址是否已标记为 Addressable？）");

            Debug.Log($"[Scene] Addressables 加载：{sceneName}（{mode}，activateOnLoad=false）");
            _handles[sceneName] = handle;
            return new AddressablesSceneLoadOperation(handle);
        }

        public IEnumerator UnloadAsync(string sceneName)
        {
            // 本服务加载的 → 用 Addressables 卸载（同时释放依赖计数）
            if (_handles.TryGetValue(sceneName, out var handle))
            {
                _handles.Remove(sceneName);

                if (handle.IsValid())
                {
                    var loadedScene = SceneManager.GetSceneByName(sceneName);

                    // Single 加载会关闭其它所有场景 → 此时场景已不在内存，但仍**必须释放句柄**，
                    // 否则 Addressables 的引用计数不降，场景及其依赖资源会一直常驻（泄漏）。
                    if (loadedScene.IsValid() && loadedScene.isLoaded)
                    {
                        var unload = Addressables.UnloadSceneAsync(handle);
                        while (unload.IsValid() && !unload.IsDone) yield return null;
                    }
                    else
                    {
                        Addressables.Release(handle);
                    }
                    yield break;
                }
            }

            // 不是本服务加载的（如编辑器初始打开的场景）→ 回退 SceneManager
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            var op = SceneManager.UnloadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
        }

        public bool SetActiveScene(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return false;
            return SceneManager.SetActiveScene(scene);
        }

        public GameObject[] GetRootGameObjects(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded
                ? scene.GetRootGameObjects()
                : Array.Empty<GameObject>();
        }

        public bool IsSceneLoaded(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
            => SceneLoaded?.Invoke(new SceneLoadedEvent(
                scene.name,
                mode == LoadSceneMode.Additive ? SceneLoadMode.Additive : SceneLoadMode.Single));

        private void HandleSceneUnloaded(Scene scene)
            => SceneUnloaded?.Invoke(new SceneUnloadedEvent(scene.name));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            _handles.Clear();
        }
    }

    /// <summary>
    /// Addressables 场景加载过程。
    /// 进度按“到 0.9 即已准备好”的约定换算（与 <see cref="UnitySceneLoadOperation"/> 保持一致，
    /// 便于 <c>SceneFlow</c> 复用同一套进度映射）。
    /// </summary>
    internal sealed class AddressablesSceneLoadOperation : ISceneLoadOperation
    {
        private readonly AsyncOperationHandle<SceneInstance> _handle;
        private bool _activationRequested;
        private bool _activated;

        public AddressablesSceneLoadOperation(AsyncOperationHandle<SceneInstance> handle) => _handle = handle;

        public float Progress => _handle.IsValid() ? _handle.PercentComplete * 0.9f : 0f;

        /// <summary>加载到「可激活」状态（<c>activateOnLoad:false</c> 时句柄完成即为可激活）。</summary>
        public bool IsPrepared => _handle.IsValid() && _handle.IsDone;

        /// <summary>调用 <see cref="Activate"/> 之前返回「是否已准备好」，之后返回「是否已激活」。</summary>
        public bool IsDone => _activationRequested ? _activated : IsPrepared;

        public void Activate()
        {
            if (_activationRequested || !_handle.IsValid() || !_handle.IsDone) return;

            _activationRequested = true;

            var op = _handle.Result.ActivateAsync();
            if (op == null)
            {
                _activated = true;
                return;
            }

            op.completed += _ => _activated = true;
        }
    }
}
