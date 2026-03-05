; InsightCast Inno Setup Installer Script
; Requires Inno Setup 6.x - https://jrsoftware.org/isinfo.php
;
; Build with:  ISCC.exe InsightCast.iss
; Or use:      .\build.ps1

#define MyAppName "Insight Training Studio"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "HARMONIC insight"
#define MyAppExeName "InsightCast.exe"
#define MyAppURL "https://github.com/HarmonicInsight/win-app-insight-cast"
#define PublishDir "..\publish"

[Setup]
AppId={{B9E4F5A3-8C2D-4F6B-A07E-3D9F2C4B6E8A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\Output
OutputBaseFilename=InsightCast_Setup_{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupLogging=yes
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=EULA.txt
SetupIconFile=..\InsightCast\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:TaskPinToTaskbar}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[CustomMessages]
japanese.TaskPinToTaskbar=タスクバーにピン留め
english.TaskPinToTaskbar=Pin to Taskbar
japanese.VoicevoxPageTitle=VOICEVOX エンジンの確認
english.VoicevoxPageTitle=VOICEVOX Engine Check
japanese.VoicevoxPageDesc=Insight Training Studio はナレーション音声の生成に VOICEVOX を使用します。
english.VoicevoxPageDesc=Insight Training Studio uses VOICEVOX for narration voice generation.
japanese.VoicevoxExplain=VOICEVOX はテキスト読み上げソフトウェアです。Insight Training Studio の音声生成機能を使うには VOICEVOX のインストールが必要です。（動画生成のみであれば VOICEVOX なしでも利用可能です）
english.VoicevoxExplain=VOICEVOX is a text-to-speech software. You need VOICEVOX installed to use the voice generation feature. (Video generation works without VOICEVOX.)
japanese.VoicevoxFound=VOICEVOX が検出されました。
english.VoicevoxFound=VOICEVOX detected.
japanese.VoicevoxNotFound=VOICEVOX が見つかりません。
english.VoicevoxNotFound=VOICEVOX not found.
japanese.VoicevoxDownload=VOICEVOX 公式サイトを開く (ダウンロード)
english.VoicevoxDownload=Open VOICEVOX website (Download)

[Files]
; Main application (self-contained .NET 8 publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; .icproj file association
Root: HKA; Subkey: "Software\Classes\.icproj"; ValueType: string; ValueName: ""; ValueData: "InsightCast.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\InsightCast.Project"; ValueType: string; ValueName: ""; ValueData: "Insight Training Studio Project"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\InsightCast.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\InsightCast.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch the app after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
// ── Constants ──────────────────────────────────────────────────────
const
  VOICEVOX_URL = 'https://voicevox.hiroshiba.jp/';

// ── Helper: Check if VOICEVOX is installed ─────────────────────────
function IsVoicevoxInstalled: Boolean;
var
  LocalAppData: String;
  ProgramFiles: String;
begin
  Result := False;

  LocalAppData := ExpandConstant('{localappdata}');
  ProgramFiles := ExpandConstant('{autopf}');

  // Check common VOICEVOX installation locations
  if FileExists(LocalAppData + '\Programs\VOICEVOX\VOICEVOX.exe') then
    Result := True
  else if FileExists(ProgramFiles + '\VOICEVOX\VOICEVOX.exe') then
    Result := True
  else if FileExists(LocalAppData + '\Programs\VOICEVOX\vv-engine\run.exe') then
    Result := True
  else if FileExists(ProgramFiles + '\VOICEVOX\vv-engine\run.exe') then
    Result := True
  else if FileExists(ProgramFiles + '\VOICEVOX ENGINE\run.exe') then
    Result := True;
end;

// ── Custom wizard page: VOICEVOX check ─────────────────────────────
var
  VoicevoxPage: TWizardPage;
  VoicevoxStatusLabel: TNewStaticText;
  VoicevoxDescLabel: TNewStaticText;
  VoicevoxDownloadButton: TNewButton;

procedure VoicevoxDownloadClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', VOICEVOX_URL, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure CreateVoicevoxPage;
begin
  VoicevoxPage := CreateCustomPage(
    wpSelectDir,
    CustomMessage('VoicevoxPageTitle'),
    CustomMessage('VoicevoxPageDesc'));

  VoicevoxDescLabel := TNewStaticText.Create(VoicevoxPage);
  VoicevoxDescLabel.Parent := VoicevoxPage.Surface;
  VoicevoxDescLabel.Top := 0;
  VoicevoxDescLabel.Left := 0;
  VoicevoxDescLabel.Width := VoicevoxPage.SurfaceWidth;
  VoicevoxDescLabel.AutoSize := False;
  VoicevoxDescLabel.WordWrap := True;
  VoicevoxDescLabel.Height := 60;
  VoicevoxDescLabel.Caption := CustomMessage('VoicevoxExplain');

  VoicevoxStatusLabel := TNewStaticText.Create(VoicevoxPage);
  VoicevoxStatusLabel.Parent := VoicevoxPage.Surface;
  VoicevoxStatusLabel.Top := 80;
  VoicevoxStatusLabel.Left := 0;
  VoicevoxStatusLabel.Width := VoicevoxPage.SurfaceWidth;
  VoicevoxStatusLabel.AutoSize := False;
  VoicevoxStatusLabel.Height := 30;
  VoicevoxStatusLabel.Font.Size := 11;
  VoicevoxStatusLabel.Font.Style := [fsBold];

  VoicevoxDownloadButton := TNewButton.Create(VoicevoxPage);
  VoicevoxDownloadButton.Parent := VoicevoxPage.Surface;
  VoicevoxDownloadButton.Top := 130;
  VoicevoxDownloadButton.Left := 0;
  VoicevoxDownloadButton.Width := 280;
  VoicevoxDownloadButton.Height := 36;
  VoicevoxDownloadButton.Caption := CustomMessage('VoicevoxDownload');
  VoicevoxDownloadButton.OnClick := @VoicevoxDownloadClick;
end;

procedure UpdateVoicevoxStatus;
begin
  if IsVoicevoxInstalled then
  begin
    VoicevoxStatusLabel.Caption := '✓ ' + CustomMessage('VoicevoxFound');
    VoicevoxStatusLabel.Font.Color := clGreen;
    VoicevoxDownloadButton.Visible := False;
  end
  else
  begin
    VoicevoxStatusLabel.Caption := '✗ ' + CustomMessage('VoicevoxNotFound');
    VoicevoxStatusLabel.Font.Color := $000060FF; // Orange
    VoicevoxDownloadButton.Visible := True;
  end;
end;

// ── Wizard events ──────────────────────────────────────────────────
procedure InitializeWizard;
begin
  CreateVoicevoxPage;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = VoicevoxPage.ID then
    UpdateVoicevoxStatus;
end;

// Allow proceeding even without VOICEVOX
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
end;
