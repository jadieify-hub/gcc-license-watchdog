#ifndef SourceDir
  #error SourceDir define is required
#endif
#ifndef OutputDir
  #error OutputDir define is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif

#define AppName "GCC License Watchdog"
#define AppExeName "GccLicenseWatchdog.exe"
#define AppIdValue "{{19CA019A-45F1-4BB0-BBEA-060775940E96}"

[Setup]
AppId={#AppIdValue}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=KRS
AppPublisherURL=https://github.com/jadieify-hub/gcc-license-watchdog
AppSupportURL=https://github.com/jadieify-hub/gcc-license-watchdog/issues
AppUpdatesURL=https://github.com/jadieify-hub/gcc-license-watchdog/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=GCC-License-Watchdog-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=KRS
VersionInfoDescription=GCC License Watchdog Setup
VersionInfoProductName=GCC License Watchdog
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=© 2026 KRS
ArchitecturesAllowed=x86compatible x64compatible
CloseApplications=no
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "service-install.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "service-uninstall.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "service-uninstall.ps1"; Flags: dontcopy
Source: "support.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\appsettings.json"; DestDir: "{commonappdata}\{#AppName}"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
Name: "{commonappdata}\{#AppName}"; Permissions: admins-full system-full users-readexec
Name: "{commonappdata}\{#AppName}\logs"; Permissions: admins-full system-full users-readexec

[Icons]
Name: "{autoprograms}\{#AppName}\Поддержать разработку"; Filename: "{app}\support.html"
Name: "{autoprograms}\{#AppName}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Code]
var
  DeleteProgramData: Boolean;

function RunPowerShellScript(const ScriptPath, ExtraParameters: String; var ResultCode: Integer): Boolean;
var
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' + ExtraParameters;
  Result := Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  Result := '';
  ExtractTemporaryFile('service-uninstall.ps1');
  ScriptPath := ExpandConstant('{tmp}\service-uninstall.ps1');
  if (not RunPowerShellScript(ScriptPath, '', ResultCode)) or (ResultCode <> 0) then
    Result := Format('Не удалось остановить или удалить предыдущую службу. Код: %d', [ResultCode]);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ScriptPath: String;
  BinaryPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    ScriptPath := ExpandConstant('{app}\tools\service-install.ps1');
    BinaryPath := ExpandConstant('{app}\{#AppExeName}');
    if (not RunPowerShellScript(ScriptPath, '-BinaryPath "' + BinaryPath + '"', ResultCode)) or
       (ResultCode <> 0) then
      RaiseException(Format('Не удалось зарегистрировать или запустить службу {#AppName}. Код: %d', [ResultCode]));
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if UninstallSilent then
    DeleteProgramData := False
  else
    DeleteProgramData := MsgBox(
      'Удалить также настройки и журналы из %ProgramData%\{#AppName}?',
      mbConfirmation,
      MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ScriptPath := ExpandConstant('{app}\tools\service-uninstall.ps1');
    if (not RunPowerShellScript(ScriptPath, '', ResultCode)) or (ResultCode <> 0) then
      RaiseException(Format('Не удалось остановить и удалить службу {#AppName}. Код: %d', [ResultCode]));
  end;

  if (CurUninstallStep = usPostUninstall) and DeleteProgramData then
    DelTree(ExpandConstant('{commonappdata}\{#AppName}'), True, True, True);
end;
