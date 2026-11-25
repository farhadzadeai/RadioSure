; RadioClassic offline installer.
; Ships the self-contained build (the .NET runtime is embedded), so it installs and runs
; with no internet connection and no prerequisites. Per-user install: no admin needed.
#define AppVersion "1.0.0"
#define SourceDir "..\dist\publish-sc"

[Setup]
AppId={{B7E4A1C2-5D3F-4A8E-9C1D-RADIOCLASSIC01}
AppName=RadioClassic
AppVersion={#AppVersion}
AppVerName=RadioClassic {#AppVersion}
AppPublisher=RadioClassic
DefaultDirName={localappdata}\Programs\RadioClassic
DefaultGroupName=RadioClassic
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=RadioClassic-Setup-{#AppVersion}-offline
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=classic
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\RadioClassic.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"


[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\RadioClassic"; Filename: "{app}\RadioClassic.exe"
Name: "{group}\Uninstall RadioClassic"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RadioClassic"; Filename: "{app}\RadioClassic.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RadioClassic.exe"; Description: "{cm:LaunchProgram,RadioClassic}"; Flags: nowait postinstall skipifsilent
