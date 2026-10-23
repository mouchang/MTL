# Mutsumi Launcher

**MTL** · 一个面向 Minecraft 的桌面启动器（WPF / .NET 10）。

> 当前进度：`0.1.0` 界面骨架。启动、下载、设置三个页面可交互，业务逻辑（版本清单、Java 探测、下载、启动）尚未接入。

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
├─ src/MutsumiLauncher/
│  ├─ Themes/          # 调色板、控件样式（按钮/标签页/卡片/手风琴）
│  ├─ Views/           # MainWindow + Pages（Launch / Download / Settings）
│  ├─ ViewModels/      # 下载页的版本数据（占位）
│  ├─ Services/        # 日志
│  ├─ Assets/          # 背景、头像、图标
│  └─ app.manifest     # PerMonitorV2 DPI
├─ docs/screenshots/   # 界面截图
└─ MutsumiLauncher.slnx
```

## 界面

- **窗口**：1120×820（自动适配工作区 92%），26px 圆角，阴影，拖动标题栏移动，支持高 DPI。
- **启动页**：头像、正版标识、用户名（可复制）、版本胶囊、启动按钮；背景为整幅插画。
- **下载页**：左侧来源列表（原版资源 / 社区资源及其子类），右侧「最新版本」卡片 + 四个可折叠版本分组，切换页面时背景自动高斯模糊。
- **设置页**：外观、下载源、Java、数据目录占位项。
- **汉堡菜单**：点击左上角按钮，导航条会从按钮本身的形状向右生长成整条玻璃导航层（启动 / 下载 / 设置），右侧关闭按钮的底片同步溶解、与导航条融为一条；启动页上点击层外自动收起，子页上常驻；页面切换带淡入动画；底部有轻提示（Toast）。

![导航层动画](docs/screenshots/nav-expand.gif)

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
- [ ] 版本清单拉取（Mojang piston-meta）
- [ ] Java 探测与 JVM 参数
- [ ] 游戏启动与进程管理
- [ ] 资源下载、校验与断点续传
- [ ] 实例（版本隔离）管理

## 许可

待定（尚未添加 `LICENSE`）。MTL 为独立项目，与任何现有启动器无隶属关系。
