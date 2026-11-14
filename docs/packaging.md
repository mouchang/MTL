# 打包发布

## 一键发布（GitHub Actions）

推送 `v*` tag 会自动完成：

1. `dotnet publish` 自包含发布（含 VC++ 运行库，见下）；
2. 安装 Inno Setup 6.7+ 并编译 `installer/MutsumiLauncher.iss`；
3. 打包便携版 `MutsumiLauncher-<版本>-portable.zip`；
4. 创建 GitHub Release，上传 `Setup.exe` 与 `portable.zip`。

```bash
git tag v1.0.0
git push origin v1.0.0
```

也可以在 Actions → Release → Run workflow 手动输入 tag。Release 标题由 tag 自动生成：
正式版 `MTL Release vX.Y.Z`，带 `-` 的 tag 走 beta 通道，标题为 `MTL Beta vX.Y.Z-beta.N`。

> 自动更新读取 `releases/latest`，发布资产的文件名必须包含 `Setup`。

## 手动打包（Inno Setup）

需要 .NET 10 SDK 与 Inno Setup 6.7+（`ISCC.exe`）。

```powershell
# 1. 自包含发布
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Version=1.0.0 -p:DebugType=None -o dist\publish

# 2. 编译安装器（输出 dist\MutsumiLauncher-1.0.0-Setup.exe）
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAPP_VERSION=1.0.0 installer\MutsumiLauncher.iss
```

安装器行为：

- 单文件、自包含，目标机器不需要 .NET 运行时；
- 启动时申请管理员权限，默认装到 `C:\Program Files\Mutsumi Launcher`；
- 自带 `uninstall.exe`，写入「应用和功能」，创建开始菜单 / 桌面快捷方式；
- 安装程序会在 `%TEMP%` 里解压临时文件，**系统临时目录必须可写**（`D:\Temp` 之类的自定义路径没有写权限会报错 5）；
- 安装包与启动器都需要管理员权限，安装时会在向导开头弹 UAC。

## 兼容性说明

精简版 Windows 常见启动失败原因与处理：

| 问题 | 处理 |
| --- | --- |
| 缺少 `vcruntime140.dll` / `msvcp140.dll` | 发布时会把 VC++ 运行库复制到程序目录（`CopyVCRuntime` target） |
| UAC 被关闭 / 非管理员 | 启动器与安装器都需要管理员权限，请右键“以管理员身份运行” |
| 游戏数据在 Program Files 不可写 | `GamePaths` 自动回退 `%APPDATA%\.mutsumi` |
| 启动失败但无提示 | 会在程序目录或 `%TEMP%` 写 `startup-error.log` 并弹窗 |

发布产物在 `dist/`，已被 `.gitignore` 忽略。