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
import statistics
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, time as clock_time, timedelta, timezone

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

    # 老库补列：has_cn 用来标记「这页没有中文名，不用收录」
    columns = {row[1] for row in conn.execute("PRAGMA table_info(urls)")}
    if "has_cn" not in columns:
        conn.execute("ALTER TABLE urls ADD COLUMN has_cn INTEGER NOT NULL DEFAULT 0")
        # 老数据按已抓到的中文名回填
        conn.execute("UPDATE urls SET has_cn = 1 WHERE chinese_name IS NOT NULL AND chinese_name <> ''")
        conn.commit()

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
            # 只收模组页面：整合包、物品、教程一律不要
            if "/class/" not in loc:
                continue
            rows.append((loc, "class", lastmod))

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

    # 清掉历史遗留的非模组条目
    removed = conn.execute("DELETE FROM urls WHERE kind <> 'class'").rowcount
    conn.commit()
    if removed:
        log(f"  已移除 {removed} 条非模组页面")

    log("任务清单已更新：" + str(total(args, conn)))


def total(args, conn):
    useful, skipped, pending, failed = conn.execute(
        "SELECT SUM(status='done' AND has_cn=1), SUM(status='done' AND has_cn=0), "
        "SUM(status='pending'), SUM(status='failed') FROM urls"
    ).fetchone()
    return f"收录 {useful or 0} / 无中文名 {skipped or 0} / 待抓 {pending or 0} / 失败 {failed or 0}"


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


def parse_windows(text):
    """'00:00-00:06,12:00-12:02' -> [(time(0,0), time(0,6)), ...]"""
    windows = []

    for part in (text or "").split(","):
        part = part.strip()
        if not part:
            continue

        match = re.match(r"^(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})$", part)
        if not match:
            raise SystemExit(f"暂停窗口格式不对：{part}（应为 HH:MM-HH:MM）")

        start = clock_time(int(match.group(1)), int(match.group(2)))
        end = clock_time(int(match.group(3)), int(match.group(4)))
        windows.append((start, end))

    return windows


def sleep_until(target_time):
    """睡到今天的 target_time（已过则明天），外加 15 秒缓冲。"""
    target = datetime.combine(datetime.now().date(), target_time)
    if target <= datetime.now():
        target += timedelta(days=1)

    seconds = max(0, (target - datetime.now()).total_seconds()) + 15
    log(f"  进入计划内断网时段，暂停到 {target.strftime('%H:%M')}（约 {seconds / 60:.0f} 分钟）")
    time.sleep(seconds)
    log("  断网时段结束，继续")


def in_pause_window(windows):
    """当前时间落在某个暂停窗口里就返回窗口结束时刻。"""
    if not windows:
        return None

    current = datetime.now().time()

    for start, end in windows:
        if start <= end:
            if start <= current < end:
                return end
        elif current >= start or current < end:  # 跨午夜
            return end

    return None


def probe_network(timeout=5.0):
    """用一个极小的请求判断网络是否恢复。"""
    try:
        request = urllib.request.Request(
            "https://www.mcmod.cn/robots.txt",
            headers={"User-Agent": DEFAULT_UA, "Cache-Control": "no-cache"},
        )
        with urllib.request.urlopen(request, timeout=timeout) as response:
            response.read(256)
        return True
    except Exception:
        return False


def wait_for_network(args):
    """断网后等待恢复。返回 True 表示网络回来了。"""
    waited = 0
    log("  网络不可用，开始等待恢复…")

    while waited < args.net_wait_max:
        time.sleep(min(30, args.net_wait_max - waited))
        waited += 30

        if probe_network():
            log(f"  网络已恢复（等待了 {waited} 秒），{args.recover_delay} 秒后继续")
            time.sleep(args.recover_delay)
            return True

        if waited % 300 == 0:
            log(f"  仍在等待网络…（已等 {waited // 60} 分钟）")

    return False


def fetch_with_recovery(url, args):
    """抓一页；遇到网络错误就等恢复后重试同一页。返回 (status, html) 或 None。"""
    for attempt in range(1, 4):
        try:
            return fetch(url, args.ua, args.timeout)
        except urllib.error.HTTPError as error:
            return error.code, ""
        except Exception as error:
            if attempt >= 3:
                log(f"  同一页连续失败 {attempt} 次：{type(error).__name__}")
                return None

            log(f"  网络异常（{type(error).__name__}: {error}），第 {attempt} 次重试用尽")
            if not wait_for_network(args):
                return None

    return None


