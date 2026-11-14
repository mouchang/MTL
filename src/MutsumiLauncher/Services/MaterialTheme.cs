using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MutsumiLauncher.Services;

/// <summary>
/// 从种子色生成整套配色（参考 FlClash 用 Material 3 的 seededColorScheme 的思路）。
///
/// 只取颜色部分：种子色 → 一组色调（tone 0-100）→ 映射到启动器的各个画刷。
/// 不引入液态玻璃、模糊之类的效果。
/// </summary>
public static class MaterialTheme
{
    /// <summary>配色风格：主色/中性色的彩度不同。</summary>
    public enum Style
    {
        Soft,
        Balanced,
        Vibrant,
        Mono,
    }

    public static Style ParseStyle(string? value) => value switch
    {
        "soft" => Style.Soft,
        "vibrant" => Style.Vibrant,
        "mono" => Style.Mono,
        _ => Style.Balanced,
    };

    public static string StyleName(Style style) => style switch
    {
        Style.Soft => "soft",
        Style.Vibrant => "vibrant",
        Style.Mono => "mono",
        _ => "balanced",
    };

    /// <summary>从种子色算出整套画刷（浅色或深色）。</summary>
    public static Dictionary<string, Color> Build(Color seed, Style style, bool dark,
        IReadOnlyDictionary<string, Color> template, bool pureBlack = false)
    {
        var colors = new Dictionary<string, Color>(template, StringComparer.Ordinal);

        var (hue, _, _) = ToHsl(seed);

        var (primaryChroma, neutralChroma) = style switch
        {
            Style.Soft => (0.30, 0.030),
            Style.Vibrant => (0.62, 0.050),
            Style.Mono => (0.05, 0.000),
            _ => (0.45, 0.040),
        };

        Color Tone(double chroma, int tone) => FromHsl(hue, chroma, tone / 100.0);

        if (dark)
        {
            // Material 深色：主色用高 tone，容器用低 tone，表面近乎黑
            var bgTone = pureBlack ? 0 : 6;

            colors["Accent"] = Tone(primaryChroma, 80);
            colors["AccentDeep"] = Tone(primaryChroma, 80);
            colors["AccentWash"] = Tone(primaryChroma, 30);
            colors["AccentTint"] = Tone(primaryChroma, 22);
            colors["AccentTintDeep"] = Tone(primaryChroma, 28);
            colors["AccentTintSoft"] = Tone(primaryChroma, 18);
            colors["PrimaryFill"] = Tone(primaryChroma, 60);
            colors["PrimaryFillHover"] = Tone(primaryChroma, 68);
            colors["InkOnAccent"] = Tone(primaryChroma, 10);

            colors["WindowBase"] = Tone(neutralChroma, bgTone);
            colors["PaneBg"] = WithAlpha(Tone(neutralChroma, bgTone + 2), 0xF2);
            colors["SurfacePanel"] = WithAlpha(Tone(neutralChroma, bgTone + 6), 0xF5);
            colors["SurfaceFloat"] = WithAlpha(Tone(neutralChroma, bgTone + 9), 0xF8);
            colors["SurfaceSolid"] = Tone(neutralChroma, bgTone + 12);
            colors["SidebarBg"] = WithAlpha(Tone(neutralChroma, bgTone + 6), 0x73);
            colors["AvatarCard"] = WithAlpha(Tone(neutralChroma, bgTone + 10), 0x8C);
            colors["PillBg"] = WithAlpha(Tone(neutralChroma, bgTone + 14), 0x8C);
            colors["ToastBg"] = WithAlpha(Tone(neutralChroma, 20), 0xF0);

            colors["Glass"] = WithAlpha(Tone(neutralChroma, bgTone + 8), 0xB8);
            colors["GlassMedium"] = WithAlpha(Tone(neutralChroma, bgTone + 12), 0xD2);
            colors["GlassStrong"] = WithAlpha(Tone(neutralChroma, bgTone + 16), 0xEC);
            colors["GlassHover"] = WithAlpha(Tone(neutralChroma, bgTone + 18), 0xE0);
            colors["GlassHoverSoft"] = WithAlpha(Tone(neutralChroma, bgTone + 8), 0xB8);

            colors["BackdropVeil"] = WithAlpha(Tone(neutralChroma, bgTone), 0xF5);

            colors["Ink"] = Tone(neutralChroma, 90);
            colors["InkStrong"] = Tone(neutralChroma, 96);
            colors["InkMax"] = Tone(neutralChroma, 100);
            colors["InkSoft"] = Tone(neutralChroma, 72);
            colors["InkMute"] = Tone(neutralChroma, 55);
            colors["InkInverse"] = Tone(neutralChroma, 10);

            colors["GlassStroke"] = WithAlpha(Tone(neutralChroma, 100), 0x33);
            colors["RingStroke"] = WithAlpha(Tone(primaryChroma, 80), 0x59);
            colors["CardBorder"] = WithAlpha(Tone(neutralChroma, 100), 0x26);
            colors["PanelBorderSoft"] = WithAlpha(Tone(neutralChroma, 100), 0x1A);
            colors["WindowBorder"] = WithAlpha(Tone(neutralChroma, 100), 0x2B);
            colors["Divider"] = WithAlpha(Tone(neutralChroma, 100), 0x1F);
            colors["HoverWash"] = WithAlpha(Tone(neutralChroma, 100), 0x14);

            colors["ShadowColor"] = Tone(neutralChroma, 0);
            colors["ShadowColorSoft"] = Tone(neutralChroma, 10);
            colors["ShadowTint"] = Tone(neutralChroma, 0);

            return colors;
        }

        // 主色系
        colors["Accent"] = Tone(primaryChroma, 88);
        colors["AccentDeep"] = Tone(primaryChroma, 40);
        colors["AccentWash"] = Tone(primaryChroma, 84);
        colors["AccentTint"] = Tone(primaryChroma, 95);
        colors["AccentTintDeep"] = Tone(primaryChroma, 89);
        colors["AccentTintSoft"] = Tone(primaryChroma, 97);
        colors["PrimaryFill"] = Tone(primaryChroma, 40);
        colors["PrimaryFillHover"] = Tone(primaryChroma, 32);
        colors["InkOnAccent"] = Tone(primaryChroma, 99);

        // 中性面
        colors["WindowBase"] = Tone(neutralChroma, 98);
        colors["PaneBg"] = WithAlpha(Tone(neutralChroma, 97), 0xF2);
        colors["SurfacePanel"] = WithAlpha(Tone(neutralChroma, 99), 0xF5);
        colors["SurfaceFloat"] = WithAlpha(Tone(neutralChroma, 100), 0xF8);
        colors["SurfaceSolid"] = Tone(neutralChroma, 100);
        colors["SidebarBg"] = WithAlpha(Tone(neutralChroma, 93), 0x73);
        colors["AvatarCard"] = WithAlpha(Tone(neutralChroma, 94), 0x8C);
        colors["PillBg"] = WithAlpha(Tone(neutralChroma, 92), 0x8C);
        colors["ToastBg"] = WithAlpha(Tone(neutralChroma, 12), 0xF0);

        colors["Glass"] = WithAlpha(Tone(neutralChroma, 99), 0xB8);
        colors["GlassMedium"] = WithAlpha(Tone(neutralChroma, 98), 0xD2);
        colors["GlassStrong"] = WithAlpha(Tone(neutralChroma, 97), 0xEC);
        colors["GlassHover"] = WithAlpha(Tone(neutralChroma, 96), 0xE0);
        colors["GlassHoverSoft"] = WithAlpha(Tone(neutralChroma, 99), 0xB8);

        colors["BackdropVeil"] = WithAlpha(Tone(neutralChroma, 98), 0xF2);

        // 文字
        colors["Ink"] = Tone(neutralChroma, 12);
        colors["InkStrong"] = Tone(neutralChroma, 6);
        colors["InkMax"] = Tone(neutralChroma, 0);
        colors["InkSoft"] = Tone(neutralChroma, 32);
        colors["InkMute"] = Tone(neutralChroma, 52);
        colors["InkInverse"] = Tone(neutralChroma, 100);

        // 描边 / 分隔
        colors["GlassStroke"] = WithAlpha(Tone(neutralChroma, 45), 0x33);
        colors["RingStroke"] = WithAlpha(Tone(primaryChroma, 45), 0x59);
        colors["CardBorder"] = WithAlpha(Tone(neutralChroma, 40), 0x26);
        colors["PanelBorderSoft"] = WithAlpha(Tone(neutralChroma, 40), 0x1A);
        colors["WindowBorder"] = WithAlpha(Tone(neutralChroma, 40), 0x2B);
        colors["Divider"] = WithAlpha(Tone(neutralChroma, 40), 0x1F);
        colors["HoverWash"] = WithAlpha(Tone(neutralChroma, 20), 0x14);

        // 阴影
        colors["ShadowColor"] = Tone(neutralChroma, 14);
        colors["ShadowColorSoft"] = Tone(neutralChroma, 30);
        colors["ShadowTint"] = Tone(neutralChroma, 14);

        return colors;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0, s = 0;

        if (Math.Abs(max - min) > 0.0001)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;

            h /= 6;
        }

        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
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

        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        return Color.FromRgb(
            (byte)Math.Round(Channel(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Channel(p, q, h) * 255),
            (byte)Math.Round(Channel(p, q, h - 1.0 / 3) * 255));
    }
}