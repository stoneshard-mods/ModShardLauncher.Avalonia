; ModShardLauncher installer
; Build: scripts/build-installer.ps1 (or makensis /DPUBLISH_DIR=..\publish scripts\ModShardLauncher.nsi)

Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinMessages.nsh"
!include "StrFunc.nsh"

; String helpers used when editing the per-user PATH value.
${StrStr}
${UnStrRep}

; ----- build inputs -------------------------------------------------------
!ifndef APP_VERSION
  !define APP_VERSION "0.13.2"
!endif

!ifndef APP_VERSION4
  !define APP_VERSION4 "${APP_VERSION}.0"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif

!ifndef LICENSE_FILE
  !define LICENSE_FILE "..\LICENSE"
!endif

!ifndef ICON_FILE
  !define ICON_FILE "..\src\ModShardLauncher.GUI\Assets\app.ico"
!endif

!ifndef OUTFILE
  !define OUTFILE "..\bundle\ModShardLauncher-Setup-${APP_VERSION}.exe"
!endif

; ----- product metadata ---------------------------------------------------
!define PRODUCT_NAME "ModShardLauncher"
!define PRODUCT_PUBLISHER "ModShardTeam"
!define PRODUCT_WEB_SITE "https://github.com/ModShardTeam/ModShardLauncher"
!define APP_KEY "Software\ModShardTeam\ModShardLauncher"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define APP_EXE "ModShardLauncher.GUI.exe"
!define CLI_EXE "ModShardLauncher.CLI.exe"
!define SERVICE_NAME "ModShardLauncher"
!define FIREWALL_RULE "ModShardLauncher"
; User data lives outside the installation folder, so updating or reinstalling the
; application never touches the user's mods, mod sources, logs or settings.
!define USER_DATA_DIR "$LOCALAPPDATA\${PRODUCT_NAME}"

Name "${PRODUCT_NAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
InstallDirRegKey HKCU "${APP_KEY}" "InstallDir"
RequestExecutionLevel user
BrandingText "${PRODUCT_NAME} ${APP_VERSION}"

SetCompressor /SOLID lzma
SetCompressorDictSize 64
CRCCheck on
ShowInstDetails show
ShowUninstDetails show

; High DPI + Windows 7..11 compatibility manifest for the installer itself.
ManifestDPIAware true
ManifestSupportedOS all

VIProductVersion "${APP_VERSION4}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "GPL-3.0"
VIAddVersionKey "Comments" "Stoneshard mod manager and modding toolkit"

; ----- UI -----------------------------------------------------------------
!define MUI_ABORTWARNING
!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "${APP_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(RunApp)"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_RESERVEFILE_LANGDLL

; ----- localized strings --------------------------------------------------
LangString SecCore        ${LANG_ENGLISH}   "Application"
LangString SecCoreDesc    ${LANG_ENGLISH}   "ModShardLauncher GUI, CLI, the shared runtime and the modding API."
LangString SecDesktop     ${LANG_ENGLISH}   "Desktop shortcut"
LangString SecDesktopDesc ${LANG_ENGLISH}   "Create a shortcut on the desktop."
LangString AppRunning     ${LANG_ENGLISH}   "ModShardLauncher is running. Close it and continue?"
LangString KeepUserData   ${LANG_ENGLISH}   "Keep your mods, mod sources, logs and settings? They are stored in %LOCALAPPDATA%\ModShardLauncher."
LangString UninstallingOld ${LANG_ENGLISH}  "Removing the previous installation from:"
LangString RunApp         ${LANG_ENGLISH}   "Launch ModShardLauncher"
LangString SecCli         ${LANG_ENGLISH}   "Command line integration"
LangString SecCliDesc     ${LANG_ENGLISH}   "Add the install folder to your user PATH and create msl.cmd, so msl compile / msl patch work in any terminal."

LangString SecCore        ${LANG_SIMPCHINESE} "应用程序"
LangString SecCoreDesc    ${LANG_SIMPCHINESE} "ModShardLauncher 图形界面、命令行、共享运行时与模组 API。"
LangString SecDesktop     ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString SecDesktopDesc ${LANG_SIMPCHINESE} "在桌面创建快捷方式。"
LangString AppRunning     ${LANG_SIMPCHINESE} "ModShardLauncher 正在运行。是否关闭并继续？"
LangString KeepUserData   ${LANG_SIMPCHINESE} "是否保留模组、模组源码、日志和设置？它们保存在 %LOCALAPPDATA%\ModShardLauncher。"
LangString UninstallingOld ${LANG_SIMPCHINESE} "正在移除旧版本，位置："
LangString RunApp         ${LANG_SIMPCHINESE} "启动 ModShardLauncher"
LangString SecCli         ${LANG_SIMPCHINESE} "命令行集成"
LangString SecCliDesc     ${LANG_SIMPCHINESE} "把安装目录加入用户 PATH 并生成 msl.cmd，之后可在任意终端直接运行 msl compile / msl patch。"

