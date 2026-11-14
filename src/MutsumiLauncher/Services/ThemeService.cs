using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

/// <summary>主题模式：跟随系统 / 浅色 / 深色。</summary>
public enum ColorMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// 主题：参考 FlClash 的配色模型——主题模式 + 种子色 + 配色风格（+ 纯黑）。
/// 整套色板由 MaterialTheme 从一个种子色推导，替换掉 Palette.xaml 那一份，
/// 界面靠 DynamicResource 自动重绘。
/// </summary>
public static class ThemeService
{
    public const string DefaultAccent = "#FF6750A4";
    public const string DefaultStyle = "balanced";

    private static ResourceDictionary? _installed;

    /// <summary>主题变了（用于让图表、用代码画的笔刷跟着刷新）。</summary>
    public static event Action? Changed;

    public static ColorMode Mode { get; private set; } = ColorMode.System;

    /// <summary>当前实际是不是深色（跟随系统时会算系统状态）。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>默认种子色。</summary>
    public static string Seed { get; private set; } = DefaultAccent;

    /// <summary>配色风格：soft / balanced / vibrant / mono。</summary>
    public static string Style { get; private set; } = DefaultStyle;

    /// <summary>深色下是否使用纯黑底。</summary>
    public static bool PureBlack { get; private set; }

    /// <summary>设置页可选的种子色。</summary>
    public static readonly (string Name, string Hex)[] ThemeSeeds =
    [
        ("紫罗兰", "#FF6750A4"),
        ("薄荷", "#FF2E9E6B"),
        ("海蓝", "#FF00658B"),
        ("青碧", "#FF006A6A"),
        ("玫瑰", "#FF8C4A60"),
        ("琥珀", "#FF7D5700"),
        ("橄榄", "#FF4F6354"),
        ("珊瑚", "#FFB3261E"),
        ("靛蓝", "#FF4355B9"),
        ("灰蓝", "#FF47617A"),
    ];

    public static void ApplyFromConfig()
    {
        var config = AppConfig.Current;

        Apply(ParseMode(config.ThemeMode), config.ThemeSeed, config.ThemeStyle, config.ThemePureBlack);
    }

    public static void SetTheme(ColorMode mode, string? seed = null, string? style = null,
        bool? pureBlack = null)
    {
        var config = AppConfig.Current;

        config.ThemeMode = ModeName(mode);

        if (!string.IsNullOrWhiteSpace(seed)) config.ThemeSeed = NormalizeHex(seed);
        if (!string.IsNullOrWhiteSpace(style)) config.ThemeStyle = style;
        if (pureBlack is not null) config.ThemePureBlack = pureBlack.Value;

        config.Save();

        Apply(mode, config.ThemeSeed, config.ThemeStyle, config.ThemePureBlack);
    }

    public static void Apply(ColorMode mode, string? seedHex = null, string? style = null,
        bool? pureBlack = null)
    {
        Mode = mode;
        Seed = NormalizeHex(seedHex);
        Style = MaterialTheme.StyleName(MaterialTheme.ParseStyle(style));
        PureBlack = pureBlack ?? PureBlack;

        IsDark = mode switch
        {
            ColorMode.Dark => true,
            ColorMode.Light => false,
            _ => SystemIsDark(),
        };

        var theme = MaterialTheme.Build(ParseColor(Seed), MaterialTheme.ParseStyle(Style), IsDark,
            LightPalette(), PureBlack && IsDark);

        Install(BuildDictionary(theme));
        Changed?.Invoke();
    }

    public static string ModeName(ColorMode mode) => mode switch
    {
        ColorMode.Dark => "dark",
        ColorMode.Light => "light",
        _ => "system",
    };

    public static ColorMode ParseMode(string? value) => value switch
    {
        "dark" => ColorMode.Dark,
        "light" or "custom" => ColorMode.Light,
        _ => ColorMode.System,
    };

    /// <summary>系统是否处于深色模式（读注册表，读不到按浅色）。</summary>
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读 Windows 个性化里的主题色（DWM ColorizationColor）。</summary>
    public static Color? ReadSystemAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\DWM");

            if (key?.GetValue("ColorizationColor") is not int value) return null;

