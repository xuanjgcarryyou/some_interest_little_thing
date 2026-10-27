"""8591 帳號交易列表監看爬蟲。

定時呼叫 8591 前端自己用的列表 API（api.8591.com.tw/v4/mall/getList），
把結果存進 SQLite，並記錄新上架、變價、下架/售出。只用 Python 標準函式庫。
"""
import json
import os
import random
import signal
import sqlite3
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone, timedelta

API = "https://api.8591.com.tw/v4/mall/getList"
DETAIL = "https://www.8591.com.tw/v3/mall/detail/{}"
TZ = timezone(timedelta(hours=8))
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/130.0 Safari/537.36")

LIST_URL = os.environ.get(
    "LIST_URL",
    "https://www.8591.com.tw/v3/mall/list/50556?searchGame=50556&searchServer=50557"
    "&searchType=2&accountTag=3&post_time_sort=1")
INTERVAL = int(os.environ.get("INTERVAL_SECONDS", "300"))
JITTER = int(os.environ.get("JITTER_SECONDS", "60"))  # 每輪間隔 = INTERVAL ± JITTER 隨機
PAGE_SIZE = int(os.environ.get("PAGE_SIZE", "100"))
PAGE_DELAY = float(os.environ.get("PAGE_DELAY_SECONDS", "3"))
DATA_DIR = os.environ.get("DATA_DIR", "/data")
EXPORT_DIR = os.environ.get("EXPORT_DIR", "/output")
DISCORD_WEBHOOK = os.environ.get("DISCORD_WEBHOOK_URL", "").strip()

# 網頁網址參數 → API 參數（照 8591 前端 convertQuery 的對應）
URL_TO_API = {
    "searchGame": "game_id",
    "searchServer": "server_id",
    "searchType": "ware_type",
    "accountTag": "account_tag",
    "priceStart": "price_start",
    "priceEnd": "price_end",
    "searchKey": "keyword",
    "deliveryType": "delivery_type",
    "safeTag": "safe_tag",
    "completed": "completed",
    "agreementVersion": "agreement_version",
}


def now():
    return datetime.now(TZ).isoformat(timespec="seconds")


def log(msg):
    print(f"{now()} {msg}", flush=True)


def api_params(list_url):
    u = urllib.parse.urlparse(list_url)
    q = dict(urllib.parse.parse_qsl(u.query))
    params = {}
    for k, v in q.items():
        params[URL_TO_API.get(k, k)] = v  # 其他（post_time_sort、price_sort…）原樣帶過去
    if "game_id" not in params:  # /v3/mall/list/<gid>
        params["game_id"] = u.path.rstrip("/").split("/")[-1]
    return params


def fetch_json(params, retries=3):
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={
        "User-Agent": UA,
        "Referer": "https://www.8591.com.tw/",
        "Origin": "https://www.8591.com.tw",
        "Accept": "application/json",
    })
    for attempt in range(1, retries + 1):
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                body = json.load(r)
            if body.get("code") != 200:
                raise RuntimeError(f"API code={body.get('code')} {body.get('message')}")
            return body["data"]
        except (urllib.error.URLError, RuntimeError, ValueError, TimeoutError) as e:
            wait = 30 * attempt
            if isinstance(e, urllib.error.HTTPError) and e.code == 429:
                wait = 300 * attempt
            log(f"[錯誤] 第 {attempt} 次請求失敗：{e}；{wait} 秒後重試")
            if attempt == retries:
                raise
            time.sleep(wait)


def fetch_all(base):
    items, offset, total = [], 0, None
    while True:
        data = fetch_json({**base, "limit": PAGE_SIZE, "offset": offset})
        total = int(data.get("total_rows") or 0)
        page = data.get("list") or []
        items.extend(page)
        offset += len(page)
        if not page or offset >= total:
            return items, total
        time.sleep(PAGE_DELAY + random.uniform(0, 1))


def to_int(s):
    try:
        return int(str(s).replace(",", ""))
    except ValueError:
        return None


def open_db():
    os.makedirs(DATA_DIR, exist_ok=True)
    db = sqlite3.connect(os.path.join(DATA_DIR, "listings.db"))
    db.executescript("""
    CREATE TABLE IF NOT EXISTS listings (
        id TEXT PRIMARY KEY,
        title TEXT, price INTEGER, seller INTEGER,
        server TEXT, page_view INTEGER, interest_count INTEGER,
        sell_count INTEGER, stock INTEGER, promoted INTEGER,
        cover TEXT, url TEXT,
        first_seen TEXT, last_seen TEXT, removed_at TEXT,
        raw TEXT
    );
    CREATE TABLE IF NOT EXISTS price_history (
        id TEXT, ts TEXT, price INTEGER
    );
    CREATE TABLE IF NOT EXISTS events (
        ts TEXT, kind TEXT, id TEXT, detail TEXT
    );
    CREATE TABLE IF NOT EXISTS runs (
        ts TEXT, total_rows INTEGER, fetched INTEGER, new INTEGER,
        price_changed INTEGER, removed INTEGER
    );
    """)
    return db


def notify(lines):
    if not DISCORD_WEBHOOK or not lines:
        return
    for i in range(0, len(lines), 10):  # Discord 一則上限 2000 字
        content = "\n".join(lines[i:i + 10])[:1990]
        req = urllib.request.Request(
            DISCORD_WEBHOOK, data=json.dumps({"content": content}).encode(),
            headers={"Content-Type": "application/json", "User-Agent": UA})
        try:
            urllib.request.urlopen(req, timeout=15).close()
        except urllib.error.URLError as e:
            log(f"[通知失敗] {e}")
        time.sleep(1)


