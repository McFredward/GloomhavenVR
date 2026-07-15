<#
.SYNOPSIS
    Copies built GloomhavenVR artifacts into a Gloomhaven install (BepInEx 5 required).

.DESCRIPTION
    Plugin DLL  -> <GamePath>\BepInEx\plugins\GloomhavenVR\
    Preloader   -> <GamePath>\BepInEx\patchers\

    Builds are NOT triggered here; run `dotnet build GloomhavenVR.sln -c Release`
    (or scripts/build.sh) first.

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
    Write-Error "No BepInEx folder found in '$GamePath'. Install BepInEx 5.4.23.5 (x64) first — see README."
}

$plugin    = Join-Path $root "src\GloomhavenVR\bin\$Configuration\net472\GloomhavenVR.dll"
$preloader = Join-Path $root "src\GloomhavenVR.Preload\bin\$Configuration\net472\GloomhavenVR.Preload.dll"

foreach ($artifact in @($plugin, $preloader)) {
    if (-not (Test-Path $artifact)) {
        Write-Error "Missing artifact '$artifact'. Build first: dotnet build GloomhavenVR.sln -c $Configuration"
    }
}

$pluginDir  = Join-Path $GamePath "BepInEx\plugins\GloomhavenVR"
$patcherDir = Join-Path $GamePath "BepInEx\patchers"

New-Item -ItemType Directory -Force -Path $pluginDir  | Out-Null
New-Item -ItemType Directory -Force -Path $patcherDir | Out-Null

Copy-Item $plugin    -Destination $pluginDir  -Force
Copy-Item $preloader -Destination $patcherDir -Force

Write-Host "Deployed:"
Write-Host "  $pluginDir\GloomhavenVR.dll"
Write-Host "  $patcherDir\GloomhavenVR.Preload.dll"
