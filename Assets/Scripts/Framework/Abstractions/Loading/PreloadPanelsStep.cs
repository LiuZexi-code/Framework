using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.UI;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 按面板配置的加载策略做场景预加载：<c>loadPolicy == Preload</c>、且 <c>sceneTags</c> 匹配当前场景的
    /// 非全局面板会被提前实例化到内存（隐藏），进入场景时即可同步打开。
    /// <para>这才是 <c>PanelDefinition.loadPolicy</c> / <c>SceneTags</c> 的真正落地处。</para>
    /// </summary>
    public sealed class PreloadPanelsStep : ILoadStep
    {
        public string Name => "预加载面板";

        public IEnumerator Run(ILoadContext context)
        {
            IUIService ui = null;
            try { ui = context.Services.Resolve<IUIService>(); }
            catch (Exception) { /* 没接 UI 服务时跳过 */ }

            if (ui == null) yield break;

            var definitions = ui.GetDefinitions();
            if (definitions == null || definitions.Count == 0) yield break;

            var targets = new List<PanelDefinition>();
            foreach (var definition in definitions)
            {
                if (definition?.PanelType == null) continue;
                if (definition.IsGlobal) continue;                              // 常驻面板不按场景预加载
                if (definition.loadPolicy != LoadPolicy.Preload) continue;      // 只处理「预加载」策略
                if (!MatchesScene(definition, context.SceneName)) continue;
                targets.Add(definition);
            }

            if (targets.Count == 0)
            {
                context.Report(Name, 1f);
                yield break;
            }

            UnityEngine.Debug.Log($"[Load] {Name}：{targets.Count} 个（{string.Join(", ", targets.ConvertAll(d => d.PanelType.Name))}）");

            for (int i = 0; i < targets.Count; i++)
            {
                Task task = null;
                try { task = ui.PreloadAsync(targets[i].PanelType); }
                catch (Exception e) { UnityEngine.Debug.LogError($"[Load] 预加载面板失败：{targets[i].PanelType.Name}（{e.Message}）"); }

                if (task != null)
                {
                    while (!task.IsCompleted)
                    {
                        context.Report(Name, (float)i / targets.Count);
                        yield return null;
                    }
                }

                context.Report(Name, (i + 1f) / targets.Count);
            }
        }

        /// <summary>场景标签为空 = 通用（所有场景都预加载）。</summary>
        private static bool MatchesScene(PanelDefinition definition, string sceneName)
        {
            var tags = definition.SceneTags;
            if (tags == null || tags.Count == 0) return true;
            if (string.IsNullOrEmpty(sceneName)) return false;
            return tags.Contains(sceneName);
        }
    }
}
