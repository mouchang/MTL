// MTL 代理网关（Cloudflare Worker）
// 一个 Worker 管两件事：
//   1) 订阅：/sub/config   —— 客户端只拿到 Clash YAML，订阅链接留在 Cloudflare
//   2) 内核：/core/mihomo.zip —— mihomo 的 zip 存在 KV 里，国内下载不再依赖 GitHub
//
// 路由：mtl.dpdns.org/sub/* 与 mtl.dpdns.org/core/*
//
// 绑定（都在 Workers → 设置里加）：
//   KV  SUB_KV           可选，存订阅副本 / 内核 zip；不绑也能用（只能回源）
//   Secret SUB_URL       可选，订阅回源地址（机场链接）
//   Secret PUBLISH_KEY   可选，上传订阅 / 内核时要校验的 key
//   变量 SUB_ENABLED     可选，默认 "1"
//
// 端点：
//   GET  /sub/config?did=<id>         Clash YAML（KV 优先，没有则回源 SUB_URL）
//   GET  /sub/status                  状态（来源、发布时间、节点数，不含订阅链接）
//   POST /sub/publish                 上传 YAML（X-Publish-Key）
//   GET  /core/mihomo.zip             内核 zip（KV 优先，否则回源 CORE_URL / GitHub）
//   GET  /core/status                 内核信息（版本、大小、SHA-256）
//   POST /core/publish                上传内核（X-Publish-Key）

const KV_SUB = "sub:yaml";
const KV_SUB_INFO = "sub:userinfo";
const KV_SUB_AT = "sub:updated";
const KV_CORE = "core:zip";
const KV_CORE_META = "core:meta";

const CACHE_SUB = "https://mtl.internal/clash.yaml";
const CACHE_CORE = "https://mtl.internal/mihomo.zip";
const CACHE_TTL = 3300;
const CORE_TTL = 30 * 24 * 3600;

// 机场普遍按 UA 决定返回 YAML 还是 base64 节点列表
const UPSTREAM_UA = "clash.meta";
const DEFAULT_CORE_URL =
  "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/mihomo-windows-amd64-compatible-v1.19.33.zip";

const DEVICE_PATTERN = /^[A-Za-z0-9-]{8,64}$/;
const MAX_CORE_BYTES = 24 * 1024 * 1024;

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: cors() });
    }

    if (url.pathname === "/sub/publish" || url.pathname === "/sub/publish/") {
      if (request.method !== "POST") return json(405, { ok: false, reason: "method" });
      return publishSubscription(request, env);
    }

    if (url.pathname === "/core/publish" || url.pathname === "/core/publish/") {
      if (request.method !== "POST") return json(405, { ok: false, reason: "method" });
      return publishCore(request, env);
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return json(405, { enabled: false, reason: "method" });
    }

    if (url.pathname === "/sub/status" || url.pathname === "/sub/status/") {
      return subscriptionStatus(env);
    }

    if (url.pathname === "/sub/config" || url.pathname === "/sub/config/") {
      return subscriptionConfig(request, env, ctx, url);
    }

    if (url.pathname === "/core/status" || url.pathname === "/core/status/") {
      return coreStatus(env);
    }

    if (url.pathname === "/core/mihomo.zip" || url.pathname === "/core/mihomo.zip/") {
      return coreDownload(request, env, ctx);
    }

    return json(404, { enabled: false, reason: "not-found" });
  },
};

// ---------------------------------------------------------------- 订阅

async function isEnabled(env) {
  if (env.SUB_ENABLED === "0") return false;

  const kv = await kvGet(env, "enabled");
  return kv !== "0";
}

async function subscriptionConfig(request, env, ctx, url) {
  const did = (url.searchParams.get("did") || "").trim();
  if (!DEVICE_PATTERN.test(did)) return json(400, { enabled: false, reason: "bad-device" });

  if (!(await isEnabled(env))) return json(503, { enabled: false, reason: "disabled" });

  // 白名单模式：KV 里 mode=closed 时才校验 allow:<did>
  const mode = (await kvGet(env, "mode")) || "open";
  if (mode === "closed" && (await kvGet(env, `allow:${did}`)) !== "1") {
    return json(403, { enabled: false, reason: "device-not-allowed" });
  }

  const force = url.searchParams.get("fresh") === "1";
  const loaded = await loadSubscription(env, ctx, force);
  if (!loaded) return json(502, { enabled: false, reason: "no-subscription" });

  const headers = new Headers({
    "Content-Type": "text/yaml; charset=utf-8",
    "Cache-Control": "no-store",
    "X-MTL-Sub": "ok",
    "X-MTL-Source": loaded.source,
    ...cors(),
  });

  if (loaded.userinfo) headers.set("subscription-userinfo", loaded.userinfo);

  return new Response(request.method === "HEAD" ? null : loaded.yaml, { status: 200, headers });
}

