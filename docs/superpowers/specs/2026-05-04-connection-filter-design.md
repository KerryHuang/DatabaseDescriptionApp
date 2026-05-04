# 連線快速篩選器設計

**日期：** 2026-05-04
**狀態：** 已核准

## 需求

連線設定檔數量多時，下拉清單不易找到目標。需要快速篩選功能，讓使用者輸入文字即可過濾連線清單。

## 設計

### 方案

將 `MainWindow.axaml` 中的 `ComboBox` 替換為 Avalonia 內建的 `AutoCompleteBox`。

### 篩選行為

- 篩選模式：`ContainsOrdinal`（不分大小寫包含比對）
- 輸入片段即可過濾，例如輸入 "staging" 顯示所有含 "staging" 的連線

### UI 變更

**檔案：** `src/Specurai.Desktop/Views/MainWindow.axaml`

| 屬性 | 值 |
|------|-----|
| 元件 | `AutoCompleteBox` |
| `ItemsSource` | `{Binding ConnectionProfiles}` |
| `SelectedItem` | `{Binding SelectedProfile}` |
| `FilterMode` | `ContainsOrdinal` |
| `ValueMemberBinding` | `{Binding Name}` |
| `HorizontalAlignment` | `Stretch` |
| `MaxDropDownHeight` | `400` |
| `Watermark` | `請選擇連線...` |

### ViewModel 變更

無需修改。`ConnectionProfiles`（`ObservableCollection<ConnectionProfile>`）與 `SelectedProfile` 直接沿用。

### 注意事項

- `AutoCompleteBox` 清空輸入時 `SelectedItem` 會變 `null`，`OnSelectedProfileChanged` 已有 null 處理，行為符合預期。
