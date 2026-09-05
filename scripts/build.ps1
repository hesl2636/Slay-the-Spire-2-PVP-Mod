# Build + stage the pvpduel mod (ticket #13 skeleton).
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1              # build + stage (DLL-only)
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Install     # also copy into <game>/mods/
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -PackPck     # also build pvpduel.pck (needs Godot CLI)
#
# Output: build/mods/pvpduel/{pvpduel.json, pvpduel.dll, PvpDuel.Core.dll[, pvpduel.pck]}
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$PackPck,
    [string]$GameDir = "F:\Steam\steamapps\common\Slay the Spire 2",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$ModId = "pvpduel"
$StageDir = Join-Path $RepoRoot "build\mods\$ModId"
$GameBin = Join-Path $RepoRoot "src\PvpDuel\bin\$Configuration\net9.0"

Write-Host "== [pvpduel] build ($Configuration) ==" -ForegroundColor Cyan

# 1. Build the solution (Core + game assembly + tests).
dotnet build "$RepoRoot\PvpDuel.sln" -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

# 2. Restage the mod directory from scratch.
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item $StageDir -ItemType Directory -Force | Out-Null

# Stage payloads: single self-contained mod DLL (Core sources are compiled into it).
Copy-Item (Join-Path $GameBin "$ModId.dll") (Join-Path $StageDir "$ModId.dll")

# Manifest: renamed to <id>.json for install (any *.json under mods/ is scanned).
$Manifest = Get-Content "$RepoRoot\mod_manifest.json" -Raw | ConvertFrom-Json
if ($PackPck) {
    $GodotExe = $env:GODOT_EXE
    if (-not $GodotExe -and (Test-Path "$RepoRoot\.tools\godot\godot.exe")) { $GodotExe = "$RepoRoot\.tools\godot\godot.exe" }
    if (-not $GodotExe) { throw "-PackPck requires Godot 4.5.1 CLI: set `$env:GODOT_EXE or run scripts/setup_godot_cli.ps1." }

    # Pack godot/ resources (namespaced res://pvpduel/...) via a tiny SceneTree script.
    & $GodotExe --headless --path "$RepoRoot" --script (Join-Path $PSScriptRoot "pack_pck.gd") -- "$StageDir\$ModId.pck" "$RepoRoot\mod_manifest.json"
    if ($LASTEXITCODE -ne 0) { throw "PCK packing failed." }
    $Manifest.has_pck = $true
}

# Write the final manifest (payload flags now reflect what actually shipped).
$Manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $StageDir "$ModId.json") -Encoding utf8

# 3. Verify every declared payload exists (silent partial staging = broken mod).
foreach ($flag in @("has_dll", "has_pck")) {
    if ($Manifest.$flag) {
        $payload = Join-Path $StageDir ("$ModId." + $flag.Remove(0, 4).ToLower())
        if (-not (Test-Path $payload)) { throw "Manifest declares $flag but $payload is missing." }
        Write-Host "  payload OK: $payload ($((Get-Item $payload).Length) bytes)"
    }
}

if (-not $Manifest.has_dll -and -not $Manifest.has_pck) { throw "Manifest declares neither payload; the loader would only warn." }

# 4. Optional install into the game's mods directory.
if ($Install) {
    $Target = Join-Path $GameDir "mods\$ModId"
    New-Item $Target -ItemType Directory -Force | Out-Null
    Copy-Item "$StageDir\*" $Target -Force
    Write-Host "== installed to $Target ==" -ForegroundColor Green
}

Write-Host "== done: $StageDir ==" -ForegroundColor Green
