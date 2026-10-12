namespace Framework.Abstractions.UI
{
    public enum PanelLayer
    {
        Background = 0,     // 静态背景（主界面底图等，位于最底层）
        Normal = 1,         // 低频 UI（主菜单、设置页等）
        HighFrequency = 2,  // 高频刷新 UI（HUD、血条、计时等）
        Popup = 3,          // 弹窗（对话框、确认框）
        Toast = 4,          // 轻提示（Toast / 飘字）
        Overlay = 5,        // 顶层遮罩（转场、强制全屏提示）
    }
}