; Mutsumi Launcher NSIS 安装器（自包含，启动时申请管理员权限）
Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "StrFunc.nsh"
; 版本号里带 beta 时，快捷方式与开始菜单带 beta 后缀，避免和正式版冲突
${StrLoc}
${UnStrLoc}

!define APP_NAME "Mutsumi Launcher"
!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!define APP_EXE "MutsumiLauncher.exe"
!define APP_PUBLISHER "Mutsumi"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\dist\MutsumiLauncher-${APP_VERSION}-setup.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "Software\${APP_NAME}" "InstallDir"

; 启动安装器时自动申请管理员权限（UAC）
RequestExecutionLevel admin

SetCompressor /SOLID lzma
SetCompressorDictSize 64

!define MUI_ICON "..\src\MutsumiLauncher\Assets\app.ico"
!define MUI_ABORTWARNING

!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchAppAsUser"
!define MUI_FINISHPAGE_RUN_TEXT "运行 Mutsumi Launcher"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
Function MTL_SetDisplayName
  StrCpy $R9 "${APP_NAME}"
  ${StrLoc} $0 "${APP_VERSION}" "beta" ">"
  ${If} $0 != ""
    StrCpy $R9 "${APP_NAME} beta"
  ${EndIf}
FunctionEnd


Function un.MTL_SetDisplayName
  StrCpy $R9 "${APP_NAME}"
  ${UnStrLoc} $0 "${APP_VERSION}" "beta" ">"
  ${If} $0 != ""
    StrCpy $R9 "${APP_NAME} beta"
  ${EndIf}
FunctionEnd

Function .onInit
  Call MTL_SetDisplayName
FunctionEnd

Function un.onInit
  Call un.MTL_SetDisplayName
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "..\dist\publish\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\$R9"
  CreateShortcut "$SMPROGRAMS\$R9\$R9.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\$R9.lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr HKLM "Software\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
  ; 先断开所有引用实例：只删影子目录和联接，外部 .minecraft 一个字节都不碰。
  ; 不先断开的话，RMDir /r 有可能顺着联接删到玩家自己的游戏目录。
  IfFileExists "$INSTDIR\${APP_EXE}" 0 mtl_skip_cleanup
    ExecWait '"$INSTDIR\${APP_EXE}" --cleanup-references'
  mtl_skip_cleanup:

  Delete "$DESKTOP\$R9.lnk"
  Delete "$SMPROGRAMS\$R9\$R9.lnk"
  RMDir "$SMPROGRAMS\$R9"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
  DeleteRegKey HKLM "Software\${APP_NAME}"

  RMDir /r "$INSTDIR"
SectionEnd

; 完成页启动时通过 explorer 以普通用户身份运行，避免游戏继承管理员权限
Function LaunchAppAsUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd