# Specurai 安裝工具設計（CLI 發布 + 一鍵安裝腳本）

- 日期：2026-06-11
- 狀態：計劃（**尚未實作**），待 review
- 目標：讓 `specurai` CLI 有「跟 mp-env（`mpe`）一樣」的安裝體驗——registry 安裝 + 一行安裝腳本——並補上目前發布流程漏掉 CLI 的缺口。

## 背景與現況盤點

打 `v*` tag 觸發 `.github/workflows/release.yml`，目前已自動發布（已發到 v1.9.0，37 個 tag）：

| 元件 | 現有安裝方式 | 狀態 |
|---|---|---|
| 桌面 App | Velopack（Win/Linux）+ `.dmg`（macOS arm64/x64） | ✅ 完整 |
| MCP Server | ① 各平台 self-contained 單一執行檔（Release 下載）② `dotnet tool install -g Specurai.McpServer`（指令 `specurai-mcp`，nuget.org） | ✅ 完整 |
| 版號 | `src/Directory.Build.props` 由 git tag 自動帶入；CI 以 `-p:Version` 明確覆寫 | ✅ |

`src/Specurai.Cli/Specurai.Cli.csproj` **已設定為 dotnet tool**（`PackAsTool=true`、`ToolCommandName=specurai`、`PackageId=Specurai.Cli`），但：

### 缺口
1. **CLI 從未被 CI 發布**：`release.yml` 完全沒有 `Specurai.Cli`（出現 0 次）。`publish-nuget` job 只 pack `Specurai.McpServer`。→ `dotnet tool install -g Specurai.Cli` 目前裝不起來。
2. **沒有一鍵安裝腳本**：使用者需自行知道 `dotnet tool install` 指令或手動下載 Release 資產，缺 mpe 那種 `irm …/install.ps1 | iex` 體驗。

### 有利條件
- repo 為 **公開**，`releases/latest` 資產可**匿名下載** → 一鍵 installer 不需任何 token（比 mp-env 的私有 GitLab + token 更順）。

## 決策（已確認）

| 決策 | 選擇 |
|---|---|
| CLI 安裝路線 | **C：兩者都做**——dotnet tool（nuget.org）＋ self-contained 執行檔 + 一鍵 installer |
| Registry | **公開 nuget.org**（與現有 MCP 一致） |
| Installer 認證 | 免 token（repo 公開） |
| 平台 | Windows x64、macOS arm64/x64、Linux x64（比照 MCP build matrix） |

## 範圍

**In scope**
- 把 `Specurai.Cli` 納入 `release.yml`：① pack + push nuget.org ② 各平台 self-contained 單一執行檔上 Release。
- 新增 `scripts/install.ps1`（Windows）與 `scripts/install.sh`（macOS/Linux）一鍵安裝腳本。
- README 與 Release notes 補 CLI 安裝章節。

**Out of scope（YAGNI）**
- 不改桌面 App / MCP 現有發布流程（已完整）。
- 不導入 semantic-release（現行 git-tag 觸發已足夠；版號自動化非本案目標）。
- 不做私有 registry、不做自動更新背景服務（提供 `dotnet tool update` 與「重跑 installer」即可）。

---

## Phase 1：把 CLI 納入發布流程

### 1a. CLI 發到 nuget.org（dotnet tool 路線）

`release.yml` 的 `publish-nuget` job，在既有「建立 NuGet 套件」步驟之後、push 之前，加一步 pack CLI（既有 push 已是 `nupkg/*.nupkg`，會一併涵蓋兩個套件，push 步驟不需改）：

```yaml
      - name: 建立 CLI NuGet 套件
        run: >
          dotnet pack src/Specurai.Cli -c Release
          -p:Version=${{ steps.get-version.outputs.version }}
          -o nupkg
```

驗收：tag 後 nuget.org 出現 `Specurai.Cli`，`dotnet tool install -g Specurai.Cli` 可裝、`specurai --help` 可跑。

### 1b. CLI self-contained 單一執行檔上 Release（installer 路線的基礎）

