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
        /// 是否跨场景常驻（= 全局）。
        /// <para>
        /// ⚠️ 这是**派生**语义，不再有独立开关：<see cref="SceneTags"/> 为空就代表「所有场景通用」→ 常驻。
        /// 以前两者是两个独立字段，会出现「勾了全局却还留着场景标签」这种**自相矛盾且无提示**的配置；
        /// 合并后「无标签 = 全局」成为唯一真相。
        /// </para>
        /// </summary>
        public bool IsGlobal => SceneTags == null || SceneTags.Count == 0;
        /// <summary>
        /// 加载策略（当 <see cref="IsGlobal"/> 为 true 时此属性无效）
        /// </summary>
        public LoadPolicy loadPolicy;
        /// <summary>
        /// 适用场景标签，<b>空表示所有场景（= 常驻）</b>。
        /// <para>
        /// 非空时会被三处消费，因此误填的代价是「打不开 + 被销毁」：
        /// ① 打开时的场景校验（<c>UIService.CheckSceneTag</c>）；
        /// ② 进入场景前的预加载筛选（<c>PreloadPanelsStep</c>）；
        /// ③ 退出场景时的实例销毁判定（<c>UIService.DisposeScenePanels</c>）。
        /// </para>
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

        /// <summary>
        /// 该面板打开时自动推入的**输入上下文**；为空表示不参与自动切换（默认）。
        /// <para>
        /// 语义完全由项目层决定（框架不解释其含义）：本项目填 <c>"UI"</c>，
        /// 打开即阻断玩法输入，最后一个声明该上下文的面板关闭时自动弹出。
        /// </para>
        /// <para>多个面板声明同一上下文时，只有「第一个打开 / 最后一个关闭」才会真正 Push / Pop。</para>
        /// </summary>
        public string InputContextId;

        /// <summary>自动推入输入上下文时的优先级（越大越优先；同 id 被多个面板声明时取最大值）。</summary>
        public int InputContextPriority;

        public PanelDefinition(Type panelType,string prefabAddress,PanelLayer layer,bool cacheOnClose,List<string> sceneTags,LoadPolicy loadPolicy = LoadPolicy.OnDemand, int priority = 0, bool isModal = false, string inputContextId = null, int inputContextPriority = 0)
        {
            PanelType = panelType;
            PrefabAddress = prefabAddress;
            Layer = layer;
            CacheOnClose = cacheOnClose;
            SceneTags = sceneTags;
            this.loadPolicy = loadPolicy;
            Priority = priority;
            IsModal = isModal;
            InputContextId = inputContextId;
            InputContextPriority = inputContextPriority;
        }
    }
}