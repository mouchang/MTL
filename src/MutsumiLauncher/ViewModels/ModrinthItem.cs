using System.ComponentModel;
using System.Windows.Media;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

/// <summary>Modrinth 搜索结果的展示包装。</summary>
public sealed class ModrinthItem : INotifyPropertyChanged
{
    private string? _iconPath;

    public required ModrinthHit Hit { get; init; }

    /// <summary>来源平台。</summary>
    public ResourcePlatform Platform { get; init; } = ResourcePlatform.Modrinth;

    public string ProjectId => Hit.ProjectId;

    public string SourceLabel => Platform == ResourcePlatform.CurseForge ? "CurseForge" : "Modrinth";

    public Brush SourceBrush => Platform == ResourcePlatform.CurseForge
        ? new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x2F))
        : new SolidColorBrush(Color.FromRgb(0x4E, 0x9E, 0x72));

    public string Title => Hit.Title;

    public string Author => Hit.Author;

    public string Description => Hit.Description;

    public string ProjectType => Hit.ProjectType;

    public string? IconUrl => Hit.IconUrl;

    /// <summary>本地缓存后的图标路径；空时界面显示首字母占位。</summary>
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (_iconPath == value) return;
            _iconPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
        }
    }

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
                ? $"{downloads} 次下载 · {SourceLabel}"
                : $"{downloads} 次下载 · {categories} · {SourceLabel}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
