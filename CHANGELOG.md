# 更新日志

本项目从 0.1.0 开始记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.4] - 2026-11-15

### 更新内容

- 安装器从 NSIS 换成 Inno Setup：启动时申请管理员权限，安装位置由用户在向导里选择，带就绪摘要页与任务选择
- 版本下载页在内容超出窗口时自动出现滚动条，可用滚轮整页滑动（之前展开加载器列表会挤掉底部按钮）
- 自定义主题：明亮 / 暗色两套里不同的 57 项颜色都能单独改，全中文、分组卡片
- 
### 技术实现

安装器：`installer/MutsumiLauncher.iss`，`PrivilegesRequired=admin`（启动即提升），
`DefaultDirName={autopf}\Mutsumi Launcher` 但保留目录选择页；`UsePreviousAppDir=no` 避免沿用过期的旧路径。
卸载前先跑 `--cleanup-references` 断开实例联接，避免误删玩家的游戏目录；
`InitializeSetup` 检测旧 NSIS 安装并提示。

启动器：数据（`data/`、`game/`）存放在安装目录下，所以程序仍以 `requireAdministrator` 运行，
和安装器权限保持一致。
自定义主题：`EditableKeys` 由两套色板逐键比对得到，
覆盖值存在 `ThemeCustomColors`，`BuildCustomPalette` 以选定底色为底再套覆盖。

## [1.1.4-beta.31] - 2026-11-15

### 更新内容

- 自定义主题：全部颜色项改成中文名
- 自定义主题重新排版：按「文字 / 主色 / 表面与背景 / 描边与分隔 / 状态色 / 阴影」分组，两列卡片

### 技术实现

`KeyLabel` 补全 57 个键的中文名，`KeyGroup` 负责分组；
设置页按固定顺序铺组，每项是一张小卡片：

```csharp
return new Border { Width = 258, CornerRadius = 10, Child = row };
```

卡片内是「色块 + 中文名 + 色值输入 + 单项恢复」，改完立即生效。
## [1.1.4-beta.30] - 2026-11-15

### 更新内容

- 全新自定义主题：明亮 / 暗色两套主题里不一致的颜色（共 57 项）都能单独改

### 技术实现

可改的键由两套色板直接比对得出，不再手写列表：

```csharp
public static IReadOnlyList<string> EditableKeys { get; } = ComputeEditableKeys();

// 明亮/暗色里值不同的键，就是切主题时实际会变的部分
if (!dark.TryGetValue(key, out var other) || other != color) keys.Add(key);
```

自定义主题以「起始配色（明亮 / 暗色）」为底，逐项覆盖：

```csharp
var colors = dark ? DarkPalette() : LightPalette();
foreach (var (key, hex) in config.ThemeCustomColors) colors[key] = ParseColor(hex);
```

设置页每行一个色块 + 中文名 + 色值输入，改完立即生效，"恢复"按钮清掉单项覆盖。
## [1.1.4-beta.29] - 2026-11-15

### 更新内容

- 主题模块回滚到 1.1.4-beta.20 的自有实现
- 删除自定义主题，外观只剩「明亮 / 暗色」两档

### 技术实现

回滚 `ThemeService` / `AppConfig` / `SettingsPage` / `MainWindow` 到 5540585 的版本，
删除后续引入的 `MaterialTheme`（种子色推导）、液态玻璃、字体、缩放等选项。

自定义主题整块移除：单选按钮、底色/主色面板、色板与色值输入、`ApplyCustomTheme` 等一并删除，
`ThemeMode` 只剩 `light` / `dark`，老配置里的 `custom` 会按明亮处理。
## [1.1.4-beta.28] - 2026-11-15

### 更新内容

- 主题页补齐 FlClash 的全部条目：主题模式、纯黑、主题色、配色风格、导航栏样式、切换动画、液态玻璃、背景模糊、界面字体、文字缩放

### 技术实现

对应关系：

