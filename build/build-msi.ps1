<#
.SYNOPSIS
    Builds the JobTracker MSI installer.

.DESCRIPTION
    1. Publishes the app as self-contained single-file (win-x64)
    2. Cleans build artifacts from publish output
    3. Builds the WiX installer project to produce the MSI

.PARAMETER Runtime
    Target runtime. Default: win-x64. Use win-x86 for 32-bit.

.PARAMETER Configuration
    Build configuration. Default: Release.

.EXAMPLE
    .\build\build-msi.ps1
    .\build\build-msi.ps1 -Runtime win-x86
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$PublishDir = Join-Path $RepoRoot "bin\installer-publish-$Runtime"
$InstallerDir = Join-Path $RepoRoot "Installer"
$OutputDir = Join-Path $RepoRoot "Installer\bin\$Configuration"

Write-Host "=== JobTracker MSI Build ===" -ForegroundColor Cyan
Write-Host "Runtime:   $Runtime"
Write-Host "Config:    $Configuration"
Write-Host "PublishTo: $PublishDir"
Write-Host ""

# Step 1: Publish self-contained
Write-Host "--- Publishing self-contained app ---" -ForegroundColor Yellow
dotnet publish "$RepoRoot\JobTracker.csproj" `
    -c $Configuration `
    --self-contained `
    -r $Runtime `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:CreatePortableZip=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Step 2: Clean build artifacts that shouldn't be in the installer
$RemoveDirs = @("Data", "BuildHost-net472", "BuildHost-netcore", "BrowserExtensions")
foreach ($dir in $RemoveDirs) {
    $path = Join-Path $PublishDir $dir
    if (Test-Path $path) {
        Write-Host "  Removing $dir ..."
        Remove-Item -Recurse -Force $path
    }
}
# Remove PDB files
Get-ChildItem -Path $PublishDir -Filter "*.pdb" -Recurse | Remove-Item -Force
# Remove dotnet-tools.json (not needed at runtime)
$toolsJson = Join-Path $PublishDir "dotnet-tools.json"
if (Test-Path $toolsJson) { Remove-Item -Force $toolsJson }

Write-Host ""

# Step 3: Build the MSI
$Platform = if ($Runtime -match "x86") { "x86" } else { "x64" }
Write-Host "--- Building MSI installer ($Platform) ---" -ForegroundColor Yellow
dotnet build "$InstallerDir\Installer.wixproj" `
    -c $Configuration `
    -p:PublishDir="$PublishDir\" `
    -p:InstallerPlatform=$Platform

if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }

# Step 4: Copy MSI to Installer output
$msi = Get-ChildItem -Path $OutputDir -Filter "*.msi" -Recurse | Select-Object -First 1
if ($msi) {
    $dest = Join-Path $RepoRoot "Installer\JobTracker-Setup-$Runtime.msi"
    Copy-Item $msi.FullName $dest -Force
    Write-Host ""
    Write-Host "=== MSI created: $dest ===" -ForegroundColor Green
    Write-Host "  Size: $([math]::Round($msi.Length / 1MB, 1)) MB"
} else {
    Write-Host "WARNING: No MSI found in output" -ForegroundColor Red
}
