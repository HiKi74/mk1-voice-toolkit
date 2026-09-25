# 自动下载 vgmstream 官方 Windows 构建，解出 vgmstream-cli.exe 及依赖 DLL 到 bin\vgmstream\
# 用法: pwsh -File tools\获取vgmstream.ps1

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Dest = Join-Path $Root 'bin\vgmstream'

Write-Host '正在查询 vgmstream 最新版本…'
$headers = @{ 'User-Agent' = 'mk1-voice-toolkit' }
$rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/vgmstream/vgmstream/releases/latest' -Headers $headers

$asset = $rel.assets | Where-Object { $_.name -match '^vgmstream-win(64)?\.zip$' } | Select-Object -First 1
if (-not $asset) {
    $asset = $rel.assets | Where-Object { $_.name -match 'win.*\.zip$' } | Select-Object -First 1
}
if (-not $asset) {
    Write-Host ('没找到 Windows 构建，请手动下载: ' + $rel.html_url) -ForegroundColor Yellow
    exit 1
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('vgmstream_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$zip = Join-Path $tmp $asset.name

Write-Host ('下载 ' + $asset.name + ' (' + [math]::Round($asset.size / 1MB, 1) + ' MB) …')
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $headers
Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force

$cli = Get-ChildItem -LiteralPath $tmp -Recurse -File -Filter 'vgmstream-cli.exe' | Select-Object -First 1
if (-not $cli) {
    Write-Host '压缩包里没有 vgmstream-cli.exe' -ForegroundColor Red
    Remove-Item -LiteralPath $tmp -Recurse -Force
    exit 1
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
Copy-Item -LiteralPath $cli.FullName -Destination $Dest -Force
Get-ChildItem -LiteralPath $cli.DirectoryName -File -Filter '*.dll' | Copy-Item -Destination $Dest -Force
Remove-Item -LiteralPath $tmp -Recurse -Force

Write-Host ('完成！已安装到 ' + $Dest) -ForegroundColor Green
Write-Host '提示：vgmstream 有自己的许可协议，再分发时请遵循其条款。'
