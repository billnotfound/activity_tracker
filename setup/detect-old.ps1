<#
.SYNOPSIS
  taskmonitor114 安装/卸载辅助脚本（PowerShell 5.1 兼容）
  detect  安装时：检测旧版安装位置与运行状态，stdout 输出固定行结果（NSIS nsExec 解析）
  cleanup 卸载时：枚举 HKU/HKLM 删除所有 taskmonitor114 注册表痕迹

.USAGE
  powershell -NoProfile -ExecutionPolicy Bypass -File detect-old.ps1 detect
  powershell -NoProfile -ExecutionPolicy Bypass -File detect-old.ps1 cleanup
#>
param([string]$Mode = "detect")

$ErrorActionPreference = "Continue"
$ProductName = "taskmonitor114"
$AppRegKeyPath = "Software\WinActivityTracker"
$AutoStartKeyPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$AutoStartValueName = "taskmonitor114"
$UninstallKeyPath = "Software\Microsoft\Windows\CurrentVersion\Uninstall\taskmonitor114"

# stdout 必须为系统 ANSI 代码页，NSIS nsExec 按同一代码页解释，中文路径不乱码
[Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding((Get-Culture).TextInfo.ANSICodePage)

function Out-Result([string]$key, [string]$value) {
    if (-not [string]::IsNullOrEmpty($value)) { Write-Output "$key=$value" }
}

function Test-ExeExists([string]$exePath) {
    return (-not [string]::IsNullOrWhiteSpace($exePath)) -and
           (Test-Path -LiteralPath $exePath -PathType Leaf)
}

function Get-RealUserSids {
    $sids = @()
    foreach ($sid in [Microsoft.Win32.Registry]::Users.GetSubKeyNames()) {
        if ($sid -match '^S-1-5-21-\d+-\d+-\d+-\d+$') { $sids += $sid }
    }
    return $sids
}

function Get-ExeFromAutoStartValue([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    if ($value -match '"([^"]+)"') { return $Matches[1] }
    return $null
}

function Get-RunningProcessInfo {
    # nsExec 无 console 环境下 Get-Process.MainModule.FileName 返回空串，
    # 必须用 CIM Win32_Process.ExecutablePath（与 Get-ServiceInfo 同款方式）
    $procs = Get-CimInstance Win32_Process -Filter "Name='$ProductName.exe'" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        if (Test-ExeExists $p.ExecutablePath) {
            return @{ Exe = $p.ExecutablePath; Mode = "USER" }
        }
    }
    return $null
}

function Get-ServiceInfo {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$ProductName'" -ErrorAction SilentlyContinue
    if ($svc -and $svc.PathName) {
        $exe = $null
        if ($svc.PathName -match '^"([^"]+\.exe)"') { $exe = $Matches[1] }
        elseif ($svc.PathName -match '^(\S+\.exe)') { $exe = $Matches[1] }
        if (Test-ExeExists $exe) { return @{ Exe = $exe; State = $svc.State } }
    }
    return $null
}

function Get-DefaultInstallExe {
    foreach ($dir in @(
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86))) {
        if (-not $dir) { continue }
        $exe = Join-Path $dir "$ProductName\taskmonitor114.exe"
        if (Test-ExeExists $exe) { return $exe }
    }
    return $null
}

function Get-AutoStartHits {
    $hits = @()
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,
                        [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $key = $base.OpenSubKey($AutoStartKeyPath)
            if ($key) {
                $val = $key.GetValue($AutoStartValueName)
                $key.Close()
                $exe = Get-ExeFromAutoStartValue $val
                if (Test-ExeExists $exe) { $hits += @{ Exe = $exe; Mode = "ADMIN"; Sid = $null } }
            }
        } catch { }
        finally { $base.Close() }
    }
    foreach ($sid in Get-RealUserSids) {
        try {
            $key = [Microsoft.Win32.Registry]::Users.OpenSubKey("$sid\$AutoStartKeyPath")
            if ($key) {
                $val = $key.GetValue($AutoStartValueName)
                $key.Close()
                $exe = Get-ExeFromAutoStartValue $val
                if (Test-ExeExists $exe) { $hits += @{ Exe = $exe; Mode = "USER"; Sid = $sid } }
            }
        } catch { }
    }
    return $hits
}

function Get-UninstallHits {
    $hits = @()
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,
                        [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $key = $base.OpenSubKey($UninstallKeyPath)
            if ($key) {
                $exe = $null
                $icon = $key.GetValue("DisplayIcon")
                if ($icon -match '"([^"]+\.exe)"') { $exe = $Matches[1] }
                elseif ($icon -match '^(\S+\.exe)') { $exe = $Matches[1] }
                $key.Close()
                if (Test-ExeExists $exe) { $hits += @{ Exe = $exe; Mode = "ADMIN"; Sid = $null } }
            }
        } catch { }
        finally { $base.Close() }
    }
    foreach ($sid in Get-RealUserSids) {
        try {
            $key = [Microsoft.Win32.Registry]::Users.OpenSubKey("$sid\$UninstallKeyPath")
            if ($key) {
                $exe = $null
                $icon = $key.GetValue("DisplayIcon")
                if ($icon -match '"([^"]+\.exe)"') { $exe = $Matches[1] }
                elseif ($icon -match '^(\S+\.exe)') { $exe = $Matches[1] }
                $key.Close()
                if (Test-ExeExists $exe) { $hits += @{ Exe = $exe; Mode = "USER"; Sid = $sid } }
            }
        } catch { }
    }
    return $hits
}

