using System.Windows.Media;

namespace MutsumiLauncher.ViewModels;

/// <summary>mods 目录里真实存在的一个 Mod 文件。</summary>
public sealed class ModFileItem
{
    public required string FileName { get; init; }

    public required string DisplayName { get; init; }

    public required string FullPath { get; init; }

    public string SizeText { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string Initial { get; init; } = "M";

    public Brush IconBrush { get; init; } = Brushes.MediumSeaGreen;

    public string StateText => Enabled ? "已启用" : "已禁用";

    public string ToggleText => Enabled ? "禁用" : "启用";
}
