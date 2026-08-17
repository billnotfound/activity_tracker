; ============================================================
; setup/install.nsi 闁?taskmonitor114 NSIS Installer/Updater
; ============================================================
; Build: cd project-root && makensis -INPUTCHARSET UTF8 /DVERSION=1.0 /DPLATFORM=win-x64 /DPUBLISH_DIR=publish\monitor-win-x64 setup\install.nsi
; ============================================================

; ---- Compile-time defines (overridden by publish.ps1) ----
!ifndef VERSION
  !define VERSION "1.0"
!endif
!ifndef PLATFORM
  !define PLATFORM "win-x64"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "publish\monitor-win-x64"
!endif

; ---- Product metadata ----
!define PRODUCT_NAME "taskmonitor114"
!define PRODUCT_PUBLISHER "WinActivityTracker"
!define PRODUCT_REG_KEY "Software\WinActivityTracker"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_AUTOSTART_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define PRODUCT_AUTOSTART_VALUE "taskmonitor114"

; ---- MultiUser.nsh defines (must precede !include MultiUser.nsh) ----
!define MULTIUSER_INSTALLMODE_ALLOW_BOTH_INSTALLATION
!define MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER
!define MULTIUSER_EXECUTIONLEVEL "Highest"
!define MULTIUSER_INSTALLMODE_COMMANDLINE
!define MULTIUSER_INSTALLMODE_INSTDIR "${PRODUCT_NAME}"
!if "${PLATFORM}" == "win-x64"
  !define MULTIUSER_USE_PROGRAMFILES64
!endif

; ---- String functions (must precede MultiUser.nsh, which defines StrStr itself) ----
!include "StrFunc.nsh"
${StrRep}
${StrStr}
${StrTrimNewLines}

; ---- Includes ----
!include "MultiUser.nsh"
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; ---- Macro: delete files matching a wildcard pattern ----
!macro DeletePattern Dir Pattern
  FindFirst $R2 $R3 "${Dir}\${Pattern}"
  ${If} $R3 != ""
    ${Do}
      Delete "${Dir}\$R3"
      FindNext $R2 $R3
    ${LoopUntil} ${Errors}
    FindClose $R2
  ${EndIf}
!macroend

; ---- Installer attributes ----
Name "${PRODUCT_NAME} ${VERSION}"
OutFile "${PUBLISH_DIR}\${PRODUCT_NAME}-setup-${PLATFORM}.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
ShowInstDetails show
ShowUnInstDetails show
XPStyle on

; ---- Compression ----
SetCompressor /SOLID lzma
SetCompressorDictSize 32

; ---- Version metadata ----
VIProductVersion "${VERSION}.0.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${PRODUCT_PUBLISHER}"

; ---- Interface settings ----
!define MUI_ABORTWARNING
; RUN checkbox is created only when MUI_FINISHPAGE_RUN is defined;
; RUN_FUNCTION replaces the default Exec (interactive user token via explorer).
!define MUI_FINISHPAGE_RUN ""
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchAppInteractive"

; ---- Variables ----
Var IsUpgrade
Var IsSilent
Var DeleteData
Var OldExe
Var OldMode

; ============================================================
; Pages (interactive mode)
; ============================================================
!define MULTIUSER_PAGE_CUSTOMFUNCTION_PRE SkipInstallModeIfUpgrade
!insertmacro MULTIUSER_PAGE_INSTALLMODE
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirectoryIfUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; ---- Uninstall pages ----
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ---- Languages ----
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

; ---- Language strings (must come after MUI_LANGUAGE) ----
!include "install-lang.nsh"

; ============================================================
; ParseKey 闁?extract "KEY=value" from stdout blob
;   Input : stack top = KEY, below = stdout
;   Output: stack top = value (empty string if not found)
; ============================================================
Function ParseKey
  Pop $0
  Pop $1
  Push $2
  Push $3
  Push $4
  StrCpy $2 ""
  ${StrStr} $3 "$1" "$0="
  ${If} $3 != ""
    StrLen $4 "$0="
    StrCpy $3 $3 "" $4
    ${StrStr} $4 "$3" "$\r$\n"
    ${If} $4 == ""
      StrCpy $2 $3
    ${Else}
      StrLen $0 $3
      StrLen $4 $4
      IntOp $0 $0 - $4
      StrCpy $2 $3 $0
    ${EndIf}
  ${EndIf}
  StrCpy $0 $2
  Pop $4
  Pop $3
  Pop $2
  Push $0
FunctionEnd

