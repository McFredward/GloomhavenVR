<#
.SYNOPSIS
    One-shot Windows install: fetches XR deps, compiles the mod from source and
    deploys the fresh DLLs into your Gloomhaven install (overwriting old ones).

.DESCRIPTION
    Does everything needed after a `git pull`:

      1. checks for the .NET SDK (and git)
      2. locates your Gloomhaven install (or use -GamePath)
      3. writes Directory.Build.props.user pointing the build at the game's
         Managed folder (auto-detects the *_Data folder name, e.g. GH_Data)
      4. downloads the two OpenXR natives (SHA256-verified) if missing
      5. builds the provisional Unity.XR.* RuntimeDeps if missing
      6. dotnet build -c Release
      7. copies plugin + RuntimeDeps + preloader + natives into BepInEx
      8. turns on Unity's threaded render submission by adding two keys to
         <GameDir>\<GH>_Data\boot.config - the single largest performance
         finding of the project (45Hz -> 90Hz on the hardware it was measured
         on). This is the ONLY game file the mod writes: two key=value lines,
         idempotent, every other line preserved, and the original copied once
         to boot.config.gloomhavenvr-backup. uninstall.ps1 restores it.
         Doing it here rather than only from the mod is what removes the
         "start the game twice" step - the engine reads boot.config before any
         mod code exists, so the mod's own write can only apply to the NEXT run.
      9. leaves the rest of the game data UNMODIFIED. If a
         <GameDir>\GloomhavenVR_Backup exists from a previous install that
         shader-patched the game, it is RESTORED so the original shader assets
         are back (VR occlusion is now fixed in-code via Forward rendering, so
         no game ASSET is ever patched)
     10. packages dist\GloomhavenVR-<version>.zip — the drag-and-drop archive
         for the GitHub releases page, built from the tree just deployed (so it
         cannot describe a layout different from the one that works) and
         verified to contain the load-bearing paths. Skip with -NoPackage.

    Safe to re-run any time - every step is idempotent and only rebuilds/copies
    what changed. BepInEx 5.4.23.5 (x64) must already be installed in the game
    folder (see INSTALL.md).

.EXAMPLE
    .\scripts\install.ps1
.EXAMPLE
    .\scripts\install.ps1 -GamePath "D:\Games\Gloomhaven"
#>
param(
    [string]$GamePath = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    # Skip building dist\GloomhavenVR-<version>.zip. The zip is cheap (it copies the
    # tree that was just deployed) and it is what goes on the GitHub releases page,
    # so it is on by default — this is for a fast iterate-and-test loop.
    [switch]$NoPackage
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- 1. toolchain ----------------------------------------------------------
Step "Checking toolchain"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK not found. Install from https://dotnet.microsoft.com/download (SDK 8+), then re-run."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "git not found. Install https://git-scm.com/download/win, then re-run."
}

# --- 2. locate game --------------------------------------------------------
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

if (-not (Test-Path (Join-Path $GamePath "BepInEx\core\BepInEx.dll"))) {
    Write-Error ("BepInEx not found in '$GamePath'. Install BepInEx 5.4.23.5 x64 first: " +
        "https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 (extract into the game folder, " +
        "run the game once, then re-run this script). See INSTALL.md.")
}

# --- 3. point the build at the game's Managed folder -----------------------
Step "Configuring game references"
$managed = Get-ChildItem -Path $GamePath -Directory -Filter "*_Data" |
    ForEach-Object { Join-Path $_.FullName "Managed" } |
    Where-Object { Test-Path (Join-Path $_ "GH.Runtime.dll") } |
    Select-Object -First 1
if (-not $managed) { Write-Error "No *_Data\Managed\GH.Runtime.dll under '$GamePath'." }
Write-Host "    managed: $managed"

$propsUser = Join-Path $root "Directory.Build.props.user"
$propsContent = @"
<Project>
  <PropertyGroup>
    <GameManaged>$managed</GameManaged>
  </PropertyGroup>
</Project>
"@
if (-not (Test-Path $propsUser) -or (Get-Content $propsUser -Raw) -ne $propsContent) {
    Set-Content -Path $propsUser -Value $propsContent -NoNewline
    Write-Host "    wrote Directory.Build.props.user"
}

