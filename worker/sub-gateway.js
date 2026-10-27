// MTL 订阅网关（Cloudflare Worker）
// 路由：mtl.dpdns.org/sub/*
//
// 作用：启动器只请求这个 Worker，真正的订阅链接放在 Cloudflare Secret（SUB_URL）里，
// 永远不会进安装包、不会进仓库、也不会出现在客户端内存之外的地方。
//
// 环境变量（wrangler secret put）：
//   SUB_URL      订阅链接（secret，仅 Worker 可见）
//   SUB_ENABLED  "1" / "0"，全局开关（默认 1；KV 里的 enabled 优先级更高）
//
// 可选 KV（绑定名 SUB_KV）：
//   enabled = "0" | "1"     远程停用
//   mode    = "open" | "closed"   默认 open；closed 时只放行 allowlist
//   allow:<deviceId> = "1"  白名单条目
//
// 端点：
//   GET /sub/config?did=<deviceId>   -> Clash YAML（200）或 JSON（403/502/503，含 enabled:false）
//   GET /sub/status                  -> { enabled, mode, upstream }

const CONFIG_CACHE_KEY = "https://mtl-sub.internal/clash.yaml";
const CONFIG_CACHE_TTL = 3300; // 55 分钟，比订阅端 24 小时刷新更积极，避免换节点后长时间不生效
const DEVICE_PATTERN = /^[A-Za-z0-9-]{8,64}$/;
const UPSTREAM_UA = "clash-verge/v2.0.0";
const KV_CONFIG = "sub:yaml";
const KV_USERINFO = "sub:userinfo";
const KV_UPDATED = "sub:updated";

/**
 * Secret 里粘贴的东西经常带首尾空白甚至换行（控制台的输入框是 textarea），
 * 直接丢给 fetch 会抛错，表现就是「上游异常 502」。这里统一清洗 + 校验。
 */
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

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: corsHeaders() });
    }

    // 上传端点用 POST，必须在「只允许 GET/HEAD」的守卫之前处理
    if (url.pathname === "/sub/publish" || url.pathname === "/sub/publish/") {
      if (request.method !== "POST") return json(405, { ok: false, reason: "method" });
      return handlePublish(request, env);
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return json(405, { enabled: false, reason: "method" });
    }

    if (url.pathname === "/sub/status" || url.pathname === "/sub/status/") {
      return handleStatus(env, url);
    }

    if (url.pathname !== "/sub/config" && url.pathname !== "/sub/config/") {
      return json(404, { enabled: false, reason: "not-found" });
    }

    const did = (url.searchParams.get("did") || "").trim();
    if (!DEVICE_PATTERN.test(did)) {
      return json(400, { enabled: false, reason: "bad-device" });
    }

    if (!(await isEnabled(env))) {
      return json(503, { enabled: false, reason: "disabled" });
    }

    if (!(await isAllowed(env, did))) {
      return json(403, { enabled: false, reason: "device-not-allowed" });
    }

    // fresh=1：跳过边缘缓存，直接从 KV / 上游取（发布新订阅后客户端用它破缓存）
    const force = url.searchParams.get("fresh") === "1";
    const config = await loadConfig(env, ctx, force);
    if (!config) {
      return json(502, { enabled: false, reason: "upstream" });
    }

    const headers = new Headers({
      "Content-Type": "text/yaml; charset=utf-8",
      "Cache-Control": "no-store",
      "X-MTL-Sub": "ok",
    });

    const userinfo = config.headers.get("X-Sub-Info");
    if (userinfo) headers.set("subscription-userinfo", userinfo);

    return new Response(request.method === "HEAD" ? null : config.body, {
      status: 200,
      headers,
    });
  },
};

// ---------------------------------------------------------------- 配置缓存

async function loadConfig(env, ctx, force = false) {
  const cache = caches.default;
  const key = new Request(CONFIG_CACHE_KEY);

  if (!force) {
    const hit = await cache.match(key);
    if (hit) return hit;
  }

  // 优先用 /sub/publish 上传的副本：订阅端经常直接拒绝 Cloudflare 出口 IP（403），
  // 由你自己的电脑拉一次再推上来，Worker 就永远只读 KV。
  const stored = await kvGet(env, KV_CONFIG);
  if (stored) {
    const response = new Response(stored, {
      status: 200,
      headers: {
        "Content-Type": "text/yaml; charset=utf-8",
        "X-Sub-Info": (await kvGet(env, KV_USERINFO)) || "",
        "X-Sub-Source": "kv",
        "Cache-Control": "max-age=" + CONFIG_CACHE_TTL,
      },
    });

    ctx.waitUntil(cache.put(key, response.clone()));
    return response;
  }

  const subUrl = normalizeUrl(env.SUB_URL);
  if (!subUrl) return null;

  let upstream;
  try {
    upstream = await fetch(subUrl, {
      // 订阅端按 UA 返回不同格式：浏览器 UA 拿到的是面板 HTML，必须声明成 clash
      headers: { "User-Agent": UPSTREAM_UA, "Accept": "*/*" },
      redirect: "follow",
    });
  } catch {
    return null;
  }

  if (!upstream.ok) return null;

  const response = new Response(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": "text/yaml; charset=utf-8",
      "X-Sub-Info": upstream.headers.get("subscription-userinfo") || "",
      "X-Sub-Source": "upstream",
      "Cache-Control": "max-age=" + CONFIG_CACHE_TTL,
    },
  });

  ctx.waitUntil(cache.put(key, response.clone()));
  return response;
}

