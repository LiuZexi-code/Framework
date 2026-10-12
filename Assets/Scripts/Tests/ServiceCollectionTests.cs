using System;
using System.Collections.Generic;
using Framework.Bootstrap.Modules;
using Framework.Core;
using NUnit.Framework;

/// <summary>
/// ServiceCollection 容器与模块注册器的基础回归测试（EditMode）。
/// </summary>
public class ServiceCollectionTests
{
    private interface IA { }
    private interface IB { }
    private interface IC { }

    private sealed class Spy : IA, IB, IC, IDisposable
    {
        public static readonly List<string> Disposed = new();
        private readonly string _name;

        public Spy(string name) => _name = name;
        public string Name => _name;
        public void Dispose() => Disposed.Add(_name);
    }

    private sealed class Parameterless
    {
        public int Value => 42;
    }

    private interface IRepo { }
    private sealed class Repo : IRepo { }

    private interface IA2 { }
    private interface IB2 { }

    private sealed class A2 : IA2
    {
        public A2(IB2 b) { }
    }

    private sealed class B2 : IB2
    {
        public B2(IA2 a) { }
    }

    private interface INeverRegistered { }

    private sealed class ServiceWithDependency
    {
        public IRepo Repo { get; }
        public ServiceWithDependency(IRepo repo) => Repo = repo;
    }

    private sealed class ServiceWithDefault
    {
        public int Retries { get; }
        public ServiceWithDefault(int retries = 3) => Retries = retries;
    }

    private sealed class ServiceWithTwoCtors
    {
        public bool UsedFullCtor;
        public ServiceWithTwoCtors() => UsedFullCtor = false;
        public ServiceWithTwoCtors(IRepo repo) => UsedFullCtor = true;
    }

    private sealed class ServiceWithMissingDependency
    {
        public ServiceWithMissingDependency(IRepo repo, INeverRegistered missing) { }
    }

    [SetUp]
    public void SetUp() => Spy.Disposed.Clear();

    // ---------- 注册 / 解析 ----------

    [Test]
    public void RegisterInstance_Resolve_ReturnsSameInstance()
    {
        var c = new ServiceCollection();
        var spy = new Spy("x");
        c.Register<IA>(spy);

        Assert.AreSame(spy, c.Resolve<IA>());
    }

    [Test]
    public void RegisterFactory_Resolve_UsesFactory()
    {
        var c = new ServiceCollection();
        c.RegisterFactory<IA>(_ => new Spy("f"));

        Assert.AreEqual("f", ((Spy)c.Resolve<IA>()).Name);
    }

    [Test]
    public void RegisterType_WithParameterlessCtor_Resolves()
    {
        var c = new ServiceCollection();
        c.Register<Parameterless, Parameterless>();

        Assert.AreEqual(42, c.Resolve<Parameterless>().Value);
    }

    [Test]
    public void Singleton_IsCached()
    {
        var c = new ServiceCollection();
        int created = 0;
        c.RegisterFactory<IA>(_ => { created++; return new Spy("s"); });

        var a = c.Resolve<IA>();
        var b = c.Resolve<IA>();

        Assert.AreSame(a, b);
        Assert.AreEqual(1, created);
    }

    [Test]
    public void Transient_CreatesNewEachTime()
    {
        var c = new ServiceCollection();
        int created = 0;
        c.RegisterFactory<IA>(_ => { created++; return new Spy("t"); }, ServiceLifetime.Transient);

        var a = c.Resolve<IA>();
        var b = c.Resolve<IA>();

        Assert.AreNotSame(a, b);
        Assert.AreEqual(2, created);
    }

    // ---------- 未注册 ----------