# --- 4. natives (SHA256-pinned, mirrors scripts/fetch-natives.sh) ----------
Step "OpenXR natives"
$nativesDir = Join-Path $root "libs\Natives"
New-Item -ItemType Directory -Force -Path $nativesDir | Out-Null
$openxrVersion = "1.10.0"
$natives = @(
    @{ Rel = "Runtime/windows/x64/UnityOpenXR.dll";        Sha = "2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5" },
    @{ Rel = "RuntimeLoaders/windows/x64/openxr_loader.dll"; Sha = "c008f1f429eb89ad1ebb959a74425c1b8df78abe6b1a08287ed44900d49881d3" }
)
foreach ($n in $natives) {
    $name = Split-Path $n.Rel -Leaf
    $out = Join-Path $nativesDir $name
    if ((Test-Path $out) -and ((Get-FileHash $out -Algorithm SHA256).Hash.ToLower() -eq $n.Sha)) {
        Write-Host "    ok $name (cached, hash verified)"
        continue
    }
    $url = "https://github.com/needle-mirror/com.unity.xr.openxr/raw/$openxrVersion/$($n.Rel)"
    Write-Host "    fetching $name"
    Invoke-WebRequest -Uri $url -OutFile "$out.tmp" -UseBasicParsing
    $actual = (Get-FileHash "$out.tmp" -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $n.Sha) {
        Remove-Item "$out.tmp"
        Write-Error "SHA256 mismatch for $name (expected $($n.Sha), got $actual)"
    }
    Move-Item -Force "$out.tmp" $out
    Write-Host "    ok $name"
}

# --- 5. provisional RuntimeDeps (mirrors scripts/build-runtimedeps.sh) -----
Step "RuntimeDeps (Unity.XR.* assemblies)"
$runtimeDepsDir = Join-Path $root "libs\RuntimeDeps"
$packages = @(
    @{ Pkg = "com.unity.xr.management"; Tag = "4.5.0";  Proj = "Unity.XR.Management" },
    @{ Pkg = "com.unity.xr.core-utils"; Tag = "2.2.3";  Proj = "Unity.XR.CoreUtils" },
    @{ Pkg = "com.unity.xr.openxr";     Tag = "1.10.0"; Proj = "Unity.XR.OpenXR" }
)
$depsMissing = $packages | Where-Object { -not (Test-Path (Join-Path $runtimeDepsDir "$($_.Proj).dll")) }
if ($depsMissing) {
    $sources = Join-Path $root "tools\RuntimeDepsBuild\sources"
    foreach ($p in $packages) {
        $dir = Join-Path $sources $p.Pkg
        $pkgJson = Join-Path $dir "package.json"
        # NOTE: fetch refs/tags/<tag> explicitly - needle-mirror repos have
        # version-named BRANCHES pointing at different snapshots.
        $haveRight = (Test-Path $pkgJson) -and ((Get-Content $pkgJson -Raw) -match """version"":\s*""$([regex]::Escape($p.Tag))""")
        if (-not $haveRight) {
            if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            Write-Host "    fetching $($p.Pkg)@$($p.Tag)"
            git -C $dir init -q
            git -C $dir remote add origin "https://github.com/needle-mirror/$($p.Pkg).git"
            git -C $dir fetch -q --depth 1 origin "refs/tags/$($p.Tag)"
            git -C $dir checkout -q FETCH_HEAD
        } else {
            Write-Host "    ok $($p.Pkg)@$($p.Tag) (cached)"
        }
    }
    New-Item -ItemType Directory -Force -Path $runtimeDepsDir | Out-Null
    foreach ($p in $packages) {
        Write-Host "    building $($p.Proj)"
        dotnet build (Join-Path $root "tools\RuntimeDepsBuild\$($p.Proj)\$($p.Proj).csproj") -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "RuntimeDeps build failed: $($p.Proj)" }
        Copy-Item (Join-Path $root "tools\RuntimeDepsBuild\$($p.Proj)\bin\Release\net472\$($p.Proj).dll") $runtimeDepsDir -Force
    }
} else {
    Write-Host "    ok (all 3 assemblies present - delete libs\RuntimeDeps to force rebuild)"
}

# --- 6. build the mod -------------------------------------------------------
Step "Building GloomhavenVR ($Configuration)"

