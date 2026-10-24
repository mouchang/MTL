namespace MutsumiLauncher.Views.VersionSettings;

/// <summary>版本设置里需要知道当前实例的页面。</summary>
public interface IInstancePage
{
    void SetInstance(string version);
}
