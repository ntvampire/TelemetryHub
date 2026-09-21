@echo off
chcp 65001 >nul
set "SERVICE_NAME=KsitalTelemetryWorker"

echo Остановка и удаление службы %SERVICE_NAME%...
sc stop %SERVICE_NAME% >nul 2>&1
sc delete %SERVICE_NAME% >nul 2>&1
