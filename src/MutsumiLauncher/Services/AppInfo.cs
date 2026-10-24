using System.Reflection;

namespace MutsumiLauncher.Services;

/// <summary>应用名称与版本信息（版本号来自 csproj / Directory.Build.props）。</summary>
public static class AppInfo
{
    public static string DisplayName
    {
        get
        {
            var value = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(value)) return "Mutsumi Launcher";

            var plus = value.IndexOf('+');
            return plus > 0 ? value[..plus] : value;
        }
    }

    public static string Version => UpdateService.CurrentVersion;
}
