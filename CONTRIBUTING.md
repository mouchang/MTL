# 贡献指南

感谢你愿意参与 Mutsumi Launcher（MTL）。提交 PR 前请先阅读下面的约定。

## 环境

| 工具 | 版本 |
| --- | --- |
| .NET SDK | 10.0（`net10.0-windows`） |
| 系统 | Windows 10/11 x64 |
| NSIS（可选） | 只在打安装包时需要，便携版 NSIS 3.11 即可 |

> 项目坚持**零第三方 NuGet 依赖**；确需引入时请先在 Issue 里讨论。

## 本地构建

```powershell
# 还原 + 编译
dotnet build

# 运行
dotnet run --project src/MutsumiLauncher
```

发布 / 安装包见 [docs/packaging.md](docs/packaging.md)。

## 目录

```text
src/MutsumiLauncher/          启动器主程序（WPF）
installer/                    NSIS 安装脚本
worker/                       Cloudflare Worker（更新 / NeoForge 反代）
docs/                         文档与截图
```

## 代码风格

- 使用仓库根目录的 `.editorconfig`；
- C#：4 空格缩进、`Nullable` 开启、文件级 namespace；
- XAML：控件命名用 `PascalCase`，事件处理器 `Xxx_Click`；
- 用户可见文案统一中文，日志用英文/中文均可，但不要输出敏感信息。

## 提交信息

使用 `<type>: <中文简述>`：

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 修复 Bug |
| `docs` | 文档 |
| `build` | 构建 / 打包 / 依赖 |
| `refactor` | 重构（不改变行为） |
| `chore` | 杂项 |

示例：

```text
fix: Fabric 合并实例缺少 classpath 导致启动失败
docs: 补充 Worker 路由冲突排查
```

## PR 检查清单

- [ ] `dotnet build -c Release` 通过；
- [ ] 涉及 UI 的改动附截图（亮色/暗色主题都看一下）；
- [ ] 不提交 `bin/`、`obj/`、`dist/`、`game/`、日志和本地配置；
- [ ] 不提交任何 token / 密钥 / 账号信息；
- [ ] 若改动安装器或 Worker，同步更新 `docs/packaging.md` 或 `worker/README.md`。

## 安全

发现安全问题请**不要**开公开 Issue，按 [SECURITY.md](SECURITY.md) 私下联系。
