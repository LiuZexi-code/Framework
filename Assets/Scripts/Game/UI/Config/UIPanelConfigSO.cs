using System;
using System.Collections.Generic;
using Framework.Abstractions;
using Framework.Abstractions.UI;
using Framework.Adapters;
using Framework.Core;
using Framework.UI;
using UnityEngine;


namespace Game.UI.Config
{
    [CreateAssetMenu(menuName = "Game/GameConfig/UI Panel Config", fileName = "UIPanelConfig")]
    public sealed class UIPanelConfigSO : ScriptableObject
    {
        [Tooltip("面板key")]
        public string panelKey;
        [Tooltip("预制体地址")]
        public string PrefabAddress;
        [Tooltip("挂载层级")]
        public PanelLayer Layer = PanelLayer.Normal;
        [Tooltip("关闭后是否保留实例")]
        public bool cacheOnClose = true;
        [Tooltip("适用场景标签。**留空 = 所有场景通用（跨场景常驻）**。\n" +
                 "非空时同时决定三件事：① 能否在该场景打开 ② 进入该场景前是否预加载 ③ 离开时是否销毁")]
        public List<string> sceneTags = new();
        [Tooltip("加载策略（仅对非全局面板生效）")]
        public LoadPolicy loadPolicy;

        [Tooltip("层内优先级（越大越靠上；只在同 Layer 内生效；同优先级时后开的在上）")]
        public int priority;

        [Tooltip("是否模态：弹窗栈会挡住其下层面板的点击（Toast 这类轻提示不要勾）")]
        public bool isModal;

        [Tooltip("打开本面板时自动推入的输入上下文（留空 = 不参与）。本项目填 UI：打开即阻断玩法输入，最后一个声明该上下文的面板关闭时自动弹出。")]
        public string inputContextId = "";

        [Tooltip("自动推入输入上下文时的优先级（越大越优先；同上下文被多个面板声明时取最大值）")]
        public int inputContextPriority;

    }
}