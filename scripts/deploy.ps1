<#
.SYNOPSIS
    Copies built GloomhavenVR artifacts into a Gloomhaven install (BepInEx 5 required).

.DESCRIPTION
    Deploy layout (docs/TESTING-P1.md documents the full picture):

      Plugin       -> <GamePath>\BepInEx\plugins\GloomhavenVR\GloomhavenVR.dll
      RuntimeDeps  -> <GamePath>\BepInEx\plugins\GloomhavenVR\RuntimeDeps\*.dll
      Preloader    -> <GamePath>\BepInEx\patchers\GloomhavenVR\GloomhavenVR.Preload.dll
      Natives      -> <GamePath>\BepInEx\patchers\GloomhavenVR\Natives\*.dll

    The preloader itself installs the natives + UnitySubsystems manifest into
    Gloomhaven_Data\ at game boot - nothing under Gloomhaven_Data is touched here.

    Builds are NOT triggered here; run first:
      dotnet build GloomhavenVR.sln -c Release      (or scripts/build.sh)
      scripts/fetch-natives.sh                       (once, populates libs/Natives)
      scripts/build-runtimedeps.sh                   (populates libs/RuntimeDeps)

.EXAMPLE
    .\scripts\deploy.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\Gloomhaven"

.EXAMPLE
    .\scripts\deploy.ps1 -GamePath D:\Games\Gloomhaven -Configuration Debug
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$GamePath,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path (Join-Path $GamePath "BepInEx"))) {
    Write-Error "No BepInEx folder found in '$GamePath'. Install BepInEx 5.4.23.5 (x64) first - see README."
}

$plugin    = Join-Path $root "src\GloomhavenVR\bin\$Configuration\net472\GloomhavenVR.dll"
$preloader = Join-Path $root "src\GloomhavenVR.Preload\bin\$Configuration\net472\GloomhavenVR.Preload.dll"

foreach ($artifact in @($plugin, $preloader)) {
    if (-not (Test-Path $artifact)) {
        Write-Error "Missing artifact '$artifact'. Build first: dotnet build GloomhavenVR.sln -c $Configuration"
    }
}

$natives     = Get-ChildItem (Join-Path $root "libs\Natives\*.dll") -ErrorAction SilentlyContinue
$runtimeDeps = Get-ChildItem (Join-Path $root "libs\RuntimeDeps\*.dll") -ErrorAction SilentlyContinue

if (-not $natives) {
    Write-Error "libs\Natives is empty. Run scripts/fetch-natives.sh first (see libs/Natives/README.md)."
}
if (-not $runtimeDeps) {
    Write-Error "libs\RuntimeDeps is empty. Run scripts/build-runtimedeps.sh first (see libs/RuntimeDeps/README.md)."
}

$pluginDir      = Join-Path $GamePath "BepInEx\plugins\GloomhavenVR"
$runtimeDepsDir = Join-Path $pluginDir "RuntimeDeps"
$patcherDir     = Join-Path $GamePath "BepInEx\patchers\GloomhavenVR"
$nativesDir     = Join-Path $patcherDir "Natives"

foreach ($dir in @($pluginDir, $runtimeDepsDir, $patcherDir, $nativesDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Copy-Item $plugin    -Destination $pluginDir  -Force
Copy-Item $preloader -Destination $patcherDir -Force
$runtimeDeps | Copy-Item -Destination $runtimeDepsDir -Force
$natives     | Copy-Item -Destination $nativesDir     -Force

# Asset bundle (control board 3D asset + future props): deploy the freshly built one
# if a local Unity build produced it, else the committed prebuilt copy. The mod probes
# <pluginDir>\gloomhavenvr.bundle at runtime (VRCardFactory/WorldUIAssets/HandVisuals);
# without it the procedural fallback visuals are used.
$bundleFresh    = Join-Path $root "unity\GloomhavenVR.Assets\Build\Bundles\gloomhavenvr.bundle"
$bundlePrebuilt = Join-Path $root "prebuilt\gloomhavenvr.bundle"
$bundle = if (Test-Path $bundleFresh) { $bundleFresh } elseif (Test-Path $bundlePrebuilt) { $bundlePrebuilt } else { $null }
if ($bundle) {
    Copy-Item $bundle -Destination (Join-Path $pluginDir "gloomhavenvr.bundle") -Force
}

# Clean up the Phase-0 flat-preloader location if present (moved into a subfolder).
$legacyPreloader = Join-Path $GamePath "BepInEx\patchers\GloomhavenVR.Preload.dll"
if (Test-Path $legacyPreloader) {
    Remove-Item $legacyPreloader -Force
    Write-Host "Removed legacy preloader at BepInEx\patchers\GloomhavenVR.Preload.dll"
}

Write-Host "Deployed:"
Write-Host "  $pluginDir\GloomhavenVR.dll"
Write-Host "  $runtimeDepsDir\  ($(@($runtimeDeps).Count) RuntimeDeps DLLs)"
Write-Host "  $patcherDir\GloomhavenVR.Preload.dll"
Write-Host "  $nativesDir\  ($(@($natives).Count) native DLLs)"
if ($bundle) { Write-Host "  $pluginDir\gloomhavenvr.bundle  (source: $bundle)" }
else         { Write-Host "  (no gloomhavenvr.bundle — procedural visuals used)" }
