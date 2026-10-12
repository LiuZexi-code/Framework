using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Logging;
using Framework.Core;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 顺序执行一组 <see cref="ILoadStep"/>，把每个步骤的局部进度换算成整体进度并上报。
    /// 单个步骤失败只记录日志、不会中断整条管线。
    /// </summary>
    public static class LoadPipeline
    {
        /// <param name="steps">要执行的步骤（按顺序）。</param>
        /// <param name="context">加载上下文。</param>
        /// <param name="onProgress">(阶段名, 整体进度 0..1)。</param>
        public static IEnumerator Run(IList<ILoadStep> steps, ILoadContext context, Action<string, float> onProgress = null)
        {
            if (steps == null || steps.Count == 0)
            {
                onProgress?.Invoke(null, 1f);
                yield break;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var scope = new StepContext(context);

                // 步骤开始就先上报该阶段（便于「开始做 X」的日志/进度文案出现在正确时机）
                onProgress?.Invoke(step.Name, (float)i / steps.Count);

                IEnumerator routine = null;
                try { routine = step.Run(scope); }
                catch (Exception e) { ReportError(context, step, e); }

                if (routine != null)
                {
                    while (true)
                    {
                        bool moved;
                        try { moved = routine.MoveNext(); }
                        catch (Exception e) { ReportError(context, step, e); break; }

                        if (!moved) break;

                        onProgress?.Invoke(step.Name, (i + scope.LocalProgress) / steps.Count);

                        // 保留步骤自身的 yield 语义（null = 等一帧；IEnumerator = 交给运行器展开）
                        yield return routine.Current;
                    }
                }

                onProgress?.Invoke(step.Name, (i + 1f) / steps.Count);
            }
        }

        private static void ReportError(ILoadContext context, ILoadStep step, Exception e)
        {
            string message = $"[Load] 步骤「{step?.Name}」执行失败：{e}";

            IServiceResolver services = context?.Services;
            if (services != null && services.TryResolve<ILogger>(out var logger) && logger != null)
            {
                logger.Error(message);
                return;
            }

            UnityEngine.Debug.LogError(message);
        }

        /// <summary>包一层以便捕获「当前步骤」自己上报的局部进度。</summary>
        private sealed class StepContext : ILoadContext
        {
            private readonly ILoadContext _inner;

            public float LocalProgress { get; private set; }

            public StepContext(ILoadContext inner) => _inner = inner;

            public IServiceResolver Services => _inner.Services;
            public string SceneName => _inner.SceneName;

            public void Report(string stage, float localProgress)
            {
                LocalProgress = localProgress < 0f ? 0f : (localProgress > 1f ? 1f : localProgress);
                _inner.Report(stage, LocalProgress);
            }
        }
    }
}
