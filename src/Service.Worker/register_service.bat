@echo off
chcp 65001 >nul
set "SERVICE_NAME=KsitalTelemetryWorker"
set "BIN_PATH=%~dp0Service.Worker.exe"

echo [1/3] Остановка и очистка предыдущей регистрации службы...
sc stop %SERVICE_NAME% >nul 2>&1
sc delete %SERVICE_NAME% >nul 2>&1

echo [2/3] Регистрация системной службы Windows %SERVICE_NAME%...
sc create %SERVICE_NAME% binPath= "\"%BIN_PATH%\"" start= auto DisplayName= "Telemetry Hub - Сервис сбора данных"
sc failure %SERVICE_NAME% reset= 60 actions= restart/5000/restart/5000/restart/5000

echo [3/3] Запуск службы %SERVICE_NAME%...
sc start %SERVICE_NAME% >nul 2>&1
