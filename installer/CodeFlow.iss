; CodeFlow CLI - Inno Setup Installer Script
; Press F9 to compile -> installer\dist\CodeFlowSetup.exe

#define AppName "CodeFlow CLI"
#define AppVersion "1.0.0"
#define AppPublisher "CodeFlow"
#define AppURL "https://github.com/PaawanSofTech/CodeFlowHub"
#define AppExeName "codeflow.exe"
#define SourceDir "C:\Users\paawa\Downloads\codeflow-complete\codeflow-complete\installer\publish"
#define OutputDir "C:\Users\paawa\Downloads\codeflow-complete\codeflow-complete\installer\dist"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\CodeFlow\CLI
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=CodeFlowSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "addtopath";   Description: "Add CodeFlow to system &PATH (recommended)"; GroupDescription: "Additional options:"
Name: "desktopicon"; Description: "Create a &desktop shortcut";                 GroupDescription: "Additional options:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CodeFlow CLI";           Filename: "{cmd}"; Parameters: "/k codeflow --help"; WorkingDir: "{app}"
Name: "{group}\Uninstall CodeFlow CLI"; Filename: "{uninstallexe}"
Name: "{commondesktop}\CodeFlow CLI";   Filename: "{cmd}"; Parameters: "/k codeflow --help"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\CodeFlow\CLI"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}";         Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\CodeFlow\CLI"; ValueType: string; ValueName: "Version";    ValueData: "{#AppVersion}"; Flags: uninsdeletekey

[Code]
procedure AddToPath(const Path: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path', Paths) then
    Paths := '';
  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    Exit;
  Paths := Paths + ';' + Path;
  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', Paths);
end;

procedure RemoveFromPath(const Path: string);
var
  Paths: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path', Paths) then
    Exit;
  P := Pos(';' + Uppercase(Path), ';' + Uppercase(Paths));
  if P = 0 then Exit;
  Delete(Paths, P - 1, Length(Path) + 1);
  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', Paths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if IsTaskSelected('addtopath') then
      AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;