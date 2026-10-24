# Mutsumi Launcher 代理 Worker

一个 Worker 同时提供三件事：

| 路径 | 用途 | 谁在用 |
|---|---|---|
| `/gh/latest` | GitHub Releases 最新版本（带 token，缓存 5 分钟） | `UpdateService`（自动更新） |
| `/gh/download?url=...` | 代理 Release 安装包下载 | `UpdateService` |
| `/<host>/<path>` | 反代白名单站点（NeoForge maven 等） | `ForgeLoader`（`/maven.neoforged.net/...`） |

## 部署

```bash
cd worker
npm i -D wrangler
npx wrangler login

# 设置 secret（不要写进代码）
npx wrangler secret put GITHUB_TOKEN

# 部署
npx wrangler deploy
```

`wrangler.toml` 里默认把 `mtl.dpdns.org/*` 指到这个 Worker；如果你的域名不同，改 `routes.pattern`。

## 环境变量

| 名称 | 类型 | 说明 |
|---|---|---|
| `REPO` | 文本 | 默认 `mouchang/MTL` |
| `GITHUB_TOKEN` | Secret | 只读公开仓库的 fine-grained PAT（Contents: Read） |
| `ALLOWED_HOSTS` | 文本（可选） | 逗号分隔的白名单，默认包含 NeoForge/Forge/Fabric maven |

## 启动器侧配置

```json
// config.json
"UpdateProxy": "https://mtl.dpdns.org/gh"
```

或环境变量 `MTL_UPDATE_PROXY`。

NeoForge CDN 已经硬编码为 `https://mtl.dpdns.org/maven.neoforged.net`，不需要额外配置。

## 验证

```bash
curl https://mtl.dpdns.org/gh/latest
curl -I https://mtl.dpdns.org/maven.neoforged.net/releases/net/neoforged/neoforge/21.1.66/neoforge-21.1.66-installer.jar
```
