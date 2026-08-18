# 截圖

根目錄 `README.md` 的「畫面」區塊會引用這裡的圖片。

| 檔名 | 內容 | 產生用的網址 hash |
| --- | --- | --- |
| `01-master-detail.png` | 單頭單身 | `#` |
| `02-table.png` | 單身總表，左側面板收合 | `#view=table&table=detail:items&panel=collapsed` |
| `03-cards.png` | 卡片檢視 | `#view=cards` |
| `04-json.png` | JSON 檢視 | `#view=json` |
| `05-light.png` | 淺色主題 ＋ 紫色強調 | `#theme=light&accent=violet` |

## 重拍方式

先啟動服務，再用 Chrome 或 Edge 的無頭模式輸出 PNG（`--user-data-dir` 每次都要換新的，
示範資料才會自動查詢）：

```bash
chrome --headless=new --disable-gpu --hide-scrollbars --ignore-certificate-errors --user-data-dir="%TEMP%/shot01" --window-size=1600,1000 --virtual-time-budget=15000 --screenshot="docs/screenshots/01-master-detail.png" "https://localhost:7298/#"
```

也可以直接開瀏覽器按 Win + Shift + S 自己截，檔名對上即可。
