using Framework.Core;

namespace Framework.Abstractions.Loading
{
    /// <summary><see cref="ILoadContext"/> 默认实现。</summary>
    public sealed class LoadContext : ILoadContext
    {
        public IServiceResolver Services { get; }
        public string SceneName { get; }

        public LoadContext(IServiceResolver services, string sceneName = null)
        {
            Services = services;
            SceneName = sceneName;
        }

        public void Report(string stage, float localProgress) { }
    }
}
