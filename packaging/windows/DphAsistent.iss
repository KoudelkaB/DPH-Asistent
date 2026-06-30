; Inno Setup script pro DPH Asistent.
;
; Očekává self-contained .NET publish výstup pro win-x64 v adresáři zadaném
; přes /DSourceDir (výchozí: ..\..\publish\win-x64 relativně k tomuto skriptu).
; Verzi předejte přes /DAppVersion=0.1.0.
;
; Příklad sestavení:
;   iscc /DAppVersion=0.1.0 /DSourceDir="C:\path\to\publish\win-x64" packaging\windows\DphAsistent.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\publish\win-x64"
#endif

#define AppName "DPH Asistent"
#define AppPublisher "Bohdan Koudelka"
#define AppExeName "DphAsistent.exe"
#define AppUrl "https://github.com/KoudelkaB/DPH-Asistent"

[Setup]
AppId={{21824057-8CF5-4A0C-8CFD-55B8688ECEDA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\DPH Asistent
DefaultGroupName=DPH Asistent
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir=..\..\dist
OutputBaseFilename=DphAsistent-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile=..\..\LICENSE

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DPH Asistent"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,DPH Asistent}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DPH Asistent"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,DPH Asistent}"; Flags: nowait postinstall skipifsilent
