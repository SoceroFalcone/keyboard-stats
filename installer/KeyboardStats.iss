; Build with scripts/build-installer.ps1 and Inno Setup 7.
#ifndef PayloadDir
  #error PayloadDir must point to the clean distribution payload.
#endif
#ifndef ReleaseDir
  #error ReleaseDir must point to the distribution output directory.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#define AppName "键盘统计器"
#define AppExe "键盘统计器.exe"

[Setup]
AppId={{6FC3BA63-C891-4EB5-89EF-3CE67955CDD7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription={#AppName}安装程序
VersionInfoProductName={#AppName}
DefaultDirName={localappdata}\Programs\KeyboardStats
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=键盘统计器-{#AppVersion}-Windows-x64-安装包
SetupIconFile=..\src\KeyTrail.Desktop\Assets\KeyTrail.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern light
WizardSizePercent=110
WizardResizable=yes
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=no
ShowLanguageDialog=no
CloseApplications=no
RestartApplications=no
AllowCancelDuringInstall=no
SetupLogging=yes
InfoBeforeFile=安装须知.txt

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[LangOptions]
DialogFontName=Microsoft YaHei UI
DialogFontSize=10

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："
Name: "startmenuicon"; Description: "添加到开始菜单"; GroupDescription: "快捷方式："

[Files]
; Explicit allowlist: no user statistics, settings, development files or backups.
Source: "{#PayloadDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\第三方许可\dotnet-LICENSE.txt"; DestDir: "{app}\第三方许可"; Flags: ignoreversion
Source: "{#PayloadDir}\第三方许可\dotnet-THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\第三方许可"; Flags: ignoreversion
Source: "{#PayloadDir}\第三方许可\windowsdesktop-LICENSE.txt"; DestDir: "{app}\第三方许可"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "启动{#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InstalledAppIsRunning: Boolean;
var
  Locator, Services, Processes: Variant;
  I: Integer;
begin
  Result := False;
  if not FileExists(ExpandConstant('{app}\{#AppExe}')) then
    Exit;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''{#AppExe}''');
    for I := 0 to Processes.Count - 1 do
      if CompareText(String(Processes.ItemIndex(I).ExecutablePath), ExpandConstant('{app}\{#AppExe}')) = 0 then
      begin
        Result := True;
        Exit;
      end;
  except
    Log('Could not verify whether the installed application is running: ' + GetExceptionMessage);
    { Do not replace a potentially running statistics process. }
    Result := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if InstalledAppIsRunning then
    Result := '请先在右下角系统托盘中，右键点击「键盘统计器」并选择「退出」，然后重试安装。关闭统计窗口会继续后台运行。';
end;

function InitializeUninstall: Boolean;
begin
  Result := not InstalledAppIsRunning;
  if not Result then
    SuppressibleMsgBox('请先在右下角系统托盘中，右键点击「键盘统计器」并选择「退出」，再重新卸载。统计数据会保留。', mbInformation, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand, ThisExecutable: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove only an autostart entry belonging to this installation. }
    ThisExecutable := '"' + ExpandConstant('{app}\{#AppExe}') + '"';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KeyTrail', StartupCommand) then
      if CompareText(Copy(StartupCommand, 1, Length(ThisExecutable)), ThisExecutable) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KeyTrail');
  end;
  { No deletion of LocalAppData\KeyTrail: history and preferences survive uninstall. }
end;
