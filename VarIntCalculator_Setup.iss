; ============================================================================
;  Varint Calculator - Inno Setup installer.
;  Built like Firefly's installer (Firefly.iss in the suite): dark title bar, the
;  blue-wave header band, dark pages, a consent tick-box, and a short flow
;  (disclaimer -> folder -> installing -> finished, with Launch).
;  Branding is Elusive Data's: setup\VarintCalculator_Header.bmp and
;  setup\VarintCalculator_Welcome.bmp, made by setup\make_installer_art.py.
;  The app, its shortcuts and this setup program carry the V icon (icon.ico,
;  made by setup\make_app_icon.py).
;
;  Build:  powershell -ExecutionPolicy Bypass -File setup\Build-Installer.ps1
;  (publishes the app self-contained for win-x64, then compiles this script
;  into Installer\VarintCalculator_<version>_x64.exe)
; ============================================================================

#define MyAppName "Varint Calculator"
#define MyAppVersion "4.0.0"
#define MyAppPublisher "Collara Works AB, trading as Elusive Data"
#define MyAppURL "https://www.elusivedata.io"
#define MyAppExeName "VarIntCalculator.exe"
#define MyAppDescription "Varint Calculator Setup"
#define MyAppCopyright "Copyright (C) 2025 Collara Works AB, trading as Elusive Data"

; --- Build guard: refuse to compile without a fresh self-contained publish ---
#define Publish AddBackslash(SourcePath) + "bin\Release\net9.0-windows\win-x64\publish\"
#if !FileExists(Publish + MyAppExeName)
  #error No published app in bin\Release\net9.0-windows\win-x64\publish - run setup\Build-Installer.ps1.
#endif