| FlClash | 本项目 |
| --- | --- |
| `ThemeMode`（system/light/dark） | `ColorMode` |
| `primaryColor` + `colorStyle` | `Seed` + `Style`，由 `MaterialTheme.Build` 展开 |
| `pureBlack` | `ThemePureBlack`，深色表面 tone 置 0 |
| `floatingNavigationBar` | `ThemeNavStyle`，悬浮时导航条加投影、圆角 22 |
| `tabAnimation`（slide/fade） | `ThemeTransition`，页面切换走位移或纯淡入 |
| `liquidGlass`（none/standard/ultra） | `ThemeLiquidGlass`，玻璃层与遮罩的 alpha 乘 0.68 / 0.42 |
| `sidebarBlur` | `ThemeSidebarBlur`，背景图 `BlurEffect` 半径 48 |
| `fontFamily` | `ThemeFont`，替换 `UiFont` 资源 |
| `textScaleFactor` | `ThemeScale`，`ShellRoot.LayoutTransform` 85-130% |

字体替换注意：页面里显式用 `{StaticResource UiFont}` 的元素在运行时不会重画，
所以改成窗口级 `FontFamily` + 资源替换，新开的页面一定是新字体。
## [1.1.4-beta.27] - 2026-11-14

### 更新内容

- 主题模块按 FlClash 完整重构：主题模式（跟随系统 / 浅色 / 深色）、纯黑背景、种子色、配色风格、实时预览

### 技术实现

`ThemeService` 改成 FlClash 的四元组模型：`ColorMode + Seed + Style + PureBlack`。

```csharp
var theme = MaterialTheme.Build(ParseColor(Seed), style, IsDark, LightPalette(), PureBlack && IsDark);
```

`MaterialTheme.Build` 新增深色分支（主色 tone 80、容器 tone 30、表面 tone 6 / 纯黑 0），
浅色仍走 tone 40/90/98。`SystemIsDark()` 读 `AppsUseLightTheme` 决定跟随系统的实际明暗。

设置页顶部加了实时预览卡：侧栏条、卡片、主/次按钮、文本条都绑 `DynamicResource`，
换模式或种子色时立刻跟着变。
## [1.1.4-beta.26] - 2026-11-14

### 更新内容

- 自定义主题新增「跟随系统」：一键取 Windows 个性化里的主题色当种子
- 新增配色预览条：主色 / 高亮 / 窗口 / 卡片 / 正文五个色块

### 技术实现

系统主题色读 `HKCU\Software\Microsoft\Windows\DWM` 的 `ColorizationColor`，
取低 24 位作为种子色：

```csharp
return Color.FromRgb((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
```

预览条直接用 `DynamicResource` 绑定当前主题的五个画刷，换主题时自动跟着变。
## [1.1.4-beta.25] - 2026-11-14

### 更新内容

- 主题参考 FlClash 改成「种子色 + 配色风格」：10 个预设色、任意色值、柔和/平衡/鲜艳/单色

### 技术实现

新增 `Services/MaterialTheme.cs`：把一个种子色展开成一组色调（tone 0-100），
再映射到启动器的画刷，等价于 Material 3 的 `seededColorScheme`：

```csharp
var (hue, _, _) = ToHsl(seed);
Color Tone(double chroma, int tone) => FromHsl(hue, chroma, tone / 100.0);
colors["AccentDeep"]  = Tone(primaryChroma, 40);
colors["AccentWash"]  = Tone(primaryChroma, 84);
colors["WindowBase"]  = Tone(neutralChroma, 98);
colors["Ink"]         = Tone(neutralChroma, 12);
```

四种风格只改彩度：柔和 0.30 / 平衡 0.45 / 鲜艳 0.62 / 单色 0.05，中性色同步收缩。
这样同一颗种子色在不同风格下，主色深浅和界面冷暖都是成套的，不再出现「彩色按钮配灰底」。

按 FlClash 的取向，只取颜色部分——液态玻璃、模糊、动画等一概不引入。
## [1.1.4-beta.24] - 2026-11-14

### 更新内容

- 自定义主题按 PCL-CE 的思路重做：色相 / 饱和度 / 明度三个滑块 + 六个配色预设
- 窗口底色直接由「明度」决定，低明度自动转深色文字
- 卡片、侧栏、描边、阴影全部由这一组 HSL 值推导，界面不再有灰色残留

### 技术实现

参考 PCL-CE 的 `Config.Preference.Theme`（ColorMode / ColorTheme / Hue / Sat / Light），
把原来「主色 + 色调 + 浓度 + 背景色」四套手写参数，收敛成 `ThemeHue / ThemeSat / ThemeLight`：

