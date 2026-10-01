<#
code_stat.ps1 —— 代码体量统计（总行 / 空行 / 注释 / 有效代码 + 注释率）

用法:
  & tools\codestat\code_stat.ps1                          # 统计仓库根，产物落 <根>\CatTemp\code_stat.txt
  & tools\codestat\code_stat.ps1 -Root D:\other\repo      # 换统计根（默认 = 本脚本上两级目录）
  & tools\codestat\code_stat.ps1 -Out .\stat.txt          # 换产物路径
  & tools\codestat\code_stat.ps1 -Ext .cs,.mau            # 换扩展名集合
  & tools\codestat\code_stat.ps1 -ExcludeDirs bin,obj     # 换排除目录集合（按路径段全匹配）
  & tools\codestat\code_stat.ps1 -GroupDepth 1            # 换目录聚合深度（默认 2 级）

口径:
  行首注释计入注释行；行内尾注释随代码行；块注释跨行累计；空行独立计数
  注释语言: .cs/.js/.ts（// + /* */）· .css（/* */）· .html（<!-- -->）
            .ps1（# 行注释 + 尖括号块注释）· .mau/.mauproj（//）；其余扩展名不识别注释
  注释率 = 注释 /（注释 + 有效代码）——已排除空行
#>
param(
    [string]$Root = '',
    [string]$Out = '',
    [string[]]$Ext = @('.cs', '.mau', '.mauproj', '.html', '.js', '.css', '.ts', '.ps1', '.json'),
    [string[]]$ExcludeDirs = @('bin', 'obj', 'node_modules', '.git', 'public', 'Mau-public', 'Data', 'CatTemp', 'MauOut', '.vs'),
    [int]$GroupDepth = 2
)

if (-not $Root) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$Root = $Root.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $Root)) { throw ('统计根不存在: ' + $Root) }

if (-not $Out) {
    $catTemp = Join-Path $Root 'CatTemp'
    if (-not (Test-Path -LiteralPath $catTemp)) { New-Item -ItemType Directory -Path $catTemp -Force | Out-Null }
    $Out = Join-Path $catTemp 'code_stat.txt'
}

function New-Stat { @{ f = 0; t = 0; b = 0; c = 0; k = 0 } }
function Add-Stat($h, $total, $blank, $comment, $code) {
    $h['f'] = $h['f'] + 1
    $h['t'] = $h['t'] + $total
    $h['b'] = $h['b'] + $blank
    $h['c'] = $h['c'] + $comment
    $h['k'] = $h['k'] + $code
}
function Get-Rate($c, $k) { if (($c + $k) -gt 0) { [math]::Round(100.0 * $c / ($c + $k), 1) } else { 0 } }

# 单文件三态计数——行首注释 / 块注释 / 空行，其余计入有效代码
function Measure-File {
    param([string]$Path, [string]$E)
    $blank = 0; $comment = 0; $code = 0
    $inBlock = $false; $inHtml = $false; $inPs = $false
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $t = $line.Trim()
        if ($t.Length -eq 0) { $blank++; continue }
        $isC = $false
        if ($E -eq '.cs' -or $E -eq '.js' -or $E -eq '.ts' -or $E -eq '.css' -or $E -eq '.mau' -or $E -eq '.mauproj') {
            if ($inBlock) {
                $isC = $true
                if ($t.Contains('*/')) { $inBlock = $false }
            }
            elseif ($t.StartsWith('/*')) {
                $isC = $true
                if (-not $t.Contains('*/')) { $inBlock = $true }
            }
            elseif ($t.StartsWith('//')) { $isC = $true }
        }
        elseif ($E -eq '.html') {
            if ($inHtml) {
                $isC = $true
                if ($t.Contains('-->')) { $inHtml = $false }
            }
            elseif ($t.StartsWith('<!--')) {
                $isC = $true
                if (-not $t.Contains('-->')) { $inHtml = $true }
            }
        }
        elseif ($E -eq '.ps1') {
            if ($inPs) {
                $isC = $true
                if ($t.Contains('#>')) { $inPs = $false }
            }
            elseif ($t.StartsWith('<#')) {
                $isC = $true
                if (-not $t.Contains('#>')) { $inPs = $true }
            }
            elseif ($t.StartsWith('#')) { $isC = $true }
        }
        if ($isC) { $comment++ } else { $code++ }
    }
    return @{ b = $blank; c = $comment; k = $code }
}

