@echo off
if exist "%~dp0src\UI.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe" (
    start "" "%~dp0src\UI.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe"
) else if exist "%~dp0src\UI.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe" (
    start "" "%~dp0src\UI.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe"
) else (
    powershell -ExecutionPolicy Bypass -File "%~dp0run_winui.ps1"
) 
