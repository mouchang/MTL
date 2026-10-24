# 打包发布

## 一键发布（GitHub Actions）

推送 `v*` tag 会自动完成：

1. `dotnet publish` 自包含发布；
2. 安装 NSIS 并编译 `installer/MutsumiLauncher.nsi`；
3. 创建 GitHub Release，上传 `MutsumiLauncher-<版本>-Setup.exe`。

```bash
git tag v0.1.0
git push origin v0.1.0
```

也可以在 Actions → Release → Run workflow 手动输入 tag。

> 自动更新读取的是 `releases/latest`，所以发布时**必须**附带文件名包含 `Setup` 的 exe，
> 否则启动器会认为没有可用更新。

自包含（推荐，用户无需安装 .NET）：

```bash
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj   -c Release -r win-x64 --self-contained true   -p:Version=0.1.0 -p:DebugType=None   -o dist/publish

powershell -Command "Compress-Archive -Path dist\publish\* -DestinationPath dist\MutsumiLauncher-0.1.0-win-x64.zip -Force"
```

框架依赖（体积小，需要 .NET 10 Desktop Runtime）：

```bash
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj -c Release -r win-x64 --self-contained false -o dist/publish-fx
```

打包产物在 `dist/`，已被 `.gitignore` 忽略，不会进仓库。

## NSIS 安装器（推荐，electron-builder 同款方案）

```powershell
# 下载便携版 NSIS 3.11（含 makensis.exe）并解压
Invoke-WebRequest "https://downloads.sourceforge.net/project/nsis/NSIS%203/3.11/nsis-3.11.zip" -OutFile nsis.zip
Expand-Archive nsis.zip -DestinationPath tools
sis

# 先发布自包含目录
dotnet publish src/MutsumiLauncher/MutsumiLauncher.csproj -c Release -r win-x64 `
  --self-contained true -p:Version=0.1.0 -p:DebugType=None -o dist\publish

# 编译安装器（输出 dist\MutsumiLauncher-0.1.0-setup.exe）
tools
sis
sis-3.11\makensis.exe installer\MutsumiLauncher.nsi
```

特点：单 exe、自包含（不需要 .NET 运行时）、按用户安装到 `%LOCALAPPDATA%\Programs\Mutsumi Launcher`、
自带卸载程序、开始菜单/桌面快捷方式；解压目标直接是安装目录，**不依赖 %TEMP%**。
