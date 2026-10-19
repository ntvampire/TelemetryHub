#define MyAppName "Telemetry Hub"
#ifndef MyAppVersion
  #define MyAppVersion "2.1.1"
#endif
#define MyAppPublisher "NTVampire"
#define MyAppExeName "KsitalTelemetryHub.UI.Avalonia.exe"

[Setup]
AppId={{C72D4E9F-1A5B-4C3D-8E2F-6A0B1C2D3E4F}}
AppName={#MyAppName} (Universal 32-bit)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=telemetry-hub-win7-x86-setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupIconFile=src\UI.Avalonia\Assets\app.ico
UninstallDisplayIcon={app}\Assets\app.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Создать ярлыки в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; Предоставляем права на запись и изменение в папку приложения встроенной группе "Пользователи"
Name: "{app}"; Permissions: users-modify
Name: "{app}\Assets"; Permissions: users-modify

[Files]
; Копируем файлы 32-битного универсального дистрибутива (Avalonia x86 + Service.Worker x86)
Source: "dist\telemetry-hub-avalonia-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "telemetry.db*,*.pdb"
Source: "src\UI.Avalonia\Assets\app.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

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
