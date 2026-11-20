# ==============================================================================
# FluentUninstaller Release & Installer Build Script
# ==============================================================================
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot "dist"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Building Fluent Uninstaller Release & Setup Installer" -ForegroundColor Cyan
Write-Host "  Output Directory: $OutputDir" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Prepare directories
$distAppDir = Join-Path $OutputDir "FluentUninstaller"
$setupProjectDir = Join-Path $PSScriptRoot "src\FluentUninstaller.Setup"
$payloadZip = Join-Path $setupProjectDir "payload.zip"

if (Test-Path $distAppDir) {
    Remove-Item -Path $distAppDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distAppDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 2. Publish Main UI & CLI Apps
Write-Host "`n[1/5] Publishing FluentUninstaller.UI ($Configuration)..." -ForegroundColor Yellow
dotnet publish (Join-Path $PSScriptRoot "src\FluentUninstaller.UI\FluentUninstaller.UI.csproj") -c $Configuration -o $distAppDir --nologo -v q

Write-Host "[2/5] Publishing FluentUninstaller.CLI ($Configuration)..." -ForegroundColor Yellow
dotnet publish (Join-Path $PSScriptRoot "src\FluentUninstaller.CLI\FluentUninstaller.CLI.csproj") -c $Configuration -o $distAppDir --nologo -v q

# 3. Authenticode sign binaries in distribution folder
Write-Host "`n[3/5] Signing binaries with Developer Certificate..." -ForegroundColor Yellow
$certThumbprint = "F35AB0961BABB0D8EBADC53FBD12A6D6390D189B"
$cert = Get-Item "Cert:\CurrentUser\My\$certThumbprint" -ErrorAction SilentlyContinue

if ($cert) {
    Get-ChildItem -Path $distAppDir -Include "*.dll","*.exe" -Recurse | ForEach-Object {
        Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $cert | Out-Null
    }
    Write-Host "  Signed files in $distAppDir" -ForegroundColor Green
} else {
    Write-Warning "Developer certificate not found in Cert:\CurrentUser\My. Skipping signing."
}

# 4. Pack into payload.zip for the Setup executable
Write-Host "`n[4/5] Packing payload.zip..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path "$distAppDir\*" -DestinationPath $payloadZip -Force
$zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
Write-Host "  Created payload.zip ($zipSizeMb MB)" -ForegroundColor Green

# 5. Build & Publish Standalone Setup Executable
Write-Host "`n[5/5] Building Standalone Setup Executable..." -ForegroundColor Yellow
$tempInstallerDir = Join-Path $OutputDir "temp_installer"
if (Test-Path $tempInstallerDir) { Remove-Item $tempInstallerDir -Recurse -Force }

dotnet publish (Join-Path $setupProjectDir "FluentUninstaller.Setup.csproj") -c $Configuration -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o $tempInstallerDir --nologo -v q

$finalSetupExe = Join-Path $OutputDir "FluentUninstaller-Setup.exe"
Copy-Item -Path (Join-Path $tempInstallerDir "FluentUninstaller.Setup.exe") -Destination $finalSetupExe -Force
Remove-Item -Path $tempInstallerDir -Recurse -Force

if ($cert) {
    Set-AuthenticodeSignature -FilePath $finalSetupExe -Certificate $cert | Out-Null
    Write-Host "  Signed $finalSetupExe" -ForegroundColor Green
}

$hash = (Get-FileHash -Path $finalSetupExe -Algorithm SHA256).Hash
$fileSizeMb = [math]::Round((Get-Item $finalSetupExe).Length / 1MB, 2)

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  BUILD SUCCESSFUL!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Installer: $finalSetupExe"
Write-Host "Size:      $fileSizeMb MB"
Write-Host "SHA256:    $hash"
Write-Host ""
