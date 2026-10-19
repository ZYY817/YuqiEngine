; Yuqi Engine Windows installer. Values are supplied by eng\Publish-Release.ps1.
#ifndef AppVersion
  #define AppVersion "0.1.3-preview"
#endif
#ifndef SourceDir
  #error "SourceDir must point to the verified publish directory."
#endif
#ifndef OutputDir
  #error "OutputDir must point to the installer release directory."
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "YuqiEngine-Setup"
#endif

#define AppName "Yuqi Engine"
#define AppPublisher "YuqiEngine"
#define AppExeName "YuqiEngine.exe"

[Setup]
AppId={{B01BC15D-443A-4D6F-85E9-7A6D7E80E2B7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/ZYY817/YuqiEngine
AppSupportURL=https://github.com/ZYY817/YuqiEngine/issues
AppUpdatesURL=https://github.com/ZYY817/YuqiEngine/releases
DefaultDirName={autopf}\YuqiEngine
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\GameOptimizer.Shell\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "languages\\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
