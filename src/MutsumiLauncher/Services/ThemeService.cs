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

        // 旧配置里的 "dark" 不再有对应主题，按明亮处理
        Apply(config.ThemeMode == "custom" ? AppTheme.Custom : AppTheme.Light,
            config.ThemeAccent, config.ThemeTone, config.ThemeStrength, config.ThemeBackground);
    }

    public static void SetTheme(AppTheme mode, string? accent = null, string? tone = null, string? strength = null, string? background = null)
    {
        var config = AppConfig.Current;

        config.ThemeMode = mode == AppTheme.Custom ? "custom" : "light";

        if (!string.IsNullOrWhiteSpace(accent)) config.ThemeAccent = accent;
        if (!string.IsNullOrWhiteSpace(tone)) config.ThemeTone = tone;
        if (!string.IsNullOrWhiteSpace(strength)) config.ThemeStrength = strength;

        // background 传 null 表示「保持不变」，传空串才是「跟随主色」
        if (background is not null) config.ThemeBackground = background.Trim();

        config.Save();

        Apply(mode, config.ThemeAccent, config.ThemeTone, config.ThemeStrength, config.ThemeBackground);
    }

    public static void Apply(AppTheme mode, string? accentHex, string? tone = null, string? strength = null, string? backgroundHex = null)
    {
        Mode = mode;
        Accent = NormalizeHex(accentHex);
        Tone = NormalizeTone(tone);
        Strength = NormalizeStrength(strength);
        Background = string.IsNullOrWhiteSpace(backgroundHex) ? string.Empty : NormalizeHex(backgroundHex);

        var colors = mode == AppTheme.Custom
            ? BuildCustom(Accent, Tone, Strength, Background)
            : LightPalette();

        Install(BuildDictionary(colors));
    }

    /// <summary>当前自定义色调（neutral / cool / warm / green）。</summary>
    public static string Tone { get; private set; } = "green";

    /// <summary>当前着色浓度（soft / normal / strong）。</summary>
    public static string Strength { get; private set; } = "normal";

    /// <summary>自定义背景色；空表示跟随主色。</summary>
    public static string Background { get; private set; } = string.Empty;

    public static string NormalizeTone(string? tone) => tone switch
    {
        "neutral" or "cool" or "warm" or "green" => tone,
        _ => "green",
    };

    public static string NormalizeStrength(string? strength) => strength switch
    {
        "soft" or "strong" => strength,
        _ => "normal",
    };

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

    /// <summary>
    /// 自定义主题：主色 + 色调 + 浓度，重算整套色板。
    /// 主色决定按钮/高亮，色调决定窗口卡片的冷暖，浓度决定着色轻重。
    /// </summary>
    private static Dictionary<string, Color> BuildCustom(string accentHex, string toneName, string strength, string backgroundHex)
    {
        var colors = LightPalette();
        var accent = ParseColor(accentHex);

        // 主色在浅底上太亮会看不清，压暗一点
        if (Luminance(accent) > 0.72) accent = Blend(accent, Colors.Black, 0.35);

        var tone = ToneColor(accent, toneName);
        var k = StrengthFactor(strength);

        // 主色系
        colors["Accent"] = Blend(accent, Colors.White, 0.42);
        colors["AccentDeep"] = accent;
        colors["AccentWash"] = Blend(accent, Colors.White, 0.28);
        colors["AccentTint"] = Blend(accent, Colors.White, 0.86);
        colors["AccentTintDeep"] = Blend(accent, Colors.White, 0.68);
        colors["AccentTintSoft"] = Blend(accent, Colors.White, 0.93);

        colors["InkOnAccent"] = Blend(accent, Colors.Black, 0.62);
        colors["PrimaryFill"] = Blend(accent, Colors.Black, 0.16);
        colors["PrimaryFillHover"] = Blend(accent, Colors.Black, 0.26);

        // 窗口 / 卡片 / 侧栏：按色调和浓度微微着色，避免「彩色按钮 + 灰界面」
        colors["WindowBase"] = Tint(colors["WindowBase"], tone, 0.10 * k);
        colors["PaneBg"] = Tint(colors["PaneBg"], tone, 0.08 * k);
        colors["SurfacePanel"] = Tint(colors["SurfacePanel"], tone, 0.05 * k);
        colors["SurfaceFloat"] = Tint(colors["SurfaceFloat"], tone, 0.04 * k);
        colors["SidebarBg"] = Tint(colors["SidebarBg"], tone, 0.20 * k);
        colors["AvatarCard"] = Tint(colors["AvatarCard"], tone, 0.10 * k);
        colors["PillBg"] = Tint(colors["PillBg"], tone, 0.10 * k);

        colors["Glass"] = Tint(colors["Glass"], tone, 0.16 * k);
        colors["GlassMedium"] = Tint(colors["GlassMedium"], tone, 0.14 * k);
        colors["GlassStrong"] = Tint(colors["GlassStrong"], tone, 0.10 * k);
        colors["GlassHover"] = Tint(colors["GlassHover"], tone, 0.16 * k);
        colors["GlassHoverSoft"] = Tint(colors["GlassHoverSoft"], tone, 0.16 * k);

        colors["GlassStroke"] = Tint(colors["GlassStroke"], tone, 0.35 * k);
        colors["RingStroke"] = Tint(colors["RingStroke"], tone, 0.45 * k);
        colors["CardBorder"] = Tint(colors["CardBorder"], tone, 0.30 * k);
        colors["PanelBorderSoft"] = Tint(colors["PanelBorderSoft"], tone, 0.30 * k);
        colors["WindowBorder"] = Tint(colors["WindowBorder"], tone, 0.30 * k);
        colors["BackdropVeil"] = Tint(colors["BackdropVeil"], tone, 0.20 * k);
        colors["HoverWash"] = Tint(colors["HoverWash"], tone, 0.25 * k);

        colors["ShadowColor"] = Blend(tone, Colors.Black, 0.72);
        colors["ShadowColorSoft"] = Blend(tone, Colors.Black, 0.55);
        colors["ShadowTint"] = Blend(tone, Colors.Black, 0.72);

        // 指定了背景色：整窗按这个底色重算，并用它压住背景图
        if (!string.IsNullOrWhiteSpace(backgroundHex))
        {
            var bg = ParseColor(backgroundHex);
            var darkBg = Luminance(bg) < 0.45;

            colors["WindowBase"] = bg;
            colors["PaneBg"] = WithAlpha(bg, 0xF2);
            colors["SurfacePanel"] = WithAlpha(Blend(bg, darkBg ? Colors.White : Colors.White, 0.10), 0xF2);
            colors["SurfaceFloat"] = WithAlpha(Blend(bg, Colors.White, 0.16), 0xF7);
            colors["SurfaceSolid"] = Blend(bg, Colors.White, 0.30);
            colors["SidebarBg"] = WithAlpha(Blend(bg, Colors.White, 0.10), 0x66);
            colors["AvatarCard"] = WithAlpha(Blend(bg, Colors.White, 0.18), 0x73);
            colors["PillBg"] = WithAlpha(Blend(bg, Colors.White, 0.12), 0x80);

            colors["Glass"] = WithAlpha(Blend(bg, Colors.White, 0.12), 0x66);
            colors["GlassMedium"] = WithAlpha(Blend(bg, Colors.White, 0.18), 0x8C);
            colors["GlassStrong"] = WithAlpha(Blend(bg, Colors.White, 0.24), 0xC8);
            colors["GlassHover"] = WithAlpha(Blend(bg, Colors.White, 0.20), 0xA6);
            colors["GlassHoverSoft"] = WithAlpha(Blend(bg, Colors.White, 0.08), 0x66);

            // 背景图几乎盖住，只留一点质感
            colors["BackdropVeil"] = WithAlpha(bg, darkBg ? (byte)0xF0 : (byte)0xE8);

            if (darkBg)
            {
                colors["Ink"] = Color.FromRgb(0xEC, 0xF1, 0xEE);
                colors["InkStrong"] = Color.FromRgb(0xF8, 0xFB, 0xF9);
                colors["InkMax"] = Colors.White;
                colors["InkSoft"] = Color.FromRgb(0xB4, 0xC0, 0xBA);
                colors["InkMute"] = Color.FromRgb(0x8A, 0x96, 0x90);
                colors["InkInverse"] = Color.FromRgb(0x12, 0x18, 0x15);
                colors["Divider"] = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
                colors["CardBorder"] = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
                colors["PanelBorderSoft"] = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
                colors["ToastBg"] = Color.FromArgb(0xF0, 0x1A, 0x20, 0x1D);
                colors["ModalScrim"] = Color.FromArgb(0x99, 0, 0, 0);
                colors["HoverWash"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
            }
        }

        return colors;
    }

    /// <summary>按色调给颜色上色，保留原有透明度。</summary>
    private static Color Tint(Color color, Color tone, double amount)
        => Color.FromArgb(color.A, Blend(color, tone, amount).R,
            Blend(color, tone, amount).G, Blend(color, tone, amount).B);

    private static Color ToneColor(Color accent, string tone) => tone switch
    {
        "cool" => Blend(accent, Color.FromRgb(0x5B, 0x8C, 0xBF), 0.60),
        "warm" => Blend(accent, Color.FromRgb(0xD9, 0x8A, 0x4E), 0.60),
        "neutral" => Blend(accent, Color.FromRgb(0x8C, 0x93, 0x99), 0.75),
        _ => accent,
    };

    private static double StrengthFactor(string strength) => strength switch
    {
        "soft" => 0.45,
        "strong" => 1.9,
        _ => 1.0,
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

    /// <summary>设置页的主题预设：一组「主色 + 色调」。</summary>
    public static readonly (string Name, string Accent, string Tone)[] ThemePresets =
    [
        ("薄荷", "#FF4E9E72", "green"),
        ("海蓝", "#FF3F7FBF", "cool"),
        ("暖橙", "#FFC97A33", "warm"),
        ("玫瑰", "#FFC95F86", "warm"),
        ("紫罗兰", "#FF7A63C9", "cool"),
        ("石墨", "#FF5A6570", "neutral"),
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