; 取 stdout 字段的宏：${GetField} $outVar "$stdout" "KEY"
!define GetField `!insertmacro GetFieldImpl`
!macro GetFieldImpl OutVar Stdout Key
  Push "${Stdout}"
  Push "${Key}"
  Call ParseKey
  Pop "${OutVar}"
!macroend

; ============================================================
; .onInit 闁?MultiUser init, script-based old install detect
; ============================================================
Function .onInit
  StrCpy $IsUpgrade "0"
  StrCpy $OldExe ""
  StrCpy $OldMode ""
  StrCpy $IsSilent "0"
  IfSilent 0 +2
  StrCpy $IsSilent "1"

  !insertmacro MULTIUSER_INIT

  ; Language selection (interactive only)
  ${If} $IsSilent == "0"
    !insertmacro MUI_LANGDLL_DISPLAY
  ${EndIf}

  ; 鈹€鈹€ Detect existing install via PowerShell script (stdout parse) 鈹€鈹€
  ; Script failure (no OLD_FOUND) is treated as a fresh install.
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "detect-old.ps1"
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\detect-old.ps1" detect'
  Pop $0
  Pop $1
  ${GetField} $0 "$1" "OLD_FOUND"
  ${If} $0 == "1"
    ${GetField} $OldExe "$1" "OLD_EXE"
    ${If} ${FileExists} "$OldExe"
      ${GetParent} $OldExe $INSTDIR
      StrCpy $IsUpgrade "1"
      ${GetField} $OldMode "$1" "OLD_MODE"
    ${Else}
      StrCpy $OldExe ""
    ${EndIf}
  ${EndIf}

  ; 鈹€鈹€ Upgrade: keep the old install mode 鈹€鈹€
  ; InstallMode page is skipped on upgrade; MultiUser already defaulted to
  ; CurrentUser. Only switch to AllUsers when the old install was per-machine
  ; AND the current token actually has admin rights.
  ${If} $IsUpgrade == "1"
    ${If} $OldMode == "ADMIN"
      ${If} $MultiUser.Privileges == "Admin"
      ${OrIf} $MultiUser.Privileges == "Power"
        StrCpy $MultiUser.InstallMode "AllUsers"
        SetShellVarContext all
      ${EndIf}
    ${EndIf}
  ${EndIf}
FunctionEnd

; ============================================================
; SkipInstallModeIfUpgrade 闁?skip install mode page when upgrading
; ============================================================
Function SkipInstallModeIfUpgrade
  ${If} $IsUpgrade == "1"
    Abort
  ${EndIf}
FunctionEnd

; ============================================================
; SkipDirectoryIfUpgrade 闁?skip dir page when upgrading
; ============================================================
Function SkipDirectoryIfUpgrade
  ${If} $IsUpgrade == "1"
    Abort
  ${EndIf}
FunctionEnd

; ============================================================
; Section "taskmonitor114" (required)
; ============================================================
Section "$(SEC_MAIN_NAME)" SEC_MAIN
  SectionIn RO

  ; ---- Phase 1: Handle running old process ----
  Call CheckAndKillOldProcess

  ${If} $IsUpgrade == "1"
    DetailPrint "$(MSG_UPGRADE)"
  ${Else}
    DetailPrint "$(MSG_FRESH_INSTALL)"
  ${EndIf}

  ; ---- Phase 2: Upgrade 闁?clean orphan files ----
  ${If} $IsUpgrade == "1"
    Call RemoveOrphanFiles
  ${EndIf}

  ; ---- Phase 3: Install files (all files from publish output) ----
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r /x *.pdb /x "taskmonitor114-setup-*.exe" /x "updater.exe" /x "remove-list.txt" "${PUBLISH_DIR}\*"

  ; ---- Phase 6: settings.json upgrade (NTP checkbox) ----
  ; Runs for fresh installs AND upgrades: adds/updates the UseNtp key in each
  ; existing user's settings.json (surgical text edit, timestamped .bak).
  ; Fresh install + unchecked → pre-writes a minimal multi-line config so the
  ; app's smart-merge keeps UseNtp=false on first start.
  ; (Body lives in a function defined after the section declarations so the
  ;  ${SEC_NTP} section constant is available — NSIS is single-pass.)
  Call UpgradeSettingsJson

  ; ---- Phase 4: data in LOCALAPPDATA by default (no registry override needed) ----
  ; ---- Phase 5: Registry 闁?uninstall info + auto-start (per install mode) ----
  ${If} $MultiUser.InstallMode == "AllUsers"
    !if "${PLATFORM}" == "win-x64"
      SetRegView 64
    !else
      SetRegView 32
    !endif
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayIcon" '"$INSTDIR\taskmonitor114.exe"'
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoRepair" 1
    WriteRegStr HKLM "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${Else}
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" '"$INSTDIR\taskmonitor114.exe"'
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
    WriteRegStr HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${EndIf}

  ; ---- Uninstaller ----
  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

