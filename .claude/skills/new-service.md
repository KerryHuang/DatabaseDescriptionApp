---
description: 建立新的 Application Service
---

# 建立新的 Service

在 Application 層建立新的服務介面和實作。

## 參數

- `$ARGUMENTS` - Service 名稱（例如：`SchemaCompareService`）

## 步驟

1. 確認 Service 名稱，如果未提供則詢問使用者

2. 在 `src/TableSpec.Application/Services/` 建立介面檔案 `I{ServiceName}.cs`

3. 在 `src/TableSpec.Application/Services/` 建立實作檔案 `{ServiceName}.cs`

4. 在 `src/TableSpec.Desktop/Program.cs` 的 `ConfigureServices()` 方法中註冊服務

## 介面範本

```csharp
namespace TableSpec.Application.Services;

/// <summary>
/// {Service 說明}
/// </summary>
public interface I{ServiceName}
{
    /// <summary>
    /// {方法說明}
    /// </summary>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>{回傳說明}</returns>
    Task<Result> DoSomethingAsync(CancellationToken cancellationToken = default);
}
```

## 實作範本

```csharp
namespace TableSpec.Application.Services;

/// <summary>
/// {Service 說明}
/// </summary>
public class {ServiceName} : I{ServiceName}
{
    private readonly IRepository _repository;

    /// <summary>
    /// 初始化 {ServiceName}
    /// </summary>
    /// <param name="repository">Repository 依賴</param>
    public {ServiceName}(IRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Result> DoSomethingAsync(CancellationToken cancellationToken = default)
    {
        // 實作邏輯
        throw new NotImplementedException();
    }
}
```

## DI 註冊範本

```csharp
// 在 ConfigureServices() 中加入
services.AddSingleton<I{ServiceName}, {ServiceName}>();
```

## 規範

- 介面以 `I` 開頭
- 使用建構函式注入依賴
- 方法支援 `CancellationToken`
- 使用 XML 文件註解（繁體中文）
- Application 層只能相依 Domain 層