function Get-WtaHits {
    $hits = @()
    foreach ($sid in Get-RealUserSids) {
        try {
            $key = [Microsoft.Win32.Registry]::Users.OpenSubKey("$sid\$AppRegKeyPath")
            if ($key) {
                foreach ($valName in @("DataDir", "ConfigDir")) {
                    $dir = $key.GetValue($valName)
                    if ($dir) {
                        $exe = Join-Path $dir "taskmonitor114.exe"
                        if (Test-ExeExists $exe) { $hits += @{ Exe = $exe; Mode = "USER"; Sid = $sid } }
                    }
                }
                $key.Close()
            }
        } catch { }
    }
    return $hits
}

function Invoke-Detect {
    $signals = [System.Collections.Generic.List[string]]::new()
    $old = $null

    $proc = Get-RunningProcessInfo
    if ($proc) { $old = $proc; $signals.Add("PROCESS") }

    if (-not $old) {
        $svc = Get-ServiceInfo
        if ($svc) { $old = @{ Exe = $svc.Exe; Mode = "ADMIN"; Sid = $null }; $signals.Add("SERVICE") }
    }
    if (-not $old) {
        $pf = Get-DefaultInstallExe
        if ($pf) { $old = @{ Exe = $pf; Mode = "ADMIN"; Sid = $null }; $signals.Add("PF") }
    }
    if (-not $old) {
        $as = @(Get-AutoStartHits)
        if ($as.Count -gt 0) { $old = $as[0]; $signals.Add("AUTOSTART") }
    }
    if (-not $old) {
        $un = @(Get-UninstallHits)
        if ($un.Count -gt 0) { $old = $un[0]; $signals.Add("UNINSTALL") }
    }
    if (-not $old) {
        $wta = @(Get-WtaHits)
        if ($wta.Count -gt 0) { $old = $wta[0]; $signals.Add("WTA") }
    }

    $procAll = @(Get-CimInstance Win32_Process -Filter "Name='$ProductName.exe'" -ErrorAction SilentlyContinue)
    $service = Get-ServiceInfo

    Out-Result "VERSION" "1"
    Out-Result "OLD_FOUND" $(if ($old) { "1" } else { "0" })
    Out-Result "OLD_EXE" $old.Exe
    Out-Result "OLD_MODE" $old.Mode
    Out-Result "RUNNING" $(if ($procAll.Count -gt 0) { "1" } else { "0" })
    if ($procAll.Count -gt 0) {
        $runExe = $null
        try { $runExe = $procAll[0].ExecutablePath } catch { }
        Out-Result "RUNNING_EXE" $runExe
    }
    Out-Result "SERVICE" $(if ($service) { $service.State } else { "ABSENT" })
    Out-Result "SERVICE_EXE" $service.Exe
    $asAll = @(Get-AutoStartHits)
    if ($asAll.Count -gt 0) { Out-Result "AUTOSTART" $asAll[0].Exe }
    Out-Result "INTERACTIVE_USER" (Get-InteractiveUserSid)
    Out-Result "SIGNALS" ($signals -join ",")
}

function Get-InteractiveUserSid {
    try {
        $explorer = Get-Process explorer -IncludeUserName -ErrorAction SilentlyContinue |
                    Select-Object -First 1
        if ($explorer -and $explorer.UserName) {
            $acct = New-Object System.Security.Principal.NTAccount($explorer.UserName)
            return $acct.Translate([System.Security.Principal.SecurityIdentifier]).Value
        }
    } catch { }
    try {
        foreach ($line in (& query user 2>$null)) {
            if ($line -match '^\s*[>]?(\S+)\s+\S+\s+\d+\s+Active') {
                $acct = New-Object System.Security.Principal.NTAccount($Matches[1])
                return $acct.Translate([System.Security.Principal.SecurityIdentifier]).Value
            }
        }
    } catch { }
    return $null
}

function Invoke-Cleanup {
    $cleaned = [System.Collections.Generic.List[string]]::new()

    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,
                        [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $run = $base.OpenSubKey($AutoStartKeyPath, $true)
            if ($run) {
                if ($run.GetValue($AutoStartValueName) -ne $null) {
                    $run.DeleteValue($AutoStartValueName)
                    $cleaned.Add("HKLM($view):Run")
                }
                $run.Close()
            }
            foreach ($sub in @($UninstallKeyPath, $AppRegKeyPath)) {
                try {
                    $base.DeleteSubKeyTree($sub, $false)
                    if (-not $base.OpenSubKey($sub)) { $cleaned.Add("HKLM($view):$sub") }
                } catch { }
            }
        } catch { }
        finally { $base.Close() }
    }

    foreach ($sid in Get-RealUserSids) {
        try {
            $base = [Microsoft.Win32.Registry]::Users.OpenSubKey($sid, $true)
            if (-not $base) { continue }
            $run = $base.OpenSubKey($AutoStartKeyPath, $true)
            if ($run) {
                if ($run.GetValue($AutoStartValueName) -ne $null) {
                    $run.DeleteValue($AutoStartValueName)
                    $cleaned.Add("HKU($sid):Run")
                }
                $run.Close()
            }
            foreach ($sub in @($UninstallKeyPath, $AppRegKeyPath)) {
                try {
                    $base.DeleteSubKeyTree($sub, $false)
                    if (-not $base.OpenSubKey($sub)) { $cleaned.Add("HKU($sid):$sub") }
                } catch { }
            }
            $base.Close()
        } catch { }
    }

    Out-Result "CLEANED" ($cleaned -join ",")
}

if ($Mode -eq "cleanup") { Invoke-Cleanup; exit 0 }
if ($Mode -eq "detect") { Invoke-Detect }
