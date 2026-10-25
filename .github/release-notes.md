# MTL alpha v1.0.1 build 1

本次更新集中在「下载源」与「社区资源」两块。

> 同版本号重发：v1.0.1 已替换为此构建，旧包请勿继续使用。

## 更新内容

### 下载源
- 原版资源使用 **BMCLAPI**（`https://bmclapi2.bangbang93.com`），国内直连稳定：
  - 版本清单、版本 JSON、client jar、libraries、assets 全部走 BMCLAPI；
  - Forge 安装器下载同样走 BMCLAPI；
  - 镜像失败会按原有逻辑回退官方源。
- 设置页与下载源界面同步显示「镜像源（BMCLAPI）」。

### 社区资源
- 来源新增 **聚合（默认）**：
  - 同时搜索 Modrinth 与 CurseForge，两边请求并行发出；
  - **同一条资源会以两个来源各出现一次**，并标注「Modrinth / CurseForge」；
  - 结果统一按 **下载量从高到低** 排序；
  - 状态栏显示两边各自的命中数量。
- **CurseForge 接入方式与 Modrinth 完全一致**：
  - 统一详情页：标题、图标、作者、下载量、描述、版本 Tab、加载器筛选；
  - 统一安装流程：确认位置 → 下载 → 完成提示；
  - **前置依赖**同样展示（必需 / 可选），点击可跳到对应的 CurseForge 项目；
  - CurseForge 固定走 MCIM 镜像（`mod.mcimirror.top/curseforge/v1`），
    不受 Modrinth 下载源设置影响，无需 API Key；官方 API 的配置入口已预留。

### 兼容性
- 发布包内置 VC++ 运行库（`vcruntime140` / `msvcp140` / `concrt140`），
  修复精简版 Windows 缺少 DLL 导致无法启动的问题。
- 启动阶段异常会写入 `startup-error.log`（程序目录，失败则 `%TEMP%`）并弹窗提示。

### 清理
- 移除旧的自定义安装器工程与 Inno Setup 脚本；
- 移除未使用的占位数据、CurseForge 空壳代码、废弃配置字段与图片资源。

## 安装

下载下方 `MutsumiLauncher-1.0.1-Setup.exe`，右键以管理员身份运行。

- 默认安装到 `C:\Program Files\Mutsumi Launcher`；
- 覆盖安装会保留 `game/` 下的实例、存档与配置；
- 已安装旧版 1.0.1 的用户请直接覆盖安装本次构建。

## 说明

- 启动器本体与安装器仍需要管理员权限；
- 精简版 Windows 若仍无法启动，请把 `startup-error.log` 或错误弹窗内容反馈到 Issues。

**完整对比**：https://github.com/mouchang/MTL/compare/v1.0.0...v1.0.1
