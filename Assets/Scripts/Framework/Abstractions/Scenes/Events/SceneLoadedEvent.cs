namespace Framework.Abstractions.Scenes
{
    public readonly struct SceneLoadedEvent
    {
        public readonly string SceneName;
        public readonly SceneLoadMode Mode;
        public SceneLoadedEvent(string name, SceneLoadMode mode) { SceneName = name; Mode = mode; }
    }
}