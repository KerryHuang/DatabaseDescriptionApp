# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Constitution

<law>一律以繁體中文回答使用者。</law>

<law>遵守 Clean Architecture 分層相依性，詳見 `.claude/rules/clean-architecture.md`。</law>

<law>ViewModel 使用 CommunityToolkit.Mvvm，詳見 `.claude/rules/mvvm-patterns.md`。</law>

<law>UI 文字、註解、Commit 訊息使用繁體中文。</law>

<law>所有腳本跨平台（Windows/macOS/Linux），檔案使用 UTF-8 無 BOM，詳見 `.claude/rules/cross-platform-scripts.md`。</law>

<law>技能探索：開始工作前，檢查 `.claude/skills/` 中的可用技能；若有相關技能則必須使用。</law>

<law>規則諮詢：執行任務時，檢查 `.claude/rules/` 中的相關規則並遵循。</law>

<law>程式碼審查：每次完成功能實作、Bug 修復或重構後，必須使用 `superpowers:requesting-code-review` 技能進行程式碼審查，再回報完成。</law>

## Quick Commands

- `/build` - 建置解決方案
- `/test` - 執行測試
- `/run` - 執行桌面應用程式
- `/publish` - 發布單一執行檔
- `/install-local` - 建置並安裝桌面 App／MCP Server／CLI 到本機（macOS）
- `/commit` - 建立 Git commit（繁體中文訊息）
- `/push` - 推送程式碼並建立版本 tag

## Build & Run Commands

```bash
# Build entire solution
dotnet build

# Run desktop application
dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj
dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj
dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj

# Run single test by filter
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Publish single executable (cross-platform)
# Windows x64
dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# macOS (Apple Silicon)
dotnet publish src/Specurai.Desktop -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
# macOS (Intel)
dotnet publish src/Specurai.Desktop -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
# Linux x64
dotnet publish src/Specurai.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Architecture

This is a **Clean Architecture** .NET 8 project with **MVVM** pattern for the UI layer.

### Layer Dependencies (inner to outer)

```
Domain → Application → Infrastructure
                    ↘ Desktop
                    ↘ McpServer
                    ↘ Cli
```

| Layer | Purpose | Key Technologies |
|-------|---------|------------------|
| **Domain** | Entities, repository interfaces | Pure C# |
| **Application** | Services, business logic | Depends only on Domain |
| **Infrastructure** | Data access, external services | Dapper, Microsoft.Data.SqlClient, ClosedXML |
| **Desktop** | Avalonia UI, ViewModels | Avalonia 11.x, Semi.Avalonia theme, CommunityToolkit.Mvvm |
| **McpServer** | MCP 工具伺服器（供 AI 客戶端使用） | Microsoft.Extensions.Hosting, MCP SDK |
| **Cli** | 命令列工具 | System.CommandLine |

### Key Patterns

- **Repositories** (Domain interfaces, Infrastructure implementations): `ITableRepository`, `IColumnRepository`, `IIndexRepository`, `IRelationRepository`, `IParameterRepository`, `IAgentJobRepository`, `IDatabaseInfoRepository`
- **Services** (Application layer): `ITableQueryService`, `IConnectionManager`, `IExportService`, `IMaintenancePlanService`, `IAgentJobService`
- **ViewModels** use `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`)
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`

### Database Objects Handled

Tables, Views, Stored Procedures, Functions - each with columns, indexes, relations, parameters, and SQL definitions. Also manages SQL Agent Jobs for maintenance plans (backup/restore schedules).

## Testing

- Framework: **xUnit**
- Mocking: **NSubstitute**
- Assertions: **FluentAssertions**
- Development approach: TDD (test-first)