            return Color.FromRgb(
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把颜色换算成笔刷字典插到 Application.Resources，替换掉 Palette.xaml。</summary>
    private static void Install(ResourceDictionary theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;

        var source = merged.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString?.Contains("Palette.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var index = source is not null
            ? merged.IndexOf(source)
            : _installed is not null ? merged.IndexOf(_installed) : 0;

        if (index < 0) index = 0;

        if (source is not null) merged.Remove(source);
        if (_installed is not null && merged.Contains(_installed)) merged.Remove(_installed);

        merged.Insert(Math.Clamp(index, 0, merged.Count), theme);
        _installed = theme;
    }

    private static ResourceDictionary BuildDictionary(IReadOnlyDictionary<string, Color> colors)
    {
        var dictionary = new ResourceDictionary { ["BackdropImage"] = LoadBackdrop() };

        foreach (var (key, color) in colors)
        {
            if (key is "ShadowColor" or "ShadowColorSoft")
            {
                dictionary[key] = color;
                continue;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dictionary[key] = brush;
        }

        dictionary["ShadowColor"] = colors.TryGetValue("ShadowTint", out var shadow) ? shadow : Colors.Black;

        return dictionary;
    }

    /// <summary>窗口底图。</summary>
    private static ImageSource LoadBackdrop()
    {
        var image = new BitmapImage();

        try
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(
                "pack://application:,,,/MutsumiLauncher;component/Assets/background.jpg",
                UriKind.Absolute);
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load backdrop");
        }

        return image;
    }

    /// <summary>启动时自查：色板必填键是否齐全。</summary>
    public static void Verify()
    {
        try
        {
            var light = LightPalette();

            if (light.Count < 40)
                AppLog.Info($"theme: 色板只有 {light.Count} 个键，可能不完整");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "verify theme");
        }
    }

    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return DefaultAccent;

        var value = hex.Trim().ToUpperInvariant();
        if (!value.StartsWith('#')) value = "#" + value;

        value = value.Length switch
        {
            7 => "#FF" + value[1..],
            9 => value,
            _ => string.Empty,
        };

        if (value.Length == 0) return DefaultAccent;

        try
        {
            _ = ColorConverter.ConvertFromString(value);
            return value;
        }
        catch
        {
            return DefaultAccent;
        }
    }

    public static Color ParseColor(string? hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(NormalizeHex(hex)) is Color color) return color;
        }
        catch
        {
            // 走默认
        }

        return Color.FromRgb(0x4E, 0x9E, 0x72);
    }

    private static Color Hex(string value)
        => ColorConverter.ConvertFromString(value) is Color color ? color : Colors.Transparent;

    private static Dictionary<string, Color> LightPalette() => new(StringComparer.Ordinal)
    {
        ["Ink"] = Hex("#2E3E35"),
        ["InkStrong"] = Hex("#1E2C24"),
        ["InkMax"] = Hex("#0F1712"),
        ["InkSoft"] = Hex("#5C6F63"),
        ["InkMute"] = Hex("#86988C"),
        ["InkInverse"] = Hex("#F7FBF4"),
        ["InkOnAccent"] = Hex("#FFFFFF"),

        ["Accent"] = Hex("#B8E9C6"),
        ["AccentDeep"] = Hex("#4E9E72"),
        ["AccentWash"] = Hex("#C9EDD5"),
        ["AccentTint"] = Hex("#E6F2E8"),
        ["AccentTintDeep"] = Hex("#C9EDD5"),
        ["AccentTintSoft"] = Hex("#DCEFE0"),
        ["PrimaryFill"] = Hex("#3F8E63"),
        ["PrimaryFillHover"] = Hex("#357A55"),

        ["Glass"] = Hex("#66FFFFFF"),
        ["GlassMedium"] = Hex("#8CFFFFFF"),
        ["GlassStrong"] = Hex("#B8FFFFFF"),
        ["GlassHover"] = Hex("#A6FFFFFF"),
        ["GlassHoverSoft"] = Hex("#55FFFFFF"),
        ["GlassStroke"] = Hex("#78FFFFFF"),
        ["HoverWash"] = Hex("#40FFFFFF"),
        ["OverlayWash"] = Hex("#22FFFFFF"),
        ["TrackBg"] = Hex("#33FFFFFF"),
        ["ChipBg"] = Hex("#14FFFFFF"),
        ["Divider"] = Hex("#1AFFFFFF"),
        ["RingStroke"] = Hex("#99FFFFFF"),

        ["WindowBase"] = Hex("#F7FBF4"),
        ["WindowChrome"] = Hex("#35473C"),
        ["WindowBar"] = Hex("#5CFFFFFF"),
        ["WindowBorder"] = Hex("#85FFFFFF"),
        ["PaneBg"] = Hex("#F2F7FBF4"),
        ["SurfacePanel"] = Hex("#F2FFFFFF"),
        ["SurfaceFloat"] = Hex("#F7FFFFFF"),
        ["SurfaceSolid"] = Hex("#FFFFFF"),
        ["SidebarBg"] = Hex("#4DFFFFFF"),
        ["AvatarCard"] = Hex("#59FFFFFF"),
        ["PillBg"] = Hex("#73FFFFFF"),
        ["ToastBg"] = Hex("#E62E3E35"),
        ["ModalScrim"] = Hex("#33000000"),
        ["Scrim"] = Hex("#1FFFFFFF"),
        ["BackdropVeil"] = Hex("#40FFFFFF"),

        ["CardBorder"] = Hex("#88FFFFFF"),
        ["PanelBorderSoft"] = Hex("#70FFFFFF"),
        ["DialogHairline"] = Hex("#1A000000"),

        ["Danger"] = Hex("#C0392B"),
        ["DangerInk"] = Hex("#B03A2E"),
        ["DangerDeep"] = Hex("#8E2A27"),
        ["DangerWash"] = Hex("#F4E5E5"),
        ["DangerWashSoft"] = Hex("#22C0392B"),
        ["DangerWashBorder"] = Hex("#66C0392B"),
        ["InfoTint"] = Hex("#D9EEF7"),
        ["InfoInk"] = Hex("#3E5C6E"),
        ["SourceGreen"] = Hex("#3F7F5C"),
        ["SourceOrange"] = Hex("#B45F1B"),

        ["ShadowTint"] = Hex("#1B2B22"),
        ["ShadowColorSoft"] = Hex("#2E4A38"),
    };
}
