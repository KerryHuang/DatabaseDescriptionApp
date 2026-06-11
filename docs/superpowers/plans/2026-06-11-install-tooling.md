# Specurai 安裝工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 補上 `Specurai.Cli` 的發布（nuget.org dotnet tool + 各平台 self-contained 單一執行檔上 GitHub Release），並提供 `install.ps1` / `install.sh` 一鍵安裝腳本，達成「跟 mp-env 一樣」的安裝體驗。

**Architecture:** 沿用既有 `.github/workflows/release.yml`（`v*` tag 觸發）。CLI 的 NuGet 發布鏡像現有 MCP 的 `publish-nuget` job；CLI 的單檔建置鏡像現有 `build-mcp-server` job。安裝腳本只下載公開 Release 的 self-contained 執行檔（免 .NET SDK、免 token，因 repo 公開），並把 CLI 執行檔更名為 `specurai` 後放入使用者 bin 目錄與 PATH。

**Tech Stack:** GitHub Actions、.NET 8 (`dotnet pack`/`publish`)、PowerShell 5.1+、Bash。

**依據規格：** `docs/superpowers/specs/2026-06-11-install-tooling-design.md`

---

## 驗證策略（重要）

- CI/release 只在 `v*` tag 觸發，無法在一般 commit 驗證。策略：**所有 `dotnet` 指令先在本機跑通**（單檔發布、pack、tool install），YAML 只是把已驗證的指令鏡像進去。
- 安裝腳本的下載/解壓/PATH 邏輯先本機跑（可指向「已含 CLI 資產」的 Release；在 PR1/PR2 發布前，先用 `-Version`/`VERSION` 指向手動建立的測試 Release 或本機檔案驗證主要邏輯）。
- **端到端最終驗收** = 推一個新 `v*` tag 後依「驗收清單」（最後一個 Task）逐項確認。

---

## File Structure

- Modify: `.github/workflows/release.yml`
  - `publish-nuget` job：加一步 pack CLI（PR1）。
  - 新增 `build-cli` job（PR2）。
  - `create-release` job：`needs` 與資產複製、Release body 加 CLI（PR2 + PR4）。
- Create: `scripts/install.ps1`（PR3）
- Create: `scripts/install.sh`（PR3）
- Modify: `README.md`（PR4）

CLI 命令名稱策略：dotnet tool 由 `ToolCommandName=specurai` 提供 `specurai`；self-contained 單檔輸出為 `Specurai.Cli(.exe)`，安裝腳本負責更名為 `specurai(.exe)` 以統一命令名。

---

## Task 1 (PR1): CLI 發布到 nuget.org

**Files:** Modify `.github/workflows/release.yml`

- [ ] **Step 1: 本機驗證 `dotnet pack` 產出 tool 套件**

Run:
```bash
cd C:/Users/zihao/source/repos/DatabaseDescriptionApp
dotnet pack src/Specurai.Cli -c Release -p:Version=9.9.9-local -o nupkg-test
```
Expected: 產生 `nupkg-test/Specurai.Cli.9.9.9-local.nupkg`，無錯誤。

- [ ] **Step 2: 本機驗證該套件可安裝為全域工具並執行**

Run:
```bash
dotnet tool install --global --add-source ./nupkg-test Specurai.Cli --version 9.9.9-local
specurai --help
dotnet tool uninstall --global Specurai.Cli
```
Expected: `specurai --help` 印出命令清單（含 conn/tables/...）。事後解除安裝。清掉 `nupkg-test`。

> 若 `dotnet tool install` 報 RID/PackAsTool 相關錯誤，表示 csproj 需微調；先解決再進 Step 3。預期應可直接成功（csproj 已正確設定 PackAsTool）。

- [ ] **Step 3: 在 `publish-nuget` job 加 pack CLI 步驟**

編輯 `.github/workflows/release.yml`，於 `publish-nuget` job 內「建立 NuGet 套件」（pack McpServer）步驟之後、「發布至 NuGet」步驟之前，插入：

```yaml
      - name: 建立 CLI NuGet 套件
        run: >
          dotnet pack src/Specurai.Cli -c Release
          -p:Version=${{ steps.get-version.outputs.version }}
          -o nupkg
```

（既有「發布至 NuGet」步驟為 `dotnet nuget push nupkg/*.nupkg …`，會自動一併推送 CLI 與 MCP，不需修改。）

- [ ] **Step 4: 本機 YAML 語法檢查**

