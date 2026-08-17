# settings-upgrade.ps1 — 安装/升级时升级 settings.json：
#   已有配置 → 外科手术式补键（保留注释与格式），先备份成带时间戳 .bak
#   全新安装 + UseNtp=0 → 交互用户配置目录预写多行最小配置
# 参数: -UseNtp 0|1
param([int]$UseNtp = 1)
[Console]::OutputEncoding = [Text.Encoding]::GetEncoding((Get-Culture).TextInfo.ANSICodePage)
$ErrorActionPreference = 'SilentlyContinue'

# 勾选状态 → 文本布尔（与 brief 内联 `-as [bool] -as [string]` 输出一致，更直白）
$useNtpText = if ($UseNtp -eq 1) { 'true' } else { 'false' }

function Get-ConfigDirs {
  # 枚举 HKU 真实用户 hive（过滤 .DEFAULT / S-1-5-18/19/20 / _Classes / -Profile）
  $dirs = @()
  foreach ($sid in [Microsoft.Win32.Registry]::Users.GetSubKeyNames()) {
    if ($sid -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { continue }
    if ($sid.EndsWith('_Classes')) { continue }
    $regPath = "$sid\Software\WinActivityTracker"
    $cfg = (Get-ItemProperty "Registry::HKEY_USERS\$regPath" -Name ConfigDir -ErrorAction SilentlyContinue).ConfigDir
    if ($cfg -and (Test-Path $cfg)) { $dirs += $cfg; continue }
    # 无注册表值 → ProfileImagePath 推 LOCALAPPDATA\WinActivityTracker
    $profile = (Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -Name ProfileImagePath -ErrorAction SilentlyContinue).ProfileImagePath
    if ($profile) { $dirs += (Join-Path $profile 'AppData\Local\WinActivityTracker') }
  }
  return $dirs
}

function Upgrade-SettingsFile([string]$path) {
  if (-not (Test-Path $path)) { return $false }
  $raw = Get-Content $path -Raw -Encoding UTF8
  if ($raw -match '(?m)^\s*"UseNtp"') {
    # 已有 UseNtp 键 → 只改值（保留缩进、注释与其余格式）
    $raw = $raw -replace '(?m)^(\s*"UseNtp"\s*:\s*)(true|false)', "`${1}$useNtpText"
  } else {
    # 缺键 → 在闭合花括号前补键 + 注释（保留原文件其余内容与注释）
    # 关键：新键不能带尾逗号 —— SettingsService.Load 用 System.Text.Json 默认选项
    # （AllowTrailingCommas=false），尾逗号会抛 JsonException → 整个配置被重置为默认。
    # 同时原最后一个属性若没有尾逗号（应用 Save 生成的格式最后键无逗号）必须补逗号，
    # 否则 "value\n"UseNtp": ..." 缺逗号同样非法 JSON。
    # 逗号必须加在"值完整行"上：单行值 → 属性行本身；多行值（行尾是 { [）→
    # 前向扫描到匹配的闭合括号行（跳过字符串与注释），在闭合行尾补逗号。
    $comment = "  // ===== 时间异常检测 ====="
    $line = "  `"UseNtp`": $useNtpText"
    $raw = $raw.TrimEnd()
    $lines = $raw -split "`n"
    for ($i = $lines.Length - 1; $i -ge 0; $i--) {
      $t = $lines[$i].TrimStart()
      if ($t -match '^"[^"]+"\s*:') {
        $trimmed = $lines[$i].TrimEnd()
        if ($trimmed -match '[\[\{]$') {
          # 多行值：追踪括号深度，跳过字符串内容与注释，找到把深度归零的闭合行
          $depth = 1
          $inString = $false
          for ($j = $i + 1; $j -lt $lines.Length; $j++) {
            if ($lines[$j].TrimStart().StartsWith('//')) { continue }
            $escaped = $false
            foreach ($ch in $lines[$j].ToCharArray()) {
              if ($inString) {
                if ($escaped) { $escaped = $false; continue }
                if ($ch -eq '\') { $escaped = $true; continue }
                if ($ch -eq '"') { $inString = $false }
                continue
              }
              if ($ch -eq '"') { $inString = $true; continue }
              if ($ch -eq '{' -or $ch -eq '[') { $depth++ }
              elseif ($ch -eq '}' -or $ch -eq ']') {
                $depth--
                if ($depth -eq 0) {
                  $cTrimmed = $lines[$j].TrimEnd()
                  if ($cTrimmed -notmatch ',$') { $lines[$j] = $cTrimmed + ',' }
                  break
                }
              }
            }
            if ($depth -eq 0) { break }
          }
        } else {
          # 单行值：仅当不以 , : { [ 结尾时补逗号
          if ($trimmed -notmatch '[,:\{\[]$') { $lines[$i] = $trimmed + ',' }
        }
        break
      }
    }
    $raw = ($lines -join "`n") -replace '\}\s*$', "`r`n$comment`r`n$line`r`n}"
  }
  $stamp = Get-Date -Format 'yyyyMMddHHmmss'
  Copy-Item $path "$path.bak-$stamp" -Force
  $tmp = "$path.tmp"
  [System.IO.File]::WriteAllText($tmp, $raw, (New-Object System.Text.UTF8Encoding($false)))
  Move-Item $tmp $path -Force
  return $true
}

$changed = $false
foreach ($dir in (Get-ConfigDirs)) {
  $changed = $changed -or (Upgrade-SettingsFile (Join-Path $dir 'settings.json'))
}

# 全新安装 + 未勾选 → 交互用户预写最小多行配置
if (-not $changed -and $UseNtp -eq 0) {
  $explorer = Get-Process explorer -IncludeUserName -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($explorer) {
    $user = $explorer.UserName -replace '^.*\\', ''
    $sid = (New-Object System.Security.Principal.NTAccount($user)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    $profile = (Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -Name ProfileImagePath).ProfileImagePath
    if ($profile) {
      $dir = Join-Path $profile 'AppData\Local\WinActivityTracker'
      $path = Join-Path $dir 'settings.json'
      if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        # 必须多行（用反引号转义换行；`\r\n` 字面量在 PS 里是反斜杠文本，会合成单行
        # → SettingsService.ExtractJsonKeys 按行首 " 提取键名，单行会丢键 → 被重置默认 true）
        [System.IO.File]::WriteAllText($path,
          "{`r`n  `"UseNtp`": false`r`n}`r`n",
          (New-Object System.Text.UTF8Encoding($false)))
        $changed = $true
      }
    }
  }
}

Write-Output ($(if ($changed) { 'OK' } else { 'SKIP' }))
