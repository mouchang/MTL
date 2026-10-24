using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

/// <summary>Modrinth 搜索结果的展示包装。</summary>
public sealed class ModrinthItem
{
    public required ModrinthHit Hit { get; init; }

    public string ProjectId => Hit.ProjectId;

    public string Title => Hit.Title;

    public string Author => Hit.Author;

    public string Description => Hit.Description;

    public string ProjectType => Hit.ProjectType;

    public string? IconUrl => Hit.IconUrl;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Hit.IconUrl);

    public string Initial => string.IsNullOrEmpty(Hit.Title)
        ? "?"
        : Hit.Title[..1].ToUpperInvariant();

    public string Meta
    {
        get
        {
            var downloads = Hit.Downloads switch
            {
                >= 100_000_000 => $"{Hit.Downloads / 100_000_000.0:0.#} 亿",
                >= 10_000 => $"{Hit.Downloads / 10_000.0:0.#} 万",
                _ => Hit.Downloads.ToString("N0"),
            };

            var categories = string.Join(" · ", Hit.Categories.Take(3));
            return string.IsNullOrEmpty(categories)
                ? $"{downloads} 次下载 · Modrinth"
                : $"{downloads} 次下载 · {categories} · Modrinth";
        }
    }
}
