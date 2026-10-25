// 独立的 Modrinth 文件镜像 Worker（不需要环境变量，白名单写死）。
// 绑定路由：mtl.dpdns.org/cdn.modrinth.com/*   （比 mtl.dpdns.org/* 更具体，会优先命中）
//
// 作用：把 https://mtl.dpdns.org/cdn.modrinth.com/<path>
//   回源到 https://cdn.modrinth.com/<path>，并交给 CF 边缘缓存（文件带版本号，内容不可变）。
//
// 冷启动第一个请求会回源（1-3 秒），之后 edge 命中，Range 请求由边缘从缓存切片返回。

const UPSTREAM_ORIGIN = "https://cdn.modrinth.com";
const PREFIX = "/cdn.modrinth.com/";

export default {
  async fetch(request) {
    const url = new URL(request.url);

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("method not allowed", { status: 405 });
    }

    if (!url.pathname.startsWith(PREFIX)) {
      return new Response("not found", { status: 404 });
    }

    const rest = url.pathname.slice(PREFIX.length);
    const target = UPSTREAM_ORIGIN + "/" + rest + url.search;

    try {
      return await fetch(target, {
        method: request.method,
        headers: {
          "User-Agent": "MTL-Modrinth-Mirror/1.0",
          "Accept": request.headers.get("Accept") || "*/*",
        },
        cf: {
          cacheEverything: true,
          cacheTtlByStatus: { "200-299": 31536000, "404-599": 30 },
        },
      });
    } catch (error) {
      return new Response("upstream error: " + error.message, { status: 502 });
    }
  },
};
