# Downloads a portable Godot 4.5.1 CLI into .tools/godot/ (used to build mod PCKs).
# Usage: powershell -ExecutionPolicy Bypass -File scripts/setup_godot_cli.ps1
[CmdletBinding()]
param(
    [string]$Version = "4.5.1"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $RepoRoot ".tools\godot"

if (Test-Path (Join-Path $TargetDir "godot.exe")) {
    Write-Host "Godot CLI already present at $TargetDir" -ForegroundColor Green
    exit 0
}

# Windows x86_64 standard build (headless-capable CLI, no .NET export templates needed for PCKPacker).
$Asset = "Godot_v${Version}_stable_win64.exe.zip"
$Url = "https://github.com/godotengine/godot/releases/download/${Version}-stable/$Asset"
$ZipPath = Join-Path $env:TEMP $Asset

Write-Host "Downloading $Url ..."
Invoke-WebRequest -Uri $Url -OutFile $ZipPath
Expand-Archive -Path $ZipPath -DestinationPath $TargetDir -Force
$Extracted = Get-ChildItem $TargetDir -Filter "Godot_v*_win64.exe" | Select-Object -First 1
Move-Item $Extracted.FullName (Join-Path $TargetDir "godot.exe") -Force
Remove-Item $ZipPath -Force

Write-Host "Godot CLI installed at $(Join-Path $TargetDir 'godot.exe')" -ForegroundColor Green
