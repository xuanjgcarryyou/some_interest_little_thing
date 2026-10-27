"""591 租屋列表監看爬蟲。

定時呼叫 591 前端自己用的列表 API（bff-house.591.com.tw/v3/web/rent/list），
把結果存進 SQLite，記錄新上架、租金變動、下架（多半是租掉了），
並輸出好讀的 JSON 到 output/。只用 Python 標準函式庫。
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

API = "https://bff-house.591.com.tw/v3/web/rent/list"
TZ = timezone(timedelta(hours=8))
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/130.0 Safari/537.36")
PER_PAGE = 30  # API 固定一頁 30 筆

DEFAULT_LIST_URL = ("https://rent.591.com.tw/list?region=3&kind=1&other=pet"
                    "&price=0$_20000$&layout=2,3,4&sort=posttime_desc")
LIST_URL = os.environ.get("LIST_URL", "").strip() or DEFAULT_LIST_URL
INTERVAL = int(os.environ.get("INTERVAL_SECONDS", "600"))
JITTER = int(os.environ.get("JITTER_SECONDS", "120"))  # 每輪間隔 = INTERVAL ± JITTER 隨機
MAX_PAGES = int(os.environ.get("MAX_PAGES", "20"))
PAGE_DELAY = float(os.environ.get("PAGE_DELAY_SECONDS", "4"))
DATA_DIR = os.environ.get("DATA_DIR", "/data")
EXPORT_DIR = os.environ.get("EXPORT_DIR", "/output")
DISCORD_WEBHOOK = os.environ.get("DISCORD_WEBHOOK_URL", "").strip()


def now():
    return datetime.now(TZ).isoformat(timespec="seconds")


def log(msg):
    print(f"{now()} {msg}", flush=True)


def api_params(list_url):
    """網頁網址參數 → API 參數，照 591 前端（wt 函式）的轉換規則。"""
    q = dict(urllib.parse.parse_qsl(urllib.parse.urlparse(list_url).query))
    p = {}

    def search(key, value, searchtype):
        p[key] = value
        p["searchtype"] = searchtype

    def range_or(key_range, key_multi, value):
        # "0$_20000$" → 0,20000（自訂區間）；否則是選項代碼
        if "$" in value:
            p[key_range] = value.replace("$", "").replace("_", ",", 1)
        else:
            p[key_multi] = value

    rename = {"region": "regionid", "section": "sectionid", "source": "role",
              "layout": "multiRoom", "floor": "multiFloor", "bathroom": "multiToilet",
              "notice": "multiNotice"}
    for k, v in q.items():
        if not v:
            continue
        if k in rename:
            p[rename[k]] = v
        elif k == "metro":
            search("mrtline", v, "4")
        elif k == "station":
            search("mrtcoods", v, "4")
        elif k == "school":
            search("school", v, "3")
        elif k == "shopping":
            search("busness", v, "2")
        elif k == "price":
            range_or("rentprice", "multiPrice", v)
        elif k == "acreage":
            range_or("area", "multiArea", v)
        elif k in ("sort", "order"):
            if k == "order" and "sort" in q:
                continue
            order, _, order_type = v.partition("_")
            p["order"], p["orderType"] = order, order_type
        elif k == "other":
            tags = v.split(",")
            sh = [t for t in ("best_house", "video", "host", "18") if t in tags]
            if sh:
                p["shType"] = ",".join(sh)
            rest = [t for t in tags if t not in sh]
            if rest:
                p["other"] = ",".join(rest)
        elif k != "page":
            p[k] = v
    return p


def fetch_json(params, retries=3):
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={
        "User-Agent": UA,
        "Referer": "https://rent.591.com.tw/",
        "Origin": "https://rent.591.com.tw",
        "Accept": "application/json",
    })
    for attempt in range(1, retries + 1):
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                body = json.load(r)
            if body.get("status") != 1:
                raise RuntimeError(f"API status={body.get('status')} {body.get('msg')}")
            return body["data"]
        except (urllib.error.URLError, RuntimeError, ValueError, TimeoutError) as e:
            wait = 30 * attempt
            if isinstance(e, urllib.error.HTTPError) and e.code in (403, 429):
                wait = 300 * attempt
            log(f"[錯誤] 第 {attempt} 次請求失敗：{e}；{wait} 秒後重試")
            if attempt == retries:
                raise
            time.sleep(wait)


def fetch_all(base):
    """回傳 (物件清單, 總筆數, 是否完整抓完)。"""
    items, total = [], 0
    for page in range(MAX_PAGES):
        data = fetch_json({**base, "firstRow": page * PER_PAGE})
        total = int(data.get("total") or 0)
        batch = data.get("items") or []
        items.extend(batch)
        if not batch or len(items) >= total:
            return items, total, True
        time.sleep(PAGE_DELAY + random.uniform(0, 2))
    log(f"[注意] 已達 MAX_PAGES={MAX_PAGES}（{len(items)}/{total} 筆），這輪不判定下架")
    return items, total, False


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
        title TEXT, price INTEGER, kind TEXT, layout TEXT, area TEXT, floor TEXT,
        address TEXT, community TEXT, tags TEXT, contact TEXT, refresh_time TEXT,
        views INTEGER, cover TEXT, url TEXT, rank INTEGER,
        first_seen TEXT, last_seen TEXT, removed_at TEXT,
        raw TEXT
    );
    CREATE TABLE IF NOT EXISTS price_history (id TEXT, ts TEXT, price INTEGER);
    CREATE TABLE IF NOT EXISTS events (ts TEXT, kind TEXT, id TEXT, detail TEXT);
    CREATE TABLE IF NOT EXISTS runs (
        ts TEXT, total INTEGER, fetched INTEGER, new INTEGER,
        price_changed INTEGER, removed INTEGER
    );
    """)
    return db


