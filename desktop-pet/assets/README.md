# 角色素材（小糬）

小糬是用 AI 生成的圖片和語音做成的 VTuber 風格角色：固定站在螢幕一角（可拖曳移動），
靠「簡化版 Live2D」變形動起來——頭以脖子為軸輕輕擺動、臉轉向滑鼠、呼吸、兩側頭髮飄動、眨眼、表情與口型。
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

- **正面站姿、頭擺正、看向前方**；手最好不要靠近臉和脖子（頭部以脖子為軸擺動，附近的手會被一起帶動）
- **純綠色背景**，角色身上不能有綠色（若工具能直接輸出透明背景 PNG 更好）
- 表情變體一律**上傳原圖、用「編輯」產生**，不要重新生成新圖

### 原圖 `base.png`

風格：一般比例（非 Q 版）的全身立繪。最穩的做法是拿已經喜歡的那張圖去「編輯」成中性表情，
這樣風格和姿勢都不會跑掉；那張原本的開心表情圖就可以直接當 `face_happy.png`。

從零生成時：

```
Full-body anime girl standing, normal slender anime proportions (NOT chibi),
high-quality VTuber model illustration. Facing the viewer directly, head straight,
looking at the viewer, gentle closed-mouth smile, both hands tucked into the hoodie pocket.
Long straight pink twin tails reaching her thighs, white headphones with cat ears,
large violet eyes, oversized long white hoodie worn as a dress, bare legs,
white high-top sneakers with pink laces.
Clean crisp lineart, soft cel shading, flat even lighting. Entire body visible and centered,
small margin around her. Plain solid green background, no shadow, no ground,
no text, no watermark, no green anywhere on the character. Portrait canvas 1024x1536.
```

從現有的圖改成中性表情（上傳那張圖）：

```
Edit this exact image. Keep EVERYTHING identical — same pose, same position,
same canvas size, same colors, same lineart, same background. Do not move, crop,
zoom, or redraw anything except the face: eyes open looking at the viewer,
gentle closed-mouth smile.
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
| `eyes_half.png` | `both eyes half-closed, mid-blink.` | 讓眨眼更順（可省略） |
| `mouth_small.png` | `the mouth slightly open as if mid-sentence, a small gap.` | 說話（小聲） |
| `mouth_a.png` | `the mouth open wide as if saying "ah", a little tongue visible.` | 說話（大聲） |
| `mouth_o.png` | `a small round "oh" mouth.` | 說話（變化） |
| `face_happy.png` | `the eyes and mouth only: happy closed eyes shaped like ^ ^ and a big open joyful smile.` | 被摸、吃東西 |
| `face_surprised.png` | `the eyes, eyebrows and mouth only: wide eyes, slightly worried eyebrows, small open "o" mouth.` | 被拖曳換位置時（可省略，省略就維持平常表情） |

只有 `base.png` 是必要的，缺少的變體會自動略過或用相近的表情代替。
編輯後整張圖偏移幾個像素沒關係，工具會自動對齊；若某張改動太多，工具會顯示 WARNING，重新生成那一張即可。

## 語音

任何能選女聲的 AI TTS 都可以。聲音描述：

```
Young, bright, sweet female voice, Taiwanese Mandarin accent, playful and warm,
slightly fast, VTuber-like energy, clear articulation, no music, no effects.
```

目前用的是 [RealDubbing](https://realdubbing.com/zh/) 的「曉顏」。台詞裡的「糬」在 TTS 輸入時寫成同音的「薯」，避免念錯。

兩種交法（WAV、MP3、M4A、OGG、FLAC 都可以）：

- **整批**：把下表全部台詞照順序、一句一行貼進 TTS，一次生成，存成 `all.wav`。
  `build-assets.ps1` 會依停頓自動切成每一句。TTS 在句中的「！。？」也會停頓，工具會依標點把這些片段併回同一句；
  若段數對不上會報錯，那幾句改用單獨檔案補上即可。
- **單句**：一句一個檔案，檔名用下表的 id。單句檔案會覆蓋 `all.wav` 切出來的同名片段，適合重錄某一句。

沒有語音檔的台詞會改用 Windows 內建的中文女聲（Microsoft Hanhan）念出來；電腦沒有中文語音時就只顯示對話泡泡。

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
