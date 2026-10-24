; Mutsumi Launcher 安装程序（Inno Setup 6）
#define AppName "Mutsumi Launcher"
#define AppVersion "0.1.0"
#define AppPublisher "Mutsumi"
#define AppExe "MutsumiLauncher.exe"

[Setup]
AppId={{7C1F5B2E-3D4A-4B8E-9C21-5A6D8E0F1B42}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=no
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=MutsumiLauncher-{#AppVersion}-setup
SetupIconFile=..\src\MutsumiLauncher\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle=安装程序
SetupWindowTitle=安装 - %1
WelcomeLabel1=欢迎安装 [name]
WelcomeLabel2=安装程序将把 [name/ver] 安装到你的电脑上。%n%n建议关闭其他正在运行的程序后继续。
WizardSelectDir=选择安装位置
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=点击“下一步”继续；如需更改位置，点击“浏览”。
WizardReady=准备安装
ReadyLabel1=安装程序已准备好，点击“安装”开始。
ReadyLabel2a=点击“安装”开始安装；如需检查或更改设置，点击“上一步”。
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候…
FinishedHeadingLabel=完成 [name] 安装
FinishedLabel=安装完成，可以从开始菜单或桌面快捷方式启动 [name]。
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)
ButtonCancel=取消
ButtonBrowse=浏览(&R)...
ButtonYes=是(&Y)
ButtonNo=否(&N)
ConfirmUninstall=确定要卸载 [name] 吗？%n%n版本信息：%1
UninstallAppFullTitle=卸载 [name]

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Mutsumi Launcher"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Mutsumi Launcher"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "运行 Mutsumi Launcher"; Flags: nowait postinstall skipifsilent
