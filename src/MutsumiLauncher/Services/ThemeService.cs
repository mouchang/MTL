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
            config.ThemeHue, config.ThemeSat, config.ThemeLight);
    }

    public static void SetTheme(AppTheme mode, int? hue = null, int? sat = null, int? light = null)
    {
        var config = AppConfig.Current;

        config.ThemeMode = mode == AppTheme.Custom ? "custom" : "light";

        if (hue is not null) config.ThemeHue = hue.Value;
        if (sat is not null) config.ThemeSat = sat.Value;
        if (light is not null) config.ThemeLight = light.Value;

        config.Save();

        Apply(mode, config.ThemeHue, config.ThemeSat, config.ThemeLight);
    }

    public static void Apply(AppTheme mode, int? hue = null, int? sat = null, int? light = null)
    {
        Mode = mode;
        Hue = Math.Clamp(hue ?? 200, 0, 360);
        Sat = Math.Clamp(sat ?? 60, 0, 100);
        Light = Math.Clamp(light ?? 62, 0, 100);

        var colors = mode == AppTheme.Custom ? BuildCustom(Hue, Sat, Light) : LightPalette();

        Install(BuildDictionary(colors));
    }

    /// <summary>自定义主题：色相 0-360。</summary>
    public static int Hue { get; private set; } = 200;

    /// <summary>自定义主题：饱和度 0-100。</summary>
    public static int Sat { get; private set; } = 60;

    /// <summary>自定义主题：明度 0-100（决定窗口底色深浅）。</summary>
    public static int Light { get; private set; } = 62;

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
    /// 自定义主题：按 PCL-CE 的思路用 HSL 生成整套色板——
    /// 色相决定整体色系，饱和度决定鲜艳程度，明度决定窗口底色深浅（低明度自动转深色文字）。
    /// </summary>
    private static Dictionary<string, Color> BuildCustom(int hue, int sat, int light)
    {
        var colors = LightPalette();

        var h = hue % 360 / 360.0;
        var s = sat / 100.0;
        var l = light / 100.0;
        var dark = l < 0.52;

        // 窗口底色：用用户选的明度，饱和度压低一点，避免刺眼
        var bg = HslToRgb(h, s * 0.45, l);

        // 主色：同色相，明度往反方向拉开，保证和背景有对比
        var accentLight = HslToRgb(h, Math.Min(1, s * 1.15), dark ? 0.70 : 0.46);
        var accentDeep = HslToRgb(h, Math.Min(1, s * 1.05), dark ? 0.55 : 0.36);

        var contrast = dark ? Colors.White : Colors.Black;

        colors["Accent"] = Blend(accentLight, contrast, 0.22);
        colors["AccentDeep"] = accentDeep;
        colors["AccentWash"] = Blend(accentLight, bg, 0.45);
        colors["AccentTint"] = Blend(accentDeep, bg, 0.85);
        colors["AccentTintDeep"] = Blend(accentDeep, bg, 0.70);
        colors["AccentTintSoft"] = Blend(accentDeep, bg, 0.92);

        colors["PrimaryFill"] = accentDeep;
        colors["PrimaryFillHover"] = Blend(accentDeep, contrast, 0.12);
        colors["InkOnAccent"] = dark ? Blend(accentLight, Colors.Black, 0.75) : Colors.White;

        // 窗口 / 卡片：按底色分层
        colors["WindowBase"] = bg;
        colors["PaneBg"] = WithAlpha(bg, 0xF2);
        colors["SurfacePanel"] = WithAlpha(Blend(bg, contrast, 0.06), 0xF2);
        colors["SurfaceFloat"] = WithAlpha(Blend(bg, contrast, 0.10), 0xF7);
        colors["SurfaceSolid"] = Blend(bg, contrast, 0.16);
        colors["SidebarBg"] = WithAlpha(Blend(bg, contrast, 0.08), dark ? (byte)0x40 : (byte)0x66);
        colors["AvatarCard"] = WithAlpha(Blend(bg, contrast, 0.12), 0x73);
        colors["PillBg"] = WithAlpha(Blend(bg, contrast, 0.10), 0x80);

        // 卡片更实一点：半透明叠在背景图上会显得发灰
        colors["Glass"] = WithAlpha(Blend(bg, contrast, 0.08), 0xB8);
        colors["GlassMedium"] = WithAlpha(Blend(bg, contrast, 0.12), 0xD2);
        colors["GlassStrong"] = WithAlpha(Blend(bg, contrast, 0.16), 0xEC);
        colors["GlassHover"] = WithAlpha(Blend(bg, contrast, 0.14), 0xE0);
        colors["GlassHoverSoft"] = WithAlpha(Blend(bg, contrast, 0.06), 0xB8);

        // 背景图压到几乎没有，只留一点点纹理
        colors["BackdropVeil"] = WithAlpha(bg, dark ? (byte)0xF7 : (byte)0xF2);

        // 描边 / 分隔线：深色底用白，浅色底用黑
        colors["GlassStroke"] = WithAlpha(contrast, 0x33);
        colors["RingStroke"] = WithAlpha(contrast, 0x59);
        colors["CardBorder"] = WithAlpha(contrast, 0x2A);
        colors["PanelBorderSoft"] = WithAlpha(contrast, 0x1F);
        colors["WindowBorder"] = WithAlpha(contrast, 0x2B);
        colors["Divider"] = WithAlpha(contrast, 0x1F);
        colors["HoverWash"] = WithAlpha(contrast, 0x1A);

        if (dark)
        {
            colors["Ink"] = HslToRgb(h, 0.06, 0.92);
            colors["InkStrong"] = HslToRgb(h, 0.04, 0.97);
            colors["InkMax"] = Colors.White;
            colors["InkSoft"] = HslToRgb(h, 0.08, 0.72);
            colors["InkMute"] = HslToRgb(h, 0.08, 0.55);
            colors["InkInverse"] = HslToRgb(h, 0.10, 0.10);
            colors["ToastBg"] = WithAlpha(Blend(bg, Colors.White, 0.10), 0xF0);
            colors["ModalScrim"] = WithAlpha(Colors.Black, 0x99);
        }
        else
        {
            colors["Ink"] = HslToRgb(h, 0.20, 0.18);
            colors["InkStrong"] = HslToRgb(h, 0.24, 0.12);
            colors["InkMax"] = HslToRgb(h, 0.28, 0.08);
            colors["InkSoft"] = HslToRgb(h, 0.12, 0.36);
            colors["InkMute"] = HslToRgb(h, 0.10, 0.52);
            colors["InkInverse"] = HslToRgb(h, 0.05, 0.97);
        }

        // 阴影
        colors["ShadowColor"] = Blend(bg, Colors.Black, dark ? 0.55 : 0.75);
        colors["ShadowColorSoft"] = Blend(bg, Colors.Black, dark ? 0.35 : 0.55);
        colors["ShadowTint"] = Blend(bg, Colors.Black, dark ? 0.55 : 0.75);

        return colors;
    }

    /// <summary>供设置页画色相渐变用：HSL → RGB。</summary>
    public static Color HslToColor(int hue, double sat, double light)
        => HslToRgb(hue / 360.0, sat, light);

    /// <summary>HSL → RGB（h/s/l 均为 0-1）。</summary>
    private static Color HslToRgb(double h, double s, double l)
    {
        h = (h % 1 + 1) % 1;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        if (s <= 0.0001)
        {
            var v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        return Color.FromRgb(
            (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(HueToRgb(p, q, h) * 255),
            (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3) * 255));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * amount), 0, 255);

        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static double Luminance(Color color)
        => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    /// <summary>设置页的主题预设（色相 / 饱和度 / 明度）。</summary>
    public static readonly (string Name, int Hue, int Sat, int Light)[] ThemePresets =
    [
        ("猫蓝", 200, 60, 22),
        ("天蓝", 205, 70, 62),
        ("青蓝", 185, 55, 45),
        ("樱粉", 330, 45, 72),
        ("琥珀", 35, 70, 60),
        ("石墨", 210, 10, 30),
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