# Show exactly which commit is being built and warn on a behind/dirty tree, so a stale
# DLL can never be deployed unnoticed (the running mod logs the same stamp on startup).
$builtHash    = (git -C $root rev-parse --short=9 HEAD 2>$null)
$builtBranch  = (git -C $root rev-parse --abbrev-ref HEAD 2>$null)
$builtSubject = (git -C $root show -s --format=%s HEAD 2>$null)
Write-Host "    commit $builtHash [$builtBranch] `"$builtSubject`"" -ForegroundColor Yellow
if (git -C $root status --porcelain --untracked-files=no 2>$null) {
    Write-Host "    WARNING: working tree is DIRTY - the build will be tagged '-dirty'." -ForegroundColor Yellow
}
$behind = (git -C $root rev-list --count "HEAD..@{u}" 2>$null)
if ($behind -and [int]$behind -gt 0) {
    Write-Host "    WARNING: branch is $behind commit(s) BEHIND upstream - 'git pull' for the latest." -ForegroundColor Red
}

dotnet build (Join-Path $root "GloomhavenVR.sln") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed." }

# --- 7. deploy into the game ------------------------------------------------
# Everything lands where the runtime probes it:
#   plugin      -> BepInEx\plugins\GloomhavenVR\GloomhavenVR.dll
#   RuntimeDeps -> BepInEx\plugins\GloomhavenVR\RuntimeDeps\*.dll
#   bundle      -> BepInEx\plugins\GloomhavenVR\gloomhavenvr.bundle
#   preloader   -> BepInEx\patchers\GloomhavenVR\GloomhavenVR.Preload.dll
#   natives     -> BepInEx\patchers\GloomhavenVR\Natives\*.dll
Step "Deploying into game"
$plugin    = Join-Path $root "src\GloomhavenVR\bin\$Configuration\net472\GloomhavenVR.dll"
$preloader = Join-Path $root "src\GloomhavenVR.Preload\bin\$Configuration\net472\GloomhavenVR.Preload.dll"
foreach ($artifact in @($plugin, $preloader)) {
    if (-not (Test-Path $artifact)) { Write-Error "Missing build artifact '$artifact' - build step failed?" }
}

$pluginDir      = Join-Path $GamePath "BepInEx\plugins\GloomhavenVR"
$runtimeDepsDst = Join-Path $pluginDir "RuntimeDeps"
$patcherDir     = Join-Path $GamePath "BepInEx\patchers\GloomhavenVR"
$nativesDst     = Join-Path $patcherDir "Natives"
foreach ($dir in @($pluginDir, $runtimeDepsDst, $patcherDir, $nativesDst)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Copy-Item $plugin    -Destination $pluginDir  -Force
Copy-Item $preloader -Destination $patcherDir -Force
Get-ChildItem (Join-Path $runtimeDepsDir "*.dll") | Copy-Item -Destination $runtimeDepsDst -Force
Get-ChildItem (Join-Path $nativesDir     "*.dll") | Copy-Item -Destination $nativesDst     -Force
if (Test-Path (Join-Path $runtimeDepsDir "versions.json")) {
    Copy-Item (Join-Path $runtimeDepsDir "versions.json") $runtimeDepsDst -Force
}

# Asset bundle (control board 3D asset + future props): a freshly built one is
# preferred, else the committed prebuilt copy. Without it the mod uses procedural
# fallback visuals.
$bundleFresh    = Join-Path $root "unity\GloomhavenVR.Assets\Build\Bundles\gloomhavenvr.bundle"
$bundlePrebuilt = Join-Path $root "prebuilt\gloomhavenvr.bundle"
$bundle = if (Test-Path $bundleFresh) { $bundleFresh } elseif (Test-Path $bundlePrebuilt) { $bundlePrebuilt } else { $null }
if ($bundle) {
    Copy-Item $bundle -Destination (Join-Path $pluginDir "gloomhavenvr.bundle") -Force
    # THE LICENCE NOTICE TRAVELS WITH THE BUNDLE (2026-09-03). The bundle carries third-party art
    # -- the WebXR Input Profiles controller models (MIT) among others -- whose licences require
    # the notice to accompany the copies, and the bundle builder deliberately keeps .txt files out
    # of the archive itself, so the notice has to ship beside it. package-release.sh:87 has done
    # this from the start; this packager did not, so a zip built HERE shipped the art with no
    # notice at all. Two packagers, one layout: they may not disagree about what a release
    # contains, and this was the one thing they did.
    $thirdParty = Join-Path $root "packaging\THIRD-PARTY.txt"
    if (Test-Path $thirdParty) {
        Copy-Item $thirdParty -Destination (Join-Path $pluginDir "THIRD-PARTY.txt") -Force
    } else {
        Write-Warning "packaging\THIRD-PARTY.txt is missing - the bundle's third-party art would ship with no licence notice."
    }
}

# Clean up the Phase-0 flat-preloader location if a stale copy is present.
$legacyPreloader = Join-Path $GamePath "BepInEx\patchers\GloomhavenVR.Preload.dll"
if (Test-Path $legacyPreloader) {
    Remove-Item $legacyPreloader -Force
    Write-Host "    removed legacy preloader at BepInEx\patchers\GloomhavenVR.Preload.dll"
}

Write-Host "    plugin + RuntimeDeps + preloader + natives$(if ($bundle) { ' + gloomhavenvr.bundle' }) deployed"

# --- 8. restore any previously shader-patched game data ----------------------
# The mod NO LONGER patches game data. VR depth occlusion is fixed entirely
# in-code: the head camera renders Forward (VRRigDriver / Plugin.ForwardRendering),
# which keeps wall depth for the transparent pass. So a fresh install touches
# nothing under *_Data. If an OLDER install shader-patched the game (leaving a
# <GameDir>\GloomhavenVR_Backup of the pristine originals), restore it now so the
# game data ends up unmodified. No backup => nothing to do.
Step "Restoring original game data (if a previous install patched it)"
$gameDataDir = Split-Path $managed -Parent                 # ...\<GH>_Data
$backupDir   = Join-Path $GamePath "GloomhavenVR_Backup"

if (Test-Path $backupDir) {
    $patcherProj = Join-Path $root "tools\ShaderOcclusionPatcher\ShaderOcclusionPatcher.csproj"
    $patcherDll  = Join-Path $root "tools\ShaderOcclusionPatcher\bin\Release\net8.0\ShaderOcclusionPatcher.dll"

    dotnet build $patcherProj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Error "ShaderOcclusionPatcher build failed." }

    $restoredCount = (Get-ChildItem -Path $backupDir -File -Recurse | Measure-Object).Count
    dotnet $patcherDll restore --game-data $gameDataDir --backup-dir $backupDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "SHADER RESTORE FAILED." -ForegroundColor Red
        Write-Host "The pristine originals are still in $backupDir - restore them with" -ForegroundColor Red
        Write-Host "  .\scripts\uninstall.ps1   (or copy $backupDir back manually," -ForegroundColor Red
        Write-Host "            or Steam 'Verify integrity of game files')" -ForegroundColor Red
        Write-Error "ShaderOcclusionPatcher restore step failed (see output above)."
    }
    Write-Host "    restored $restoredCount file(s) from a previous shader patch - game data is now unmodified." -ForegroundColor Green
} else {
    Write-Host "    no GloomhavenVR_Backup found - game data was never patched, nothing to restore."
}

# ---------------------------------------------------------------------------
# Graphics jobs - the single largest performance finding of the project.
#
# Unity submits every draw call on ONE thread unless this is on, and that thread
# was the entire bottleneck in a scenario: with it on, main-thread render went
# 14.9ms -> 1.8ms, the frame 17.5ms -> 11.14ms, and the headset from locked-at-45Hz
# to a clean 90Hz (2026-07-28 hardware). The reported ghosting disappeared.
#
# WHY HERE AND NOT ONLY IN THE MOD: the engine reads boot.config before any mod
# code exists, so the preloader's own write can only ever apply to the NEXT start.
# Writing it HERE, at install time, means the very first launch afterwards already
# has it - no "start the game twice" step. The preloader keeps doing it too, as the
# fallback for anyone who installs by unzipping the release instead of running this.
# ---------------------------------------------------------------------------
Step "Enabling threaded render submission (graphics jobs)"
$bootConfig = Join-Path $gameDataDir "boot.config"
if (-not (Test-Path $bootConfig)) {
    Write-Host "    no boot.config under $gameDataDir - skipped. The mod will write it on first run instead." -ForegroundColor Yellow
} else {
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
        Write-Host "    already enabled - boot.config untouched."
    } else {
        # Back up the file as it was BEFORE this mod ever touched it, once. Never
        # overwritten: a second backup taken after our own edit would record our edit
        # as the original. This is the escape hatch if the game ever fails to start -
        # nothing in the mod can help there, because it never runs.
        $backup = "$bootConfig.gloomhavenvr-backup"
        if (-not (Test-Path $backup)) { Copy-Item -LiteralPath $bootConfig -Destination $backup }
        Set-Content -LiteralPath $bootConfig -Value $lines -Encoding UTF8
        Write-Host "    enabled in boot.config - active from the next launch (original saved as boot.config.gloomhavenvr-backup)." -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Release zip for the GitHub releases page - what a normal user drag-and-drops.
#
# BUILT FROM WHAT WAS JUST DEPLOYED, deliberately. The alternative was to stage
# the layout a second time here, which would mean two definitions of "what a
# working install looks like" that can drift - and a drifted release zip is a
# silently broken download nobody notices until a stranger reports it. Zipping
# the two GloomhavenVR subtrees that were just installed means the archive is by
# construction the tree that works on this machine.
#
# The INSTALL.txt files come from packaging/INSTALL.txt.in and
# packaging/INSTALL.de.txt.in, the same templates package-release.sh renders, so
# the two packagers cannot describe the install differently. BOTH LANGUAGES SHIP,
# and both are asserted below: an English-only zip out of this packager and a
# bilingual one out of package-release.sh is exactly the drift the paragraph above
# says a second layout definition causes.
# ---------------------------------------------------------------------------
if (-not $NoPackage) {
    Step "Packaging release zip"
    $version = (Select-String -Path (Join-Path $root "src\GloomhavenVR\GloomhavenVR.csproj") `
                              -Pattern '<Version>(.*)</Version>').Matches[0].Groups[1].Value
    $dist  = Join-Path $root "dist"
    $stage = Join-Path $dist "stage-install"
    $zip   = Join-Path $dist "GloomhavenVR-$version.zip"

    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    if (Test-Path $zip)   { Remove-Item -Force $zip }
    New-Item -ItemType Directory -Force -Path (Join-Path $stage "BepInEx\plugins"),
                                              (Join-Path $stage "BepInEx\patchers") | Out-Null

    Copy-Item -Recurse -Force $pluginDir  (Join-Path $stage "BepInEx\plugins\GloomhavenVR")
    Copy-Item -Recurse -Force $patcherDir (Join-Path $stage "BepInEx\patchers\GloomhavenVR")

    $templates = @{
        "INSTALL.txt"         = Join-Path $root "packaging\INSTALL.txt.in"
        "INSTALL-DEUTSCH.txt" = Join-Path $root "packaging\INSTALL.de.txt.in"
    }
    foreach ($name in $templates.Keys) {
        $template = $templates[$name]
        if (-not (Test-Path $template)) { Write-Error "Missing $template - cannot package." }
        # UTF8 on purpose: both files carry em dashes and the German one carries
        # umlauts. Transliterating would be the only alternative and it reads
        # amateurish to the person the file is written for.
        (Get-Content -LiteralPath $template -Raw).Replace('@VERSION@', $version) |
            Set-Content -LiteralPath (Join-Path $stage $name) -Encoding UTF8 -NoNewline
    }

    # No graphics-jobs enabler ships any more: the preloader writes boot.config
    # itself and restarts the game once on the boot that needs it.

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
    Remove-Item -Recurse -Force $stage

    # The same load-bearing paths package-release.sh asserts. A zip that is
    # missing one of these looks fine and fails at the stranger's machine.
    $required = @(
        "BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll",
        "BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.OpenXR.dll",
        "BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll",
        "BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll",
        "INSTALL.txt",
        "INSTALL-DEUTSCH.txt")
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try   { $entries = $archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' } }
    finally { $archive.Dispose() }
    $missing = $required | Where-Object { $entries -notcontains $_ }
    if ($missing) {
        Write-Error "Packaged zip is missing:`n  $($missing -join "`n  ")"
    }

    $sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "    $zip ($sizeMb MB) - layout verified, ready for the GitHub release page." -ForegroundColor Green
}

Write-Host ""
Write-Host "Deployed commit $builtHash [$builtBranch] `"$builtSubject`" - this exact stamp appears in LogOutput.log at startup." -ForegroundColor Green
Write-Host "Done. Launch Gloomhaven and check BepInEx\LogOutput.log." -ForegroundColor Green
Write-Host "If something fails, drop LogOutput.log + openxr-diagnostics.log + Player.log into .planning/debug/."
