using System;
using System.Collections.Generic;
using Framework.Core;

namespace Framework.Abstractions.UI
{
    public sealed class PanelDefinition
    {
        /// <summary>
        /// panel脚本的类型
        /// </summary>
        public Type PanelType;
        /// <summary>
        /// 预制体位置
        /// </summary>
        public string PrefabAddress;
        /// <summary>
        /// 挂载层级
        /// </summary>
        public PanelLayer Layer;
        /// <summary>
        /// 关闭后是否保留实例
        /// </summary>
        public bool CacheOnClose;
        /// <summary>
        /// 是否跨场景常驻
        /// </summary>
        public bool IsGlobal;
        /// <summary>
        /// 加载策略（当IsGlobal=true时此属性无效）
        /// </summary>
        public LoadPolicy loadPolicy;
        /// <summary>
        /// 适用场景标签，空表示所有场景,当loadPolicy为预加载时，进入指定场景前会预加载（当IsGlobal=true时此属性无效）
        /// </summary>
        public List<string> SceneTags;

        /// <summary>
        /// 层内优先级（越大越靠上）。**只在同一 <see cref="Layer"/> 内生效**；
        /// 相同优先级时由「打开顺序」决定（后开的在上）。映射见 <see cref="UILayerOrder"/>。
        /// </summary>
        public int Priority;

        /// <summary>
        /// 是否模态：为 true 时，弹窗栈会把它「下面的」弹窗挡住点击（由弹窗服务放置共享遮罩）。
        /// 默认 false —— 像 Toast 这类轻提示天生不该阻断交互。
        /// </summary>
        public bool IsModal;

        public PanelDefinition(Type panelType,string prefabAddress,PanelLayer layer,bool cacheOnClose,bool isGlobal,List<string> sceneTags,LoadPolicy loadPolicy = LoadPolicy.OnDemand, int priority = 0, bool isModal = false)
        {
            PanelType = panelType;
            PrefabAddress = prefabAddress;
            Layer = layer;
            CacheOnClose = cacheOnClose;
            IsGlobal = isGlobal;
            SceneTags = sceneTags;
            this.loadPolicy = loadPolicy;
            Priority = priority;
            IsModal = isModal;
        }
    }
}