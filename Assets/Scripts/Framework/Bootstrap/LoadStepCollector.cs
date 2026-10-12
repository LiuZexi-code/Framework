using System;
using System.Collections.Generic;
using Framework.Abstractions.GameConfig;
using Framework.Abstractions.Loading;
using Framework.Bootstrap.Modules;
using UnityEngine;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 加载步骤收集器：定义**内置步骤**，并反射发现业务注册的步骤提供者
    /// （与 <c>IServiceModule</c> / <c>IConfigFactory</c> 同一约定：加类即接入）。
    /// <para>
    /// 启动阶段：`预热配置地址 → 加载配置 → 业务启动步骤`；
    /// 进入场景前：`按 loadPolicy 预加载面板 → 按场景配置预加载专属资源 → 业务场景步骤`。
    /// </para>
    /// </summary>
    public static class LoadStepCollector
    {
        /// <summary>启动阶段步骤。</summary>
        public static List<ILoadStep> CollectStartup(ILoadContext context, string[] scanPrefixes = null)
        {
            var steps = new List<ILoadStep>();

            var configs = GameConfigRegistry.Discover(scanPrefixes);
            if (configs.Count > 0)
            {
                var addresses = CollectAddresses(configs);
                if (addresses.Count > 0)
                    steps.Add(new PreloadAddressesStep("预热配置", addresses));

                steps.Add(new LoadConfigsStep(configs));
            }

            AppendProviders<IStartupLoadStepProvider>(context, steps, scanPrefixes);
            return steps;
        }

        /// <summary>进入场景前的预加载步骤。</summary>
        public static List<ILoadStep> CollectScene(ILoadContext context, string[] scanPrefixes = null)
        {
            // 内置：① 按 PanelDefinition.loadPolicy == Preload 且 sceneTags 匹配预加载面板
            //       ② 按 SceneDefinition.PreloadAddresses 预加载场景专属资源
            var steps = new List<ILoadStep> { new PreloadPanelsStep(), new PreloadSceneAddressesStep() };

            AppendProviders<ISceneLoadStepProvider>(context, steps, scanPrefixes);
            return steps;
        }

        private static List<string> CollectAddresses(List<IGameConfig> configs)
        {
            var addresses = new List<string>();
            foreach (var config in configs)
            {
                var address = config?.LoadAddress;
                if (string.IsNullOrEmpty(address)) continue;
                if (!addresses.Contains(address)) addresses.Add(address);
            }
            return addresses;
        }

        private static void AppendProviders<TProvider>(ILoadContext context, List<ILoadStep> steps, string[] scanPrefixes)
            where TProvider : class, ILoadStepProvider
        {
            if (steps == null) return;

            foreach (var type in ServiceModuleRegistry.EnumerateTypes(scanPrefixes))
            {
                if (!typeof(TProvider).IsAssignableFrom(type)) continue;

                try
                {
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        Debug.LogError($"[Load] 步骤提供者 {type.Name} 需要公开无参构造函数，已跳过");
                        continue;
                    }

                    ((TProvider)ctor.Invoke(null)).Collect(steps, context);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Load] 步骤提供者 {type.Name} 收集失败：{e}");
                }
            }
        }
    }
}
