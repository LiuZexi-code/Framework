using Framework.Abstractions.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Framework.UI
{
    public static class UIRootCreator
    {
        /// <param name="settings">UI 缩放参数（参考分辨率/匹配模式）；为 null 时用默认 1920×1080 / 0.5。</param>
        public static GameObject CreateUIRoot(out UIRoot uIRoot,Transform parent = null, UISettings settings = null)
        {
            settings ??= new UISettings();

            Debug.Log($"[UI] UIRoot 缩放：参考分辨率 {settings.ReferenceResolution.x}×{settings.ReferenceResolution.y}，" +
                      $"MatchWidthOrHeight={settings.MatchWidthOrHeight}（0→按宽，1→按高）");

            // 没有 EventSystem 的 UI 收不到点击/拖拽，先确保存在
            EnsureEventSystem();

            GameObject root = CreateCanvas(parent, "UIRoot", 0, settings);

            var background    = CreateCanvas(root.transform, "Canvas_Background", -100, settings);
            var normal        = CreateCanvas(root.transform, "Canvas_Normal", 0, settings);
            var highFrequency = CreateCanvas(root.transform, "Canvas_HighFrequency", 100, settings);
            var popup         = CreateCanvas(root.transform, "Canvas_Popup", 200, settings);
            var toast         = CreateCanvas(root.transform, "Canvas_Toast", 300, settings);
            var overlay       = CreateCanvas(root.transform, "Canvas_Overlay", 400, settings);

            uIRoot = root.AddComponent<UIRoot>();
            uIRoot.BindLayers(background.transform, normal.transform, highFrequency.transform,
                              popup.transform, toast.transform, overlay.transform);

            // 运行时创建的 UI 根节点不归属任何场景，避免场景卸载时被销毁
            if (parent == null) Object.DontDestroyOnLoad(root);

            return root;
        }

        /// <summary>
        /// 确保场景中存在 EventSystem（否则运行时 UI 无法接收交互事件）。
        /// 已存在（含场景自带的）则直接复用，不重复创建。
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            EventSystem existing = Object.FindObjectOfType<EventSystem>();
            if (existing != null) return existing;

            GameObject go = new GameObject("EventSystem");
            EventSystem eventSystem = go.AddComponent<EventSystem>();
            AddInputModule(go);

            // 与 UIRoot 同样跨场景常驻，避免切换场景后交互失效
            Object.DontDestroyOnLoad(go);
            return eventSystem;
        }

        /// <summary>
        /// 按项目的 Active Input Handling 选择输入模块：
        /// 新输入系统启用时优先 InputSystemUIInputModule（反射获取，避免框架硬依赖该包），
        /// 否则回退到旧输入系统的 StandaloneInputModule。
        /// </summary>
        private static void AddInputModule(GameObject go)
        {
#if ENABLE_INPUT_SYSTEM
            System.Type moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
                return;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<StandaloneInputModule>();
#else
            Debug.LogWarning("[UIRootCreator] 未找到可用的 UI 输入模块（InputSystemUIInputModule / StandaloneInputModule），UI 交互将不可用。");
#endif
        }

        // 缩放参数由 UISettings 传入（业务可在启动时按配置覆盖），不再写死分辨率
        private static GameObject CreateCanvas(Transform parent, string name, int sortingOrder, UISettings settings)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if(parent != null) go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = settings.MatchWidthOrHeight;

            go.AddComponent<GraphicRaycaster>();

            return go;
        }
    }
}