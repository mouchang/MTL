// MTL CurseForge 网关（Cloudflare Worker）
// 路由：mtl.dpdns.org/cf/*
//
// 作用：官方 API Key 只存在 Cloudflare 的 secret 里，客户端永远拿不到 ——
// 公开分发的启动器里不能带 Key，否则谁都能抠出来，还可能被官方吊销。
//
// 环境变量：
//   CURSEFORGE_KEY   官方 API Key（wrangler secret put CURSEFORGE_KEY）
//   CF_CACHE_TTL     搜索结果缓存秒数，默认 300；设 0 关闭缓存
//
// 端点：
//   GET  /cf/v1/mods/search?...      转发到 api.curseforge.com/v1/mods/search
//   POST /cf/v1/fingerprints         同上（不缓存）
//   GET  /cf/status                  { configured, cacheTtl }

const UPSTREAM = "https://api.curseforge.com";
const STATUS_PATH = "/cf/status";

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: cors() });
    }

    if (url.pathname === STATUS_PATH || url.pathname === STATUS_PATH + "/") {
      return json(200, {
        configured: Boolean(env.CURSEFORGE_KEY),
        cacheTtl: cacheTtl(env),
      });
    }

    if (!url.pathname.startsWith("/cf/")) {
      return json(404, { ok: false, reason: "not-found" });
    }

    if (!env.CURSEFORGE_KEY) {
      return json(503, { ok: false, reason: "cf-key-missing" });
    }

    if (request.method !== "GET" && request.method !== "POST") {
      return json(405, { ok: false, reason: "method" });
    }

    const upstreamPath = url.pathname.slice(3); // 去掉 "/cf"，保留 "/v1/..."
    if (!upstreamPath.startsWith("/v1/")) {
      return json(404, { ok: false, reason: "not-found" });
    }

    const target = UPSTREAM + upstreamPath + url.search;
    const cache = caches.default;
    const cacheKey = new Request("https://mtl-cf.internal" + upstreamPath + url.search);
    const ttl = cacheTtl(env);
    const cacheable = request.method === "GET" && ttl > 0;

    if (cacheable) {
      const hit = await cache.match(cacheKey);
      if (hit) {
        const headers = new Headers(hit.headers);
        headers.set("X-MTL-Cache", "HIT");
        return new Response(hit.body, { status: hit.status, headers });
      }
    }

    const init = {
      method: request.method,
      headers: {
        "x-api-key": env.CURSEFORGE_KEY.trim(),
        Accept: "application/json",
        "User-Agent": "MutsumiLauncher/1.0",
      },
      redirect: "follow",
    };

    if (request.method === "POST") {
      init.body = request.body;
      init.headers["Content-Type"] = request.headers.get("content-type") || "application/json";
    }

    let response;
    try {
      response = await fetch(target, init);
    } catch {
      return json(502, { ok: false, reason: "cf-unreachable" });
    }

    const headers = new Headers({
      "Content-Type": response.headers.get("content-type") || "application/json; charset=utf-8",
      "X-MTL-Cache": "MISS",
      ...cors(),
    });

    if (!response.ok) {
      headers.set("Cache-Control", "no-store");
      return new Response(response.body, { status: response.status, headers });
    }

    headers.set("Cache-Control", cacheable ? `public, max-age=${ttl}` : "no-store");

    const wrapped = new Response(response.body, { status: response.status, headers });

    if (cacheable) {
      ctx.waitUntil(cache.put(cacheKey, wrapped.clone()));
    }

    return wrapped;
  },
};

function cacheTtl(env) {
  const raw = Number(env.CF_CACHE_TTL ?? "300");
  return Number.isFinite(raw) && raw >= 0 ? raw : 300;
}

function cors() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "content-type",
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
  };
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...cors() },
  });
}
