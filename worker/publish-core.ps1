<#
.SYNOPSIS
  把 mihomo 内核 zip 推到 MTL 代理网关（存 KV），国内下载不再依赖 GitHub。

.DESCRIPTION
  网关会返回 { ok:true, bytes, sha256 }；启动器下载后会自己再校验一次 SHA-256。
  不传 -Path 时会先从 GitHub 下载。

.EXAMPLE
  .\publish-core.ps1 -PublishKey "你的 PUBLISH_KEY"

.EXAMPLE
  .\publish-core.ps1 -Path "D:\mihomo.zip" -PublishKey "你的 PUBLISH_KEY"
#>
param(
    [Parameter(Mandatory = $true)][string]$PublishKey,
    [string]$Path,
    [string]$Gateway = "https://mtl.dpdns.org/core",
    [string]$CoreUrl = "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/mihomo-windows-amd64-compatible-v1.19.33.zip",
    [string]$Version = "v1.19.33"
)

$ErrorActionPreference = "Stop"
$temp = $null

try {
    if (-not $Path) {
        $temp = Join-Path $env:TEMP ("mtl-core-" + [guid]::NewGuid().ToString("N") + ".zip")
        Write-Host "1/3 下载内核…" -ForegroundColor Cyan
        & curl.exe -sS -L -o $temp $CoreUrl
        if ($LASTEXITCODE -ne 0) { throw "下载失败（curl 退出码 $LASTEXITCODE）" }
        $Path = $temp
    }

    $size = (Get-Item $Path).Length
    Write-Host ("    文件 {0:N0} 字节" -f $size) -ForegroundColor DarkGray

    if ($size -lt 1048576) { throw "文件太小，不像 mihomo 的 zip" }
    if ($size -gt 24MB) { throw "超过 KV 的 24 MB 上限" }

    Write-Host "2/3 计算 SHA-256…" -ForegroundColor Cyan
    $sha = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLower()

    Write-Host "3/3 上传到网关…" -ForegroundColor Cyan
    $result = & curl.exe -sS -X POST -H "X-Publish-Key: $PublishKey" -H "X-Core-Sha256: $sha" -H "X-Core-Version: $Version" -H "Content-Type: application/zip" --data-binary "@$Path" "$Gateway/publish"

    if ($LASTEXITCODE -ne 0) { throw "上传失败（curl 退出码 $LASTEXITCODE）" }
    Write-Host "    网关返回：$result"

    if ($result -notmatch '"ok"\s*:\s*true') { throw "网关拒绝了本次上传：$result" }
    if ($sha) { Write-Host "    本地 SHA-256：$sha" -ForegroundColor DarkGray }

    Write-Host "完成：内核已存进网关 KV。" -ForegroundColor Green

    $check = & curl.exe -sS "$Gateway/status"
    Write-Host "    /core/status：$check" -ForegroundColor DarkGray
}
finally {
    if ($temp -and (Test-Path $temp)) { Remove-Item $temp -Force }
}
