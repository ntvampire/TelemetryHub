@echo off
chcp 65001 >nul
echo ===================================================
echo   Ksital Telemetry Hub - Generator testovoy trevogi
echo ===================================================
powershell -ExecutionPolicy Bypass -Command "& { dotnet run --project '%~dp0src\Tools.Simulator\Tools.Simulator.csproj' }"
echo.
pause
