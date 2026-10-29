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

    /// <summary>不含来源的说明（来源由合并卡片上的标签体现）。</summary>
    public string MetaWithoutSource
    {
        get
        {
            var downloads = Hit.Downloads switch
            {
                >= 100_000_000 => $"{Hit.Downloads / 100_000_000.0:0.#} 亿",
                >= 10_000 => $"{Hit.Downloads / 10_000.0:0.#} 万",
                _ => Hit.Downloads.ToString("N0"),
            };

            return $"{downloads} 次下载";
        }
    }

    public string Meta => $"{MetaWithoutSource} · {SourceLabel}";

    /// <summary>平台上的标签（去掉加载器和项目类型标记，翻译成中文，最多 5 个）。</summary>
    public string TagsText => CategoryNames.VisibleText(Hit.Categories, 5);

    public event PropertyChangedEventHandler? PropertyChanged;
}