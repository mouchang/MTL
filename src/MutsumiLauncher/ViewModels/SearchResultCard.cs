using System.ComponentModel;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

/// <summary>
/// 聚合搜索里的一张卡片：同一个项目在 Modrinth / CurseForge 的条目合成一张，
/// 顶部两个来源标签切换，卡片主体跟随当前来源刷新。
/// </summary>
public sealed class SearchResultCard : INotifyPropertyChanged
{
    private ModrinthItem _current;

    public SearchResultCard(IEnumerable<ResourceHit> hits)
    {
        Items = hits
            .Select(hit => new ModrinthItem { Hit = hit.Hit, Platform = hit.Platform })
            .ToList();

        if (Items.Count == 0) throw new ArgumentException("至少需要一条结果", nameof(hits));

        _current = Items[0];

        foreach (var item in Items) item.PropertyChanged += OnItemChanged;
    }

    /// <summary>这个项目在各平台的条目，Modrinth 在前。</summary>
    public IReadOnlyList<ModrinthItem> Items { get; }

    /// <summary>当前选中的来源。</summary>
    public ModrinthItem Current => _current;

    public bool HasModrinth => Items.Any(item => item.Platform == ResourcePlatform.Modrinth);

    public bool HasCurseForge => Items.Any(item => item.Platform == ResourcePlatform.CurseForge);

    public bool ModrinthSelected => _current.Platform == ResourcePlatform.Modrinth;

    public bool CurseForgeSelected => _current.Platform == ResourcePlatform.CurseForge;

    public string Title => _current.Title;

    private string? _chineseTitle;

    /// <summary>译名表里的中文名（没有就隐藏这一行）。</summary>
    public string? ChineseTitle
    {
        get => _chineseTitle;
        set
        {
            if (_chineseTitle == value) return;

            _chineseTitle = value;
            foreach (var name in new[]
                     {
                         nameof(ChineseTitle), nameof(ChineseTitleVisibility),
                         nameof(PrimaryTitle), nameof(SecondaryTitle), nameof(SecondaryTitleVisibility),
                     })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public System.Windows.Visibility ChineseTitleVisibility =>
        string.IsNullOrWhiteSpace(_chineseTitle)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    /// <summary>主标题：有中文名时用中文，否则用原名。</summary>
    public string PrimaryTitle =>
        string.IsNullOrWhiteSpace(_chineseTitle) ? _current.Title : _chineseTitle!;

    /// <summary>副标题：有中文名时下面显示原名。</summary>
    public string SecondaryTitle =>
        string.IsNullOrWhiteSpace(_chineseTitle) ? string.Empty : _current.Title;

    public System.Windows.Visibility SecondaryTitleVisibility =>
        string.IsNullOrWhiteSpace(_chineseTitle)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    /// <summary>平台标签（替代原来的简介）。</summary>
    public string TagsText => _current.TagsText;

    public System.Windows.Visibility TagsVisibility =>
        string.IsNullOrWhiteSpace(TagsText)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public string Author => _current.Author;

    public string Description => _current.Description;

    public string Initial => _current.Initial;

    public string Meta => _current.MetaWithoutSource;

    public string? IconUrl => _current.IconUrl;

    public string? IconPath => _current.IconPath;

    /// <summary>切到另一个来源（只切显示，不重新请求）。</summary>
    public void Select(ResourcePlatform platform)
    {
        var target = Items.FirstOrDefault(item => item.Platform == platform);
        if (target is null || ReferenceEquals(target, _current)) return;

        _current = target;

        foreach (var name in new[]
                 {
                     nameof(Current), nameof(ModrinthSelected), nameof(CurseForgeSelected),
                     nameof(Title), nameof(Author), nameof(Description), nameof(Initial),
                     nameof(Meta), nameof(IconUrl), nameof(IconPath),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _current)) return;
        if (e.PropertyName == nameof(ModrinthItem.IconPath))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
