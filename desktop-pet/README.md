# 麻糬 desktop pet

住在工作列上的麻糬貓。

- 左鍵點：開心跳一跳
- 拖曳：抓起來晃，放手會掉下來
- 右鍵（或系統匣圖示）：餵食、摸頭、跟著滑鼠走、睡覺、叫回螢幕中間、結束
- 放著不管會自己散步，久了會睡著；會慢慢肚子餓

## 編譯

不需要安裝 SDK，用 Windows 內建的 .NET Framework 4.x C# 編譯器：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
.\bin\DesktopPet.exe
```
