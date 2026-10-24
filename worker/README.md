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

## 路由冲突排查

如果 `https://mtl.dpdns.org/gh/latest` 返回 `403 forbidden`，但
`/maven.neoforged.net/...` 正常，说明请求仍然落在**旧的 maven Worker** 上：

- 旧 Worker 如果绑定的是 **Custom Domain `mtl.dpdns.org`**，Custom Domain 的优先级高于 Routes，
  新 Worker 的 `/gh/*` 路由不会生效；
- 解决方式（二选一）：
  1. 把旧 Worker 改成 Route 绑定：`mtl.dpdns.org/maven.neoforged.net/*`，
     新 Worker 绑定 Route：`mtl.dpdns.org/gh/*`（更具体的路径优先）；
  2. 直接用一个 Worker 同时处理三类请求（本目录 `worker.js` 就是合并版），
     替换旧 Worker 代码后重新部署。
