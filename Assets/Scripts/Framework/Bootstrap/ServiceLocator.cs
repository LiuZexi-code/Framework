using System;
using Framework.Core;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 提供给外部访问服务的接口
    /// </summary>
    public static class ServiceLocator
    {
        private static IServiceResolver current;

        public static T Get<T>() where T : class
        {
            if (current == null)
                throw new InvalidOperationException(
                    "[Strata] ServiceLocator has no resolver. Did GameRoot dispose?");
            return current.Resolve<T>();
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (current == null)
            {
                service = null;
                return false;
            }
            return current.TryResolve(out service);
        }

        internal static void Set(IServiceResolver serviceResolver)
        {
            current = serviceResolver;
        }

        internal static void Clear()
        {
            current = null;
        }
    }
}
