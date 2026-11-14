using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

public enum AppTheme
{
    Light,
    Dark,
    Custom,
}

/// <summary>
/// 主题：明亮 / 暗色 / 自定义。整套色板放在一个 ResourceDictionary 里，
/// 切换时把 Themes/Palette.xaml 那一份整份换掉，界面靠 DynamicResource 自动重绘。
/// </summary>
public static class ThemeService
{
    public const string DefaultAccent = "#FF4E9E72";

    private static ResourceDictionary? _installed;

    /// <summary>主题变了（用于让图表、用代码画的笔刷跟着刷新）。</summary>
    public static event Action? Changed;

    public static AppTheme Mode { get; private set; } = AppTheme.Light;

    /// <summary>当前是不是深色底（自定义主题也会落在这两个之一）。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>自定义主题用的主色。</summary>
    public static string Accent { get; private set; } = DefaultAccent;

    public static void ApplyFromConfig()
    {
        var config = AppConfig.Current;

        Apply(config.ThemeMode switch
        {
            "dark" => AppTheme.Dark,
            "custom" => AppTheme.Custom,
            _ => AppTheme.Light,
        }, config.ThemeAccent, config.ThemeBase == "dark");
    }

    public static void SetTheme(AppTheme mode, string? accent = null, bool darkBase = false)
    {
        var config = AppConfig.Current;

        config.ThemeMode = mode switch
        {
            AppTheme.Dark => "dark",
            AppTheme.Custom => "custom",
            _ => "light",
        };

        if (!string.IsNullOrWhiteSpace(accent)) config.ThemeAccent = accent;
        config.ThemeBase = darkBase ? "dark" : "light";
        config.Save();

        Apply(mode, config.ThemeAccent, darkBase);
    }

    public static void Apply(AppTheme mode, string? accentHex, bool darkBase)
    {
        Mode = mode;
        Accent = NormalizeHex(accentHex);

        IsDark = mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Custom => darkBase,
            _ => false,
        };

        var colors = mode switch
        {
            AppTheme.Dark => DarkPalette(),
            AppTheme.Custom => BuildCustom(IsDark, Accent),
            _ => LightPalette(),
        };

