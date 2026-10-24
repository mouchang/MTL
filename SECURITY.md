# 安全说明

## 报告问题

如果发现可能影响用户的安全问题（例如安装器提权、更新源被劫持、路径穿越、密钥泄露），
请**不要**直接开公开 Issue，通过下面任一方式私下联系：

- GitHub：[@mouchang](https://github.com/mouchang)
- 或在仓库的 **Security → Report a vulnerability** 里提交

请尽量包含：复现步骤、影响范围、相关日志（去掉账号与 token）。

## 支持范围

| 版本 | 是否接收安全修复 |
| --- | --- |
| 最新 Release | ✅ |
| 更早版本 | 视问题严重程度 |

## 给用户的提醒

- 不要把自己的 **GitHub PAT**、**Modrinth Token**、**微软账号**信息贴到 Issue / 截图里；
- 启动器不会向任何第三方上传你的账号密码；离线账号只保存在本地 `config.json`；
- 更新连接只访问 `github.com` 或你自己的 `releases.mtl.dpdns.org` 代理；
- 如果下载到的安装包校验异常，请停止运行并从 Release 页面重新下载。
