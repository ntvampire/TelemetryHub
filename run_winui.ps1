# Ksital Telemetry Hub (WinUI 3) CLI Runner
param(
    [switch]$RunWorker = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Ksital Telemetry Hub (WinUI 3) CLI Runner ===" -ForegroundColor Cyan

$winUiProj = Join-Path $PSScriptRoot "src\UI.WinUI\UI.WinUI.csproj"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK not found in PATH! Install .NET 8 SDK."
    exit 1
}

Write-Host "1. Building WinUI 3 (Debug win-x64)..." -ForegroundColor Yellow
dotnet build $winUiProj -c Debug -r win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed for WinUI project."
    exit $LASTEXITCODE
}

Write-Host "2. Building Worker Service..." -ForegroundColor Yellow
dotnet build $workerProj -c Debug -r win-x64

if ($RunWorker) {
    Write-Host "3. Starting Service.Worker..." -ForegroundColor Green
    $workerExe = Join-Path $PSScriptRoot "src\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe"
    if (Test-Path $workerExe) {
        Start-Process $workerExe
    } else {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$workerProj`" -c Debug -r win-x64"
    }
}

Write-Host "4. Starting WinUI 3 Application..." -ForegroundColor Green
$winUiExe = Join-Path $PSScriptRoot "src\UI.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\KsitalTelemetryHub.UI.WinUI.exe"

if (Test-Path $winUiExe) {
    Start-Process $winUiExe
} else {
    dotnet run --project $winUiProj -c Debug -r win-x64 --no-build
}

Write-Host "Application launched successfully!" -ForegroundColor Cyan