```csharp
var bg = HslToRgb(h, s * 0.45, l);            // 窗口底色
var accentLight = HslToRgb(h, s * 1.15, dark ? 0.70 : 0.46);
var contrast = dark ? Colors.White : Colors.Black;
colors["CardBorder"] = WithAlpha(contrast, 0x2A);
```

深色底（明度 < 52%）自动切浅色文字、白色低透明度描边；浅色底反之。
预设直接给 (色相, 饱和度, 明度)，点一下三个滑块一起走。
## [1.1.4-beta.23] - 2026-11-14

### 更新内容

- 自定义主题支持改背景色（8 个预设 + 任意色值，可切回「跟随主色」）
- 选深色背景时自动切换为浅色文字，不会出现黑底黑字

### 技术实现

`ThemeBackground` 存背景色；`BuildCustom` 里若指定了背景：

```csharp
colors["WindowBase"]   = bg;
colors["SurfacePanel"] = Blend(bg, white, 0.10) 带 F2 透明度;
colors["BackdropVeil"] = WithAlpha(bg, 0xE8);   // 压住背景图，只留一点质感
```

同时按背景亮度（`Luminance(bg) < 0.45`）把 Ink 系列换成浅色、
描边/分隔线换成白色低透明度，保证深色背景下的可读性。
## [1.1.4-beta.22] - 2026-11-14

### 更新内容

- 自定义主题面板重做：预设、主色、界面色调、着色浓度、恢复默认
- 自定义主题从「只换主色」升级为「主色 + 色调 + 浓度」三维调节

### 技术实现

新增 `ThemeTone`（green / cool / warm / neutral）与 `ThemeStrength`（soft / normal / strong）两项配置，
`BuildCustom` 据此推导整套色板：

```csharp
var tone = ToneColor(accent, toneName);      // 冷暖倾向
var k = StrengthFactor(strength);            // 0.45 / 1.0 / 1.9
colors["WindowBase"] = Tint(base, tone, 0.10 * k);
colors["SidebarBg"]  = Tint(base, tone, 0.20 * k);
```

面板里的预设 / 色调 / 浓度都是代码生成的单选组（避免 XAML 重复），
切换时直接落到 `ThemeService.SetTheme`，配置持久化后下次启动复原。
## [1.1.4-beta.21] - 2026-11-14

### 更新内容

- 移除暗色主题：外观只剩「明亮 / 自定义」，旧配置里的 dark 自动回落明亮
- 自定义主题重做：主色会驱动整套路色（窗口、卡片、侧栏、描边、阴影、高亮），不再只是按钮变色

### 技术实现

`AppTheme` 去掉 `Dark`，`DarkPalette()`、深色底图、`IsDark` 一并删除；
`ApplyFromConfig` 里旧的 `"dark"` 直接按明亮处理。

`BuildCustom` 现在从主色推导：

```csharp
colors["WindowBase"] = Blend(light.WindowBase, accent, 0.06);
colors["SidebarBg"]  = Blend(accent 透明底, accent, 0.12);
colors["RingStroke"] = Blend(accent, white, 0.35);
colors["ShadowColor"] = Blend(accent, black, 0.72);
```

外加 `Accent / AccentDeep / AccentWash / *Tint / PrimaryFill / InkOnAccent` 全部跟随主色，
所以换成粉色/橙色时整个界面都跟着变，而不是绿按钮配灰底。
## [1.1.4-beta.20] - 2026-11-14

### 更新内容

- Mod 列表的筛选项（全部 / 启用 / 禁用 / 可更新）加入滑动高亮

### 技术实现

新增 `SegmentItemPillOverlay`（选中文字转白），高亮块用 `AccentDeep` 底色，
随选中项滑动；计数文字变化会改变宽度，`Refresh` / `SyncUpdateFilter` 后都重新量一次。

筛选项可能被数据隐藏（全部启用时不显示「启用」），此时高亮跟到仍可见的选中项，
若选中项本身不可见则收起。
## [1.1.4-beta.19] - 2026-11-14

### 更新内容

- 下载页「社区资源」分组：标题与子项的悬浮底色不再贴着分组边框

### 技术实现

