---
name: update-docs
description: Use when completing a feature, after all code is committed. Use when user says "update docs", "更新文件", "update readme". Updates README.md, UserGuide.md, CLAUDE.md and other project documentation to reflect new or changed features.
---

# 更新專案文件

完成功能後，更新所有相關的專案文件。

## 需要更新的文件

依序檢查並更新：

### 1. README.md

- **功能特色**：新增功能的說明段落（放在正確的分類下）
- **快捷鍵表**：新增或修改的快捷鍵
- **專案結構**：新增的檔案（Domain/Application/Infrastructure/Desktop 各層）
- **使用說明**：新增功能的使用步驟（編號接續現有章節）

### 2. docs/UserGuide.md

- **目錄**：新增章節連結
- **內容**：新增功能的詳細使用說明，包含操作步驟、截圖位置標記、注意事項

### 3. CLAUDE.md

- **Architecture**：更新 Key Patterns 中的 Repository 和 Service 清單
- **Database Objects Handled**：若新增處理的資料庫物件類型

### 4. docs/superpowers/specs/ 和 plans/

- 若功能有設計文件或實作計劃，確認內容與最終實作一致
- 若有重大變更（如從對話框改為分頁），更新設計文件

## 執行步驟

1. **讀取 git log**：檢視 feature branch 的所有 commit，了解變更內容
2. **讀取現有文件**：讀取上述所有文件的目前內容
3. **比對差異**：找出文件中缺少的新功能描述
4. **更新文件**：逐一更新，保持既有格式和風格
5. **檢查快捷鍵**：確認所有 KeyBinding 和 InputGesture 都列在快捷鍵表中
6. **Commit**：提交更新的文件

## 注意事項

- 保持既有文件的格式風格（標題層級、表格格式、編號方式）
- 繁體中文撰寫
- 不要刪除或修改與本次功能無關的內容
- 快捷鍵表要完整列出所有功能，不只是新增的
