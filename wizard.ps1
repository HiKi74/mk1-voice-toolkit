# =====================================================================
#  《真人快打1》语音导出向导
#  双击「启动向导.bat」运行；也可以用参数静默执行：
#    pwsh -File wizard.ps1 -Paks "D:\Steam\...\Paks" -Character Homelander -Yes
# =====================================================================

param(
    [string]$Paks,
    [string]$Key,
    [string]$Character,
    [string]$Language = "English(US)",
    [string]$OutDir,
    [string]$OodleDll,
    [switch]$NoSrt,
    [switch]$NoMerge,
    [switch]$WithDescriptive,
    [switch]$Zip,
    [switch]$KeepTemp,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$Root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinExe  = Join-Path $Root 'bin\mkextract\mkextract.exe'
$BinDll  = Join-Path $Root 'bin\mkextract\mkextract.dll'
$Vgm     = Join-Path $Root 'bin\vgmstream\vgmstream-cli.exe'
$PyTool  = Join-Path $Root 'tools\整理语音.py'
$Cache   = Join-Path $Root 'cache'
$CfgFile = Join-Path $Root '配置.json'

$DefaultKey = '0x6FAABA4F4EF8A6AC188A517ACEF38F1422484E3B1F3F4CF3DACB27A6CBCCD076'  # MK1 的 AES 密钥（公开流传）
$KnownChars = 'OmniMan, Homelander, Peacemaker, QuanChi, Ermac, Tanya, Takeda, Conan, Ghostface, Sektor, Cyrax, NoobSaibot, T1000, LiMei, ShangTsung, SubZero'

function Say([string]$msg, [string]$color = 'Gray') { Write-Host $msg -ForegroundColor $color }
function Head([string]$msg) {
    Write-Host ''
    Write-Host ('=' * 64) -ForegroundColor DarkCyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ('=' * 64) -ForegroundColor DarkCyan
}
function Ask([string]$prompt, [string]$default) {
    if ($Yes) { return $default }
    if ($default) { $v = Read-Host "$prompt [$default]" } else { $v = Read-Host $prompt }
    if ([string]::IsNullOrWhiteSpace($v)) { return $default }
    return $v.Trim().Trim('"')
}
function AskBool([string]$prompt, [bool]$default) {
    if ($Yes) { return $default }
    $d = if ($default) { 'Y' } else { 'n' }
    $v = Read-Host "$prompt (Y/n) [$d]"
    if ([string]::IsNullOrWhiteSpace($v)) { return $default }
    return ($v.Trim().ToLower() -in @('y', 'yes', '是', '1'))
}
function LoadConfig {
    if (Test-Path -LiteralPath $CfgFile) {
        try { return Get-Content -LiteralPath $CfgFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
    }
    return $null
}
function SaveConfig($cfg) {
    $json = $cfg | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($CfgFile, $json, (New-Object System.Text.UTF8Encoding($true)))
}

# ---------------------------------------------------------------- 0. 环境自检
Head '第 0 步 / 环境自检'
if (Test-Path -LiteralPath $BinExe) { $Mk = $BinExe }
elseif (Test-Path -LiteralPath $BinDll) { $Mk = $BinDll }
else { Say "缺少 bin\mkextract\mkextract.exe，无法继续（请重新解压完整包）" 'Red'; exit 1 }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Say '未检测到 .NET 运行时，需要先安装 .NET 9 Runtime: https://dotnet.microsoft.com/download/dotnet/9.0' 'Red'; exit 1 }
Say "  ✓ 解包工具 mkextract" 'Green'

if (Test-Path -LiteralPath $Vgm) { Say '  ✓ vgmstream-cli（wem → wav）' 'Green' }
else { Say '  ! 没找到 bin\vgmstream\vgmstream-cli.exe，导出将只保留 .wem 不转 wav' 'Yellow' }

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
if ($python) { Say "  ✓ Python（整理/字幕/合集）" 'Green' }
else { Say '  ! 没找到 Python，将跳过"命名整理+字幕"步骤（需要 Python 3.8+）' 'Yellow' }

$cfg = LoadConfig

# ---------------------------------------------------------------- 1. 游戏目录
Head '第 1 步 / 找到《真人快打1》的 Paks 目录'
function Find-Paks {
    $cands = New-Object System.Collections.Generic.List[string]
    try {
        $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
        if ($steam) {
            $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $vdf) {
                $txt = Get-Content -LiteralPath $vdf -Raw
                foreach ($m in [regex]::Matches($txt, '"path"\s+"([^"]+)"')) {
                    $cands.Add((Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Mortal Kombat 1\MK12\Content\Paks'))
                }
            }
            $cands.Add((Join-Path $steam 'steamapps\common\Mortal Kombat 1\MK12\Content\Paks'))
        }
    } catch {}
    foreach ($d in @('C:', 'D:', 'E:', 'F:')) {
        $cands.Add("$d\SteamLibrary\steamapps\common\Mortal Kombat 1\MK12\Content\Paks")
        $cands.Add("$d\Steam\steamapps\common\Mortal Kombat 1\MK12\Content\Paks")
        $cands.Add("$d\Program Files (x86)\Steam\steamapps\common\Mortal Kombat 1\MK12\Content\Paks")
    }
    foreach ($c in $cands) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}
if (-not $Paks) {
    $auto = Find-Paks
    if ($auto) {
        Say "  已自动找到: $auto" 'Green'
        if (AskBool '  使用这个目录吗？' $true) { $Paks = $auto }
    }
}
if (-not $Paks) { $Paks = Ask '  请粘贴 Paks 目录路径（形如 ...\Mortal Kombat 1\MK12\Content\Paks）' $null }
if (-not $Paks -or -not (Test-Path -LiteralPath $Paks)) { Say "目录不存在: $Paks" 'Red'; exit 1 }
$paksCount = (Get-ChildItem -LiteralPath $Paks -File -Include *.pak, *.utoc, *.ucas -ErrorAction SilentlyContinue).Count
Say "  ✓ 目录有效，含 $paksCount 个 pak/utoc/ucas 文件" 'Green'

# ---------------------------------------------------------------- 2. 密钥
Head '第 2 步 / AES 密钥'
if (-not $Key) {
    $lastKey = if ($cfg -and $cfg.key) { $cfg.key } else { $DefaultKey }
    $Key = Ask '  MK1 密钥（直接回车用默认）' $lastKey
}
Say ("  使用密钥: " + $Key.Substring(0, [Math]::Min(18, $Key.Length)) + '...') 'DarkGray'

# ---------------------------------------------------------------- 3. Oodle 库
Head '第 3 步 / Oodle 解压库'
function Find-Oodle {
    $local = @(
        (Join-Path $Root 'bin\oo2core_9_win64.dll'),
        (Join-Path $Root 'bin\mkextract\oo2core_9_win64.dll'),
        (Join-Path $Root 'bin\oodle-data-shared.dll')
    )
    foreach ($p in $local) { if (Test-Path -LiteralPath $p) { return $p } }
    # 到常见游戏目录里找 oo2core*.dll，优先用版本号最高的那个
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($d in @('C:', 'D:', 'E:', 'F:')) {
        foreach ($sub in @('SteamLibrary\steamapps\common', 'Steam\steamapps\common', 'Games', 'Program Files (x86)\Steam\steamapps\common')) {
            $base = "$d\$sub"
            if (-not (Test-Path -LiteralPath $base)) { continue }
            Get-ChildItem -LiteralPath $base -Recurse -Depth 3 -File -Filter 'oo2core*_win64.dll' -ErrorAction SilentlyContinue |
                ForEach-Object {
                    $m = [regex]::Match($_.Name, 'oo2core_(\d+)')
                    $ver = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
                    $found.Add([pscustomobject]@{ Path = $_.FullName; Ver = $ver })
                }
        }
    }
    if ($found.Count -gt 0) { return ($found | Sort-Object Ver -Descending | Select-Object -First 1).Path }
    return $null
}
if (-not $OodleDll) {
    if ($cfg -and $cfg.oodle -and (Test-Path -LiteralPath $cfg.oodle)) { $OodleDll = $cfg.oodle }
    else {
        $found = Find-Oodle
        if ($found) {
            Say "  已自动找到: $found" 'Green'
            if (AskBool '  使用它吗？' $true) { $OodleDll = $found }
        }
    }
}
if (-not $OodleDll) {
    Say '  没找到 oo2core 运行库。几乎所有虚幻引擎游戏都自带一个（文件名 oo2core_X_win64.dll），' 'Yellow'
    Say '  随便找一个也能用；或到该目录下载：https://github.com/WorkingRobot/OodleUE/releases' 'Yellow'
    $OodleDll = Ask '  粘贴 oo2core*.dll 的完整路径（可把文件拖到这个窗口）' $null
}
if ($OodleDll -and (Test-Path -LiteralPath $OodleDll)) { Say "  ✓ 使用: $OodleDll" 'Green' }
else { Say '  ! 没有 Oodle 库，导出会失败（可以之后再补）' 'Yellow'; $OodleDll = $null }

# ---------------------------------------------------------------- 4. 角色
Head '第 4 步 / 要导出哪个角色'
if (-not $Character) {
    Say "  填游戏内部名（英文、无空格）。常见的：$KnownChars" 'DarkGray'
    $Character = Ask '  角色关键词' ($(if ($cfg -and $cfg.character) { $cfg.character } else { 'Homelander' }))
}
Say "  → $Character" 'Green'

# ---------------------------------------------------------------- 5. 语言 & 输出
Head '第 5 步 / 语言与输出位置'
if (-not $Yes) {
    Say '  可用: English(US) / French(France) / German / Italian / Portuguese(Brazil) / Spanish(Mexico) / Spanish(Spain)' 'DarkGray'
    $Language = Ask '  语言（填 all 表示全部语言都导）' $Language
}
if (-not $OutDir) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not $desktop) { $desktop = $Root }
    $defaultOut = Join-Path $desktop ($Character + '语音包')
    if ($cfg -and $cfg.outRoot -and -not $Yes) {
        $defaultOut = Join-Path $cfg.outRoot ($Character + '语音包')
    }
    $OutDir = Ask '  输出目录' $defaultOut
}
Say "  → $OutDir" 'Green'