/** KV 副本优先；没有才回源 SUB_URL。 */
async function loadSubscription(env, ctx, force) {
  const cache = caches.default;
  const key = new Request(CACHE_SUB);

  if (!force) {
    const hit = await cache.match(key);
    if (hit) {
      return {
        yaml: await hit.text(),
        source: hit.headers.get("X-MTL-Source") || "cache",
        userinfo: hit.headers.get("subscription-userinfo") || "",
      };
    }
  }

  const stored = await kvGet(env, KV_SUB);
  if (stored) {
    const userinfo = (await kvGet(env, KV_SUB_INFO)) || "";
    const body = { yaml: stored, source: "kv", userinfo };
    ctx.waitUntil(cache.put(key, cacheable(body)));
    return body;
  }

  const upstream = normalizeUrl(env.SUB_URL);
  if (!upstream) return null;

  let response;
  try {
    response = await fetch(upstream, {
      headers: { "User-Agent": UPSTREAM_UA, Accept: "*/*" },
      redirect: "follow",
    });
  } catch {
    return null;
  }

  if (!response.ok) return null;

  const yaml = await response.text();
  if (!looksLikeClash(yaml)) return null;

  const body = {
    yaml,
    source: "upstream",
    userinfo: response.headers.get("subscription-userinfo") || "",
  };

  ctx.waitUntil(cache.put(key, cacheable(body)));
  return body;
}

function cacheable(body) {
  return new Response(body.yaml, {
    headers: {
      "Content-Type": "text/yaml; charset=utf-8",
      "X-MTL-Source": body.source,
      "subscription-userinfo": body.userinfo,
      "Cache-Control": "max-age=" + CACHE_TTL,
    },
  });
}

async function subscriptionStatus(env) {
  const yaml = await kvGet(env, KV_SUB);
  const publishedAt = await kvGet(env, KV_SUB_AT);
  const upstream = normalizeUrl(env.SUB_URL);
  const core = await kvGet(env, KV_CORE_META);

  return json(200, {
    enabled: await isEnabled(env),
    mode: (await kvGet(env, "mode")) || "open",
    source: yaml ? "kv" : upstream ? "upstream" : "none",
    publishedAt: publishedAt || null,
    upstreamHost: upstream ? new URL(upstream).host : null,
    nodeCount: yaml ? countNodes(yaml) : 0,
    bytes: yaml ? yaml.length : 0,
    core: core ? JSON.parse(core) : null,
  });
}

async function publishSubscription(request, env) {
  if (!env.PUBLISH_KEY) return json(404, { ok: false, reason: "publish-disabled" });
  if (!env.SUB_KV) return json(501, { ok: false, reason: "kv-missing" });

  const key = (request.headers.get("x-publish-key") || "").trim();
  if (key !== env.PUBLISH_KEY.toString().trim()) return json(403, { ok: false, reason: "bad-key" });

  const text = await request.text();
  if (!text || text.length < 200) return json(400, { ok: false, reason: "too-small" });
  if (!looksLikeClash(text)) return json(400, { ok: false, reason: "not-clash-yaml" });

  await env.SUB_KV.put(KV_SUB, text);
  await env.SUB_KV.put(KV_SUB_INFO, request.headers.get("x-sub-info") || "");
  await env.SUB_KV.put(KV_SUB_AT, new Date().toISOString());
  await caches.default.delete(new Request(CACHE_SUB));

  return json(200, { ok: true, bytes: text.length, nodes: countNodes(text) });
}

// ---------------------------------------------------------------- 内核

async function coreDownload(request, env, ctx) {
  const range = request.headers.get("range");

  // 1) KV 里存了内核：直接切片返回，支持断点续传
  if (env.SUB_KV) {
    const cached = await coreBytes(env);
    if (cached) return serveBytes(cached, range, request.method === "HEAD", "kv");
  }

  // 2) 没存：回源（默认 GitHub），整包透传，客户端自己负责重试
  const upstreamUrl = normalizeUrl(env.CORE_URL) || DEFAULT_CORE_URL;
  const key = new Request(CACHE_CORE);
  const cache = caches.default;

  if (!range) {
    const hit = await cache.match(key);
    if (hit) return relay(hit, request.method === "HEAD", "cache");
  }

  let response;
  try {
    response = await fetch(upstreamUrl, {
      headers: { "User-Agent": "MTL-Core-Mirror/2.0", ...(range ? { Range: range } : {}) },
      redirect: "follow",
    });
  } catch {
    return new Response("upstream unreachable", { status: 502 });
  }

  if (!response.ok && response.status !== 206) {
    return new Response("upstream " + response.status, { status: 502 });
  }

  if (response.status === 200 && !range) {
    ctx.waitUntil(cache.put(key, new Response(response.clone().body, {
      headers: { "Content-Type": "application/zip", "Cache-Control": "max-age=" + CORE_TTL },
    })));
  }

  return relay(response, request.method === "HEAD", "upstream");
}

