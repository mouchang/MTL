using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

/// <summary>皮肤处理：下载皮肤、裁出正脸（含帽子层）、按整数倍最近邻放大。</summary>
public static class SkinService
{
    private static readonly HttpClient Http = HttpUtil.Create(TimeSpan.FromSeconds(30));

    public static async Task<string?> EnsureSkinAsync(string skinUrl, string accountKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataRoot, "skins");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, accountKey + ".png");

            if (!File.Exists(path))
            {
                var bytes = await Http.GetByteArrayAsync(skinUrl, cancellationToken);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }

            return path;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "download skin");
            return null;
        }
    }

    /// <summary>
    /// 裁 8×8 正脸 + 帽子层，按最近邻放大到 size。
    /// 自己算像素而不用 WPF 的缩放（RenderOptions 在不同路径下不保证生效，实测会被插值成模糊图）。
    /// </summary>
    public static BitmapSource? CreateFace(string skinPath, int size = 128)
    {
        try
        {
            using var stream = File.OpenRead(skinPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            if (frame.PixelWidth < 64 || frame.PixelHeight < 32) return null;

            var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            var face = ReadBlock(source, 8, 8);

            // 现代皮肤（64×64）才有帽子层，直接盖在脸上
            if (frame.PixelHeight >= 64)
                AlphaBlend(face, ReadBlock(source, 40, 8));

            var pixels = new byte[size * size * 4];
            var stride = size * 4;

            for (var y = 0; y < size; y++)
            {
                var srcY = y * 8 / size;

                for (var x = 0; x < size; x++)
                {
                    var srcX = x * 8 / size;
                    var from = (srcY * 8 + srcX) * 4;
                    var to = y * stride + x * 4;

                    pixels[to] = face[from];
                    pixels[to + 1] = face[from + 1];
                    pixels[to + 2] = face[from + 2];
                    pixels[to + 3] = face[from + 3];
                }
            }

            var result = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "create skin face");
            return null;
        }
    }

    /// <summary>从皮肤里取一个 8×8 方块（BGRA）。</summary>
    private static byte[] ReadBlock(BitmapSource source, int left, int top)
    {
        var stride = 8 * 4;
        var buffer = new byte[stride * 8];
        source.CopyPixels(new Int32Rect(left, top, 8, 8), buffer, stride, 0);
        return buffer;
    }

    /// <summary>把 overlay 按 alpha 叠到 base 上（都是 8×8 BGRA）。</summary>
    private static void AlphaBlend(byte[] basePixels, byte[] overlay)
    {
        for (var i = 0; i < basePixels.Length; i += 4)
        {
            var alpha = overlay[i + 3];

            if (alpha == 0) continue;

            if (alpha == 255)
            {
                basePixels[i] = overlay[i];
                basePixels[i + 1] = overlay[i + 1];
                basePixels[i + 2] = overlay[i + 2];
                basePixels[i + 3] = 255;
                continue;
            }

            var a = alpha / 255.0;
            var inv = 1 - a;

            basePixels[i] = (byte)(overlay[i] * a + basePixels[i] * inv);
            basePixels[i + 1] = (byte)(overlay[i + 1] * a + basePixels[i + 1] * inv);
            basePixels[i + 2] = (byte)(overlay[i + 2] * a + basePixels[i + 2] * inv);
            basePixels[i + 3] = 255;
        }
    }
}
