
namespace Framework.Core
{
    public enum ServiceLifetime
    {
        Singleton,   // 容器内缓存，只创建一次
        Transient    // 每次解析新建，不缓存
    }
}