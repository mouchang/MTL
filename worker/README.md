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

## 独立部署 Modrinth 镜像（不动现有 Worker）

如果账号里已经有别的 Worker 占着 `mtl.dpdns.org/*`（例如老的 `mtl-neoforge`），
不想合并，也可以单独建一个只做 Modrinth 镜像的 Worker：

1. Workers & Pages → 创建应用程序 → 创建 Worker，命名 `mtl-modrinth`
2. 编辑代码，粘贴 [`modrinth-mirror.js`](modrinth-mirror.js) 全文，部署
3. 该 Worker → 设置 → 域和路由 → 添加路由：`mtl.dpdns.org/cdn.modrinth.com/*`

更具体的路由（`mtl.dpdns.org/cdn.modrinth.com/*`）优先于 `mtl.dpdns.org/*`，
所以不会影响 NeoForge maven 那条。客户端不需要改任何配置。

## 订阅网关（内置代理用）

[`sub-gateway.js`](sub-gateway.js) 让启动器**永远拿不到订阅链接**：

```
启动器（只知道 Worker 地址 + 设备 ID）
   └─ GET /sub/config?did=<deviceId>
        Worker 用 SUB_URL（secret）去拉订阅 → 缓存 55 分钟 → 返回 Clash YAML
```

| 响应 | 含义 |
|---|---|
| `200 text/yaml` | 正常，正文为 Clash 配置；`subscription-userinfo` 头带流量信息 |
| `400 {"enabled":false,"reason":"bad-device"}` | deviceId 格式不对 |
| `403 {"enabled":false,"reason":"device-not-allowed"}` | 白名单模式且该设备未授权 |
| `502 {"enabled":false,"reason":"upstream"}` | 订阅端拉取失败（有缓存时会用缓存） |
| `503 {"enabled":false,"reason":"disabled"}` | 已被远程停用 |

### 部署

```bash
cd worker
npx wrangler secret put SUB_URL          # 粘贴订阅链接，只存 Cloudflare
npx wrangler deploy -c sub-gateway.wrangler.toml
```

路由 `mtl.dpdns.org/sub/*` 写在 `sub-gateway.wrangler.toml` 里，比 `mtl.dpdns.org/*` 更具体，
不会影响 NeoForge / Modrinth 那两个 Worker。

> 订阅端按 UA 返回不同格式：网关固定用 `User-Agent: clash-verge/...` 请求，
> 否则拿到的是面板 HTML 而不是 Clash YAML。

### 开关与白名单（可选 KV）

不绑 KV 就是**开放模式**（任何合法 deviceId 都能拿配置），先跑通再收紧：

```bash
npx wrangler kv namespace create SUB_KV    # 把返回的 id 写进 sub-gateway.wrangler.toml

# 1) 全员停用（客户端自动回退直连）
npx wrangler kv key put --binding SUB_KV enabled "0"

# 2) 切到白名单模式
npx wrangler kv key put --binding SUB_KV mode "closed"

# 3) 放行某台设备（设备 ID 在启动器「设置 → 下载源 → 内置代理」里显示）
npx wrangler kv key put --binding SUB_KV "allow:3f2a1c40-..." "1"
```

### 验证

```bash
curl -s "https://mtl.dpdns.org/sub/status"
curl -s -o clash.yaml -w "%{http_code} %{size_download}\n" \
  "https://mtl.dpdns.org/sub/config?did=test-device-0001"
head -3 clash.yaml
```

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

# Modrinth 文件镜像（第一次回源，之后 CF-Cache-Status: HIT）
curl -o /dev/null -w "%{http_code} %{size_download}\n" \
  "https://mtl.dpdns.org/cdn.modrinth.com/data/P7dR8mSH/versions/tCBJwbEV/fabric-api-0.165.0%2B26.4.jar"
curl -I "https://mtl.dpdns.org/cdn.modrinth.com/data/P7dR8mSH/versions/tCBJwbEV/fabric-api-0.165.0%2B26.4.jar" | grep -i cf-cache-status
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
