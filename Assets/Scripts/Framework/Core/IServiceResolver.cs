using System;

namespace Framework.Core
{
    public interface IServiceResolver
    {
        T Resolve<T>() where T : class;
        bool TryResolve<T>(out T service) where T : class;

        /// <summary>非泛型解析（供构造函数注入等反射场景使用）。</summary>
        object Resolve(Type serviceType);

        /// <summary>非泛型解析，未注册时返回 false 而不抛异常。</summary>
        bool TryResolve(Type serviceType, out object service);
    }
}
