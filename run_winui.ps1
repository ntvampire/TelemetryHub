# Скрипт сборки и запуска Ksital Telemetry Hub (WinUI 3) без Visual Studio
param(
    [switch]$RunWorker = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Ksital Telemetry Hub (WinUI 3) CLI Runner ===" -ForegroundColor Cyan

$winUiProj = Join-Path $PSScriptRoot "src\UI.WinUI\UI.WinUI.csproj"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"

# Проверка наличия dotnet
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK не найден в PATH! Установите .NET 8 SDK (winget install Microsoft.DotNet.SDK.8)"
    exit 1
}

Write-Host "1. Восстановление зависимостей и сборка WinUI 3 (Debug win-x64)..." -ForegroundColor Yellow
dotnet build $winUiProj -c Debug -r win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "Ошибка при сборке WinUI проекта."
    exit $LASTEXITCODE
}

Write-Host "2. Сборка фоновой службы Worker..." -ForegroundColor Yellow
dotnet build $workerProj -c Debug -r win-x64

if ($RunWorker) {
    Write-Host "3. Запуск фоновой службы Service.Worker в отдельном окне..." -ForegroundColor Green
    $workerExe = Join-Path $PSScriptRoot "src\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe"
    if (Test-Path $workerExe) {
        Start-Process $workerExe
    } else {
        dotnet run --project $workerProj -c Debug -r win-x64 --no-build &
    }
}

Write-Host "4. Запуск WinUI 3 интерфейса..." -ForegroundColor Green
$winUiExe = Join-Path $PSScriptRoot "src\UI.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe"

if (Test-Path $winUiExe) {
    Start-Process $winUiExe
} else {
    # Запуск через dotnet run
    dotnet run --project $winUiProj -c Debug -r win-x64 --no-build
}

Write-Host "Приложение запущено!" -ForegroundColor Cyan
