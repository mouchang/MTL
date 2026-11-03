# 更新日志

本项目从 0.1.0 开始记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.3-beta.19] - 2026-11-03

### 更新内容

- 「信息」按钮改为直达 MC百科详情页；索引里找不到时才退回搜索

### 技术实现

和资源模块、PCL2 的行为对齐：拿到百科 ID 就直接跳 `class/{id}.html`。

```csharp
var url = McmodIndex.FindExact(null, item.DisplayName) is { } match
    ? $"https://www.mcmod.cn/class/{match.Id}.html"
    : "https://www.mcmod.cn/s?key=" + Uri.EscapeDataString(item.DisplayName);
```

为此新增 `FindExact`：只做标题 / slug / 简称的精确查找，不走模糊匹配。
宁可退回搜索，也不要因为模糊命中把用户带到别的 Mod 页面。

C2ME 的特例也随之下线——它的英文名在索引里有斜杠别名，精确查找直接命中 id 3511。
## [1.1.3-beta.18] - 2026-11-03

### 更新内容

- 「信息」按钮只对 C2ME 用简称搜索，其余 Mod 一律全名

### 技术实现

去掉了按百科别名规则判断的通用逻辑，只保留这一条特例：

```csharp
if (keyword.Contains("Chunk Management Engine", StringComparison.OrdinalIgnoreCase))
    keyword = "C2ME";
```

不再改索引，也不影响其它任何 Mod 的搜索关键词。
## [1.1.3-beta.17] - 2026-11-03

### 更新内容

- 「信息」按钮只对 C2ME 这类复合英文名用简称，其它 Mod 仍搜全名

### 技术实现

上一版对所有命中百科索引的 Mod 都优先用简称，导致搜索关键词变成 `Sodium` 以外的短词，
反而搜不到。改成只在百科英文名是「短名/全名」复合写法时才用简称：

```csharp
if (McmodIndex.Find(null, item.DisplayName) is { Abbr: { Length: > 0 } abbr } match
    && match.English.Contains('/'))
{
    keyword = abbr;
}
```

C2ME 的百科英文名是 `C^2M-Engine/Concurrent Chunk Management Engine`，命中这条规则；
其余 Mod 保持原来的全名搜索。
## [1.1.3-beta.16] - 2026-11-03

### 更新内容

- 行内按钮不再挤成一团：补上内边距、最小宽度与间距

### 技术实现

上一版的胶囊模板忘了把 `Padding` 传给 Border，文字直接贴边，
四个按钮自然挤在一起：

```xml
<Border CornerRadius="10" Padding="{TemplateBinding Padding}" …>
```

同时把高度 30→32、最小宽度 56、按钮间距 8→10。
## [1.1.3-beta.15] - 2026-11-03

### 更新内容

- 图标占位不再先显示色块再变透明，从第一帧就是界面背景
- 「信息」按钮改用百科简称搜索（C2ME 这类全名搜不到）
- 百科索引支持斜杠别名，英文名写成「短名/全名」也能匹配
- 行内按钮换成浅色胶囊样式，删除按钮单独用红字

### 技术实现

#### 1. 图标底色

占位底色原来是随机色，图标加载后切成透明——中间会闪一下色块。
改成默认就透明：

```csharp
private Brush _iconBrush = Brushes.Transparent;
```

首字母从白色改成 `InkMute`（透明底上白字看不见）。搜索卡片的占位同样去掉 `AccentTint`。

#### 2. 百科简称

索引里 C2ME 的英文名是 `C^2M-Engine/Concurrent Chunk Management Engine`，
归一化后拼成一个长串，和 Mod 元数据里的名字对不上。两处改动：

- 建索引时按 `/` 拆别名：`foreach (var alias in entry.English.Split('/', '|'))`
- 「信息」按钮优先用命中的简称：`match.Abbr`，其次中文名

现在 C2ME 的「信息」会直接搜 `C2ME`。

#### 3. 行内按钮

新增 `RowActionButton`：白底、1px 描边、圆角 9，悬停变主色浅底 + 主色字；
删除用 `RowDangerButton`（红字）。
## [1.1.3-beta.14] - 2026-11-03

### 更新内容

- 修复部分 Mod 仍显示文件名（元数据 JSON 带裸换行导致解析失败）
- 图标底色改为透明，界面背景直接透出；资源搜索页同步处理
- 多文件同时下载时按活跃文件数分摊连接，第三个下载不再「点了没反应」

### 技术实现

#### 1. 元数据解析

`entity_texture_features`、`EuphoriaPatcher` 的 `fabric.mod.json` 里 `description` 带裸换行，
`JsonDocument.Parse` 直接抛 `JsonReaderException`，`Read` 返回 null，界面退回文件名。

严格解析放进 try，失败后走正则兜底：

```csharp
try { using var doc = JsonDocument.Parse(stream); … }
catch (JsonException) { if (ReadLenient(archive) is { } info) return info; }
```

实测三个原本失败的 jar 都能读出名字与版本。

#### 2. 图标背景

底色从白色改成透明：

```csharp
item.IconBrush = Brushes.Transparent;
```

搜索卡片同理，新增 `PlaceholderVisibility`：有图标就把占位色块折叠，
透明区域显示的是页面背景而不是色块。

#### 3. 下载排队

引擎的全局槽位是 16，直连模式每个文件按 8 段下载——两个文件就占满，
第三个点击后它的分段全在 `Slots.WaitAsync` 里等着，看起来像没反应。

改成按活跃文件数分摊：

```csharp
var activeFiles = Math.Max(1, Interlocked.Increment(ref _activeFiles));
var pieceCap = Math.Clamp(MaxThreads / activeFiles, 2, tuning.MaxPieces);
```

同时把 `MaxThreads` 提到 24，并保证退出时递减计数。同一时刻的文件越多，
每个文件分的段越少，但都不会被完全饿死。
## [1.1.3-beta.13] - 2026-11-03

### 更新内容

- 更新 Mod 时页面内显示进度条（第几个 / 百分比），不再只靠角落浮标
- 修复删除 Mod 需要确认两次
- 中文名匹配支持去掉「(Fabric)」这类后缀
- 元数据损坏的 Mod 用宽松解析取名字，不再显示文件名
- 有图标的 Mod 改用白底，透明区域不再透出随机色块

### 技术实现

#### 1. 页面内进度条

`DownloadCenter` 的浮标只在右下角闪一下，小文件几乎看不到。
在列表下方加了一行独立进度：

```csharp
var overall = (index + sub / 100.0) * 100 / targets.Count;
SetUpdateProgress(overall, "正在更新 …（2/3 · 47%）");
```

#### 2. 删除确认两次

上一次修「删除前确认」时，页面里已经有一处 `MessageDialog.Show`，
我又加了一个 `MessageBox`，于是要点两次。删掉后加的那个。

#### 3. 中文名

