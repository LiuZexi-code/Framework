using System;

namespace Framework.Abstractions.Events
{
    public interface IEventBus
    {
        void Publish<T>(T evt);
        IEventSubscription Subscribe<T>(Action<T> handler);
        void Clear();
    }
}