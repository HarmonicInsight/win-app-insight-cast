; InsightMovie Inno Setup Installer Script
; Requires Inno Setup 6.x - https://jrsoftware.org/isinfo.php
;
; Build with:  ISCC.exe InsightMovie.iss
; Or use:      .\build.ps1

#define MyAppName "Insight Training Studio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HARMONIC insight"
#define MyAppExeName "InsightCast.exe"
#define MyAppURL "https://github.com/HarmonicInsight/win-app-insight-cast"
#define PublishDir "..\publish"

[Setup]
AppId={{A8D3F4E2-7B1C-4E5A-9F6D-2C8E1B3A5D7F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\Output
OutputBaseFilename=InsightTrainingStudio_Setup_{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupLogging=yes
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=
; Uncomment and set if you have an icon:
; SetupIconFile=..\InsightMovie\Resources\app.ico
; UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成"; GroupDescription: "追加オプション:"; Flags: unchecked
Name: "quicklaunchicon"; Description: "タスクバーにピン留め"; GroupDescription: "追加オプション:"; Flags: unchecked

[Files]
; Main application (self-contained .NET 8 publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} をアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch the app after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} を起動"; Flags: nowait postinstall skipifsilent shellexec

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
    'VOICEVOX エンジンの確認',
    'Insight Training Studio はナレーション音声の生成に VOICEVOX を使用します。');

  VoicevoxDescLabel := TNewStaticText.Create(VoicevoxPage);
  VoicevoxDescLabel.Parent := VoicevoxPage.Surface;
  VoicevoxDescLabel.Top := 0;
  VoicevoxDescLabel.Left := 0;
  VoicevoxDescLabel.Width := VoicevoxPage.SurfaceWidth;
  VoicevoxDescLabel.AutoSize := False;
  VoicevoxDescLabel.WordWrap := True;
  VoicevoxDescLabel.Height := 60;
  VoicevoxDescLabel.Caption :=
    'VOICEVOX はテキスト読み上げソフトウェアです。' + #13#10 +
    'Insight Training Studio の音声生成機能を使うには VOICEVOX のインストールが必要です。' + #13#10 +
    '（動画生成のみであれば VOICEVOX なしでも利用可能です）';

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
  VoicevoxDownloadButton.Caption := 'VOICEVOX 公式サイトを開く (ダウンロード)';
  VoicevoxDownloadButton.OnClick := @VoicevoxDownloadClick;
end;

procedure UpdateVoicevoxStatus;
begin
  if IsVoicevoxInstalled then
  begin
    VoicevoxStatusLabel.Caption := '✓ VOICEVOX が検出されました。';
    VoicevoxStatusLabel.Font.Color := clGreen;
    VoicevoxDownloadButton.Visible := False;
  end
  else
  begin
    VoicevoxStatusLabel.Caption := '✗ VOICEVOX が見つかりません。';
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
