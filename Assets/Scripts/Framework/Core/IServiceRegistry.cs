using System;
using System.Collections;
using System.Collections.Generic;

namespace Framework.Core
{
    public interface IServiceRegistry
    {
        void Register<TService>(TService instance) where TService : class;
        void Register<TService,TImpl>(ServiceLifetime lifeTime = ServiceLifetime.Singleton) where TService : class where TImpl : class,TService;
        void RegisterFactory<TService>(Func<IServiceResolver,TService> factory,ServiceLifetime lifeTime = ServiceLifetime.Singleton) where TService : class;
    }
}
