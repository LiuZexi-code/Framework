using UnityEngine;
using UnityEngine.UI;

namespace Game.RPG.Cursors
{
    /// <summary>
    /// 软件光标用的 UI 图层（自建常驻 <c>[CursorUI]</c> Canvas + 一个 Image），由两个软件方案共用。
    /// <para>
    /// **坐标约定（关键）**：`ScreenSpaceOverlay` + `CanvasScaler.ConstantPixelSize / scaleFactor = 1` 下，
    /// 若 Image 的锚点与轴心都取**左下角 (0,0)**，则 `anchoredPosition` 恰好等于「屏幕像素坐标（原点左下）」。
    /// 这与 `Canvas.pixelRect`、以及 `VirtualMouseInput` 写入虚拟 `Mouse` 的坐标是**同一套** ——
    /// 因此「跟随真实鼠标」和「跟随虚拟鼠标」两个方案可以共用同一段摆放代码。
    /// </para>
    /// <para>`sortingOrder = 20000` 必须高于 `UILayerOrder` 的 6000 上限，否则光标会被面板盖住。</para>
    /// </summary>
    internal sealed class CursorImageLayer
    {
        public const string CanvasName = "[CursorUI]";

        /// <summary>层排序：高于 UILayerOrder（层基址最大 6000）。</summary>
        private const int SortingOrder = 20000;

        private const float FallbackSize = 32f;

        private RectTransform _rect;
        private Image _image;

        public GameObject Root { get; private set; }
        public RectTransform Rect => _rect;
        public Image Graphic => _image;

        public void Ensure(float size)
        {
            // 与框架 AudioHost 同款守卫：关闭域重载时若发现残留先销毁重建，避免同时存在两个软件光标
            var existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                Debug.LogWarning($"[Cursor] 发现残留的 {CanvasName}，已销毁重建（避免出现两个软件光标）");
                Object.DestroyImmediate(existing);
            }

            var resolved = size > 0f ? size : FallbackSize;

            Root = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Root.transform.SetParent(null, false);

            var canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            var imageGo = new GameObject("Cursor", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(Root.transform, false);

            _rect = imageGo.GetComponent<RectTransform>();
            _rect.anchorMin = Vector2.zero;   // 左下角 —— 与 pixelRect / 虚拟鼠标坐标一致
            _rect.anchorMax = Vector2.zero;
            _rect.pivot = Vector2.zero;
            _rect.sizeDelta = new Vector2(resolved, resolved);

            _image = imageGo.GetComponent<Image>();
            // ★ 关键：不能拦射线，否则软件光标会挡住自己对世界的悬停/点击判定
            _image.raycastTarget = false;
            _image.enabled = false;

            Object.DontDestroyOnLoad(Root);
        }

        /// <summary>
        /// 把图标**中心**摆到屏幕坐标 <paramref name="centerPoint"/>（像素，原点左下），再叠加 <paramref name="offset"/>。
        /// <para>用「中心对齐」而不是「左下角对齐」：这样 `hotspotOffset = (0,0)` 就等于「指针在图标正中」，
        /// 与 A6 既有表现一致，也不会因为图标尺寸变化而整体偏移。</para>
        /// </summary>
        public void SetScreenPosition(Vector2 centerPoint, Vector2 offset, float size)
        {
            if (_rect == null) return;

            var resolved = size > 0f ? size : FallbackSize;
            var half = new Vector2(resolved * 0.5f, resolved * 0.5f);
            _rect.anchoredPosition = centerPoint + offset - half;
        }

        public void Destroy()
        {
            var root = Root;
            _image = null;
            _rect = null;
            Root = null;

            if (root != null) Object.Destroy(root);
        }
    }
}
