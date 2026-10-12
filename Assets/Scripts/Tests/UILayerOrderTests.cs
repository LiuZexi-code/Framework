using Framework.Abstractions.UI;
using NUnit.Framework;

/// <summary>
/// UI 层内排序空间（<see cref="UILayerOrder"/>）的纯逻辑测试：层优先、优先级夹取、区间安全。
/// 真实的 Canvas.sortingOrder 落盘由 Play 验证（编辑器里创建 Canvas 属集成测试范围）。
/// </summary>
public class UILayerOrderTests
{
    [Test]
    public void BaseOf_IsOrderedByLayer()
    {
        Assert.Less(UILayerOrder.BaseOf(PanelLayer.Background), UILayerOrder.BaseOf(PanelLayer.Normal));
        Assert.Less(UILayerOrder.BaseOf(PanelLayer.Normal), UILayerOrder.BaseOf(PanelLayer.HighFrequency));
        Assert.Less(UILayerOrder.BaseOf(PanelLayer.HighFrequency), UILayerOrder.BaseOf(PanelLayer.Popup));
        Assert.Less(UILayerOrder.BaseOf(PanelLayer.Popup), UILayerOrder.BaseOf(PanelLayer.Toast));
        Assert.Less(UILayerOrder.BaseOf(PanelLayer.Toast), UILayerOrder.BaseOf(PanelLayer.Overlay));
    }

    /// <summary>层优先：**任何** 低层的面板都必须低于高层的基址（优先级不能越层）。</summary>
    [Test]
    public void LayerDominatesPriority()
    {
        int lowest = UILayerOrder.ForPanel(PanelLayer.Normal, int.MaxValue);
        int highestOfNext = UILayerOrder.ForPanel(PanelLayer.HighFrequency, int.MinValue);

        Assert.Less(lowest, highestOfNext, "层内优先级不得溢出到相邻层");
    }

    [Test]
    public void Priority_IsClamped()
    {
        Assert.AreEqual(UILayerOrder.BaseOf(PanelLayer.Normal) + 1, UILayerOrder.ForPanel(PanelLayer.Normal, -100));
        Assert.AreEqual(UILayerOrder.BaseOf(PanelLayer.Normal) + 1 + UILayerOrder.MaxPriority,
            UILayerOrder.ForPanel(PanelLayer.Normal, int.MaxValue));
    }

    /// <summary>面板必须**严格高于**自己所在层的 Canvas（否则与层 Canvas 同值，顺序要靠层级猜）。</summary>
    [Test]
    public void Panel_IsAboveItsOwnLayerCanvas()
    {
        foreach (PanelLayer layer in new[] { PanelLayer.Background, PanelLayer.Normal, PanelLayer.HighFrequency,
                                            PanelLayer.Popup, PanelLayer.Toast, PanelLayer.Overlay })
        {
            Assert.Greater(UILayerOrder.ForPanel(layer, 0), UILayerOrder.BaseOf(layer));
        }
    }

    [Test]
    public void HigherPriority_IsAbove()
    {
        Assert.Greater(UILayerOrder.ForPanel(PanelLayer.Popup, 10), UILayerOrder.ForPanel(PanelLayer.Popup, 1));
    }

    /// <summary>必须落在 Canvas.sortingOrder 的 int16 表达范围内（否则 Unity 会截断/异常）。</summary>
    [Test]
    public void AllValues_FitInInt16()
    {
        foreach (PanelLayer layer in new[] { PanelLayer.Background, PanelLayer.Normal, PanelLayer.HighFrequency,
                                            PanelLayer.Popup, PanelLayer.Toast, PanelLayer.Overlay })
        {
            Assert.Greater(UILayerOrder.ForPanel(layer, int.MinValue), 0, "必须大于场景 Canvas 的常见 0");
            Assert.Less(UILayerOrder.ForPanel(layer, int.MaxValue), short.MaxValue);
        }
    }
}
