@echo off
chcp 65001 >nul
echo [Ksital Telemetry Hub] Генерация тестовой тревоги...
powershell -ExecutionPolicy Bypass -Command "& { dotnet run --project '%~dp0src\Tools.Simulator\Tools.Simulator.csproj' }"
echo.
pause
