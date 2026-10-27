# 8591-watcher

持續監看 8591 某個列表條件下的商品（預設：明日方舟：終末地／亞服／帳號／`accountTag=3`／依刊登時間排序），
把每一筆存進 SQLite，並記錄 **新上架、變價、下架/售出**。

直接打 8591 前端自己在用的 JSON API（`https://api.8591.com.tw/v4/mall/getList`），不用開瀏覽器、不用登入。
只用 Python 標準函式庫，跑在 Docker 裡。

## 啟動

```bash
# 在 WSL Ubuntu 裡
mkdir -p ~/projects/8591-watcher/data
cd /mnt/c/Users/ShawnWei/Desktop/claude/some_interest_little_thing/8591-watcher
docker compose up -d --build
docker compose logs -f          # 看即時紀錄
```

停止：`docker compose down`

## 設定（`compose.yaml` 的 `environment`）

| 變數 | 預設 | 說明 |
|---|---|---|
| `LIST_URL` | 終末地帳號列表 | 到 8591 網頁上篩好條件，把網址整串貼上即可 |
| `INTERVAL_SECONDS` | `300` | 多久抓一次。別設太短，避免被擋 |
| `JITTER_SECONDS` | `60` | 每輪間隔前後隨機 ±幾秒（預設實際 4–6 分鐘） |
| `PAGE_SIZE` | `100` | 每頁筆數 |
| `PAGE_DELAY_SECONDS` | `3` | 翻頁之間等幾秒 |
| `DISCORD_WEBHOOK_URL` | 空 | 選填。放在 `.env`（已被 git 忽略），有新上架/變價就推播到 Discord |
| `RUN_ONCE` | 空 | 設成 `1` 就只跑一輪 |

## 看結果：`output/`（每輪自動更新，VS Code 直接開）

- [`output/listings.json`](output/listings.json)：目前在架的商品，一般商品依首次看到時間新→舊排，置頂廣告排最後
- [`output/events.json`](output/events.json)：最近 200 筆異動（新上架／變價／下架/售出），新的在最上面

```json
{
  "更新時間": "2026-10-19T08:45:00+08:00",
  "在架數": 126,
  "商品": [
    {
      "價格": 3191,
      "標題": "【60等、女】滿潛5+6莊方宜…",
      "網址": "https://www.8591.com.tw/v3/mall/detail/2420421015",
      ...
    }
  ]
}
```

## 原始資料（`~/projects/8591-watcher/data/`）

- `listings.db`：SQLite
  - `listings`：每筆商品（`first_seen` 首次看到、`last_seen` 最後看到、`removed_at` 從列表消失的時間、`raw` 原始 JSON）
  - `price_history`：價格變化
  - `events`：`new` / `price` / `removed` / `relisted` 事件
  - `runs`：每一輪抓了幾筆

查詢範例：

```bash
sqlite3 ~/projects/8591-watcher/data/listings.db \
  "select ts, kind, detail from events order by ts desc limit 20"
```

## 備註

- 第一次啟動只會建檔，不會把現有的全部當成「新上架」。
- 只有完整抓到全部筆數時才會判定下架，避免網路不穩誤判。
- `置頂廣告`（`promoted`）= 付費置頂的商品，它們永遠排在最前面，刊登時間可能很久以前。
