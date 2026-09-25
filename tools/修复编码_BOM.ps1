# 给 CSV / TXT / MD / TSV 补上 UTF-8 BOM，避免中文 Excel、WPS 按 GBK 解码出现乱码。
# 只处理"合法的 UTF-8 但缺少 BOM"的文件，其它文件一律跳过（不动内容、不改编码）。
#
# 用法:
#   pwsh -File 修复编码_BOM.ps1 -Path 'D:\Codex\祖国人语音包_MK1'
#   pwsh -File 修复编码_BOM.ps1 -Path 'D:\A','D:\B' -Include '*.csv'

param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [string[]]$Include = @('*.csv', '*.txt', '*.md', '*.tsv')
)

$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$bom = [byte[]](0xEF, 0xBB, 0xBF)
$fixed = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]
$notUtf8 = New-Object System.Collections.Generic.List[string]

$exts = $Include | ForEach-Object { $_.TrimStart('*') }   # '*.csv' -> '.csv'

foreach ($root in $Path) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $exts -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $skipped.Add($_.FullName) | Out-Null
            return
        }
        try { $null = $utf8Strict.GetString($bytes) }
        catch { $notUtf8.Add($_.FullName) | Out-Null; return }

        $new = New-Object byte[] ($bytes.Length + 3)
        [Array]::Copy($bom, 0, $new, 0, 3)
        [Array]::Copy($bytes, 0, $new, 3, $bytes.Length)
        [System.IO.File]::WriteAllBytes($_.FullName, $new)
        $fixed.Add($_.FullName) | Out-Null
    }
}

Write-Host ("已补 BOM: {0} 个" -f $fixed.Count)
$fixed | ForEach-Object { Write-Host ("  + " + $_) }
if ($skipped.Count -gt 0) { Write-Host ("已有 BOM，跳过: {0} 个" -f $skipped.Count) }
if ($notUtf8.Count -gt 0) {
    Write-Host ("非 UTF-8，未处理: {0} 个" -f $notUtf8.Count)
    $notUtf8 | ForEach-Object { Write-Host ("  ? " + $_) }
}
