; WebDisplay WinUI 3 installer. Compile with Inno Setup 6.7 or newer.
; Optional ISCC overrides: /DPublishDir="C:\path\to\publish" and
; /DChineseLanguageFile="C:\path\to\ChineseSimplified.isl" and
; /DTraditionalChineseLanguageFile="C:\path\to\ChineseTraditional.isl".
; Configuration and browser data live outside {app}, in
; %LOCALAPPDATA%\WebDisplay, and are deliberately retained on upgrade/uninstall.

#ifndef PublishDir
  #define PublishDir "..\dist\WebDisplay-WinUI3-win-x64"
#endif
#ifndef ChineseLanguageFile
  #define ChineseLanguageFile "languages\ChineseSimplified.isl"
#endif
#ifndef TraditionalChineseLanguageFile
  #define TraditionalChineseLanguageFile "languages\ChineseTraditional.isl"
#endif
#ifndef AppVersion
  #define AppVersion "2.5.0"
#endif
#define AppExeName "WebDisplay.exe"

[Setup]
AppId={{B41E7064-56FA-4AF8-B731-5359E2AD8FD6}
AppName={cm:AppDisplayName}
AppVersion={#AppVersion}
AppVerName={cm:AppDisplayName} {#AppVersion}
AppPublisher=WebDisplay
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=WebDisplay Setup
DefaultDirName={localappdata}\Programs\WebDisplay
DefaultGroupName=WebDisplay
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern dynamic
WizardResizable=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName={cm:AppDisplayName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\WebDisplay\Assets\app.ico
OutputDir=..\dist
OutputBaseFilename=WebDisplay-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "zhcn"; MessagesFile: "{#ChineseLanguageFile}"
Name: "zhtw"; MessagesFile: "{#TraditionalChineseLanguageFile}"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.AppDisplayName=WebDisplay 网页展示器
zhtw.AppDisplayName=WebDisplay 網頁展示器
en.AppDisplayName=WebDisplay
zhcn.DesktopShortcut=创建桌面快捷方式(&D)
zhtw.DesktopShortcut=建立桌面捷徑(&D)
en.DesktopShortcut=Create a desktop shortcut
zhcn.AdditionalShortcuts=快捷方式：
zhtw.AdditionalShortcuts=捷徑：
en.AdditionalShortcuts=Shortcuts:
zhcn.UninstallShortcut=卸载 WebDisplay 网页展示器
zhtw.UninstallShortcut=解除安裝 WebDisplay 網頁展示器
en.UninstallShortcut=Uninstall WebDisplay
zhcn.LaunchApp=启动 WebDisplay 网页展示器
zhtw.LaunchApp=啟動 WebDisplay 網頁展示器
en.LaunchApp=Launch WebDisplay
zhcn.DependencyTitle=准备网页运行组件
zhtw.DependencyTitle=準備網頁執行元件
en.DependencyTitle=Preparing the browser runtime
zhcn.DependencyDescription=首次安装缺少的 WebView2 Runtime 时需要联网。
zhtw.DependencyDescription=首次安裝缺少的 WebView2 Runtime 時需要連線至網路。
en.DependencyDescription=An internet connection is required if WebView2 Runtime is missing.
zhcn.DependencyInstalling=正在安装 Microsoft Edge WebView2 Runtime，请稍候…
zhtw.DependencyInstalling=正在安裝 Microsoft Edge WebView2 Runtime，請稍候…
en.DependencyInstalling=Installing Microsoft Edge WebView2 Runtime. Please wait...
zhcn.DependencyVerifying=正在确认 WebView2 Runtime 已安装…
zhtw.DependencyVerifying=正在確認 WebView2 Runtime 已安裝…
en.DependencyVerifying=Verifying the WebView2 Runtime installation...
zhcn.DependencyFailure=无法完成 WebView2 Runtime 安装。请确认网络可访问微软下载服务，或先从微软网站手动安装 Evergreen WebView2 Runtime，然后重试。安装程序未继续安装 WebDisplay。
zhtw.DependencyFailure=無法完成 WebView2 Runtime 安裝。請確認網路可存取 Microsoft 下載服務，或先從 Microsoft 網站手動安裝 Evergreen WebView2 Runtime，然後重試。安裝程式尚未繼續安裝 WebDisplay。
en.DependencyFailure=WebView2 Runtime could not be installed. Check access to Microsoft download services, or install the Evergreen WebView2 Runtime from Microsoft manually, then retry. WebDisplay installation has not continued.
zhcn.DependencyExitCode=组件安装程序返回代码：
zhtw.DependencyExitCode=元件安裝程式傳回代碼：
en.DependencyExitCode=Runtime installer exit code:
zhcn.DependencyLaunchFailure=无法启动组件安装程序。系统错误：
zhtw.DependencyLaunchFailure=無法啟動元件安裝程式。系統錯誤：
en.DependencyLaunchFailure=The runtime installer could not be started. System error:
zhcn.UninstallNotice=卸载会保留网页展示设置、日志和网页登录数据。Windows 定时重启计划和自动登录设置不会被更改。如果不再需要这些系统功能，请先取消卸载，返回程序设置将它们关闭。
zhtw.UninstallNotice=解除安裝會保留網頁展示設定、記錄和網頁登入資料。Windows 定時重新啟動排程和自動登入設定不會變更。如果不再需要這些系統功能，請先取消解除安裝，返回程式設定將它們關閉。
en.UninstallNotice=Your display settings, logs, and website sign-in data will be preserved. Windows restart schedules and automatic sign-in settings will not be changed. If you no longer need those features, cancel uninstallation and turn them off in WebDisplay settings first.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalShortcuts}"; Flags: unchecked

[Files]
; Extracted only when the official registry detection says the runtime is absent.
; Keep this first so solid compression does not require decoding the application
; payload before PrepareToInstall can access the small bootstrapper.
Source: "dependencies\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WebDisplay"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WebDisplay"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked; Check: IsWebView2Installed

[Code]
const
  WebViewClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupValueName = 'WebDisplay';

var
  DependencyPage: TOutputProgressWizardPage;
  InstallingDependency: Boolean;

function IsInstalledVersion(Version: String): Boolean;
var
  I, Components, DigitsInComponent: Integer;
  HasPositiveDigit: Boolean;
begin
  Version := Trim(Version);
  Result := False;
  Components := 1;
  DigitsInComponent := 0;
  HasPositiveDigit := False;
  if Version = '' then Exit;
  for I := 1 to Length(Version) do
  begin
    if Version[I] = '.' then
    begin
      if DigitsInComponent = 0 then Exit;
      Components := Components + 1;
      DigitsInComponent := 0;
    end
    else
    begin
      if (Version[I] < '0') or (Version[I] > '9') then Exit;
      DigitsInComponent := DigitsInComponent + 1;
      if Version[I] <> '0' then HasPositiveDigit := True;
    end;
  end;
  Result := (Components = 4) and (DigitsInComponent > 0) and HasPositiveDigit;
end;

function HasRuntimeVersion(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Version := '';
  Result := RegQueryStringValue(RootKey, WebViewClientKey, 'pv', Version);
  if Result then Result := IsInstalledVersion(Version);
end;

function IsWebView2Installed: Boolean;
begin
  { Microsoft documents HKLM's 32-bit EdgeUpdate view and HKCU for per-user.
    HKCU\Software is normally shared; both views are checked explicitly. }
  Result := HasRuntimeVersion(HKLM32) or HasRuntimeVersion(HKCU64) or
    HasRuntimeVersion(HKCU32);
end;

procedure InitializeWizard;
begin
  DependencyPage := CreateOutputProgressPage(CustomMessage('DependencyTitle'),
    CustomMessage('DependencyDescription'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, Attempt: Integer;
  Started: Boolean;
begin
  Result := '';
  if IsWebView2Installed then
  begin
    Log('An Evergreen WebView2 Runtime is already installed.');
    Exit;
  end;

  InstallingDependency := True;
  DependencyPage.SetText(CustomMessage('DependencyInstalling'), '');
  DependencyPage.SetProgress(0, 0);
  DependencyPage.Show;
  try
    try
      ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
      ExitCode := 0;
      { Setup's lowest-privilege default runs this without elevation. Microsoft
        chooses per-user/per-machine scope according to its existing updater. }
      Started := Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
        '/silent /install', ExpandConstant('{tmp}'), SW_HIDE,
        ewWaitUntilTerminated, ExitCode);
      if not Started then
      begin
        Result := CustomMessage('DependencyFailure') + #13#10#13#10 +
          CustomMessage('DependencyLaunchFailure') + ' ' + SysErrorMessage(ExitCode);
        Exit;
      end;

      DependencyPage.SetText(CustomMessage('DependencyVerifying'), '');
      { Registry detection is authoritative, including concurrent installations.
        Give Edge Update a short bounded interval to finish registration. }
      for Attempt := 0 to 19 do
      begin
        if IsWebView2Installed then Break;
        Sleep(500);
      end;
      if not IsWebView2Installed then
      begin
        Result := CustomMessage('DependencyFailure') + #13#10#13#10 +
          CustomMessage('DependencyExitCode') + ' ' + IntToStr(ExitCode);
        if ExitCode = 3010 then NeedsRestart := True;
      end
      else
        Log('WebView2 Runtime installation verified. Installer code: ' + IntToStr(ExitCode));
    except
      Result := CustomMessage('DependencyFailure') + #13#10#13#10 + GetExceptionMessage;
    end;
  finally
    DependencyPage.Hide;
    InstallingDependency := False;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  { Do not leave an untracked dependency installer running after setup closes. }
  if InstallingDependency then Cancel := False;
end;

function StartupEntryTargetsThisInstall(Command: String): Boolean;
var
  ExpectedExecutable, Executable: String;
  ClosingQuote: Integer;
begin
  Result := False;
  Command := Trim(Command);
  ExpectedExecutable := ExpandConstant('{app}\{#AppExeName}');
  if Command = '' then Exit;
  if Command[1] = '"' then
  begin
    Delete(Command, 1, 1);
    ClosingQuote := Pos('"', Command);
    if ClosingQuote = 0 then Exit;
    Executable := Copy(Command, 1, ClosingQuote - 1);
  end
  else
  begin
    { The app writes a quoted path. Only accept an exact unquoted legacy path,
      never a prefix such as WebDisplay.exe.other or another installation. }
    Executable := Command;
  end;
  Result := CompareText(Executable, ExpectedExecutable) = 0;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := SuppressibleMsgBox(CustomMessage('UninstallNotice'),
      mbInformation, MB_OKCANCEL, IDOK) = IDOK;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    Command := '';
    if RegQueryStringValue(HKCU, StartupKey, StartupValueName, Command) then
      if StartupEntryTargetsThisInstall(Command) then
      begin
        if RegDeleteValue(HKCU, StartupKey, StartupValueName) then
          Log('Removed the current-user startup entry for this installation.');
      end;
    { No task deletion, Winlogon edits, LSA edits, or user-data deletion here. }
  end;
end;
