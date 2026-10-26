<#
.SYNOPSIS
  把订阅内容推送到 MTL 订阅网关（绕开「订阅端拒绝 Cloudflare 出口 IP」）。

.DESCRIPTION
  Worker 直接回源订阅端会被 403（机场常见地屏蔽 Cloudflare 的 ASN），
  所以改成：本机用 clash UA 拉一次 YAML -> POST 到 /sub/publish -> 存进 KV。
  之后所有客户端都从 KV 取，订阅链接始终不出现在客户端。

.EXAMPLE
  .\publish-sub.ps1 -SubscriptionUrl "https://xxx/api/v1/pq/xxxx" -PublishKey "你的 PUBLISH_KEY"
#>
param(
    [Parameter(Mandatory = $true)][string]$SubscriptionUrl,
    [Parameter(Mandatory = $true)][string]$PublishKey,
    [string]$Gateway = "https://mtl.dpdns.org/sub",
    [string]$UserAgent = "clash-verge/v2.0.0"
)

$ErrorActionPreference = "Stop"
$temp = Join-Path $env:TEMP ("mtl-sub-" + [guid]::NewGuid().ToString("N") + ".yaml")

try {
    Write-Host "1/3 拉取订阅…" -ForegroundColor Cyan
    # 订阅端按 UA 返回不同格式，必须声明成 clash，否则拿到的是面板 HTML / base64 列表
    $pullArgs = @("-sS", "-L", "-A", $UserAgent, "-o", $temp, $SubscriptionUrl)
    & curl.exe @pullArgs
    if ($LASTEXITCODE -ne 0) { throw "拉取订阅失败（curl 退出码 $LASTEXITCODE）" }

    $size = (Get-Item $temp).Length
    Write-Host ("    收到 {0:N0} 字节" -f $size)

    $head = (Get-Content $temp -TotalCount 5 -Encoding UTF8) -join [Environment]::NewLine
    if ($head -notmatch 'mixed-port|proxies:|proxy-groups:') {
        Write-Host "    前几行：" -ForegroundColor DarkYellow
        Write-Host $head
        throw "返回内容不像 Clash YAML，检查订阅链接或 User-Agent"
    }

    Write-Host "2/3 上传到网关…" -ForegroundColor Cyan
    $postArgs = @(
        "-sS", "-X", "POST",
        "-H", "X-Publish-Key: $PublishKey",
        "-H", "Content-Type: text/yaml; charset=utf-8",
        "--data-binary", "@$temp",
        "$Gateway/publish"
    )
    $result = & curl.exe @postArgs
    if ($LASTEXITCODE -ne 0) { throw "上传失败（curl 退出码 $LASTEXITCODE）" }
    Write-Host "    网关返回：$result"

    if ($result -notmatch '"ok"\s*:\s*true') { throw "网关拒绝了本次上传：$result" }

    Write-Host "3/3 校验…" -ForegroundColor Cyan
    $check = & curl.exe -sS "$Gateway/config?did=selftest-device-0001"
    $first = ($check -split [char]10)[0]
    Write-Host "    /sub/config 首行：$first"

    if ($first -match 'mixed-port|proxies:|proxy-groups:') {
        Write-Host "完成：网关已经能下发订阅。" -ForegroundColor Green
    } else {
        Write-Host "警告：/sub/config 返回的不是 YAML，请到 /sub/status 查看状态。" -ForegroundColor Yellow
        Write-Host $check
    }
}
finally {
    if (Test-Path $temp) { Remove-Item $temp -Force }
}
