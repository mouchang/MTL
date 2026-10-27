# MTL beta v1.0.0 build 2

小版本，只改了一件事：**更新检查的版本比较**。

## 下载

| 形式 | 文件 |
| --- | --- |
| 安装版 | `MutsumiLauncher-1.0.0-beta.2-Setup.exe` |
| 便携版 | `MutsumiLauncher-1.0.0-beta.2-portable.zip` |

自包含发布，不需要装 .NET 运行时；启动器与安装器都需要管理员权限。

## 改了什么

以前的版本比较会把 `1.0.0-beta` 截成 `1.0.0`，后缀直接丢掉，于是：

- beta 构建认为自己就是 `1.0.0`，同版本会误报「发现新版本」
- 两个 beta 之间（`1.0.0-beta` → `1.0.0-beta.2`）永远收不到更新

现在按完整语义化版本比较：

```text
1.0.0-alpha < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0 < 1.0.1
```

程序集版本存不下 `-beta` 这类后缀，所以另外用 `AssemblyMetadata` 把 `$(Version)` 原样写进程序集，运行时读到的就是完整版本号。

## 从 build 1 升级

build 1 的比较逻辑有问题，收不到这次更新 —— 直接下载覆盖安装即可。从 build 2 开始，后续 beta 更新都能正常收到。

## 完整记录

见 [CHANGELOG.md](https://github.com/mouchang/MTL/blob/main/CHANGELOG.md)。界面与功能的大改动都在 [build 1](https://github.com/mouchang/MTL/releases/tag/v1.0.0-beta) 里。