def run_once(db, base):
    items, total = fetch_all(base)
    ts = now()
    first_run = db.execute("SELECT COUNT(*) FROM listings").fetchone()[0] == 0
    seen, new, changed, alerts = set(), 0, 0, []

    for it in items:
        lid = str(it["id"])
        seen.add(lid)
        price = to_int(it.get("ware_price"))
        title = it.get("ware_title", "")
        url = DETAIL.format(lid)
        row = (title, price, it.get("sell_user"),
               ((it.get("game_info") or {}).get("server") or {}).get("name"),
               to_int(it.get("page_view")), to_int(it.get("interest_count")),
               it.get("sell_count"), it.get("ware_number"),
               int(it.get("top_status") != 9999999),
               it.get("ware_cover_image"), url, ts,
               json.dumps(it, ensure_ascii=False))
        old = db.execute("SELECT price, removed_at FROM listings WHERE id=?", (lid,)).fetchone()
        if old is None:
            db.execute("""INSERT INTO listings (title, price, seller, server, page_view,
                interest_count, sell_count, stock, promoted, cover, url, last_seen, raw,
                id, first_seen) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""", row + (lid, ts))
            db.execute("INSERT INTO price_history VALUES (?,?,?)", (lid, ts, price))
            new += 1
            if not first_run:
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "new", lid, f"{price} {title}"))
                log(f"[新上架] ${price:,} {title} {url}" if price else f"[新上架] {title} {url}")
                alerts.append(f"🆕 **${price:,}** {title}\n<{url}>" if price else f"🆕 {title}\n<{url}>")
        else:
            db.execute("""UPDATE listings SET title=?, price=?, seller=?, server=?, page_view=?,
                interest_count=?, sell_count=?, stock=?, promoted=?, cover=?, url=?, last_seen=?,
                raw=?, removed_at=NULL WHERE id=?""", row + (lid,))
            if old[1] is not None:
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "relisted", lid, title))
                log(f"[重新上架] {title} {url}")
            if old[0] != price:
                db.execute("INSERT INTO price_history VALUES (?,?,?)", (lid, ts, price))
                db.execute("INSERT INTO events VALUES (?,?,?,?)",
                           (ts, "price", lid, f"{old[0]} -> {price} {title}"))
                changed += 1
                log(f"[變價] {old[0]} → {price} {title} {url}")
                alerts.append(f"💲 **{old[0]} → {price}** {title}\n<{url}>")

    removed = 0
    # 只有完整抓到全部筆數時才判定下架，避免抓一半誤判
    if len(seen) >= total:
        for lid, title in db.execute(
                "SELECT id, title FROM listings WHERE removed_at IS NULL").fetchall():
            if lid not in seen:
                db.execute("UPDATE listings SET removed_at=? WHERE id=?", (ts, lid))
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "removed", lid, title))
                removed += 1
                log(f"[下架/售出] {title} {DETAIL.format(lid)}")

    db.execute("INSERT INTO runs VALUES (?,?,?,?,?,?)", (ts, total, len(items), new, changed, removed))
    db.commit()
    export(db)
    notify(alerts)
    if first_run:
        log(f"首次建檔：共 {len(items)} 筆（總數 {total}）")
    else:
        log(f"完成：抓到 {len(items)}/{total} 筆，新上架 {new}、變價 {changed}、下架 {removed}")


def write_json(name, obj):
    os.makedirs(EXPORT_DIR, exist_ok=True)
    path = os.path.join(EXPORT_DIR, name)
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    os.replace(path + ".tmp", path)


EVENT_NAMES = {"new": "新上架", "price": "變價", "removed": "下架/售出", "relisted": "重新上架"}


def export(db):
    """輸出好讀的 JSON：目前在架清單 + 最近的異動。"""
    rows = db.execute("""SELECT id, price, title, server, seller, page_view, interest_count,
        promoted, first_seen, last_seen, url FROM listings WHERE removed_at IS NULL
        ORDER BY promoted, first_seen DESC, id DESC""").fetchall()
    write_json("listings.json", {
        "更新時間": now(),
        "條件": LIST_URL,
        "在架數": len(rows),
        "商品": [{
            "價格": price, "標題": title, "網址": url, "伺服器": server,
            "瀏覽": views, "關注": likes, "置頂廣告": bool(promoted),
            "首次看到": first, "最後看到": last, "編號": lid, "賣家ID": seller,
        } for lid, price, title, server, seller, views, likes, promoted, first, last, url in rows],
    })
    events = db.execute("""SELECT e.ts, e.kind, e.detail, l.url FROM events e
        LEFT JOIN listings l ON l.id = e.id ORDER BY e.rowid DESC LIMIT 200""").fetchall()
    write_json("events.json", {
        "更新時間": now(),
        "最近異動": [{"時間": ts, "類型": EVENT_NAMES.get(kind, kind), "內容": detail, "網址": url}
                 for ts, kind, detail, url in events],
    })


def main():
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
    base = api_params(LIST_URL)
    log(f"開始監看：{LIST_URL}")
    log(f"API 參數：{base}，每 {INTERVAL}±{JITTER} 秒一次")
    db = open_db()
    while True:
        try:
            run_once(db, base)
        except Exception as e:  # 單次失敗不要讓整個服務掛掉
            log(f"[本輪失敗] {type(e).__name__}: {e}")
        if os.environ.get("RUN_ONCE"):
            return
        # 用牆上時鐘分段等：電腦睡眠時單調時鐘會停，醒來後才能馬上補抓
        next_at = time.time() + INTERVAL + random.uniform(-JITTER, JITTER)
        while (left := next_at - time.time()) > 0:
            time.sleep(min(30, left))


if __name__ == "__main__":
    main()
