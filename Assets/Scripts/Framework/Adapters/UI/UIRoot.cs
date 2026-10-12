using Framework.Abstractions.UI;
using UnityEngine;

namespace Framework.UI
{
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Transform _background;
        [SerializeField] private Transform _normal;
        [SerializeField] private Transform _highFrequency;
        [SerializeField] private Transform _popup;
        [SerializeField] private Transform _toast;
        [SerializeField] private Transform _overlay;

        public Transform GetLayer(PanelLayer layer)
        {
            var t = layer switch
            {
                PanelLayer.Background => _background,
                PanelLayer.Normal => _normal,
                PanelLayer.HighFrequency => _highFrequency,
                PanelLayer.Popup => _popup,
                PanelLayer.Toast => _toast,
                PanelLayer.Overlay => _overlay,
                _ => _normal
            };
            // 未绑定时退回根节点，避免 SetParent(null) 导致面板跑到根层级
            return t != null ? t : transform;
        }

        /// <summary>由 UIRootCreator 在运行时创建后调用，绑定各层级节点。</summary>
        public void BindLayers(Transform background, Transform normal, Transform highFrequency,
                               Transform popup, Transform toast, Transform overlay)
        {
            _background = background;
            _normal = normal;
            _highFrequency = highFrequency;
            _popup = popup;
            _toast = toast;
            _overlay = overlay;
        }
    }
}