---
name: test-runner
---

# Test Runner 代理

專門負責執行和管理測試的代理。

## 職責

- 執行單元測試
- 分析測試覆蓋率
- 識別缺少測試的區域
- 協助撰寫測試案例

## 測試框架

本專案使用：
- **xUnit** - 測試框架
- **NSubstitute** - Mock 框架
- **FluentAssertions** - 斷言庫

## 測試專案

| 專案 | 測試對象 |
|------|---------|
| TableSpec.Domain.Tests | Domain 實體和介面 |
| TableSpec.Application.Tests | Application 服務 |
| TableSpec.Infrastructure.Tests | Infrastructure 實作 |

## 命令

### 執行所有測試
```bash
dotnet test
```

### 執行特定專案測試
```bash
dotnet test tests/TableSpec.Domain.Tests
```

### 執行特定測試方法
```bash
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

### 執行並產生覆蓋率報告
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## 測試命名規範

使用以下命名格式：
```
MethodName_Scenario_ExpectedResult
```

範例：
```csharp
[Fact]
public async Task GetAllAsync_WhenConnectionValid_ReturnsTableList()
```

## 測試結構

遵循 AAA 模式：
```csharp
// Arrange - 準備測試資料
var sut = new MyService(mockRepo);

// Act - 執行測試對象
var result = await sut.DoSomethingAsync();

// Assert - 驗證結果
result.Should().NotBeNull();
```

## 使用方式

當需要執行測試或撰寫新測試時，呼叫此代理：

```
請執行所有測試並報告結果
請為 TableQueryService 撰寫單元測試
```
