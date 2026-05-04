# Recovery Model 管理功能設計文件

**日期：** 2026-05-04
**功能：** 調整資料庫 Recovery Model（SIMPLE / FULL）

---

## 功能概述

新增獨立 MDI 文件頁，讓使用者查詢所有資料庫的 Recovery Model，並透過下拉選單修改，按儲存後批次執行 `ALTER DATABASE` 變更。

---

## 架構設計（Clean Architecture）

### Domain 層

**Entity：** `Specurai.Domain/Entities/DatabaseRecoveryModel.cs`

```csharp
public class DatabaseRecoveryModel
{
    public required string DatabaseName { get; init; }
    public required string RecoveryModel { get; init; } // "SIMPLE" | "FULL" | "BULK_LOGGED"
}
```

**Repository 介面：** `Specurai.Domain/Interfaces/IDatabaseRecoveryModelRepository.cs`

- `Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync()`
- `Task SetRecoveryModelAsync(string databaseName, string recoveryModel)`

---

### Application 層

**Service 介面：** `Specurai.Application/Services/IDatabaseRecoveryModelService.cs`

- `Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync()`
- `Task SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)> changes)`

**Service 實作：** `Specurai.Application/Services/DatabaseRecoveryModelService.cs`

- 依序呼叫 Repository 的 `SetRecoveryModelAsync`，逐一執行每筆變更
- 若任一筆失敗，拋出例外並停止（不做 rollback，讓使用者重新調整）

---

### Infrastructure 層

**Repository 實作：** `Specurai.Infrastructure/Repositories/DatabaseRecoveryModelRepository.cs`

查詢 SQL：
```sql
SELECT name AS DatabaseName, recovery_model_desc AS RecoveryModel
FROM sys.databases
ORDER BY name;
```

變更 SQL（動態組合，DatabaseName 僅允許從查詢結果取得，不接受使用者輸入）：
```sql
ALTER DATABASE [{databaseName}] SET RECOVERY SIMPLE; -- 或 FULL
```

---

### Desktop 層

**ViewModel：** `Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs`

- 繼承 `DocumentViewModel`
- `DocumentType` = `"RecoveryModel"`
- `ObservableCollection<RecoveryModelRowViewModel> Rows`
- `bool HasChanges`（計算屬性，`Rows.Any(r => r.IsDirty)`）
- `[RelayCommand] LoadAsync()`
- `[RelayCommand(CanExecute = nameof(HasChanges))] SaveAsync()`
- 儲存前開啟確認對話框，列出所有 dirty rows

**Row ViewModel：** `RecoveryModelRowViewModel`（巢狀或獨立類別）

- `DatabaseName`、`OriginalRecoveryModel`、`SelectedRecoveryModel`
- `IsDirty`（計算屬性）

**View：** `Specurai.Desktop/Views/RecoveryModelDocumentView.axaml`

- DataGrid 顯示所有資料庫
- RecoveryModel 欄位使用 ComboBox（SIMPLE / FULL）
- IsDirty = true 的列套用紅色前景色
- 工具列：重新整理按鈕、已變更筆數、儲存變更按鈕

**確認對話框：** 使用現有 `ConfirmExecuteCallback` 模式，或 `MessageBox` 列出變更清單

**選單整合：** 在 `ObjectTreeViewModel` 或連線選單新增「Recovery Model」入口

---

## UI 行為流程

1. 使用者點擊選單「Recovery Model」→ 開啟文件頁並自動載入
2. DataGrid 顯示所有資料庫（含系統庫），每列有 ComboBox
3. 使用者修改任意列的 ComboBox → 該列標紅、`HasChanges = true`
4. 按「儲存變更」→ 出現確認對話框，列出所有變更項目（原值 → 新值）
5. 確認後批次執行 `ALTER DATABASE`，完成後重新載入清單
6. 若執行失敗，顯示錯誤訊息，清單保留變更狀態供使用者重試

---

## 測試範圍

- **Domain：** `DatabaseRecoveryModel` entity 屬性
- **Application：** `DatabaseRecoveryModelService` - mock repository，驗證只對 dirty 項目呼叫 `SetRecoveryModelAsync`
- **Desktop：** `RecoveryModelDocumentViewModel` - 初始狀態、`HasChanges` 計算、設計時建構函式

---

## 不在範圍內

- `BULK_LOGGED` 模式不提供選項（只顯示，不允許設為此值）
- 不支援批次復原（失敗停止，使用者手動重試）
- 不做 SQL Agent Job 相關整合
