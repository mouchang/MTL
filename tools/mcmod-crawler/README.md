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
| `--pause-window` | 00:00-00:06 | **计划内断网时段**，到点自动暂停；可写多个（逗号分隔） |
| `--net-wait-max` | 1800 | 断网后最多等多久（秒），超时才停 |
| `--recover-delay` | 10 | 网络恢复后额外等几秒再继续 |
| `--only` | all | 只抓 `class`（模组）或 `modpack`（整合包） |
| `--timeout` | 30 | 单页超时 |
| `--db` | state.db | 进度库 |

## 断网 / 暂停处理

两条防线：

1. **计划内断网（默认每天 00:00–00:06）**
   到点前主动暂停，睡到时段结束后继续，**不会白白发出失败请求**。
   ```bash
   python crawl.py crawl --pause-window "00:00-00:06"
   ```

2. **意外断网**
   请求抛异常时不会把这一页标记失败，而是：
   - 用 `/robots.txt` 做轻量探活（5 秒超时）
   - 每 30 秒探一次，最多等 `--net-wait-max`（默认 30 分钟）
   - 一旦恢复，等 `--recover-delay` 秒后**重抓同一页**
   - 超过等待上限才退出，进度照旧保存

## 无中文名的页面

抓完解析后，如果标题里没有中文名（很多国外模组只有英文名）：
- 记 `has_cn=0`，**不进导出表**，`stats` 里单独计数
- 状态仍是 `done`，后续增量不会重复抓

> 注意：**这一条省不了请求**——不请求就不知道有没有中文名。它的收益是导出表更干净
> （目前约 40% 的页面没有中文名），以及以后不用重抓。
> 真想省时间的话用 `--only class` 跳过 1,511 个整合包页面（约省 1 小时）。

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
