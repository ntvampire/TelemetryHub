# Telemetry Hub CLI Runner
param(
    [switch]$RunWorker = $false,
    [switch]$Build = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Telemetry Hub CLI Runner ===" -ForegroundColor Cyan

$avaloniaProj = Join-Path $PSScriptRoot "src\UI.Avalonia\UI.Avalonia.csproj"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"

if ($Build) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error ".NET SDK not found in PATH! Install .NET 8 SDK."
        exit 1
    }

    Write-Host "1. Building UI.Avalonia..." -ForegroundColor Yellow
    dotnet build $avaloniaProj -c Debug

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for Avalonia project."
        exit $LASTEXITCODE
    }

    Write-Host "2. Building Worker Service..." -ForegroundColor Yellow
    dotnet build $workerProj -c Debug
}

if ($RunWorker) {
    Write-Host "Starting Service.Worker..." -ForegroundColor Green
    $workerExe = Join-Path $PSScriptRoot "src\Service.Worker\bin\Debug\net8.0\Service.Worker.exe"
    if (Test-Path $workerExe) {
        Start-Process $workerExe
    } else {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$workerProj`" -c Debug"
    }
}

Write-Host "Starting Telemetry Hub UI..." -ForegroundColor Green
$candidateExes = Get-ChildItem (Join-Path $PSScriptRoot "src\UI.Avalonia\bin") -Recurse -Filter "KsitalTelemetryHub.UI.Avalonia.exe" -ErrorAction SilentlyContinue |
                 Sort-Object LastWriteTime -Descending

if ($candidateExes -and $candidateExes.Count -gt 0) {
    $latest = $candidateExes[0]
    Write-Host "Launching latest build ($($latest.LastWriteTime)): $($latest.FullName)" -ForegroundColor DarkGray
    Start-Process -FilePath $latest.FullName -WorkingDirectory $latest.DirectoryName
} else {
    Write-Host "Binary not found, running via dotnet run..." -ForegroundColor Yellow
    dotnet run --project $avaloniaProj -c Debug
}

Write-Host "Application launched successfully!" -ForegroundColor Cyan