    [Test]
    public void Resolve_Unregistered_Throws()
    {
        var c = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => c.Resolve<IA>());
    }

    [Test]
    public void TryResolve_Unregistered_ReturnsFalse()
    {
        var c = new ServiceCollection();
        Assert.IsFalse(c.TryResolve<IA>(out _));
    }

    // ---------- 父子容器 ----------

    [Test]
    public void TryResolve_FallsBackToParent()
    {
        var parent = new ServiceCollection();
        var spy = new Spy("parent");
        parent.Register<IA>(spy);

        var child = new ServiceCollection(parent);
        Assert.IsTrue(child.TryResolve<IA>(out var got));
        Assert.AreSame(spy, got);
    }

    [Test]
    public void Child_Registration_OverridesParent()
    {
        var parent = new ServiceCollection();
        parent.Register<IA>(new Spy("parent"));

        var child = new ServiceCollection(parent);
        child.Register<IA>(new Spy("child"));

        Assert.AreEqual("child", ((Spy)child.Resolve<IA>()).Name);
    }

    // ---------- 释放 ----------

    [Test]
    public void Dispose_DisposesSingletons_InReverseCreationOrder()
    {
        var c = new ServiceCollection();
        c.RegisterFactory<IA>(_ => new Spy("A"));
        c.RegisterFactory<IB>(_ => new Spy("B"));
        c.RegisterFactory<IC>(_ => new Spy("C"));

        c.Resolve<IA>();
        c.Resolve<IB>();
        c.Resolve<IC>();
        c.Dispose();

        CollectionAssert.AreEqual(new[] { "C", "B", "A" }, Spy.Disposed);
    }

    [Test]
    public void Dispose_Twice_IsSafe()
    {
        var c = new ServiceCollection();
        c.RegisterFactory<IA>(_ => new Spy("A"));
        c.Resolve<IA>();

        c.Dispose();
        Assert.DoesNotThrow(() => c.Dispose());
    }

    // ---------- 构造函数注入 ----------

    [Test]
    public void RegisterType_WithDependency_InjectsDependency()
    {
        var c = new ServiceCollection();
        c.Register<IRepo, Repo>();
        c.Register<ServiceWithDependency, ServiceWithDependency>();

        Assert.IsNotNull(c.Resolve<ServiceWithDependency>().Repo);
    }

    [Test]
    public void RegisterType_OptionalParameter_UsesDefaultValue()
    {
        var c = new ServiceCollection();
        c.Register<ServiceWithDefault, ServiceWithDefault>();

        Assert.AreEqual(3, c.Resolve<ServiceWithDefault>().Retries);
    }

    [Test]
    public void RegisterType_PrefersConstructorWithResolvableDependency()
    {
        var c = new ServiceCollection();
        c.Register<IRepo, Repo>();
        c.Register<ServiceWithTwoCtors, ServiceWithTwoCtors>();

        Assert.IsTrue(c.Resolve<ServiceWithTwoCtors>().UsedFullCtor);
    }

    [Test]
    public void RegisterType_FallsBackToParameterlessCtor()
    {
        var c = new ServiceCollection();
        c.Register<ServiceWithTwoCtors, ServiceWithTwoCtors>();

        Assert.IsFalse(c.Resolve<ServiceWithTwoCtors>().UsedFullCtor);
    }

    [Test]
    public void RegisterType_UnresolvableDependency_Throws()
    {
        var c = new ServiceCollection();
        c.Register<IRepo, Repo>();
        c.Register<ServiceWithMissingDependency, ServiceWithMissingDependency>();

        Assert.Throws<InvalidOperationException>(() => c.Resolve<ServiceWithMissingDependency>());
    }

    [Test]
    public void RegisterType_CyclicDependency_Throws()
    {
        var c = new ServiceCollection();
        c.Register<IA2, A2>();
        c.Register<IB2, B2>();

        Assert.Throws<InvalidOperationException>(() => c.Resolve<IA2>());
    }

    // ---------- 模块注册器 ----------

    [Test]
    public void RegisterModules_NullRegistry_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => ServiceModuleRegistry.RegisterGlobalModules(null));
        Assert.DoesNotThrow(() => ServiceModuleRegistry.RegisterSceneModules(null, "AnyScene"));
    }
}
