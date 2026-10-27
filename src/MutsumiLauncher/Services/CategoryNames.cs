namespace MutsumiLauncher.Services;

/// <summary>Modrinth/CurseForge 分类 slug → 中文显示名。</summary>
public static class CategoryNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["adventure"] = "冒险", ["cursed"] = "整活", ["decoration"] = "装饰",
        ["economy"] = "经济", ["equipment"] = "装备", ["food"] = "食物",
        ["game-mechanics"] = "游戏机制", ["library"] = "前置库", ["magic"] = "魔法",
        ["management"] = "管理", ["minigame"] = "小游戏", ["mobs"] = "生物",
        ["optimization"] = "优化", ["social"] = "社交", ["storage"] = "存储",
        ["technology"] = "科技", ["transportation"] = "交通", ["utility"] = "实用",
        ["worldgen"] = "世界生成", ["building"] = "建筑", ["combat"] = "战斗",
        ["challenge"] = "挑战", ["environment"] = "环境", ["misc"] = "杂项",
        ["audio"] = "音效", ["gui"] = "界面", ["theme"] = "主题",
        ["font"] = "字体", ["realistic"] = "写实", ["simplistic"] = "简约",
        ["tweaks"] = "微调", ["cartoon"] = "卡通", ["core-shaders"] = "核心光影",
        ["screenshot"] = "截图", ["vanilla-like"] = "原版风", ["modded"] = "模组向",
    };

    public static string Chinese(string slug) =>
        Map.TryGetValue(slug, out var name) ? name : slug;
}