; ============================================================
; Section "Auto-start with Windows" (selected by default)
; ============================================================
Section "$(SEC_STARTUP_NAME)" SEC_STARTUP
  ${If} $MultiUser.InstallMode == "AllUsers"
    !if "${PLATFORM}" == "win-x64"
      SetRegView 64
    !else
      SetRegView 32
    !endif
    WriteRegStr HKLM "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${Else}
    WriteRegStr HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${EndIf}
SectionEnd

; ============================================================
; Section "Use NTP time verification" (selected by default)
; Component carries no files; the checked state is read by the
; main section's Phase 6 (settings.json upgrade).
; ============================================================
Section "$(SEC_NTP_NAME)" SEC_NTP
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} "$(PRODUCT_DESC)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_STARTUP} "$(SEC_STARTUP_DESC)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_NTP} "$(SEC_NTP_DESC)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ============================================================
; Function: UpgradeSettingsJson — add/update UseNtp in settings.json
; Called from SEC_MAIN Phase 6. Defined here (after the section
; declarations) so ${SEC_NTP} is available at parse time.
; ============================================================
Function UpgradeSettingsJson
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "settings-upgrade.ps1"
  ${If} ${SectionIsSelected} ${SEC_NTP}
    nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\settings-upgrade.ps1" -UseNtp 1'
  ${Else}
    nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\settings-upgrade.ps1" -UseNtp 0'
  ${EndIf}
  Pop $0
  Pop $1
  DetailPrint "Settings upgrade: $1"
FunctionEnd

; ============================================================
; Uninstall section
; ============================================================
Section "Uninstall"
  Call un.CheckAndKillOldProcess

  ; Remove application files (NOT user data unless user opted in)
  Delete "$INSTDIR\taskmonitor114.exe"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\createdump.exe"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\resource.txt"
  Delete "$INSTDIR\*.md"
  Delete "$INSTDIR\web.config"
  Delete "$INSTDIR\uninstall.exe"
  Delete "$INSTDIR\taskmonitor114.deps.json"
  Delete "$INSTDIR\taskmonitor114.runtimeconfig.json"
  Delete "$INSTDIR\taskmonitor114.staticwebassets.endpoints.json"
  Delete "$INSTDIR\runtimeconfig.template.json"
  RMDir /r "$INSTDIR\wwwroot"
  RMDir /r "$INSTDIR\Resources"
  RMDir /r "$INSTDIR\i18n"
  ; .NET satellite language dirs
  RMDir /r "$INSTDIR\cs"
  RMDir /r "$INSTDIR\de"
  RMDir /r "$INSTDIR\es"
  RMDir /r "$INSTDIR\fr"
  RMDir /r "$INSTDIR\it"
  RMDir /r "$INSTDIR\ja"
  RMDir /r "$INSTDIR\ko"
  RMDir /r "$INSTDIR\pl"
  RMDir /r "$INSTDIR\pt-BR"
  RMDir /r "$INSTDIR\ru"
  RMDir /r "$INSTDIR\tr"
  RMDir /r "$INSTDIR\zh-Hans"
  RMDir /r "$INSTDIR\zh-Hant"
  ; Delete data branch: wipe the whole dir; keep branch: only empty leftovers
  ${If} $DeleteData == "1"
    RMDir /r "$INSTDIR"
  ${Else}
    RMDir "$INSTDIR"
  ${EndIf}

  ; ---- Registry cleanup via script (enumerates HKU/HKLM all views) ----
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "detect-old.ps1"
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\detect-old.ps1" cleanup'
  Pop $0
  Pop $1
  DetailPrint "Registry cleanup: $1"

  ; ---- NSIS fallback (still removes keys visible to this process) ----
  SetRegView 64
  DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
  DeleteRegValue HKLM "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"
  SetRegView 32
  DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
  DeleteRegValue HKLM "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  DeleteRegValue HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"

  StrCmp $DeleteData "1" del_data no_del
del_data:
  Call un.DeleteAllData
no_del:
SectionEnd

; ============================================================
; CheckAndKillOldProcess 闁?detect + prompt + kill
; ============================================================
Function CheckAndKillOldProcess
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq taskmonitor114.exe" /NH'
  Pop $0
  Pop $1
  ${StrStr} $2 "$1" "taskmonitor114"
  ${If} $2 == ""
    Return
  ${EndIf}

  ; Interactive: ask user
  ${If} $IsSilent == "0"
    MessageBox MB_YESNO|MB_ICONQUESTION "$(MSG_PROCESS_FOUND)" IDYES +2
    Abort "$(MSG_PROCESS_FOUND)"
  ${EndIf}

  Call KillOldProcess
