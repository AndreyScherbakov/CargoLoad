; Установщик «Раскладка грузов» (Inno Setup 6)
; Собирается скриптом make-installer.ps1

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#define MyAppName "Раскладка грузов"
#define MyAppExeName "CargoLoader.exe"
#define MyAppPublisher "CargoLoad"

[Setup]
AppId={{8E3C2B71-6E4A-4C3F-9B7A-2D5F1C9A8E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\CargoLoad
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Установка без прав администратора: в Program Files текущего пользователя.
; Пользователь может переключить режим на «для всех» в диалоге UAC.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=out
OutputBaseFilename=CargoLoad-Setup-{#MyAppVersion}
SetupIconFile=..\CargoLoad\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\pkg\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

; Данные пользователя (%LOCALAPPDATA%\CargoLoad) при удалении не затрагиваются.