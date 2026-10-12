// Framework/Core/Scenes/ISceneService.cs
using System;
using System.Collections;

namespace Framework.Abstractions.Scenes
{
    public readonly struct SceneUnloadedEvent
    {
        public readonly string SceneName;
        public SceneUnloadedEvent(string name) { SceneName = name; }
    }
}