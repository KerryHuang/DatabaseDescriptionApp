---
description: 執行測試
---

# 執行測試

執行專案的單元測試。

## 參數

- `$ARGUMENTS` - 可選的測試篩選條件

## 步驟

1. 如果有提供篩選條件，執行：
   ```bash
   dotnet test --filter "$ARGUMENTS"
   ```

2. 如果沒有提供篩選條件，執行所有測試：
   ```bash
   dotnet test
   ```

3. 分析測試結果，如果有失敗的測試，提供失敗原因摘要。

## 範例

- `/test` - 執行所有測試
- `/test TableQueryService` - 只執行包含 "TableQueryService" 的測試
- `/test FullyQualifiedName~Domain` - 只執行 Domain 層的測試
