
using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.Abstractions.UI;
using UnityEngine;

namespace Framework.UI
{
    /// <summary>
    /// 用于获取被特性标记的panel的类型
    /// </summary>
    public static class PanelTypeRegistry
    {
        private static readonly Dictionary<string, Type> _map = new();
        private static bool _scanned;

        public static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    // 某些程序集（编辑器/动态程序集）可能部分类型加载失败，取能加载的部分
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null) continue;

                    try
                    {
                        if (type.IsAbstract || !typeof(IUIPanel).IsAssignableFrom(type)) continue;

                        var attr = type.GetCustomAttribute<UIPanelAttribute>();
                        if (attr == null) continue;

                        if (_map.ContainsKey(attr.Key))
                        {
                            Debug.LogError($"[PanelTypeRegistry] Panel Key 重复: {attr.Key}（{_map[attr.Key].Name} / {type.Name}）");
                            continue;
                        }

                        _map[attr.Key] = type;
                    }
                    catch (Exception)
                    {
                        // 忽略个别类型的问题，避免因单个程序集导致启动中断
                    }
                }
            }
        }

        public static Type Get(string key)
            => !string.IsNullOrEmpty(key) && _map.TryGetValue(key, out var t) ? t : null;
    }
}
