namespace Framework.Abstractions.UI
{
    public interface ILoadingPanel : IUIPanel
    {
        public void SetProgress(float progress, string stage);
    }
}