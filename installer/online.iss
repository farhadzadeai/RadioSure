; RadioSure online installer.
; Small download; checks for the .NET 10 Desktop Runtime and downloads it automatically
; when missing. Per-user install: no administrator rights required.
#define AppVersion "1.0.0"
#define SourceDir "..\dist\publish-fd"

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
OutputBaseFilename=RadioSure-Setup-{#AppVersion}-online
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
Source: "runtime-check.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\RadioSure"; Filename: "{app}\RadioSure.exe"
Name: "{group}\Uninstall RadioSure"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RadioSure"; Filename: "{app}\RadioSure.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RadioSure.exe"; Description: "{cm:LaunchProgram,RadioSure}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (settings, favorites, recordings database) lives in %APPDATA%\RadioSure and is
; intentionally kept on uninstall so reinstalling does not lose anything. Comment out to change.
Type: filesandordirs; Name: "{app}\portable.flag"

[Code]
// Checks whether any .NET 10 (or newer) Windows Desktop Runtime is installed.
function DotNetDesktopRuntimePresent: Boolean;
var
  versions: string;
  rc: Integer;
  output: AnsiString;
  tmpFile: string;
begin
  tmpFile := ExpandConstant('{tmp}\dotnet-runtimes.txt');
  Exec(ExpandConstant('{cmd}'), '/c dotnet --list-runtimes > "' + tmpFile + '" 2>nul', '',
    SW_HIDE, ewWaitUntilTerminated, rc);
  if LoadStringFromFile(tmpFile, output) then
    Result := Pos('Microsoft.WindowsDesktop.App 10.', output) > 0
  else
    Result := False;
end;

function InitializeSetup: Boolean;
var
  rc: Integer;
  answer: Integer;
begin
  Result := True;
  if DotNetDesktopRuntimePresent then exit;

  answer := MsgBox(
    'RadioSure needs the Microsoft .NET 10 Desktop Runtime, which is not installed yet.'#13#10#13#10 +
    'Choose Yes to download and install it now (about 55 MB, internet required). '#13#10 +
    'Choose No to continue anyway - the application will not start until the runtime is installed.',
    mbConfirmation, MB_YESNO);
  if answer <> IDYES then exit;

  ExtractTemporaryFile('runtime-check.ps1');
  Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\runtime-check.ps1') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, rc);
  if rc <> 0 then
    MsgBox('The runtime installation did not complete. RadioSure is still installed, but ' +
      'you may need to install the .NET 10 Desktop Runtime manually from https://dot.net',
      mbInformation, MB_OK);
end;
