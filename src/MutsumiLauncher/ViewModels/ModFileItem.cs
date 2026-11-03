using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MutsumiLauncher.Services;

namespace MutsumiLauncher.ViewModels;

/// <summary>mods 目录里真实存在的一个 Mod 文件。</summary>
public sealed class ModFileItem : INotifyPropertyChanged
{
    private ModrinthVersion? _update;

    public required string FileName { get; init; }

    private string _displayName = string.Empty;

    /// <summary>界面上显示的名字：jar 里的 Mod 名称（有中文名就用中文），不是文件名。</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;

            _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Initial)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBrush)));
        }
    }

    public required string FullPath { get; init; }

    private string _sizeText = string.Empty;

    /// <summary>文件大小。列表先渲染、后台再补，所以是可写的。</summary>
    public string SizeText
    {
        get => _sizeText;
        set
        {
            if (_sizeText == value) return;

            _sizeText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
        }
    }

    private string _loaderText = string.Empty;

    /// <summary>加载器（Fabric / Forge / NeoForge …），后台读 jar 元数据后填入。</summary>
    public string LoaderText
    {
        get => _loaderText;
        set
        {
            if (_loaderText == value) return;

            _loaderText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoaderText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
        }
    }

    private string _versionText = string.Empty;

    /// <summary>Mod 自身声明的版本号。</summary>
    public string VersionText
    {
        get => _versionText;
        set
        {
            if (_versionText == value) return;

            _versionText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
        }
    }

    /// <summary>第二行的组合文本：加载器 · 版本 · 大小（空项自动省略）。</summary>
    public string DetailText => string.Join(" · ",
        new[] { _loaderText, _versionText, SizeText }.Where(text => !string.IsNullOrWhiteSpace(text)));

    public bool Enabled { get; init; }

    public string Initial { get; init; } = "M";

    // 默认透明：加载前就是界面背景，加载后也一致，不会「先有色块再变透明」
    private Brush _iconBrush = Brushes.Transparent;

    /// <summary>
    /// 图标位底色。没有图标时用随机色 + 首字母；有图标时改成白色，
    /// 免得 PNG 的透明区域透出彩色块（看起来像糊了一块背景）。
    /// </summary>
    public Brush IconBrush
    {
        get => _iconBrush;
        set
        {
            if (ReferenceEquals(_iconBrush, value)) return;

            _iconBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBrush)));
        }
    }

    private string? _iconPath;

    /// <summary>Mod 自己的图标（先从 jar 里取，取不到再按 SHA-1 去 Modrinth 找）。</summary>
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (_iconPath == value) return;

            _iconPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InitialVisibility)));
        }
    }

    public Visibility IconVisibility => _iconPath is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InitialVisibility => _iconPath is null ? Visibility.Visible : Visibility.Collapsed;

    public string StateText => Enabled ? "已启用" : "已禁用";

    public string ToggleText => Enabled ? "禁用" : "启用";

    public ModrinthVersion? Update
    {
        get => _update;
        private set
        {
            _update = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Update)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUpdate)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateVisibility)));
        }
    }

    public bool CanUpdate => _update is not null;

    private bool _isSelected;

    /// <summary>多选状态（底部批量操作栏据此显示）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>同一版本的其它下载源（两个平台指向同一个文件时合并，用于失败换源）。</summary>
    public List<string> ExtraDownloadUrls { get; } = [];

    /// <summary>选中状态变化（页面用它刷新底部操作栏）。</summary>
    public event EventHandler? SelectionChanged;

    public string UpdateText => _update is null ? string.Empty : $"可更新 → {_update.VersionNumber}";

    public Visibility UpdateVisibility => _update is null ? Visibility.Collapsed : Visibility.Visible;

    public void SetUpdate(ModrinthVersion? version) => Update = version;

    public event PropertyChangedEventHandler? PropertyChanged;
}