param([string]$Installer, [string]$Fixture)
$ErrorActionPreference = 'Stop'
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
if (-not $source.Contains('"BepInEx/plugins/GloomhavenVR/Movies/CP_Intro/GH_CP_Intro.mov",')) {
    throw 'Windows release ZIP must require its plugin-local campaign movie.'
}

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
    $expected = [IO.File]::ReadAllBytes($introMovie)
    . $deploy
    $installed = Join-Path $GamePath 'BepInEx/plugins/GloomhavenVR/Movies/CP_Intro/GH_CP_Intro.mov'
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($installed)) -ne [Convert]::ToBase64String($expected)) {
        throw "$Configuration installation lost or changed the campaign movie."
    }
    if ([IO.File]::ReadAllText($nativeGameMovie) -ne 'original game movie must remain unchanged') {
        throw "$Configuration installation overwrote the game's own movie."
    }
    # Force the next configuration to copy different bytes; a stale successful first
    # installation must never make an omitted second deployment look correct.
    if ($Configuration -eq 'Debug') { [IO.File]::WriteAllText($introMovie, 'second deployment movie') }
}
Remove-Item -LiteralPath $introMovie
$rejected = $false
try { . $preflight } catch {
    if (-not $_.Exception.Message.Contains('Missing approved campaign intro')) { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Missing movie must reject developer installation before deployment.' }
Write-Output 'Native movie PowerShell installer: syntax, actual Debug/Release deployment and missing-asset control passed.'
