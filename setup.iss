; BlueRelay 安装脚本（Inno Setup 7）
; 自包含单文件 + LZMA 高压缩 → 单安装包，用户下载安装即用，无需 .NET

#define MyAppName "蓝桥音频 BlueRelay"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "yixuemin"
#define MyAppExeName "BlueRelay.exe"

[Setup]
AppId={{63E21C69-1A13-437D-8783-7952A98BB528}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\BlueRelay
DefaultGroupName=BlueRelay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=artifacts\installer
OutputBaseFilename=BlueRelaySetup-v1.0.0-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
Source: "artifacts\publish-portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\蓝桥音频"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\蓝桥音频"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 蓝桥音频"; Flags: nowait postinstall skipifsilent
