---
description: 建立新的單元測試
---

# 建立新的單元測試

為指定的類別建立單元測試檔案。

## 參數

- `$ARGUMENTS` - 要測試的類別名稱（例如：`TableQueryService`）

## 步驟

1. 確認要測試的類別名稱，如果未提供則詢問使用者

2. 確定類別所屬的層級，對應到測試專案：
   - Domain → `tests/TableSpec.Domain.Tests/`
   - Application → `tests/TableSpec.Application.Tests/`
   - Infrastructure → `tests/TableSpec.Infrastructure.Tests/`

3. 建立測試檔案 `{ClassName}Tests.cs`

4. 依據類別的公開方法建立測試案例骨架

## 測試檔案範本

```csharp
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace TableSpec.{Layer}.Tests;

/// <summary>
/// {ClassName} 的單元測試
/// </summary>
public class {ClassName}Tests
{
    private readonly {ClassName} _sut;
    private readonly IMockDependency _mockDependency;

    public {ClassName}Tests()
    {
        _mockDependency = Substitute.For<IMockDependency>();
        _sut = new {ClassName}(_mockDependency);
    }

    [Fact]
    public async Task MethodName_WhenCondition_ShouldExpectedResult()
    {
        // Arrange
        var expected = new ExpectedType();
        _mockDependency.SomeMethod().Returns(expected);

        // Act
        var result = await _sut.MethodNameAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MethodName_WhenInvalidInput_ShouldThrowException(string? input)
    {
        // Arrange

        // Act
        var act = () => _sut.MethodNameAsync(input);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
```

## 測試命名規範

使用以下格式：
```
MethodName_Scenario_ExpectedResult
```

範例：
- `GetAllAsync_WhenConnectionValid_ReturnsTableList`
- `GetByIdAsync_WhenIdNotFound_ReturnsNull`
- `CreateAsync_WhenDuplicateName_ThrowsException`

## AAA 模式

所有測試遵循 AAA 模式：

```csharp
// Arrange - 準備測試資料和 Mock
var mockRepo = Substitute.For<IRepository>();
mockRepo.GetAsync(Arg.Any<int>()).Returns(testData);

// Act - 執行被測試的方法
var result = await _sut.ProcessAsync(input);

// Assert - 驗證結果
result.Should().NotBeNull();
result.Count.Should().Be(5);
```

## FluentAssertions 常用斷言

```csharp
// 基本斷言
result.Should().NotBeNull();
result.Should().Be(expected);
result.Should().BeEquivalentTo(expected);

// 集合斷言
list.Should().HaveCount(5);
list.Should().Contain(x => x.Id == 1);
list.Should().BeInAscendingOrder(x => x.Name);

// 例外斷言
act.Should().Throw<InvalidOperationException>()
   .WithMessage("*expected message*");

// 異步例外斷言
await act.Should().ThrowAsync<ArgumentException>();
```

## NSubstitute 常用 Mock

```csharp
// 建立 Mock
var mock = Substitute.For<IService>();

// 設定回傳值
mock.GetAsync(Arg.Any<int>()).Returns(data);

// 設定異步回傳
mock.GetAsync(1).Returns(Task.FromResult(data));

// 驗證呼叫
await mock.Received(1).SaveAsync(Arg.Any<Entity>());
mock.DidNotReceive().DeleteAsync(Arg.Any<int>());
```

## 規範

- 每個測試類別對應一個被測試類別
- 使用 `[Fact]` 標記單一測試
- 使用 `[Theory]` + `[InlineData]` 標記參數化測試
- 測試方法名稱使用繁體中文註解說明
- 遵循 AAA 模式