[Setup]
; 3.0 was installed without an AppId, so Inno used its AppName, "VarInt Calculator".
; Keeping that id makes 4.0 upgrade the existing install in place instead of
; installing a second copy beside it.
AppId=VarInt Calculator
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\VarIntCalculator
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=setup\VarintCalculator_Disclaimer.rtf
OutputDir=Installer
OutputBaseFilename=VarintCalculator_{#MyAppVersion}_x64
SetupIconFile=icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=setup\VarintCalculator_Welcome.bmp
WizardSmallImageFile=setup\VarintCalculator_Header.bmp
WizardImageStretch=yes
; Streamlined flow: Disclaimer -> (Folder) -> Installing -> Finished (Launch)
DisableWelcomePage=yes
DisableReadyPage=yes
DisableProgramGroupPage=yes
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Ticked by default: 3.0 always put the calculator on the desktop.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; 3.0 copied its icon file beside the exe; the icon now lives in the exe itself.
Type: files; Name: "{app}\icon.ico"

[Files]
; The self-contained publish: the app, the .NET runtime it runs on, and the font licenses.
Source: "{#Publish}*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
SetupAppTitle=Setup - {#MyAppName}
SetupWindowTitle=Setup - {#MyAppName} {#MyAppVersion}
BeveledLabel=
StatusExtractFiles=Installing {#MyAppName}...
WizardLicense=Disclaimer
LicenseLabel=Please read the disclaimer before installing {#MyAppName}.
LicenseLabel3=Please read the following disclaimer. You must accept it before installing {#MyAppName}.

[Code]
const
  DWMWA_USE_IMMERSIVE_DARK_MODE     = 20;
  DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
  DWMWA_CAPTION_COLOR               = 35;   // Win11 22000+: title bar background
  DWMWA_TEXT_COLOR                  = 36;   // Win11 22000+: title bar text
  PBM_SETBARCOLOR                   = $0409;
  PBM_SETBKCOLOR                    = $2001;
  PBM_SETSTATE                      = $0410;
  PBST_NORMAL                       = 1;
  // The calculator's Default theme (COLORREF is 0x00BBGGRR).
  clBgDark    = $00231910;  // #101923 navy (window)
  clBgPanel   = $003B291B;  // #1B293B panel navy
  clAccent    = $00F8642B;  // #2B64F8 accent blue
  clTextLight = $00E6DED6;  // light text
  clWhiteX    = $00FFFFFF;

function DwmSetWindowAttribute(hwnd: HWND; dwAttribute: Integer;
  var pvAttribute: Integer; cbAttribute: Integer): Integer;
  external 'DwmSetWindowAttribute@dwmapi.dll stdcall';
function SetWindowTheme(hwnd: HWND; SubAppName: WideString; SubIdList: WideString): Integer;
  external 'SetWindowTheme@uxtheme.dll stdcall';
function SendMessage(hwnd: HWND; Msg: Cardinal; wParam: Longint; lParam: Longint): Longint;
  external 'SendMessageW@user32.dll stdcall';

var
  AcceptCheckBox: TNewCheckBox;
  AcceptLabel: TNewStaticText;
  MemoOrigH: Integer;

procedure SetDarkTitleBar(H: HWND);
var
  V, ColBg, ColTx: Integer;
begin
  V := 1;
  if DwmSetWindowAttribute(H, DWMWA_USE_IMMERSIVE_DARK_MODE, V, SizeOf(V)) <> 0 then
    DwmSetWindowAttribute(H, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, V, SizeOf(V));
  // Keep the caption dark even while the window is active (no accent-colored title bar).
  ColBg := clBgDark;
  DwmSetWindowAttribute(H, DWMWA_CAPTION_COLOR, ColBg, SizeOf(ColBg));
  ColTx := clWhiteX;
  DwmSetWindowAttribute(H, DWMWA_TEXT_COLOR, ColTx, SizeOf(ColTx));
end;

procedure ApplyDarkTheme(AParent: TWinControl);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to AParent.ControlCount - 1 do
  begin
    C := AParent.Controls[I];
    if C is TNewStaticText then
      TNewStaticText(C).Font.Color := clTextLight
    else if C is TLabel then
    begin
      TLabel(C).Font.Color := clTextLight;
      TLabel(C).Transparent := True;
    end
    else if C is TNewCheckBox then
    begin
      TNewCheckBox(C).ParentFont := False;
      TNewCheckBox(C).Font.Color := clWhiteX;
    end
    else if C is TNewRadioButton then
    begin
      TNewRadioButton(C).ParentFont := False;
      TNewRadioButton(C).Font.Color := clWhiteX;
    end
    else if C is TNewCheckListBox then
    begin
      TNewCheckListBox(C).ParentFont := False;
      TNewCheckListBox(C).Font.Color := clWhiteX;
      TNewCheckListBox(C).Color := clBgDark;
    end
    else if C is TRichEditViewer then
      TRichEditViewer(C).Color := clBgPanel
    else if C is TNewEdit then
    begin
      TNewEdit(C).Color := clBgPanel;
      TNewEdit(C).Font.Color := clWhiteX;
    end
    else if C is TEdit then
    begin
      TEdit(C).Color := clBgPanel;
      TEdit(C).Font.Color := clWhiteX;
    end
    else if C is TNewNotebookPage then
      TNewNotebookPage(C).Color := clBgDark
    else if C is TPanel then
      TPanel(C).Color := clBgDark;

    if C is TWinControl then
      ApplyDarkTheme(TWinControl(C));
  end;
end;

procedure StyleHeaderBand;
begin
  // Stretch the wave band across the whole header panel; hide the default titles.
  WizardForm.MainPanel.Color := clBgDark;
  WizardForm.PageNameLabel.Visible := False;
  WizardForm.PageDescriptionLabel.Visible := False;
  WizardForm.WizardSmallBitmapImage.Stretch := True;
  WizardForm.WizardSmallBitmapImage.Left := 0;
  WizardForm.WizardSmallBitmapImage.Top := 0;
  WizardForm.WizardSmallBitmapImage.Width := WizardForm.MainPanel.Width;
  WizardForm.WizardSmallBitmapImage.Height := WizardForm.MainPanel.Height;
end;

procedure StyleProgressBar;
begin
  // Strip the visual style so a custom color takes effect, then paint the accent blue.
  SetWindowTheme(WizardForm.ProgressGauge.Handle, '', '');
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETSTATE, PBST_NORMAL, 0);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR, 0, clBgPanel);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, clAccent);
end;

procedure HideStockGlyphs;
begin
  WizardForm.BeveledLabel.Visible := False;          // etched footer text
  WizardForm.SelectDirBitmapImage.Visible := False;  // white folder icon
end;

procedure AcceptCheckBoxClick(Sender: TObject);
begin
  WizardForm.LicenseAcceptedRadio.Checked := AcceptCheckBox.Checked;
  WizardForm.NextButton.Enabled := AcceptCheckBox.Checked;
end;

procedure AcceptLabelClick(Sender: TObject);
begin
  AcceptCheckBox.Checked := not AcceptCheckBox.Checked;
  AcceptCheckBoxClick(nil);
end;

procedure InitializeWizard;
begin
  SetDarkTitleBar(WizardForm.Handle);

  WizardForm.Color := clBgDark;

  ApplyDarkTheme(WizardForm);
  StyleHeaderBand;
  HideStockGlyphs;

  // Consent tick-box (box only) plus a separate white label for its caption,
  // because themed checkboxes ignore Font.Color for their own caption text.
  AcceptCheckBox := TNewCheckBox.Create(WizardForm);
  AcceptCheckBox.Parent := WizardForm.LicenseMemo.Parent;
  AcceptCheckBox.Width := ScaleX(20);
  AcceptCheckBox.Height := ScaleY(20);
  AcceptCheckBox.Caption := '';
  AcceptCheckBox.Visible := False;
  AcceptCheckBox.OnClick := @AcceptCheckBoxClick;

  AcceptLabel := TNewStaticText.Create(WizardForm);
  AcceptLabel.Parent := WizardForm.LicenseMemo.Parent;
  AcceptLabel.AutoSize := True;
  AcceptLabel.Caption := 'I have read and accept the disclaimer';
  AcceptLabel.Font.Color := clWhiteX;
  AcceptLabel.Visible := False;
  AcceptLabel.OnClick := @AcceptLabelClick;

  WizardForm.LicenseAcceptedRadio.Visible := False;
  WizardForm.LicenseNotAcceptedRadio.Visible := False;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  CbTop: Integer;
begin
  SetDarkTitleBar(WizardForm.Handle);
  StyleHeaderBand;
  HideStockGlyphs;
  ApplyDarkTheme(WizardForm);   // re-theme: task checkboxes and the run list are created lazily

  if CurPageID = wpInstalling then
    StyleProgressBar;

  if CurPageID = wpLicense then
  begin
    if MemoOrigH = 0 then
      MemoOrigH := WizardForm.LicenseMemo.Height;
    WizardForm.LicenseMemo.Height := MemoOrigH - ScaleY(44);
    CbTop := WizardForm.LicenseMemo.Top + WizardForm.LicenseMemo.Height + ScaleY(12);
    AcceptCheckBox.Left := WizardForm.LicenseMemo.Left;
    AcceptCheckBox.Top := CbTop;
    AcceptCheckBox.Visible := True;
    AcceptLabel.Left := AcceptCheckBox.Left + ScaleX(24);
    AcceptLabel.Top := CbTop + ScaleY(1);
    AcceptLabel.Font.Color := clWhiteX;   // re-assert: ApplyDarkTheme set static text grey
    AcceptLabel.Visible := True;
    WizardForm.NextButton.Enabled := AcceptCheckBox.Checked;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StyleProgressBar;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DeleteUserData: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Suppressible, defaulting to No, so a silent uninstall (/VERYSILENT) keeps the
    // user's settings and history instead of blocking on a question nobody can answer.
    DeleteUserData := SuppressibleMsgBox('Do you want to remove your Varint Calculator settings and history?',
                             mbConfirmation, MB_YESNO, IDNO);
    if DeleteUserData = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\Elusive Data\Varint Calculator'), True, True, True);
      // The parent folder goes only if no other Elusive Data tool keeps anything there.
      RemoveDir(ExpandConstant('{userappdata}\Elusive Data'));
    end;
  end;
end;