新增 `build-cli` job，鏡像既有 `build-mcp-server`：

```yaml
  build-cli:
    name: 建置 CLI 單一執行檔
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        include:
          - { runtime: win-x64,   artifact: Specurai.Cli-win-x64.zip }
          - { runtime: osx-arm64, artifact: Specurai.Cli-osx-arm64.tar.gz }
          - { runtime: osx-x64,   artifact: Specurai.Cli-osx-x64.tar.gz }
          - { runtime: linux-x64, artifact: Specurai.Cli-linux-x64.tar.gz }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - id: get-version
        shell: bash
        run: echo "version=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT
      - name: 發布 CLI
        run: >
          dotnet publish src/Specurai.Cli -c Release
          -r ${{ matrix.runtime }} --self-contained
          -p:PublishSingleFile=true
          -p:PackAsTool=false
          -p:Version=${{ steps.get-version.outputs.version }}
          -o cli-publish
      - name: 打包（Windows）
        if: contains(matrix.runtime, 'win')
        shell: bash
        run: mkdir -p Releases && (cd cli-publish && zip -r ../Releases/${{ matrix.artifact }} .)
      - name: 打包（macOS / Linux）
        if: "!contains(matrix.runtime, 'win')"
        run: |
          mkdir -p Releases
          (cd cli-publish && chmod +x Specurai.Cli && tar czf ../Releases/${{ matrix.artifact }} .)
      - uses: actions/upload-artifact@v4
        with: { name: cli-${{ matrix.runtime }}, path: Releases/*, retention-days: 30 }
```

> **已知 gotcha**：`Specurai.Cli.csproj` 全域有 `PackAsTool=true`，與 `dotnet publish -r … --self-contained --PublishSingleFile` 可能衝突；上面以 `-p:PackAsTool=false` 在 publish 時關閉，需於實作時驗證。

`create-release` job：`needs` 加入 `build-cli`，並在「整理發布檔案」複製 CLI 資產：

```yaml
    needs: [build-windows, build-macos, build-linux, build-mcp-server, build-cli]
    # ...
          cp artifacts/cli-win-x64/*   release-files/ 2>/dev/null || true
          cp artifacts/cli-osx-arm64/* release-files/ 2>/dev/null || true
          cp artifacts/cli-osx-x64/*   release-files/ 2>/dev/null || true
          cp artifacts/cli-linux-x64/* release-files/ 2>/dev/null || true
```

並在 Release body（`softprops/action-gh-release` 的 `body:`）補一段 CLI 安裝說明（見 Phase 3）。

---

## Phase 2：一鍵安裝腳本（「安裝工具」本體）

### scripts/install.ps1（Windows）

