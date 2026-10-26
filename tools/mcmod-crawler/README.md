# MC百科译名表爬虫

给启动器生成「模组中文名 ↔ 英文名」对照表的离线工具。**不随启动器运行**，只在你自己的机器上跑。

## 为什么这么写

- 只抓 `robots.txt` 允许的 `/class/N.html` 和 `/modpack/N.html`（robots 里只禁止 `/class/add/` 这类编辑路径）
- 地址来自站点自己发布的 `sitemap.xml`，**不盲扫 ID、不用搜索接口**
- 默认 **1.5 秒一页、单线程**，被限流会自动退避，连续被拒 5 次直接停下
- 断点续爬：进度存在 `state.db`，Ctrl+C 之后重新跑会接着来

## 用法

```bash
# 1) 拉 sitemap，建任务清单（约 3.3 万页，30 秒）
python crawl.py sync

# 2) 先小批量验证（3 页，看解析对不对）
python crawl.py crawl --limit 3

# 3) 全量（默认 1.5s/页 ≈ 13~14 小时；中途可以随时停）
python crawl.py crawl

# 4) 导出
python crawl.py export        # -> mcmod-zh.json / mcmod-zh.json.gz

# 看进度
python crawl.py stats
```

## 参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `--delay` | 1.5 | 每页间隔秒数。**不建议低于 1** |
| `--jitter` | 0.4 | 在 delay 上叠加的随机抖动 |
| `--limit` | 0 | 本次最多抓多少页，0 = 全部 |
| `--max-blocked` | 5 | 连续被拒多少次就停（停之前会指数退避） |
| `--pause-every` / `--pause-seconds` | 0 / 120 | 每 N 页休息一次，缓解长时间抓取 |
| `--timeout` | 30 | 单页超时 |
| `--db` | state.db | 进度库 |

## 输出格式

```json
[{"id":1,"type":"class","cn":"我的世界原版","en":"Minecraft","abbr":"MC"},
 {"id":100,"type":"class","cn":"虚拟人生/凡家物语","en":"Minecraft Comes Alive","abbr":"MCA"}]
```

- `type`：`class`（模组）/ `modpack`（整合包）
- `cn` / `en` / `abbr` 来自页面标题 `[JEI]JEI物品管理器 (Just Enough Items)`

## 注意事项

1. **别开多线程**。站点对高频访问有 WAF，慢就是快。
2. **建议凌晨跑**，并开启 `--pause-every 500 --pause-seconds 120`。
3. 数据版权归 MC百科与贡献者所有；这份表**只用于本地名称对照**，要不要再分发（放 Release）由你自己决定。
4. 页面标题里没有括号英文名的条目，`en` 就是 `null`，导出时保留（部分老模组确实没有英文名）。