/**
 * POST /sub/publish
 * 头：X-Publish-Key: <PUBLISH_KEY>
 * 体：Clash YAML 原文
 *
 * 用来绕开「订阅端拒绝 Cloudflare 出口 IP」的问题：本机拉一次，推到 KV 里。
 */
async function handlePublish(request, env) {
  if (!env.PUBLISH_KEY) return json(404, { ok: false, reason: "publish-disabled" });
  if (!env.SUB_KV) return json(501, { ok: false, reason: "kv-missing" });

  const key = (request.headers.get("x-publish-key") || "").trim();
  if (key !== env.PUBLISH_KEY.toString().trim()) {
    return json(403, { ok: false, reason: "bad-key" });
  }

  let text;
  try {
    text = await request.text();
  } catch {
    return json(400, { ok: false, reason: "read-body" });
  }

  if (!text || text.length < 200) return json(400, { ok: false, reason: "too-small" });
  if (text.length > 4 * 1024 * 1024) return json(400, { ok: false, reason: "too-large" });
  if (!/(^|\n)\s*(proxies|proxy-groups)\s*:/.test(text)) {
    return json(400, { ok: false, reason: "not-clash-yaml" });
  }

  await env.SUB_KV.put(KV_CONFIG, text);
  await env.SUB_KV.put(KV_USERINFO, request.headers.get("x-sub-info") || "");
  await env.SUB_KV.put(KV_UPDATED, new Date().toISOString());

  // 让下一次请求立刻拿到新配置
  await caches.default.delete(new Request(CONFIG_CACHE_KEY));

  return json(200, { ok: true, bytes: text.length, updated: new Date().toISOString() });
}

// ---------------------------------------------------------------- 开关 / 白名单

async function isEnabled(env) {
  const kv = await kvGet(env, "enabled");
  if (kv === "0" || kv === "false") return false;

  const flag = (env.SUB_ENABLED || "1").toString().trim().toLowerCase();
  return flag !== "0" && flag !== "false";
}

async function isAllowed(env, did) {
  // 没绑 KV 就是开放模式，先跑通再收紧
  if (!env.SUB_KV) return true;

  const mode = (await kvGet(env, "mode")) || "open";
  if (mode !== "closed") return true;

  return (await kvGet(env, "allow:" + did)) === "1";
}

async function kvGet(env, key) {
  if (!env.SUB_KV) return null;

  try {
    return await env.SUB_KV.get(key);
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------- 杂项

async function handleStatus(env, url) {
  const enabled = await isEnabled(env);
  const mode = env.SUB_KV ? (await kvGet(env, "mode")) || "open" : "open";
  const subUrl = normalizeUrl(env.SUB_URL);

  const body = {
    enabled,
    mode,
    upstream: subUrl ? "configured" : env.SUB_URL ? "invalid-url" : "missing",
    upstreamHost: subUrl ? new URL(subUrl).host : null,
    published: (await kvGet(env, KV_CONFIG)) ? "yes" : "no",
    publishedAt: (await kvGet(env, KV_UPDATED)) || null,
    cache: (await caches.default.match(new Request(CONFIG_CACHE_KEY))) ? "hit" : "miss",
  };

  // 带 ?probe=1 才真的去拉一次订阅，用来定位「上游异常」到底卡在哪
  if (url.searchParams.get("probe") === "1") {
    if (!subUrl) {
      body.probe = { ok: false, reason: "no-url" };
    } else {
      try {
        const response = await fetch(subUrl, {
          headers: { "User-Agent": UPSTREAM_UA, "Accept": "*/*" },
          redirect: "follow",
        });
        const text = response.ok ? await response.text() : "";
        body.probe = {
          ok: response.ok,
          status: response.status,
          type: response.headers.get("content-type"),
          bytes: text.length,
          looksLikeYaml: /^\s*(mixed-port|port|proxies|proxy-groups)\s*:/m.test(text.slice(0, 2000)),
        };
      } catch (error) {
        body.probe = { ok: false, status: 0, error: String(error).slice(0, 120) };
      }
    }
  }

  return json(200, body);
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: Object.assign(
      { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
      corsHeaders(),
    ),
  });
}

function corsHeaders() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, HEAD, OPTIONS",
    "Access-Control-Allow-Headers": "*",
  };
}
