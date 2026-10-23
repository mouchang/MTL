namespace MutsumiLauncher.ViewModels;

public sealed class VersionEntry
{
    public required string Name { get; init; }

    public required string Caption { get; init; }

    public required string Icon { get; init; }
}

public sealed class VersionSection
{
    public required string Title { get; init; }

    public required IReadOnlyList<VersionEntry> Versions { get; init; }
}

/// <summary>下载页的静态占位数据，接入版本清单 API 后替换。</summary>
public sealed class DownloadPageViewModel
{
    public IReadOnlyList<VersionEntry> Latest { get; } =
    [
        new VersionEntry
        {
            Name = "26.3",
            Caption = "最新正式版，发布于 2026/9/15 19:23",
            Icon = "/Assets/grass.png",
        },
        new VersionEntry
        {
            Name = "26.4-snapshot-5",
            Caption = "最新预览版，发布于 2026/10/20 20:15",
            Icon = "/Assets/snapshot.png",
        },
    ];

    public IReadOnlyList<VersionSection> Sections { get; } =
    [
        new VersionSection
        {
            Title = "正式版",
            Versions =
            [
                new VersionEntry { Name = "26.3", Caption = "2026/9/15", Icon = "/Assets/grass.png" },
                new VersionEntry { Name = "26.2", Caption = "2026/8/12", Icon = "/Assets/grass.png" },
                new VersionEntry { Name = "26.1", Caption = "2026/6/24", Icon = "/Assets/grass.png" },
                new VersionEntry { Name = "1.21.11", Caption = "2026/3/18", Icon = "/Assets/grass.png" },
            ],
        },
        new VersionSection
        {
            Title = "预览版",
            Versions =
            [
                new VersionEntry { Name = "26.4-snapshot-5", Caption = "2026/10/20", Icon = "/Assets/snapshot.png" },
                new VersionEntry { Name = "26.4-snapshot-4", Caption = "2026/10/13", Icon = "/Assets/snapshot.png" },
                new VersionEntry { Name = "26.4-snapshot-3", Caption = "2026/10/6", Icon = "/Assets/snapshot.png" },
            ],
        },
        new VersionSection
        {
            Title = "远古版",
            Versions =
            [
                new VersionEntry { Name = "b1.7.3", Caption = "Beta · 2011", Icon = "/Assets/grass.png" },
                new VersionEntry { Name = "a1.2.6", Caption = "Alpha · 2010", Icon = "/Assets/grass.png" },
                new VersionEntry { Name = "inf-20100618", Caption = "Infdev · 2010", Icon = "/Assets/grass.png" },
            ],
        },
        new VersionSection
        {
            Title = "愚人节版本",
            Versions =
            [
                new VersionEntry { Name = "25w14craftmine", Caption = "2025 愚人节", Icon = "/Assets/snapshot.png" },
                new VersionEntry { Name = "24w14potato", Caption = "2024 愚人节", Icon = "/Assets/snapshot.png" },
                new VersionEntry { Name = "23w13a_or_b", Caption = "2023 愚人节", Icon = "/Assets/snapshot.png" },
            ],
        },
    ];
}
