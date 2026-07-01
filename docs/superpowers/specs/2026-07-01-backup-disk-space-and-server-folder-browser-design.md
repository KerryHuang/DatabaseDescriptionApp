# 備份頁：伺服器磁碟空間提示 + 伺服器端資料夾瀏覽 設計文件

- **日期**：2026-07-01
- **狀態**：設計已核准，待撰寫實作計畫
- **影響範圍**：Desktop 備份頁（Domain / Application / Infrastructure / Desktop 四層皆有異動）

## 1. 背景與目標

備份頁（`BackupRestoreDocumentView` 的「備份」分頁）目前有兩個缺口：

1. 使用者無法在備份前得知 SQL Server 伺服器端各磁碟的剩餘空間，難以判斷備份路徑該放哪一顆碟。
2. 「備份路徑」只能手動輸入；且目前的「瀏覽」邏輯（`BrowseBackupPathAsync`）用的是 Avalonia 本機檔案選擇器（`SaveFilePicker`），選到的其實是操作端本機路徑，**與備份路徑必須是「SQL Server 伺服器端路徑」的事實矛盾**（頁面提示文字也寫明「此路徑為伺服器端路徑，非本機路徑」）。

本次要達成：

- 在備份頁新增「伺服器磁碟空間」表格，顯示各磁碟的總量、可用空間、使用率。
- 提供類似 SSMS「尋找資料庫檔案」的**伺服器端**資料夾瀏覽對話框，讓使用者在伺服器檔案系統上挑選備份資料夾與檔名。
- 跨平台支援 Windows 與 Linux SQL Server（環境中兩者並存）。

## 2. 現況調查重點（實作前已確認）

| 項目 | 檔案 / 位置 | 說明 |
|------|-------------|------|
| 備份頁 ViewModel | `src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs` | `BackupPath`（第 55-57 行）、`GenerateDefaultBackupPath()`（310-338）、`GetSqlServerDefaultBackupPathAsync`（343-354，**分層違規**：直接在 ViewModel 連 SQL）、`BrowseBackupPathAsync`（448-469，用本機 `SaveFilePicker`，**須取代**） |
| 備份頁 View | `src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml` | 「備份設定」區塊第 86-121 行；備份路徑 TextBox 第 108-109 行；此頁目前**無**瀏覽按鈕 |
| 服務介面 | `src/Specurai.Domain/Interfaces/IBackupService.cs` | 現有 `BackupDatabaseAsync` 等；本次新增磁碟/目錄查詢方法 |
| 服務實作 | `src/Specurai.Infrastructure/Services/MssqlBackupService.cs` | 已有 `GetServerDefaultDataPathAsync`（504-519）等直連 SqlClient 查詢範式可沿用 |
| 可重用範例 | `src/Specurai.Infrastructure/Repositories/DatabaseInfoRepository.cs` | `GetDatabaseFilesAsync`（192-232）已用 `sys.dm_os_volume_stats` 取 `volume_mount_point` / `available_bytes` |

**實測結果（連線 Fupite-Staging，100.124.184.134）**：

- `sys.dm_os_host_info` → `host_platform = Windows`（Windows Server 2019）。環境中另有 Linux SQL Server（如 waydosoft01-server，路徑 `/var/opt/mssql`），故設計必須跨平台。
- `sys.dm_os_enumerate_fixed_drives` → 回傳所有固定磁碟（C:\、D:\），欄位僅 `fixed_drive_path / drive_type / drive_type_desc / free_space_in_bytes`，**無總量**。
- `sys.dm_os_volume_stats` → 有 `total_bytes` + `available_bytes`，但僅涵蓋「有資料庫檔案」的磁碟區。
- 兩者 `OUTER APPLY` 合併查詢（見 §5）在該伺服器實測成功，一次取得「所有碟 + 可用 + 總量」。
- `xp_dirtree` 因 MCP 唯讀工具阻擋 EXEC 未能於調查階段實測；App 內走直連 SqlClient 可正常執行，實作時做權限容錯。

## 3. 設計決策（與使用者確認）

| 決策 | 選定 | 理由 |
|------|------|------|
| 資料夾瀏覽完整度 | **完整樹狀（xp_dirtree）**，最接近 SSMS | 使用者要求體驗貼近 SSMS |
| 磁碟顯示內容 | **可用 + 總量**，**表格式** | 判斷空間是否足夠更直覺 |
| 磁碟涵蓋範圍 | **所有固定磁碟**（含無 DB 檔的空碟） | DB 在 C、備份可放 D，D 可能無 DB 檔 |
| 磁碟表格位置 | **獨立卡片**，置於「來源資料庫」與「備份設定」之間 | 選連線後先看磁碟、再設路徑，動線最順 |
| 對話框行為 | **顯示資料夾＋現有備份檔、可編輯檔名**，回傳「資料夾＋檔名」 | 最貼近 SSMS；點現有檔可帶入檔名以利覆蓋 |

