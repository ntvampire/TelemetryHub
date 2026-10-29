param(
    [string]$Version = "2.2.0"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Build Telemetry Hub v$Version ===" -ForegroundColor Cyan

$distRoot = Join-Path $PSScriptRoot "dist"
$distAvaloniaX64 = Join-Path $distRoot "telemetry-hub-avalonia-x64"
$distAvaloniaX86 = Join-Path $distRoot "telemetry-hub-avalonia-x86"
$distLinux = Join-Path $distRoot "telemetry-hub-linux"

# 0. Очистка каталогов сборки
if (Test-Path $distAvaloniaX64) { Remove-Item $distAvaloniaX64 -Recurse -Force }
if (Test-Path $distAvaloniaX86) { Remove-Item $distAvaloniaX86 -Recurse -Force }
if (Test-Path $distLinux) { Remove-Item $distLinux -Recurse -Force }
if (Test-Path $distRoot) {
    Get-ChildItem -Path $distRoot -Filter "telemetry-hub-*.exe" -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $distRoot -Filter "telemetry-hub-*.tar.gz" -ErrorAction SilentlyContinue | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
}

New-Item -ItemType Directory -Path $distAvaloniaX64 -Force | Out-Null
New-Item -ItemType Directory -Path $distAvaloniaX86 -Force | Out-Null
New-Item -ItemType Directory -Path $distLinux -Force | Out-Null

$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"
$avaloniaProj = Join-Path $PSScriptRoot "src\UI.Avalonia\UI.Avalonia.csproj"

# ==============================================================================
# 1. ПУБЛИКАЦИЯ 64-БИТНОЙ ВЕРСИИ AVALONIA (WINDOWS 7 / 8 / 10 / 11 x64)
# ==============================================================================
Write-Host "1/6. Publish UI.Avalonia & Worker (win-x64)..." -ForegroundColor Yellow
dotnet publish $avaloniaProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $distAvaloniaX64

$workerDirAvaloniaX64 = Join-Path $distAvaloniaX64 "WorkerService"
dotnet publish $workerProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $workerDirAvaloniaX64
Copy-Item (Join-Path $PSScriptRoot "src\Service.Worker\register_service.bat") $workerDirAvaloniaX64 -Force
Copy-Item (Join-Path $PSScriptRoot "src\Service.Worker\unregister_service.bat") $workerDirAvaloniaX64 -Force
$assetsDirAvaloniaX64 = Join-Path $distAvaloniaX64 "Assets"
if (-not (Test-Path $assetsDirAvaloniaX64)) { New-Item -ItemType Directory -Path $assetsDirAvaloniaX64 -Force | Out-Null }
Copy-Item (Join-Path $PSScriptRoot "src\UI.Avalonia\Assets\app.ico") $assetsDirAvaloniaX64 -Force
Get-ChildItem -Path $distAvaloniaX64 -Include "telemetry.db*","*.pdb" -Recurse | Remove-Item -Force

# ==============================================================================
# 2. ПУБЛИКАЦИЯ 32-БИТНОЙ ВЕРСИИ AVALONIA (WINDOWS 7 / 8 / 10 / 11 x86)
# ==============================================================================
Write-Host "2/6. Publish UI.Avalonia & Worker (win-x86 32-bit)..." -ForegroundColor Yellow
dotnet publish $avaloniaProj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o $distAvaloniaX86

$workerDirAvaloniaX86 = Join-Path $distAvaloniaX86 "WorkerService"
dotnet publish $workerProj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o $workerDirAvaloniaX86
Copy-Item (Join-Path $PSScriptRoot "src\Service.Worker\register_service.bat") $workerDirAvaloniaX86 -Force
Copy-Item (Join-Path $PSScriptRoot "src\Service.Worker\unregister_service.bat") $workerDirAvaloniaX86 -Force
$assetsDirAvaloniaX86 = Join-Path $distAvaloniaX86 "Assets"
if (-not (Test-Path $assetsDirAvaloniaX86)) { New-Item -ItemType Directory -Path $assetsDirAvaloniaX86 -Force | Out-Null }
Copy-Item (Join-Path $PSScriptRoot "src\UI.Avalonia\Assets\app.ico") $assetsDirAvaloniaX86 -Force
Get-ChildItem -Path $distAvaloniaX86 -Include "telemetry.db*","*.pdb" -Recurse | Remove-Item -Force

# ==============================================================================
# 3. ПУБЛИКАЦИЯ ДЛЯ LINUX (LINUX-X64)
# ==============================================================================
Write-Host "3/6. Publish UI.Avalonia & Worker (linux-x64)..." -ForegroundColor Yellow
dotnet publish $avaloniaProj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o $distLinux

$workerDirLinux = Join-Path $distLinux "WorkerService"
dotnet publish $workerProj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o $workerDirLinux

# Создаем скрипт запуска run.sh для Linux
$runShContent = @'
#!/bin/bash
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$DIR"
chmod +x ./KsitalTelemetryHub.UI.Avalonia ./WorkerService/Service.Worker
./KsitalTelemetryHub.UI.Avalonia "$@"
'@
Set-Content -Path (Join-Path $distLinux "run.sh") -Value $runShContent -NoNewline
Get-ChildItem -Path $distLinux -Include "telemetry.db*","*.pdb" -Recurse | Remove-Item -Force

# ==============================================================================
# 4. УПАКОВКА LINUX В .tar.gz
# ==============================================================================
Write-Host "4/6. Packaging Linux tar.gz..." -ForegroundColor Yellow
$linuxArchive = Join-Path $distRoot "telemetry-hub-linux-x64-v$Version.tar.gz"
tar -czf $linuxArchive -C $distLinux .
if (Test-Path $linuxArchive) {
    Write-Host "SUCCESS: Linux archive ready -> $linuxArchive" -ForegroundColor Green
}

# ==============================================================================
# 5. ПОДГОТОВКА REDISTRIBUTABLE (VC++ 2015-2022 X86)
# ==============================================================================
Write-Host "5/6. Ensuring Microsoft Visual C++ 2015-2022 Redistributable (x86)..." -ForegroundColor Yellow
$redistDir = Join-Path $PSScriptRoot "redist"
$redistFile = Join-Path $redistDir "vc_redist.x86.exe"
if (-not (Test-Path $redistDir)) {
    New-Item -ItemType Directory -Path $redistDir -Force | Out-Null
}

$sharedRedist = "D:\Projects\Redist\vc_redist.x86.exe"
if (-not (Test-Path $redistFile)) {
    if (Test-Path $sharedRedist) {
        Write-Host "Copying vc_redist.x86.exe from $sharedRedist..." -ForegroundColor Green
        Copy-Item $sharedRedist $redistFile -Force
    } else {
        Write-Host "Downloading vc_redist.x86.exe from Microsoft CDN..." -ForegroundColor Yellow
        $vcUrl = "https://aka.ms/vs/17/release/vc_redist.x86.exe"
        try {
            curl.exe -sSL $vcUrl -o $redistFile
        } catch {
            Invoke-WebRequest -Uri $vcUrl -OutFile $redistFile
        }
        if (Test-Path "D:\Projects") {
            if (-not (Test-Path "D:\Projects\Redist")) {
                New-Item -ItemType Directory -Path "D:\Projects\Redist" -Force | Out-Null
            }
            Copy-Item $redistFile $sharedRedist -Force
        }
    }
}
if (Test-Path $redistFile) {
    Write-Host "SUCCESS: vc_redist.x86.exe ready ($((Get-Item $redistFile).Length) bytes)" -ForegroundColor Green
} else {
    Write-Warning "vc_redist.x86.exe is missing. x86 installer may fail to bundle it."
}

# ==============================================================================
# 6. СБОРКА УСТАНОВЩИКОВ INNO SETUP
# ==============================================================================
Write-Host "6/6. Searching Inno Setup compiler..." -ForegroundColor Yellow
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $isccCandidates) {
    Write-Warning "Inno Setup (ISCC.exe) не найден локально. Сборка exe-установщиков пропущена."
    Write-Warning "Установите Inno Setup 6 с сайта jrsoftware.org или winget install JRSoftware.InnoSetup"
} else {
    $iscc = $isccCandidates[0]

    # 6.1. 64-битный установщик (Win 7-11 x64)
    Write-Host "Compiling 64-bit Windows Setup (Win 7-11 x64)..." -ForegroundColor Yellow
    $issAvaloniaX64 = Join-Path $PSScriptRoot "installer-avalonia-x64.iss"
    & $iscc "/DMyAppVersion=$Version" $issAvaloniaX64
    $setupAvaloniaX64 = Join-Path $distRoot "telemetry-hub-win7-11-x64-setup-v$Version.exe"
    if (Test-Path $setupAvaloniaX64) {
        Write-Host "SUCCESS: 64-bit Windows Setup ready -> $setupAvaloniaX64" -ForegroundColor Green
    }

    # 6.2. 32-битный установщик (Win 7-11 x86)
    Write-Host "Compiling 32-bit Windows Setup (Win 7-11 x86)..." -ForegroundColor Yellow
    $issAvaloniaX86 = Join-Path $PSScriptRoot "installer-avalonia-x86.iss"
    & $iscc "/DMyAppVersion=$Version" $issAvaloniaX86
    $setupAvaloniaX86 = Join-Path $distRoot "telemetry-hub-win7-x86-setup-v$Version.exe"
    if (Test-Path $setupAvaloniaX86) {
        Write-Host "SUCCESS: 32-bit Windows Setup ready -> $setupAvaloniaX86" -ForegroundColor Green
    }
}

Write-Host "=== Build Completed Successfully! ===" -ForegroundColor Green
