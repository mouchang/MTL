# 打包发布

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

## 安装器（Inno Setup 6）

```bash
# 需要 Inno Setup 6（ISCC.exe）
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/MutsumiLauncher.iss
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/MutsumiLauncher-portable.iss
```

- `MutsumiLauncher-<ver>-setup.exe`：完整安装（开始菜单/桌面快捷方式 + 卸载项）；
- `MutsumiLauncher-<ver>-setup-portable.exe`：便携安装（不写注册表、不建快捷方式），适合受限环境。

输出都在 `dist/`。
