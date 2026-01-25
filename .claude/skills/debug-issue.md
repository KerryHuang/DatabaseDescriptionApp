---
description: 系統性除錯問題。Use when 遇到 bug、測試失敗、或非預期行為需要追蹤根因。
---

# 系統性除錯

採用 TDD 方式進行系統性除錯，確保問題被正確識別並驗證修復。

## 除錯流程

### 1. 問題定義

收集問題資訊：
- 錯誤訊息或非預期行為
- 重現步驟
- 預期行為 vs 實際行為

### 2. 重現問題

建立失敗測試來重現問題：

```csharp
[Fact]
public async Task MethodName_WhenBugCondition_ShouldExpectedBehavior()
{
    // Arrange - 重現 bug 的條件
    var sut = new ServiceUnderTest(mockDependency);

    // Act
    var result = await sut.ProblematicMethodAsync();

    // Assert - 預期的正確行為
    result.Should().Be(expected);
}
```

執行測試確認失敗：
```bash
dotnet test --filter "FullyQualifiedName~BugCondition"
```

### 3. 根因分析

追蹤程式碼路徑：
1. 從失敗點向上追蹤呼叫堆疊
2. 檢查相關的 Repository/Service 實作
3. 驗證 SQL 查詢（如適用）
4. 檢查 DI 註冊是否正確

常見根因：
| 症狀 | 可能原因 |
|------|---------|
| NullReferenceException | DI 未註冊、連線字串為空 |
| 資料不正確 | SQL 查詢邏輯錯誤 |
| UI 未更新 | 未觸發 PropertyChanged |
| 命令無回應 | CanExecute 回傳 false |

### 4. 修復問題

根據分析結果修改程式碼，遵循 Clean Architecture：
- Domain 層問題 → 修改 Entity
- 邏輯問題 → 修改 Application Service
- 資料存取問題 → 修改 Infrastructure Repository
- UI 問題 → 修改 ViewModel/View

### 5. 驗證修復

執行先前建立的失敗測試：
```bash
dotnet test --filter "FullyQualifiedName~BugCondition"
```

執行所有測試確保無回歸：
```bash
dotnet test
```

### 6. 確認建置

確保專案可正常建置：
```bash
dotnet build
```

## 除錯工具

### 查看測試輸出
```bash
dotnet test --verbosity normal
```

### 執行特定測試專案
```bash
dotnet test tests/TableSpec.Application.Tests
```

### Mock 驗證
```csharp
// 確認方法被呼叫
await mock.Received(1).MethodAsync(Arg.Any<string>());

// 確認方法未被呼叫
mock.DidNotReceive().MethodAsync(Arg.Any<string>());
```

## 檢查清單

- [ ] 建立失敗測試重現問題
- [ ] 識別根因
- [ ] 修復程式碼
- [ ] 測試通過
- [ ] 無回歸（所有測試通過）
- [ ] 建置成功
