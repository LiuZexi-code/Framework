namespace Framework.Abstractions.GameConfig
{
    /// <summary>
    /// ConfigService注册config的入口
    /// </summary>
    public interface IConfigFactory
    {
        IGameConfig CreateConfig();
    }
}