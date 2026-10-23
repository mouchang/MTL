using System.Windows.Media;

namespace MutsumiLauncher.ViewModels;

/// <summary>版本设置 / 下载搜索里共用的 Mod 卡片数据（占位）。</summary>
public sealed class ModItem
{
    public required string Name { get; init; }

    public string EnglishName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Initial { get; init; } = "M";

    public Brush IconBrush { get; init; } = Brushes.MediumSeaGreen;

    public string Meta { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}

public static class ModSamples
{
    public static IReadOnlyList<ModItem> Installed { get; } =
    [
        new ModItem
        {
            Name = "3D 皮肤层", EnglishName = "Skin Layers 3D", Version = "1.11.4",
            Tags = "冒险 · 装饰",
            Description = "Render the player skin layer in 3d!",
            Initial = "3D", IconBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x8A, 0x5B)),
            Meta = "skinlayers3d-fabric-1.11.4-mc1.21.1",
        },
        new ModItem
        {
            Name = "C^2M 引擎", EnglishName = "C^2M-Engine", Version = "0.4.0-alpha.0.30+1.21.1",
            Tags = "性能优化",
            Description = "A Fabric mod designed to improve the performance of chunk rendering.",
            Initial = "CM", IconBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8C, 0x5A)),
            Meta = "c2me-fabric-mc1.21.1-0.4.0-alpha.0.30",
        },
        new ModItem
        {
            Name = "JEI 物品管理器", EnglishName = "Just Enough Items", Version = "19.69.0.456",
            Tags = "实用 · 支持库",
            Description = "View Items and Recipes.",
            Initial = "JEI", IconBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0xB0, 0x6A)),
            Meta = "jei-1.21.1-fabric-19.69.0.456",
        },
        new ModItem
        {
            Name = "Lambd 的动态光源", EnglishName = "LambDynamicLights", Version = "4.8.11+1.21.1",
            Tags = "冒险 · 实用 · 装饰",
            Description = "Adds dynamic lights to Minecraft.",
            Initial = "LD", IconBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)),
            Meta = "lambdynamiclights-4.8.11+1.21.1",
        },
        new ModItem
        {
            Name = "Xaero 的世界地图", EnglishName = "Xaero's World Map", Version = "1.47.0",
            Tags = "冒险 · 实用 · 运输",
            Description = "Adds a full screen world map.",
            Initial = "XW", IconBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
            Meta = "xaeroworldmap-fabric-1.21.1-1.47.0",
        },
        new ModItem
        {
            Name = "Xaero 的小地图", EnglishName = "Xaero's Minimap", Version = "26.6.0",
            Tags = "实用",
            Description = "Adds a minimap to the screen.",
            Initial = "XM", IconBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x9C, 0xC8)),
            Meta = "xaerominimap-fabric-1.21.1-26.6.0", Enabled = false,
        },
        new ModItem
        {
            Name = "钠", EnglishName = "Sodium", Version = "0.6.13",
            Tags = "性能优化",
            Description = "A high-performance rendering engine replacement for Minecraft.",
            Initial = "Na", IconBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xC4, 0x7F)),
            Meta = "sodium-fabric-0.6.13+mc1.21.1",
        },
        new ModItem
        {
            Name = "Iris 光影", EnglishName = "Iris Shaders", Version = "1.8.8",
            Tags = "装饰 · 性能优化",
            Description = "A modern shader pack loader for Minecraft.",
            Initial = "Ir", IconBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0xE0)),
            Meta = "iris-fabric-1.8.8+mc1.21.1",
        },
    ];

    public static IReadOnlyList<ModItem> SearchResults { get; } =
    [
        new ModItem
        {
            Name = "钠", EnglishName = "Sodium", Version = "Fabric / NeoForge 1.16+",
            Tags = "性能优化",
            Description = "A high-performance rendering engine replacement for Minecraft, which greatly improves frame rates.",
            Initial = "Na", IconBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xC4, 0x7F)),
            Meta = "2.52 亿  ·  7 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Iris Shaders", Version = "Fabric / NeoForge 1.16+",
            Tags = "性能优化 · 装饰",
            Description = "A modern shader pack loader for Minecraft intended to be compatible with existing OptiFine shader packs.",
            Initial = "Ir", IconBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0xE0)),
            Meta = "1.95 亿  ·  23 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "实体渲染机制优化", EnglishName = "Entity Culling", Version = "1.16+, 1.12",
            Tags = "性能优化",
            Description = "Using async path-tracing to hide Block-/Entities that are not visible.",
            Initial = "EC", IconBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x66)),
            Meta = "1.89 亿  ·  14 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Cloth Config API", Version = "Fabric / Forge 1.20+",
            Tags = "支持库",
            Description = "Configuration Library for Minecraft Mods.",
            Initial = "CC", IconBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xB8, 0x5A)),
            Meta = "1.52 亿  ·  2 个月前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Fabric API", Version = "Fabric 1.14+",
            Tags = "支持库",
            Description = "Essential hooks and interop for the Fabric mod loader.",
            Initial = "FA", IconBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)),
            Meta = "8.71 亿  ·  1 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Mod Menu", Version = "Fabric 1.16+",
            Tags = "实用",
            Description = "Adds a mod menu to view the list of mods you have installed.",
            Initial = "MM", IconBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
            Meta = "3.10 亿  ·  9 天前  ·  Modrinth",
        },
    ];
}