`McmodIndex.Find` 增加 `StripQualifiers`：循环剥离末尾的 `(Fabric)` `（Forge）` `[1.20.1]`，
只剥「加载器 / 版本号 / snapshot」这类噪声，真正的副标题不动。

#### 4. 文件名兜底

部分 Mod 的 `fabric.mod.json` 里有裸换行或注释，`JsonDocument` 直接抛异常。
新增 `ReadLenient`：正则捞 `name` / `version`，放在严格解析之后作为兜底。

#### 5. 图标背景

图标位底色原本是随机色 + 首字母；PNG 带透明通道时会透出这块彩色。
现在一旦取到图标就把底色改成白色。
## [1.1.3-beta.12] - 2026-11-03

### 更新内容

- 更新 Mod 时在下载面板显示进度（第几个 / 百分比 / 速度）
- 更新完成后自动回到「全部」，更新按钮与徽标立即消失
- 下载速度改用 3 秒窗口采样，不再跳来跳去

### 技术实现

#### 1. 更新进度

`ApplyUpdateAsync` 新增 `IProgress<DownloadProgress>` 参数，直接透传给 `DownloadEngine`；
批量和单条都包一层 `DownloadCenter` 任务：

```csharp
var sub = p.Total > 0 ? p.Received * 100.0 / p.Total : 0;
var overall = (index + sub / 100.0) * 100 / targets.Count;
DownloadCenter.Report(task, $"正在更新 {item.DisplayName}（{captured}/{targets.Count}）", overall, p.Received);
```

#### 2. 更新后筛选与按钮

原来更新成功只删缓存，`item.Update` 还在，按钮不消失、计数也不变。现在：

```csharp
item.ExtraDownloadUrls.Clear();
item.SetUpdate(null);        // 通知 UpdateVisibility / CanUpdate
RemoveCachedUpdate(item);
```

新增 `SyncUpdateFilter()`：按列表实时重算「可更新 (N)」，为 0 时隐藏标签并切回「全部」。

#### 3. 速度显示

对照 PCL2 的 `RefreshStat`（保留 30 个样本加权平均），改成 3 秒窗口采样：

```csharp
GlobalSpeedMb = span >= 0.5 ? Math.Max(0, total - first.Bytes) / 1048576.0 / span : 0;
```

面板定时器从 1 秒改成 250ms，表头优先用全局速度；任务行速度做指数平滑（0.6 旧 + 0.4 新）。
## [1.1.3-beta.11] - 2026-11-03

### 更新内容

- 修复检查更新结束后遮罩不消失（界面一直卡在「检查更新中」）
- 修复提示里的「未找到来源」数量恒等于总数

### 技术实现

并行化那次改动里漏了两处：

**1. `StopBusy()` 被一起删掉了**

替换代码块时从 toast 一直删到 `SaveUpdateCache()`，而原来的 `StopBusy()`
正好夹在中间，于是检查完遮罩不收起，看起来像「还没结束」。
现在放进 `finally`，成功失败都会收起：

```csharp
try
{
    await Task.WhenAll(modrinthTask, curseForgeTask);
    (found, matchedFiles) = MergeUpdates(...);
}
finally
{
    StopBusy();
}
```

**2. `matchedFiles` 变量被删了但引用还在**

它一直保持 0，`unknown = total - 0` 就等于全部 Mod 数，
于是提示变成「发现 2 个可更新（54 个未找到来源）」——数字对不上。
`MergeUpdates` 现在返回 `(Found, Matched)`，用命中集合去重计数。
## [1.1.3-beta.10] - 2026-11-03

### 更新内容

- Modrinth 与 CurseForge 并行查询，检查耗时减半
- 本地哈希加缓存，同一批文件重复检查几乎瞬时

### 技术实现

日志显示 54 个 Mod 的耗时分布：

```
21:24:53 check updates: hashed=54 fingerprints=54
21:24:56 modrinth update check: 2/54 命中    ← 3 秒
21:24:57 curseforge update check: 52/54 命中  ← 1 秒
```

本地哈希已经很快，瓶颈是两轮网络串行等待。

#### 1. 两个平台并行

对照 PCL2：它的 Modrinth 与 CurseForge 查询跑在两个独立线程里（`RunInNewThread`），
我们原来是 `await` 完 Modrinth 才发 CurseForge。改成：

```csharp
var modrinthTask = Task.Run(() => modrinth.CheckUpdatesIndividualAsync(queries));
var curseForgeTask = Task.Run(() => CheckCurseForgeAsync(curseForge, fingerprintMap, info));

await Task.WhenAll(modrinthTask, curseForgeTask);
```

结果合并放到 UI 线程做（`MergeUpdates`），避免跨线程改绑定属性。

#### 2. 哈希缓存

PCL2 把 MurmurHash 写进 `Cache\ModHash.ini`，按「文件名 + 修改时间 + 大小」缓存。
我们用内存版，键是「路径 + 大小 + 修改时间」，命中就不用再读一遍 jar。
第一次约 0.3 秒，之后重复检查基本瞬时。
## [1.1.3-beta.9] - 2026-11-03

### 更新内容

- 修复启动崩溃（`Item has already been added. Key: MtlCheckBox`）

### 技术实现

上一版为多选框新增了 `MtlCheckBox` 样式，但 **Styles.xaml 里本来就有同名样式**——
资源字典在加载时抛 `ArgumentException`，App.InitializeComponent 直接失败，
启动只剩一个错误弹窗。

删除重复定义，沿用原有的 `MtlCheckBox`（18px、圆角 5、主色填充 + 白色勾选图标）。
本地构建不会报错，只有运行时合并资源字典才暴露，所以这次以启动日志为准。
## [1.1.3-beta.8] - 2026-11-03

### 更新内容

- 检查更新明显变快（哈希并行、加载器读缓存）
- 多选框换成圆角样式，不再是系统方框
- 移除「含测试版」开关，更新检查默认包含测试版

### 技术实现

#### 1. 提速

两处瓶颈：

- 哈希（SHA-1 + Murmur2）在 Task.Run 里串行跑，几百个 jar 累计可观
- `update_individual` 的 loaders 参数每个文件重新 `ZipFile.OpenRead` 一次，等于又扫了一遍全部 jar

```csharp
Parallel.ForEach(_items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, item =>
{
    sha1Map.TryAdd(Sha1Of(item.FullPath), item);
    fingerprintMap.TryAdd(CurseForgeClient.ComputeFingerprint(item.FullPath), item);
});
```

loaders 改读元数据缓存：

```csharp
var loader = ReadModMeta(item.FullPath).Loader;   // 命中缓存，不重新解压
```

#### 2. 多选框

新增 `MtlCheckBox` 样式：22px 圆角方块，选中时填充主色 + 白色对勾，悬停描边加深。
模板里只有 `Border` + `Path`，没有系统控件那层灰色边框。

#### 3. 测试版

去掉工具栏复选框与 `AppConfig.IncludePrereleaseUpdates`，`IncludeVersion` 恒为 true，
检查更新默认把 beta / alpha 也算进候选。
## [1.1.3-beta.7] - 2026-11-03

