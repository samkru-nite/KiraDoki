; KiraDoki installer (Inno Setup 6). KiraDoki = IsleBar renamed (user 10-12): only what people see says KiraDoki — the
; install folder, data folder, registry keys and the `islebar` command keep IsleBar.
; Build with scripts\build-installer.ps1, which publishes the app and CLI first.
; Per-user install — no admin rights: the app lives in %LOCALAPPDATA%\IsleBar\app, the CLI that hooks call in ...\IsleBar\bin.
; Settings, logs and state in %LOCALAPPDATA%\IsleBar are kept on uninstall (reinstalling picks them up again).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\build\app"
#endif
#ifndef CliDir
  #define CliDir "..\build\bin"
#endif

[Setup]
LicenseFile=license-page.txt
AppId={{BB6CDAA6-D69E-451F-8CB5-7022618B46F4}
AppName=KiraDoki
AppVersion={#AppVersion}
AppPublisher=KiraDoki
AppComments=Taskbar island for Windows 11, based on IsleBar by ds3owl. Not affiliated with Anthropic or OpenAI.
DefaultDirName={localappdata}\IsleBar\app
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=KiraDoki-Setup-{#AppVersion}
SetupIconFile=..\src\IsleBar.App\Assets\kiradoki.ico
UninstallDisplayIcon={app}\IsleBar.App.exe
UninstallDisplayName=KiraDoki
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=force
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
; Simplified Chinese: Inno Setup's unofficial translation (issrc tag is-6_7_3), kept next to this script
Name: "zh"; MessagesFile: "ChineseSimplified.isl"
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.TaskHooks=Connect Claude Code notifications (working · done · needs an answer)
zh.TaskHooks=连接 Claude Code 通知（工作中 · 完成 · 等你回答）
ko.TaskHooks=Claude Code 알림 연결 (작업 중 · 완료 · 확인 필요)
ja.TaskHooks=Claude Code の通知を接続（作業中・完了・確認が必要）
fr.TaskHooks=Connecter les notifications Claude Code
de.TaskHooks=Claude Code-Benachrichtigungen verbinden
it.TaskHooks=Collega le notifiche di Claude Code
pt.TaskHooks=Conectar notificações do Claude Code
es.TaskHooks=Conectar notificaciones de Claude Code
en.TaskStartup=Start KiraDoki when I sign in
zh.TaskStartup=登录时启动 KiraDoki
ko.TaskStartup=로그인할 때 KiraDoki 시작
ja.TaskStartup=サインイン時に KiraDoki を起動
fr.TaskStartup=Démarrer KiraDoki à l'ouverture de session
de.TaskStartup=KiraDoki bei der Anmeldung starten
it.TaskStartup=Avvia KiraDoki all'accesso
pt.TaskStartup=Iniciar o KiraDoki ao entrar
es.TaskStartup=Iniciar KiraDoki al iniciar sesión

[Tasks]
Name: "startup"; Description: "{cm:TaskStartup}"
; not offered after the person disconnected in IsleBar's settings (reconnect there) — a reinstall used to undo that (review 10-03)
Name: "hooks"; Description: "{cm:TaskHooks}"; Check: not HooksTurnedOff

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#CliDir}\islebar.exe"; DestDir: "{localappdata}\IsleBar\bin"; Flags: ignoreversion
Source: "hooks.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "restore-banners.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
; a way back in after 退出 (quit lives only in Settings now) — the original installer made no shortcut (10-12)
Name: "{userprograms}\KiraDoki"; Filename: "{app}\IsleBar.App.exe"

[InstallDelete]
; files earlier builds shipped and this one no longer does (the OpenAI logo was dropped 10-01 — trademark)
Type: files; Name: "{app}\Assets\codex-blossom-*.svg"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IsleBar"; \
  ValueData: """{app}\IsleBar.App.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; a one-click update (silent, /RELAUNCH=1) must not reconnect hooks the user disconnected in settings (code review 10-02)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\setup\hooks.ps1"" -Apply"; \
  Flags: runhidden waituntilterminated; Tasks: hooks; Check: not IsRelaunch
Filename: "{app}\IsleBar.App.exe"; Flags: nowait postinstall skipifsilent; Description: "{cm:LaunchProgram,KiraDoki}"
; one-click update runs setup silently with /RELAUNCH=1 — start the bar again afterwards (10-01)
Filename: "{app}\IsleBar.App.exe"; Flags: nowait; Check: IsRelaunch

[UninstallRun]
; stop the bar first (the supervisor and the bar both run as IsleBar.App.exe), then undo what it changed outside its folder
Filename: "taskkill.exe"; Parameters: "/F /IM IsleBar.App.exe"; Flags: runhidden; RunOnceId: "StopBar"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\setup\restore-banners.ps1"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "RestoreBanners"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\setup\hooks.ps1"" -Revert"; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveHooks"

[UninstallDelete]
Type: files; Name: "{localappdata}\IsleBar\bin\islebar.exe"

[Code]
// Upgrades: stop the running bar first. Its supervisor would otherwise relaunch the bar from half-copied files while setup
// replaces them (Restart Manager alone can't hold it back) — code review 10-01.
function HooksTurnedOff: Boolean;
var
  Value: Cardinal;
begin
  Result := RegQueryDWordValue(HKCU, 'Software\IsleBar', 'HooksOff', Value) and (Value = 1);
end;

function IsRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM IsleBar.App.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
