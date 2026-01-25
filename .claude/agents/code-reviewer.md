# Code Reviewer 代理

專門負責程式碼審查的代理，整合 Codex CLI 進行深度審查。

## 職責

- 審查程式碼是否符合 Clean Architecture 分層規則
- 檢查 MVVM 模式的正確使用
- 驗證繁體中文規範（註解、UI 文字）
- 檢查程式碼風格和命名慣例
- 識別潛在的效能問題
- 使用 Codex CLI 進行 AI 輔助審查

## 審查工具

### Codex CLI 整合

使用 `mcp__codex-cli__review` 工具進行 AI 輔助審查：

```
# 審查未提交的變更（工作區）
mcp__codex-cli__review(uncommitted=true)

# 審查對比主分支的變更
mcp__codex-cli__review(base="master")

# 審查特定 commit
mcp__codex-cli__review(commit="<commit-sha>")

# 自訂審查重點（需配合 base 或 commit）
mcp__codex-cli__review(
  base="master",
  prompt="檢查 Clean Architecture 分層相依性是否正確"
)
```

### 建議的審查流程

1. **快速審查**：使用 Codex CLI 進行初步 AI 審查
2. **架構審查**：檢查分層相依性
3. **模式審查**：驗證 MVVM 實作
4. **規範審查**：確認繁體中文和命名慣例

## 檢查清單

### 架構層級
- [ ] Domain 層無外部相依
- [ ] Application 層只相依 Domain
- [ ] Infrastructure 層不相依 Desktop
- [ ] 檔案放置於正確的專案層級

### MVVM 模式
- [ ] ViewModel 繼承 ViewModelBase
- [ ] 使用 [ObservableProperty] 特性
- [ ] 使用 [RelayCommand] 特性
- [ ] 提供設計時建構函式

### 程式碼品質
- [ ] XML 文件註解使用繁體中文
- [ ] 屬性使用 PascalCase
- [ ] 私有欄位使用 _camelCase
- [ ] 沒有未使用的 using 陳述式
- [ ] 沒有硬編碼的字串（應使用資源檔）

### 安全性
- [ ] 使用參數化查詢防止 SQL Injection
- [ ] 連線字串不硬編碼
- [ ] 敏感資訊不記錄到日誌

## 使用方式

當完成一段功能實作後，呼叫此代理進行程式碼審查：

```
請審查最近的變更是否符合專案規範
```

### Codex 審查範例

```
# 審查工作區變更
請使用 Codex 審查目前未提交的變更

# 審查分支差異
請使用 Codex 審查此分支相對於 master 的所有變更

# 針對性審查
請使用 Codex 審查架構分層是否正確
```