        Install(BuildDictionary(colors, IsDark));
        Changed?.Invoke();
    }

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

    private static ResourceDictionary BuildDictionary(IReadOnlyDictionary<string, Color> colors, bool dark)
    {
        var dictionary = new ResourceDictionary { ["BackdropImage"] = LoadBackdrop(dark) };

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

    /// <summary>窗口底图：深色主题换成压暗过的那张（tools/backgrounds/make-dark.py 生成）。</summary>
    private static ImageSource LoadBackdrop(bool dark)
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
                (dark ? "background-dark.jpg" : "background.jpg"),
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

    /// <summary>启动时自查一次：暗色色板少键的话，切过去会出现浅色漏底，写日志方便发现。</summary>
    public static void Verify()
    {
        try
        {
            var light = LightPalette();
            var dark = DarkPalette();

            var missing = light.Keys.Where(key => !dark.ContainsKey(key)).ToList();
            if (missing.Count > 0)
                AppLog.Info("theme: 暗色色板缺少 " + string.Join(", ", missing));

            var extra = dark.Keys.Where(key => !light.ContainsKey(key)).ToList();
            if (extra.Count > 0)
                AppLog.Info("theme: 暗色色板多出 " + string.Join(", ", extra));
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

    private static Dictionary<string, Color> DarkPalette() => new(StringComparer.Ordinal)
    {
        // 文字：偏中性白，别再带绿
        ["Ink"] = Hex("#E8ECEA"),
        ["InkStrong"] = Hex("#F6F8F7"),
        ["InkMax"] = Hex("#FFFFFF"),
        ["InkSoft"] = Hex("#A6B0AD"),
        ["InkMute"] = Hex("#79837F"),
        ["InkInverse"] = Hex("#101513"),
        ["InkOnAccent"] = Hex("#0B2117"),

        // 主色：薄荷绿只做点缀，暗底上提亮一档
        ["Accent"] = Hex("#7AD7A1"),
        ["AccentDeep"] = Hex("#4FC181"),
        ["AccentWash"] = Hex("#2B4A39"),
        ["AccentTint"] = Hex("#16241C"),
        ["AccentTintDeep"] = Hex("#1E3527"),
        ["AccentTintSoft"] = Hex("#131F19"),
        ["PrimaryFill"] = Hex("#2E9B64"),
        ["PrimaryFillHover"] = Hex("#37B275"),

        // 卡片 / 玻璃层：中性灰蓝，不再发绿
        ["Glass"] = Hex("#1E2427"),
        ["GlassMedium"] = Hex("#242B2F"),
        ["GlassStrong"] = Hex("#2A3237"),
        ["GlassHover"] = Hex("#303A40"),
        ["GlassHoverSoft"] = Hex("#282F34"),
        ["GlassStroke"] = Hex("#33FFFFFF"),
        ["HoverWash"] = Hex("#1AFFFFFF"),
        ["OverlayWash"] = Hex("#14FFFFFF"),
        ["TrackBg"] = Hex("#24FFFFFF"),
        ["ChipBg"] = Hex("#18FFFFFF"),
        ["Divider"] = Hex("#1FFFFFFF"),
        ["RingStroke"] = Hex("#59FFFFFF"),

        // 窗口与浮层
        ["WindowBase"] = Hex("#0E1113"),
        ["WindowChrome"] = Hex("#090C0D"),
        ["WindowBar"] = Hex("#171C1F"),
        ["WindowBorder"] = Hex("#2BFFFFFF"),
        ["PaneBg"] = Hex("#11161A"),
        ["SurfacePanel"] = Hex("#171D21"),
        ["SurfaceFloat"] = Hex("#1B2227"),
        ["SurfaceSolid"] = Hex("#242C31"),
        ["SidebarBg"] = Hex("#161C20"),
        ["AvatarCard"] = Hex("#20272C"),
        ["PillBg"] = Hex("#2A3238"),
        ["ToastBg"] = Hex("#232B30"),
        ["ModalScrim"] = Hex("#80000000"),
        ["Scrim"] = Hex("#40000000"),
        ["BackdropVeil"] = Hex("#73000000"),

        ["CardBorder"] = Hex("#2AFFFFFF"),
        ["PanelBorderSoft"] = Hex("#1FFFFFFF"),
        ["DialogHairline"] = Hex("#1FFFFFFF"),

        ["Danger"] = Hex("#FF8A7A"),
        ["DangerInk"] = Hex("#FF9C8C"),
        ["DangerDeep"] = Hex("#E0685A"),
        ["DangerWash"] = Hex("#3A201C"),
        ["DangerWashSoft"] = Hex("#33C0392B"),
        ["DangerWashBorder"] = Hex("#80C0392B"),
        ["InfoTint"] = Hex("#17272F"),
        ["InfoInk"] = Hex("#9CC8DA"),
        ["SourceGreen"] = Hex("#6FD79C"),
        ["SourceOrange"] = Hex("#E8A45C"),

        ["ShadowTint"] = Hex("#000000"),
        ["ShadowColorSoft"] = Hex("#000000"),
    };

    /// <summary>自定义：在浅色/深色底上换一套主色，其余保持原样。</summary>
    private static Dictionary<string, Color> BuildCustom(bool dark, string accentHex)
    {
        var colors = dark ? DarkPalette() : LightPalette();
        var accent = ParseColor(accentHex);
        var background = colors["WindowBase"];

        // 和背景反差太小的话，把主色往反方向推一点，保证按钮/标题看得见
        var luminance = Luminance(accent);
        if (dark && luminance < 0.35) accent = Blend(accent, Colors.White, 0.45);
        if (!dark && luminance > 0.75) accent = Blend(accent, Colors.Black, 0.35);

        colors["AccentDeep"] = accent;
        colors["Accent"] = Blend(accent, Colors.White, dark ? 0.22 : 0.42);
        colors["AccentWash"] = Blend(accent, background, dark ? 0.35 : 0.45);
        colors["AccentTint"] = Blend(accent, background, dark ? 0.80 : 0.88);
        colors["AccentTintDeep"] = Blend(accent, background, dark ? 0.64 : 0.72);
        colors["AccentTintSoft"] = Blend(accent, background, dark ? 0.87 : 0.93);

        var fill = dark ? Blend(accent, Colors.Black, 0.12) : Blend(accent, Colors.Black, 0.18);
        colors["PrimaryFill"] = fill;
        colors["PrimaryFillHover"] = Blend(fill, Colors.White, 0.12);

        return colors;
    }

    private static Color Hex(string hex)
    {
        if (hex.Length == 6) return ParseColor("#FF" + hex);
        return ParseColor(hex);
    }

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * amount), 0, 255);

        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static double Luminance(Color color)
        => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

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