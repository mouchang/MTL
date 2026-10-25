# Mutsumi Launcher

[![GitHub](https://img.shields.io/badge/GitHub-mouchang%2FMTL-2E3E35?logo=github)](https://github.com/mouchang/MTL)
[![Release](https://img.shields.io/github/v/release/mouchang/MTL?label=release&color=4E9E72)](https://github.com/mouchang/MTL/releases/latest)
[![Build](https://github.com/mouchang/MTL/actions/workflows/build.yml/badge.svg)](https://github.com/mouchang/MTL/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-MIT-4E9E72)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-8FD8A8)](#)

**MTL** · 一个面向 Minecraft 的 Windows 桌面启动器，WPF + .NET 10，**零第三方 NuGet 依赖**。

> 当前版本：**MTL alpha v1.0.1 build 2**（`1.0.1`） · [下载安装包](https://github.com/mouchang/MTL/releases/latest)

![启动页](docs/screenshots/launch.png)

## 功能

### 安装
- 原版 / Fabric / Forge / NeoForge / OptiFine，支持选择任意游戏版本
- 下载源自动选择（官方 / BMCLAPI 测速）+ 手动切换，失败自动回退；版本清单与文件下载分开设置
- 依赖库 / 资源 / 社区文件都有镜像映射（BMCLAPI maven+libraries、MCIM），加载器 maven 镜像优先
- 社区文件官方 CDN 优先、MCIM 回退
- **PCL2 式下载引擎**：全局线程池、Range 多段并发、源失败切换、流式 SHA-1 校验
- 社区文件官方 CDN 优先、MCIM 回退（与 PCL2 默认一致）
- SHA-1 校验、多任务下载与实时速度
- 本地同文件复用：装新实例时直接复制已有客户端 jar / 依赖 / Mod，不重复下载

### 启动
- 自动探测本机 Java；**没有合适 Java 时自动下载 Adoptium JRE**
- 每个实例可单独设置 Java、内存、版本隔离
- 启动面板：进度、实例、Java、PID、日志、**强制关闭**
- 游戏正常退出自动收起面板；异常退出时**分析日志并给出可读原因**
- 启动前清理 Fabric remap 残留、独立临时目录

### 社区与 Mod
- **社区资源聚合**：统一资源层并行搜索 Modrinth 与 CurseForge；同一资源合成一张卡片，
  顶部 Modrinth / CurseForge 标签可就地切换来源，按下载量降序
- 搜索支持中文关键词：内置常见 Mod 中英别名表，中文会被翻译成英文再查
- 可限制下载带宽（不限速 / 1 / 4 / 10 MB/s）
- 详情 / 版本 / 前置 / 下载全部走同一套资源模型，不再按平台分叉
- Modrinth / CurseForge 统一详情页、版本列表与安装流程
- CurseForge 暂走 MCIM 镜像，后续接入官方 API
- 加载器 / 游戏版本筛选；从 Mod 管理跳转时自动带入当前实例
- Mod 管理：启用 / 禁用 / 删除 / 从文件安装 / **检查更新**
- 资源包、光影、数据包安装（可确认安装位置）

### 数据与账号
- 离线账号、微软正版登录（等待 Mojang 应用审核）
- 整合包导入（Modrinth `.mrpack` / CurseForge / MMC）与导出（`.mrpack` / `.zip`）
- 实时监听 `versions/`，安装或删除版本后界面自动刷新
- 内置日志查看器、文件夹浏览器（不依赖系统资源管理器）

### 发布
- **兼容精简版 Windows**：自带 VC++ 运行库，启动失败会写 `startup-error.log`
- NSIS 单文件安装器：自包含、无需 .NET 运行时、带卸载与快捷方式
- 自动更新：GitHub Releases + 可选 Cloudflare Worker 代理，失败自动直连回退
- GitHub Actions：push 自动编译，推 `v*` tag 自动构建并发布安装包

## 界面

| 启动页 | 下载页 |
| --- | --- |
| ![启动页](docs/screenshots/launch.png) | ![下载页](docs/screenshots/download.png) |

### 启动面板

点击「启动游戏」后从右侧滑出，显示进度、进程与日志，并支持强制关闭：

![启动面板](docs/screenshots/launch-panel.gif)

![启动面板](docs/screenshots/launch-panel.png)

### 社区资源

![Modrinth 搜索](docs/screenshots/download-community.png)

### 版本设置

| Mod 管理 | 导出实例 |
| --- | --- |
| ![Mod 管理](docs/screenshots/version-mods.png) | ![导出](docs/screenshots/version-export.png) |

## 技术栈

| 项 | 选择 |
| --- | --- |
| 运行时 | .NET 10（`net10.0-windows`，LTS） |
| UI | WPF，无边框圆角窗口 + 自绘标题栏、玻璃拟态控件 |
| 第三方依赖 | **零**（优先官方 API / 自绘控件） |
| 架构 | 视图 + 轻量 ViewModel；页面级 UserControl 切换 |
| 安装包 | NSIS（自包含，约 43 MB，附 VC++ 运行库） |

## 运行与构建

需要 .NET 10 SDK 与 Windows 10/11：

```powershell
# 运行
dotnet run --project src/MutsumiLauncher

# 编译
dotnet build -c Release

# 自包含发布
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj `
  -c Release -r win-x64 --self-contained true -p:Version=1.0.0 -o dist/publish
```

调试时可用环境变量直达某个页面：

```powershell
$env:MTL_START_PAGE = "download"   # launch / download / settings
```

打安装包、发版流程见 [docs/packaging.md](docs/packaging.md)。

## 目录

```text
MutsumiLauncher/
├─ src/MutsumiLauncher/          # 启动器主程序
│  ├─ Themes/                   # 调色板、控件样式
│  ├─ Views/                    # MainWindow、页面、版本设置、浮层
│  ├─ ViewModels/               # 列表项数据模型
│  ├─ Services/                 # 安装、启动、下载、更新、账号等核心逻辑
│  ├─ Assets/                   # 背景、头像、图标
│  └─ app.manifest              # PerMonitorV2 DPI + 管理员权限
├─ installer/MutsumiLauncher.nsi # NSIS 安装脚本
├─ worker/                       # Cloudflare Worker（更新代理 / NeoForge 反代）
├─ docs/                         # 文档与界面截图
├─ .github/workflows/            # Build / Release 自动构建
└─ MutsumiLauncher.slnx
```

## 文档

| 文档 | 说明 |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | 版本更新记录 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 环境、代码风格、提交规范 |
| [SECURITY.md](SECURITY.md) | 安全问题报告方式 |
| [docs/packaging.md](docs/packaging.md) | 发布与打包流程 |
| [worker/README.md](worker/README.md) | Cloudflare Worker 部署与排查 |

## 路线图

- [x] 原版 / Fabric / Forge / NeoForge / OptiFine 安装与合并实例
- [x] 启动面板、崩溃原因、Java 自动下载
- [x] Modrinth 搜索、Mod 管理、更新检查
- [x] 下载任务（进度 / 速度 / 取消 / 重试）
- [x] 整合包导入导出
- [x] 自动更新（Cloudflare 代理 + 直连回退）
- [x] NSIS 单文件安装器
- [ ] 微软登录（等待 Mojang 应用审核）
- [ ] CurseForge（等待 API 审核）

## 许可

[MIT](LICENSE) © 2026 mouchang。MTL 为独立项目，与任何现有启动器无隶属关系。
