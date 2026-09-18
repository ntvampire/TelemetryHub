# Telemetry Hub (WinUI 3) CLI Runner
param(
    [switch]$RunWorker = $false,
    [switch]$Build = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Telemetry Hub (WinUI 3) CLI Runner ===" -ForegroundColor Cyan

$winUiProj = Join-Path $PSScriptRoot "src\UI.WinUI\UI.WinUI.csproj"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"

if ($Build) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error ".NET SDK not found in PATH! Install .NET 8 SDK."
        exit 1
    }

    Write-Host "1. Building WinUI 3 (Debug win-x64)..." -ForegroundColor Yellow
    dotnet build $winUiProj -c Debug -r win-x64 -p:Platform=x64

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for WinUI project."
        exit $LASTEXITCODE
    }

    Write-Host "2. Building Worker Service..." -ForegroundColor Yellow
    dotnet build $workerProj -c Debug -r win-x64 -p:Platform=x64
}

if ($RunWorker) {
    Write-Host "Starting Service.Worker..." -ForegroundColor Green
    $workerExe = Join-Path $PSScriptRoot "src\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe"
    if (Test-Path $workerExe) {
        Start-Process $workerExe
    } else {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$workerProj`" -c Debug -r win-x64"
    }
}

Write-Host "Starting WinUI 3 Application..." -ForegroundColor Green
$candidateExes = Get-ChildItem (Join-Path $PSScriptRoot "src\UI.WinUI\bin") -Recurse -Filter "KsitalTelemetryHub.UI.WinUI.exe" -ErrorAction SilentlyContinue |
                 Sort-Object LastWriteTime -Descending

if ($candidateExes -and $candidateExes.Count -gt 0) {
    $latest = $candidateExes[0]
    Write-Host "Launching latest build ($($latest.LastWriteTime)): $($latest.FullName)" -ForegroundColor DarkGray
    Start-Process -FilePath $latest.FullName -WorkingDirectory $latest.DirectoryName
} else {
    Write-Error "KsitalTelemetryHub.UI.WinUI.exe not found. Please build the project: dotnet build -c Debug"
    exit 1
}

Write-Host "Application launched successfully!" -ForegroundColor Cyan