FunctionEnd

; ============================================================
; KillOldProcess 闁?graceful 闁?force 闁?service (no sleeps)
; ============================================================
Function KillOldProcess
  killRetry:
  DetailPrint "Stopping taskmonitor114..."
  ExecWait 'taskkill /IM taskmonitor114.exe'

  ; Check still running 闁?force kill
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq taskmonitor114.exe" /NH'
  Pop $0
  Pop $1
  ${StrStr} $2 "$1" "taskmonitor114"
  ${If} $2 != ""
    DetailPrint "Force stopping..."
    ExecWait 'taskkill /F /IM taskmonitor114.exe /T'
  ${EndIf}

  ; Final check
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq taskmonitor114.exe" /NH'
  Pop $0
  Pop $1
  ${StrStr} $2 "$1" "taskmonitor114"
  ${If} $2 != ""
    ${If} $IsSilent == "0"
      MessageBox MB_ABORTRETRYIGNORE|MB_ICONEXCLAMATION "$(MSG_KILL_RETRY)" IDRETRY killRetry IDIGNORE +2
      Abort
    ${Else}
      DetailPrint "Warning: process still running."
    ${EndIf}
  ${EndIf}

  ; Stop Windows Service (ignore errors)
  ExecWait 'net stop taskmonitor114'
FunctionEnd

; ============================================================
; RemoveOrphanFiles 闁?delete files no longer in new version
;   Uses precomputed remove-list.txt (embedded at build time).
;   Protected files are never in the list (filtered at build time).
; ============================================================
Function RemoveOrphanFiles
  DetailPrint "$(MSG_ORPHAN_TIER2)"

  ; Extract embedded remove-list.txt to temp
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /nonfatal "${PUBLISH_DIR}\remove-list.txt"

  ${IfNot} ${FileExists} "$PLUGINSDIR\remove-list.txt"
    DetailPrint "  (no remove-list.txt 闁?skipping orphan cleanup)"
    Return
  ${EndIf}

  ClearErrors
  FileOpen $R0 "$PLUGINSDIR\remove-list.txt" r
  IfErrors done

  listloop:
    ClearErrors
    FileRead $R0 $R1
    IfErrors done
    ${StrTrimNewLines} $R1 $R1

    ${If} $R1 == ""
      Goto listloop
    ${EndIf}
    StrCpy $R2 $R1 1
    ${If} $R2 == "#"
      Goto listloop
    ${OrIf} $R2 == ";"
      Goto listloop
    ${EndIf}

    ${StrRep} $R1 $R1 "/" "\"
    ${If} ${FileExists} "$INSTDIR\$R1"
      DetailPrint "$(MSG_DELETING)$R1"
      Delete "$INSTDIR\$R1"
    ${EndIf}
    Goto listloop

  done:
    ${If} $R0 != ""
      FileClose $R0
    ${EndIf}
FunctionEnd

; ============================================================
; .onInstSuccess 闁?auto-run app in silent mode (interactive user token)
; ============================================================
Function .onInstSuccess
  ${If} $IsSilent == "1"
    ExecShell "open" '"$INSTDIR\taskmonitor114.exe"' "--autostart"
  ${EndIf}
FunctionEnd

; ============================================================
; LaunchAppInteractive 闁?finish-page "Run" (interactive user token)
; ============================================================
Function LaunchAppInteractive
  ExecShell "open" '"$INSTDIR\taskmonitor114.exe"' "--autostart"
FunctionEnd

; ============================================================
; Uninstall helpers
; ============================================================
Function un.CheckAndKillOldProcess
  ; Try graceful kill (succeeds even if process not found)
  ExecWait 'taskkill /IM taskmonitor114.exe'
  ; Force kill just in case
  ExecWait 'taskkill /F /IM taskmonitor114.exe /T'
  ; Stop service if present
  ExecWait 'net stop taskmonitor114'
FunctionEnd

