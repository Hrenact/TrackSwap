#ifndef TrackSwapVersion
  #define TrackSwapVersion "v009"
#endif
#ifndef SourceDir
  #error SourceDir must point to a complete TrackSwap release directory.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define TrackSwapAppId "{{6D81BA19-79CE-4A1E-B957-7388879635D4}"

[Setup]
AppId={#TrackSwapAppId}
AppName=TrackSwap
AppVersion={#TrackSwapVersion}
AppPublisher=Hrenact
AppPublisherURL=https://github.com/Hrenact/TrackSwap
AppSupportURL=https://github.com/Hrenact/TrackSwap/issues
AppUpdatesURL=https://github.com/Hrenact/TrackSwap/releases
DefaultDirName={localappdata}\Programs\TrackSwap
DefaultGroupName=TrackSwap
DisableProgramGroupPage=yes
DisableReadyPage=yes
LicenseFile={#SourceDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=TrackSwap-{#TrackSwapVersion}-setup-win-x64
SetupIconFile=..\src\TrackSwap\Assets\TrackSwap.ico
UninstallDisplayIcon={app}\TrackSwap.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex=Local\TrackSwap.UI.v1,Local\TrackSwap.Runtime.v1
MinVersion=10.0.17763
VersionInfoDescription=TrackSwap 安装程序
VersionInfoCompany=Hrenact
VersionInfoProductName=TrackSwap
VersionInfoCopyright=Copyright (C) 2026 Hrenact

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\TrackSwap"; Filename: "{app}\TrackSwap.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\TrackSwap"; Filename: "{app}\TrackSwap.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TrackSwap.exe"; Description: "打开 TrackSwap"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TrackSwap"

[Code]
function PowerShellPath(): String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function SteamVrIsRunning(): Boolean;
var
  ResultCode: Integer;
  Parameters: String;
begin
  Parameters := '-NoProfile -NonInteractive -Command "' +
    '$names=@(''vrserver'',''vrmonitor'',''vrcompositor'');' +
    'if($names|%%{Get-Process -Name $_ -ErrorAction SilentlyContinue}|Select-Object -First 1){exit 1};exit 0"';
  if not Exec(PowerShellPath(), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := True;
    exit;
  end;
  Result := ResultCode <> 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if SteamVrIsRunning() then
    Result := 'SteamVR 仍在运行。请完全退出 SteamVR，然后重新开始安装。';
end;

function InitializeUninstall(): Boolean;
begin
  if SteamVrIsRunning() then
  begin
    MsgBox('SteamVR 仍在运行。请完全退出 SteamVR 后再卸载 TrackSwap。', mbError, MB_OK);
    Result := False;
    exit;
  end;
  Result := True;
end;

function RunPowerShellScript(const ScriptPath, Arguments: String): Boolean;
var
  ResultCode: Integer;
  Parameters: String;
begin
  Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ScriptPath + '" ' + Arguments;
  Result := Exec(
    PowerShellPath(),
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ManifestArguments: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not RunPowerShellScript(
      ExpandConstant('{app}\scripts\Install-Driver.ps1'),
      '-DriverPath "' + ExpandConstant('{app}\driver\trackswap') + '" -ReplaceExisting') then
      RaiseException('TrackSwap 驱动注册失败。请确认 SteamVR 已完全退出，并检查安装日志。');

    ManifestArguments := '-Mode Install -ManifestPath "' +
      ExpandConstant('{app}\TrackSwap.vrmanifest') + '" -JsonLibraryPath "' +
      ExpandConstant('{app}\Newtonsoft.Json.dll') + '"';
    if not RunPowerShellScript(
      ExpandConstant('{app}\scripts\Manage-SteamVrRegistration.ps1'),
      ManifestArguments) then
      RaiseException('TrackSwap 的 SteamVR 应用信息注册失败。请检查安装日志。');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ManifestArguments: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ManifestArguments := '-Mode Uninstall -ManifestPath "' +
      ExpandConstant('{app}\TrackSwap.vrmanifest') + '" -JsonLibraryPath "' +
      ExpandConstant('{app}\Newtonsoft.Json.dll') + '" -PurgeUserData';
    if not RunPowerShellScript(
      ExpandConstant('{app}\scripts\Manage-SteamVrRegistration.ps1'),
      ManifestArguments) then
      RaiseException('无法清理 TrackSwap 的 SteamVR 配置。卸载已停止，配置仍被保留。');

    if not RunPowerShellScript(
      ExpandConstant('{app}\scripts\Uninstall-Driver.ps1'),
      '') then
      RaiseException('无法注销 TrackSwap 驱动。卸载已停止，驱动仍被保留。');
  end;
end;
