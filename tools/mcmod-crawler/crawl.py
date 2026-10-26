#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
MC百科译名表爬虫（MTL 自用工具，零第三方依赖）

只抓 robots.txt 允许的 /class/N.html 与 /modpack/N.html，并且从站点自己发布的
sitemap 里取地址，不盲扫 ID、不碰搜索接口、不碰任何编辑路径。

默认节奏非常保守（1.5 秒一页，单线程），被限流会自动退避并在连续失败后停下。
支持断点续爬，随时 Ctrl+C，下次接着跑。

用法：
    python crawl.py sync              # 拉 sitemap，建任务清单
    python crawl.py crawl --limit 20  # 先小批量验证
    python crawl.py crawl             # 全量（约 3.3 万页，1.5s/页 ≈ 14 小时）
    python crawl.py export            # 生成 mcmod-zh.json / .json.gz
    python crawl.py stats             # 看进度
"""
import argparse
import gzip
import html as html_module
import json
import os
import random
import re
import sqlite3
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

SITEMAP_INDEX = "https://www.mcmod.cn/sitemap.xml"
# HTTP 头只能是 latin-1，别把中文放进来
DEFAULT_UA = "MTL-Translator-Crawler/1.0 (+https://github.com/mouchang/MTL)"
TITLE_SUFFIX = re.compile(r"\s*-\s*MC百科.*$")
BLOCK_HINTS = ("访问过于频繁", "安全验证", "人机验证", "请稍后再试")

SCHEMA = """
CREATE TABLE IF NOT EXISTS urls (
    url          TEXT PRIMARY KEY,
    kind         TEXT NOT NULL,
    lastmod      TEXT,
    status       TEXT NOT NULL DEFAULT 'pending',
    attempts     INTEGER NOT NULL DEFAULT 0,
    http         INTEGER,
    chinese_name TEXT,
    english_name TEXT,
    abbr         TEXT,
    fetched_at   TEXT
);
CREATE INDEX IF NOT EXISTS idx_status ON urls(status);
"""


def now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def log(message):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {message}", flush=True)


def open_db(path):
    conn = sqlite3.connect(path)
    conn.executescript(SCHEMA)
    return conn


def fetch(url, ua, timeout):
    """返回 (status, text)。网络错误抛异常。"""
    request = urllib.request.Request(url, headers={
        "User-Agent": ua,
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "zh-CN,zh;q=0.9",
    })
    with urllib.request.urlopen(request, timeout=timeout) as response:
        raw = response.read()
        charset = response.headers.get_content_charset() or "utf-8"
        return response.status, raw.decode(charset, errors="replace")


def extract_locs(xml):
    """从 sitemap 里取 (loc, lastmod) 列表。"""
    items = []
    for block in re.findall(r"<url>(.*?)</url>", xml, re.S) or re.findall(r"<sitemap>(.*?)</sitemap>", xml, re.S):
        loc = re.search(r"<loc>([^<]+)</loc>", block)
        if not loc:
            continue
        lastmod = re.search(r"<lastmod>([^<]+)</lastmod>", block)
        items.append((loc.group(1).strip(), lastmod.group(1).strip() if lastmod else None))
    return items


def sync(args, conn):
    log(f"读取 sitemap 索引：{SITEMAP_INDEX}")
    _, xml = fetch(SITEMAP_INDEX, args.ua, args.timeout)
    sub_sitemaps = [loc for loc, _ in extract_locs(xml)]
    if not sub_sitemaps:
        sub_sitemaps = [SITEMAP_INDEX]

    total_new = 0
    for sitemap_url in sub_sitemaps:
        log(f"读取 {sitemap_url}")
        _, body = fetch(sitemap_url, args.ua, args.timeout)

        rows = []
        for loc, lastmod in extract_locs(body):
            if "/class/" in loc:
                kind = "class"
            elif "/modpack/" in loc:
                kind = "modpack"
            else:
                continue
            rows.append((loc, kind, lastmod))

        before = conn.total_changes
        conn.executemany(
            "INSERT INTO urls(url, kind, lastmod) VALUES(?,?,?) "
            "ON CONFLICT(url) DO UPDATE SET lastmod=excluded.lastmod "
            "WHERE excluded.lastmod IS NOT NULL AND excluded.lastmod <> urls.lastmod",
            rows,
        )
        conn.commit()
        total_new += len(rows)
        log(f"  收录 {len(rows)} 条（累计 {total_new}）")
        time.sleep(args.delay)

    log("任务清单已更新：" + str(total(args, conn)))


def total(args, conn):
    done, pending, failed = conn.execute(
        "SELECT "
        "SUM(status='done'), SUM(status='pending'), SUM(status='failed') FROM urls"
    ).fetchone()
    return f"完成 {done or 0} / 待抓 {pending or 0} / 失败 {failed or 0}"


CJK = re.compile(r"[\u4e00-\u9fff]")


def parse_title(title):
    """'[JEI]JEI物品管理器 (Just Enough Items) - MC百科...' -> (abbr, 中文名, 英文名)"""
    text = html_module.unescape(TITLE_SUFFIX.sub("", title).strip())

    abbr, chinese, english = None, text, None

    match = re.match(r"^\[([^\]]{1,32})\]\s*(.+?)\s*\(([^()]*)\)\s*$", text)
    if match:
        abbr, chinese, english = match.group(1), match.group(2), match.group(3)
    else:
        match = re.match(r"^(.+?)\s*\(([^()]*)\)\s*$", text)
        if match:
            chinese, english = match.group(1), match.group(2)

    # 没有括号的标题，也要把开头的 [ABBR] 拆出来
    if abbr is None and english is None:
        match = re.match(r"^\[([^\]]{1,32})\]\s*(.+)$", text)
        if match:
            abbr, chinese = match.group(1), match.group(2)

    abbr = abbr.strip() if isinstance(abbr, str) else None
    chinese = chinese.strip() if isinstance(chinese, str) else None
    english = english.strip() if isinstance(english, str) else None

    # 标题里没有中文名的（很多国外模组就是这样），把名字挪到英文名上
    if chinese and not CJK.search(chinese):
        if not english:
            english, chinese = chinese, None

    return abbr or None, chinese or None, english or None


def parse_page(html):
    match = re.search(r"<title>(.*?)</title>", html, re.S | re.I)
    if not match:
        return None, None, None
    return parse_title(re.sub(r"\s+", " ", match.group(1)).strip())


def looks_blocked(status, html):
    if status in (403, 429, 503):
        return True
    return any(hint in html[:4000] for hint in BLOCK_HINTS)


def crawl(args, conn):
    pending = conn.execute(
        "SELECT url FROM urls WHERE status != 'done' ORDER BY status = 'failed', url LIMIT ?",
        (args.limit if args.limit else -1,),
    ).fetchall()

    if not pending:
        log("没有待抓的页面，先跑 sync？")
        return

    log(f"本次计划抓取 {len(pending)} 页，节奏 {args.delay}s/页")
    blocked = 0
    started = time.time()
    done_this_run = 0

    for index, (url,) in enumerate(pending, start=1):
        time.sleep(args.delay + random.uniform(0, args.jitter))

        try:
            status, html = fetch(url, args.ua, args.timeout)
        except urllib.error.HTTPError as error:
            status, html = error.code, ""
        except Exception as error:  # 网络层错误：记一次失败，继续
            conn.execute("UPDATE urls SET status='failed', attempts=attempts+1 WHERE url=?", (url,))
            conn.commit()
            log(f"  [{index}/{len(pending)}] 网络错误 {url} -> {error}")
            continue

        if looks_blocked(status, html):
            blocked += 1
            wait = min(300, 30 * (2 ** (blocked - 1)))
            log(f"  !! 疑似被限流（HTTP {status}，第 {blocked} 次），退避 {wait} 秒")

            if blocked >= args.max_blocked:
                log("连续被拒，已停止。等几小时后再运行，任务进度已保存。")
                return

            time.sleep(wait)
            continue

        blocked = 0
        abbr, chinese, english = parse_page(html)

        conn.execute(
            "UPDATE urls SET status='done', http=?, chinese_name=?, english_name=?, abbr=?, "
            "fetched_at=?, attempts=attempts+1 WHERE url=?",
            (status, chinese, english, abbr, now(), url),
        )
        conn.commit()
        done_this_run += 1

        if index % 20 == 0 or index == len(pending):
            elapsed = time.time() - started
            speed = elapsed / max(1, index)
            eta = (len(pending) - index) * speed / 3600
            log(f"  [{index}/{len(pending)}] {chinese or '?'} | 剩余约 {eta:.1f} 小时 | {total(args, conn)}")

        if args.pause_every and index % args.pause_every == 0:
            log(f"  休息 {args.pause_seconds} 秒（每 {args.pause_every} 页）")
            time.sleep(args.pause_seconds)

    log(f"本轮完成 {done_this_run} 页，{total(args, conn)}")


def export(args, conn):
    rows = conn.execute(
        "SELECT url, kind, chinese_name, english_name, abbr FROM urls "
        "WHERE status='done' AND (chinese_name IS NOT NULL OR english_name IS NOT NULL)"
    ).fetchall()

    records = []
    for url, kind, chinese, english, abbr in rows:
        match = re.search(r"/(\d+)\.html", url)
        records.append({
            "id": int(match.group(1)) if match else None,
            "type": kind,
            "cn": chinese,
            "en": english,
            "abbr": abbr,
        })

    records.sort(key=lambda item: (item["type"] or "", item["id"] or 0))

    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(records, handle, ensure_ascii=False, separators=(",", ":"))

    with open(args.out, "rb") as source, gzip.open(args.out + ".gz", "wb", compresslevel=9) as target:
        target.write(source.read())

    raw_mb = os.path.getsize(args.out) / 1024 / 1024
    gz_mb = os.path.getsize(args.out + ".gz") / 1024 / 1024
    log(f"导出 {len(records)} 条 -> {args.out}（{raw_mb:.2f} MB，gz {gz_mb:.2f} MB）")


def main():
    parser = argparse.ArgumentParser(description="MC百科译名表爬虫")
    parser.add_argument("command", choices=["sync", "crawl", "export", "stats"])
    parser.add_argument("--db", default="state.db")
    parser.add_argument("--out", default="mcmod-zh.json")
    parser.add_argument("--delay", type=float, default=1.5, help="每页间隔秒数（默认 1.5）")
    parser.add_argument("--jitter", type=float, default=0.4)
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--limit", type=int, default=0, help="本次最多抓多少页，0 = 全部")
    parser.add_argument("--max-blocked", type=int, default=5, help="连续被拒多少次就停")
    parser.add_argument("--pause-every", type=int, default=0, help="每 N 页休息一次，0 = 不休息")
    parser.add_argument("--pause-seconds", type=int, default=120)
    parser.add_argument("--ua", default=DEFAULT_UA)
    args = parser.parse_args()

    conn = open_db(args.db)

    try:
        if args.command == "sync":
            sync(args, conn)
        elif args.command == "crawl":
            crawl(args, conn)
        elif args.command == "export":
            export(args, conn)
        else:
            log(total(args, conn))
    except KeyboardInterrupt:
        log("已中断，进度已保存")
        return 1
    finally:
        conn.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
