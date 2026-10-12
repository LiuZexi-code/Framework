using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Logging;

namespace Framework.Adapters.Scenes
{
    /// <summary>
    /// <see cref="ISceneCatalog"/> 的默认实现：进程内字典 + 登记顺序列表。
    /// <para>
    /// 由 <c>CoreServiceModule</c> 注册为单例；业务在启动步骤里按配置调用 <see cref="Register"/> 填充。
    /// 与 <c>UISettings</c> 一致，采用「注册可变实例 + 业务读取后赋值」的模式。
    /// </para>
    /// </summary>
    public sealed class SceneCatalog : ISceneCatalog
    {
        private readonly Dictionary<string, SceneDefinition> _byName = new();
        private readonly List<SceneDefinition> _ordered = new();

        public string EntrySceneName { get; set; }
        public string LoadingSceneName { get; set; }
        public SceneTransitionPolicy TransitionPolicy { get; set; } = SceneTransitionPolicy.SingleDirect;
        public bool Strict { get; set; }

        public IReadOnlyList<SceneDefinition> All => _ordered;

        public void Register(SceneDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Name)) return;

            if (_byName.TryGetValue(definition.Name, out var existing))
            {
                // 同名覆盖：保留原位置，替换定义（便于业务用更大 Order 的配置覆盖框架默认）
                var index = _ordered.IndexOf(existing);
                if (index >= 0) _ordered[index] = definition;
                _byName[definition.Name] = definition;
                Log.Warn($"[Scenes] 场景定义被覆盖（同名重复登记）：{definition.Name}");
                return;
            }

            _byName.Add(definition.Name, definition);
            _ordered.Add(definition);
        }

        public bool TryGet(string sceneName, out SceneDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(sceneName)) return false;
            return _byName.TryGetValue(sceneName, out definition);
        }

        public SceneDefinition Get(string sceneName)
            => TryGet(sceneName, out var definition) ? definition : null;
    }
}
