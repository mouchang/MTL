using System.ComponentModel;
using System.Runtime.CompilerServices;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

public sealed class VersionEntry
{
    public required string Name { get; init; }

    public required string Caption { get; init; }

    public required string Icon { get; init; }

    /// <summary>该版本的 JSON 地址（安装时使用）。</summary>
    public string? Url { get; init; }
}

public sealed class VersionSection
{
    public required string Title { get; init; }

    public required IReadOnlyList<VersionEntry> Versions { get; init; }
}

/// <summary>下载页「原版资源」的数据：优先 Mojang 版本清单，失败时回退到内置示例。</summary>
public sealed class DownloadPageViewModel : INotifyPropertyChanged
{
    private const string ReleaseIcon = "/Assets/grass.png";
    private const string SnapshotIcon = "/Assets/command.png";
    private const string FoolsIcon = "/Assets/gold.png";
    private const string AncientIcon = "/Assets/dirt.png";

    private static readonly HashSet<string> AprilFools = new(StringComparer.OrdinalIgnoreCase)
    {
        "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite",
        "22w13oneBlockAtATime", "23w13a_or_b", "24w14potato", "25w14craftmine",
    };

    private readonly VersionManifestService _service = new();

    private IReadOnlyList<VersionEntry> _latest = [];
    private IReadOnlyList<VersionSection> _sections = [];
    private string _status = "正在获取版本清单…";

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VersionEntry> Latest
    {
        get => _latest;
        private set => SetField(ref _latest, value);
    }

    public IReadOnlyList<VersionSection> Sections
    {
        get => _sections;
        private set => SetField(ref _sections, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>拉取真实版本清单；返回是否成功。</summary>
    public async Task<bool> LoadAsync(bool forceRefresh = false)
    {
        Status = "正在获取版本清单…";

        try
        {
            var (manifest, fromNetwork) = await _service.GetAsync(forceRefresh);

            if (manifest.Versions.Count == 0)
            {
                Latest = [];
                Sections = [];
                Status = "版本清单为空，请点右上角刷新重试";
                return false;
            }

            Apply(manifest);
            Status = fromNetwork
                ? $"已从 Mojang 获取版本清单（{DateTime.Now:yyyy/M/d HH:mm}）"
                : "已加载本地缓存的版本清单";
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "load version manifest");

            // 不再显示内置假版本：清单拿不到就明确报错，避免用户点到不存在的版本
            Latest = [];
            Sections = [];
            Status = "获取版本清单失败：" + ex.Message + "（点右上角刷新重试）";
            return false;
        }
    }

    private void Apply(VersionManifest manifest)
    {
        VersionEntry? latestRelease = null;
        VersionEntry? latestSnapshot = null;

        var release = manifest.Versions.FirstOrDefault(v => v.Id == manifest.Latest.Release);
        if (release is not null)
        {
            latestRelease = new VersionEntry
            {
                Name = release.Id,
                Caption = $"最新正式版，发布于 {Format(release.ReleaseTime)}",
                Icon = ReleaseIcon,
                Url = release.Url,
            };
        }

        var snapshot = manifest.Versions.FirstOrDefault(v => v.Id == manifest.Latest.Snapshot);
        if (snapshot is not null)
        {
            latestSnapshot = new VersionEntry
            {
                Name = snapshot.Id,
                Caption = $"最新预览版，发布于 {Format(snapshot.ReleaseTime)}",
                Icon = SnapshotIcon,
                Url = snapshot.Url,
            };
        }

        Latest = new[] { latestRelease, latestSnapshot }.Where(v => v is not null).Cast<VersionEntry>().ToList();

        var releases = manifest.Versions.Where(v => v.Type == "release");
        var snapshots = manifest.Versions
            .Where(v => v.Type == "snapshot" && !AprilFools.Contains(v.Id));
        var ancient = manifest.Versions
            .Where(v => v.Type is "old_alpha" or "old_beta");
        var fools = manifest.Versions
            .Where(v => v.Type == "snapshot" && AprilFools.Contains(v.Id));

        Sections =
        [
            Section("正式版", releases, ReleaseIcon),
            Section("预览版", snapshots, SnapshotIcon),
            Section("远古版", ancient, AncientIcon),
            Section("愚人节版本", fools, FoolsIcon),
        ];
    }

    private static VersionSection Section(string title, IEnumerable<MinecraftVersion> versions, string icon)
        => new()
        {
            Title = title,
            Versions = versions
                .Select(v => new VersionEntry
                {
                    Name = v.Id,
                    Caption = Format(v.ReleaseTime),
                    Icon = icon,
                    Url = v.Url,
                })
                .ToList(),
        };

    private static string Format(DateTimeOffset time) => time.LocalDateTime.ToString("yyyy/M/d");

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