$extSet = @($Ext | ForEach-Object { $_.ToLower() })

$files = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object {
    ($extSet -contains $_.Extension.ToLower()) -and
    -not (($_.FullName.Substring($Root.Length) -split '[\\/]') | Where-Object { $ExcludeDirs -contains $_ })
})

$per = @{}
$extAgg = @{}
$tot = New-Stat
$depth = [Math]::Max(1, $GroupDepth)

foreach ($f in $files) {
    $rel = ($f.FullName.Substring($Root.Length) -replace '^[\\/]+', '')
    $parts = $rel -split '[\\/]'
    $seg = [Math]::Min($depth, $parts.Count - 1)
    $key = if ($seg -ge 1) { ($parts[0..($seg - 1)] -join '\') } else { '(root)' }

    $m = Measure-File -Path $f.FullName -E $f.Extension.ToLower()
    $total = $m['b'] + $m['c'] + $m['k']

    if (-not $per.ContainsKey($key)) { $per[$key] = New-Stat }
    Add-Stat $per[$key] $total $m['b'] $m['c'] $m['k']

    $e = $f.Extension.ToLower()
    if (-not $extAgg.ContainsKey($e)) { $extAgg[$e] = New-Stat }
    Add-Stat $extAgg[$e] $total $m['b'] $m['c'] $m['k']

    Add-Stat $tot $total $m['b'] $m['c'] $m['k']
}

$sb = New-Object System.Text.StringBuilder
function Add-Line($s) { [void]$script:sb.AppendLine($s) }

Add-Line ('# 代码体量统计（去空行 / 去注释口径）  ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Add-Line ('根: ' + $Root)
Add-Line ('排除目录: ' + ($ExcludeDirs -join ', '))
Add-Line ('扩展名: ' + ($extSet -join ' '))
Add-Line ('口径: 行首注释计入注释行；行内尾注释随代码行；块注释跨行累计；注释率 = 注释 /（注释+代码）')
Add-Line ''
Add-Line '## 按扩展名 [ext files total blank comment code commentRate%]'
foreach ($k in ($extAgg.Keys | Sort-Object { -$extAgg[$_]['k'] })) {
    $v = $extAgg[$k]
    Add-Line ($k + ' | ' + $v['f'] + ' | ' + $v['t'] + ' | ' + $v['b'] + ' | ' + $v['c'] + ' | ' + $v['k'] + ' | ' + (Get-Rate $v['c'] $v['k']))
}
Add-Line ''
Add-Line ('## 按目录（' + $depth + ' 级） [dir files total blank comment code commentRate%]')
foreach ($k in ($per.Keys | Sort-Object { -$per[$_]['k'] })) {
    $v = $per[$k]
    Add-Line ($k + ' | ' + $v['f'] + ' | ' + $v['t'] + ' | ' + $v['b'] + ' | ' + $v['c'] + ' | ' + $v['k'] + ' | ' + (Get-Rate $v['c'] $v['k']))
}
Add-Line ''
Add-Line ('## 总计 | ' + $tot['f'] + ' | ' + $tot['t'] + ' | ' + $tot['b'] + ' | ' + $tot['c'] + ' | ' + $tot['k'] + ' | ' + (Get-Rate $tot['c'] $tot['k']))

[System.IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Output ('DONE files=' + $tot['f'] + ' total=' + $tot['t'] + ' blank=' + $tot['b'] + ' comment=' + $tot['c'] + ' code=' + $tot['k'])
Write-Output ('OUT=' + $Out)