## 4. 分層架構

沿用 Clean Architecture，並**修正現有分層違規**：所有伺服器查詢集中於 `IBackupService` / `MssqlBackupService`，ViewModel 僅呼叫服務。

```
Domain      新增實體 ServerVolumeInfo、ServerDirectoryEntry；IBackupService 增方法
Application (本功能無獨立 Application 服務，維持經 IBackupService)
Infra       MssqlBackupService 實作三個新方法（磁碟、目錄、預設備份路徑）
Desktop     備份頁卡片 + 瀏覽按鈕；新資料夾瀏覽對話框（View + ViewModel）
```

## 5. Domain：新增實體與介面

### 5.1 `ServerVolumeInfo`（`src/Specurai.Domain/Entities/`）

```csharp
public sealed class ServerVolumeInfo
{
    public required string Name { get; init; }        // "C:\" 或 "/var/opt/mssql"
    public string? Label { get; init; }               // 磁碟區標籤，可空
    public long FreeBytes { get; init; }
    public long? TotalBytes { get; init; }             // 空碟可能為 null（拿不到總量）
    public double? UsedPercent =>
        TotalBytes is > 0 ? (double)(TotalBytes.Value - FreeBytes) / TotalBytes.Value * 100 : null;
    public bool IsLowSpace =>
        TotalBytes is > 0 ? FreeBytes < TotalBytes.Value * 0.10 : false;   // 門檻：可用 < 10%
}
```

### 5.2 `ServerDirectoryEntry`（`src/Specurai.Domain/Entities/`）

```csharp
public sealed class ServerDirectoryEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    // 註：xp_dirtree 不提供檔案大小，SizeBytes 一律為 null（保留欄位以利日後擴充）
    public long? SizeBytes { get; init; }
}
```

### 5.3 `IBackupService` 新增方法

```csharp
Task<IReadOnlyList<ServerVolumeInfo>> GetServerVolumesAsync(
    string connectionString, CancellationToken ct = default);

Task<IReadOnlyList<ServerDirectoryEntry>> ListServerDirectoryAsync(
    string connectionString, string path, CancellationToken ct = default);

Task<string?> GetServerDefaultBackupPathAsync(
    string connectionString, CancellationToken ct = default);
```

## 6. Infrastructure：查詢實作（`MssqlBackupService`）

### 6.1 磁碟清單 `GetServerVolumesAsync`

主查詢（SQL 2019 CU2+）：

```sql
SELECT d.fixed_drive_path        AS Name,
       d.free_space_in_bytes     AS FreeBytes,
       v.total_bytes             AS TotalBytes,
       v.logical_volume_name     AS Label
FROM sys.dm_os_enumerate_fixed_drives AS d
OUTER APPLY (
    SELECT TOP 1 vs.total_bytes, vs.logical_volume_name
    FROM sys.master_files AS mf
    CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) AS vs
    WHERE vs.volume_mount_point = d.fixed_drive_path
) AS v;
```

**版本容錯**：若 `sys.dm_os_enumerate_fixed_drives` 不存在（SQL 2017 或更舊，捕捉 SqlException/以物件存在性判斷），改走 fallback：

- 先查 `SELECT host_platform FROM sys.dm_os_host_info`。
- `Windows` → `EXEC xp_fixeddrives`（僅可用 MB，總量 null）。
- 非 Windows（Linux）→ 僅用 `sys.dm_os_volume_stats`（DISTINCT volume_mount_point）。

### 6.2 目錄樹 `ListServerDirectoryAsync`

