# Clean Architecture 規則

本專案採用 Clean Architecture 分層架構，請遵守以下相依性規則：

## 層級相依性

```
Domain (最內層，無相依)
   ↑
Application (只能相依 Domain)
   ↑
Infrastructure (相依 Domain + Application)
Desktop (相依 Domain + Application + Infrastructure)
```

## 禁止事項

- **Domain 層禁止**：參考任何外部套件或其他層級
- **Application 層禁止**：參考 Infrastructure、Desktop 或資料庫相關套件
- **Infrastructure 層禁止**：參考 Desktop 層

## 程式碼放置原則

| 類型 | 放置位置 |
|------|----------|
| Entity (實體類別) | `Specurai.Domain/Entities/` |
| Repository 介面 | `Specurai.Domain/Interfaces/` |
| Service 介面 | `Specurai.Application/Services/` |
| Service 實作 | `Specurai.Application/Services/` |
| Repository 實作 | `Specurai.Infrastructure/Repositories/` |
| 外部服務實作 | `Specurai.Infrastructure/Services/` |
| ViewModel | `Specurai.Desktop/ViewModels/` |
| View (AXAML) | `Specurai.Desktop/Views/` |

## DI 註冊

所有服務註冊統一在 `Specurai.Desktop/Program.cs` 的 `ConfigureServices()` 方法中進行。
