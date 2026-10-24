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

    public required string DisplayName { get; init; }

    public required string FullPath { get; init; }

    public string SizeText { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string Initial { get; init; } = "M";

    public Brush IconBrush { get; init; } = Brushes.MediumSeaGreen;

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

    public string UpdateText => _update is null ? string.Empty : $"可更新 → {_update.VersionNumber}";

    public Visibility UpdateVisibility => _update is null ? Visibility.Collapsed : Visibility.Visible;

    public void SetUpdate(ModrinthVersion? version) => Update = version;

    public event PropertyChangedEventHandler? PropertyChanged;
}
