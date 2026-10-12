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

        public PanelDefinition(Type panelType,string prefabAddress,PanelLayer layer,bool cacheOnClose,bool isGlobal,List<string> sceneTags,LoadPolicy loadPolicy = LoadPolicy.OnDemand)
        {
            PanelType = panelType;
            PrefabAddress = prefabAddress;
            Layer = layer;
            CacheOnClose = cacheOnClose;
            IsGlobal = isGlobal;
            SceneTags = sceneTags;
            this.loadPolicy = loadPolicy;
        }
    }
}