; Mutsumi Launcher 安装器（Inno Setup 6.7+）
; 编译：ISCC.exe /DAPP_VERSION=1.1.5 /DAPP_DISPLAY="Mutsumi Launcher" installer/MutsumiLauncher.iss

#ifndef APP_VERSION
  #define APP_VERSION "0.0.0"
#endif

#ifndef APP_DISPLAY
  #define APP_DISPLAY "Mutsumi Launcher"
#endif

#define APP_NAME "Mutsumi Launcher"
#define APP_EXE "MutsumiLauncher.exe"
#define APP_PUBLISHER "Mutsumi"

[Setup]
; 同一 AppId 视为同一产品的升级，不要随意改
AppId={{7C4D2B1E-5A6F-4E39-9B7C-3D8A1F6E2045}
AppName={#APP_NAME}
AppVersion={#APP_VERSION}
AppVerName={#APP_DISPLAY} {#APP_VERSION}
AppPublisher={#APP_PUBLISHER}
DefaultDirName={autopf}\Mutsumi Launcher
DefaultGroupName={#APP_DISPLAY}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=MutsumiLauncher-{#APP_VERSION}-Setup
SetupIconFile=..\src\MutsumiLauncher\Assets\app.ico
UninstallDisplayIcon={app}\{#APP_EXE}
UninstallDisplayName={#APP_DISPLAY}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
VersionInfoProductName={#APP_NAME}
VersionInfoDescription={#APP_DISPLAY} 安装程序
VersionInfoCompany={#APP_PUBLISHER}

[Languages]
Name: "chinesesimp"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#APP_DISPLAY}"; Filename: "{app}\{#APP_EXE}"
Name: "{autodesktop}\{#APP_DISPLAY}"; Filename: "{app}\{#APP_EXE}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#APP_EXE}"; Description: "{cm:LaunchProgram,{#APP_DISPLAY}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 先断开所有引用实例：只删影子目录和联接，外部 .minecraft 一个字节都不碰。
Filename: "{app}\{#APP_EXE}"; Parameters: "--cleanup-references"; Flags: runhidden; RunOnceId: "CleanReferences"

[Code]
// 旧版是 NSIS 装的（写在 HKLM\Software\Mutsumi Launcher），提示一下，避免两份并存
function InitializeSetup(): Boolean;
var
  OldDir: String;
begin
  Result := True;

  if RegQueryStringValue(HKLM, 'Software\Mutsumi Launcher', 'InstallDir', OldDir) then
    MsgBox('检测到旧版（NSIS）安装：' + OldDir + #13#10 +
           '建议先在“设置 - 应用”里卸载旧版，再安装这个版本，避免两份程序同时存在。',
           mbInformation, MB_OK);
end;