let coreMemo = null;

async function coreBytes(env) {
  if (coreMemo) return coreMemo;

  const buffer = await env.SUB_KV.get(KV_CORE, "arrayBuffer");
  if (!buffer) return null;

  coreMemo = new Uint8Array(buffer);
  return coreMemo;
}

function serveBytes(bytes, range, head, source) {
  const total = bytes.byteLength;
  let start = 0;
  let end = total - 1;
  let status = 200;

  if (range) {
    const match = /^bytes=(\d*)-(\d*)$/.exec(range.trim());
    if (match) {
      if (match[1]) start = parseInt(match[1], 10);
      if (match[2]) end = parseInt(match[2], 10);
      if (Number.isNaN(start) || start >= total) {
        return new Response(null, { status: 416, headers: { "Content-Range": `bytes */${total}` } });
      }
      if (end >= total) end = total - 1;
      status = 206;
    }
  }

  const body = head ? null : bytes.slice(start, end + 1);

  return new Response(body, {
    status,
    headers: {
      "Content-Type": "application/zip",
      "Content-Disposition": 'attachment; filename="mihomo.zip"',
      "Accept-Ranges": "bytes",
      "Content-Length": String(end - start + 1),
      "Cache-Control": "public, max-age=86400",
      "X-MTL-Core": source,
      ...(status === 206 ? { "Content-Range": `bytes ${start}-${end}/${total}` } : {}),
      ...cors(),
    },
  });
}

function relay(response, head, source) {
  return new Response(head ? null : response.body, {
    status: response.status,
    headers: {
      "Content-Type": "application/zip",
      "Content-Disposition": 'attachment; filename="mihomo.zip"',
      "Accept-Ranges": "bytes",
      "Cache-Control": "public, max-age=86400",
      "X-MTL-Core": source,
      ...(response.headers.get("content-range")
        ? { "Content-Range": response.headers.get("content-range") }
        : {}),
      ...(response.headers.get("content-length")
        ? { "Content-Length": response.headers.get("content-length") }
        : {}),
      ...cors(),
    },
  });
}

async function coreStatus(env) {
  const meta = await kvGet(env, KV_CORE_META);

  return json(200, meta
    ? JSON.parse(meta)
    : { stored: false, upstream: normalizeUrl(env.CORE_URL) || DEFAULT_CORE_URL });
}

async function publishCore(request, env) {
  if (!env.PUBLISH_KEY) return json(404, { ok: false, reason: "publish-disabled" });
  if (!env.SUB_KV) return json(501, { ok: false, reason: "kv-missing" });

  const key = (request.headers.get("x-publish-key") || "").trim();
  if (key !== env.PUBLISH_KEY.toString().trim()) return json(403, { ok: false, reason: "bad-key" });

  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_CORE_BYTES) return json(413, { ok: false, reason: "too-large" });

  const bytes = await request.arrayBuffer();
  if (bytes.byteLength < 1024 * 1024) return json(400, { ok: false, reason: "too-small" });
  if (bytes.byteLength > MAX_CORE_BYTES) return json(413, { ok: false, reason: "too-large" });

  const meta = {
    stored: true,
    bytes: bytes.byteLength,
    sha256: request.headers.get("x-core-sha256") || "",
    version: request.headers.get("x-core-version") || "",
    updatedAt: new Date().toISOString(),
  };

  await env.SUB_KV.put(KV_CORE, bytes);
  await env.SUB_KV.put(KV_CORE_META, JSON.stringify(meta));
  coreMemo = null;
  await caches.default.delete(new Request(CACHE_CORE));

  return json(200, { ok: true, ...meta });
}

// ---------------------------------------------------------------- 工具

async function kvGet(env, key) {
  if (!env.SUB_KV) return null;
  try {
    return await env.SUB_KV.get(key);
  } catch {
    return null;
  }
}

function looksLikeClash(text) {
  return /(^|\n)\s*(proxies|proxy-groups)\s*:/.test(text);
}

function countNodes(yaml) {
  const block = yaml.split(/\r?\nproxies:\s*\r?\n/)[1];
  if (!block) return 0;

  const lines = block.split(/\r?\n/);
  let count = 0;

  for (const line of lines) {
    if (line.length > 0 && !/^\s/.test(line)) break;
    if (/^\s*-\s/.test(line)) count++;
  }

  return count;
}

function normalizeUrl(raw) {
  if (!raw) return null;

  const value = raw.toString().trim();
  if (!/^https?:\/\//i.test(value)) return null;

  try {
    new URL(value);
    return value;
  } catch {
    return null;
  }
}

function cors() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "content-type,x-publish-key,x-core-sha256,x-core-version,x-sub-info",
    "Access-Control-Allow-Methods": "GET,HEAD,POST,OPTIONS",
  };
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...cors() },
  });
}
