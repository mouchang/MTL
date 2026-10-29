# 把 mihomo 内核 zip 下到 tools/core/mihomo.zip（构建前跑一次，不进仓库）
#
# 用法：
#   .\tools\get-core.ps1              # 从 MTL 网关取（KV 里有，国内也快）
#   .\tools\get-core.ps1 -FromGithub  # 从 GitHub 取（CI 用这个更快）

param(
    [switch]$FromGithub,
    [string]$Gateway = "https://mtl.dpdns.org/core/mihomo.zip",
    [string]$Github = "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/mihomo-windows-amd64-compatible-v1.19.33.zip"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "tools/core"
$target = Join-Path $dir "mihomo.zip"
$expected = "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d"

New-Item -ItemType Directory -Force -Path $dir | Out-Null

if (Test-Path $target) {
    $current = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
    if ($current -eq $expected) {
        Write-Host "core already present and verified: $target" -ForegroundColor Green
        exit 0
    }
    Remove-Item $target -Force
}

$urls = if ($FromGithub) { @($Github, $Gateway) } else { @($Gateway, $Github) }

foreach ($url in $urls) {
    try {
        Write-Host ("downloading: " + $url)
        & curl.exe -sS -L --max-time 900 -o $target $url
        if ($LASTEXITCODE -ne 0) { throw ("curl exit " + $LASTEXITCODE) }

        $actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
        if ($actual -eq $expected) {
            $size = (Get-Item $target).Length
            Write-Host ("done: " + $size + " bytes, sha256 ok") -ForegroundColor Green
            exit 0
        }

        Write-Host "sha256 mismatch, trying next source" -ForegroundColor Yellow
        Remove-Item $target -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Host ("failed: " + $_.Exception.Message) -ForegroundColor Yellow
        Remove-Item $target -Force -ErrorAction SilentlyContinue
    }
}

throw "all sources failed"
