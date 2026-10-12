using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Core;

namespace Framework.Bootstrap
{
    public sealed class EmptySceneFlowHooks : ISceneFlowHooks
    {       
        public IEnumerator RegisterSceneServices(ServiceCollection c, string n) { yield return null; }
        public IEnumerator SceneResourcePreload(IServiceResolver r, string n, Action<float> reportProgress)
        {
            reportProgress?.Invoke(1f);
            yield return null;
        }
        public void SceneResourceRelease(IServiceResolver r, string n) { }

        // ↓ Loading 相关
        public IEnumerator OnLoadingBegin(IServiceResolver resolver, string targetScene){yield return null;}

        public IEnumerator OnLoadingEnd(IServiceResolver resolver, string targetScene){yield return null;}

        public void OnLoadingProgress(IServiceResolver resolver, string targetScene, float progress) { }
    }
}