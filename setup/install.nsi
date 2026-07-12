; ============================================================
; setup/install.nsi — taskmonitor114 NSIS Installer/Updater
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

; ---- Includes ----
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"

; ---- String functions ----
${StrRep}
${StrStr}
${StrTrimNewLines}

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
!if "${PLATFORM}" == "win-x86"
  InstallDir "$PROGRAMFILES32\${PRODUCT_NAME}"
!else if "${PLATFORM}" == "win-x86-selfcontained"
  InstallDir "$PROGRAMFILES32\${PRODUCT_NAME}"
!else
  InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
!endif
RequestExecutionLevel admin
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
!define MUI_FINISHPAGE_RUN "$INSTDIR\taskmonitor114.exe"

; ---- Variables ----
Var IsUpgrade
Var IsSilent
Var DeleteData

; ============================================================
; Pages (interactive mode)
; ============================================================
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
; .onInit — Detect old install, language selection
; ============================================================
Function .onInit
  StrCpy $IsUpgrade "0"
  StrCpy $IsSilent "0"
  IfSilent 0 +2
  StrCpy $IsSilent "1"

  ; Language selection (interactive only)
  ${If} $IsSilent == "0"
    !insertmacro MUI_LANGDLL_DISPLAY
  ${EndIf}

  ; ── Detect existing install (5 signals, first match wins) ──

  ; 1. Registry: DataDir → most reliable, set by our own installer
  ReadRegStr $0 HKCU "${PRODUCT_REG_KEY}" "DataDir"
  ${If} $0 != ""
    ${If} ${FileExists} "$0\taskmonitor114.exe"
      StrCpy $INSTDIR $0
      StrCpy $IsUpgrade "1"
    ${EndIf}
  ${EndIf}

  ; 2. Running process: get executable path via wmic
  ${If} $IsUpgrade == "0"
    nsExec::ExecToStack 'wmic process where name="taskmonitor114.exe" get ExecutablePath /value'
    Pop $0
    Pop $1
    ${StrStr} $2 "$1" "ExecutablePath="
    ${If} $2 != ""
      StrCpy $0 $2 "" 16
      ${StrTrimNewLines} $0 $0
      ${GetParent} $0 $1
      ${If} ${FileExists} "$1\taskmonitor114.exe"
        StrCpy $INSTDIR $1
        StrCpy $IsUpgrade "1"
      ${EndIf}
    ${EndIf}
  ${EndIf}

  ; 3. Registry: auto-start Run key (may point to old path)
  ${If} $IsUpgrade == "0"
    ReadRegStr $0 HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"
    ${If} $0 != ""
      Push $0
      Call ParseExePath
      Pop $0
      ${If} $0 != ""
        ${GetParent} $0 $1
        ${If} ${FileExists} "$1\taskmonitor114.exe"
          StrCpy $INSTDIR $1
          StrCpy $IsUpgrade "1"
        ${EndIf}
      ${EndIf}
    ${EndIf}
  ${EndIf}

  ; 4. Default install location (platform-aware Program Files)
  ${If} $IsUpgrade == "0"
    !if "${PLATFORM}" == "win-x86"
      StrCpy $0 "$PROGRAMFILES32\${PRODUCT_NAME}"
    !else if "${PLATFORM}" == "win-x86-selfcontained"
      StrCpy $0 "$PROGRAMFILES32\${PRODUCT_NAME}"
    !else
      StrCpy $0 "$PROGRAMFILES64\${PRODUCT_NAME}"
    !endif
    ${If} ${FileExists} "$0\taskmonitor114.exe"
      StrCpy $INSTDIR $0
      StrCpy $IsUpgrade "1"
    ${EndIf}
  ${EndIf}

  ; 5. Default data location (%LOCALAPPDATA%)
  ${If} $IsUpgrade == "0"
    StrCpy $0 "$LOCALAPPDATA\${PRODUCT_NAME}"
    ${If} ${FileExists} "$0\taskmonitor114.exe"
      StrCpy $INSTDIR $0
      StrCpy $IsUpgrade "1"
    ${EndIf}
  ${EndIf}
FunctionEnd

; ============================================================
; SkipDirectoryIfUpgrade — skip dir page when upgrading
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

  ; ---- Phase 2: Upgrade — clean orphan files ----
  ${If} $IsUpgrade == "1"
    Call RemoveOrphanFiles
  ${EndIf}

  ; ---- Phase 3: Install files (all files from publish output) ----
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r /x *.pdb /x "taskmonitor114-setup-*.exe" /x "updater.exe" /x "remove-list.txt" "${PUBLISH_DIR}\*"

  ; ---- Phase 4: Registry — app paths ----
  WriteRegStr HKCU "${PRODUCT_REG_KEY}" "ConfigDir" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_REG_KEY}" "DataDir" "$INSTDIR"

  ; ---- Phase 5: Registry — uninstall info ----
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" '"$INSTDIR\taskmonitor114.exe"'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1

  ; ---- Uninstaller ----
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; ---- Phase 6: Auto-start (fresh install only, upgrade handled separately) ----
  ${If} $IsUpgrade == "0"
    WriteRegStr HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${Else}
    Call UpdateAutoStart
  ${EndIf}
