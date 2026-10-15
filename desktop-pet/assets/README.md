# 角色素材（小糬）

小糬是用 AI 生成的圖片和語音做成的「簡化版 Live2D」角色。
你只要提供一張原圖、幾張只改表情的變體、以及語音檔，其餘（去背、對齊、切出表情補丁、轉檔）都由工具自動處理。

## 流程

1. 用下面的提示詞生成圖片與語音，全部放進 `assets/xiaoshu/raw/`
2. 處理素材：`powershell -ExecutionPolicy Bypass -File build-assets.ps1`
   - 會輸出 `assets/xiaoshu/build/`（要打包的檔案）和 `assets/xiaoshu/preview.png`（檢查用的對照圖）
   - 語音轉檔用 WSL 裡的 Docker（`alpine` + ffmpeg），請先確認 Docker 有在跑
3. 編譯：`powershell -ExecutionPolicy Bypass -File build.ps1`
   - 有 `assets/<id>/build/manifest.txt` 的角色會自動打包進 exe；沒有素材時，選單裡不會出現小糬

## 圖片

**三個硬性規定：**

- **左右對稱**的正面站姿（不要只有單邊的配件）
- **純綠色背景 `#00FF00`**，角色身上不能有綠色（若工具能直接輸出透明背景 PNG 更好）
- 表情變體一律**上傳原圖、用「編輯」產生**，不要重新生成新圖

### 原圖 `base.png`

```
Full-body chibi anime girl character for a desktop mascot / VTuber-style avatar,
2.5-head-tall chibi proportions. Standing, facing the viewer directly, perfectly
symmetrical front pose, arms relaxed at her sides, feet together.
Long fluffy twin tails in pastel pink fading to white, white cat-ear headphones
with soft pink glowing accents, large sparkling violet eyes, gentle closed-mouth smile.
Oversized white hoodie with pink trim and a small round white mochi-cat face printed
on the chest, pastel lavender pleated skirt, white socks, pink sneakers.
Clean crisp lineart, soft cel shading, high-quality VTuber model illustration,
flat even lighting. Entire character visible and centered with generous margin.
Plain solid pure green (#00FF00) background, no shadow, no ground, no text,
no watermark, no green anywhere on the character. Portrait canvas 1024x1536.
```

### 表情變體

上傳 `base.png`，每張都用這段開頭，後面接表格裡的內容：

```
Edit this exact image. Keep EVERYTHING identical — same pose, same position,
same canvas size, same colors, same lineart, same background. Do not move, crop,
zoom, or redraw anything except:
```

| 檔名 | 接在開頭後面 | 用途 |
|---|---|---|
| `eyes_closed.png` | `both eyes gently closed, relaxed curved eyelids with lashes.` | 眨眼、睡覺 |
| `mouth_small.png` | `the mouth slightly open as if mid-sentence, a small gap.` | 說話（小聲） |
| `mouth_a.png` | `the mouth open wide as if saying "ah", a little tongue visible.` | 說話（大聲） |
| `mouth_o.png` | `a small round "oh" mouth.` | 說話（變化） |
| `face_happy.png` | `the eyes and mouth only: happy closed eyes shaped like ^ ^ and a big open joyful smile.` | 被摸、吃東西 |
| `face_surprised.png` | `the eyes, eyebrows and mouth only: wide eyes, slightly worried eyebrows, small open "o" mouth.` | 被抓起來 |

只有 `base.png` 是必要的，缺少的變體會自動略過或用相近的表情代替。
編輯後整張圖偏移幾個像素沒關係，工具會自動對齊；若某張改動太多，工具會顯示 WARNING，重新生成那一張即可。

## 語音

任何能選女聲的 AI TTS 都可以。聲音描述：

```
Young, bright, sweet female voice, Taiwanese Mandarin accent, playful and warm,
slightly fast, VTuber-like energy, clear articulation, no music, no effects.
```

**一句一個檔案**，檔名用下表的 id（WAV、MP3、M4A、OGG、FLAC 都可以）。
沒有語音檔的台詞會改用 Windows 內建的中文女聲（Microsoft Hanhan）念出來。

| id | 台詞 |
|---|---|
| `greeting` | 嗨嗨～我是小糬！今天也請多指教喔！ |
| `talk1` | 你在忙什麼呀？ |
| `talk2` | 記得喝水喔～ |
| `talk3` | 眼睛累了就休息一下吧！ |
| `talk4` | 嘿嘿，陪我聊天嘛～ |
| `talk5` | 今天也辛苦了！ |
| `talk6` | 好無聊喔……來玩嘛！ |
| `click1` | 呀！好癢！ |
| `click2` | 嘿嘿，喜歡你～ |
| `click3` | 再摸一下嘛！ |
| `drag1` | 哇啊啊！放我下來啦！ |
| `drag2` | 好、好高喔！ |
| `hurt` | 嗚……好痛…… |
| `hungry1` | 肚子餓餓的…… |
| `hungry2` | 想吃麻糬…… |
| `eating` | 嚼嚼嚼…… |
| `ate` | 好好吃！謝謝你！ |
| `full` | 吃不下了啦～ |
| `sleepy` | 好睏喔……晚安…… |
| `wake` | 嗯……早安…… |

台詞的程式定義在 `src/Xiaoshu.cs`，改台詞時兩邊要一起改。

> 使用前請確認 AI 工具的條款允許把生成內容放在公開 repo。
