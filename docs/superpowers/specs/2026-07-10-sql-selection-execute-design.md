# SQL 查詢分頁：選取範圍執行 設計文件

日期：2026-07-10
狀態：已由使用者確認（方案 A）

## 需求

SQL 查詢分頁的編輯器有選取文字時，**執行 (F5)** 與 **Dry Run (F6)** 只針對選取的文字執行；未選取時維持現狀（執行全部）。對齊 SSMS 行為，讓編輯器可同時放多句 SQL、選哪句跑哪句——Dry Run 只接受單一 DML，此功能是其自然配套。

## 方案（A：綁定選取索引到 ViewModel）

Avalonia TextBox 的 `SelectionStart`/`SelectionEnd` 為可雙向綁定的 StyledProperty。`SqlQueryDocumentViewModel` 新增兩個 `[ObservableProperty] int`，於 AXAML 的 `SqlTextBox` 雙向綁定。

否決方案 B（code-behind 取 SelectedText 傳命令參數）：HotKey F5/F6 不經 click handler，且邏輯進 code-behind 不可單元測試，違反專案 MVVM 規範。

## 行為規格

1. **有效 SQL 判定**（私有方法 `GetEffectiveSql()`，兩命令共用）：
   - `SelectionStart != SelectionEnd` 且選取子字串非純空白 → 選取文字
   - 否則 → 整個 `SqlText`
   - 反向選取（游標從後往前拖）以 min/max 正規化；索引超出目前文字長度時鉗制在合法範圍
2. **兩個命令共用**：`ExecuteQueryAsync` 與 `DryRunAsync` 都以有效 SQL 執行
3. **狀態列標示**：使用選取範圍時狀態訊息加註「（選取範圍）」，如「查詢完成（選取範圍）：5 筆…」「Dry Run 完成（選取範圍）：影響 1 筆…」
4. **歷史記錄**：記實際執行的那段（選取子字串），與 SSMS 一致
5. **不影響**：MCP、CLI 與其他分頁

## 測試

VM 單元測試（`SqlQueryDocumentViewModelTests`）：有選取執行選取文字、無選取執行全文、純空白選取執行全文、反向選取正規化、Dry Run 同邏輯、狀態訊息含「（選取範圍）」、歷史記錄記選取文字。
