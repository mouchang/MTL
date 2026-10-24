; Mutsumi Launcher NSIS 安装器（自包含，无需 .NET 运行时）
Unicode true

!include "MUI2.nsh"
!include "x64.nsh"

!define APP_NAME "Mutsumi Launcher"
!define APP_VERSION "0.1.0"
!define APP_EXE "MutsumiLauncher.exe"
!define APP_PUBLISHER "Mutsumi"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\dist\MutsumiLauncher-0.1.0-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Mutsumi Launcher"
InstallDirRegKey HKCU "Software\MutsumiLauncher" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!define MUI_ICON "..\src\MutsumiLauncher\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "运行 Mutsumi Launcher"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

Section "Install"
  CreateDirectory "$INSTDIR"

  ; 目录可写性检查：Program Files 这类位置需要管理员权限
  ClearErrors
  FileOpen $0 "$INSTDIR\.mtl-write-test" w
  IfErrors 0 write_ok
    MessageBox MB_ICONEXCLAMATION|MB_OK "无法写入安装目录：$INSTDIR$\r$\n$\r$\n这个位置需要管理员权限。请选择用户目录（默认位置），或关闭安装程序后右键选择以管理员身份运行。"
    Abort
  write_ok:
  FileClose $0
  Delete "$INSTDIR\.mtl-write-test"

  SetOutPath "$INSTDIR"
  File /r "..\dist\publish\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr HKCU "Software\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
  DeleteRegKey HKCU "Software\${APP_NAME}"

  RMDir /r "$INSTDIR"
SectionEnd
