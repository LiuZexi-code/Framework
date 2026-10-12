// Framework/Adapters/Unity/UnitySceneService.cs
using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using Scene = UnityEngine.SceneManagement.Scene;

namespace Framework.Adapters
{
    public sealed class UnitySceneService : ISceneService
    {
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

        private bool _disposed;

        public UnitySceneService()
        {
            SceneManager.sceneLoaded   += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        public ISceneLoadOperation LoadAsync(string sceneName, SceneLoadMode mode)
        {
            var unityMode = mode == SceneLoadMode.Additive
                ? LoadSceneMode.Additive
                : LoadSceneMode.Single;

            var op = SceneManager.LoadSceneAsync(sceneName, unityMode);
            if (op == null)
                throw new InvalidOperationException(
                    $"场景加载失败：{sceneName}（是否已加入 Build Settings？）");

            return new UnitySceneLoadOperation(op);
        }

        public IEnumerator UnloadAsync(string sceneName)
        {
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
        {
            SceneLoaded?.Invoke(new SceneLoadedEvent(
                scene.name,
                mode == LoadSceneMode.Additive ? SceneLoadMode.Additive : SceneLoadMode.Single));
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            SceneUnloaded?.Invoke(new SceneUnloadedEvent(scene.name));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneManager.sceneLoaded   -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }
    }
}