Run:
```bash
cd C:/Users/zihao/source/repos/DatabaseDescriptionApp
python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml',encoding='utf-8')); print('YAML OK')"
```
Expected: `YAML OK`（若無 python，可用任一 YAML linter；確認縮排正確、step 落在 `publish-nuget.steps` 下）。

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: release 加入 Specurai.Cli 的 NuGet 發布（dotnet tool）"
```

---

## Task 2 (PR2): CLI self-contained 單一執行檔上 Release

**Files:** Modify `.github/workflows/release.yml`

- [ ] **Step 1: 本機驗證 self-contained 單檔發布（解決 PackAsTool 衝突）**

Run（在 Windows 開發機；驗證 `-p:PackAsTool=false` 能產出可執行單檔）：
```bash
cd C:/Users/zihao/source/repos/DatabaseDescriptionApp
dotnet publish src/Specurai.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PackAsTool=false -p:Version=9.9.9-local -o cli-publish-test
./cli-publish-test/Specurai.Cli.exe --help
```
Expected: 編譯成功且 `Specurai.Cli.exe --help` 可執行。記下 `cli-publish-test/` 內除主 exe 外是否還有必要的 native 檔（影響安裝腳本是否需整包搬移）。事後刪 `cli-publish-test`。

- [ ] **Step 2: 新增 `build-cli` job**

在 `.github/workflows/release.yml` 中，於 `build-mcp-server` job 之後新增（鏡像其結構）：

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
      - name: 檢出程式碼
        uses: actions/checkout@v4
      - name: 設定 .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      - name: 取得版本號
        id: get-version
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
        run: |
          mkdir -p Releases
          (cd cli-publish && zip -r ../Releases/${{ matrix.artifact }} .)
      - name: 打包（macOS / Linux）
        if: "!contains(matrix.runtime, 'win')"
        run: |
          mkdir -p Releases
          (cd cli-publish && chmod +x Specurai.Cli && tar czf ../Releases/${{ matrix.artifact }} .)
      - name: 上傳安裝包
        uses: actions/upload-artifact@v4
        with:
          name: cli-${{ matrix.runtime }}
          path: Releases/*
          retention-days: 30
```

- [ ] **Step 3: 把 `build-cli` 接到 `create-release`**

在 `create-release` job：
1. `needs` 加入 `build-cli`：
```yaml
    needs: [build-windows, build-macos, build-linux, build-mcp-server, build-cli]
```
2. 「整理發布檔案」步驟中，在 MCP 那幾行 `cp` 之後加入：
```yaml
          cp artifacts/cli-win-x64/*   release-files/ 2>/dev/null || true
          cp artifacts/cli-osx-arm64/* release-files/ 2>/dev/null || true
          cp artifacts/cli-osx-x64/*   release-files/ 2>/dev/null || true
          cp artifacts/cli-linux-x64/* release-files/ 2>/dev/null || true
```

- [ ] **Step 4: YAML 語法檢查**

Run:
```bash
python -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml',encoding='utf-8')); print('YAML OK')"
```
Expected: `YAML OK`。

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: 新增 build-cli job 產各平台 CLI 單一執行檔並掛上 Release"
```

---

## Task 3 (PR3): 一鍵安裝腳本

**Files:** Create `scripts/install.ps1`、`scripts/install.sh`

> 命令名統一為 `specurai`：腳本把單檔執行檔安裝到專屬目錄並更名（Windows 更名為 `specurai.exe`；macOS/Linux 以 `~/.local/bin/specurai` 連結/更名）。MCP（`-WithMcp`/`--with-mcp`）更名為 `specurai-mcp`。

- [ ] **Step 1: 建立 `scripts/install.ps1`**

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version,   # 例 "1.9.0" 或 "v1.9.0"；留空＝最新
    [switch]$WithMcp
)
$ErrorActionPreference = 'Stop'
$repo = 'KerryHuang/DatabaseDescriptionApp'
$root = Join-Path $env:LOCALAPPDATA 'Programs\Specurai'

function Resolve-Tag {
    if ($Version) { if ($Version.StartsWith('v')) { return $Version } else { return "v$Version" } }
    return (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest").tag_name
}

function Install-Asset($tag, $asset, $exeName, $finalName, $subdir) {
    $url = "https://github.com/$repo/releases/download/$tag/$asset"
    $tmpZip = Join-Path $env:TEMP $asset
    $dest = Join-Path $root $subdir
    Write-Host "下載 $asset ($tag)..."
    Invoke-WebRequest -Uri $url -OutFile $tmpZip
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Expand-Archive -Path $tmpZip -DestinationPath $dest -Force
    Remove-Item $tmpZip
    Move-Item (Join-Path $dest $exeName) (Join-Path $dest $finalName) -Force
    Write-Host "已安裝 $finalName 至 $dest"
    return $dest
}

$tag = Resolve-Tag
$cliDir = Install-Asset $tag 'Specurai.Cli-win-x64.zip' 'Specurai.Cli.exe' 'specurai.exe' 'cli'
if ($WithMcp) {
    Install-Asset $tag 'Specurai.McpServer-win-x64.zip' 'Specurai.McpServer.exe' 'specurai-mcp.exe' 'mcp' | Out-Null
}

# 加入 User PATH（指向 cli 子目錄）
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$cliDir*") {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$cliDir", 'User')
    Write-Host "已將 $cliDir 加入 User PATH。"
}
Write-Host ""
Write-Host "完成！請『重開終端機』後執行： specurai --help"
```