行為：
1. `$ErrorActionPreference = 'Stop'`；接受可選參數 `-Version`（預設抓 `releases/latest`）、`-WithMcp`（同時裝 MCP）。
2. 解析版本：呼叫 `https://api.github.com/repos/KerryHuang/DatabaseDescriptionApp/releases/latest` 取 `tag_name`。
3. 下載資產 `Specurai.Cli-win-x64.zip`（`https://github.com/.../releases/download/<tag>/...`）到暫存。
4. 解壓到安裝目錄：`$env:LOCALAPPDATA\Programs\Specurai\`（覆蓋舊版）。
5. 若該目錄不在 User PATH，則加入（`[Environment]::SetEnvironmentVariable('Path', "$old;$dir", 'User')`）。
6. `-WithMcp` 時：另下載 `Specurai.McpServer-win-x64.zip` 解壓，並提示/執行 `claude mcp add specurai -s user -- <path>` 或寫入 `.mcp.json`。
7. 輸出成功訊息：請重開終端機後執行 `specurai --help`。

一行安裝（公開 repo，免 token）：
```powershell
irm https://raw.githubusercontent.com/KerryHuang/DatabaseDescriptionApp/master/scripts/install.ps1 | iex
```
帶參數版：
```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/KerryHuang/DatabaseDescriptionApp/master/scripts/install.ps1))) -WithMcp
```

### scripts/install.sh（macOS / Linux）

行為：
1. `set -euo pipefail`；可選 env `VERSION`、旗標 `--with-mcp`。
2. 偵測平台：`uname -s`（Darwin/Linux）+ `uname -m`（arm64/x86_64）→ `osx-arm64` / `osx-x64` / `linux-x64`。
3. `curl -fsSL` 取 `releases/latest` 的 `tag_name`（用 `grep`/`sed` 解析，或要求有 `jq`）。
4. 下載對應 `Specurai.Cli-<rid>.tar.gz`，解壓到 `~/.local/bin/`，`chmod +x`。
5. 若 `~/.local/bin` 不在 PATH，提示加入 shell rc（`echo 'export PATH="$HOME/.local/bin:$PATH"'`）。
6. macOS quarantine 提示：`xattr -dr com.apple.quarantine ~/.local/bin/Specurai.Cli`（首次）。
7. 成功訊息 + `specurai --help`。

一行安裝：
```bash
curl -fsSL https://raw.githubusercontent.com/KerryHuang/DatabaseDescriptionApp/master/scripts/install.sh | bash
```

> **設計原則**：installer 只下載 self-contained 執行檔（免裝 .NET SDK），與 `dotnet tool` 路線並存、互不依賴。重跑 installer 即「更新」。

---

## Phase 3：文件

- **README.md** 新增「安裝 CLI」章節，並列兩條路線：
  - 路線 A（有 .NET 8 SDK）：`dotnet tool install -g Specurai.Cli` → `specurai --help`；更新 `dotnet tool update -g Specurai.Cli`。
  - 路線 B（免 SDK，一鍵）：上面的 `irm | iex` / `curl | bash`；更新＝重跑。
- **Release notes 模板**（`release.yml` 的 `body:`）：在現有 MCP 段落後補 CLI 安裝段落，與 MCP 一致風格。
- 文件中標註平台支援與 macOS quarantine 注意事項（沿用現有 MCP 寫法）。

---

## 風險與注意事項

1. **`PackAsTool` × `PublishSingleFile` 衝突**：Phase 1b 以 `-p:PackAsTool=false` 規避，實作時務必先本機驗證 `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:PackAsTool=false src/Specurai.Cli` 能產出可執行單檔。
2. **CLI self-contained 體積**：System.CommandLine + Spectre.Console + SqlClient，單檔可能數十 MB（與 MCP 相當，可接受）。可選 `-p:PublishTrimmed=true` 縮小，但 SqlClient/反射有 trim 風險——**預設不開 trim**，列為日後優化。
3. **install.ps1 PATH 生效**：`SetEnvironmentVariable('Path', …, 'User')` 需新開終端機才生效，腳本須明確提示。
4. **nuget.org 套件名稱**：`Specurai.Cli` 須在 nuget.org 可用（未被占用）；MCP 既已用 `Specurai.McpServer`，同帳號發布應無礙。
5. **`NUGET_API_KEY` secret**：CLI push 沿用既有 `publish-nuget` 的 `HAS_NUGET_KEY` 閘門，未設則只 upload artifact 不 push（與 MCP 行為一致）。

## 驗收標準

- 打一個新 `v*` tag 後：
  - nuget.org 出現 `Specurai.Cli`，`dotnet tool install -g Specurai.Cli` → `specurai --help` 成功。
  - GitHub Release 含 4 個 `Specurai.Cli-<rid>` 資產。
  - `irm …/install.ps1 | iex`（Windows）與 `curl …/install.sh | bash`（mac/Linux）能裝好 `specurai` 並可執行。
- README 與 Release notes 含 CLI 安裝說明。

## 實作切分建議（待核准後再寫成 TDD plan）

- PR1：Phase 1a（CLI 上 nuget）——最小、立即可用。
- PR2：Phase 1b（CLI self-contained 上 Release）。
- PR3：Phase 2（install.ps1 / install.sh）。
- PR4：Phase 3（文件）。
