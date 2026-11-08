# Builds Legend and packs it into a single LegendSetup.exe
# Usage: powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 [-Version 1.0.1]

param([string]$Version = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$appOut = Join-Path $dist 'app'
$payload = Join-Path $root 'Installer\payload.zip'
$setupOut = Join-Path $dist 'installer'
$versionArg = if ($Version) { '-p:Version=' + $Version } else { '' }

Write-Host '[*] Cleaning dist...'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host '[*] Publishing Legend (self-contained, win-x64)...'
dotnet publish (Join-Path $root 'Legend.csproj') -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -p:GenerateDocumentationFile=false $versionArg -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'Legend publish failed.' }

Get-ChildItem $appOut -Include *.pdb, *.xml -Recurse | Remove-Item -Force

# update.json tells the app where its release manifest lives
$updateConfig = Join-Path $root 'update-config.json'
if (Test-Path $updateConfig) {
    Copy-Item $updateConfig (Join-Path $appOut 'update.json') -Force
}

Write-Host '[*] Packing payload...'
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path (Join-Path $appOut '*') -DestinationPath $payload -CompressionLevel Optimal

Write-Host '[*] Publishing installer (single self-contained exe)...'
dotnet publish (Join-Path $root 'Installer\LegendSetup.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $setupOut
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

Copy-Item (Join-Path $setupOut 'LegendSetup.exe') (Join-Path $dist 'LegendSetup.exe') -Force
Write-Host ''
Write-Host ('[+] Done: ' + (Join-Path $dist 'LegendSetup.exe'))
Write-Host ('    App folder: ' + $appOut)
