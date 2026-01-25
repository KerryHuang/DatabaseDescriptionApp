---
description: 建立 Git commit（繁體中文訊息）
arguments:
  - name: message
    description: 可選的 commit 訊息，未提供時自動產生
    required: false
---

# Git Commit

建立符合專案規範的 Git commit，使用繁體中文訊息。

## 步驟

### 1. 檢查變更狀態

執行以下指令查看變更：

```bash
git status
git diff --staged
git diff
```

### 2. 分析變更內容

根據變更的檔案和內容，判斷 commit 類型：

| 類型 | 說明 | 範例 |
|------|------|------|
| 新增 | 新功能或新檔案 | 新增 XXX 功能 |
| 修正 | 錯誤修復 | 修正 XXX 問題 |
| 更新 | 改進現有功能 | 更新 XXX 邏輯 |
| 重構 | 程式碼重構 | 重構 XXX 結構 |
| 文件 | 文件變更 | 更新 README |
| 測試 | 測試相關 | 新增 XXX 測試 |

### 3. 暫存變更

將相關檔案加入暫存區：

```bash
git add <檔案路徑>
```

注意：
- 避免使用 `git add -A`，應明確指定檔案
- 不要提交敏感檔案（.env、credentials 等）

### 4. 建立 Commit

使用 HEREDOC 格式建立 commit：

```bash
git commit -m "$(cat <<'EOF'
{commit 訊息}

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

### 5. 驗證結果

確認 commit 建立成功：

```bash
git log -1 --oneline
```

## Commit 訊息規範

- 使用繁體中文
- 第一行簡述變更（50 字以內）
- 描述「做了什麼」而非「為什麼」
- 使用動詞開頭（新增、修正、更新、移除、重構）

## 範例

```
/commit
/commit 修正連線測試的錯誤處理
```
