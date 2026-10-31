using System.IO;

namespace MutsumiLauncher.Services;

/// <summary>
/// CurseForge 文件指纹：先去掉空白字节（0x9 / 0xA / 0xB / 0xC / 0xD / 0x20），
/// 再对剩下的字节做 murmur2（seed = 1）。和 CurseForge 客户端、PCL2 的算法一致。
/// </summary>
public static class CurseForgeFingerprint
{
    private const uint M = 0x5bd1e995;
    private const int R = 24;

    public static uint OfFile(string path)
    {
        try
        {
            var raw = File.ReadAllBytes(path);
            var filtered = new byte[raw.Length];
            var count = 0;

            foreach (var b in raw)
            {
                if (b is 0x9 or 0xA or 0xB or 0xC or 0xD or 0x20) continue;
                filtered[count++] = b;
            }

            return Murmur2(filtered, count);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "fingerprint " + Path.GetFileName(path));
            return 0;
        }
    }

    private static uint Murmur2(byte[] data, int length)
    {
        var hash = 1u ^ (uint)length;
        var i = 0;
        var remaining = length;

        while (remaining >= 4)
        {
            var k = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));

            k *= M;
            k ^= k >> R;
            k *= M;

            hash *= M;
            hash ^= k;

            i += 4;
            remaining -= 4;
        }

        switch (remaining)
        {
            case 3:
                hash ^= (uint)data[i + 2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)data[i + 1] << 8;
                goto case 1;
            case 1:
                hash ^= data[i];
                hash *= M;
                break;
        }

        hash ^= hash >> 13;
        hash *= M;
        hash ^= hash >> 15;

        return hash;
    }
}