def notify(lines):
    if not DISCORD_WEBHOOK or not lines:
        return
    for i in range(0, len(lines), 8):  # Discord 一則上限 2000 字
        content = "\n".join(lines[i:i + 8])[:1990]
        req = urllib.request.Request(
            DISCORD_WEBHOOK, data=json.dumps({"content": content}).encode(),
            headers={"Content-Type": "application/json", "User-Agent": UA})
        try:
            urllib.request.urlopen(req, timeout=15).close()
        except urllib.error.URLError as e:
            log(f"[通知失敗] {e}")
        time.sleep(1)


def summary(price, it):
    return (f"${price:,}" if price else "?") + \
        f"｜{it.get('layoutStr', '')}｜{it.get('area_name', '')}｜{it.get('address', '')}｜{it.get('title', '')}"


def run_once(db, base):
    items, total, complete = fetch_all(base)
    ts = now()
    first_run = db.execute("SELECT COUNT(*) FROM listings").fetchone()[0] == 0
    seen, new, changed, alerts = set(), 0, 0, []

    for rank, it in enumerate(items):
        lid = str(it["id"])
        if lid in seen:  # 翻頁時偶爾重複
            continue
        seen.add(lid)
        price = to_int(it.get("price"))
        url = it.get("url") or f"https://rent.591.com.tw/{lid}"
        row = (it.get("title", ""), price, it.get("kind_name"), it.get("layoutStr"),
               it.get("area_name"), it.get("floor_name"), it.get("address"),
               it.get("community_name"), json.dumps(it.get("tags") or [], ensure_ascii=False),
               it.get("role_name"), it.get("refresh_time"), it.get("browse_count"),
               it.get("cover"), url, rank, ts, json.dumps(it, ensure_ascii=False))
        old = db.execute("SELECT price, removed_at FROM listings WHERE id=?", (lid,)).fetchone()
        if old is None:
            db.execute("""INSERT INTO listings (title, price, kind, layout, area, floor, address,
                community, tags, contact, refresh_time, views, cover, url, rank, last_seen, raw,
                id, first_seen) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""", row + (lid, ts))
            db.execute("INSERT INTO price_history VALUES (?,?,?)", (lid, ts, price))
            new += 1
            if not first_run:
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "new", lid, summary(price, it)))
                log(f"[新上架] {summary(price, it)} {url}")
                alerts.append(f"🏠 **新上架** {summary(price, it)}\n<{url}>")
        else:
            db.execute("""UPDATE listings SET title=?, price=?, kind=?, layout=?, area=?, floor=?,
                address=?, community=?, tags=?, contact=?, refresh_time=?, views=?, cover=?, url=?,
                rank=?, last_seen=?, raw=?, removed_at=NULL WHERE id=?""", row + (lid,))
            if old[1] is not None:
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "relisted", lid, summary(price, it)))
                log(f"[重新上架] {summary(price, it)} {url}")
            if old[0] != price:
                db.execute("INSERT INTO price_history VALUES (?,?,?)", (lid, ts, price))
                db.execute("INSERT INTO events VALUES (?,?,?,?)",
                           (ts, "price", lid, f"{old[0]} → {price}｜{summary(price, it)}"))
                changed += 1
                arrow = "⬇️ 降價" if old[0] and price and price < old[0] else "⬆️ 漲價"
                log(f"[{arrow[2:].strip()}] {old[0]} → {price} {summary(price, it)} {url}")
                alerts.append(f"{arrow} **{old[0]} → {price}** {summary(price, it)}\n<{url}>")

    removed = 0
    # 只有完整抓到全部筆數時才判定下架，避免抓一半誤判
    if complete and len(seen) >= total:
        for lid, title in db.execute(
                "SELECT id, title FROM listings WHERE removed_at IS NULL").fetchall():
            if lid not in seen:
                db.execute("UPDATE listings SET removed_at=? WHERE id=?", (ts, lid))
                db.execute("INSERT INTO events VALUES (?,?,?,?)", (ts, "removed", lid, title))
                removed += 1
                log(f"[下架] {title} https://rent.591.com.tw/{lid}")

    db.execute("INSERT INTO runs VALUES (?,?,?,?,?,?)", (ts, total, len(seen), new, changed, removed))
    db.commit()
    export(db)
    notify(alerts)
    if first_run:
        log(f"首次建檔：共 {len(seen)} 筆（總數 {total}）")
    else:
        log(f"完成：抓到 {len(seen)}/{total} 筆，新上架 {new}、租金變動 {changed}、下架 {removed}")