; ============================================================
; un.DeleteAllData 闁?find data via registry, delete all
; ============================================================
Function un.DeleteAllData
  DetailPrint "$(UNINST_DATA_DEL)"

  ; Resolve data directory (registry 闁?fallback to app default %LOCALAPPDATA%\WinActivityTracker)
  ; Note: the app only writes DataDir/ConfigDir registry values when the user
  ; changes paths via the admin API; normal installs have no values, so the
  ; fallback (not $LOCALAPPDATA\${PRODUCT_NAME}) is what actually holds the data.
  ReadRegStr $R0 HKCU "${PRODUCT_REG_KEY}" "DataDir"
  ${If} $R0 == ""
    StrCpy $R0 "$LOCALAPPDATA\WinActivityTracker"
  ${EndIf}

  ; Resolve config directory (usually the same as DataDir)
  ReadRegStr $R1 HKCU "${PRODUCT_REG_KEY}" "ConfigDir"
  ${If} $R1 == ""
    StrCpy $R1 "$LOCALAPPDATA\WinActivityTracker"
  ${EndIf}

  ; Helper: delete all files matching a pattern in a directory
  ; Uses $R2/$R3 as scratch registers

  ; Delete database + WAL + pid files from data dir, then the dir itself
  !insertmacro DeletePattern "$R0" "*.db"
  !insertmacro DeletePattern "$R0" "*.db-shm"
  !insertmacro DeletePattern "$R0" "*.db-wal"
  !insertmacro DeletePattern "$R0" "*.sqlite"
  !insertmacro DeletePattern "$R0" "*.sqlite3"
  !insertmacro DeletePattern "$R0" "*.bak"
  !insertmacro DeletePattern "$R0" "*.pid"
  Delete "$R0\settings.json"
  Delete "$R0\tags.json"
  Delete "$R0\title_rules.json"
  RMDir /r "$R0"

  ; Delete config files from config dir, then the dir itself
  ${If} $R1 != $R0
    Delete "$R1\settings.json"
    Delete "$R1\tags.json"
    Delete "$R1\title_rules.json"
    !insertmacro DeletePattern "$R1" "*.pid"
    RMDir /r "$R1"
  ${EndIf}

  ; Legacy default dir ($LOCALAPPDATA\taskmonitor114) from earlier installers
  ${If} $R0 != "$LOCALAPPDATA\taskmonitor114"
    !insertmacro DeletePattern "$LOCALAPPDATA\taskmonitor114" "*.db"
    !insertmacro DeletePattern "$LOCALAPPDATA\taskmonitor114" "*.sqlite"
    !insertmacro DeletePattern "$LOCALAPPDATA\taskmonitor114" "*.sqlite3"
    !insertmacro DeletePattern "$LOCALAPPDATA\taskmonitor114" "*.bak"
    !insertmacro DeletePattern "$LOCALAPPDATA\taskmonitor114" "*.pid"
    Delete "$LOCALAPPDATA\taskmonitor114\settings.json"
    Delete "$LOCALAPPDATA\taskmonitor114\tags.json"
    Delete "$LOCALAPPDATA\taskmonitor114\title_rules.json"
    RMDir /r "$LOCALAPPDATA\taskmonitor114"
  ${EndIf}

  ; Also clean $INSTDIR in case data lived alongside the app
  ${If} $R0 != "$INSTDIR"
    !insertmacro DeletePattern "$INSTDIR" "*.db"
    !insertmacro DeletePattern "$INSTDIR" "*.sqlite"
    !insertmacro DeletePattern "$INSTDIR" "*.sqlite3"
    !insertmacro DeletePattern "$INSTDIR" "*.bak"
  ${EndIf}
  ${If} $R1 != "$INSTDIR"
    Delete "$INSTDIR\settings.json"
    Delete "$INSTDIR\tags.json"
    Delete "$INSTDIR\title_rules.json"
  ${EndIf}

  ; Remove registry keys that pointed to data/config dirs
  DeleteRegValue HKCU "${PRODUCT_REG_KEY}" "DataDir"
  DeleteRegValue HKCU "${PRODUCT_REG_KEY}" "ConfigDir"
  DeleteRegKey /ifempty HKCU "${PRODUCT_REG_KEY}"
FunctionEnd

Function un.onInit
  !insertmacro MULTIUSER_UNINIT
  ; "Keep data?" — YES keeps, NO deletes
  ; /SD IDYES: silent uninstall auto-answers YES (keep data) so no dialog blocks
  MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(UNINST_DATA_ASK)" /SD IDYES IDYES +2
  StrCpy $DeleteData "1"
  Goto confirm
  StrCpy $DeleteData "0"

confirm:
  StrCmp $DeleteData "1" del_dialog keep_dialog

del_dialog:
  MessageBox MB_YESNO|MB_ICONQUESTION "$(UNINST_CONFIRM_DEL)" /SD IDYES IDYES +2
  Abort
  Goto done

keep_dialog:
  MessageBox MB_YESNO|MB_ICONQUESTION "$(UNINST_CONFIRM)" /SD IDYES IDYES +2
  Abort

done:
FunctionEnd
