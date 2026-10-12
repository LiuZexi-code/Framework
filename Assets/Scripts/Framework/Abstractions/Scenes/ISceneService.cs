// Framework/Core/Scenes/ISceneService.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.Abstractions.Scenes
{
    public enum SceneLoadMode { Single, Additive }

    public interface ISceneService : IDisposable
    {
        string ActiveSceneName { get; }

        /// <summary>
        /// 当前已加载的场景名（按 SceneManager 顺序）。
        /// <para>用于诊断「多场景共存」：Additive 切换期间会短暂有多个场景，Single 加载则只会有一个。</para>
        /// </summary>
        IReadOnlyList<string> LoadedSceneNames { get; }

        /// <summary>异步加载场景（默认不激活，需要显式 Activate）。</summary>
        ISceneLoadOperation LoadAsync(string sceneName, SceneLoadMode mode);

        /// <summary>Additive 模式下卸载场景；Single 模式由引擎自动卸载。</summary>
        IEnumerator UnloadAsync(string sceneName);

        bool SetActiveScene(string sceneName);

        /// <summary>获取场景的所有根物体（场景无效/未加载时返回空数组）。</summary>
        GameObject[] GetRootGameObjects(string sceneName);

        /// <summary>场景是否已加载（切换前判断，避免重复 Additive 加载导致对象/相机重复）。</summary>
        bool IsSceneLoaded(string sceneName);

        event Action<SceneLoadedEvent>   SceneLoaded;
        event Action<SceneUnloadedEvent> SceneUnloaded;
    }
}