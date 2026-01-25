# Gemini Context

This document provides context for the Gemini AI assistant to understand the TableSpec project.

<law>一律以繁體中文回答使用者。</law>

## Project Overview

TableSpec is a cross-platform desktop application for querying and managing SQL Server database schemas. It is built using .NET 8 and Avalonia UI, following a clean architecture pattern (Domain, Application, Infrastructure, Desktop). The application allows users to connect to multiple SQL Server instances, browse database objects (tables, views, stored procedures, etc.), view detailed information about these objects, and execute custom SQL queries.

## Quick Commands

- `/build` - 建置解決方案
- `/test` - 執行測試
- `/run` - 執行桌面應用程式
- `/publish` - 發布單一執行檔

## Key Technologies

*   **.NET 8**: The underlying framework for the application.
*   **Avalonia UI**: A cross-platform UI framework for creating desktop applications.
*   **Dapper**: A simple object mapper for .NET, used for database access.
*   **Microsoft.Data.SqlClient**: The data provider for SQL Server.
*   **ClosedXML**: A library for creating Excel files, used for the export functionality.
*   **CommunityToolkit.Mvvm**: A modern MVVM library for .NET.
*   **Microsoft.Extensions.DependencyInjection**: Used for dependency injection.

## Project Structure

The project is organized into four main layers, following the principles of clean architecture:

*   `TableSpec.Domain`: Contains the core business logic and entities of the application. It has no dependencies on other layers.
*   `TableSpec.Application`: Contains the application logic and services. It depends on the Domain layer.
*   `TableSpec.Infrastructure`: Contains the implementation of services defined in the Application layer, such as data access and file I/O. It depends on the Domain and Application layers.
*   `TableSpec.Desktop`: The main application project, containing the UI and view models. It depends on all other layers.

The solution also includes a comprehensive set of unit tests for each layer.

## Building and Running

### Build

To build the project, run the following command from the root directory:

```bash
dotnet build
```

### Run

To run the application, use the following command:

```bash
dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj
```

### Test

To run the unit tests, use the following command:

```bash
dotnet test
```

## Development Conventions

*   **Clean Architecture**: The project follows a strict clean architecture, with a clear separation of concerns between the different layers.
*   **MVVM**: The desktop application uses the Model-View-ViewModel (MVVM) pattern. ViewModels use `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`).
*   **Dependency Injection**: The project uses dependency injection to decouple components and improve testability.
*   **Coding Style**: The project uses the default .NET coding style, enforced by `dotnet format`.
*   **Testing**: The project follows a Test-Driven Development (TDD) approach.
*   **CI/CD**: The project has a CI/CD pipeline set up with GitHub Actions, which builds and tests the project on every push and pull request.

## Testing

*   **Framework**: xUnit
*   **Mocking**: NSubstitute
*   **Assertions**: FluentAssertions

## Language

This project uses **Traditional Chinese** as the primary language, including UI text, code comments, and Git commit messages.

**Encoding**: All files use **UTF-8** (without BOM) and **LF** line endings. See `.editorconfig` for details.