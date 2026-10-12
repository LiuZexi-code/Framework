using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.Abstractions.Modules;
using Framework.Core;
using UnityEngine;

namespace Framework.Bootstrap.Modules
{
    /// <summary>
    /// 服务模块注册器：反射发现 IGlobalServiceModule / ISceneServiceModule，
    /// 按 Order 升序执行注册。新增服务只需实现模块接口（并位于被扫描的程序集），
    /// 无需改动任何启动代码。
    /// </summary>
    /// <remarks>
    /// 约束：模块类型需具备公开无参构造函数（用 Order 决定顺序，后注册者覆盖先注册者）。
    /// 日志：可能在任何服务注册之前被调用，因此这里直接使用 UnityEngine.Debug。
    /// </remarks>
    public static class ServiceModuleRegistry
    {
        /// <summary>默认只扫描这些名字前缀的程序集，避免扫到编辑器/第三方程序集。</summary>
        private static readonly string[] DefaultAssemblyPrefixes =
        {
            "Abstractions", "Adapters", "Bootstrap", "Game", "Assembly-CSharp"
        };

        private static readonly List<Type> _globalTypes = new();
        private static readonly List<Type> _sceneTypes = new();
        private static readonly Dictionary<string, List<Type>> _typeCache = new();
        private static bool _scanned;

        /// <summary>扫描程序集并缓存模块类型。幂等（重复调用只扫一次）。</summary>
        public static void Scan(string[] assemblyNamePrefixes = null)
        {
            if (_scanned) return;
            _scanned = true;

            foreach (var type in EnumerateTypes(assemblyNamePrefixes))
            {
                try
                {
                    if (typeof(IGlobalServiceModule).IsAssignableFrom(type) && !_globalTypes.Contains(type))
                        _globalTypes.Add(type);

                    if (typeof(ISceneServiceModule).IsAssignableFrom(type) && !_sceneTypes.Contains(type))
                        _sceneTypes.Add(type);
                }
                catch (Exception) { /* 忽略个别类型的问题，避免中断扫描 */ }
            }

            SortByOrder(_globalTypes);
            SortByOrder(_sceneTypes);

            if (_globalTypes.Count > 0 || _sceneTypes.Count > 0)
                Debug.Log($"[Modules] 扫描完成：全局模块 {_globalTypes.Count} 个，场景模块 {_sceneTypes.Count} 个");
        }

        /// <summary>
        /// 枚举被扫描程序集中的具体类型（模块 / 配置工厂 / 加载步骤提供者共用）。
        /// 结果按「前缀组合」缓存，避免每次切场景都重新遍历所有程序集；
        /// 首次调用发生在启动早期（所有程序集已加载），因此缓存是安全的。
        /// </summary>
        internal static List<Type> EnumerateTypes(string[] assemblyNamePrefixes = null)
        {
            var prefixes = assemblyNamePrefixes ?? DefaultAssemblyPrefixes;
            string cacheKey = string.Join("|", prefixes);

            if (_typeCache.TryGetValue(cacheKey, out var cached)) return cached;

            var result = new List<Type>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (string.IsNullOrEmpty(asmName)) continue;

                // 跳过编辑器程序集（-Editor）与非框架/业务程序集
                if (asmName.IndexOf("-Editor", StringComparison.Ordinal) >= 0) continue;
                if (!MatchPrefix(asmName, prefixes)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    result.Add(type);
                }
            }

            _typeCache[cacheKey] = result;
            return result;
        }

        /// <summary>按 Order 升序注册所有全局服务模块。</summary>
        public static void RegisterGlobalModules(IServiceRegistry registry)
        {
            if (registry == null) return;
            EnsureScanned();

            foreach (var type in _globalTypes)
            {
                try
                {
                    var module = (IGlobalServiceModule)Create(type);
                    module.RegisterGlobal(registry);
                    Debug.Log($"[Modules] 已注册全局模块 {type.Name}（Order={module.Order}）");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Modules] 全局模块注册失败 {type.Name}：{e}");
                }
            }
        }

        /// <summary>按 Order 升序把场景服务模块注册进指定场景容器。</summary>
        public static void RegisterSceneModules(IServiceRegistry registry, string sceneName)
        {
            if (registry == null) return;
            EnsureScanned();

            foreach (var type in _sceneTypes)
            {
                try
                {
                    var module = (ISceneServiceModule)Create(type);
                    module.RegisterScene(registry, sceneName);
                    Debug.Log($"[Modules] 已注册场景模块 {type.Name}（scene={sceneName}）");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Modules] 场景模块注册失败 {type.Name}：{e}");
                }
            }
        }

        private static object Create(Type type)
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                throw new InvalidOperationException($"模块 {type.Name} 需要公开无参构造函数");
            return ctor.Invoke(null);
        }

        private static void SortByOrder(List<Type> types)
            => types.Sort((a, b) => GetOrder(a).CompareTo(GetOrder(b)));

        private static int GetOrder(Type type)
        {
            try
            {
                var ctor = type.GetConstructor(Type.EmptyTypes);
                return ctor == null ? 0 : ((IServiceModule)ctor.Invoke(null)).Order;
            }
            catch (Exception) { return 0; }
        }

        private static bool MatchPrefix(string name, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!string.IsNullOrEmpty(prefixes[i])
                    && name.StartsWith(prefixes[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void EnsureScanned()
        {
            if (!_scanned) Scan();
        }
    }
}
