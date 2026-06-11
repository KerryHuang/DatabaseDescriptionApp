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
