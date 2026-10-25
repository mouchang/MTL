using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MutsumiLauncher.Services;

/// <summary>
/// 中文搜社区的别名表：Modrinth / CurseForge 只认英文，中文关键词先翻译成英文再搜。
/// 内置一小份常见条目，另可在数据目录放 search-aliases.json 自行扩充（格式：{"中文": ["english", "..."], ...}）。
/// </summary>
public static class SearchAliases
{
    private static readonly Regex ChinesePattern =
        new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["工业时代"] = ["industrial craft"],
        ["工业"] = ["industrial craft", "industry"],
        ["建筑"] = ["buildcraft", "building"],
        ["林业"] = ["forestry"],
        ["铁路"] = ["railcraft", "rail"],
        ["植物魔法"] = ["botania"],
        ["匠魂"] = ["tinkers construct"],
        ["无尽贪婪"] = ["avaritia"],
        ["暮色森林"] = ["twilight forest"],
        ["等价交换"] = ["equivalent exchange", "projecte"],
        ["神秘时代"] = ["thaumcraft"],
        ["应用能源"] = ["applied energistics"],
        ["天地"] = ["ae2"],
        ["更多实用设备"] = ["extra utilities"],
        ["热力膨胀"] = ["thermal expansion"],
        ["工业先锋"] = ["industrial foregoing"],
        ["机械动力"] = ["create"],
        ["星辉魔法"] = ["astral sorcery"],
        ["血魔法"] = ["blood magic"],
        ["开放电脑"] = ["opencomputers"],
        ["管道"] = ["pipe"],
        ["能源"] = ["energy"],
        ["电力"] = ["electric"],
        ["机器"] = ["machine"],
        ["自动化"] = ["automation"],
        ["存储"] = ["storage"],
        ["背包"] = ["backpack", "inventory"],
        ["小地图"] = ["minimap"],
        ["地图"] = ["map"],
        ["光影"] = ["shader"],
        ["着色器"] = ["shader"],
        ["优化"] = ["optimization", "performance"],
        ["性能"] = ["performance"],
        ["钠"] = ["sodium"],
        ["锂"] = ["lithium"],
        ["磷"] = ["phosphor"],
        ["铷"] = ["rubidium"],
        ["家具"] = ["furniture"],
        ["装饰"] = ["decoration"],
        ["食物"] = ["food"],
        ["农业"] = ["farming", "farm"],
        ["武器"] = ["weapon", "combat"],
        ["工具"] = ["tool"],
        ["附魔"] = ["enchant"],
        ["药水"] = ["potion"],
        ["魔法"] = ["magic"],
        ["科技"] = ["tech"],
        ["冒险"] = ["adventure"],
        ["维度"] = ["dimension"],
        ["传送"] = ["teleport", "portal"],
        ["空岛"] = ["skyblock"],
        ["生存"] = ["survival"],
        ["任务"] = ["quest"],
        ["整合包"] = ["modpack"],
        ["皮肤"] = ["skin"],
        ["村庄"] = ["village"],
        ["生物"] = ["mob"],
        ["女仆"] = ["maid"],
        ["灾变"] = ["cataclysm"],
        ["声音"] = ["sound", "audio"],
        ["音乐"] = ["music"],
        ["界面"] = ["gui", "interface"],
        ["盔甲"] = ["armor"],
        ["钓鱼"] = ["fishing"],
        ["种植"] = ["planting", "crop"],
        ["合成"] = ["crafting"],
        ["配方"] = ["recipe"],
        ["区块"] = ["chunk"],
        ["渲染"] = ["render"],
        ["数据包"] = ["datapack"],
        ["资源包"] = ["resourcepack"],
        ["高清修复"] = ["optifine"],
        ["高清"] = ["hd"],
    };

    private static Dictionary<string, string[]>? _merged;

    private static string AliasPath => Path.Combine(AppPaths.DataRoot, "search-aliases.json");

    /// <summary>查询里是否含中文（只有这时候才需要翻译）。</summary>
    public static bool ContainsChinese(string? text)
        => !string.IsNullOrWhiteSpace(text) && ChinesePattern.IsMatch(text);

    /// <summary>
    /// 把中文查询翻成英文候选；翻不出来返回 null。
    /// 先整串匹配，再在查询里找最长的中文关键词。
    /// </summary>
    public static string? Translate(string? query)
    {
        if (!ContainsChinese(query)) return null;

        var text = query!.Trim();
        var table = Table;

        if (table.TryGetValue(text, out var exact) && exact.Length > 0)
            return exact[0];

        string? bestKey = null;

        foreach (var key in table.Keys)
        {
            if (key.Length < 2) continue;
            if (!text.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (bestKey is null || key.Length > bestKey.Length) bestKey = key;
        }

        if (bestKey is null) return null;

        var terms = table[bestKey];
        if (terms.Length == 0) return null;

        // 关键词前后还有内容时拼上去，例如「工业时代2」→「industrial craft 2」
        var index = text.IndexOf(bestKey, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return terms[0];

        var before = text[..index];
        var after = text[(index + bestKey.Length)..];

        if (after.Length > 0 && char.IsAsciiDigit(after[0])) after = " " + after;
        if (before.Length > 0 && char.IsAsciiDigit(before[^1])) before += " ";

        var translated = (before + terms[0] + after).Trim();
        return string.IsNullOrWhiteSpace(translated) ? terms[0] : translated;
    }

    private static Dictionary<string, string[]> Table
    {
        get
        {
            if (_merged is not null) return _merged;

            var table = new Dictionary<string, string[]>(BuiltIn, StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(AliasPath))
                {
                    var custom = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                        File.ReadAllText(AliasPath));

                    if (custom is not null)
                    {
                        foreach (var (key, values) in custom)
                        {
                            if (string.IsNullOrWhiteSpace(key)) continue;
                            if (values is not { Length: > 0 }) continue;

                            table[key.Trim()] = values
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .Select(value => value.Trim())
                                .ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "load search aliases");
            }

            _merged = table;
            return table;
        }
    }
}
