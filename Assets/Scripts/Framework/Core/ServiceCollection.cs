using System;
using System.Collections.Generic;
using System.Reflection;

namespace Framework.Core
{
    public sealed class ServiceCollection : IServiceRegistry, IServiceResolver,IDisposable
    {
        /// <summary>
        /// 服务释放失败时的回调（默认写入 stderr）。宿主可接上日志系统，例如：
        /// <c>ServiceCollection.DisposeErrorHandler = e =&gt; Log.Error(e.ToString());</c>
        /// </summary>
        public static Action<Exception> DisposeErrorHandler;

        private readonly Dictionary<Type,ServiceDescriptor> _descriptors = new();
        private readonly Dictionary<Type,object> _cache = new();
        private readonly List<object> _creationOrder = new();
        private readonly IServiceResolver _parent;

        public ServiceCollection(IServiceResolver parent = null)
        {
            _parent = parent;
        }

        public void Register<TService>(TService instance) where TService : class
        {
            _descriptors[typeof(TService)] = ServiceDescriptor.FromInstance(instance);
        }

        public void Register<TService, TImpl>(ServiceLifetime lifeTime = ServiceLifetime.Singleton) where TService : class where TImpl : class,TService
        {
            _descriptors[typeof(TService)] = ServiceDescriptor.FromType(typeof(TImpl),lifeTime);
        }

        public void RegisterFactory<TService>(Func<IServiceResolver, TService> factory,ServiceLifetime lifeTime = ServiceLifetime.Singleton) where TService : class
        {
            _descriptors[typeof(TService)] = ServiceDescriptor.FromFactory(resolver => factory(resolver),lifeTime);
        }

        public T Resolve<T>() where T : class
        {
            if (TryResolve<T>(out var service)) return service;

            throw new InvalidOperationException($"Service not registered: {typeof(T)}");
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            if (TryResolve(typeof(T), out var raw))
            {
                service = (T)raw;
                return true;
            }

            service = null;
            return false;
        }

        public object Resolve(Type serviceType)
        {
            if (TryResolve(serviceType, out var service)) return service;

            throw new InvalidOperationException($"Service not registered: {serviceType}");
        }

        public bool TryResolve(Type serviceType, out object service)
        {
            // 1. 先查自己
            if (_descriptors.TryGetValue(serviceType, out var descriptor))
            {
                service = CreateOrGet(descriptor, serviceType);
                return true;
            }

            // 2. 回退父容器
            if (_parent != null)
                return _parent.TryResolve(serviceType, out service);

            service = null;
            return false;
        }

        private object CreateOrGet(ServiceDescriptor descriptor, Type serviceType)
        {
            // 已缓存（Singleton）
            if (_cache.TryGetValue(serviceType, out var cached))
                return cached;

            object created = descriptor.Instance
                ?? (descriptor.Factory != null
                    ? descriptor.Factory(this)
                    : CreateInstance(descriptor.ImplementationType));

            if (descriptor.Lifetime == ServiceLifetime.Singleton)
            {
                _cache[serviceType] = created;
                _creationOrder.Add(created);
            }

            return created;
        }

        // ---------- 构造函数注入 ----------

        private readonly Dictionary<Type, ConstructorInfo> _ctorCache = new();
        private readonly HashSet<Type> _resolving = new();

        /// <summary>按「参数最多且都能满足」的公开构造创建实例，并自动解析依赖。</summary>
        private object CreateInstance(Type implType)
        {
            if (implType == null)
                throw new InvalidOperationException("ServiceDescriptor 缺少实现类型（Instance/Factory/ImplementationType 均为空）。");

            if (!_resolving.Add(implType))
                throw new InvalidOperationException($"检测到循环依赖：{implType.FullName}");

            try
            {
                var ctor = SelectConstructor(implType, out var args);
                if (ctor == null)
                    throw new InvalidOperationException(
                        $"无法构造 {implType.FullName}：没有参数可满足的公开构造函数（请注册其依赖或提供默认值/无参构造）。");

                return ctor.Invoke(args);
            }
            finally
            {
                _resolving.Remove(implType);
            }
        }

        private ConstructorInfo SelectConstructor(Type implType, out object[] args)
        {
            args = null;

            if (_ctorCache.TryGetValue(implType, out var cached))
            {
                var cps = cached.GetParameters();
                var cbuilt = new object[cps.Length];
                for (int i = 0; i < cps.Length; i++)
                {
                    if (!TryBuildParameter(cps[i], out cbuilt[i]))
                        throw new InvalidOperationException(
                            $"无法构造 {implType.FullName}：构造参数 {cps[i].ParameterType.Name} {cps[i].Name} 未注册。");
                }
                args = cbuilt;
                return cached;
            }

            var ctors = implType.GetConstructors();
            Array.Sort(ctors, (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();
                var built = new object[ps.Length];
                bool ok = true;

                for (int i = 0; i < ps.Length; i++)
                {
                    if (TryBuildParameter(ps[i], out built[i])) continue;
                    ok = false;
                    break;
                }

                if (!ok) continue;

                _ctorCache[implType] = ctor;
                args = built;
                return ctor;
            }

            return null;
        }

        private bool TryBuildParameter(ParameterInfo p, out object value)
        {
            if (TryResolve(p.ParameterType, out value)) return true;

            if (p.HasDefaultValue)
            {
                value = p.DefaultValue;
                return true;
            }

            value = null;
            return false;
        }

        public void Dispose()
        {
            for (int i = _creationOrder.Count - 1; i >= 0; i--)
            {
                if (_creationOrder[i] is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception e)
                    {
                        // 单个服务释放失败不应中断其余服务的释放（旧实现会 throw 导致后续全部泄漏）
                        var handler = DisposeErrorHandler;
                        if (handler != null) handler(e);
                        else Console.Error.WriteLine(
                            $"[ServiceCollection] Dispose failed: {disposable.GetType().FullName} -> {e}");
                    }
                }
            }

            _creationOrder.Clear();
            _cache.Clear();
            _descriptors.Clear();
        }
    }
}