LangString SecCore        ${LANG_RUSSIAN}   "Приложение"
LangString SecCoreDesc    ${LANG_RUSSIAN}   "Интерфейс ModShardLauncher, CLI, общая среда выполнения и API для модов."
LangString SecDesktop     ${LANG_RUSSIAN}   "Ярлык на рабочем столе"
LangString SecDesktopDesc ${LANG_RUSSIAN}   "Создать ярлык на рабочем столе."
LangString AppRunning     ${LANG_RUSSIAN}   "ModShardLauncher запущен. Закрыть его и продолжить?"
LangString KeepUserData   ${LANG_RUSSIAN}   "Сохранить моды, исходники, журналы и настройки? Они хранятся в %LOCALAPPDATA%\ModShardLauncher."
LangString UninstallingOld ${LANG_RUSSIAN}  "Удаление предыдущей установки из:"
LangString RunApp         ${LANG_RUSSIAN}   "Запустить ModShardLauncher"
LangString SecCli         ${LANG_RUSSIAN}   "Интеграция с командной строкой"
LangString SecCliDesc     ${LANG_RUSSIAN}   "Добавить папку установки в пользовательский PATH и создать msl.cmd, чтобы msl compile / msl patch работали в любом терминале."

Var OLD_INSTDIR

; PATH bookkeeping for the command line integration section.
Var MSL_PATH
Var MSL_MATCH
Var MSL_TMP

; ----- init ---------------------------------------------------------------
Function .onInit
  ${IfNot} ${Silent}
    !insertmacro MUI_LANGDLL_DISPLAY
  ${EndIf}
FunctionEnd

Function un.onInit
  ${IfNot} ${Silent}
    !insertmacro MUI_UNGETLANGUAGE
  ${EndIf}
FunctionEnd

; ----- helpers ------------------------------------------------------------
Function CheckRunningApps
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /NH | find /I "${APP_EXE}"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    IfSilent killApps
    MessageBox MB_YESNO|MB_ICONQUESTION "$(AppRunning)" IDYES killApps
    Abort
  ${EndIf}
  Return

  killApps:
    nsExec::Exec 'taskkill /F /IM "${APP_EXE}"'
    Pop $0
    nsExec::Exec 'taskkill /F /IM "${CLI_EXE}"'
    Pop $0
FunctionEnd

Function FindOldInstall
  StrCpy $OLD_INSTDIR ""

  ; Per-user installation created by this installer.
  ReadRegStr $0 HKCU "${UNINST_KEY}" "InstallLocation"
  ${If} $0 == ""
    ReadRegStr $0 HKCU "${APP_KEY}" "InstallDir"
  ${EndIf}
  ${If} $0 != ""
    StrCpy $OLD_INSTDIR $0
    Return
  ${EndIf}

  ; Per-machine installation from an older packaging.
  SetRegView 64
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${If} $0 == ""
    ReadRegStr $0 HKLM "${APP_KEY}" "InstallDir"
  ${EndIf}
  SetRegView 32
  ${If} $0 == ""
    ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
    ${If} $0 == ""
      ReadRegStr $0 HKLM "${APP_KEY}" "InstallDir"
    ${EndIf}
  ${EndIf}

  ${If} $0 != ""
    StrCpy $OLD_INSTDIR $0
  ${EndIf}
FunctionEnd

Function UninstallOldVersion
  Call FindOldInstall
  ${If} $OLD_INSTDIR == ""
    Return
  ${EndIf}
  ${IfNot} ${FileExists} "$OLD_INSTDIR\uninstall.exe"
    Return
  ${EndIf}

  DetailPrint "$(UninstallingOld) $OLD_INSTDIR"
  ExecWait '"$OLD_INSTDIR\uninstall.exe" /S _?=$OLD_INSTDIR' $0
  Delete "$OLD_INSTDIR\uninstall.exe"
  RMDir "$OLD_INSTDIR"
FunctionEnd

; ----- command line integration -------------------------------------------
; PATH is a per-user REG_EXPAND_SZ value; always write it back with
; WriteRegExpandStr so entries such as %USERPROFILE% stay unexpanded.
Function AddToUserPath
  ReadRegStr $MSL_PATH HKCU "Environment" "Path"

  ${StrStr} $MSL_MATCH "$MSL_PATH" "$INSTDIR"
  ${If} $MSL_MATCH != ""
    Return
  ${EndIf}

  ${If} $MSL_PATH == ""
    WriteRegExpandStr HKCU "Environment" "Path" "$INSTDIR"
  ${Else}
    WriteRegExpandStr HKCU "Environment" "Path" "$MSL_PATH;$INSTDIR"
  ${EndIf}

  SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment" /TIMEOUT=5000
FunctionEnd

