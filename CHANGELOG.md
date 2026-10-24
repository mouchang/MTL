# 更新日志

本项目从 0.1.0 开始记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 计划中
- 微软正版登录（等待 Mojang 应用审核）
- CurseForge 搜索 / 整合包
- OptiFine 安装
- 崩溃报告分析弹窗

## [1.0.0] - 2026-10-25

**MTL alpha v1.0 build 1**

首个公开 alpha 版本。包含 0.1.0 的全部功能，并清理了开发期的个人默认数据（离线账户、默认用户名等）。

## [0.1.0] - 2026-10-25

首个可用版本。

### 新增
- 原版 / Fabric / Forge / NeoForge 安装，PCL2 式合并为单一实例
- 实时监听版本目录，自动刷新版本列表
- 启动面板：进度、实例、Java、PID、日志、强制关闭，游戏退出自动收起
- 缺少 Java 时自动下载 Adoptium JRE
- 游戏异常退出时给出可读原因（崩溃分析）
- Modrinth 搜索、项目详情、前置展示、加载器筛选
- Mod 管理：启用 / 禁用 / 删除 / 从文件安装 / 更新检查
- 下载任务面板：进度、速度、取消、重试
- 整合包导入（.mrpack）与导出（.mrpack / .zip）
- 自动更新检查（支持 Cloudflare Worker 代理）
- 下载源切换（BMCLAPI / 官方 / MCIM）、自动代理探测
- NSIS 单文件自包含安装器（42MB，带卸载与快捷方式）

### 修复
- Fabric 合并实例缺 classpath 导致 `ClassNotFoundException`
- Fabric remap 残留 `.tmp` 导致启动失败
- `Program Files` 安装后游戏目录不可写
- Mod 跳转携带实例名而不是游戏版本
- 前置详情返回到搜索页而不是上一个详情页
