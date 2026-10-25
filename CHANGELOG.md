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

### 下载引擎
- 参考 PCL2 重构：全局线程池、HTTP Range 多段并发、源失败切换、流式 SHA-1 校验
- 大文件（Mod / 整合包 / client jar）自动分块，小文件与 BMCLAPI/GitHub 保持单线程
- 版本依赖、Fabric/Forge 依赖改为并发下载

### 计划中
- 微软登录（等待 Mojang 应用审核）
- OptiFine 安装
