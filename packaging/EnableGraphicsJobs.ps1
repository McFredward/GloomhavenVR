<#
.SYNOPSIS
    Turns on Unity's threaded render submission for Gloomhaven (GloomhavenVR).

.DESCRIPTION
    Adds two keys to <GameDir>\<GH>_Data\boot.config:
        gfx-enable-gfx-jobs=1
        gfx-enable-native-gfx-jobs=1

    WHY THIS IS A SEPARATE SCRIPT AND NOT JUST PART OF THE MOD. Unity reads
    boot.config during engine startup, before any mod code exists — so the mod
    can write the setting, but only ever for the NEXT start. Running this once,
    before you launch the game, is what removes the "start it twice" step.

    WHY IT EDITS THE FILE INSTEAD OF THE ZIP SHIPPING ONE. boot.config is engine
    build output: it carries version-specific settings the game's own build
    produced. Shipping a copy would replace those with a snapshot from a
    different version, and a later game update would silently have its changes
    reverted. So only these two lines are touched; every other line is preserved
    byte for byte.

    Idempotent — run it as often as you like. The original is copied once to
    boot.config.gloomhavenvr-backup and never overwritten by a later run.

.PARAMETER GamePath
    The Gloomhaven install folder. Defaults to the folder this script sits in,
    which is where it lands if you extracted the mod zip into the game folder.

.PARAMETER Undo
    Restore boot.config from the backup and remove it.
#>
param(
    [string]$GamePath = "",
    [switch]$Undo
)

$ErrorActionPreference = "Stop"

if (-not $GamePath) { $GamePath = $PSScriptRoot }

# The data folder is named after the product (GH_Data on the Steam build) and is
# NOT guaranteed to be, which is why it is discovered rather than hardcoded. The
# Managed check is what distinguishes it from any other *_Data folder.
$dataDir = Get-ChildItem -Path $GamePath -Directory -Filter "*_Data" -ErrorAction SilentlyContinue |
           Where-Object { Test-Path (Join-Path $_.FullName "Managed") } |
           Select-Object -First 1 -ExpandProperty FullName

if (-not $dataDir) {
    Write-Host ""
    Write-Host "Could not find the game's data folder under:" -ForegroundColor Red
    Write-Host "  $GamePath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Put this script in the Gloomhaven folder (the one containing GH.exe)," -ForegroundColor Yellow
    Write-Host "or run it as:  .\EnableGraphicsJobs.ps1 -GamePath ""D:\Games\Gloomhaven""" -ForegroundColor Yellow
    exit 1
}

$bootConfig = Join-Path $dataDir "boot.config"
$backup     = "$bootConfig.gloomhavenvr-backup"

if (-not (Test-Path $bootConfig)) {
    Write-Host "No boot.config in $dataDir - nothing to do." -ForegroundColor Yellow
    Write-Host "The mod will write it on first run instead (then restart the game once)."
    exit 0
}

if ($Undo) {
    if (Test-Path $backup) {
        Copy-Item -LiteralPath $backup -Destination $bootConfig -Force
        Remove-Item -LiteralPath $backup -Force
        Write-Host "Restored - graphics jobs are back to whatever the game shipped with." -ForegroundColor Green
    } else {
        Write-Host "No backup found - boot.config was never modified. Nothing to undo."
    }
    exit 0
}

$keys    = @("gfx-enable-gfx-jobs", "gfx-enable-native-gfx-jobs")
$lines   = [System.Collections.Generic.List[string]](Get-Content -LiteralPath $bootConfig)
$changed = $false

foreach ($key in $keys) {
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $eq = $lines[$i].IndexOf('=')
        if ($eq -gt 0 -and $lines[$i].Substring(0, $eq).Trim() -ieq $key) { $idx = $i; break }
    }
    if ($idx -ge 0) {
        if ($lines[$idx] -ne "$key=1") { $lines[$idx] = "$key=1"; $changed = $true }
    } else {
        $lines.Add("$key=1"); $changed = $true
    }
}

if (-not $changed) {
    Write-Host "Already enabled - boot.config untouched." -ForegroundColor Green
    Write-Host "Start the game normally; the log will say 'Graphics jobs: ON for this session'."
    exit 0
}

if (-not (Test-Path $backup)) { Copy-Item -LiteralPath $bootConfig -Destination $backup }
Set-Content -LiteralPath $bootConfig -Value $lines -Encoding UTF8

Write-Host ""
Write-Host "Done - threaded render submission is on." -ForegroundColor Green
Write-Host "  file:   $bootConfig"
Write-Host "  backup: $backup"
Write-Host ""
Write-Host "Start the game normally. No second start needed." -ForegroundColor Green
Write-Host "BepInEx\LogOutput.log will say: [Core] Graphics jobs: ON for this session"
Write-Host ""
Write-Host "To undo:  .\EnableGraphicsJobs.ps1 -Undo   (or copy the backup over boot.config)"