### 更新内容

- beta 版的桌面快捷方式与开始菜单改名为「Mutsumi Launcher beta」，不再和正式版冲突

### 技术实现

安装器新增编译期开关 `APP_CHANNEL`：

```nsis
!ifndef APP_CHANNEL
  !define APP_CHANNEL ""
!endif
!if "${APP_CHANNEL}" == "beta"
  !define DISPLAY_NAME "${APP_NAME} beta"
!else
  !define DISPLAY_NAME "${APP_NAME}"
!endif
```

快捷方式与开始菜单目录统一用 `${DISPLAY_NAME}`，安装段和卸载段各引用一次，
保证卸载时删的是同一个名字。

构建时按版本号决定通道：

```bash
makensis /DAPP_VERSION=1.1.3-beta.7 /DAPP_CHANNEL=beta installer/MutsumiLauncher.nsi
```

CI 里自动判断：tag 含 `-`（如 `v1.2.0-beta.1`）即为 beta 通道，正式版不带后缀。
## [1.1.3-beta.6] - 2026-11-03

### 更新内容

- 更新时合并两个平台的下载源，主源失败自动换另一个
- 检查更新新增「含测试版」开关，默认只提示正式版

### 技术实现

#### 1. 多下载源合并

同一个 Mod 版本经常在 Modrinth 与 CurseForge 各有一份（SHA-1 相同或文件名相同）。
检查阶段把两边的地址按 `sha1:` / `name:` 归组：

```csharp
return file.Hashes?.Sha1 is { Length: > 0 } sha1
    ? "sha1:" + sha1
    : "name:" + file.Filename.ToLowerInvariant();
```

下载时把它们连同 `DownloadSources.ModFile` 的镜像候选一起交给引擎：

```csharp
var urls = new List<string> { file.Url };
urls.AddRange(item.ExtraDownloadUrls);
urls.AddRange(DownloadSources.ModFile(file.Url).Select(e => e.Url));
await DownloadEngine.DownloadAsync(urls.Distinct(…).ToList(), target, sha1, size);
```

#### 2. 发布通道过滤

`AppConfig.IncludePrereleaseUpdates` 默认 `false`，检查时按 `VersionType` 过滤：

```csharp
private static bool IncludeVersion(string? type)
    => AppConfig.Current.IncludePrereleaseUpdates
       || string.IsNullOrWhiteSpace(type)
       || type.Equals("release", StringComparison.OrdinalIgnoreCase);
```

工具栏新增「含测试版」复选框，勾选立即写盘并影响下一次检查。
## [1.1.3-beta.5] - 2026-11-03

### 更新内容

- 打开 Mod 管理时列表立即出现，大小与版本信息在后台补全

### 技术实现

#### 1. 扫描阶段不再读文件大小

之前 `Refresh()` 对每个 jar 做一次 `new FileInfo(file).Length`，
两百个 Mod 就是两百次同步 I/O，界面要等这一圈才出来。
现在扫描只取文件名，大小留空：

```csharp
_items.Add(new ModFileItem { FileName = name, FullPath = file, Enabled = enabled, … });
```

渲染完再后台补：

```csharp
private static async Task FillSizesAsync(IReadOnlyList<ModFileItem> items)
{
    using var semaphore = new SemaphoreSlim(8);
    …
    Application.Current?.Dispatcher.Invoke(() => item.SizeText = text);
}
```

`SizeText` 因此从 `init` 改成可写属性，赋值时同时通知 `DetailText`。

#### 2. 更新缓存键改为文件名

缓存键原本是「文件名 + 大小」，但异步补大小意味着渲染那一刻还没有 size，
按 `KeyOf(name, size)` 根本查不到。改成只用文件名——同一个 mods 目录里文件名本来就唯一：

```csharp
public static string KeyOf(string fileName) => fileName.ToLowerInvariant();
```

`Entry.Size` 仍保留（写盘时若是更新阶段再读一次），只作展示用。
## [1.1.3-beta.4] - 2026-11-03

### 更新内容

- Mod 列表第二行显示「加载器 · 版本号 · 大小」
- 元数据带缓存，重复刷新不重复读 jar

### 技术实现

`ModFileItem` 增加 `LoaderText` / `VersionText`，并用组合属性输出第二行：

```csharp
public string DetailText => string.Join(" · ",
    new[] { _loaderText, _versionText, SizeText }.Where(t => !string.IsNullOrWhiteSpace(t)));
```

`LoadMetadataAsync` 原本只读显示名，现在一并读版本号与加载器：

```csharp
version = ModMetadata.Read(path)?.Version;
loader  = ModMetadata.LoadersOf(path) switch { "fabric" => "Fabric", "forge" => "Forge", … };
```

缓存键带上大小与修改时间，文件没变就不重复解压：

```csharp
key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
```

实测 6 个 Fabric Mod 都能读到版本与加载器（1 个 jar 内 `fabric.mod.json` 本身损坏，只显示加载器）。
## [1.1.3-beta.3] - 2026-11-03

### 更新内容

- Mod 列表支持多选，底部出现批量操作栏（启用 / 禁用 / 更新 / 删除 / 取消）
- 删除 Mod 前弹确认框（单条与批量都有）
- 每行新增「位置」（资源管理器定位）与「信息」（MC百科搜索）
- 「启用」「禁用」筛选项按数据自动显隐

### 技术实现

#### 1. 多选

`ModFileItem` 增加 `IsSelected` 与 `SelectionChanged` 事件，行首绑定复选框：

```xml
<CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" />
```

`Refresh()` 时为每个条目挂事件，选中变化即刷新底部栏；`RefreshSelectionBar()` 按选中项
的实际状态决定按钮可用性（例如选中的都启用时，「启用」置灰）。

#### 2. 批量操作

- 启用/禁用：批量改扩展名（`.jar` ↔ `.jar.disabled`），与单条逻辑一致
- 更新：复用单条的 `ApplyUpdateAsync`，逐个执行并统计
- 删除：先确认，再逐个删除并统计失败数

#### 3. 行内操作

```csharp
Process.Start(new ProcessStartInfo { FileName = "explorer.exe",
    Arguments = $"/select,\"{item.FullPath}\"", UseShellExecute = true });
```

「信息」在没有项目 ID 的情况下按名称去 MC百科搜索（PCL2 的兜底做法）。
## [1.1.3-beta.2] - 2026-11-03

### 更新内容

- 检查更新改为逐文件判断加载器，多加载器实例不再误判
- Mod 列表新增「日志」按钮，直接打开该更新的更新日志
- 更新信息保存时会带上日志地址

### 技术实现

#### 1. 逐文件 loaders（对齐 PCL2 的 update_individual）

之前整批用一个实例级 loader 过滤，混装实例（同一目录里 Fabric 与 Forge Mod 并存）
会互相误判。现在每个文件先读自己属于哪个加载器：

