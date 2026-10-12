using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Input;
using Framework.Abstractions.Loading;
using Framework.Adapters.Input;
using UnityEngine.InputSystem;

namespace Game.Inputs
{
    /// <summary>
    /// 把 Input System 绑定资源接入输入后端（在启动加载管线里执行）。
    /// <para>
    /// 之所以不在 <see cref="GameInputModule"/> 里直接加载：模块注册发生在很早期，Addressables 还没准备好。
    /// 放在加载步骤里 = 复用框架既有的「异步加载 + 进度 + 超时兜底」能力。
    /// </para>
    /// <para>句柄所有权交给后端，由后端在 <c>Dispose</c> 时释放（避免 Addressables 句柄泄漏）。</para>
    /// </summary>
    public sealed class AttachInputActionsStep : ILoadStep
    {
        /// <summary>Addressables 地址（该资产需标记为 Addressable）。</summary>
        public const string Address = "Input/GameInputActions";

        private readonly int _timeoutFrames;

        public string Name => "接入输入绑定";

        public AttachInputActionsStep(int timeoutFrames = 600)
        {
            _timeoutFrames = timeoutFrames > 0 ? timeoutFrames : 600;
        }

        public IEnumerator Run(ILoadContext context)
        {
            var backend = TryResolve<IInputBackend>(context);
            if (!(backend is InputSystemBackend system))
            {
                context.Report(Name, 1f);
                yield break;   // 未使用 Input System 后端（空后端/自定义后端）→ 无事可做
            }

            if (system.HasAsset)
            {
                context.Report(Name, 1f);
                yield break;
            }

            var assets = TryResolve<IAssetService>(context);
            if (assets == null)
            {
                UnityEngine.Debug.LogWarning($"[Load] {Name}：无法解析 IAssetService，跳过");
                context.Report(Name, 1f);
                yield break;
            }

            IAssetHandle<InputActionAsset> handle = null;
            bool completed = false;
            assets.LoadAsync<InputActionAsset>(Address, h => { handle = h; completed = true; });

            int waited = 0;
            while (!completed && waited++ < _timeoutFrames) yield return null;

            if (handle == null)
            {
                UnityEngine.Debug.LogWarning($"[Load] {Name}：加载 {Address} 超时，输入暂不可用");
                context.Report(Name, 1f);
                yield break;
            }

            waited = 0;
            while (!handle.IsDone && waited++ < _timeoutFrames) yield return null;

            if (handle.IsValid)
            {
                system.Attach(handle.Asset, handle);
                UnityEngine.Debug.Log($"[Load] {Name}：已接入 {Address}");
            }
            else
            {
                handle.Dispose();
                UnityEngine.Debug.LogWarning($"[Load] {Name}：加载 {Address} 失败（句柄无效）");
            }

            context.Report(Name, 1f);
        }

        private static T TryResolve<T>(ILoadContext context) where T : class
        {
            try { return context.Services.Resolve<T>(); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// 自动发现（无需改引导代码）：把 <see cref="AttachInputActionsStep"/> 加进启动加载管线。
    /// </summary>
    public sealed class GameInputAssetStepProvider : IStartupLoadStepProvider
    {
        public void Collect(IList<ILoadStep> steps, ILoadContext context)
        {
            if (steps == null) return;
            steps.Add(new AttachInputActionsStep());
        }
    }
}
