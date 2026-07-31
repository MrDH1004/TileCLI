; TileCLI installer (Inno Setup 6)
; Per-user install (no admin) so the app's self-update can swap its own exe
; without elevation. Compile with:  ISCC.exe installer\TileCLI.iss
; The published single-file exe must exist first (dotnet publish -c Release).

#define AppName      "TileCLI"
#define AppPublisher "MrDH1004"
#define AppURL       "https://github.com/MrDH1004/TileCLI"
#define AppExe       "TileCLI.exe"
#define SourceExe    "..\src\TileCLI\bin\Release\net9.0-windows\win-x64\publish\TileCLI.exe"
#define AppIcon      "..\src\TileCLI\app.ico"

; Version is read from the built exe so the installer always matches the payload.
#ifexist SourceExe
  #define AppVersion GetFileVersion(SourceExe)
#else
  #error Published exe not found. Run: dotnet publish src\TileCLI\TileCLI.csproj -c Release
#endif

[Setup]
; Stable AppId (do not change) so upgrades replace the same install.
AppId={{A9F3C1B7-4E2D-4C8A-9B6F-1D3E5A7C9B2F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}

; Per-user, no elevation. Install under %LocalAppData%\Programs\TileCLI.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

OutputDir=Output
OutputBaseFilename=TileCLI-Setup-{#AppVersion}
SetupIconFile={#AppIcon}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Close a running TileCLI before installing/upgrading, then restart it after.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                      Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
