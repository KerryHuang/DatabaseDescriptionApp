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
    $t = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest").tag_name
    if (-not $t) { throw "找不到最新版本（可能尚無 Release 或 GitHub API 限流）。請用 -Version 指定版本。" }
    return $t
}

function Install-Asset($tag, $asset, $exeName, $finalName, $subdir) {
    $url = "https://github.com/$repo/releases/download/$tag/$asset"
    $tmpZip = Join-Path $env:TEMP $asset
    $tmpDir = Join-Path $env:TEMP ("specurai-" + [System.Guid]::NewGuid().ToString('N'))
    $dest = Join-Path $root $subdir
    Write-Host "下載 $asset ($tag)..."
    $oldProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'   # 關閉進度條以加速大檔下載
    try {
        try { Invoke-WebRequest -Uri $url -OutFile $tmpZip }
        catch { throw "下載失敗（版本 $tag 可能無此資產 $asset）：$($_.Exception.Message)" }
    } finally { $ProgressPreference = $oldProgress }

    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force
    Remove-Item $tmpZip
    Move-Item (Join-Path $tmpDir $exeName) (Join-Path $tmpDir $finalName) -Force
    if (-not (Test-Path (Join-Path $tmpDir $finalName))) { throw "解壓後找不到執行檔 $finalName" }

    # 下載/解壓成功後才置換舊版本（避免升級失敗殘留破損狀態）
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Move-Item $tmpDir $dest -Force
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
