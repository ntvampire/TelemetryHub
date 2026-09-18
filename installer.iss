#define MyAppName "Telemetry Hub"
#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif
#define MyAppPublisher "NTVampire"
#define MyAppExeName "KsitalTelemetryHub.UI.WinUI.exe"

[Setup]
AppId={{D38F2B7E-7F2A-4B2E-8F12-892D98D1C765}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=telemetry-hub-setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupIconFile=src\UI.WinUI\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Создать ярлыки в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; Предоставляем права на запись и изменение в папку приложения встроенной группе "Пользователи"
; Это гарантирует, что оператор сможет запускать программу и писать в рабочую БД telemetry.db без прав администратора (без UAC)
Name: "{app}"; Permissions: users-modify

[Files]
; Копируем все файлы скомпилированного UI и скриптов (исключая рабочие базы)
Source: "dist\ksital-hub\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "telemetry.db*,*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: startmenuicon
Name: "{autoprograms}\{#MyAppName}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
; Настройка и запуск системной службы Windows Service после завершения копирования
Filename: "{sys}\sc.exe"; Parameters: "stop KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create KsitalTelemetryWorker binPath= """"{app}\WorkerService\Service.Worker.exe"""" start= auto DisplayName= ""Telemetry Hub - Сервис сбора данных"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure KsitalTelemetryWorker reset= 60 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start KsitalTelemetryWorker"; Flags: runhidden

; Предложение запустить интерфейс сразу после установки
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Остановка и удаление службы при удалении программы
Filename: "{sys}\sc.exe"; Parameters: "stop KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete KsitalTelemetryWorker"; Flags: runhidden