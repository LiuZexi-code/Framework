using System;
using System.Collections.Generic;
using Framework.Abstractions.GameConfig;
using Framework.Bootstrap.Modules;
using UnityEngine;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 配置注册器：反射发现所有 <see cref="IConfigFactory"/> 实现并创建配置实例。
    /// <para>
    /// 新增配置只需实现一个 <see cref="IConfigFactory"/>（并位于被扫描的程序集），
    /// 启动流程会自动「预热地址 → 注册 → 加载」，<b>无需改动任何启动代码</b>。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 约束：工厂类型需具备公开无参构造函数（与 <see cref="ServiceModuleRegistry"/> 一致）。
    /// 顺序：按 <see cref="IGameConfig.LoadOrder"/> 升序；相同则按扫描顺序。
    /// </remarks>
    public static class GameConfigRegistry
    {
        /// <summary>发现所有配置实例（按 LoadOrder 升序）。单个工厂失败会被跳过并记录，不影响其他配置。</summary>
        public static List<IGameConfig> Discover(string[] assemblyNamePrefixes = null)
        {
            var result = new List<IGameConfig>();

            foreach (var type in ServiceModuleRegistry.EnumerateTypes(assemblyNamePrefixes))
            {
                if (!typeof(IConfigFactory).IsAssignableFrom(type)) continue;

                try
                {
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        Debug.LogError($"[Config] {type.Name} 需要公开无参构造函数，已跳过");
                        continue;
                    }

                    var factory = (IConfigFactory)ctor.Invoke(null);
                    var config = factory.CreateConfig();
                    if (config == null)
                    {
                        Debug.LogWarning($"[Config] {type.Name}.CreateConfig() 返回 null，已跳过");
                        continue;
                    }

                    result.Add(config);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Config] {type.Name} 创建配置失败：{e}");
                }
            }

            result.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));

            if (result.Count > 0)
                Debug.Log($"[Config] 发现配置 {result.Count} 个：" +
                          string.Join(", ", result.ConvertAll(c => $"{c.ConfigId}(order={c.LoadOrder})")));

            return result;
        }
    }
}
