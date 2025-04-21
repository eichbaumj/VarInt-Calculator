[Setup]
AppName=VarInt Calculator
AppVersion=3.0.0
AppPublisher=Elusive Data
AppPublisherURL=https://elusivedata.io
DefaultDirName={commonpf}\VarIntCalculator
DefaultGroupName=VarInt Calculator
OutputDir=.
OutputBaseFilename=VarIntCalculator_Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=C:\Users\eichb\VarInt\icon.ico
UninstallDisplayIcon={app}\VarIntCalculator.exe
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

[Files]
Source: "C:\Users\eichb\VarInt\bin\Release\net9.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "C:\Users\eichb\VarInt\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\VarInt Calculator"; Filename: "{app}\VarIntCalculator.exe"; IconFilename: "{app}\VarIntCalculator.exe"
Name: "{commondesktop}\VarInt Calculator"; Filename: "{app}\VarIntCalculator.exe"; IconFilename: "{app}\VarIntCalculator.exe"

[Registry]
; This makes your app show up properly in Programs & Features with icon
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#emit SetupSetting("AppId")}}_is1"; ValueType: string; ValueName: "DisplayIcon"; ValueData: "{app}\VarIntCalculator.exe"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#emit SetupSetting("AppId")}}_is1"; ValueType: string; ValueName: "DisplayName"; ValueData: "VarInt Calculator"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#emit SetupSetting("AppId")}}_is1"; ValueType: string; ValueName: "Publisher"; ValueData: "Elusive Data"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#emit SetupSetting("AppId")}}_is1"; ValueType: string; ValueName: "DisplayVersion"; ValueData: "3.0.0" 