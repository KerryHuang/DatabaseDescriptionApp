---
description: 推送程式碼並建立版本 tag
arguments:
  - name: version
    description: 可選的版本號（如 v1.0.5），未提供時自動遞增
    required: false
---

# Git Push

推送程式碼到遠端倉庫，並在推送前詢問是否建立新版本 tag。

## 步驟

### 1. 檢查目前狀態

確認本地有待推送的 commit：

```bash
git status
git log origin/master..HEAD --oneline
```

### 2. 查詢目前最新版本

```bash
git tag --sort=-v:refname | head -5
```

### 3. 詢問是否建立 Tag

詢問使用者是否要建立新版本 tag：

- 顯示目前最新版本
- 建議下一個版本號（修訂版本 +1）
- 讓使用者確認或自訂版本號

### 4. 建立並推送 Tag（若使用者同意）

```bash
git tag {版本號}
git push origin {版本號}
```

### 5. 推送程式碼

```bash
git push
```

### 6. 顯示結果

確認推送成功並提供 GitHub Actions 連結。

## 版本號規範

使用語意化版本（Semantic Versioning）：`vX.Y.Z`

| 位置 | 名稱 | 遞增時機 |
|------|------|----------|
| X | 主版本 | 不相容的 API 變更 |
| Y | 次版本 | 向下相容的功能新增 |
| Z | 修訂版本 | 向下相容的問題修正 |

特殊版本：
- 預覽版：`v1.0.0-preview.1`
- Beta 版：`v1.0.0-beta.1`

## 範例

```
/push
/push v1.1.0
```
