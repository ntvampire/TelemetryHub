@echo off
set "EXE_DIR=%~dp0src\UI.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64"
if exist "%EXE_DIR%\KsitalTelemetryHub.UI.WinUI.exe" (
    start "" /D "%EXE_DIR%" "%EXE_DIR%\KsitalTelemetryHub.UI.WinUI.exe"
    exit /b 0
)

set "EXE_DIR2=%~dp0src\UI.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64"
if exist "%EXE_DIR2%\KsitalTelemetryHub.UI.WinUI.exe" (
    start "" /D "%EXE_DIR2%" "%EXE_DIR2%\KsitalTelemetryHub.UI.WinUI.exe"
    exit /b 0
)

powershell -ExecutionPolicy Bypass -File "%~dp0run_winui.ps1" 
