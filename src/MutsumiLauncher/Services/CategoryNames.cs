namespace MutsumiLauncher.Services;

/// <summary>
/// Modrinth/CurseForge 分类 slug → 中文显示名，顺带把「加载器 / 项目类型」这类噪音标签滤掉。
/// 分类清单来自 https://api.modrinth.com/v2/tag/category（mod / modpack / shader / resourcepack）。
/// </summary>
public static class CategoryNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- Mod ----
        ["adventure"] = "冒险",
        ["cursed"] = "整活",
        ["decoration"] = "装饰",
        ["economy"] = "经济",
        ["equipment"] = "装备",
        ["food"] = "食物",
        ["game-mechanics"] = "游戏机制",
        ["library"] = "前置库",
        ["magic"] = "魔法",
        ["management"] = "管理",
        ["minigame"] = "小游戏",
        ["mobs"] = "生物",
        ["optimization"] = "优化",
        ["social"] = "社交",
        ["storage"] = "存储",
        ["technology"] = "科技",
        ["transportation"] = "交通",
        ["utility"] = "实用",
        ["worldgen"] = "世界生成",
        ["building"] = "建筑",
        ["combat"] = "战斗",
        ["challenge"] = "挑战",
        ["environment"] = "环境",
        ["misc"] = "杂项",

        // ---- 整合包 ----
        ["challenging"] = "挑战",
        ["kitchen-sink"] = "大杂烩",
        ["lightweight"] = "轻量",
        ["multiplayer"] = "多人",
        ["quests"] = "任务",

        // ---- 光影 ----
        ["atmosphere"] = "氛围",
        ["bloom"] = "泛光",
        ["cartoon"] = "卡通",
        ["colored-lighting"] = "彩色光照",
        ["fantasy"] = "奇幻",
        ["foliage"] = "植被",
        ["high"] = "高配",
        ["medium"] = "中配",
        ["low"] = "低配",
        ["potato"] = "超低配",
        ["path-tracing"] = "路径追踪",
        ["pbr"] = "PBR 材质",
        ["realistic"] = "写实",
        ["semi-realistic"] = "半写实",
        ["reflections"] = "反射",
        ["shadows"] = "阴影",
        ["screenshot"] = "截图",
        ["vanilla-like"] = "原版风",

        // ---- 资源包 ----
        ["8x-"] = "8x 以下",
        ["16x"] = "16x",
        ["32x"] = "32x",
        ["48x"] = "48x",
        ["64x"] = "64x",
        ["128x"] = "128x",
        ["256x"] = "256x",
        ["512x+"] = "512x 以上",
        ["audio"] = "音效",
        ["blocks"] = "方块",
        ["core-shaders"] = "核心光影",
        ["entities"] = "实体",
        ["fonts"] = "字体",
        ["font"] = "字体",
        ["gui"] = "界面",
        ["items"] = "物品",
        ["locale"] = "语言",
        ["models"] = "模型",
        ["modded"] = "模组向",
        ["photos"] = "写实照片",
        ["simplistic"] = "简约",
        ["themed"] = "主题化",
        ["theme"] = "主题",
        ["tweaks"] = "微调",
        ["core"] = "核心",
        ["structures"] = "结构",
    };

    /// <summary>
    /// 不该当成「分类」显示的标签：加载器、项目类型本身。
    /// 卡片上一排标签很占位置，Modrinth 的 categories 字段却把加载器也塞进来了。
    /// </summary>
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        // 加载器
        "fabric", "forge", "neoforge", "quilt", "liteloader", "rift", "modloader",
        "iris", "optifine", "canvas", "bukkit", "spigot", "paper", "purpur", "folia",
        // 项目类型 / 平台标记
        "minecraft", "datapack", "mod", "modpack", "resourcepack", "shader", "plugin",
    };

    public static string Chinese(string slug) =>
        Map.TryGetValue(slug, out var name) ? name : slug;

    /// <summary>这个标签值不值得显示（加载器、类型标记都不显示）。</summary>
    public static bool IsVisible(string slug) => !string.IsNullOrWhiteSpace(slug) && !Noise.Contains(slug);

    /// <summary>过滤噪音并翻译，最多取 <paramref name="take"/> 个。</summary>
    public static string VisibleText(IEnumerable<string>? categories, int take)
    {
        if (categories is null) return string.Empty;

        var names = categories
            .Where(IsVisible)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(Chinese)
            .ToList();

        return string.Join(" · ", names);
    }
}
