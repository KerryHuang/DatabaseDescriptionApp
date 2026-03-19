# 連線設定匯出/匯入功能設計

## 概述

為 Specurai 應用程式新增連線設定的匯出與匯入功能，讓使用者能在不同電腦或團隊成員之間分享資料庫連線設定。

## 需求

- 支援純文字 JSON 和 AES 加密 JSON 兩種匯出格式
- 支援全部匯出和選擇性匯出
- 密碼處理由使用者決定：加密格式預設包含密碼，純文字格式預設排除密碼
- 匯入衝突時逐一詢問使用者處理方式
- UI 入口在主視窗選單列

## 架構設計

### 層級分工

| 層級 | 新增內容 | 職責 |
|------|----------|------|
| Domain | `ConnectionExportData` 模型 | 匯出資料結構定義 |
| Application | `IConnectionExportService` 介面 | 匯出/匯入業務邏輯抽象 |
| Infrastructure | `ConnectionExportService` 實作 | JSON 序列化、AES 加密/解密、檔案讀寫 |
| Desktop | 主選單項目、匯出/匯入對話框 | UI 互動與流程控制 |

### Domain 層

新增 `ConnectionExportData` 於 `Specurai.Domain/Entities/`：

```csharp
public class ConnectionExportData
{
    public int Version { get; init; } = 1;
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;
    public required IReadOnlyList<ConnectionProfile> Profiles { get; init; }
}
```

### Application 層

新增 `IConnectionExportService` 於 `Specurai.Application/Services/`：

```csharp
public interface IConnectionExportService
{
    /// <summary>匯出連線設定為 JSON 位元組陣列</summary>
    byte[] ExportToJson(IReadOnlyList<ConnectionProfile> profiles, bool includePasswords);

    /// <summary>匯出連線設定為加密位元組陣列</summary>
    byte[] ExportToEncryptedJson(IReadOnlyList<ConnectionProfile> profiles, string password, bool includePasswords);

    /// <summary>從 JSON 匯入連線設定</summary>
    ConnectionExportData ImportFromJson(byte[] data);

    /// <summary>從加密 JSON 匯入連線設定</summary>
    ConnectionExportData ImportFromEncryptedJson(byte[] data, string password);

    /// <summary>偵測檔案是否為加密格式</summary>
    bool IsEncryptedFormat(byte[] data);
}
```

### Infrastructure 層

新增 `ConnectionExportService` 於 `Specurai.Infrastructure/Services/`：

**加密方案：**
- 演算法：AES-256-CBC
- 金鑰衍生：PBKDF2 with SHA-256, 100,000 iterations
- 隨機 Salt（16 bytes）+ 隨機 IV（16 bytes）存入檔案標頭
- 檔案開頭以 magic bytes `TSEC`（4 bytes）標識加密格式

**純文字 JSON 格式 (.json)：**
```json
{
    "Version": 1,
    "ExportedAt": "2026-03-12T10:30:00Z",
    "Profiles": [
        {
            "Name": "生產環境",
            "Server": "db-server",
            "Database": "MyDB",
            "AuthType": 0,
            "Username": null,
            "Password": null,
            "IsDefault": false
        }
    ]
}
```

**加密格式 (.tsjson) 二進位結構：**
```
[TSEC 4 bytes][Salt 16 bytes][IV 16 bytes][AES 加密的 JSON 內容]
```

### Desktop 層

#### 主選單

在 `MainWindow.axaml` 新增選單列，加入「連線」選單：
- 匯出連線設定...
- 匯入連線設定...

#### 匯出對話框 (ExportConnectionsWindow)

**UI 元素：**
- 連線清單（CheckBox 勾選）+ 全選/取消全選
- 格式選擇：純文字 JSON / 加密 JSON（RadioButton）
- 包含密碼勾選框（加密時預設勾選，純文字時預設不勾選）
- 加密密碼輸入（選擇加密時顯示，含確認欄位）
- 匯出按鈕

**流程：**
1. 載入所有連線設定並顯示勾選清單
2. 使用者選擇連線、格式、密碼選項
3. 點擊匯出後開啟儲存檔案對話框
4. 寫入檔案，顯示成功訊息

#### 匯入對話框 (ImportConnectionsWindow)

**UI 元素：**
- 匯入預覽清單（顯示即將匯入的連線）
- 衝突標記（與現有連線重複的項目標黃色）
- 衝突處理按鈕：覆蓋 / 跳過 / 全部覆蓋 / 全部跳過

**流程：**
1. 開啟檔案對話框選擇 `.json` 或 `.tsjson` 檔案
2. 自動偵測格式；若為加密格式，彈出密碼輸入框
3. 解析後顯示預覽清單，標記衝突項目
4. 使用者處理衝突後確認匯入
5. 呼叫 `IConnectionManager.AddProfile()` 或 `UpdateProfile()` 寫入
6. 顯示結果摘要（成功 N 個、跳過 N 個、覆蓋 N 個）

## 檔案副檔名

| 格式 | 副檔名 | 說明 |
|------|--------|------|
| 純文字 JSON | `.json` | 可直接以文字編輯器開啟 |
| 加密 JSON | `.tsjson` | Specurai 專用加密格式 |

## 錯誤處理

- 加密密碼錯誤：顯示「密碼不正確，請重新輸入」
- 檔案格式不正確：顯示「檔案格式無法辨識」
- 版本不相容：顯示「此匯出檔案版本不支援」

## 測試策略

- **Domain**：`ConnectionExportData` 實體測試
- **Application/Infrastructure**：匯出/匯入 JSON、加密/解密、格式偵測、密碼排除邏輯
- **Desktop**：ViewModel 測試（匯出選項、衝突處理邏輯）
