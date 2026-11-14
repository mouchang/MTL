namespace MutsumiLauncher.Views.VersionSettings;

/// <summary>版本设置里需要知道当前实例的页面。</summary>
public interface IInstancePage
{
    void SetInstance(string version);

    /// <summary>切到这一页时把内容滚回顶部。</summary>
    void ScrollToTop();
}
