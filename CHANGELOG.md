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

### 资源与下载源（对齐 PCL2）
- 镜像映射补齐：依赖库同时走 `bmclapi/maven` 与 `bmclapi/libraries` 两条路径，
  Forge / Fabric / NeoForge 的 maven 也纳入镜像（加载器依赖镜像优先、官方兜底）
- 社区文件地址生成官方多变体（edge / mediafilez / media / overwolf）+ MCIM 镜像，去重后按源偏好排序
- Modrinth API 支持多源回退：auto 官方优先、MCIM 兜底；mcim 模式镜像优先、官方兜底
- 版本清单与文件下载拆成两套源偏好（设置页「版本清单」独立选项），清单内容不合格自动换源
- 清单获取失败不再显示内置假版本，改为明确报错并提示刷新
- 下载引擎：按状态码分级禁用下载源（404 / 502 / 416 / 校验失败，非 BMCLAPI 的 403、429）；
  所有源都失败后自动降级为「单线程 + 不续传」再重试一轮
- 已存在的客户端 jar / 依赖库按 大小 + SHA-1 校验，一致就跳过下载；资源索引同样校验
- 依赖库直接用清单里的文件大小，省掉每次下载前的 HEAD 探测
- auto 模式下 BMCLAPI / MCIM 走直连，避免系统代理与国内镜像 SSL 握手失败
- 新增本地文件复用（PCL2 的 CheckExistingFile）：下载前在本地实例目录、官方启动器
  `.minecraft\libraries` / `.minecraft\versions` 里按「文件名 / jar 大小 + SHA-1」
  找同一份文件直接复制；新装同版本实例不再重复下载客户端 jar

### 下载
- 新增下载限速（全局令牌桶）：所有下载线程共用一份带宽额度，设置页可选
  不限速 / 1 MB/s / 4 MB/s / 10 MB/s

### 社区资源
- 中文搜索：内置常见 Mod 中英别名表（可在数据目录放 `search-aliases.json` 扩充），
  中文关键词自动翻译成英文再搜，结果栏提示实际使用的搜索词
- 聚合搜索按项目分组：同一资源在 Modrinth / CurseForge 的两条结果挨在一起（组内 Modrinth 在前），
  组内按下载量取最大值参与排序；分页按组切分，避免同一资源的两个来源被拆到两页
- 同一平台重复返回的结果（接口偶发）不再重复展示
- CurseForge 文件列表改为翻页获取：之前只请求第一页（50 个），JEI 这类 3900+ 文件的项目
  版本列表是残缺的；现在按页翻取、空页自动重试，单项目最多取 300 个
- 游戏版本标签统一排序与显示：正式版按版本号排（1.21.10 > 1.21.1 > 1.21），
  快照 / 预览版加「快照」「预览版」后缀并排在对应位置；整合包只显示最新的正式版游戏版本

### 加载器
- 新增 OptiFine 安装：BMCLAPI 版本列表 → 官方安装器命令行 → 合并成独立实例
- 安装器在临时 `.minecraft` 里运行（子进程 APPDATA 重定向），不碰用户真实目录
- 安装器产出的客户端 jar 优先作为实例 jar，兼容老版本 OptiFine 的补丁逻辑
- 实例信息识别 OptiFine；下载页 OptiFine 行可选版本（默认只列正式版）

### 修复
- 社区文件下载校验失败时自动回退官方 CDN（Modrinth / 整合包）
- 聚合结果跨页全局按下载量排序，不再按来源分页堆积
- 版本列表按自然顺序排列（1.21 排在 1.9 前面），不再受字符串排序影响
- 下载页「已安装」标记能认出合并后的实例名（如 1.21.1-Fabric-0.20.1）
- User-Agent 与启动参数里的版本号改为真实版本，不再写死 0.1.0

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
