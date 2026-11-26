; RadioSure offline installer.
; Ships the self-contained build (the .NET runtime is embedded), so it installs and runs
; with no internet connection and no prerequisites. Per-user install: no admin needed.
#define AppVersion "1.0.0"
#define SourceDir "..\dist\publish-sc"

[Setup]
AppId={{B7E4A1C2-5D3F-4A8E-9C1D-RadioSure01}
AppName=RadioSure
AppVersion={#AppVersion}
AppVerName=RadioSure {#AppVersion}
AppPublisher=Farhadzadeai
DefaultDirName={localappdata}\Programs\RadioSure
DefaultGroupName=RadioSure
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=RadioSure-Setup-{#AppVersion}-offline
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=classic
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\RadioSure.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"


[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\RadioSure"; Filename: "{app}\RadioSure.exe"
Name: "{group}\Uninstall RadioSure"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RadioSure"; Filename: "{app}\RadioSure.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RadioSure.exe"; Description: "{cm:LaunchProgram,RadioSure}"; Flags: nowait postinstall skipifsilent
