# Mutsumi Launcher 代理 Worker

一个 Worker 同时提供三件事：

| 路径 | 用途 | 谁在用 |
|---|---|---|
| `/gh/latest` | GitHub Releases 最新版本（带 token，缓存 5 分钟） | `UpdateService`（自动更新） |
| `/gh/download?url=...` | 代理 Release 安装包下载 | `UpdateService` |
| `/<host>/<path>` | 反代白名单站点（NeoForge maven 等） | `ForgeLoader`（`/maven.neoforged.net/...`） |
| `/cdn.modrinth.com/<path>` | **Modrinth 文件镜像**（墙外回源 + 边缘缓存） | `DownloadSources`（社区 Mod 下载兜底） |
| `/<forgecdn>/<path>` | CurseForge 文件镜像（同上） | `DownloadSources` |
| `/sub/config?did=...` | **订阅网关**：订阅链接留在 Cloudflare，客户端只拿 YAML | 启动器内置代理 |
| `/sub/status` | 网关状态（是否启用 / 模式） | 启动器设置页 |
| `/core/mihomo.zip` | **内核镜像**：mihomo release 缓存在边缘，下载内核不再依赖 GitHub | `ProxyCore` |

## 独立部署 Modrinth 镜像（不动现有 Worker）

如果账号里已经有别的 Worker 占着 `mtl.dpdns.org/*`（例如老的 `mtl-neoforge`），
不想合并，也可以单独建一个只做 Modrinth 镜像的 Worker：

1. Workers & Pages → 创建应用程序 → 创建 Worker，命名 `mtl-modrinth`
2. 编辑代码，粘贴 [`modrinth-mirror.js`](modrinth-mirror.js) 全文，部署
3. 该 Worker → 设置 → 域和路由 → 添加路由：`mtl.dpdns.org/cdn.modrinth.com/*`

更具体的路由（`mtl.dpdns.org/cdn.modrinth.com/*`）优先于 `mtl.dpdns.org/*`，
所以不会影响 NeoForge maven 那条。客户端不需要改任何配置。

## 订阅网关与内核镜像（内置代理用）

这两个功能已经合并成一个 Worker：`proxy-gateway.js`。

| 路径 | 用途 | 谁在用 |
|---|---|---|
| `/sub/config?did=...` | 下发 Clash YAML（订阅链接不出 Cloudflare） | SubscriptionGateway |
| `/sub/status` | 网关状态：来源（kv/upstream）、发布时间、节点数 | 排查用 |
| `/sub/publish` | 上传订阅（X-Publish-Key） | publish-sub.ps1 |
| `/core/mihomo.zip` | 内核 zip，支持 Range 断点续传 | ProxyCore |
| `/core/status` | 内核版本 / 大小 / SHA-256 | 排查用 |
| `/core/publish` | 上传内核 zip（X-Publish-Key） | publish-core.ps1 |

**部署步骤、日常换订阅、排查手册都在 [PROXY-SETUP.md](PROXY-SETUP.md)**

要点：

- 内核 zip 存在 KV 里（22 MB < 25 MB 上限），不再让 Worker 现去 GitHub 拉 ——
  国内用户第一次下载也不会卡在 GitHub 上
- 客户端会校验内核的 SHA-256（7aaf636a…864d），镜像被篡改也装不上
- 订阅优先级：KV 副本 → 回源 SUB_URL

---

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
curl -s https://mtl.dpdns.org/sub/status
curl -s https://mtl.dpdns.org/core/status
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