撤回上一版对高亮块宽度的限制（判断错了对象，那次改动已 revert）。
真正的问题是分组 `Border` 只有纵向内边距：

```xml
<Border … Padding="0,4">   →   Padding="6,4"
```

「社区资源」标题和子项自身的悬浮底色（白色 wash）会横向顶到分组边缘，
看起来像框套框、没对齐。给 6px 横向内边距后，悬浮框和选中块都缩进到分组内部。
## [1.1.4-beta.17] - 2026-11-14

### 更新内容

- 修高亮块的宽度/位置在首次显示时量错（胶囊比按钮窄、文字露出）
- 高亮块颜色改为跟随主题（`AccentWash`），暗色下不再是一块亮绿
- 重做暗色主题：中性灰底 + 薄荷点缀，去掉原来的墨绿浑浊感

### 技术实现

`SlidingHighlight` 首次摆放时控件可能还没完成布局，`ActualWidth` 量到的是旧值；
现在布局结束后会再校一次（只补一次，避免循环）。

暗色色板重排：背景 `#0E1113`、卡片 `#1E2427`、浮层 `#1B2227`，
文字改为中性白 `#E8ECEA`，主色提亮到 `#7AD7A1`，描边统一 15%~33% 白。
## [1.1.4-beta.16] - 2026-11-14

### 更新内容

- 设置页的两组分段按钮（内存模式、外观主题）加入滑动高亮
- 下载页「正在搜索」的蒙层改成圆角，不再露出直角
- 修复设置页首次打开时导航高亮不出现

### 技术实现

分段控件新增 `SegmentItemOverlay` 样式（不画选中底色），高亮由独立 `Border` 承担，
复用 `SlidingHighlight`，位置与宽度按实际布局量。

首次不出现的原因是 `Loaded` 时控件还没参与布局，`ActualWidth` 为 0——
`SlidingHighlight` 现在对 `animate: false` 也允许重试一次。

`LoadingOverlay` 原来直接把 `SidebarBg` 铺在 `UserControl` 上（矩形），
改为内部 `Border CornerRadius="18"`。
## [1.1.4-beta.15] - 2026-11-14

### 更新内容

- 修复下载页「社区资源」收起后，高亮块还留在空白处

### 技术实现

折叠只是把子项 `Visibility` 关掉，高亮块是同一层里的独立元素，不会跟着消失。
于是记住当前选中的单选项，折叠时判断它是否还可见：

```csharp
if (_currentSource is null || !_currentSource.IsVisible) SourceIndicator.Opacity = 0;
else { SourceIndicator.Opacity = 1; SlidingHighlight.Move(...); }
```

展开分组时再放回去，位置按实际布局重新量。
## [1.1.4-beta.14] - 2026-11-14

### 更新内容

- 切换社区资源分类时先清空旧结果，不再「标题变了、列表还是上一个分类」

### 技术实现

`Configure` 里只重置了页码就发起新搜索，`ResultsList` 仍挂着上一个分类的卡片，
加载指示器又盖在上面，于是出现「数据包」标题下显示 Mod 结果的画面。

```csharp
ClearResults();   // ItemsSource = null，页码/总数一并归零
_ = RunSearchAsync(0);
```
## [1.1.4-beta.13] - 2026-11-14

### 更新内容

- 修复顶栏高亮块错位：现在和页签共用同一坐标系

### 技术实现

高亮块原来挂在 `NavInner` 下，而页签 `NavTabs` 自身带 `TranslateTransform Y=-6` 且居中，
两者坐标系不同，算出来的偏移自然差了一截（截图里高亮块压在页签字上、上沿还越出导航条）。

现在把页签和高亮包进同一个 `Grid x:Name="NavTabLayer"`，位移/淡入挂在整层上，
高亮相对该层定位，与页签完全同源：

```xml
<Grid x:Name="NavTabLayer">
  <TranslateTransform Y="-6" />
  <Border x:Name="NavTabIndicator" … />
  <StackPanel x:Name="NavTabs" …>…</StackPanel>
</Grid>
```

`SetNavExpanded` 里的透明度动画也一并改到这一层。
## [1.1.4-beta.12] - 2026-11-14

### 更新内容