SectionEnd

; ============================================================
; Section "Auto-start with Windows" (selected by default)
; ============================================================
Section "$(SEC_STARTUP_NAME)" SEC_STARTUP
  WriteRegStr HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} "$(PRODUCT_DESC)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_STARTUP} "$(SEC_STARTUP_DESC)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ============================================================
; Uninstall section
; ============================================================
Section "Uninstall"
  Call un.CheckAndKillOldProcess

  ; Remove application files (NOT user data unless user opted in)
  Delete "$INSTDIR\taskmonitor114.exe"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\resource.txt"
  Delete "$INSTDIR\*.md"
  Delete "$INSTDIR\web.config"
  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR\wwwroot"
  RMDir /r "$INSTDIR\Resources"
  RMDir /r "$INSTDIR\i18n"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  DeleteRegValue HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"

  ; Conditionally delete user data
  ${If} $DeleteData == "1"
    Call un.DeleteAllData
  ${EndIf}
SectionEnd

; ============================================================
; CheckAndKillOldProcess — detect + prompt + kill
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
; KillOldProcess — graceful → force → service (no sleeps)
; ============================================================
Function KillOldProcess
  killRetry:
  DetailPrint "Stopping taskmonitor114..."
  ExecWait 'taskkill /IM taskmonitor114.exe'

  ; Check still running → force kill
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
; RemoveOrphanFiles — delete files no longer in new version
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
    DetailPrint "  (no remove-list.txt — skipping orphan cleanup)"
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
; UpdateAutoStart — update if path changed
; ============================================================
Function UpdateAutoStart
  ReadRegStr $0 HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}"
  ${If} $0 != '"$INSTDIR\taskmonitor114.exe" --autostart'
    WriteRegStr HKCU "${PRODUCT_AUTOSTART_KEY}" "${PRODUCT_AUTOSTART_VALUE}" '"$INSTDIR\taskmonitor114.exe" --autostart'
    DetailPrint "Updated auto-start registry."
  ${Else}
    DetailPrint "Auto-start path unchanged."
  ${EndIf}
FunctionEnd

; ============================================================
; ParseExePath — extract exe path from registry value
;   "<path>" --autostart → path
; ============================================================
Function ParseExePath
  Exch $0
  Push $1
  Push $2

  StrCpy $1 $0 1
  ${If} $1 == '"'
    StrCpy $1 $0 "" 1
    ${StrStr} $2 $1 '"'
    ${If} $2 != ""
      StrLen $3 $1
      StrLen $4 $2
      IntOp $5 $3 - $4
      StrCpy $0 $1 $5
    ${EndIf}
  ${Else}
    ${StrStr} $2 $0 " "
    ${If} $2 != ""
      StrLen $3 $0
      StrLen $4 $2
      IntOp $5 $3 - $4
      StrCpy $0 $0 $5
    ${EndIf}
  ${EndIf}

  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; ============================================================
; ============================================================
; .onInstSuccess — auto-run app in silent mode
; ============================================================
Function .onInstSuccess
  ${If} $IsSilent == "1"
    Exec '"$INSTDIR\taskmonitor114.exe" --autostart'
  ${EndIf}
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
; un.DeleteAllData — find data via registry, delete all
; ============================================================
Function un.DeleteAllData
  DetailPrint "$(UNINST_DATA_DEL)"

  ; Resolve data directory (registry → fallback to default)
  ReadRegStr $R0 HKCU "${PRODUCT_REG_KEY}" "DataDir"
  ${If} $R0 == ""
    StrCpy $R0 "$LOCALAPPDATA\${PRODUCT_NAME}"
  ${EndIf}

  ; Resolve config directory
  ReadRegStr $R1 HKCU "${PRODUCT_REG_KEY}" "ConfigDir"
  ${If} $R1 == ""
    StrCpy $R1 "$LOCALAPPDATA\${PRODUCT_NAME}"
  ${EndIf}

  ; Helper: delete all files matching a pattern in a directory
  ; Uses $R2/$R3 as scratch registers

  ; Delete database files from data dir
  !insertmacro DeletePattern "$R0" "*.db"
  !insertmacro DeletePattern "$R0" "*.sqlite"
  !insertmacro DeletePattern "$R0" "*.sqlite3"
  !insertmacro DeletePattern "$R0" "*.bak"

  ; Delete config files from config dir
  Delete "$R1\settings.json"
  Delete "$R1\tags.json"
  Delete "$R1\title_rules.json"

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
  ; Step 1: Ask about data deletion first
  ${If} $IsSilent == "0"
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(UNINST_DATA_ASK)" IDYES +2
    StrCpy $DeleteData "0"
    Goto +2
    StrCpy $DeleteData "1"
  ${Else}
    StrCpy $DeleteData "0"
  ${EndIf}

  ; Step 2: Confirm uninstall (message depends on data choice)
  ${If} $DeleteData == "1"
    MessageBox MB_YESNO|MB_ICONQUESTION "$(UNINST_CONFIRM_DEL)" IDYES +2
    Abort
  ${Else}
    MessageBox MB_YESNO|MB_ICONQUESTION "$(UNINST_CONFIRM)" IDYES +2
    Abort
  ${EndIf}
FunctionEnd