$makeSrt   = -not $NoSrt
$makeMerge = -not $NoMerge
if (-not $Yes) {
    $makeSrt   = AskBool '  生成逐条字幕（官方英文+官方简中）' $makeSrt
    $makeMerge = AskBool '  生成台词合集（整段音频+同步字幕，方便配视频）' $makeMerge
    if (-not $WithDescriptive) { $WithDescriptive = AskBool '  包含"无障碍解说"音轨吗（不是角色本人的声音）' $false }
    if (-not $Zip) { $Zip = AskBool '  最后打包成 zip 吗' $false }
}

# ---------------------------------------------------------------- 6. 开工
Head '第 6 步 / 开始导出'
$temp = Join-Path $Root ('temp_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$wemDir = Join-Path $temp 'wem'
$wavDir = Join-Path $temp 'wav'
New-Item -ItemType Directory -Force -Path $wemDir, $wavDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if ($OodleDll) { $env:OODLE_DLL = $OodleDll }
if (Test-Path -LiteralPath $Vgm) { $env:VGMSTREAM_CLI = $Vgm }
$env:MKCHAR = $Character

$sw = [Diagnostics.Stopwatch]::StartNew()
Say ''
Say "  正在从游戏包里提取 $Character 的音频事件……（首次大约 1-3 分钟）" 'Cyan'
& $Mk voices $Paks $Key $wemDir $wavDir $Language $Character
if ($LASTEXITCODE -ne 0) { Say "  解包失败（退出码 $LASTEXITCODE）" 'Red'; if (-not $KeepTemp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }; exit 1 }

$wavCount = (Get-ChildItem -LiteralPath $wavDir -Recurse -File -Filter *.wav -ErrorAction SilentlyContinue).Count
Say "  ✓ 提取到 $wavCount 个音频文件" 'Green'
if ($wavCount -eq 0) {
    Say "  没有找到 $Character 的音频。请检查角色内部名拼写（英文、无空格），例如 OmniMan / Homelander / Peacemaker。" 'Yellow'
    if (-not $KeepTemp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    exit 1
}

# 官方文本表（字幕用）
$locresArgs = @()
if ($makeSrt -and $python) {
    New-Item -ItemType Directory -Force -Path $Cache | Out-Null
    $tables = @{ en = 'en-US'; zh = 'zh-CN'; tw = 'zh-HK' }
    $paths = @{}
    foreach ($k in $tables.Keys) {
        $tsv = Join-Path $Cache ("MK12_" + $tables[$k] + '.tsv')
        $paths[$k] = $tsv
        if (-not (Test-Path -LiteralPath $tsv)) {
            Say "  正在导出官方文本表 $($tables[$k]) …（只需一次，之后会缓存）" 'Cyan'
            & $Mk locres $Paks $Key $tsv ("Localization/MK12/" + $tables[$k]) | Out-Null
        }
    }
    $locresArgs = @('--locres-en', $paths.en, '--locres-zh', $paths.zh, '--locres-tw', $paths.tw)
}

if ($python) {
    Say ''
    Say '  正在整理文件名 / 生成对照表与字幕……' 'Cyan'
    $pyArgs = @($PyTool, '--char', $Character, '--raw', $wavDir, '--out', $OutDir) + $locresArgs
    if (-not $makeSrt) { $pyArgs += '--no-srt' }
    if (-not $makeMerge) { $pyArgs += '--no-merge' }
    if (-not $WithDescriptive) { $pyArgs += @('--skip-cat', '04') }
    if ($Zip) { $pyArgs += '--zip' }
    & $python.Source @pyArgs
    if ($LASTEXITCODE -ne 0) { Say '  整理步骤出错' 'Red' }
} else {
    Say '  跳过整理（未安装 Python），原始 wav 保留在:' 'Yellow'
    Copy-Item -LiteralPath $wavDir -Destination (Join-Path $OutDir '原始导出') -Recurse -Force
}

$sw.Stop()
Say ''
Say ('  完成！用时 {0:N1} 分钟' -f $sw.Elapsed.TotalMinutes) 'Green'
Say "  输出目录: $OutDir" 'Green'

# 记忆配置
SaveConfig ([pscustomobject]@{
    paks = $Paks; key = $Key; oodle = $OodleDll; language = $Language
    character = $Character; outRoot = (Split-Path -Parent $OutDir)
})

if (-not $KeepTemp) {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Say '  临时文件已清理' 'DarkGray'
}

if (-not $Yes) {
    if (AskBool '  现在打开输出目录吗？' $true) { Start-Process explorer.exe $OutDir }
}
