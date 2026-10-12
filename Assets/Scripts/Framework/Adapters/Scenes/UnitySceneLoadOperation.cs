// Framework/Adapters/Unity/UnitySceneService.cs
using System;
using System.Collections;
using Framework.Abstractions.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using Scene = UnityEngine.SceneManagement.Scene;

namespace Framework.Adapters
{
    internal sealed class UnitySceneLoadOperation : ISceneLoadOperation
    {
        private readonly AsyncOperation _op;

        public UnitySceneLoadOperation(AsyncOperation op)
        {
            _op = op;
            _op.allowSceneActivation = false;   // 关键：先准备、后激活
        }

        public float Progress   => _op.progress;

        // 正常就绪：progress 到 0.9；兼底：已完成（防某些环境下 progress 不更新的情况）
        public bool IsPrepared  => _op.progress >= 0.9f || _op.isDone;
        public bool IsDone      => _op.isDone;
        public void Activate()  => _op.allowSceneActivation = true;
    }
}