<#
.SYNOPSIS
    Uninstalls the GloomhavenVR mod: restores the shader-patched game files
    from GloomhavenVR_Backup and removes the mod's BepInEx deployment.

.DESCRIPTION
    1. locates your Gloomhaven install (or use -GamePath)
    2. copies every file from <GameDir>\GloomhavenVR_Backup back into the
       game's data folder (these are the pristine originals - the patcher
       never overwrites an existing backup)
    3. re-runs the patcher's `verify` to confirm the original (unpatched)
       shader state is back (skipped if the .NET SDK is unavailable)
    4. removes what install.ps1 deployed:
         BepInEx\plugins\GloomhavenVR\   (plugin + RuntimeDeps + gloomhavenvr.bundle)
         BepInEx\patchers\GloomhavenVR\  (preloader + OpenXR natives)
         BepInEx\patchers\GloomhavenVR.Preload.dll  (legacy Phase-0 location)
       BepInEx itself is left in place (remove it manually if unwanted).

    The backup dir (GloomhavenVR_Backup) and patch-manifest.json are kept so
    you can double-check; delete them manually after a successful game start.
    Steam "Verify integrity of game files" is always a fallback restore.

.EXAMPLE
    .\scripts\uninstall.ps1
.EXAMPLE
    .\scripts\uninstall.ps1 -GamePath "D:\Games\Gloomhaven"
#>
param(
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- 1. locate game (mirrors install.ps1) ----------------------------------
Step "Locating Gloomhaven"
if (-not $GamePath) {
    $candidates = @("C:\Program Files (x86)\Steam\steamapps\common\Gloomhaven")
    try {
        $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath
        if ($steam) {
            $candidates += Join-Path $steam "steamapps\common\Gloomhaven"
            $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
            if (Test-Path $vdf) {
                foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                    $candidates += Join-Path ($m.Groups[1].Value -replace '\\\\', '\') "steamapps\common\Gloomhaven"
                }
            }
        }
    } catch {}
    $GamePath = $candidates | Where-Object { Test-Path (Join-Path $_ "GH.exe") } | Select-Object -First 1
    if (-not $GamePath) {
        Write-Error "Gloomhaven not found automatically. Re-run with -GamePath `"C:\...\Gloomhaven`""
    }
}
if (-not (Test-Path (Join-Path $GamePath "GH.exe"))) {
    Write-Error "'$GamePath' does not look like a Gloomhaven install (GH.exe missing)."
}
Write-Host "    game: $GamePath"

$gameDataDir = Get-ChildItem -Path $GamePath -Directory -Filter "*_Data" |
    Where-Object { Test-Path (Join-Path $_.FullName "Managed\GH.Runtime.dll") } |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $gameDataDir) { Write-Error "No *_Data\Managed\GH.Runtime.dll under '$GamePath'." }

# --- 2. restore shader-patched files from backup ---------------------------
Step "Restoring original game files"
$backupDir = Join-Path $GamePath "GloomhavenVR_Backup"
$restored = 0
if (Test-Path $backupDir) {
    Get-ChildItem -Path $backupDir -File -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($backupDir.Length).TrimStart('\', '/')
        $dst = Join-Path $gameDataDir $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item $_.FullName -Destination $dst -Force
        Write-Host "    restored $rel"
        $script:restored++
    }
    if ($restored -eq 0) {
        Write-Host "    backup dir is empty - nothing had been patched."
    }
} else {
    Write-Host "    no backup dir at $backupDir - shader patch was never applied."
}

# --- 3. verify restored state via the patcher tool -------------------------
# The tool's `verify` exits 2 when the ORIGINAL "ZTest Always" passes are
# present (unpatched state) and 0 when the game is still patched.
if ($restored -gt 0) {
    Step "Verifying restored files"
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $patcherProj = Join-Path $root "tools\ShaderOcclusionPatcher\ShaderOcclusionPatcher.csproj"
        $patcherDll  = Join-Path $root "tools\ShaderOcclusionPatcher\bin\Release\net8.0\ShaderOcclusionPatcher.dll"
        dotnet build $patcherProj -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "ShaderOcclusionPatcher build failed." }
        dotnet $patcherDll verify --game-data $gameDataDir --manifest-out (Join-Path ([IO.Path]::GetTempPath()) "ghvr-uninstall-verify.json")
        if ($LASTEXITCODE -eq 2) {
            Write-Host "    OK - original (unpatched) shader state confirmed." -ForegroundColor Green
        } elseif ($LASTEXITCODE -eq 0) {
            Write-Host "    WARNING: shaders still look patched after restore - backup may be incomplete." -ForegroundColor Yellow
            Write-Host "    Use Steam 'Verify integrity of game files' to restore pristine files." -ForegroundColor Yellow
        } else {
            Write-Host "    WARNING: verify tool reported an error - falling back is safe via Steam 'Verify integrity'." -ForegroundColor Yellow
        }
    } else {
        Write-Host "    .NET SDK not found - skipping verify (files were copied back 1:1 from the backup)."
    }
}

# --- 3b. undo the boot.config graphics-jobs edit ----------------------------
# The one game FILE install.ps1 writes. Restoring it is not optional politeness:
# every mutation this mod makes has to be reversible, and this is the only one that
# outlives the process. The backup is the file exactly as it was before the mod ever
# touched it (never overwritten by a later install), so copying it back is exact.
Step "Restoring boot.config (threaded render submission)"
$bootConfig = Join-Path $gameDataDir "boot.config"
$bootBackup = "$bootConfig.gloomhavenvr-backup"
if (Test-Path $bootBackup) {
    Copy-Item -LiteralPath $bootBackup -Destination $bootConfig -Force
    Remove-Item -LiteralPath $bootBackup -Force
    Write-Host "    restored - graphics jobs are back to whatever the game shipped with." -ForegroundColor Green
} else {
    Write-Host "    no backup found - boot.config was never modified by this mod, nothing to undo."
}

# --- 4. remove the mod's BepInEx deployment --------------------------------
# Exactly what install.ps1 deploys; BepInEx itself stays.
Step "Removing mod deployment"
# GloomhavenVR-update/ is NOT deployed by install.ps1 — the self-updater creates it at runtime
# (Core/SelfUpdatePaths.StagingFolderName) to stage a downloaded zip and to keep the pre-update
# backup until the new version has booted once. It therefore holds a full copy of the mod, on the
# order of 140 MB, and leaving it behind means an "uninstall" that frees almost nothing. It is
# safe to take here for the same reason the deployment is: the thing it exists to roll back to is
# what this script is removing.
$deployed = @(
    (Join-Path $GamePath "BepInEx\plugins\GloomhavenVR"),
    (Join-Path $GamePath "BepInEx\patchers\GloomhavenVR"),
    (Join-Path $GamePath "BepInEx\patchers\GloomhavenVR.Preload.dll"),  # legacy Phase-0
    (Join-Path $GamePath "BepInEx\GloomhavenVR-update")
)
foreach ($item in $deployed) {
    if (Test-Path $item) {
        Remove-Item -Recurse -Force $item
        Write-Host "    removed $item"
    }
}

Write-Host ""
Write-Host "Uninstall complete: $restored game file(s) restored, mod removed from BepInEx." -ForegroundColor Green
Write-Host "Kept for safety: $backupDir (+ patch-manifest.json next to it, if present)."
Write-Host "After a successful game start you may delete them manually."
Write-Host "BepInEx itself was left installed; Steam 'Verify integrity of game files' is the fallback restore."
