# 代理网关部署（订阅 + 内核，一个 Worker）

`proxy-gateway.js` 一个 Worker 管两件事：

| 端点 | 作用 |
| --- | --- |
| `GET /sub/config?did=<设备ID>` | 下发 Clash YAML（KV 副本优先，没有则回源 `SUB_URL`） |
| `GET /sub/status` | 状态：来源、发布时间、节点数（不含订阅链接） |
| `POST /sub/publish` | 上传 YAML，需要 `X-Publish-Key` |
| `GET /core/mihomo.zip` | 内核 zip（KV 里存着就直接给，支持 `Range` 断点续传） |
| `GET /core/status` | 内核版本 / 大小 / SHA-256 |
| `POST /core/publish` | 上传内核 zip，需要 `X-Publish-Key` |

设计上的两个取舍：

- **订阅链接永远不进客户端**：客户端只知道 Worker 地址和自己生成的设备 ID。
- **内核存在 KV 里**：不再让 Worker 现去 GitHub 拉（那条路又慢又容易被掐），
  冷下载一次 22 MB 存进 KV，之后所有客户端都从 Cloudflare 边缘拿。

---

## 一、一次性部署

### 1. 建 KV

控制台：**Storage & Databases → KV → Create instance**，命名 `SUB_KV`。

或者命令行：

```bash
cd worker
npx wrangler kv namespace create SUB_KV
# 记下返回的 id
```

### 2. 建 Worker

控制台：**Workers & Pages → Create → Worker**，命名 `mtl-proxy`，
把 [`proxy-gateway.js`](proxy-gateway.js) 全文粘进去后 **Deploy**。

命令行：

```bash
npx wrangler deploy -c proxy-gateway.wrangler.toml
```

### 3. 路由

Worker → **Settings → Domains & Routes → Add route**，加两条：

```
mtl.dpdns.org/sub/*
mtl.dpdns.org/core/*
```

比 `mtl.dpdns.org/*` 更具体，不会影响 `/gh`、`/maven.neoforged.net`、`/cdn.modrinth.com`。

### 4. 绑定与密钥

Worker → **Settings**：

| 名字 | 类型 | 用途 |
| --- | --- | --- |
| `SUB_KV` | KV namespace | 存订阅副本 + 内核 zip |
| `SUB_URL` | Secret（可选） | 订阅回源地址，KV 为空时才会用到 |
| `PUBLISH_KEY` | Secret | 上传订阅 / 内核时校验，随便一个长随机串 |

`SUB_ENABLED` 是普通变量，默认 `1`，设成 `0` 可以一次性停掉所有人的内置代理。

### 5. 推内容

```powershell
cd worker

# 订阅
.\publish-sub.ps1 -SubscriptionUrl "https://机场/订阅链接" -PublishKey "你的 PUBLISH_KEY"

# 内核（不传 -Path 会先从 GitHub 下）
.\publish-core.ps1 -PublishKey "你的 PUBLISH_KEY"
```

两个脚本都会自动校验并回读确认。

---

## 二、日常换订阅

**推荐**：改 `SUB_URL` 就行，Worker 会自己回源。

```
Worker → Settings → Variables and Secrets → SUB_URL → 编辑 → 粘贴新链接 → Deploy
```

然后用探测确认新机场能不能被 Cloudflare 访问到：

```bash
curl -s "https://mtl.dpdns.org/sub/status" | jq .source
```

> 注意：只要 KV 里有副本，`/sub/config` 就**永远走 KV**，`SUB_URL` 只在 KV 为空时生效。
> 想让新订阅立刻生效，要么跑一次 `publish-sub.ps1`，要么删掉 KV 里的 `sub:yaml`。

**机场封 Cloudflare 出口 IP 的时候**（回源 403 / 超时）：只能走本机推送：

```powershell
.\publish-sub.ps1 -SubscriptionUrl "https://机场/订阅链接" -PublishKey "..."
```

脚本用 `clash.meta` 的 UA 在你本机拉一次 YAML，再 POST 到网关存进 KV。

---

## 三、排查

```bash
# 网关整体状态
curl -s https://mtl.dpdns.org/sub/status

# 实际下发的配置（-H 看 X-MTL-Source 是 kv 还是 upstream）
curl -sI "https://mtl.dpdns.org/sub/config?did=selftest-device-0001&fresh=1"

# 内核信息
curl -s https://mtl.dpdns.org/core/status

# 续传是否正常：应该返回 206 + Content-Range
curl -sI -H "Range: bytes=0-1023" https://mtl.dpdns.org/core/mihomo.zip
```

