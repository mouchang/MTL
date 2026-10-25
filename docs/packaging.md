# 打包发布

## 一键发布（GitHub Actions）

推送 `v*` tag 会自动完成：

1. `dotnet publish` 自包含发布（含 VC++ 运行库，见下）；
2. 安装 NSIS 并编译 `installer/MutsumiLauncher.nsi`；
3. 创建 GitHub Release，上传 `MutsumiLauncher-<版本>-Setup.exe`。

```bash
git tag v1.0.0
git push origin v1.0.0
```

也可以在 Actions → Release → Run workflow 手动输入 tag。Release 标题从
`.github/release-title.txt` 读取。

> 自动更新读取 `releases/latest`，发布资产的文件名必须包含 `Setup`。

## 手动打包（NSIS）

需要 .NET 10 SDK 与便携版 NSIS 3.11（`makensis.exe`）。

```powershell
# 1. 自包含发布
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Version=1.0.0 -p:DebugType=None -o dist\publish

# 2. 编译安装器（输出 dist\MutsumiLauncher-1.0.0-setup.exe）
"\path\to\nsis-3.11\makensis.exe" /DAPP_VERSION=1.0.0 installer\MutsumiLauncher.nsi
```

安装器行为：

- 单文件、自包含，目标机器不需要 .NET 运行时；
- 启动时申请管理员权限，默认装到 `C:\Program Files\Mutsumi Launcher`；
- 自带 `uninstall.exe`，写入「应用和功能」，创建开始菜单 / 桌面快捷方式；
- 文件直接解压到安装目录，**不依赖 `%TEMP%`**（精简系统也能装）。

## 兼容性说明

精简版 Windows 常见启动失败原因与处理：

| 问题 | 处理 |
| --- | --- |
| 缺少 `vcruntime140.dll` / `msvcp140.dll` | 发布时会把 VC++ 运行库复制到程序目录（`CopyVCRuntime` target） |
| UAC 被关闭，`requireAdministrator` 程序无法启动 | 启动器本体现为 `asInvoker`，只有安装器需要管理员 |
| 游戏数据在 Program Files 不可写 | `GamePaths` 自动回退 `%APPDATA%\.mutsumi` |
| 启动失败但无提示 | 会在程序目录或 `%TEMP%` 写 `startup-error.log` 并弹窗 |

发布产物在 `dist/`，已被 `.gitignore` 忽略。