- [ ] **Step 2: 建立 `scripts/install.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail

repo="KerryHuang/DatabaseDescriptionApp"
version="${VERSION:-}"
with_mcp=0
[ "${1:-}" = "--with-mcp" ] && with_mcp=1

bindir="$HOME/.local/bin"
sharedir="$HOME/.local/share/specurai"

os="$(uname -s)"; arch="$(uname -m)"
case "$os-$arch" in
  Darwin-arm64)  rid="osx-arm64" ;;
  Darwin-x86_64) rid="osx-x64" ;;
  Linux-x86_64)  rid="linux-x64" ;;
  *) echo "不支援的平台：$os-$arch" >&2; exit 1 ;;
esac

tag="$version"
if [ -z "$tag" ]; then
  tag="$(curl -fsSL "https://api.github.com/repos/$repo/releases/latest" \
        | grep '"tag_name"' | head -1 | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')"
fi
case "$tag" in v*) : ;; *) tag="v$tag" ;; esac

install_asset() {
  asset="$1"; exe="$2"; final="$3"; sub="$4"
  url="https://github.com/$repo/releases/download/$tag/$asset"
  dest="$sharedir/$sub"
  tmp="$(mktemp -d)"
  echo "下載 $asset ($tag)..."
  curl -fsSL "$url" -o "$tmp/$asset"
  rm -rf "$dest"; mkdir -p "$dest"
  tar xzf "$tmp/$asset" -C "$dest"
  rm -rf "$tmp"
  chmod +x "$dest/$exe"
  xattr -dr com.apple.quarantine "$dest/$exe" 2>/dev/null || true
  mkdir -p "$bindir"
  ln -sf "$dest/$exe" "$bindir/$final"
  echo "已安裝 $final 至 $bindir（→ $dest/$exe）"
}

install_asset "Specurai.Cli-$rid.tar.gz" "Specurai.Cli" "specurai" "cli"
[ "$with_mcp" = 1 ] && install_asset "Specurai.McpServer-$rid.tar.gz" "Specurai.McpServer" "specurai-mcp" "mcp"

case ":$PATH:" in
  *":$bindir:"*) : ;;
  *) echo ""; echo "提醒：請把 $bindir 加入 PATH（在 ~/.zshrc 或 ~/.bashrc 加一行）：";
     echo '  export PATH="$HOME/.local/bin:$PATH"' ;;
esac
echo ""
echo "完成！執行： specurai --help"
```

- [ ] **Step 3: 確保檔案為 UTF-8 無 BOM、LF 行尾**

依專案 `.claude/rules/cross-platform-scripts.md`，確認兩個檔案為 UTF-8（無 BOM）、LF。可於 `.gitattributes` 確認 `*.sh`/`*.ps1` 不被轉 CRLF（必要時加 `*.sh text eol=lf`、`*.ps1 text eol=lf`）。

Run（檢查無 BOM / 無 CRLF）：
```bash
file scripts/install.sh scripts/install.ps1
git add scripts/install.ps1 scripts/install.sh
git diff --cached --stat
```
Expected: 兩檔存在；`file` 不顯示 "with BOM" / "CRLF"。

- [ ] **Step 4: 本機驗證腳本邏輯（語法 + 平台偵測；下載部分待有 CLI 資產的 Release）**

Run（bash 語法檢查）：
```bash
bash -n scripts/install.sh && echo "install.sh 語法 OK"
```
Run（PowerShell 語法檢查——若本機 PowerShell 可用，於 PowerShell 視窗執行；本工具環境 PowerShell 受限，改由你在終端機手動跑）：
```powershell
powershell -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('scripts\install.ps1',[ref]$null,[ref]$null); 'install.ps1 語法 OK'"
```
Expected: 兩者語法 OK。