Function un.RemoveFromUserPath
  ReadRegStr $MSL_PATH HKCU "Environment" "Path"
  ${If} $MSL_PATH == ""
    Return
  ${EndIf}

  ; Remove the entry whatever shape it has been edited into.
  ${UnStrRep} $MSL_TMP "$MSL_PATH" ';"$INSTDIR"' ""
  ${UnStrRep} $MSL_TMP "$MSL_TMP" '"$INSTDIR";' ""
  ${UnStrRep} $MSL_TMP "$MSL_TMP" '"$INSTDIR"' ""
  ${UnStrRep} $MSL_TMP "$MSL_TMP" ";$INSTDIR" ""
  ${UnStrRep} $MSL_TMP "$MSL_TMP" "$INSTDIR;" ""
  ${UnStrRep} $MSL_TMP "$MSL_TMP" "$INSTDIR" ""

  ${If} $MSL_TMP != "$MSL_PATH"
    WriteRegExpandStr HKCU "Environment" "Path" "$MSL_TMP"
    SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment" /TIMEOUT=5000
  ${EndIf}
FunctionEnd

; ----- install ------------------------------------------------------------
Section "-Prepare"
  Call CheckRunningApps
  Call UninstallOldVersion
SectionEnd

Section "$(SecCore)" SEC_CORE
  SectionIn RO

  SetOutPath "$INSTDIR"
  ; Never ship user data or debug symbols from the build output.
  File /r /x "Mods" /x "ModSources" /x "logs" /x "export" /x "Settings.json" /x "*.pdb" "${PUBLISH_DIR}\*.*"

  ; Mods, mod sources, logs and Settings.json belong to the user data folder, not to $INSTDIR.
  CreateDirectory "${USER_DATA_DIR}"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk" "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${APP_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${APP_KEY}" "Version" "${APP_VERSION}"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "$(SecDesktop)" SEC_DESKTOP
  CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

Section "$(SecCli)" SEC_CLI
  ; The CLI ships next to the GUI, so PATH only needs the install folder. The
  ; shim is generated here (not published) so it always resolves to $INSTDIR.
  FileOpen $0 "$INSTDIR\msl.cmd" w
  FileWrite $0 '@echo off$\r$\n'
  FileWrite $0 '"%~dp0ModShardLauncher.CLI.exe" %*$\r$\n'
  FileClose $0

  Call AddToUserPath
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CORE} "$(SecCoreDesc)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} "$(SecDesktopDesc)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CLI} "$(SecCliDesc)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ----- uninstall ----------------------------------------------------------
Section "Uninstall"
  SetShellVarContext current

  ; Close running instances before touching files.
  nsExec::Exec 'taskkill /F /IM "${APP_EXE}"'
  Pop $0
  nsExec::Exec 'taskkill /F /IM "${CLI_EXE}"'
  Pop $0

  ; Legacy services / firewall rules (no-op when absent).
  nsExec::Exec 'sc.exe stop "${SERVICE_NAME}"'
  Pop $0
  nsExec::Exec 'sc.exe delete "${SERVICE_NAME}"'
  Pop $0
  nsExec::Exec 'netsh advfirewall firewall delete rule name="${FIREWALL_RULE}"'
  Pop $0
  nsExec::Exec 'netsh advfirewall firewall delete rule name="${FIREWALL_RULE} (Mod Interface)"'
  Pop $0

  ; Undo the command line integration before the files are removed.
  Call un.RemoveFromUserPath
  Delete "$INSTDIR\msl.cmd"

  Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"

  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "${APP_KEY}"
  DeleteRegKey /ifempty HKCU "Software\ModShardTeam"

  ; Silent upgrades keep user data; interactive uninstall asks.
  IfSilent keepData
  MessageBox MB_YESNO|MB_ICONQUESTION "$(KeepUserData)" IDYES keepData

  RMDir /r "$INSTDIR"
  RMDir /r "${USER_DATA_DIR}"
  Goto cleanupDone

  keepData:
    ; Remove every application file/folder, keep user data. The legacy entries cover an
    ; installation that still has the pre-1.0 portable layout in $INSTDIR.
    FindFirst $R0 $R1 "$INSTDIR\*.*"
    keepLoop:
      StrCmp $R1 "" keepDone
      StrCmp $R1 "." keepNext
      StrCmp $R1 ".." keepNext
      StrCmp $R1 "Mods" keepNext
      StrCmp $R1 "ModSources" keepNext
      StrCmp $R1 "logs" keepNext
      StrCmp $R1 "Settings.json" keepNext
      Delete "$INSTDIR\$R1"
      RMDir /r "$INSTDIR\$R1"
      keepNext:
      FindNext $R0 $R1
      Goto keepLoop
    keepDone:
      FindClose $R0
      RMDir "$INSTDIR"

  cleanupDone:
SectionEnd
