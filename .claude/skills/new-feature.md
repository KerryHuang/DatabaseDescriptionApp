---
description: 端對端建立新功能（Entity → Repository → ViewModel）
---

# 端對端建立新功能

完整建立一個新功能所需的所有層級元件。

## 參數

- `$ARGUMENTS` - 功能名稱（例如：`Trigger`）

## 流程

### 1. 需求確認

詢問使用者：
- 功能用途說明
- 需要哪些屬性/欄位
- SQL 查詢邏輯（如果已知）

### 2. Domain 層

建立 Entity：
- `src/TableSpec.Domain/Entities/{Name}Info.cs`

建立 Repository 介面：
- `src/TableSpec.Domain/Interfaces/I{Name}Repository.cs`

### 3. Infrastructure 層

建立 Repository 實作：
- `src/TableSpec.Infrastructure/Repositories/{Name}Repository.cs`

### 4. Desktop 層

建立 ViewModel（如果需要獨立畫面）：
- `src/TableSpec.Desktop/ViewModels/{Name}ViewModel.cs`

### 5. DI 註冊

更新 `src/TableSpec.Desktop/Program.cs`：
- 註冊 Repository
- 註冊 ViewModel（如果有）

### 6. 測試

建立對應的測試檔案：
- `tests/TableSpec.Domain.Tests/{Name}InfoTests.cs`
- `tests/TableSpec.Infrastructure.Tests/{Name}RepositoryTests.cs`

## 檢查清單

- [ ] Entity 建立完成
- [ ] Repository 介面建立完成
- [ ] Repository 實作建立完成
- [ ] DI 註冊完成
- [ ] 測試檔案建立完成
- [ ] 建置成功 (`dotnet build`)
- [ ] 測試通過 (`dotnet test`)
