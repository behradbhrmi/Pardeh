; The Windows installer (Inno Setup 6.6 or newer). scripts\publish.ps1 -Installer builds it from the standalone package:
;   ISCC /DAppVersion=1.2.3 /DSourceDir=<package folder> /DOutputDir=<folder> installer.iss
; It installs for the current user only (no administrator rights), into the same folder as install.ps1, and takes over
; an installation made by that script. /DTestInstall builds a harmless copy for trying the installer: its own identity
; and folder and no Start menu entry. It runs the same upgrade and uninstall code, but on stand-ins under
; HKCU\Software\PardehSetupTest and %APPDATA%\PardehSetupTest, and it never installs or removes the widget.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<package folder>
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifdef TestInstall
  #define AppName "Pardeh Setup Test"
  #define AppGuid "{{6D0B1C52-0E4F-4B7A-9C1E-7E57A11E57A1}"
  #define OutputName "Pardeh-" + AppVersion + "-win-x64-setup-test"
  #define RegBase "Software\PardehSetupTest\"
  #define DataFolder "PardehSetupTest"
#else
  #define AppName "Pardeh"
  ; Never change this id: Windows finds the installed copy by it when upgrading and uninstalling.
  #define AppGuid "{{8F3A2C91-5D47-4E0B-A6B2-1C9D84F7E350}"
  #define OutputName "Pardeh-" + AppVersion + "-win-x64-setup"
  #define RegBase ""
  #define DataFolder "Pardeh"
#endif

#define HasWidget FileExists(AddBackslash(SourceDir) + "Widget\PardehWidget.msix")

[Setup]
AppId={#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pardeh contributors
AppPublisherURL=https://github.com/behradbhrmi/Pardeh
AppSupportURL=https://github.com/behradbhrmi/Pardeh/issues
AppUpdatesURL=https://github.com/behradbhrmi/Pardeh/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
; Windows 11 style wizard that follows the light or dark setting.
WizardStyle=modern dynamic
WizardSmallImageFile=..\docs\img\pardeh-logo-512.png
SetupIconFile=..\src\Pardeh.App\pardeh.ico
UninstallDisplayIcon={app}\Pardeh.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
#if HasWidget
Name: "widget"; Description: "Windows 11 Widgets board widget (asks once for administrator rights to trust its package)"; GroupDescription: "Extras:"; MinVersion: 10.0.22000
#endif

[Files]
; install.ps1 and uninstall.ps1 belong to the zip package; the installer does their work itself.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\install.ps1,\uninstall.ps1"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
#ifndef TestInstall
Name: "{userprograms}\Pardeh"; Filename: "{app}\Pardeh.exe"; Comment: "Pardeh media player"
#endif
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\Pardeh.exe"; Tasks: desktopicon

#ifndef TestInstall
; Not in the test copy: started as Pardeh.exe, it would point the user's file icons at its own folder.
[Run]
Filename: "{app}\Pardeh.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
const
  { Empty in the real installer; a stand-in root in the test one. }
  RegBase = '{#RegBase}';
  ScriptUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Pardeh';

{ Runs Windows PowerShell hidden and waits. False when it could not run or reported failure. }
function RunPowerShell(const Arguments: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass ' + Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure InstallWidget;
begin
  if not WizardIsTaskSelected('widget') then
    exit;
  WizardForm.StatusLabel.Caption := 'Installing the Widgets board widget...';
#ifdef TestInstall
  Log('Test installer: the widget is not installed.');
#else
  if not RunPowerShell('-File "' + ExpandConstant('{app}\install-widget.ps1') + '"') then
    SuppressibleMsgBox('Pardeh is installed, but the Widgets board widget is not.' + #13#10 + #13#10 +
      'To add it, run this setup again and accept the administrator prompt.', mbInformation, MB_OK, IDOK);
#endif
end;

{ Also when this installer carries no widget: an earlier installation may have added it. }
procedure RemoveWidget;
begin
  if not FileExists(ExpandConstant('{app}\Widget\PardehWidget.cer')) then
    exit;
#ifdef TestInstall
  Log('Test uninstaller: the widget is not removed.');
#else
  RunPowerShell('-File "' + ExpandConstant('{app}\uninstall-widget.ps1') + '"');
#endif
end;

{ An open player would keep its files from being deleted: ask its window to close, and end it if it does not. }
procedure CloseRunningApp;
var
  Exe: String;
begin
  Exe := ExpandConstant('{app}\Pardeh.exe');
  StringChangeEx(Exe, '''', '''''', True);
  RunPowerShell('-Command "$p = Get-Process Pardeh -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq ''' + Exe + ''' }; ' +
    'if ($p) { $p | ForEach-Object { [void]$_.CloseMainWindow() }; $p | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue; ' +
    '$p | Where-Object { -not $_.HasExited } | Stop-Process -Force; $p | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue }; exit 0"');
end;

{ An installation made by install.ps1 is now this one: remove its second entry in Settings > Apps and its scripts. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, RegBase + ScriptUninstallKey);
    DeleteFile(ExpandConstant('{app}\install.ps1'));
    DeleteFile(ExpandConstant('{app}\uninstall.ps1'));
    InstallWidget;
  end;
end;

{ What the app registers for the current user: Tools > Options > File Types, and the file icons. }
procedure RemoveFileRegistration;
var
  Names: TArrayOfString;
  I: Integer;
begin
  RegDeleteKeyIncludingSubkeys(HKCU, RegBase + 'Software\Classes\Pardeh.Media');
  RegDeleteKeyIncludingSubkeys(HKCU, RegBase + 'Software\Classes\Applications\Pardeh.exe');
  RegDeleteKeyIncludingSubkeys(HKCU, RegBase + 'Software\Pardeh');
  RegDeleteValue(HKCU, RegBase + 'Software\RegisteredApplications', 'Pardeh');
  if RegGetSubkeyNames(HKCU, RegBase + 'Software\Classes', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 1) = '.' then
        RegDeleteValue(HKCU, RegBase + 'Software\Classes\' + Names[I] + '\OpenWithProgids', 'Pardeh.Media');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApp;
    RemoveWidget;
  end;

  if CurUninstallStep <> usPostUninstall then
    exit;
  RemoveFileRegistration;
  { A widget folder left by an earlier installation is not in this uninstaller's list of files. }
  DelTree(ExpandConstant('{app}\Widget'), True, True, True);
  RemoveDir(ExpandConstant('{app}'));
  { Kept unless the user says otherwise; a silent uninstall always keeps it. }
  Data := ExpandConstant('{userappdata}\{#DataFolder}');
  if DirExists(Data) then
    if SuppressibleMsgBox('Also delete your Pardeh library, playlists, skins and settings?' + #13#10 + #13#10 + Data,
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(Data, True, True, True);
end;
