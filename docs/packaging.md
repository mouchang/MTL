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
