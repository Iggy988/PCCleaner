; Inno Setup script for Ultimate PC Cleaner.
; Build with:  .\build.ps1   (tests, publishes the app, then compiles this script)
; or manually: ISCC.exe /DAppVersion=2.0.0 installer\UltimatePCCleaner.iss

#define AppName "Ultimate PC Cleaner"
#define AppExeName "UltimatePCCleaner.exe"
#define AppPublisher "Ultimate PC Cleaner"
#define LegacyExeName "PCCleaner.exe"
#define LegacyName "PC Cleaner"
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
; Keep this GUID forever: it identifies the app for upgrades and uninstall (it is the same as "PC Cleaner" 1.x,
; so installing over an old copy upgrades it in place).
AppId={{F8523D64-7D6B-497E-852A-49B9794FC508}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright © 2026 {#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installing into Program Files needs admin; the app itself runs as the current user and asks for elevation only when needed.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=UltimatePCCleanerSetup-{#AppVersion}
SetupIconFile=..\src\PCCleaner\Assets\app.ico
WizardStyle=modern
WizardImageFile=wizard-164.bmp,wizard-192.bmp,wizard-246.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-64.bmp,wizard-small-83.bmp
WizardImageStretch=no
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoCompany={#AppPublisher}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=yes
RestartApplications=no
; Serbian (first entry) is the default; Setup still preselects the Windows display language when it is one of these.
ShowLanguageDialog=auto
SetupLogging=yes

[Languages]
Name: "serbian"; MessagesFile: "Languages\SerbianLatin.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Leftovers of "PC Cleaner" 1.x when upgrading in place.
Type: files; Name: "{app}\{#LegacyExeName}"
Type: files; Name: "{autoprograms}\{#LegacyName}.lnk"
Type: files; Name: "{autodesktop}\{#LegacyName}.lnk"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Start the app as the signed-in user (not elevated), matching how it normally runs.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
