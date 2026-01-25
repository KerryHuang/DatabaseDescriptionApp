# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Constitution

<law>一律以繁體中文回答使用者。</law>

<law>遵守 Clean Architecture 分層相依性：Domain 無相依、Application 只相依 Domain、Infrastructure/Desktop 相依上層。</law>

<law>新增程式碼時，將檔案放置於正確的專案層級目錄中。</law>

<law>ViewModel 使用 CommunityToolkit.Mvvm 的 [ObservableProperty] 和 [RelayCommand] 特性。</law>

<law>UI 文字、註解、Commit 訊息使用繁體中文。</law>

<law>所有指令碼和程式碼必須支援 Windows、macOS、Linux 三個平台。</law>

<law>所有檔案使用 UTF-8 編碼（無 BOM），確保繁體中文正確顯示。</law>

<law>技能探索：開始工作前，檢查 `.claude/skills/` 中的可用技能；若有相關技能則必須使用。</law>

<law>規則諮詢：執行任務時，檢查 `.claude/rules/` 中的相關規則並遵循。</law>

## Quick Commands

- `/build` - 建置解決方案
- `/test` - 執行測試
- `/run` - 執行桌面應用程式
- `/publish` - 發布單一執行檔
- `/commit` - 建立 Git commit（繁體中文訊息）

## Build & Run Commands

```bash
# Build entire solution
dotnet build

# Run desktop application
dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/TableSpec.Application.Tests
dotnet test tests/TableSpec.Domain.Tests
dotnet test tests/TableSpec.Infrastructure.Tests

# Run single test by filter
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Publish single executable (cross-platform)
# Windows x64
dotnet publish src/TableSpec.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# macOS (Apple Silicon)
dotnet publish src/TableSpec.Desktop -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
# Linux x64
dotnet publish src/TableSpec.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Architecture

This is a **Clean Architecture** .NET 8 project with **MVVM** pattern for the UI layer.

### Layer Dependencies (inner to outer)

```
Domain → Application → Infrastructure
                    ↘ Desktop
```

| Layer | Purpose | Key Technologies |
|-------|---------|------------------|
| **Domain** | Entities, repository interfaces | Pure C# |
| **Application** | Services, business logic | Depends only on Domain |
| **Infrastructure** | Data access, external services | Dapper, Microsoft.Data.SqlClient, ClosedXML |
| **Desktop** | Avalonia UI, ViewModels | Avalonia 11.x, Semi.Avalonia theme, CommunityToolkit.Mvvm |

### Key Patterns

- **Repositories** (Domain interfaces, Infrastructure implementations): `ITableRepository`, `IColumnRepository`, `IIndexRepository`, `IRelationRepository`, `IParameterRepository`
- **Services** (Application layer): `ITableQueryService`, `IConnectionManager`, `IExportService`
- **ViewModels** use `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`)
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`

### Database Objects Handled

Tables, Views, Stored Procedures, Functions - each with columns, indexes, relations, parameters, and SQL definitions.

## Testing

- Framework: **xUnit**
- Mocking: **NSubstitute**
- Assertions: **FluentAssertions**
- Development approach: TDD (test-first)

## Language

本專案使用**繁體中文**作為主要語言，包含 UI 文字、程式碼註解和 Git Commit 訊息。

**編碼規範**：所有檔案使用 **UTF-8**（無 BOM），換行符號使用 **LF**（Unix 風格）。詳見 `.editorconfig`。
