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
$start = $source.IndexOf('$introMovie = Join-Path $root ')
$end = $source.IndexOf('# Prepared native figure derivatives', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native movie preflight not found.' }
$preflight = [scriptblock]::Create($source.Substring($start, $end - $start))
$start = $source.IndexOf('# --- 7. deploy into the game ')
$end = $source.IndexOf('# Text files a Windows user double-clicks:', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native installer deployment block not found.' }
$deploy = [scriptblock]::Create($source.Substring($start, $end - $start))
$movieKeys = @('CP_Intro/GH_CP_Intro.mov', 'Calibration/calibration-sbs.mp4', 'Calibration/calibration-mono.mp4')
foreach ($key in $movieKeys) {
    Check ($source.Contains('"BepInEx/plugins/GloomhavenVR/Movies/' + $key + '",')) ('Windows ZIP requires movie: ' + $key)
}
$start = $source.IndexOf('$script:Utf8Strict =')
$end = $source.IndexOf('. (Join-Path $PSScriptRoot "frame-archive.ps1")', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native archive text helpers not found.' }
$textHelpers = [scriptblock]::Create($source.Substring($start, $end - $start))
$start = $source.IndexOf('if (-not $NoPackage) {')
$end = $source.IndexOf('Write-Host "Deployed commit ', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native release packaging block not found.' }
$package = [scriptblock]::Create($source.Substring($start, $end - $start))

function Step([string]$Text) { }
$root = $Fixture
$runtimeDepsDir = Join-Path $root 'libs/RuntimeDeps'
$nativesDir = Join-Path $root 'libs/Natives'
$GamePath = Join-Path $root 'game'
$nativeGameMovie = Join-Path $GamePath 'Gloomhaven_Data/StreamingAssets/Movies/CP_Intro/GH_CP_Intro.mov'
New-Item -ItemType Directory -Force -Path (Split-Path $nativeGameMovie) | Out-Null
[IO.File]::WriteAllText($nativeGameMovie, 'original game movie must remain unchanged')
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
    # Force the next configuration to copy different bytes; a stale successful first
    # installation must never make an omitted second deployment look correct.
    if ($Configuration -eq 'Debug') {
        foreach ($key in $movieKeys) {
            [IO.File]::WriteAllBytes((Join-Path $root ('src/GloomhavenVR/Assets/Movies/' + $key)), [byte[]]($expected[$key] + 42))
        }
    }
}
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
} finally { $archive.Dispose() }
foreach ($key in $movieKeys) {
    $path = Join-Path $root ('src/GloomhavenVR/Assets/Movies/' + $key)
    $saved = [IO.File]::ReadAllBytes($path)
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { . $preflight } catch {
        if (-not $_.Exception.Message.Contains('Missing approved campaign intro') -and -not $_.Exception.Message.Contains('Missing stereo calibration movie')) { throw }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $saved) }
    Check $rejected ('Missing source rejects installation: ' + $key)
}
foreach ($key in @('Calibration/calibration-sbs.mp4', 'Calibration/calibration-mono.mp4')) {
    $path = Join-Path $pluginDir ('Movies/' + $key)
    $saved = [IO.File]::ReadAllBytes($path)
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { . $package } catch {
        if (-not $_.Exception.Message.Contains('Packaged zip is missing:') -or -not $_.Exception.Message.Contains($key)) { throw }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $saved) }
    Check $rejected ('Windows archive validator rejects missing installed movie: ' + $key)
}
Write-Output ("Native movie PowerShell installer: $script:checks assertions; actual Debug/Release deployment, Windows ZIP and 5 missing-file controls passed.")
