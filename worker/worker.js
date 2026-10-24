// Mutsumi Launcher 的 Cloudflare Worker：
//   1) /gh/latest                       -> GitHub Releases 最新版本（带 token、缓存）
//   2) /gh/download?url=...             -> 代理 Release 安装包下载
//   3) /<host>/<path>                   -> 反代白名单站点（NeoForge maven 等），路径形式与 ForgeLoader 一致
//
// 环境变量（Settings -> Variables and Secrets）：
//   REPO          文本   mouchang/MTL
//   GITHUB_TOKEN  Secret 只读公开仓库的 PAT（不要写进客户端）
//   ALLOWED_HOSTS 文本（可选）逗号分隔，覆盖默认白名单

const DEFAULT_ALLOWED_HOSTS = [
  "maven.neoforged.net",
  "maven.minecraftforge.net",
  "files.minecraftforge.net",
  "maven.fabricmc.net",
];

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("method not allowed", { status: 405 });
    }

    if (url.pathname === "/gh/latest") {
      return handleLatestRelease(request, env, ctx);
    }

    if (url.pathname === "/gh/download") {
      return handleReleaseDownload(url, env);
    }

    return handleMavenProxy(request, url, env, ctx);
  },
};

async function handleLatestRelease(request, env, ctx) {
  const cache = caches.default;
  const cacheKey = new Request(new URL("/gh/latest", request.url).toString());

  let cached = await cache.match(cacheKey);
  if (cached) return cached;

  const repo = env.REPO || "mouchang/MTL";

  async function request(withToken) {
    const headers = {
      Accept: "application/vnd.github+json",
      "User-Agent": "MTL-Update-Worker",
    };
    if (withToken && env.GITHUB_TOKEN) headers.Authorization = `Bearer ${env.GITHUB_TOKEN}`;
    return fetch(`https://api.github.com/repos/${repo}/releases/latest`, { headers });
  }

  let upstream;
  try {
    upstream = await request(true);

    // token 没配置或已失效时，退回匿名请求（公开仓库每天 60 次/每个出口 IP）
    if (upstream.status === 401) upstream = await request(false);
  } catch (error) {
    return new Response(`upstream error: ${error.message}`, { status: 502 });
  }

  const headers = new Headers(upstream.headers);
  headers.set("Cache-Control", "public, max-age=300");
  headers.set("Access-Control-Allow-Origin", "*");

  const response = new Response(upstream.body, { status: upstream.status, headers });

  if (upstream.ok) ctx.waitUntil(cache.put(cacheKey, response.clone()));
  return response;
}

async function handleReleaseDownload(url, env) {
  const target = url.searchParams.get("url") || "";
  const repo = env.REPO || "mouchang/MTL";
  const prefix = `https://github.com/${repo}/releases/download/`;

  if (!target.startsWith(prefix)) {
    return new Response("forbidden", { status: 403 });
  }

  let upstream;
  try {
    upstream = await fetch(target, {
      redirect: "follow",
      headers: { "User-Agent": "MTL-Update-Worker" },
    });
  } catch (error) {
    return new Response(`upstream error: ${error.message}`, { status: 502 });
  }

  return new Response(upstream.body, {
    status: upstream.status,
    headers: {
      "Content-Type": upstream.headers.get("Content-Type") || "application/octet-stream",
      "Content-Length": upstream.headers.get("Content-Length") || "",
      "Cache-Control": "public, max-age=86400",
    },
  });
}

async function handleMavenProxy(request, url, env, ctx) {
  const match = url.pathname.match(/^\/([^/]+)\/(.+)$/);
  if (!match) return new Response("Mutsumi Launcher proxy is running.", { status: 200 });

  const host = match[1];
  const rest = match[2];

  const allowed = (env.ALLOWED_HOSTS || DEFAULT_ALLOWED_HOSTS.join(","))
    .split(",")
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);

  if (!allowed.includes(host.toLowerCase())) {
    return new Response("host not allowed", { status: 403 });
  }

  const target = `https://${host}/${rest}${url.search}`;
  const cache = caches.default;
  const cacheKey = new Request(target);

  let cached = await cache.match(cacheKey);
  if (cached) return cached;

  let upstream;
  try {
    upstream = await fetch(target, {
      headers: {
        "User-Agent": "MTL-Proxy-Worker",
        "Accept": request.headers.get("Accept") || "*/*",
      },
    });
  } catch (error) {
    return new Response(`upstream error: ${error.message}`, { status: 502 });
  }

  if (!upstream.ok) {
    return new Response(upstream.body, {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("Content-Type") || "text/plain" },
    });
  }

  const headers = new Headers();
  headers.set("Content-Type", upstream.headers.get("Content-Type") || "application/octet-stream");
  headers.set("Cache-Control", "public, max-age=86400");

  const response = new Response(upstream.body, { status: 200, headers });
  ctx.waitUntil(cache.put(cacheKey, response.clone()));

  return response;
}
