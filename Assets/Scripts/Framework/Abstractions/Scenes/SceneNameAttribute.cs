using System;

namespace Framework.Abstractions.Scenes
{
    /// <summary>
    /// 标记一个 <c>string</c> 字段为「场景名」，供编辑器侧提供场景下拉与登记校验。
    /// <para>
    /// 用属性标记而不是直接引用场景资产，是因为 <c>UnityEditor.SceneAsset</c> 属于 Editor 程序集，
    /// 运行时程序集引用它会导致编译失败 / 出包报错 —— 所以字段类型必须是 <c>string</c>。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SceneNameAttribute : Attribute
    {
    }
}
