using UnityEngine;

namespace Framework.Abstractions.UI
{
    /// <summary>
    /// UI 根节点的缩放参数。由 UI 配置写入，在创建 <c>UIRoot</c> 时生效
    /// （解决「配置里有 referenceResolution/matchWidthOrHeight 但写死在代码里」的问题）。
    /// </summary>
    public sealed class UISettings
    {
        /// <summary>参考分辨率（默认 1920×1080，横屏基准）。</summary>
        public Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>宽高匹配权重：0=按宽度匹配，1=按高度匹配。</summary>
        public float MatchWidthOrHeight = 0.5f;

        /// <summary>
        /// 是否强制校验面板的场景标签：非全局面板若声明了 <c>SceneTags</c>，
        /// 当前场景不匹配时**拒绝打开**；设为 false 则只记警告仍然打开。
        /// </summary>
        public bool EnforceSceneTags = true;
    }
}
