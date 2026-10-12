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
        [Tooltip("是否跨场景常驻")]
        public bool isGlobal;
        [Tooltip("适用场景标签，空表示所有场景")]
        public List<string> sceneTags = new();
        [Tooltip("加载策略")]
        public LoadPolicy loadPolicy;

    }
}