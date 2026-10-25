# 更新日志

本项目从 0.1.0 开始记录。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.1] - 2026-10-25

**MTL alpha v1.0.1 build 1**

- 原版资源默认切换 MC 光速镜像（bsmirror.hkmc.online）
- 社区资源默认「聚合」：同时展示 Modrinth 与 CurseForge，并标注来源
- CurseForge 走 MCIM 镜像，详情/版本/前置/安装与 Modrinth 共用同一套界面
- 内置 VC++ 运行库，启动失败写 startup-error.log
- 清理旧安装器、占位数据与无用资源

## [Unreleased]

### 修复
- 原版镜像回退 BMCLAPI（MC 光速镜像国内不可达）
- 社区聚合固定显示 Modrinth + CurseForge 两份结果，并按下载量降序排列
- CurseForge 镜像不再受 Modrinth 下载源设置影响

### 计划中
- 微软登录（等待 Mojang 应用审核）
- OptiFine 安装

