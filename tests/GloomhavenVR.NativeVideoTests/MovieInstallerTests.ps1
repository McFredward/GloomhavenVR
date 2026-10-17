param([string]$Installer, [string]$Fixture)
$ErrorActionPreference = 'Stop'
$script:checks = 0
function Check([bool]$Condition, [string]$Label) {
    $script:checks++
    if (-not $Condition) { throw $Label }
}
$tokens = $null
$errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($Installer, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$source = [IO.File]::ReadAllText($Installer)
$start = $source.IndexOf('$movieSourceRoot = Join-Path $root ')
$end = $source.IndexOf('# Prepared native figure derivatives', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native movie preflight not found.' }
$preflight = [scriptblock]::Create($source.Substring($start, $end - $start))
$start = $source.IndexOf('# --- 7. deploy into the game ')
$end = $source.IndexOf('# Text files a Windows user double-clicks:', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native installer deployment block not found.' }
$deploy = [scriptblock]::Create($source.Substring($start, $end - $start))
$movieKeys = @(Get-Content -LiteralPath (Join-Path $Fixture 'packaging/movie-files.txt') -Encoding UTF8 |
    Where-Object { $_ -and -not $_.StartsWith('#') })
Check ($movieKeys.Count -eq 22) 'Complete22-file delivery set reaches actual installer fixture'
Check ($source.Contains('$required += @($moviePaths | ForEach-Object { "BepInEx/plugins/GloomhavenVR/Movies/" + $_ })')) `
    'Windows ZIP validator requires every exact manifest movie'
$start = $source.IndexOf('$script:Utf8Strict =')
$end = $source.IndexOf('. (Join-Path $PSScriptRoot "frame-archive.ps1")', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native archive text helpers not found.' }
$textHelpers = [scriptblock]::Create($source.Substring($start, $end - $start))
$start = $source.IndexOf('if (-not $NoPackage) {')
$end = $source.IndexOf('Write-Host "Deployed commit ', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native release packaging block not found.' }
$package = [scriptblock]::Create($source.Substring($start, $end - $start))
Check ($source.IndexOf('Remove-RetiredCalibrationMovies $pluginDir') -gt $source.IndexOf('if ($textProblems)')) `
    'Installed calibration cleanup occurs only after successful deployment and archive validation'

function Step([string]$Text) { }
$root = $Fixture
$runtimeDepsDir = Join-Path $root 'libs/RuntimeDeps'
$nativesDir = Join-Path $root 'libs/Natives'
$GamePath = Join-Path $root 'game'
$nativeGameMovie = Join-Path $GamePath 'Gloomhaven_Data/StreamingAssets/Movies/CP_Intro/GH_CP_Intro.mov'
New-Item -ItemType Directory -Force -Path (Split-Path $nativeGameMovie) | Out-Null
[IO.File]::WriteAllText($nativeGameMovie, 'original game movie must remain unchanged')
$retiredKeys = @('Calibration/calibration-sbs.mp4', 'Calibration/calibration-mono.mp4', 'CP_Intro/GH_CP_Intro.sbs-v1.mov')
$foreignKeys = @('Calibration/keep-custom.mp4', 'Calibration/provenance.json', 'Custom/keep.mov')
foreach ($key in @($retiredKeys + $foreignKeys)) {
    $path = Join-Path $GamePath ('BepInEx/plugins/GloomhavenVR/Movies/' + $key)
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, 'retain until success: ' + $key)
}
foreach ($Configuration in @('Debug', 'Release')) {
    foreach ($project in @('GloomhavenVR', 'GloomhavenVR.Preload')) {
        $artifact = Join-Path $root "src/$project/bin/$Configuration/net472/$project.dll"
        New-Item -ItemType Directory -Force -Path (Split-Path $artifact) | Out-Null
        [IO.File]::WriteAllText($artifact, "$Configuration substitute $project")
    }
    . $preflight
    $expected = @{}
    foreach ($key in $movieKeys) { $expected[$key] = [IO.File]::ReadAllBytes((Join-Path $root ('src/GloomhavenVR/Assets/Movies/' + $key))) }
    . $deploy
    foreach ($key in $movieKeys) {
        $installed = Join-Path $GamePath ('BepInEx/plugins/GloomhavenVR/Movies/' + $key)
        Check ([Convert]::ToBase64String([IO.File]::ReadAllBytes($installed)) -eq [Convert]::ToBase64String($expected[$key])) `
            ("$Configuration installer copies exact movie: " + $key)
    }
    Check ([IO.File]::ReadAllText($nativeGameMovie) -eq 'original game movie must remain unchanged') 'Original game movie remains untouched'
    foreach ($key in $retiredKeys) {
        Check ([IO.File]::ReadAllText((Join-Path $pluginDir ('Movies/' + $key))) -eq ('retain until success: ' + $key)) `
            ('Partial installation never removes retired movie before all steps succeed: ' + $key)
    }
    # Force the next configuration to copy different bytes; a stale successful first
    # installation must never make an omitted second deployment look correct.
    if ($Configuration -eq 'Debug') {
        foreach ($key in $movieKeys) {
            [IO.File]::WriteAllBytes((Join-Path $root ('src/GloomhavenVR/Assets/Movies/' + $key)), [byte[]]($expected[$key] + 42))
        }
    }
}
$savedSourceRoot = $movieSourceRoot
$movieSourceRoot = Join-Path $root 'missing-during-copy'
$copyRejected = $false
try { . $deploy } catch { $copyRejected = $true } finally { $movieSourceRoot = $savedSourceRoot }
Check $copyRejected 'Actual deployment rejects a missing copy input'
foreach ($key in $retiredKeys) {
    Check (Test-Path -LiteralPath (Join-Path $pluginDir ('Movies/' + $key)) -PathType Leaf) `
        ('Failed deployment preserves previously installed calibration: ' + $key)
}
. $deploy
. $textHelpers
. (Join-Path $root 'scripts/frame-archive.ps1')
$figureBanks = @(Get-ChildItem (Join-Path $root 'prebuilt/ghvr-figure-meshes-*.bundle'))
$NoPackage = $false
. $package
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($key in $movieKeys) {
        $stream = $archive.GetEntry('BepInEx/plugins/GloomhavenVR/Movies/' + $key).Open()
        $bytes = New-Object IO.MemoryStream
        try { $stream.CopyTo($bytes) } finally { $stream.Dispose() }
        Check ([Convert]::ToBase64String($bytes.ToArray()) -eq [Convert]::ToBase64String($expected[$key])) `
            ('Windows package contains exact installed movie: ' + $key)
        $bytes.Dispose()
    }
    foreach ($key in $retiredKeys) {
        Check ($null -eq $archive.GetEntry('BepInEx/plugins/GloomhavenVR/Movies/' + $key)) `
            ('Windows ZIP excludes exact retired calibration movie: ' + $key)
    }
    foreach ($key in $foreignKeys) {
        Check ($null -ne $archive.GetEntry('BepInEx/plugins/GloomhavenVR/Movies/' + $key)) `
            ('Windows ZIP preserves unrelated installed movie evidence: ' + $key)
    }
} finally { $archive.Dispose() }
foreach ($key in $retiredKeys) {
    Check (-not (Test-Path -LiteralPath (Join-Path $pluginDir ('Movies/' + $key)))) `
        ('Successful installation retires only exact old calibration: ' + $key)
}
foreach ($key in $foreignKeys) {
    Check ([IO.File]::ReadAllText((Join-Path $pluginDir ('Movies/' + $key))) -eq ('retain until success: ' + $key)) `
        ('Successful installation preserves foreign movie/evidence: ' + $key)
}
foreach ($key in $movieKeys) {
    $path = Join-Path $root ('src/GloomhavenVR/Assets/Movies/' + $key)
    $saved = [IO.File]::ReadAllBytes($path)
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { . $preflight } catch {
        if (-not $_.Exception.Message.Contains('Missing required movie')) { throw }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $saved) }
    Check $rejected ('Missing source rejects installation: ' + $key)
}
foreach ($key in $movieKeys) {
    $path = Join-Path $pluginDir ('Movies/' + $key)
    $saved = [IO.File]::ReadAllBytes($path)
    foreach ($retired in $retiredKeys) {
        [IO.File]::WriteAllText((Join-Path $pluginDir ('Movies/' + $retired)), 'old calibration survives failure')
    }
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { . $package } catch {
        if (-not $_.Exception.Message.Contains('Packaged zip is missing:') -or -not $_.Exception.Message.Contains($key)) { throw }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $saved) }
    Check $rejected ('Windows archive validator rejects missing installed movie: ' + $key)
    foreach ($retired in $retiredKeys) {
        Check ([IO.File]::ReadAllText((Join-Path $pluginDir ('Movies/' + $retired))) -eq 'old calibration survives failure') `
            ('Failed archive validation never cleans installed calibration: ' + $retired)
    }
}
# The normal developer update may skip ZIP generation. Its successful end still
# retires the known old clips and retains unrelated user files.
$NoPackage = $true
. $package
foreach ($key in $retiredKeys) {
    Check (-not (Test-Path -LiteralPath (Join-Path $pluginDir ('Movies/' + $key)))) `
        ('Successful NoPackage install also retires exact old calibration: ' + $key)
}
foreach ($key in $foreignKeys) {
    Check ([IO.File]::ReadAllText((Join-Path $pluginDir ('Movies/' + $key))) -eq ('retain until success: ' + $key)) `
        ('NoPackage cleanup preserves unrelated movie/evidence: ' + $key)
}
Write-Output ("Native movie PowerShell installer: $script:checks assertions; actual Debug/Release deployment, Windows ZIP, bounded retirement and manifest-wide missing-file controls passed.")