- 设置页（主设置，不是版本设置）的侧栏也换成滑动高亮
- 高亮颜色加深：`#E6C9EDD5` → `#F28FD8A8`
- 修复顶栏高亮位置偏移、下载页/设置页首次进入时高亮不出现

### 技术实现

#### 1. 顶栏偏移

指示器原来是 `VerticalAlignment=Center`，而位置又按 `TransformToAncestor` 算了一遍 Y，
两者叠加导致高亮偏下且高度溢出。改成 `VerticalAlignment=Top`，只由位移决定位置。

#### 2. 首次进入

`Source_Checked` / `Nav_Checked` 在 XAML 解析阶段就会触发，此时控件还没有尺寸。
现在 `Loaded` 时统一补一次（`animate: false` 直接就位），避免高亮块不出现。

#### 3. 颜色

四个导航（顶栏、版本设置、下载页、设置页）统一用 `#F28FD8A8`，比之前的浅绿明显一档。
## [1.1.4-beta.11] - 2026-11-14

### 更新内容

- 只去掉「检查更新」这条提示结尾的句号，不再全局裁剪标点

### 技术实现

上一版在 `ShowToast` 里统一 `TrimEnd('。', '.', ' ')`，影响面太大。
现已撤回，改为只调整检查更新的那条文案本身。
## [1.1.4-beta.10] - 2026-11-14

### 更新内容

- 主界面顶栏（启动 / 下载 / 设置）加入同款滑动高亮
- 下载页左侧来源导航加入同款滑动高亮
- 气泡提示统一去掉结尾句号

### 技术实现

新增 `Controls/SlidingHighlight`：位置与尺寸都从目标控件的实际布局里量，
所以不等宽的页签、52/38 混排的侧栏共用同一套实现。

```csharp
var offset = target.TransformToAncestor(container).Transform(new Point(0, 0));
// 同时动画 Width / Height / TranslateX / TranslateY，230ms SineEase
```

`NavTab` 与 `SidebarSubItem` 的选中底色一并移除，改由高亮块表达；
顶栏收起时高亮随页签一起淡出。
## [1.1.4-beta.9] - 2026-11-14

### 更新内容

- 切换完成反馈重做：不再做几何回弹，改为高亮柔光轻亮一下
- 侧栏切换加快：380ms → 230ms
- 检查 Mod 更新后同样回到列表顶部（之前只有更新后才会）

### 技术实现

回弹改缩放会显得"弹一下"，改成动画柔光的透明度更安静：

```csharp
_indicatorShadow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse); // 0.18 → 0.42 → 0.18
```

柔光由代码创建（XAML 里的 `DropShadowEffect` 可能被冻结，无法动画）。

检查更新的收尾补上 `_scrollTopPending = true`，由 `ApplyFilter` 在换完数据后滚到第 1 行。
## [1.1.4-beta.8] - 2026-11-14

### 更新内容

- 更新完成后回到第 1 行：改成在列表数据换上之后再滚，不再依赖时机

### 技术实现

之前的做法是更新完立刻 `Refresh(); ScrollToTop();`，滚动可能发生在列表重建之前。
现在只置一个标记，由 `ApplyFilter` 在换完 `ItemsSource` 后执行：

```csharp
ModList.ItemsSource = list;
…
if (_scrollTopPending) { _scrollTopPending = false; ScrollToTop(); }
```

`ScrollToTop` 也简化成「让第一行进入视野」，容器未实例化时退回 `PART_Scroll` 滚到顶。
## [1.1.4-beta.7] - 2026-11-14

### 更新内容

- 侧栏切换完成后加一个轻微回弹，作为「已切好」的反馈
- 修复 Mod 更新完成后列表不回到顶部

### 技术实现

#### 1. 落定回弹

滑动动画的 `Completed` 里给胶囊做一次 1 → 1.045 → 1 的缩放（220ms）。
连点会叠加动画，所以用序号丢弃过期的回调：

```csharp
var sequence = ++_indicatorSequence;
slide.Completed += (_, _) => { if (sequence == _indicatorSequence) PlayIndicatorSettle(); };
```

#### 2. 回顶失效

原来靠遍历 `ModList` 的可视树找 `ScrollViewer`，列表刚换 `ItemsSource` 时可能还没建好，
于是静默什么都不做。改成直接向模板要具名部件，并在布局完成后再补一次：