> **完整下載驗證**需有「含 `Specurai.Cli-<rid>` 資產」的 Release（即 PR1/PR2 發布後）。屆時跑 `curl -fsSL .../install.sh | bash` 與 `irm .../install.ps1 | iex` 實測；並特別確認 self-contained 單檔搬移後仍可獨立執行（Step 1 of Task 2 記下的 native 檔若存在，確認連結/PATH 指向的目錄包含它們——本腳本將整包解到專屬目錄並以 symlink/PATH 指向，已涵蓋此情況）。

- [ ] **Step 5: Commit**

```bash
git add scripts/install.ps1 scripts/install.sh .gitattributes
git commit -m "feat: 新增 install.ps1 / install.sh 一鍵安裝腳本"
```

---

## Task 4 (PR4): 文件（README + Release notes）

**Files:** Modify `README.md`、`.github/workflows/release.yml`（Release body）

- [ ] **Step 1: README 新增「安裝 CLI」章節**

在 `README.md` 適當位置（安裝/快速開始附近）加入：

````markdown
## 安裝 CLI（specurai）

### 方式一：一鍵安裝（免 .NET SDK）

Windows（PowerShell）：
```powershell
irm https://raw.githubusercontent.com/KerryHuang/DatabaseDescriptionApp/master/scripts/install.ps1 | iex
```

macOS / Linux：
```bash
curl -fsSL https://raw.githubusercontent.com/KerryHuang/DatabaseDescriptionApp/master/scripts/install.sh | bash
```

安裝後（Windows 需重開終端機）：
```bash
specurai --help
```

更新＝重跑上述指令。需要連 MCP 一起裝：Windows 加 `-WithMcp`、其他平台加 `--with-mcp`。

### 方式二：dotnet tool（需 .NET 8 SDK）

```bash
dotnet tool install -g Specurai.Cli      # 指令：specurai
dotnet tool update  -g Specurai.Cli      # 更新
```
````

- [ ] **Step 2: Release notes 模板加 CLI 段落**

在 `.github/workflows/release.yml` 的 `create-release` → `softprops/action-gh-release` 的 `body:` 內，於現有「MCP Server 安裝」段落之後加入 CLI 安裝段落（一鍵 + dotnet tool 兩法），文案沿用 Step 1 內容的精簡版。

- [ ] **Step 3: Commit**

```bash
git add README.md .github/workflows/release.yml
git commit -m "docs: README 與 Release notes 補 CLI 安裝說明"
```

---

## Task 5: 端到端發布驗收（推 tag 後）

> 此 Task 需實際發版，由你決定何時做。

- [ ] **Step 1: 推一個新版本 tag**

Run（版號依語意遞增，例如目前 v1.9.0 → v1.10.0）：
```bash
git push origin master
git tag v1.10.0
git push origin v1.10.0
```

- [ ] **Step 2: 觀察 Actions**

到 GitHub Actions 確認 `Release` workflow 綠燈，且 `build-cli`（4 runtime）、`publish-nuget` 皆成功。

- [ ] **Step 3: 驗收清單**

- [ ] nuget.org 出現 `Specurai.Cli`，`dotnet tool install -g Specurai.Cli` → `specurai --help` 成功。
- [ ] GitHub Release 含 4 個 `Specurai.Cli-<rid>` 資產。
- [ ] Windows：`irm …/install.ps1 | iex` → 重開終端機 → `specurai --help` 成功。
- [ ] macOS：`curl …/install.sh | bash` → `specurai --help` 成功（Apple Silicon 用 osx-arm64、Intel 用 osx-x64）。
- [ ] README 與 Release notes 含 CLI 安裝說明。

---

## Self-Review 紀錄

- **Spec 覆蓋**：Phase 1a → Task 1；Phase 1b → Task 2；Phase 2 → Task 3；Phase 3 → Task 4；驗收標準 → Task 5。✅
- **Placeholder 掃描**：無 TBD；YAML 與兩個安裝腳本均為完整內容。✅
- **一致性**：命令名 `specurai`（tool 由 ToolCommandName 提供；單檔由安裝腳本更名/連結），MCP 為 `specurai-mcp`，與既有 Release notes 一致；資產命名 `Specurai.Cli-<rid>.(zip|tar.gz)` 在 build-cli、create-release、install 腳本三處一致。✅
- **已知風險**：`PackAsTool` × `PublishSingleFile` 以 `-p:PackAsTool=false` 規避，Task 1 Step 1 與 Task 2 Step 1 各有本機驗證先擋；self-contained 單檔若帶 native side files，安裝腳本採「整包解到專屬目錄 + symlink/PATH」涵蓋。
- **獨立可上線**：PR1（Task 1）即可單獨合併讓 `dotnet tool install` 生效；PR2/PR3/PR4 可分批。