- 根層（`path` 為空）→ 回傳 `GetServerVolumesAsync` 各磁碟為 `IsDirectory = true` 的節點。
- 一般路徑 → `EXEC master.sys.xp_dirtree @path, 1, 1`；結果欄 `subdirectory`、`depth`、`file`（1=檔案、0=資料夾）。
  - `file = 0` → 目錄，永遠納入。
  - `file = 1` → 檔案，僅當副檔名符合 `*.bak` / `*.trn` 才納入（過濾邏輯在服務層或呼叫端，設計上放服務層）。
  - `FullPath` 以伺服器平台的分隔字元組合（Windows `\`、Linux `/`）；平台由 `host_platform` 判定。
- 權限不足 / 例外 → 拋出可辨識的例外或回傳空清單並帶錯誤旗標，供對話框顯示提示（不使 App 崩潰）。

### 6.3 `GetServerDefaultBackupPathAsync`

把原 ViewModel 內 `SELECT SERVERPROPERTY('InstanceDefaultBackupPath')` 查詢搬入服務層；查不到回傳 null，由 ViewModel 決定 fallback 檔名（不再硬編 `C:\Backup\`，改依平台或留待使用者以瀏覽對話框選擇）。

## 7. Desktop UI

### 7.1 View（`BackupRestoreDocumentView.axaml`）

- 於「來源資料庫」卡片與「備份設定」卡片之間，新增「伺服器磁碟空間」卡片：
  - `DataGrid`（或等效表格）欄位：`磁碟` / `總量` / `可用` / `使用率`（進度條 + 百分比；`IsLowSpace` 以橘/紅標示；`UsedPercent` 為 null 時使用率顯示「—」）。
  - 卡片右上「↻ 重新整理」按鈕，綁定 `RefreshVolumesCommand`。
- 「備份路徑」列右側新增「瀏覽…」按鈕，綁定改寫後的 `BrowseBackupPathCommand`。

### 7.2 ViewModel（`BackupRestoreDocumentViewModel`）

- 新增 `ObservableCollection<ServerVolumeInfo> ServerVolumes`。
- `OnSelectedProfileChanged`：選連線時自動載入磁碟清單（呼叫 `IBackupService.GetServerVolumesAsync`），並以 `GetServerDefaultBackupPathAsync` 產生預設路徑（取代原 `GetSqlServerDefaultBackupPathAsync` 內嵌 SQL）。
- `RefreshVolumesCommand`：手動重新查詢磁碟。
- 改寫 `BrowseBackupPathAsync`：開啟 `ServerFolderBrowserWindow`；使用者確定後，將回傳的「資料夾 + 檔名」組成 `BackupPath`（以伺服器平台分隔字元組合）。
- 磁碟查詢失敗：`ServerVolumes` 清空並設一個「無法取得磁碟資訊」狀態旗標供 UI 顯示，**不阻擋備份**。

### 7.3 新對話框（`ServerFolderBrowserWindow.axaml` + `ServerFolderBrowserViewModel`）

- `TreeView` 惰性載入：節點展開時才呼叫 `ListServerDirectoryAsync`（避免一次載入整棵樹）。根節點為各磁碟，並於節點顯示可用/總量摘要。
- 欄位：`選取的路徑`（唯讀顯示選中資料夾）、`檔案名稱`（可編輯；點樹中現有 .bak/.trn 檔會帶入其檔名）、`檔案類型`（*.bak;*.trn，固定顯示）。
- 「確定」回傳 `{ 資料夾, 檔名 }`；「取消」不變更。
- `xp_dirtree` 權限不足 → 對話框內顯示提示訊息；使用者仍可於主畫面 TextBox 手動輸入路徑（TextBox 保持可編輯）。
- 需設計期構造函式（無參數）+ DI 構造函式，符合本專案 ViewModel 慣例。

## 8. 錯誤處理總則

| 情境 | 行為 |
|------|------|
| 磁碟查詢失敗 / 無權限 | 卡片顯示「無法取得磁碟資訊」，備份流程不受影響 |
| `dm_os_enumerate_fixed_drives` 不存在（舊版 SQL） | 走 §6.1 平台 fallback |
| `xp_dirtree` 權限不足 | 對話框顯示提示，改由手動輸入路徑 |
| 預設備份路徑查不到 | 不硬編 Windows 路徑；提示使用者以瀏覽對話框選擇 |

## 9. 測試（TDD）

- **Domain**：`ServerVolumeInfo.UsedPercent`（含 TotalBytes 為 null 與為 0 的邊界）、`IsLowSpace` 門檻；`ServerDirectoryEntry` 基本屬性。
- **Desktop ViewModel**：mock `IBackupService`——
  - 選連線後 `ServerVolumes` 正確填入；查詢失敗時清空並設錯誤旗標。
  - `RefreshVolumesCommand` 重新查詢。
  - 瀏覽對話框確定後 `BackupPath` 正確組合（Windows `\`、Linux `/` 兩種平台）。
- **對話框 ViewModel**：惰性載入（展開才查詢）、選取檔案帶入檔名、確定回傳值、權限不足狀態。
- 遵循既有命名 `[方法]_[條件]_[預期]`（繁體中文），使用 xUnit + NSubstitute + FluentAssertions。

## 10. 範圍外（YAGNI）

- 不新增 MCP / CLI 對應工具（`IBackupService` 新方法未來可再曝露）。
- 不做整棵目錄樹一次性載入（惰性載入即可）。
- 不顯示檔案大小（`xp_dirtree` 不提供，避免額外查詢與權限成本）。
- 不記憶「上次瀏覽的資料夾」（可日後再加）。