def crawl(args, conn):
    pending = conn.execute(
        "SELECT url FROM urls WHERE status != 'done' AND kind = 'class' "
        "ORDER BY status = 'failed', url LIMIT ?",
        (args.limit if args.limit else -1,),
    ).fetchall()

    if not pending:
        log("没有待抓的页面，先跑 sync？")
        return

    log(f"本次计划抓取 {len(pending)} 页，节奏 {args.delay}s/页")

    windows = parse_windows(args.pause_window)
    if windows:
        describe = "、".join(f"{s.strftime('%H:%M')}-{e.strftime('%H:%M')}" for s, e in windows)
        log(f"计划内断网窗口：{describe}（到点会自动暂停，网络恢复后接着抓同一页）")

    blocked = 0
    started = time.time()
    fetched_this_run = 0
    skipped_this_run = 0

    for index, (url,) in enumerate(pending, start=1):
        # 落在断网窗口里就睡过去，不浪费请求
        while True:
            window_end = in_pause_window(windows)
            if window_end is None:
                break
            sleep_until(window_end)

        time.sleep(args.delay + random.uniform(0, args.jitter))

        result = fetch_with_recovery(url, args)
        if result is None:
            log("网络长时间不可用，已停止（进度已保存，恢复后重跑同一条命令即可）")
            return

        status, html = result

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

        # 没有中文名的页面不收录：记成 done，但不进导出表，以后也不会再抓
        has_cn = 1 if chinese else 0

        conn.execute(
            "UPDATE urls SET status='done', http=?, chinese_name=?, english_name=?, abbr=?, has_cn=?, "
            "fetched_at=?, attempts=attempts+1 WHERE url=?",
            (status, chinese, english, abbr, has_cn, now(), url),
        )
        conn.commit()

        fetched_this_run += 1
        if not has_cn:
            skipped_this_run += 1

        if index % 20 == 0 or index == len(pending):
            elapsed = time.time() - started
            speed = elapsed / max(1, index)
            eta = (len(pending) - index) * speed / 3600
            label = chinese or f"(无中文名) {english or '?'}"
            log(f"  [{index}/{len(pending)}] {label} | 剩余约 {eta:.1f} 小时 | {total(args, conn)}")

        if args.pause_every and index % args.pause_every == 0:
            log(f"  休息 {args.pause_seconds} 秒（每 {args.pause_every} 页）")
            time.sleep(args.pause_seconds)

    log(f"本轮完成 {fetched_this_run} 页（其中 {skipped_this_run} 页没有中文名，未收录），{total(args, conn)}")


def export(args, conn):
    rows = conn.execute(
        "SELECT url, kind, chinese_name, english_name, abbr FROM urls "
        "WHERE status='done' AND chinese_name IS NOT NULL AND chinese_name <> ''"
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


def stats(args, conn):
    """打印进度 + 按已抓页的平均耗时估算剩余时间。"""
    print(total(args, conn))

    pending = conn.execute(
        "SELECT COUNT(*) FROM urls WHERE status != 'done' AND kind = 'class'"
    ).fetchone()[0]

    count, first, last = conn.execute(
        "SELECT COUNT(*), MIN(fetched_at), MAX(fetched_at) FROM urls "
        "WHERE status = 'done' AND fetched_at IS NOT NULL"
    ).fetchone()

    done = conn.execute("SELECT COUNT(*) FROM urls WHERE status = 'done'").fetchone()[0]
    total_pages = done + pending

    if total_pages:
        print(f"进度 {done / total_pages * 100:.1f}%（{done:,} / {total_pages:,} 页）")

    if not pending:
        print("全部抓完，可以 export 了")
        return

    # 用最近 120 页的间隔中位数估算节奏，避免把跨轮次的长空档算进去
    rows = conn.execute(
        "SELECT fetched_at FROM urls WHERE status = 'done' AND fetched_at IS NOT NULL "
        "ORDER BY fetched_at DESC LIMIT 120"
    ).fetchall()

    try:
        times = sorted(datetime.strptime(row[0], "%Y-%m-%dT%H:%M:%SZ") for row in rows)
    except (ValueError, TypeError):
        times = []

    gaps = [(later - earlier).total_seconds() for earlier, later in zip(times, times[1:])]
    gaps = [gap for gap in gaps if 0.5 <= gap <= 120]

    if gaps:
        per_page = statistics.median(gaps)
        hours = pending * per_page / 3600
        print(f"节奏 {per_page:.2f} 秒/页，预计还需 {hours:.1f} 小时")


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
    parser.add_argument("--pause-window", default="00:00-00:06",
                        help="计划内断网时段，如 00:00-00:06（可写多个，逗号分隔）；到点自动暂停")
    parser.add_argument("--net-wait-max", type=int, default=1800,
                        help="断网后最多等多久（秒），超时则停下并保存进度")
    parser.add_argument("--recover-delay", type=int, default=10,
                        help="网络恢复后额外等几秒再继续")
    parser.add_argument("--only", choices=["class", "all", "modpack"], default="class",
                        help="抓取范围，默认只抓模组（class）")
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
            stats(args, conn)
    except KeyboardInterrupt:
        log("已中断，进度已保存")
        return 1
    finally:
        conn.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
