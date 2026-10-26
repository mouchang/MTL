// MTL 内核镜像（Cloudflare Worker）
// 路由：mtl.dpdns.org/core/*
//
// 把 mihomo 的 release 资产缓存在 Cloudflare 边缘，启动器下载内核不再依赖 GitHub 直连。
// 客户端会校验 ZIP 的 SHA-256，镜像被篡改也装不上。

const RELEASE = "v1.19.33";
const ASSET = "mihomo-windows-amd64-compatible-" + RELEASE + ".zip";
const UPSTREAM = "https://github.com/MetaCubeX/mihomo/releases/download/" + RELEASE + "/" + ASSET;
const CACHE_TTL = 30 * 24 * 3600;

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (url.pathname !== "/core/mihomo.zip" && url.pathname !== "/core/mihomo.zip/") {
      return new Response("not found", { status: 404 });
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("method not allowed", { status: 405 });
    }

    const cache = caches.default;
    const key = new Request("https://mtl-core.internal/" + ASSET);

    const hit = await cache.match(key);
    if (hit) {
      return new Response(request.method === "HEAD" ? null : hit.body, {
        status: 200,
        headers: serveHeaders("HIT"),
      });
    }

    let upstream;
    try {
      upstream = await fetch(UPSTREAM, {
        headers: { "User-Agent": "MTL-Core-Mirror/1.0" },
        redirect: "follow",
      });
    } catch {
      return new Response("upstream unreachable", { status: 502 });
    }

    if (!upstream.ok) {
      return new Response("upstream " + upstream.status, { status: 502 });
    }

    const stored = new Response(upstream.body, {
      status: 200,
      headers: { "Content-Type": "application/zip", "Cache-Control": "max-age=" + CACHE_TTL },
    });

    ctx.waitUntil(cache.put(key, stored.clone()));

    return new Response(request.method === "HEAD" ? null : stored.body, {
      status: 200,
      headers: serveHeaders("MISS"),
    });
  },
};

function serveHeaders(state) {
  return {
    "Content-Type": "application/zip",
    "Content-Disposition": 'attachment; filename="' + ASSET + '"',
    "Cache-Control": "public, max-age=86400",
    "X-MTL-Core": RELEASE,
    "X-MTL-Cache": state,
    "Access-Control-Allow-Origin": "*",
  };
}
