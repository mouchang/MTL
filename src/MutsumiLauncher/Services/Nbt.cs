using System.IO;
using System.IO.Compression;
using System.Text;

namespace MutsumiLauncher.Services;

/// <summary>
/// 最小 NBT 读取器：只为了从 level.dat 里取出存档名、游戏版本和最后游玩时间。
/// Java 的 NBT 是大端序，所以不能直接用 BinaryReader 的 ReadInt32 之类。
/// </summary>
public static class Nbt
{
    public sealed record LevelMeta(string? Name, string? GameVersion, DateTimeOffset? LastPlayed);

    private const int MaxDepth = 64;
    private const int MaxCollection = 4 * 1024 * 1024;

    /// <summary>Unix 毫秒的合法上限（9999-12-31），用来挡掉损坏的 LastPlayed。</summary>
    private const long MaxUnixMs = 253402300799999L;

    /// <summary>读取 level.dat；文件不存在、损坏或不是 NBT 时返回 null。</summary>
    public static LevelMeta? ReadLevelDat(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            if (reader.ReadByte() != 10) return null;  // 根必须是 compound
            ReadString(reader);                         // 根名字，通常是空串

            if (ReadPayload(reader, 10, 0) is not Dictionary<string, object?> root) return null;
            if (!root.TryGetValue("Data", out var data) || data is not Dictionary<string, object?> level) return null;

            var name = level.TryGetValue("LevelName", out var rawName) ? Clean(rawName as string) : null;

            var gameVersion = level.TryGetValue("Version", out var rawVersion)
                              && rawVersion is Dictionary<string, object?> version
                              && version.TryGetValue("Name", out var rawVersionName)
                ? Clean(rawVersionName as string)
                : null;

            DateTimeOffset? lastPlayed = level.TryGetValue("LastPlayed", out var rawTime)
                                         && rawTime is long ms
                                         && ms > 0
                                         && ms < MaxUnixMs
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

            return new LevelMeta(name, gameVersion, lastPlayed);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "read level.dat");
            return null;
        }
    }

    private static object? ReadPayload(BinaryReader reader, int type, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("NBT 嵌套过深");

        switch (type)
        {
            case 0:
                return null;

            case 1:
                return reader.ReadByte();

            case 2:
                return ReadInt16(reader);

            case 3:
                return ReadInt32(reader);

            case 4:
                return ReadInt64(reader);

            case 5:
                return BitConverter.Int32BitsToSingle(ReadInt32(reader));

            case 6:
                return BitConverter.Int64BitsToDouble(ReadInt64(reader));

            case 7:  // byte array
            {
                var length = ReadInt32(reader);
                Skip(reader, length);
                return null;
            }

            case 8:
                return ReadString(reader);

            case 9:  // list
            {
                var itemType = reader.ReadByte();
                var count = ReadInt32(reader);
                if (count <= 0) return Array.Empty<object?>();
                if (count > MaxCollection) throw new InvalidDataException("NBT 列表过长");

                var items = new List<object?>(Math.Min(count, 256));
                for (var i = 0; i < count; i++) items.Add(ReadPayload(reader, itemType, depth + 1));
                return items;
            }

            case 10:  // compound
            {
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);

                while (true)
                {
                    var childType = reader.ReadByte();
                    if (childType == 0) break;

                    var childName = ReadString(reader);
                    map[childName] = ReadPayload(reader, childType, depth + 1);
                }

                return map;
            }

            case 11:  // int array
            {
                var length = ReadInt32(reader);
                Skip(reader, (long)length * 4);
                return null;
            }

            case 12:  // long array
            {
                var length = ReadInt32(reader);
                Skip(reader, (long)length * 8);
                return null;
            }

            default:
                throw new InvalidDataException($"未知的 NBT 类型 {type}");
        }
    }

    private static short ReadInt16(BinaryReader reader)
        => (short)((reader.ReadByte() << 8) | reader.ReadByte());

    private static int ReadInt32(BinaryReader reader)
        => (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();

    private static long ReadInt64(BinaryReader reader)
        => ((long)ReadInt32(reader) << 32) | (uint)ReadInt32(reader);

    private static string ReadString(BinaryReader reader)
    {
        var length = (reader.ReadByte() << 8) | reader.ReadByte();
        if (length == 0) return string.Empty;

        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void Skip(BinaryReader reader, long count)
    {
        if (count < 0) throw new InvalidDataException("NBT 长度非法");

        var buffer = new byte[64 * 1024];

        while (count > 0)
        {
            var read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read <= 0) throw new EndOfStreamException();
            count -= read;
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
