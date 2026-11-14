using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

public enum AppTheme
{
    Light,
    Custom,
}

/// <summary>
/// 主题：明亮 / 自定义。整套色板放在一个 ResourceDictionary 里，
/// 切换时把 Themes/Palette.xaml 那一份整份换掉，界面靠 DynamicResource 自动重绘。
/// </summary>
public static class ThemeService
{
    public const string DefaultAccent = "#FF4E9E72";

    private static ResourceDictionary? _installed;

    /// <summary>主题变了（用于让图表、用代码画的笔刷跟着刷新）。</summary>
    public static event Action? Changed;

    public static AppTheme Mode { get; private set; } = AppTheme.Light;

    /// <summary>自定义主题用的主色。</summary>
    public static string Accent { get; private set; } = DefaultAccent;

    public static void ApplyFromConfig()
    {
        var config = AppConfig.Current;

        Apply(config.ThemeMode == "custom" ? AppTheme.Custom : AppTheme.Light,
            config.ThemeSeed, config.ThemeStyle);
    }

    public static void SetTheme(AppTheme mode, string? seed = null, string? style = null)
    {
        var config = AppConfig.Current;

        config.ThemeMode = mode == AppTheme.Custom ? "custom" : "light";

        if (!string.IsNullOrWhiteSpace(seed)) config.ThemeSeed = NormalizeHex(seed);
        if (!string.IsNullOrWhiteSpace(style)) config.ThemeStyle = style;

        config.Save();

        Apply(mode, config.ThemeSeed, config.ThemeStyle);
    }

    public static void Apply(AppTheme mode, string? seedHex = null, string? style = null)
    {
        Mode = mode;
        Seed = NormalizeHex(seedHex);
        Style = MaterialTheme.StyleName(MaterialTheme.ParseStyle(style));

        var colors = mode == AppTheme.Custom
            ? MaterialTheme.Build(ParseColor(Seed), MaterialTheme.ParseStyle(Style), LightPalette())
            : LightPalette();

        Install(BuildDictionary(colors));
    }

    /// <summary>自定义主题的种子色。</summary>
    public static string Seed { get; private set; } = DefaultAccent;

    /// <summary>自定义主题的配色风格（soft / balanced / vibrant / mono）。</summary>
    public static string Style { get; private set; } = "balanced";

    /// <summary>把颜色换算成笔刷字典插到 Application.Resources，替换掉 Palette.xaml。</summary>
    private static void Install(ResourceDictionary theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;

        // 第一次进来时把设计时用的 Palette.xaml 摘掉，之后每次换掉上一次装的那份
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
            // 阴影的 Color 属性要的是 Color，不是 Brush
            if (key is "ShadowColor" or "ShadowColorSoft")
            {
                dictionary[key] = key == "ShadowColor"
                    ? color
                    : colors.TryGetValue("ShadowColorSoft", out var soft) ? soft : color;
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
            // OnLoad + Freeze：读进内存后不再占文件句柄，可以安全共用
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            // 写全程序集名，别依赖「入口程序集就是启动器」这个前提
            image.UriSource = new Uri(
                "pack://application:,,,/MutsumiLauncher;component/Assets/" +
                "background.jpg",
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

    /// <summary>启动时自查：色板必填键是否齐全，缺了会漏底色。</summary>
    public static void Verify()
    {
        try
        {
            var light = LightPalette();
            var required = light.Keys.Where(key => key is not "ShadowColor" and not "ShadowColorSoft").ToList();

            if (required.Count < 40)
                AppLog.Info($"theme: 色板只有 {required.Count} 个键，可能不完整");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "verify theme");
        }
    }

    /// <summary>
    /// 读 Windows 个性化里的主题色（DWM ColorizationColor），失败返回 null。
    /// </summary>
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

    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return DefaultAccent;

        var value = hex.Trim().ToUpperInvariant();
        if (!value.StartsWith('#')) value = "#" + value;

        value = value.Length switch
        {
            7 => "#FF" + value[1..],   // #RRGGBB
            9 => value,                // #AARRGGBB
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
        ["InkOnAccent"] = Hex("#27452F"),

        ["Accent"] = Hex("#8FD8A8"),
        ["AccentDeep"] = Hex("#4E9E72"),
        ["AccentWash"] = Hex("#B8E9C6"),
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

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * amount), 0, 255);

        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static double Luminance(Color color)
        => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    /// <summary>设置页的主题种子色（Material 3 常用主色）。</summary>
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

    /// <summary>给设置页用的推荐主色。</summary>
    public static readonly (string Name, string Hex)[] AccentPresets =
    [
        ("薄荷", "#FF4E9E72"),
        ("松林", "#FF3F8E8A"),
        ("湖蓝", "#FF3F7FBF"),
        ("靛蓝", "#FF5A5FCB"),
        ("紫藤", "#FF8A63C9"),
        ("玫瑰", "#FFC95F86"),
        ("落日", "#FFD07A3C"),
        ("琥珀", "#FFC9A227"),
    ];
}