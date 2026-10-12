using System.Collections.Generic;
using UnityEngine;

namespace Game.UI.Config
{
    [CreateAssetMenu(menuName = "Game/Config/UIConfigAsset")]
    public class UIConfigAssetSO : ScriptableObject
    {
        public Vector2 referenceResolution = new Vector2(1920, 1080);
        public float matchWidthOrHeight = 0.5f;
        public List<UIPanelConfigSO> panels = new();
    }
}