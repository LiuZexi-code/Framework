using System;

namespace Framework.Core
{
    public sealed class ServiceDescriptor
    {
        public object Instance;
        public Type ImplementationType;
        public Func<IServiceResolver,object> Factory;
        public ServiceLifetime Lifetime;


        public static ServiceDescriptor FromInstance(object instance)
            => new(){ Instance = instance};

        public static ServiceDescriptor FromType(Type type,ServiceLifetime lifetime)
            => new(){ ImplementationType = type , Lifetime = lifetime };
        
        public static ServiceDescriptor FromFactory(Func<IServiceResolver,object> factory,ServiceLifetime lifetime)
            => new(){ Factory = factory , Lifetime = lifetime };
    }
}
