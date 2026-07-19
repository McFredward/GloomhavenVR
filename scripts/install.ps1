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
    [string]$Configuration = "Release"
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
if ($bundle) { Copy-Item $bundle -Destination (Join-Path $pluginDir "gloomhavenvr.bundle") -Force }

# Clean up the Phase-0 flat-preloader location if a stale copy is present.
$legacyPreloader = Join-Path $GamePath "BepInEx\patchers\GloomhavenVR.Preload.dll"
if (Test-Path $legacyPreloader) {
    Remove-Item $legacyPreloader -Force
    Write-Host "    removed legacy preloader at BepInEx\patchers\GloomhavenVR.Preload.dll"
}

Write-Host "    plugin + RuntimeDeps + preloader + natives$(if ($bundle) { ' + gloomhavenvr.bundle' }) deployed"

Write-Host ""
Write-Host "Deployed commit $builtHash [$builtBranch] `"$builtSubject`" - this exact stamp appears in LogOutput.log at startup." -ForegroundColor Green
Write-Host "Done. Launch Gloomhaven and check BepInEx\LogOutput.log." -ForegroundColor Green
Write-Host "If something fails, drop LogOutput.log + openxr-diagnostics.log + Player.log into .planning/debug/."