```csharp
public static IReadOnlyList<string> LoadersOf(string jarPath)
{
    // fabric.mod.json → fabric；quilt.mod.json → quilt
    // META-INF/neoforge.mods.toml → neoforge；META-INF/mods.toml → forge
}
```

再走 Modrinth 的逐文件接口：

```csharp
POST /v2/version_files/update_individual
{"algorithm":"sha1","hashes":[{"hash":"…","loaders":["fabric"],"game_versions":["1.20.1"]}]}
```

请求本身已经按 loaders + 游戏版本筛过，所以返回结果不再二次过滤。
实测：`c2me-fabric-…jar` 返回 `0.2.0+alpha.11.18+1.20.1`，`project_id` 正常带回。

#### 2. 更新日志

结果里带 `project_id`（Modrinth）或项目页地址（CurseForge），拼成：

```csharp
item.ChangelogUrl = $"https://modrinth.com/mod/{version.ProjectId}/changelog?g={gameVersion}";
item.ChangelogUrl = project.Detail.WebsiteUrl.TrimEnd('/') + "/files";   // CurseForge
```

列表在「更新」旁边显示「日志」按钮（`ChangelogVisibility` 绑定），
地址一并写进 `ModUpdateCache`，刷新后依然可点。
## [1.1.3-beta.1] - 2026-11-03

### 更新内容

- 检查更新的结果会保存下来，刷新列表或重开实例后「可更新」不会消失
- CurseForge 检查改为批量请求：一百多个 Mod 从上百次请求降到三次
- 检查完成后提示里会写明有多少 Mod 查不到来源

### 技术实现

#### 1. 结果持久化（对齐 PCL2）

PCL2 把每个本地文件的 `ProjectVersion` / `UpdateFile` / `ChangelogUrls` 一起写进 JSON，
列表刷新时直接还原，不需要重新联网。我们之前每次 `Refresh()` 都重建 `ModFileItem`，
`Update` 字段随之丢失，于是徽标消失、只能再点一次检查。

新增 `ModUpdateCache`，按「文件名 + 大小」做键：

```csharp
public static string KeyOf(string fileName, long size)
    => fileName.ToLowerInvariant() + "|" + size;
```

`Refresh()` 末尾 `ApplyCachedUpdates()` 把缓存还原成 `ModrinthVersion` 挂回列表；
检查完调用 `SaveUpdateCache()`；更新成功后 `RemoveCachedUpdate()` 删掉对应条目，
避免刷新后旧徽标复活。

#### 2. CurseForge 批量检查

原流程对每个命中的 Mod 调一次 `GetFilesAsync`（内部还会翻页），
100 个 Mod 就是 100+ 次请求、每次几秒。改成三步：

```csharp
var matches = await curseForge.MatchFingerprintsAsync(keys);        // 1 次
var projects = await curseForge.GetModsWithLatestFilesAsync(modIds); // 1 次，带 latestFiles
var localFiles = await curseForge.GetFilesBatchAsync(fileIds);       // 1 次
```

候选文件直接从 `latestFiles` 里筛（同游戏版本 + 加载器匹配 + 比本地新），
不需要再逐个项目拉文件列表。

#### 3. 未识别数量

统计两边指纹/SHA-1 的命中数，提示里给差集：

```csharp
var unknown = Math.Max(0, total - matchedFiles);
```

自编译或私有 Mod 无法匹配属于正常现象，明说出来比静默跳过好。
## [1.1.2] - 2026-11-02

### 更新内容

- 聚合搜索翻页后 CurseForge 结果不再消失
- 搜索结果滚动到接近底部时自动预取下一页
- 详情页再次打开不再转圈（命中缓存直接渲染）
- 翻页与进入详情页加淡入上浮动画
- 安装继续用系统保存对话框，并定位到目标实例的 mods 目录
- 修复对话框可能被主窗口压住、看起来「点了没反应」
- 点「安装」时记录日志，便于定位无响应

### 技术实现

#### 1. CurseForge 在后几页消失

`CommunitySearchView` 的补刷预算写成了全局计数：

```csharp
if (_curseForgeRefreshLeft <= 0) _curseForgeRefreshLeft = 3;
_curseForgeRefreshLeft--;
```

三次之后永久为 0，后面翻到的页再也不会调度补刷。改为按页计数 + 指数退避：

```csharp
if (_cfRefreshPage != page) { _cfRefreshPage = page; _cfRefreshAttempt = 0; }
var delayMs = Math.Min(8000, 1200 * Math.Pow(2, _cfRefreshAttempt));
```

同时聚合层的等待从「无限等」改成首屏 2.5s、后续页 1.2s：

```csharp
await task.WaitAsync(TimeSpan.FromMilliseconds(waitMs), cancellationToken);
```

超时先返回已有结果并标记 `CurseForgePending`，由界面延时补刷。

#### 2. 滚动预取

取到 `ItemsControl` 模板里的 `ScrollViewer`，监听 `ScrollChanged`，
剩余不足一屏时后台抓下一页进聚合缓存（不更新界面）：

```csharp
var remaining = viewer.ExtentHeight - viewer.VerticalOffset - viewer.ViewportHeight;
if (remaining <= viewer.ViewportHeight) PrefetchNextPage(_page);
```

点「下一页」时命中缓存，切换接近瞬时；同一页只预取一次。

#### 3. 详情页秒开与动画

新增内存缓存探测 `PeekProject` / `PeekVersions`，命中后先渲染再后台刷新，
跳过加载遮罩。动画用 WPF 合成层完成：

```csharp
BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, 180ms));
slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, 220ms));
```

#### 4. 系统保存对话框的 owner

主窗口是 `AllowsTransparency` 的分层窗口，系统对话框不指定 owner 时可能被压在下面，
表现为「点了没反应」：

```csharp
var owner = Window.GetWindow(this);
var confirmed = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
```

初始目录按来源决定，从实例进入时直接定位到该实例的 `mods`。
## [1.1.1] - 2026-11-01

### 更新内容

- 下载调度按链路分档：直连保持激进换源，走代理时换成保守参数
- 失败重试改为指数退避，连接池改为长生命周期，任务间隙不再重连
- 去掉「下载限速」设置（原先默认 10 MB/s，会把直连也一起拖慢）
- 修复：Forge 1.17+ 整合包装不上（安装器被改写后校验失败）
- 修复：部分 CurseForge 项目版本列表为空、详情页显示 0 个版本
- 修复：同一个 Mod 在搜索结果里出现两次（跨平台 slug 不一致）
- 修复：版本列表接口失败后把空结果写进缓存，导致永远空
- 详情页加载器筛选按实际数据生成，移除 Quilt 按钮
- 整合包的加载器信息缺失时沿用推断值，版本图标同步修正

### 技术实现

#### 1. 下载调度分成两套参数

旧逻辑对所有链路都用同一套「按相对速度判定慢源 → 中止 → 换源」策略。这套在直连时代
有效，但走代理时所有分段共享一条隧道：8 条连接互相抢带宽，每条都低于中位数的 1/6，
于是集体判定为慢源、集体中止重连，形成「越换越慢」的正反馈。

