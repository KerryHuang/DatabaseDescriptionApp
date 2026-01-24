---
description: 建立新的 Domain Entity
---

# 建立新的 Entity

在 Domain 層建立新的實體類別。

## 參數

- `$ARGUMENTS` - Entity 名稱（例如：`TriggerInfo`）

## 步驟

1. 確認 Entity 名稱，如果未提供則詢問使用者

2. 在 `src/TableSpec.Domain/Entities/` 建立新檔案 `{EntityName}.cs`

3. 使用以下範本：

```csharp
namespace TableSpec.Domain.Entities;

/// <summary>
/// {Entity 說明}
/// </summary>
public class {EntityName}
{
    // 屬性使用 init 存取子確保不可變性
    public string Property { get; init; } = string.Empty;
}
```

## 規範

- 使用 `init` 存取子（immutable）
- 加入 XML 文件註解（繁體中文）
- 不參考其他層級的套件
- 屬性名稱使用 PascalCase
