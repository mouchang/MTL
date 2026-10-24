; Mutsumi Launcher 便携安装版（不写注册表 / 不建快捷方式，适合受限环境）
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
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=no
DisableWelcomePage=no
PrivilegesRequired=lowest
Uninstallable=no
OutputDir=..\dist
OutputBaseFilename=MutsumiLauncher-{#AppVersion}-setup-portable
SetupIconFile=..\src\MutsumiLauncher\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle=安装程序
SetupWindowTitle=安装 - %1
WelcomeLabel1=欢迎安装 [name]
WelcomeLabel2=安装程序将把 [name/ver] 安装到你选择的文件夹。%n%n这是便携安装模式：不写注册表、不创建快捷方式。
WizardSelectDir=选择安装位置
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=点击“下一步”继续；如需更改位置，点击“浏览”。
WizardReady=准备安装
ReadyLabel1=安装程序已准备好，点击“安装”开始。
ReadyLabel2a=点击“安装”开始安装；如需检查或更改设置，点击“上一步”。
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候…
FinishedHeadingLabel=完成 [name] 安装
FinishedLabel=安装完成，可以直接运行 [name]。
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)
ButtonCancel=取消
ButtonBrowse=浏览(&R)...

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{app}\{#AppExe}"; Description: "运行 Mutsumi Launcher"; Flags: nowait postinstall skipifsilent
