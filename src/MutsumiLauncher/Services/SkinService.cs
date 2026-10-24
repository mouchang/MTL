using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MutsumiLauncher.Services;

/// <summary>皮肤处理：下载皮肤、裁出正脸（含帽子层）、缩放到头像尺寸。</summary>
public static class SkinService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
        => HttpUtil.Create(TimeSpan.FromSeconds(30));

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

    /// <summary>裁剪 8×8 正脸 + 帽子层，放大到 size（最近邻，保持像素感）。</summary>
    public static BitmapSource? CreateFace(string skinPath, int size = 128)
    {
        try
        {
            using var stream = File.OpenRead(skinPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            if (frame.PixelWidth < 64 || frame.PixelHeight < 32) return null;

            var face = new CroppedBitmap(frame, new Int32Rect(8, 8, 8, 8));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                dc.DrawImage(face, new Rect(0, 0, 8, 8));

                // 现代皮肤（64×64）才有帽子层
                if (frame.PixelHeight >= 64)
                {
                    var hat = new CroppedBitmap(frame, new Int32Rect(40, 8, 8, 8));
                    dc.DrawImage(hat, new Rect(0, 0, 8, 8));
                }
            }

            var faceBitmap = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
            faceBitmap.Render(visual);
            faceBitmap.Freeze();

            var scaledVisual = new DrawingVisual();
            using (var dc = scaledVisual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(scaledVisual, BitmapScalingMode.NearestNeighbor);
                dc.DrawImage(faceBitmap, new Rect(0, 0, size, size));
            }

            var result = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            result.Render(scaledVisual);
            result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "create skin face");
            return null;
        }
    }
}
