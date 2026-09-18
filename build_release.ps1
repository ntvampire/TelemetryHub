param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Build Telemetry Hub v$Version ===" -ForegroundColor Cyan

$distDir = Join-Path $PSScriptRoot "dist\ksital-hub"
$distRoot = Join-Path $PSScriptRoot "dist"

if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
if (Test-Path $distRoot) {
    Get-ChildItem -Path $distRoot -Filter "telemetry-hub-setup-*.exe" -ErrorAction SilentlyContinue | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "1/4. Publish UI.WinUI (win-x64)..." -ForegroundColor Yellow
$uiProj = Join-Path $PSScriptRoot "src\UI.WinUI\UI.WinUI.csproj"
dotnet publish $uiProj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=false -o $distDir

Write-Host "2/4. Publish Service.Worker (win-x64)..." -ForegroundColor Yellow
$workerDir = Join-Path $distDir "WorkerService"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"
dotnet publish $workerProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $workerDir

Write-Host "3/4. Cleanup binaries..." -ForegroundColor Yellow
Get-ChildItem -Path $distDir -Include "telemetry.db*","*.pdb" -Recurse | Remove-Item -Force

Write-Host "4/4. Compiling Installer via Inno Setup..." -ForegroundColor Yellow

# Поиск компилятора Inno Setup
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $isccCandidates) {
    Write-Warning "Inno Setup (ISCC.exe) не найден локально. Сборка установщика пропущена."
    Write-Warning "Установите Inno Setup 6 с сайта jrsoftware.org или winget install JRSoftware.InnoSetup"
} else {
    $iscc = $isccCandidates[0]
    $issFile = Join-Path $PSScriptRoot "installer.iss"
    & $iscc "/DMyAppVersion=$Version" $issFile

    $setupFile = Join-Path $distRoot "telemetry-hub-setup-v$Version.exe"
    if (Test-Path $setupFile) {
        Write-Host "SUCCESS: Installer ready -> $setupFile" -ForegroundColor Green
    }
}