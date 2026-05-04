# 連線快速篩選器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將主視窗連線選單從 `ComboBox` 替換為 `AutoCompleteBox`，支援輸入文字即時篩選連線清單。

**Architecture:** 純 UI 層變更，只修改 `MainWindow.axaml`。`AutoCompleteBox` 使用 `ContainsOrdinal` 篩選模式，直接綁定既有的 `ConnectionProfiles` 與 `SelectedProfile`，不需異動 ViewModel 或其他層。

**Tech Stack:** Avalonia 11.x `AutoCompleteBox`（內建於 `Avalonia.Controls` 命名空間）

---

### Task 1: 將 ComboBox 替換為 AutoCompleteBox

**Files:**
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml:266-276`

- [ ] **Step 1: 確認目前 ComboBox 內容**

開啟 `src/Specurai.Desktop/Views/MainWindow.axaml`，確認第 266–276 行內容如下：

```xml
<ComboBox ItemsSource="{Binding ConnectionProfiles}"
          SelectedItem="{Binding SelectedProfile}"
          PlaceholderText="請選擇連線..."
          HorizontalAlignment="Stretch"
          MaxDropDownHeight="400">
    <ComboBox.ItemTemplate>
        <DataTemplate x:DataType="domain:ConnectionProfile">
            <TextBlock Text="{Binding Name}"/>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

- [ ] **Step 2: 替換為 AutoCompleteBox**

將上述 `ComboBox` 區塊完整替換為：

```xml
<AutoCompleteBox ItemsSource="{Binding ConnectionProfiles}"
                 SelectedItem="{Binding SelectedProfile}"
                 ValueMemberBinding="{Binding Name}"
                 FilterMode="ContainsOrdinal"
                 Watermark="請選擇連線..."
                 HorizontalAlignment="Stretch"
                 MaxDropDownHeight="400"/>
```

> **注意：** `AutoCompleteBox` 不需要 `ItemTemplate` 子元素，`ValueMemberBinding` 已指定顯示 `Name` 屬性。

- [ ] **Step 3: 建置確認無編譯錯誤**

```bash
dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj
```

預期輸出：`Build succeeded.`

- [ ] **Step 4: 執行應用程式目視測試**

```bash
dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj
```

確認事項：
1. 連線下拉區域顯示為可輸入的文字框
2. 點擊後展開連線清單
3. 輸入 "staging" 後清單只顯示含 "staging" 的項目（不分大小寫）
4. 輸入片段後選取項目，`SelectedProfile` 正確切換（左側物件樹更新）
5. 清空輸入文字後清單恢復全部項目

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/Views/MainWindow.axaml
git commit -m "feat(desktop): 連線選單改用 AutoCompleteBox 支援快速篩選"
```