```csharp
private readonly record struct Tuning(int MaxPieces, long PieceSize, int StallSeconds,
    bool AggressiveSwitch)
{
    public static Tuning For(bool proxied) => proxied
        ? new Tuning(4, 8 * 1024 * 1024, 20, false)   // 代理：保守
        : new Tuning(8, 4 * 1024 * 1024, 10, true);   // 直连：PCL2 式
}
```

读超时也从「固定 10 秒」改成「无进展超时」：只要窗口内收到过字节就继续等，
慢但持续有数据不再算故障。

```csharp
timeout.CancelAfter(TimeSpan.FromSeconds(tuning.StallSeconds));
```

#### 2. 失败退避与连接复用

重试原本是立刻重拨，多个分段会同步冲击代理节点。改为 0.5s → 8s 的指数退避，带 ±30% 抖动：

```csharp
var baseMs = Math.Min(8000, 500 * Math.Pow(2, Math.Min(attempt - 1, 4)));
var jitter = 1 + Random.Shared.NextDouble() * 0.3;
```

连接池同步调整，避免任务间隙（下一个 Mod 开始前）重新握手：

```csharp
PooledConnectionLifetime = TimeSpan.FromMinutes(10),
PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
MaxConnectionsPerServer = 8,
```

#### 3. 限速设置移除

`AppConfig.MaxDownloadSpeedKbps` 删除，`DownloadEngine.ApplySpeedLimit()` 移除。
令牌桶不再是全局闸门——它原本只该约束加速线路，但实现成了对直连也生效。

#### 4. Forge 1.17+ 安装失败

诊断为两层原因：

1. Forge 47.x 的安装器会校验自身内容；用 `ZipArchiveMode.Update` 原地改条目、
   或在 JSON 里替换下载地址，都会让安装器启动两秒内报「A problem installing was detected」，
   且崩溃报告不带异常信息。
2. 安装器是独立 JVM，自己的 `HttpURLConnection` 不走启动器的路由。

最终改法：**不改写 jar**，只把探测到的代理以 JVM 参数传给安装器：

```csharp
if (await HttpUtil.DetectProxyAsync(ct) is { } proxy)
{
    startInfo.ArgumentList.Add($"-Dhttps.proxyHost={proxy.Host}");
    startInfo.ArgumentList.Add($"-Dhttps.proxyPort={proxy.Port}");
}
```

没有代理时安装器仍可完成（会自动回落到 `maven.creeperhost.net` 镜像），只是更慢。

#### 5. CurseForge 版本列表为空

两个独立缺陷：

- **缓存毒化**：请求失败返回空列表后，`[]` 被写进 `data\cache\api\versions-CurseForge-<id>.json`，
  之后每次都读缓存，永不重试。现在空列表不写缓存、读到空缓存也重新拉取。
- **条目被整批丢弃**：作者关闭第三方分发时接口返回 `downloadUrl: null`，
  解析函数遇到空地址直接 `return null`，956 个文件全被丢掉。
  现在按文件 id 拼 CDN 路径：`mediafilez.forgecdn.net/files/{id/1000}/{id%1000}/{文件名}`。

#### 6. 跨平台归并

搜索结果里同一个 Mod 会同时来自两个平台，归并键原本只取 slug，但两边写法不同：

```
Modrinth   : 3dskinlayers
CurseForge : skin-layers-3d
```

改为三档匹配，命中任意一档即归为一组：

1. MC百科条目 id
2. 标题分词排序（`3D Skin Layers` 与 `Skin Layers 3D` 词集相同）
3. slug

#### 7. 加载器筛选动态化

固定四按钮改为按版本实际出现的加载器生成，`quilt` 直接排除；
整合包版本未声明 `loaders` 时沿用「任意版本声明过的值」，版本图标同步：

```csharp
private List<string> VersionLoaders(ModrinthVersion version)
    => version.Loaders.Count > 0 ? normalized : _inferredLoader is null ? [] : [_inferredLoader];
```
## [1.1.0] - 2026-11-01

正式版。相比 1.0.0 的主要变化：

### 数据与目录