```csharp
ModList.ApplyTemplate();
var viewer = ModList.Template?.FindName("PART_Scroll", ModList) as ScrollViewer;
```

同时记一行日志（`mods scroll top: 30 -> 0`），以后再失效可以直接从日志判断是没找到还是没生效。
## [1.1.4-beta.6] - 2026-11-14

### 更新内容

- 侧栏切换更柔：去掉选中加粗，改为 380ms 正弦缓动 + 柔光边缘
- 更新检查的遮罩改成圆角卡片，不再露出四个直角

### 技术实现

#### 1. 去掉字重跳变

选中项原来会把 `FontWeight` 改成 SemiBold，切换完成的瞬间文字会「啪」地变粗。
字重本身无法平滑过渡，直接去掉，选中状态只由胶囊表达。

#### 2. 更柔的移动

```csharp
var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
var duration = TimeSpan.FromMilliseconds(380);
```

胶囊加了一点 `DropShadowEffect`（BlurRadius 14 / Opacity 0.18），边缘不再是硬边；
底色改成 `#E6C9EDD5`（90% 不透明），和侧栏底色过渡更自然。

#### 3. 遮罩圆角

原来蒙层是一个铺满的 `Border`，四角是直角。现在拆成两层：

```xml
<Border Background="#66FFFFFF" CornerRadius="18" />   <!-- 蒙层 -->
<Border Background="{DynamicResource SurfaceSolid}" CornerRadius="16" />  <!-- 提示卡 -->
```
## [1.1.4-beta.5] - 2026-11-14

### 更新内容

- 侧栏高亮改为更小的胶囊，缓动换成对称 `CubicEase`，切换过程看得见「走」
- 引用版本对话框的「开始引用」按钮不再被裁切
- 检查更新的提示改为「其余 N 个未找到新版」
- 检查更新的遮罩更淡，并加了淡入淡出
- 没有版本时彻底卸下设置页（不再在空状态后面跑页面）

### 技术实现

- 指示器：高 44、圆角 12、1px 主色描边，垂直居中于 52 高的条目；240ms `CubicEase.EaseInOut`
- 对话框按钮：去掉固定 `Width=88`，改 `MinWidth=88` + `Padding=18,0`，文案再长也不会截断
- 遮罩：`#59FFFFFF` + 180ms 淡入 / 140ms 淡出，`Completed` 后再 `Collapsed`
- 空状态：`RefreshEmptyState` 在无版本时 `PageHost.Content = null`、侧栏置灰、隐藏指示器
## [1.1.4-beta.4] - 2026-11-14

### 更新内容

- 侧栏高亮改为「滑动 + 呼吸」：选中块在条目之间滑行，途中轻微收窄再回弹

### 技术实现

换成单块指示器 + `TransformGroup`（位移 + 缩放）：

```csharp
NavIndicatorSlide.BeginAnimation(TranslateTransform.YProperty,
    new DoubleAnimation(y, 300ms) { EasingFunction = new QuinticEase { EasingMode = EaseOut } });
```

滑动途中用关键帧让缩放走 1 → 0.965 → 1，模拟「被拖动时略微收窄」的手感；
因为始终是同一块元素，连续快速点击会从当前位置续着走，不会闪。

上一版的「双块交叉淡化」看起来像直接跳变（高亮先消失再出现），已经去掉。
## [1.1.4-beta.3] - 2026-11-14

### 更新内容

- 重新设计侧栏切换效果：高亮改为淡出 / 淡入，不再在条目之间滑行
- 悬停底色改中性灰，不再和选中态撞色
- 选中项文字保持深色，切换时不会出现「白字悬空」的一瞬
- 没有可用版本时不再初始化任何页面（空状态后面不再跑扫盘 / 更新检查）

### 技术实现

侧栏放两块等大的 `NavIndicator` / `NavIndicatorAlt` 交替使用：

```csharp
var current = _activeIndicator;
var next = ReferenceEquals(current, NavIndicator) ? NavIndicatorAlt : NavIndicator;
```

旧块 110ms 淡出，新块延迟 110ms 后用 150ms 淡入，中间不会出现两块同时高亮。
位置仍按 `TransformToAncestor` 取实际布局，不写死行高。

