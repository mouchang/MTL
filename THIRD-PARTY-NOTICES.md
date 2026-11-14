# 第三方组件与参考实现说明

本项目源码以 MIT 许可发布。下面列出运行时依赖与「参考了其实现思路」的上游项目。

## PCL-CE（PCL 社区版）

- 地址：https://github.com/PCL-Community/PCL-CE
- 许可：Apache License 2.0（许可证原文见 `third_party/PCL-CE/LICENSE`）
- 参考内容：下载引擎的分源/换源与重试策略、分段下载与文件校验顺序、
  CurseForge 分页缺失重试、MC百科条目匹配与中文名处理、实例合并规则等。

这些改动均以 C# 重新实现并适配本项目的架构，未直接复制源文件；
如后续引入其源码片段，需在该文件补充对应的归属说明与许可证文本。

PCL-CE 上游为 [PCL](https://github.com/Meloong-Git/PCL)。

## 其他

- OpenBMCLAPI（https://github.com/bangbang93/openbmclapi）：原版游戏文件国内镜像
- MCIM（https://mcimirror.top）：CurseForge 接口社区镜像
- MC百科（https://www.mcmod.cn）：Mod 中文名与缩写数据（本项目自行抓取）