- 配置、缓存、日志全部放到安装目录的 `data\`，不再写 `%LOCALAPPDATA%`
- 游戏文件收进 `game\.minecraft\`（versions / libraries / assets），整合包独立到 `game\modpacks\`
- 旧布局首次启动弹确认框后再整理，失败自动回滚；老版本 AppData 数据自动搬过来
- 设置里可以改数据目录 / 游戏目录，支持一并迁移
- 卸载前自动断开所有引用实例，不会顺着目录联接删到外部 `.minecraft`

### 版本管理

- 支持**引用**现有 `.minecraft`：生成 `ref-*` 影子实例，mods / config / saves 用目录联接指回原处
- 支持**导入**现有 `.minecraft`：勾选版本复制进来，可只补缺失的 libraries / assets
- 卸载程序会先断开所有引用实例再删目录
- Forge 安装器改走 BMCLAPI 镜像，安装从几分钟降到几十秒
- 启动前自动补齐缺失依赖（BMCLAPI → 官方 → Forge maven）
- 只有 natives 的库不再被误判为「缺少依赖」

### 下载

- 分段下载 + 慢源自动换源（相对中位数判定），断点续传，收尾阶段不换源
- 整合包文件并发下载（6 路），失败文件自动补下 3 轮
- CurseForge 上 `downloadUrl` 为 null 的文件，走 download-url 接口或 CDN 路径拼接
- 下载文件名带中文名前缀（`[3D皮肤层] skinlayers3d-….jar`），更新 Mod 时同样生效
- 整合包安装成功后删除安装包（仅限启动器自己下载的那份）
- 移除内置代理内核，改为跟随系统 / 本机代理；官方 CDN 优先走代理，其余直连或走镜像

### Mod 与搜索

- Mod 列表显示真实 Mod 名（Fabric / Forge / Quilt / 1.12.2 mcmod.info），优先中文名
- Mod 图标先读 jar 自带图片，缺的再联网兜底；列表不再被网络阻塞
- 检查更新后出现「可更新 (N)」筛选；检查期间遮罩挡住整页
- 社区搜索：列表虚拟化、翻页回顶、回车搜索、版本下拉可编辑、聚合翻页均衡
- 前置依赖标签改成胶囊样式，只保留 required，过滤加载器本体

### 界面

- 目录选择、迁移、导入都带进度；进度条改成圆角胶囊形
- 内存滑块 1GB 以下按 1MB、以上按 0.1GB 调
- 公告页去掉被裁成方块的投影，免责声明更新
- 关于页新增感谢名单（PCL2 / BMCLAPI / MCIM / MC百科）

### 修复

- 引用实例启动时找不到客户端 jar、库缺失、资源重复下载
- 版本下拉选完不显示、下拉弹层透底
- 下载速度显示卡在旧数字、任务面板速度显示 `—`
- 下载完成却提示「下载不完整」（分段重复计数）
## [1.0.0] - 2026-10-30

第一个正式版。从 beta.6 起的改动：

### 社区资源

- Modrinth 与 CurseForge 聚合搜索：同名资源合并成一张卡片，卡片顶部切换来源
- 两边结果交替排列（MD、CF、MD、CF…），翻页后每页都有两边
- 来源标签按平台配色：Modrinth 绿、CurseForge 橙
- Modrinth 一次抓 40 条、CurseForge 一次 20 条，各按页补齐
- 翻页后自动回到列表顶部
- 移除「找到 N 个结果」的统计显示（CurseForge 的 totalCount 封顶 10000，数字无意义）
- 聚合翻页上限 25 → 100 页
- 修翻页竞态：慢请求返回时不再覆盖当前页
- 修某一页只剩单来源的问题（Modrinth 抓取失败不再拖垮整页）

### 安装与下载

- 代理内核随安装包分发，首次启动无需再下载
- 代理设置改完立即生效，响应头 15 秒内重探
- CurseForge 改走自建 Worker 反代（mtl-curseforge），失败退回社区镜像
- Modrinth 走自建 Worker 反代（mtl-modrinth-api）

### Mod 管理

- 检查更新不再误报「已是最新」
- CurseForge Mod 指纹识别修复
- 兼容性不匹配的版本不会被推荐
- 一键全部更新，保留启用 / 禁用状态

### 界面

- 首次启动公告页（代理内核说明 + 免责申明），5 秒后可关
- 代理测试文案统一为「已成功连接代理」
- 启动时把窗口带到最前面
- 清理废弃 UI 与死代码（约 20 处成员、2 块旧界面）

## [0.1.2] - 2026-10-25

**MTL alpha v1.0.1 build 2**

- 原版资源使用 BMCLAPI（bmclapi2.bangbang93.com），Forge 安装器同样走 BMCLAPI
- 社区资源默认「聚合」：并行搜索 Modrinth + CurseForge，同一资源两边各出现一次并标注来源，结果按下载量降序
- CurseForge 固定走 MCIM 镜像，详情/版本/前置/安装与 Modrinth 共用同一套界面
- 内置 VC++ 运行库，启动失败写 `startup-error.log`
- 清理旧安装器、占位数据与无用资源
- 社区文件校验失败时自动回退官方 CDN（Modrinth / 整合包）
- 聚合结果跨页全局按下载量排序，不再按来源分页堆积
## [0.1.1] - 2026-10-26

**MTL alpha v1.0.2 build 1**

下载与社区资源这条链路基本重做了一遍，重点解决「国内直连下不动」和「同一个 Mod 重复出现」。

### 网络与下载
- 每条请求在直连与代理之间自动兜底：一条路失败自动换另一条；国内镜像域名始终优先直连
- 连不上的域名进入 2 分钟冷却，后续文件直接跳过，不再每个文件都白等一次连接超时
- 移除 MCIM 文件源（它只是 302 跳转器，还会返回半截文件与错文件名），社区文件改走
  **官方 CDN → 自建 Cloudflare 镜像**（墙外回源 + 边缘缓存）
- 镜像返回半截内容时先重试，不再一次就禁用；全源失败后最多再整体重来两轮
- 服务器声明的长度与已知大小不符时立刻换源
- Mod 下载与依赖库直接用接口/清单给的大小，省掉下载前的 HEAD 探测

### 下载引擎
- 新增**本地文件复用**：先在本机实例目录、官方启动器的 `.minecraft\libraries` /
  `.minecraft\versions` 里找同一份文件直接复制（按文件名 / jar 大小 + SHA-1 匹配）
- 已存在的客户端 jar、依赖库按 大小 + SHA-1 校验，一致就跳过
- 新增**下载限速**：全局令牌桶，设置页可选 不限速 / 1 / 4 / 10 MB/s
- 源失败分级：404 / 502 / 416 立刻换源，非 BMCLAPI 的 403、429 也换源

### 源映射（对齐 PCL2）
- 依赖库同时走 `bmclapi/maven` 与 `bmclapi/libraries`；Forge / Fabric / NeoForge 的 maven
  也纳入镜像，加载器依赖镜像优先、官方兜底
- 版本清单与文件下载拆成两套独立的源偏好（设置页新增「版本清单」）
- 版本清单内容不合格（少于 100 条、缺最新正式版）自动换源；失败时不再显示内置假版本
- 社区文件生成官方多变体（edge / mediafilez / media / overwolf）去重后按源偏好排序

### 社区资源
- 聚合搜索改成**一张卡片两个来源标签**：同一项目的 Modrinth / CurseForge 条目合并，
  点标签就地切换（简介、下载量、图标跟着变），点卡片按当前来源进详情
- 标签切换带高亮淡入、文字交叉淡出与弹性缩放；结果列表整体淡入
- 组按组内最大下载量 / 最新更新时间排序，分页按组切分；同平台重复结果不再重复展示
- 新增**中文搜索**：内置常见 Mod 中英别名表（数据目录放 `search-aliases.json` 可扩充），
  中文自动翻译成英文再搜，结果栏提示实际搜索词
- CurseForge 文件列表改为翻页获取（之前只取第一页 50 个，JEI 这类项目版本残缺）
- 游戏版本标签统一排序与显示：正式版按版本号排，快照 / 预览版加后缀，整合包只显示最新正式版

### 加载器
- 新增 **OptiFine 安装**：BMCLAPI 版本列表 → 官方安装器命令行 → 合并成独立实例
- 安装器在临时 `.minecraft` 里运行（子进程 APPDATA 重定向），不碰用户真实目录
- 安装器产出的客户端 jar 优先作为实例 jar，兼容老版本 OptiFine 的补丁逻辑

### 修复
- 版本列表按自然顺序排列（1.21 排在 1.9 前面）
- 下载页「已安装」标记能认出合并后的实例名（如 1.21.1-Fabric-0.20.1）
- User-Agent 与启动参数里的版本号改为真实版本，不再写死 0.1.0

### 关于 MCIM
- 文件下载不再使用 MCIM；CurseForge 的搜索与详情仍走 MCIM
  （没有官方 API Key 之前别无选择，但它只传 JSON 元数据，不传文件）

## [Unreleased]

### 结果数显示
- **删掉“找到 N 个结果 / 共 N 个结果”的统计**：CurseForge 的 totalCount 封顶 10000，
  聚合又是两边相加，这个数字本来就不准，只在没结果时给一句提示
- 聚合翻页上限 25 → **100 页**（数据按页现抓，只有真翻那么深才会去请求）
- 单平台 CurseForge 受官方 `index + pageSize ≤ 10000` 限制，最多翻到第 **500** 页（每页 20 条）

### 交互细节
- 资源页翻页后**回到列表顶部**（之前停在上一页的底部）
- 启动时**把窗口拉到最前**：之前从资源管理器双击打开，窗口常被当前窗口挡住；
  现在先 Activate，再短暂置顶，界面画完后自动取消置顶（不会一直压着别的窗口）

### 聚合：Modrinth 一次抓 40 条当缓冲
- `ModrinthChunk` 20 → **40**，目标余量多一页（第 N 页时池里 MD ≈ (N+2)×20、CF ≈ (N+1)×20）
- 每页仍各发一次请求；MD 多出的那一页用来扛请求抖动，不会因为一次没回来就整页只剩 CF
- 实测空查询连翻 5 页：MD 16/4 → 14/6 → 11/9 → 15/5 → 10/10，跨页重复 0

### 聚合：Modrinth 抓取失败不再拖垮整页
- `EnsureModrinthAsync` 单次失败只记日志、下次翻页重试，不再让整页退化成单来源
  （之前一次超时就会导致某一页只剩 CurseForge）
- 每页打一行日志：`aggregate page N: 池 MD x / CF y，分组 z，本页 n`，方便排查

### 聚合排序：两边交替，不再一边倒
- 原因：CurseForge 的下载量普遍比 Modrinth 高一个量级（JEI 6.4 亿 vs Fabric API 2.9 亿），
  直接按下载量混排，第一页会被 CF 整页占满
- 改成两边各自排好再**交替合并**（MD、CF、MD、CF…），每页两边都有；
  按更新时间和相关度排序同理
- 实测空查询第 1 页：MD 13 / CF 7（改之前是 CF 全占）；第 2 页 MD 12 / CF 8

### 全局提示与翻页竞态
- 首次启动的代理自检 toast 改成「已成功连接代理」/「连接代理失败：原因」，
  不再显示节点名和毫秒数（和设置页保持一致）
- **修**：翻页点太快时，前一次（更慢的）搜索会后返回并覆盖当前页 ——
  现在每次搜索带一个代次号，只有最新一次的结果才会渲染，loading 也不会被旧请求提前关掉

### 来源标签配色
- 搜索结果卡片上的 **CurseForge** 标签选中时改成它自己的橙色（`SourceOrange`），
  不再和 Modrinth 一样都是主题绿 —— 之前两个标签选中都是绿的，分不清在看哪个平台

### 聚合翻页：两边同步加载
- Modrinth 每块 100 条 → **20 条**，和 CurseForge 对齐
- 改成「每页各抓 20 条」：翻到第 N 页时两边都有 N×20 条，混排后每页都均衡
- 去掉之前的预取余量（一次抓 2 页），现在是一次一页，首屏更快
- 实测 `create`：1 页 12/15、2 页 12/17、3 页 12/11、4 页 13/8、5 页 16/14、6 页 17/9

### 聚合翻页：每页都有两边
- **修**：第 1 页 MD + CF 都有，第 2 页开始只剩 Modrinth。原因是 Modrinth 一块 100 条、
  CurseForge 一块只有 20 条，只按总条数补的话 CF 早就被用光了
- 现在每页按比例补到 `(页码+1) × 每页条数` 条 CF，翻页时两边一起跟上来
- 实测 `jei`：第 1 页 MD 15 / CF 19、第 2 页 MD 13 / CF 19、第 3 页 MD 5 / CF 17

### 清理废弃代码与过时界面
- 删掉 `DownloadPage` 里早已不显示的安装进度条（`InstallBar` 整块，Visibility 永远是 Collapsed）
- 删掉「网络代理修改后，重启启动器生效」的旧提示（现在每 15 秒重探一次，根本不用重启）
- 订阅网关去掉旧的 `/status` 版本探测（新网关支持 `fresh=1` 且 KV 优先），
  进程内第一次取订阅带 `fresh=1`，之后走缓存，少一次跨境请求
- 修正过时注释与文案：CurseForge 客户端说明、MCIM 说明、内核大小提示（现在是随包自带）
- 删掉 `HttpUtil.Create` 里没人用过的 `forceDirect` 参数（三处调用全是默认值 false）

### 清理废弃代码
- 删掉未使用的成员：`BackendName` / `ResetProxyFlag` / `CurseForgeApiBase` / `ModrinthApiBase` /
  `FabricMaven` / `OfficialLibraries` / `SlotsTotal` / `SourceBrush` / `Stable` / `Coverage` /
  `GetLatestVersionAsync` / `HasGameArguments` / `TakePage` / `IdleTimeout` / `Offset`

### Modrinth API 也走反代
- 新增独立 Worker `mtl-modrinth-api`（`worker/modrinth-gateway.js`）：
  客户端请求 `mtl.dpdns.org/md/v2/...`，Worker 转发 `api.modrinth.com`，
  GET 结果边缘缓存 5 分钟；带 Personal Access Token 的请求不缓存
- `AppConfig.ModrinthProxy` 默认指向它；反代不可用时自动退回官方直连
- 自家网关 `mtl.dpdns.org` 也改成直连优先（国内直连 CF 边缘比绕代理快，还省代理带宽）
- Worker 可配 Secret `MODRINTH_TOKEN`：用 Worker 里的 Token 请求上游，
  所有客户端共享更高配额；客户端无需自带 token
- 和现成的 `mtl-modrinth`（`cdn.modrinth.com` 文件镜像）是两回事，互不影响

### 聚合搜索：CurseForge 与 Modrinth 平权
- 去掉「CurseForge 只等 3 秒就先返回」的限制 —— 那导致首屏永远只有 Modrinth。
  现在和 Modrinth 一样等它抓完再出页面（Worker 反代一次 20 条约 2~4 秒）
- 实测 `sodium`：首屏 MD 17 条 + CF 15 条（7.5 秒）；`jei`：MD 15 + CF 19（2.8 秒）
- 公告页倒计时 10 秒 → **5 秒**；内核装好后进度条与状态文字不再变化

### 内置内核随安装包分发
- mihomo 内核 zip（22.5 MB）**直接打进安装包**，首次启动不再下载 ——
  少了 20~60 秒等待，公告页那 10 秒足够把内核解压好
- 构建前跑一次 `tools/get-core.ps1` 把内核下到 `tools/core/`（已在 `.gitignore`，不进仓库）；
  CI 的 release 流程也加了这一步
- 客户端优先用安装包里的内核（校验 SHA-256），没有再走网络下载

### 首次启动公告
- 新增公告页，**只在第一次启动时弹一次**：
  - 页面打开的同时开始安装代理内核，实时显示进度（下载中 / 已就绪 / 失败原因）
  - 下方是 7 条免责申明（非官方、不提供游戏本体、内置代理仅用于加速、令牌只存本机、风险自负等）
  - **关闭按钮 10 秒倒计时**，倒计时内 Esc / Alt+F4 也关不掉
- 看完写进配置 `AnnouncementShown`，之后不再出现

### CurseForge 官方 API（独立 Worker）
- 新增独立 Worker `mtl-curseforge`（`worker/curseforge-gateway.js`）：
  客户端请求 `mtl.dpdns.org/cf/v1/...`，Worker 加 `x-api-key` 转发给官方，
  GET 结果边缘缓存 5 分钟（`CF_CACHE_TTL` 可调）
- **客户端不再接触任何 Key**：默认走这个反代，不可用时（403 / 404 / 502 / 503）
  自动退回社区镜像并写日志
- 后端选择简化为「Worker 反代 → 社区镜像」，删掉了本机填 Key 的那条路
  （公开分发的程序里任何字符串都能被抠出来）
- `api.curseforge.com` / `api.modrinth.com` / `cdn.modrinth.com` 改为**直连优先**：
  都挂在 Cloudflare 上、国内能直连，绕代理反而先失败两次（实测首屏 15 秒 → 3 秒）
- 路由失败冷却：本机代理失败后 5 分钟内排到最后（内置代理同样 2 → 5 分钟），
  代理挂掉时不用每个请求都先白等一遍
- 聚合的 CurseForge 单次条数 50 → 20（官方响应随条数线性变大，50 条 ≈ 685 KB）
- 聚合预取从「当前页 +3 页」收窄到「+2 页」，首次打开少下几百 KB

## [0.1.0] - 2026-10-29

**MTL beta v1.0.0 build 6**

代理部分整体重做（Worker 合并、内核进 KV、断点续传），修好 Mod 更新检查的三个错误，
社区搜索的 CurseForge 结果不再被超时丢掉，外加新图标。

### 图标
- 桌面图标换成新设计稿，导出 16/24/32/48/64/128/256 七个尺寸；
  生成脚本放进 `tools/icon/make-ico.py`，以后换图重跑一次即可

### 内置代理：Worker 重做（订阅 + 内核合并）
- 订阅网关与内核镜像合并成一个 Worker（`worker/proxy-gateway.js`）：
  `/sub/config`、`/sub/status`、`/sub/publish`、`/core/mihomo.zip`、`/core/status`、`/core/publish`
- **内核存进 KV**：不再让 Worker 现去 GitHub 拉（那条路又慢又容易被掐），
  22 MB 存一次，之后所有客户端从 Cloudflare 边缘拿；下载支持 `Range` 断点续传
- 订阅回调带 `X-MTL-Source`（kv / upstream），客户端日志里能直接看出走的哪条
- 客户端新增 `publish-core.ps1`，部署与排查步骤写进 `worker/PROXY-SETUP.md`
- 内核下载失败时提示改成「内核下载失败（网络不稳定，或两个下载源都连不上）」，
  不再笼统报「内核启动失败」

### 内置代理：内核下载
- 内核改成**断点续传**：先写 `.part`，校验通过再改名。以前是 `File.Create` 直接覆盖，
  镜像慢、连接被掐断一次就把已经下好的部分全截断，21 MB 永远下不完
- 每个下载源最多重试 2 次，断线后从上次的字节继续
- 校验内核文件大小（> 5 MB）才算可用，避免半截文件被当成正常内核

### 社区搜索：CurseForge 不再被超时丢掉
- **修**：Mod / 整合包等类目的聚合结果里只有 Modrinth，看不到 CurseForge。
  原因是 CurseForge 走第三方镜像，实测一次搜索要 **19 秒**（mod 类目 50 条响应 1.4 MB），
  而之前给它设的是 6 秒上限 —— 每次都超时，CF 结果被整块丢弃
- 现在前台只等 3 秒（且整页只等一次），Modrinth 先出；CurseForge 丢后台抓，
  抓到后界面 22 秒自动补刷一次，翻到下一页也能看到
- CurseForge 超时不再「永久放弃这个查询」，下次翻页会重新尝试

### Mod 更新
- **修**：本地已经是最新时仍然提示「可更新」。Modrinth 的 `version_files/update`
  对「已是最新」的哈希也会把那个最新版返回，现在用文件 SHA-1 再确认一次
- **修**：CurseForge 的指纹匹配结果没带指纹，只能按文件名对回本地文件 ——
  文件改过名（`xxx (1).jar`）就永远查不到更新。现在读 `file.fileFingerprint`
- **修**：CurseForge 可能把更旧的文件提示成「最新」造成降级 —— 改为按
  （发布时间, 文件 ID）排序，只有确实更新才提示
- 更新时保留原来的启用/禁用状态（禁用中的 Mod 更新后仍是 `.jar.disabled`），
  旧文件一定会被清掉，不会两个版本共存
- 新增「全部更新」按钮：一次装完检查出来的所有更新

### 内置代理：节点选择
- 默认节点改为 **美国|BGP②|X0.2**，其余真实节点全部作为备用（原来的逻辑只带 3 个备选）
- 首选挂掉时 mihomo 自动往下找，不再只有一个兜底
- **信息节点过滤**：机场会把「剩余流量：700 GB」「套餐到期：2026-12-28」这类展示项
  混进 `proxies:` 列表，而且是排在最前面的三条。以前挑不到偏好节点时兜底取第一个，
  就会把「剩余流量」当成默认节点（能连，但界面显示很怪）。现在挑节点前先剔掉这批
- 兜底顺序调整为：偏好名 → `0.1倍` → `X0.1` → 第一个真节点

### 社区搜索：关键词化 + 容错
- 搜索串统一做「关键词化」：去标点、转小写、合并空白，小数点 / 连字符这类夹在词里的保留
  - `Create: Steam 'n' Rails` → `create steam n rails`
  - `sodium [1.21]` → `sodium 1.21`
  以前带方括号、加号、引号的查询会被当成额外关键词，把结果卡到个位数
- **中文多关键词逐个翻译**：「机械动力 汽鸣铁道」过去整串查不到，现在会挑最具体的
  「Create: Steam 'n' Rails」去搜；全角逗号、空格都能拆
- 关键词串太长导致一条都搜不到时，自动退回「最长的那个词」再找一次
- **修**：译名表现在只对含中文的输入生效。之前英文查询会被中文条目乱套
  （搜 `JEI` 被翻成 `JEI Bees`、搜 `sodium` 变成 `Sodium Crash Fix`），
  MC百科里不少条目名字本身就是英文缩写
- 聚合缓存 2000 → 1000 条（约 25 页），上限和分页条保持一致
- **修**：MC百科译名表里有 297 条页面的「中文名」其实是英文（`Dev World` / `Better With Lib` …），
  之前会被当成中文副标题盖住真标题 —— 搜 `Fabric API` 显示成「Dev World」就是这么来的。
  现在只有真含汉字的条目才会被当成中文名

## [1.0.0-beta.5] - 2026-10-28

**MTL beta v1.0.0 build 5**

继续修社区资源翻页：build 3 的深度上限只够 12 页。

### 翻页
- 聚合缓存上限 400 → **2000 条**（约 50 页），原来 400 条合并去重后正好 12 页
- 每次按「当前页 + 3 页」的余量补抓：翻下一页通常不用再请求，已经看过的页也不会
  因为补抓而位移（实测跨页重复从 13 个降到 0）