空状态：`Load` 在版本为空时直接 `return`，`PageHost.Content = null`、`NavPanel` 置灰；
首次从空状态导入版本后会自动接上新版本，不用重开浮层。
## [1.1.4-beta.2] - 2026-11-14

### 更新内容

- 版本设置侧栏的选中高亮改为滑动过渡：切换板块时色块在按钮之间移动

### 技术实现

侧栏项本身不再绘制选中底色（新增 `SidebarItemOverlay` 样式），选中块由一块独立的
`NavIndicator` 负责，用 `TranslateTransform` 在项之间滑动：

```csharp
y = target.TransformToAncestor(NavPanel).Transform(new Point(0, 0)).Y;
transform.BeginAnimation(TranslateTransform.YProperty,
    new DoubleAnimation(y, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
```

位置按实际布局量取，不写死行高；首次打开直接就位，之后切换才播放 220ms 过渡。

## [1.1.4-beta.1] - 2026-11-14

### 更新内容

- 版本设置每次打开都回到「概览」，不再停在上次关闭时的板块
- Mod 列表改为实时扫描，移除「刷新」按钮；外部增删文件会自动更新
- 打开 Mod 板块时自动检查一次更新（同一会话内切换板块不重复检查）
- 切换版本设置板块时回到顶部，并加入与主界面导航一致的淡入上浮动画
- Mod 更新完成后自动回到列表顶端

### 技术实现

#### 1. 打开即回概览

`Load()` 里重置选中项并显式调用 `ShowPage("overview")`：

```csharp
_modsAutoChecked = false;
_loaded.Clear();
NavOverview.IsChecked = true;
ShowPage("overview");
```

#### 2. 实时扫描

`ModManagePage` 用 `FileSystemWatcher` 监听 mods 目录，400ms 防抖后刷新：

```csharp
watcher.Created += (_, _) => ScheduleRefresh();
watcher.Renamed += (_, _) => ScheduleRefresh();
```

更新进行中（`_updating`）跳过监听触发的刷新，避免打断选中状态。

#### 3. 自动检查更新

`VersionSettingsView.ShowPage` 在进入 `mods` 时调用 `AutoCheckUpdates()`，
并用 `_modsAutoChecked` 保证每个设置会话只查一次；关闭浮层后 `Load()` 清零。

#### 4. 回到顶部与切换动画

`IInstancePage` 新增 `ScrollToTop()`，各页在 `ShowPage` 里以 `DispatcherPriority.Loaded` 调用
（等模板建好再找 ScrollViewer）；动画与主窗口导航一致：190ms 淡入 + 10px 上浮，`CubicEase`。
## [1.1.3] - 2026-11-03

### 更新内容

- Mod 管理支持多选与批量启用 / 禁用 / 更新 / 删除，删除前有确认
- 每行新增「位置」（资源管理器定位）与「信息」（MC百科详情页）
- 列表显示加载器与版本号；大小、元数据后台补全，打开页面不再卡顿
- 检查更新：两个平台并行查询、本地哈希缓存、更新结果持久化
- 更新 Mod 时页面内显示进度条，完成后自动收起并回到「全部」
- 更新合并多平台下载源，主源失败自动换源
- 图标透明区域直接显示界面背景，不再有色块闪烁
- 中文名匹配支持「(Fabric)」这类后缀，元数据损坏也能读出名字
- beta 版安装后的快捷方式命名为「Mutsumi Launcher beta」

### 技术实现

- Mod 列表改为「先渲染、后台补大小」，扫描阶段不再做同步 I/O
- 哈希改并行（4 路），并按「路径 + 大小 + 修改时间」缓存 SHA-1 与 Murmur2
- Modrinth 与 CurseForge 的检查用 Task.WhenAll 并发，结果在 UI 线程合并
- 下载槽位按活跃文件数分摊，避免同时下多个文件时互相饿死
- 下载速度改为 3 秒窗口采样：GlobalSpeedMb = (total - first.Bytes) / span
- 百科按钮统一走 McmodIndex.FindExact，命中直连 /class/{id}.html，否则退回搜索
- 删除旧的 McmodEntry / McmodService.Lookup 与一批不再使用的私有成员

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