| 现象 | 原因 |
| --- | --- |
| `/sub/config` 502 `no-subscription` | KV 里没有 `sub:yaml`，且 `SUB_URL` 没配或回源失败 |
| `/sub/config` 403 `device-not-allowed` | KV 里 `mode=closed`，且没有对应的 `allow:<设备ID>` |
| `/sub/publish` 404 `publish-disabled` | 没设 `PUBLISH_KEY` |
| `/sub/publish` 501 `kv-missing` | 没绑 `SUB_KV` |
| `/core/publish` 413 `too-large` | zip 超过 24 MB（KV value 上限 25 MB） |
| `/core/mihomo.zip` 502 | KV 里没有内核，且回源 GitHub 失败 → 跑 `publish-core.ps1` |

---

## 四、客户端对应关系

| 客户端代码 | 用的端点 |
| --- | --- |
| `Services/SubscriptionGateway.cs` | `/sub/config?did=`（读 `X-MTL-Source` 写日志） |
| `Services/ProxyCore.cs` `CoreUrls` | `/core/mihomo.zip`（失败再试 GitHub） |

客户端会校验内核 zip 的 **SHA-256**（`7aaf636a…864d`），镜像被篡改也装不上。
## CurseForge 官方 API（独立 Worker：mtl-curseforge）

官方 Key 不能打进客户端（公开分发的 exe 里任何字符串都能被抠出来），
所以「拿 Key 去请求」放在一个**单独的 Worker** 里：

```
客户端 → mtl.dpdns.org/cf/v1/... → mtl-curseforge 加 x-api-key → api.curseforge.com
```

### 部署

1. **建 Worker**：Workers & Pages → Create Worker，命名 `mtl-curseforge`，
   粘贴 [`curseforge-gateway.js`](curseforge-gateway.js) 全文 → Deploy
2. **加 Secret**：Settings → Variables and Secrets → 新增 Secret `CURSEFORGE_KEY`，粘贴官方 Key
3. **加路由**：Settings → Domains and Routes → `mtl.dpdns.org/cf/*`
4. **验证**：

```bash
curl -s https://mtl.dpdns.org/cf/status
# {"configured":true,"cacheTtl":300}

curl -s "https://mtl.dpdns.org/cf/v1/mods/search?gameId=432&classId=6&pageSize=3&sortField=6&sortOrder=desc" | head -c 120
```

或用 wrangler：

```bash
cd worker
npx wrangler deploy -c curseforge-gateway.wrangler.toml
npx wrangler secret put CURSEFORGE_KEY -c curseforge-gateway.wrangler.toml
```

### 参数

| 变量 | 类型 | 说明 |
| --- | --- | --- |
| `CURSEFORGE_KEY` | Secret | 官方 API Key |
| `CF_CACHE_TTL` | 文本（可选） | GET 结果缓存秒数，默认 `300`，设 `0` 关闭 |

### 客户端行为

- 默认后端就是 `https://mtl.dpdns.org/cf/v1`（`AppConfig.CurseForgeProxy`）
- 反代不可用（403 / 404 / 502 / 503 …）时自动退回社区镜像，日志里写一行
- 客户端**不接触**任何 API Key

### 排查

| 现象 | 原因 |
| --- | --- |
| `/cf/status` 返回 503 `cf-key-missing` | 没设 `CURSEFORGE_KEY` |
| `/cf/status` 返回 404 | 路由 `mtl.dpdns.org/cf/*` 没加，请求落到了别的 Worker |
| 客户端日志出现 `改用社区镜像` | 反代不可用，已自动降级（功能不受影响，只是慢） |
## Modrinth API 反代（独立 Worker：mtl-modrinth-api）

和 CurseForge 那个同样的思路，但 Modrinth 的公开接口**不需要 Key**：
只是把请求挪到 Cloudflare 边缘，国内更稳，GET 结果缓存 5 分钟。

```
客户端 → mtl.dpdns.org/md/v2/... → mtl-modrinth-api → api.modrinth.com/v2/...
```

### 部署

1. **建 Worker**：Workers & Pages → Create Worker，命名 `mtl-modrinth-api`，
   粘贴 [`modrinth-gateway.js`](modrinth-gateway.js) 全文 → Deploy
2. **加路由**：Settings → Domains and Routes → `mtl.dpdns.org/md/*`
3. （可选）变量 `MD_CACHE_TTL`，默认 `300` 秒
4. **验证**：

```bash
curl -s https://mtl.dpdns.org/md/status
# {"ok":true,"cacheTtl":300}

curl -s "https://mtl.dpdns.org/md/v2/search?query=sodium&limit=2" | head -c 120
```

### 客户端行为

- `AppConfig.ModrinthProxy` 默认 `https://mtl.dpdns.org/md`，请求基址是它 + `/v2`
- 反代不可用（403 / 404 / 5xx）时自动退回 `api.modrinth.com`，搜索不会失败
- 带 Personal Access Token 的请求不缓存（Worker 里按 `Authorization` 判断）

### 注意

现成的 `mtl-modrinth` Worker 是**文件镜像**（`cdn.modrinth.com`），
和这次的 `mtl-modrinth-api`（API 反代）是两件事，不要合并。
