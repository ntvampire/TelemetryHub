#define MyAppName "Telemetry Hub"
#ifndef MyAppVersion
  #define MyAppVersion "2.1.0"
#endif
#define MyAppPublisher "NTVampire"
#define MyAppExeName "KsitalTelemetryHub.UI.Avalonia.exe"

[Setup]
AppId={{E59B3C8A-8A1D-4E2F-9B3C-1F2A3B4C5D6E}
AppName={#MyAppName} (Universal Beta)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=telemetry-hub-win7-11-setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupIconFile=src\UI.Avalonia\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Создать ярлыки в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; Предоставляем права на запись и изменение в папку приложения встроенной группе "Пользователи"
Name: "{app}"; Permissions: users-modify

[Files]
; Копируем файлы универсального дистрибутива (Avalonia + Service.Worker)
Source: "dist\telemetry-hub-avalonia\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "telemetry.db*,*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: startmenuicon
Name: "{autoprograms}\{#MyAppName}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
; Регистрация и гарантированный запуск системной службы Windows Service после установки
Filename: "{sys}\cmd.exe"; Parameters: "/c """"{app}\WorkerService\register_service.bat"""""; Flags: runhidden

; Предложение запустить интерфейс сразу после установки
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Остановка и удаление службы при удалении программы
Filename: "{sys}\cmd.exe"; Parameters: "/c """"{app}\WorkerService\unregister_service.bat"""""; Flags: runhidden
