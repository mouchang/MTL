# MTL alpha v1.0.1 build 1

本次更新集中在「下载源」与「社区资源」两块。

## 更新内容

### 下载源
- 原版资源默认切换为 **MC 光速镜像**：`https://bsmirror.hkmc.online`
  - 版本清单、版本 JSON、client jar、libraries、assets 全部走该镜像；
  - Forge 安装器下载同样走光速镜像；
  - 镜像失败会按原有逻辑回退官方源。
- 设置页与下载源界面同步更新为「MC 光速镜像」。

### 社区资源
- 来源新增 **聚合（默认）**：
  - 同时搜索 Modrinth 与 CurseForge，结果交替展示；
  - 同一条资源会在两边各出现一次，并标注「Modrinth / CurseForge」来源；
  - 状态栏显示两边各自的命中数量。
- **CurseForge 接入方式与 Modrinth 完全一致**：
  - 统一详情页：标题、图标、作者、下载量、描述、版本 Tab、加载器筛选；
  - 统一安装流程：确认位置 → 下载 → 完成提示；
  - **前置依赖**同样展示（必需 / 可选），点击可跳到对应的 CurseForge 项目；
  - CurseForge 暂时统一走 MCIM 镜像（`mod.mcimirror.top/curseforge/v1`），无需 API Key；
    官方 API 的配置入口已预留。

### 兼容性
- 发布包内置 VC++ 运行库（`vcruntime140` / `msvcp140` / `concrt140`），
  修复精简版 Windows 缺少 DLL 导致无法启动的问题。
- 启动阶段异常会写入 `startup-error.log`（程序目录，失败则 `%TEMP%`）并弹窗提示。

### 清理
- 移除旧的自定义安装器工程与 Inno Setup 脚本；
- 移除未使用的占位数据、CurseForge 空壳代码、废弃配置字段与图片资源。

## 安装

下载下方 `MutsumiLauncher-1.0.1-Setup.exe`，右键以管理员身份运行（安装器需要管理员权限）。

- 默认安装到 `C:\Program Files\Mutsumi Launcher`；
- 覆盖安装会保留 `game/` 下的实例、存档与配置；
- 已安装 1.0.0 的用户也可以直接在启动器里「检查更新」。

## 说明

- 启动器本体仍需要管理员权限（与 1.0.0 一致）；
- 精简版 Windows 若仍无法启动，请把 `startup-error.log` 或错误弹窗内容反馈到 Issues。

**完整对比**：https://github.com/mouchang/MTL/compare/v1.0.0...v1.0.1
