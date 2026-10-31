using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

/// <summary>
/// 把图标路径转成按显示尺寸解码的图片。
///
/// 直接绑路径的话，WPF 会把原图（常是 512×512 甚至更大）整张解出来再缩到 40×40，
/// 列表里几十个图标就白吃几十 MB 内存、解码也慢。这里统一按 192px 解码并缓存（高分屏不糊）。
/// </summary>
public sealed class IconSourceConverter : IValueConverter
{
    private const int DecodeWidth = 192;

    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        return Cache.GetOrAdd(path, static key =>
        {
            var image = new BitmapImage();

            try
            {
                image.BeginInit();
                image.UriSource = new Uri(key, UriKind.Absolute);
                image.DecodePixelWidth = DecodeWidth;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.EndInit();
                image.Freeze();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "icon decode " + Path.GetFileName(key));
                return null!;
            }

            return image;
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}