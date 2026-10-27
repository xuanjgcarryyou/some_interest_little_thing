# 591-rent-watcher

找房用：持續監看 591 租屋網某個搜尋條件，記錄 **新上架、租金變動（降價/漲價）、下架（多半是租掉了）**，
輸出成好讀的 JSON，VS Code 打開就能看。

預設條件：新北市／整層住家／可養寵物／租金 2 萬以下／2 房以上／依最新排序
（[網頁版](https://rent.591.com.tw/list?region=3&kind=1&other=pet&price=0$_20000$&layout=2,3,4&sort=posttime_desc)）。

直接打 591 前端自己在用的 JSON API（`bff-house.591.com.tw/v3/web/rent/list`），
網址參數照前端的規則轉換，不用開瀏覽器、不用登入。只用 Python 標準函式庫，跑在 Docker 裡。

## 在新電腦上啟動

```bash
git clone https://github.com/xuanjgcarryyou/some_interest_little_thing.git
cd some_interest_little_thing/591-rent-watcher

mkdir -p ~/projects/591-rent-watcher/data   # 先建好，不然 Docker 會用 root 建，容器寫不進去
cp .env.example .env                        # 選填：改搜尋條件、加 Discord 推播
docker compose up -d --build
docker compose logs -f                      # 看即時紀錄
```

- 暫停：`docker compose stop`，繼續：`docker compose start`（重開機後不會自己跑，要手動 start）
- 停掉並移除容器：`docker compose down`
- 沒有 Docker 也能跑：裝 Python 3.8+，`DATA_DIR=./data EXPORT_DIR=./output python crawler.py`

## 看結果：`output/`（每輪自動更新）

- `output/listings.json`：目前在架的物件，**最新發現的在最上面**
- `output/events.json`：最近 200 筆異動（新上架／租金變動／下架），新的在最上面

```json
{
  "租金": 17000,
  "標題": "獨立豪華2房1客廳1衛",
  "網址": "https://rent.591.com.tw/22238588",
  "格局": "2房1廳",
  "坪數": "13坪",
  "樓層": "3F/4F",
  "地址": "板橋區-金門街379巷",
  "標籤": ["屋主直租", "新上架", "可開伙", "可養寵物"],
  "聯絡人": "屋主王小姐",
  "首次看到": "2026-10-27T09:22:50+08:00",
  ...
}
```

## 設定

`.env`（不會進 git；網址有 `$`，要用**單引號**包起來）：

| 變數 | 說明 |
|---|---|
| `LIST_URL` | 到 591 網頁上篩好條件，把網址整串貼上。不設就用上面的預設條件 |
| `DISCORD_WEBHOOK_URL` | 選填。有新上架、租金變動就推播到 Discord |

`compose.yaml` 的 `environment`：

| 變數 | 預設 | 說明 |
|---|---|---|
| `INTERVAL_SECONDS` | `600` | 多久抓一次。別設太短，避免被 591 擋 |
| `JITTER_SECONDS` | `120` | 每輪前後隨機 ±幾秒（預設實際 8–12 分鐘） |
| `MAX_PAGES` | `20` | 每輪最多翻幾頁（一頁 30 筆）。條件太寬超過時，該輪不判定下架 |
| `PAGE_DELAY_SECONDS` | `4` | 翻頁之間等幾秒 |

## 原始資料（`~/projects/591-rent-watcher/data/listings.db`）

SQLite：`listings`（每筆物件＋原始 JSON）、`price_history`、`events`、`runs`。

```bash
sqlite3 ~/projects/591-rent-watcher/data/listings.db \
  "select ts, kind, detail from events order by rowid desc limit 20"
```

## 備註

- 第一次啟動只會建檔，不會把現有的全部當成「新上架」。
- 只有完整抓到全部筆數時才判定下架，避免網路不穩誤判。
- 電腦睡眠時抓不到；醒來後 30 秒內會自動補抓一輪。
- 591 若改版 API，可能要重新對參數。
