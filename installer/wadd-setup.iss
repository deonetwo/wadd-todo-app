; Wadd ToDo - Inno Setup Installer Script
; Generates Windows x64 Installer executable

#ifndef AppVersion
#define AppVersion "1.5.1"
#endif

#ifndef SourceDir
#define SourceDir "..\dist\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\dist"
#endif

#define AppName "Wadd ToDo"
#define AppPublisher "Lunoit Labs"
#define AppExeName "Wadd-ToDo.exe"
#define AppId "{{9F6D1D54-205C-46C1-90EE-396C2DB0D348}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/deonetwo/wadd-todo-app
AppSupportURL=https://github.com/deonetwo/wadd-todo-app/issues
AppUpdatesURL=https://github.com/deonetwo/wadd-todo-app/releases
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=Wadd-v{#AppVersion}-Windows-x64-Setup
SetupIconFile=..\src\Wadd.UI\Assets\logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
