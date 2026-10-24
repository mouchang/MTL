# Mutsumi Launcher

[![GitHub](https://img.shields.io/badge/GitHub-mouchang%2FMTL-2E3E35?logo=github)](https://github.com/mouchang/MTL)
[![Build](https://github.com/mouchang/MTL/actions/workflows/build.yml/badge.svg)](https://github.com/mouchang/MTL/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-MIT-4E9E72)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-8FD8A8)](#)

**MTL** · 一个面向 Minecraft 的桌面启动器（WPF / .NET 10）。

> 当前版本 `MTL alpha v1.0 build 1`（1.0.0）。支持原版 / Fabric / Forge / NeoForge 安装与启动、Modrinth、整合包导入导出、自动更新。微软正版登录等待 Mojang 应用审核。

| 文档 | 说明 |
| --- | --- |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 环境、构建、代码风格、提交规范 |
| [CHANGELOG.md](CHANGELOG.md) | 版本更新记录 |
| [SECURITY.md](SECURITY.md) | 安全问题报告方式 |
| [docs/packaging.md](docs/packaging.md) | NSIS 打包、发布流程 |
| [worker/README.md](worker/README.md) | Cloudflare Worker（更新代理 / NeoForge 反代） |

![启动页](docs/screenshots/launch.png)

![下载页](docs/screenshots/download.png)

## 技术栈

| 项 | 选择 |
| --- | --- |
| 运行时 | .NET 10（`net10.0-windows`，LTS） |
| UI | WPF，无边框圆角窗口 + 自绘标题栏 |
| 第三方依赖 | **零**（后续按需引入，优先官方包） |
| 架构 | 视图 + 轻量 ViewModel；页面级 UserControl 切换 |

## 运行

```powershell
# 直接跑
dotnet run --project src/MutsumiLauncher

# 只编译
dotnet build

# 发布单文件（后续接入发布流程）
dotnet publish src/MutsumiLauncher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

调试时可用环境变量直达某个页面：

```powershell
$env:MTL_START_PAGE = "download"   # launch / download / settings
```

## 目录

```text
MutsumiLauncher/
├─ src/MutsumiLauncher/          # 启动器主程序
│  ├─ Themes/                   # 调色板、控件样式
│  ├─ Views/                    # MainWindow、Pages、VersionSettings、浮层
│  ├─ ViewModels/               # 列表项数据模型
│  ├─ Services/                 # 安装、启动、下载、更新、账号等核心逻辑
│  ├─ Assets/                   # 背景、头像、图标
│  └─ app.manifest              # PerMonitorV2 DPI + 管理员权限
├─ src/MutsumiLauncher.Setup/    # 旧版自定义安装器（保留）
├─ installer/MutsumiLauncher.nsi # NSIS 安装脚本
├─ worker/                       # Cloudflare Worker（更新代理 / NeoForge 反代）
├─ docs/                         # 文档与界面截图
├─ .github/workflows/            # Build / Release 自动构建
└─ MutsumiLauncher.slnx
```

## 界面

- **窗口**：1120×820（自动适配工作区 92%），26px 圆角，阴影，拖动标题栏移动，支持高 DPI。
- **启动页**：头像、正版标识、用户名（可复制）、版本胶囊、启动按钮；背景为整幅插画。
- **下载页**：左侧来源列表（原版资源 / 社区资源及其子类），右侧「最新版本」卡片 + 四个可折叠版本分组，切换页面时背景自动高斯模糊。
- **设置页**：外观、下载源、Java、数据目录占位项。
- **汉堡菜单**：点击左上角按钮，导航条会从按钮本身的形状向右生长成整条玻璃导航层（启动 / 下载 / 设置），右侧关闭按钮的底片同步溶解、与导航条融为一条；启动页上点击层外自动收起，子页上常驻；页面切换带淡入动画；底部有轻提示（Toast）。
- **版本设置浮层**：启动页版本胶囊右侧的齿轮打开，在同一窗口内从底部滑出并覆盖整窗（含标题栏，主界面按钮同步溶解）；浮层自带关闭按钮，关闭时向下滑回；左侧「概览 / 设置 / Mod 管理 / 导出」四页，玻璃卡片 + 自绘下拉框 / 勾选框 / 单选按钮 / 滑杆。
  - 概览：版本信息、个性化、快捷方式、高级管理（导出启动脚本 / 补全文件 / 删除版本）
  - 设置：启动选项、内存分配（滑杆 + 内存占用条）、服务器、高级选项折叠区、全局设置入口
  - Mod 管理：名称搜索、启用状态过滤、回到顶部悬浮按钮
  - 导出：可勾选的导出内容树（Mod / 资源包 / 光影包 / 启动器程序等）、高级选项与配置读写入口
- **社区资源搜索**：下载页切到 mod / 整合包 / 数据包 / 资源包 / 光影包 后，右侧变为对应分类的搜索页（独立标题、搜索占位与结果数据）；Mod 页多一个「任意 Mod 加载器」下拉，整合包页多一个「安装已有整合包」按钮；「原版资源」仍显示版本清单。

### 通用 UI 组件

- **搜索框**：放大镜图标 + 占位文字 + 一键清空按钮，Mod 管理与社区搜索共用同一控件。
- **加载遮罩**：半透明蒙层 + 旋转弧线 + 文案，搜索/加载时覆盖内容区。
- **消息对话框**：玻璃风确认框，支持 Esc 取消 / Enter 确定；「删除版本」等危险操作走它确认。
- **提示气泡 / 细滚动条**：全局替换系统默认 ToolTip 与 ScrollBar 样式。


![版本设置](docs/screenshots/version-overview.png)

![社区搜索](docs/screenshots/download-community.png)

![导航层动画](docs/screenshots/nav-expand.gif)

## 已接入真实数据

- **断点续传**：客户端/依赖/资源下载中断后保留 `.tmp`，下次自动用 Range 续传，不再从头下。
- **下载速度**：下载任务面板实时显示 MB/s（按字节增量计算），客户端与依赖库下载过程中持续刷新。
- **默认中文**：安装完成与每次启动都会把 `options.txt` 的 `lang` 设为 `zh_cn`，新下载的实例同样生效。
- **启动前校验**：启动前检查 classpath 是否齐全，缺文件时给出「重新安装以补全」的提示；安装会清理 `.tmp` 残留并重下空文件。
- **下载浮标**：开始下载后离开下载页，右下角会出现浮标（显示进行中数量与总进度）；点击打开「下载任务」面板，列出每个任务的速度、进度、状态，支持多任务并存。
- **下载源设置**：设置页新增「下载源」子界面（右侧滑入），可分别为「原版资源」和「社区资源」选择官方源或镜像源；同页可设置**网络代理**（直接连接 / 跟随系统 / 自定义），默认直连——系统代理关掉后不会再导致所有请求失败。选择即时保存，代理模式重启后生效。
- **原版版本列表**：正式版 / 预览版 / 远古版 / 愚人节版本不再限量，清单里有多少显示多少。
- **社区搜索分页**：Modrinth 搜索结果按 20 条一页，列表底部显示「第 x / y 页 · 共 N 个结果」与页码按钮（上一页 / 页码 / 下一页）。
- **下载加速**：原版资源默认走 BMCLAPI（版本清单 / 版本 JSON / client / libraries / assets），社区资源默认走 MCIM（Modrinth API、CDN 文件与图标）；可在 `config.json` 用 `VanillaMirror` / `ModMirror` 设为 `official` 切回官方。
- **版本清单**：启动时从 `piston-meta.mojang.com` 拉取 `version_manifest_v2.json`，按类型拆成正式版 / 预览版 / 远古版 / 愚人节版本，并高亮最新正式版与预览版；数据缓存到本地（6 小时内直接读缓存），网络失败时回退缓存，再失败则显示内置示例并在卡片上提示。右侧「刷新」可强制重新拉取。

- **整合包导入**：下载页「导入整合包」支持 `.mrpack`，自动装原版 + 加载器（Fabric/Forge/NeoForge）、下载 `files[]`（SHA-1 校验）、解压 `overrides/`，并创建/切换实例。
- **导出整合包**：版本设置 → 导出，支持 `.mrpack`（Modrinth）和 `.zip`，可选 Mods / 配置 / options.txt / 资源包 / 光影 / 存档；mrpack 自动写入正确的 `dependencies`（minecraft + fabric-loader/forge/neoforge）。
- **启动日志**：概览页「查看启动日志」打开日志查看器，读取最新 `.log`，支持自动刷新、复制全部、打开日志文件夹。
- **实例管理**：版本设置里可查看实例路径/占用空间/Mod 数量，**重命名**（同步改目录、版本 JSON、实例设置），**删除实例**（整个目录），打开版本/Mod/存档/截图/日志文件夹。
- **每实例启动设置**：Java、内存、版本隔离按实例保存到 `instances/<实例>.json`，可「跟随全局」或单独指定。
- **Mod 管理**：原版（无加载器）实例显示「此版本不支持安装 Mod」并禁用安装按钮；「安装新 Mod」会跳到社区资源 Mod 页并预选该实例的版本与加载器；从跳转进入后安装默认落到该实例，并提供文件位置确认框；离开下载页后不再默认到该实例。
- **前置显示**：Mod/资源包每个版本都列出全部依赖（必需+可选），点击可跳转到对应项目。
- **Mod 管理**：扫描实例 mods 目录，支持启用/禁用（改后缀）、删除、从文件安装、打开文件夹。
- **PCL2 式合并安装**：装加载器时原版先装到临时位置，加载器装到临时版本目录，最后把两个 JSON 合并成 `versions/<实例>/<实例>.json`（删除 `inheritsFrom` / `jar`、复制原版 jar），原版临时目录删除——版本列表里只留一个完整实例，不再出现“本体 + 加载器”两个文件夹。
- **实例名称**：版本详情页可自定义实例名称（安装到 `versions/<实例名称>`），默认取版本号；选择加载器后自动建议 `26.3-Fabric-0.20.1` 这类不冲突的名字，重名会拒绝安装。
- **NeoForge CDN**：NeoForge 官方 maven 国内直连不通，安装器与内部下载地址会改写为 `https://mtl.dpdns.org/maven.neoforged.net/...`，失败自动回退官方。
- **Fabric API 选择**：选定 Fabric 加载器版本后，会额外列出该游戏版本可用的 Fabric API 版本（默认最新，可选「不安装」），安装时一起下到实例 mods。
- **加载器筛选**：Mod 详情页版本列表下方可按加载器（Fabric/Forge/NeoForge/Quilt）过滤；从版本设置跳转时会自动选中对应加载器。
- **加载器安装**：Fabric / Forge / NeoForge 均可展开选择版本、选中高亮、三者互斥（其余行禁用并显示「与所选加载器不互通」）。Fabric 走 meta profile；Forge/NeoForge 下载官方 installer 在临时目录执行 `--installClient`，再合并 `versions/` 与 `libraries/`。NeoForge 官方 maven 在国内直连不通，需要代理或镜像。
- **下载详情页**：原版版本列表和社区搜索结果都改为「点条目打开详情」，详情从右侧滑入（280ms）。原版详情展示版本信息、加载器（Forge / NeoForge / Fabric / OptiFine）与「开始下载」；Modrinth 详情展示项目图标、信息（下载量 / 分类 / 更新时间）、**全部受支持游戏版本**的标签（两行、横向滚动、带下方导航条、不限数量）、带加载器徽标的文件列表与逐个安装，另有「转到 Modrinth」「复制名称」。项目图标会下载到本地 `icons/` 缓存；版本来源目前是 Modrinth，CurseForge 客户端已留好骨架（配置 `CurseForgeApiKey` / `MTL_CURSEFORGE_KEY` 后即可并入）。
- **图标**：图标下载到本地 `icons/` 缓存并校验图片头，镜像失败时自动回退官方 CDN；原版条目使用官方材质渲染（正式版=草方块、远古版=泥土、预览版=命令方块、愚人节=金块），社区条目使用项目真实图标（本地缓存，失败回退首字母），详情页文件行带加载器徽标。
- **社区资源（Modrinth）**：搜索走 Modrinth 开放 API，版本下拉直接读取 `/v2/tag/game_version` 的正式版本，支持按游戏版本、加载器、类型筛选；从搜索页进入详情时会把当前选择的版本带过去并自动选中对应标签；详情页内按文件安装，按项目类型落到 `mods` / `resourcepacks` / `shaderpacks` / `datapacks`（开启版本隔离时装进 `versions/<版本>/`），文件带 SHA-1 校验。整合包目前下载 `.mrpack`，自动安装待做。Personal Access Token 可选：环境变量 `MTL_MODRINTH_TOKEN` 优先，其次本地 `config.json` 的 `ModrinthToken`（不会入库）。
- **Java 探测**：启动器扫描 `JAVA_HOME`、`PATH`、注册表（JavaSoft）、常见安装目录（Adoptium / Microsoft / Zulu / Corretto 等）以及 `.minecraft
untime`，优先读 JDK 自带 `release` 文件解析版本，失败再跑 `java -version`；结果去重后按主版本倒序展示。设置页可查看与重新检测，版本设置里的 Java 下拉直接使用这份列表。

- **版本安装**：下载页每个版本行都有「安装」，确认后依次拉取版本 JSON、client jar、依赖库（含 Windows natives），逐文件做 SHA-1 校验，落盘到游戏目录；顶部有进度条。

- **启动游戏**：启动页「启动游戏」会挑选已安装版本启动——解压 natives、拼 classpath、按 `minecraftArguments` / `arguments.game` 替换占位符、用离线 UUID，并按版本要求自动选 Java（1.16 及更早 → 8、1.17 → 16、1.18–1.20.4 → 17、1.20.5+ → 21）；游戏输出写入 `游戏目录\logs\<版本>-<时间>.log`。实测可启动 Beta 1.8.1。

- **资源文件**：安装与启动前都会检查 `assets/indexes/<id>.json`，按索引下载缺失的 objects（按哈希寻址、跳过已存在文件、12 并发），进度实时显示。
- **界面细节**：所有滚动条（含横向导航条）统一为胶囊形主题绿滑块，默认 8px，鼠标悬停放大 1.45 倍、拖动时放大 1.9 倍，松手回弹（120ms 动画）；侧栏选中项与「全部」标签同色高亮；启动页头像外框是比头像大两圈的圆角方形。
- **缓存管理**：设置页显示缓存大小与路径（版本清单缓存 + 项目图标缓存），一键清理并提示释放大小。
- **配置持久化**：玩家名、所选版本、Java 路径、内存分配、版本隔离写入 `config.json`；点击启动页的版本胶囊会从底部滑出「选择启动版本」浮层（带滑入/滑出动画），选中即记住；版本设置里的 Java / 内存 / 隔离改动即时保存并参与启动。

- **账号**：启动页头像旁的账号胶囊显示当前账号与类型（离线/正版），点 ⇄ 从底部滑出账号浮层，可添加离线账号（校验 3–16 位字母数字下划线）、切换、删除；账号列表与选择写入 `config.json`，启动时使用当前账号的名字与离线 UUID（`OfflinePlayer:<name>` 的 MD5）。

- **微软登录**：账号浮层里的「正版登录」弹出带淡入/上滑动画的窗口，显示设备码与验证链接，并**自动复制设备码、自动打开浏览器**；两处都有「复制」按钮兜底。授权后依次换取 Xbox Live / XSTS / Minecraft accessToken，拉取档案与皮肤；需要先在 `config.json` 填 `MicrosoftClientId`（或设置环境变量 `MTL_MS_CLIENT_ID`）。
- **皮肤头像**：微软账号下载皮肤 PNG，裁 8×8 正脸并叠加帽子层（64×64 皮肤），按最近邻放大显示；离线账号使用 `Assets/steve-head.png`（取自 Unoclix 的原版史蒂夫皮肤）。启动时微软账号使用 `accessToken` + `userType=msa`。

## 素材替换

界面用的是占位素材，可以直接替换同名文件：

| 文件 | 用途 | 建议 |
| --- | --- | --- |
| `src/MutsumiLauncher/Assets/background.jpg` | 窗口背景 | 横图，≥ 1920×1280，浅色系 |
| `src/MutsumiLauncher/Assets/avatar.png` | 玩家头像 | 8×8 像素图，会按最近邻放大 |
| `src/MutsumiLauncher/Assets/app.ico` | 应用与任务栏图标 | 多尺寸 ico（16/32/48/256） |

> 视觉效果以 `docs/screenshots/` 中的截图为准；占位背景只是临时氛围图。

## 路线图

- [x] 仓库初始化与工程骨架
- [x] 三页界面与导航（启动 / 下载 / 设置）
- [x] 版本清单拉取（Mojang piston-meta，带本地缓存与失败回退）
- [x] Java 探测与自动下载（Adoptium JRE）
- [x] 版本安装（版本 JSON、client jar、libraries，SHA-1 校验，进度条）
- [x] Fabric / Forge / NeoForge 安装与 PCL2 式合并实例
- [x] 启动游戏（natives 解压、classpath、参数替换、按版本自动选 Java、进程与日志）
- [x] 启动面板（进度 / PID / 日志 / 强制关闭 / 崩溃原因）
- [x] 资源文件（assets 索引 + objects，断点续传、并发下载）
- [x] 启动页版本切换、实时监听与配置持久化
- [x] 离线账号管理（添加 / 切换 / 删除，离线 UUID）
- [x] 微软正版登录（设备码 OAuth → Xbox Live → XSTS → Minecraft 服务，需自备 clientId）
- [x] 皮肤头像（微软账号拉皮肤裁正脸，离线默认史蒂夫）
- [x] Modrinth 搜索、详情、前置展示、加载器筛选
- [x] Mod 管理（启用 / 禁用 / 删除 / 从文件安装 / 更新检查）
- [x] 整合包 `.mrpack` 导入与导出（`.mrpack` / `.zip`）
- [x] 下载任务面板（进度 / 速度 / 取消 / 重试）
- [x] 自动更新（支持 Cloudflare Worker 代理）
- [x] NSIS 单文件自包含安装器
- [ ] 微软登录（等待 Mojang 应用审核）
- [ ] CurseForge 搜索与整合包（等待 API 审核）
- [ ] OptiFine 安装

## 许可

[MIT](LICENSE) © 2026 mouchang。MTL 为独立项目，与任何现有启动器无隶属关系。
