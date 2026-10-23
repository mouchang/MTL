using System.Windows.Media;

namespace MutsumiLauncher.ViewModels;

/// <summary>下载页各社区分类的占位数据。</summary>
public static class CommunitySamples
{
    public static IReadOnlyList<ModItem> ForKind(string kind) => kind switch
    {
        "pack" => Modpacks,
        "datapack" => DataPacks,
        "resourcepack" => ResourcePacks,
        "shader" => Shaders,
        _ => ModSamples.SearchResults,
    };

    private static readonly ModItem[] Modpacks =
    [
        new ModItem
        {
            Name = "猫饼镇整合包", EnglishName = "Cat Town Pack", Version = "Fabric 1.21.1",
            Tags = "冒险 · 休闲 · 轻量",
            Description = "围绕小镇生活与轻冒险搭建的整合包，含 80+ 模组与定制任务线。",
            Initial = "猫", IconBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xC4, 0x7F)),
            Meta = "12.4 万  ·  3 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Fabulously Optimized", Version = "Fabric 1.21.1",
            Tags = "性能优化 · 原版增强",
            Description = "以性能为核心的整合包，保留原版体验的同时大幅提升帧率。",
            Initial = "FO", IconBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8C, 0x5A)),
            Meta = "890 万  ·  1 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "All the Mods 10", Version = "NeoForge 1.21.1",
            Tags = "大型 · 科技 · 魔法",
            Description = "数百个模组的大型整合，科技、魔法、冒险全线覆盖。",
            Initial = "AM", IconBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)),
            Meta = "430 万  ·  5 天前  ·  CurseForge",
        },
        new ModItem
        {
            Name = "Better MC", Version = "Fabric 1.21.1",
            Tags = "冒险 · 优化",
            Description = "在原版基础上补充内容并优化性能的综合整合包。",
            Initial = "BM", IconBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
            Meta = "260 万  ·  2 周前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "RLCraft", Version = "Forge 1.12.2",
            Tags = "硬核 · 生存",
            Description = "高难度生存整合，饥饿、温度、口渴与真实战斗。",
            Initial = "RL", IconBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)),
            Meta = "1.2 亿  ·  1 个月前  ·  CurseForge",
        },
    ];

    private static readonly ModItem[] DataPacks =
    [
        new ModItem
        {
            Name = "Terralith", Version = "1.21.x",
            Tags = "地形 · 世界生成",
            Description = "用原版方块重写世界生成，新增 100+ 生物群系。",
            Initial = "Te", IconBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0xB0, 0x6A)),
            Meta = "3,400 万  ·  6 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Incendium", Version = "1.21.x",
            Tags = "下界 · 结构",
            Description = "重做下界结构与地形，加入全新 Boss 与掉落。",
            Initial = "In", IconBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x5B, 0x3F)),
            Meta = "1,900 万  ·  3 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Nullscape", Version = "1.21.x",
            Tags = "末地 · 世界生成",
            Description = "重塑末地地形，让外岛不再千篇一律。",
            Initial = "Nu", IconBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xC9)),
            Meta = "1,200 万  ·  1 周前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Vanilla Tweaks", Version = "1.21.x",
            Tags = "实用 · 原版",
            Description = "大量可选的小型原版增强数据包合集。",
            Initial = "VT", IconBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xB8, 0x5A)),
            Meta = "980 万  ·  2 天前  ·  Vanilla Tweaks",
        },
    ];

    private static readonly ModItem[] ResourcePacks =
    [
        new ModItem
        {
            Name = "Faithful 32x", Version = "1.21.x",
            Tags = "高清 · 原版风",
            Description = "在原版风格基础上把分辨率提升到 32x。",
            Initial = "F32", IconBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
            Meta = "2,100 万  ·  4 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Fresh Animations", Version = "1.21.x",
            Tags = "动画 · 生物",
            Description = "为生物添加更自然的动作与表情。",
            Initial = "FA", IconBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)),
            Meta = "1,700 万  ·  1 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Stay True", Version = "1.21.x",
            Tags = "原版增强 · 柔和",
            Description = "柔和的原版风格重绘，保留熟悉感。",
            Initial = "ST", IconBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xC4, 0x7F)),
            Meta = "860 万  ·  2 周前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Bare Bones", Version = "1.21.x",
            Tags = "极简 · 卡通",
            Description = "极简卡通风，适合截图与低配设备。",
            Initial = "BB", IconBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xB0, 0x8A)),
            Meta = "640 万  ·  3 周前  ·  Modrinth",
        },
    ];

    private static readonly ModItem[] Shaders =
    [
        new ModItem
        {
            Name = "Complementary Reimagined", Version = "1.21.x",
            Tags = "光影 · 综合",
            Description = "平衡画质与性能的光影，开箱即用。",
            Initial = "CR", IconBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x9E, 0x72)),
            Meta = "1.1 亿  ·  5 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "BSL Shaders", Version = "1.21.x",
            Tags = "光影 · 经典",
            Description = "经典光影，色彩浓郁，配置项丰富。",
            Initial = "BSL", IconBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0xE0)),
            Meta = "9,800 万  ·  2 周前  ·  CurseForge",
        },
        new ModItem
        {
            Name = "Photon", Version = "1.21.x",
            Tags = "光影 · 写实",
            Description = "强调光照与阴影细节的写实光影。",
            Initial = "Ph", IconBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x4C)),
            Meta = "4,300 万  ·  9 天前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "Solas Shader", Version = "1.21.x",
            Tags = "光影 · 轻量",
            Description = "轻量光影，低配设备也能稳定运行。",
            Initial = "So", IconBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
            Meta = "2,700 万  ·  1 个月前  ·  Modrinth",
        },
        new ModItem
        {
            Name = "MakeUp Ultra Fast", Version = "1.21.x",
            Tags = "光影 · 性能",
            Description = "几乎零开销的轻量光影，适合整合包。",
            Initial = "MU", IconBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)),
            Meta = "1,500 万  ·  3 周前  ·  Modrinth",
        },
    ];
}
