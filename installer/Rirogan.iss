#define MyAppName "Rirogan"
#define MyAppVersion "1.6.6"
#define MyAppPublisher "Rirogan"
#define MyAppExeName "Rirogan.exe"

[Setup]
AppId={{D9B4E0E6-2CA8-49B5-B292-673988C1B48B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist\installer
OutputBaseFilename=Rirogan-Setup-{#MyAppVersion}-Windows-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\rirogan.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
SetupMutex=Rirogan.Setup.D9B4E0E6
AppMutex=Local\Rirogan.Desktop.D9B4E0E6.Launcher
RestartApplications=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Files]
Source: "..\dist\Rirogan-App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\Rirogan-Launcher\Rirogan.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Rirogan"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Rirogan"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Rirogan"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Dados de login WebView2 ficam preservados por padrão na pasta do usuário.
; Nunca apagar dados de autenticação automaticamente durante o uninstall.