def write_json(name, obj):
    os.makedirs(EXPORT_DIR, exist_ok=True)
    path = os.path.join(EXPORT_DIR, name)
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    os.replace(path + ".tmp", path)


EVENT_NAMES = {"new": "新上架", "price": "租金變動", "removed": "下架", "relisted": "重新上架"}


def district(address):
    """「板橋區-金門街379巷」→「板橋區」。"""
    head = (address or "").split("-", 1)[0].strip()
    return head if head.endswith(("區", "市", "鎮", "鄉")) else "其他"


def export(db):
    """輸出好讀的 JSON：在架清單（新的在上）、依行政區分類、最近的異動。"""
    rows = db.execute("""SELECT id, price, title, layout, area, floor, address, community, tags,
        contact, refresh_time, views, url, first_seen, last_seen FROM listings
        WHERE removed_at IS NULL ORDER BY first_seen DESC, rank""").fetchall()
    listings = [{
        "租金": price, "標題": title, "網址": url, "區": district(address), "格局": layout,
        "坪數": area, "樓層": floor, "地址": address, "社區": community or None,
        "標籤": json.loads(tags or "[]"), "聯絡人": contact, "591更新": refresh_time,
        "瀏覽": views, "首次看到": first, "最後看到": last, "編號": lid,
    } for lid, price, title, layout, area, floor, address, community, tags, contact,
        refresh_time, views, url, first, last in rows]
    write_json("listings.json", {
        "更新時間": now(),
        "條件": LIST_URL,
        "在架數": len(listings),
        "物件": listings,
    })

    groups = {}
    for it in listings:
        groups.setdefault(it["區"], []).append(it)
    order = sorted(groups, key=lambda d: (-len(groups[d]), d))  # 物件多的區排前面

    def overview(items):
        prices = [it["租金"] for it in items if it["租金"]]
        return {"數量": len(items),
                "最低租金": min(prices) if prices else None,
                "平均租金": round(sum(prices) / len(prices)) if prices else None,
                "最高租金": max(prices) if prices else None}

    write_json("by_district.json", {
        "更新時間": now(),
        "條件": LIST_URL,
        "在架數": len(listings),
        "各區概況": {d: overview(groups[d]) for d in order},
        "分區物件": {d: [{k: v for k, v in it.items() if k != "區"} for it in groups[d]]
                 for d in order},
    })
    events = db.execute("""SELECT e.ts, e.kind, e.detail, e.id FROM events e
        ORDER BY e.rowid DESC LIMIT 200""").fetchall()
    write_json("events.json", {
        "更新時間": now(),
        "最近異動": [{"時間": ts, "類型": EVENT_NAMES.get(kind, kind), "內容": detail,
                  "網址": f"https://rent.591.com.tw/{lid}"} for ts, kind, detail, lid in events],
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
