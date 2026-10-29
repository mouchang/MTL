// MTL Modrinth 网关（Cloudflare Worker）
// 路由：mtl.dpdns.org/md/*
//
// 作用：把 Modrinth 的 API 请求转到 Cloudflare 边缘，国内访问更稳、也省一次跨境往返。
// Modrinth 的公开接口不需要 Key；如果客户端带了 Personal Access Token，
// 这里会把 Authorization 原样转发（带令牌的请求不缓存）。
//
// 环境变量：
//   MD_CACHE_TTL   搜索结果缓存秒数，默认 300；设 0 关闭
//
// 端点：
//   GET  /md/v2/search?...          转发到 api.modrinth.com/v2/search
//   POST /md/v2/version_files/update  同上（不缓存）
//   GET  /md/status                 { ok, cacheTtl }

const UPSTREAM = "https://api.modrinth.com";
const STATUS_PATH = "/md/status";

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: cors() });
    }

    if (url.pathname === STATUS_PATH || url.pathname === STATUS_PATH + "/") {
      return json(200, { ok: true, cacheTtl: cacheTtl(env) });
    }

    if (!url.pathname.startsWith("/md/")) {
      return json(404, { ok: false, reason: "not-found" });
    }

    if (request.method !== "GET" && request.method !== "POST") {
      return json(405, { ok: false, reason: "method" });
    }

    const upstreamPath = url.pathname.slice(3); // 去掉 "/md"，保留 "/v2/..."
    if (!upstreamPath.startsWith("/v2/")) {
      return json(404, { ok: false, reason: "not-found" });
    }

    const target = UPSTREAM + upstreamPath + url.search;
    const authorized = request.headers.has("authorization");
    const ttl = cacheTtl(env);
    const cacheable = request.method === "GET" && !authorized && ttl > 0;
    const cache = caches.default;
    const cacheKey = new Request("https://mtl-md.internal" + upstreamPath + url.search);

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
        Accept: request.headers.get("accept") || "application/json",
        "User-Agent": "MutsumiLauncher/1.0 (github.com/mouchang/MTL)",
      },
      redirect: "follow",
    };

    if (authorized) init.headers["Authorization"] = request.headers.get("authorization");

    if (request.method === "POST") {
      init.body = request.body;
      init.headers["Content-Type"] = request.headers.get("content-type") || "application/json";
    }

    let response;
    try {
      response = await fetch(target, init);
    } catch {
      return json(502, { ok: false, reason: "modrinth-unreachable" });
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
  const raw = Number(env.MD_CACHE_TTL ?? "300");
  return Number.isFinite(raw) && raw >= 0 ? raw : 300;
}

function cors() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "content-type,authorization",
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
  };
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...cors() },
  });
}
