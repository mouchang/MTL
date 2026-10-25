# 更新日志

本项目从 0.1.0 开始记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.1] - 2026-10-25

**MTL alpha v1.0.1 build 2**

- 原版资源使用 BMCLAPI（bmclapi2.bangbang93.com），Forge 安装器同样走 BMCLAPI
- 社区资源默认「聚合」：并行搜索 Modrinth + CurseForge，同一资源两边各出现一次并标注来源，结果按下载量降序
- CurseForge 固定走 MCIM 镜像，详情/版本/前置/安装与 Modrinth 共用同一套界面
- 内置 VC++ 运行库，启动失败写 `startup-error.log`
- 清理旧安装器、占位数据与无用资源
- 社区文件校验失败时自动回退官方 CDN（Modrinth / 整合包）
- 聚合结果跨页全局按下载量排序，不再按来源分页堆积

## [Unreleased]

### 加载器
- 新增 OptiFine 安装：BMCLAPI 版本列表 → 官方安装器命令行 → 合并成独立实例
- 安装器在临时 `.minecraft` 里运行（子进程 APPDATA 重定向），不碰用户真实目录
- 安装器产出的客户端 jar 优先作为实例 jar，兼容老版本 OptiFine 的补丁逻辑
- 实例信息识别 OptiFine；下载页 OptiFine 行可选版本（默认只列正式版）

### 修复
- 社区文件下载校验失败时自动回退官方 CDN（Modrinth / 整合包）
- 聚合结果跨页全局按下载量排序，不再按来源分页堆积

### 下载源（对齐 PCL2）
- 新增统一 `DownloadSources`：原版官方/BMCLAPI 自动测速排序，社区官方优先、MCIM 回退
- 设置页新增「自动选择」选项（原版 / 社区均默认自动）
- 资源索引、libraries、assets、Forge 安装器、Fabric meta、Mod 文件全部走统一源列表
- Fabric 依赖库增加 BMCLAPI maven 镜像回退

### 资源获取（对齐 PCL2）
- 新增 `ResourceService`：搜索 / 项目 / 版本 / 前置 / 下载统一入口，界面不再区分 Modrinth / CurseForge
- 搜索分页、聚合排序、平台筛选统一由资源层处理
- 详情页、前置跳转、外部项目页全部走统一模型
- 图标保持直连，不走下载镜像

### 下载引擎（PCL2 对齐）
- 源超时切换、失败计数/禁用
- 分块可从不同源下载，单片失败自动换源
- 低速/无数据 10 秒断开并换源
- 全局线程池 + 多段并发保留

### 整合包与搜索
- 新增 CurseForge 整合包导入（manifest.json + overrides/client-overrides）
- 新增 MMC / Prism 整合包导入（mmc-pack.json + minecraft/ 目录）
- 搜索新增排序：下载量 / 更新时间 / 相关度（默认下载量）
- 相关度聚合时按来源交替展示
- 项目/版本元数据增加磁盘缓存（6 小时）

### 资源更新
- 本地 Mod 同时支持 Modrinth SHA-1 与 CurseForge 指纹反查更新
- CurseForge 更新自动取当前版本/加载器下最新文件

### 下载引擎
- 参考 PCL2 重构：全局线程池、HTTP Range 多段并发、源失败切换、流式 SHA-1 校验
- 大文件（Mod / 整合包 / client jar）自动分块，小文件与 BMCLAPI/GitHub 保持单线程
- 版本依赖、Fabric/Forge 依赖改为并发下载

### 计划中
- 微软登录（等待 Mojang 应用审核）
- OptiFine 安装
