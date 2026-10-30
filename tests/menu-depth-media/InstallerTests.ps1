param([Parameter(Mandatory=$true)][string]$Installer, [Parameter(Mandatory=$true)][string]$Fixture)
$ErrorActionPreference = 'Stop'
$script:checks = 0
function Check([bool]$Condition, [string]$Label) {
    $script:checks++
    if (-not $Condition) { throw $Label }
}
function Extract([string]$Start, [string]$End) {
    $begin = $source.IndexOf($Start, [StringComparison]::Ordinal)
    $finish = $source.IndexOf($End, $begin, [StringComparison]::Ordinal)
    if ($begin -lt 0 -or $finish -le $begin) { throw "Production installer block missing: $Start" }
    return $source.Substring($begin, $finish - $begin)
}
$source = [IO.File]::ReadAllText($Installer)
$preflightText = Extract '# Prepared menu illustration depth is mandatory' '# Prepared native figure derivatives'
$preflight = [scriptblock]::Create($preflightText)
$deploy = [scriptblock]::Create((Extract '# --- 7. deploy into the game ' '# Text files a Windows user double-clicks:'))
$textHelpers = [scriptblock]::Create((Extract '$script:Utf8Strict =' '. (Join-Path $PSScriptRoot "frame-archive.ps1")'))
$packageText = Extract 'if (-not $NoPackage) {' 'Write-Host "Deployed commit '
$package = [scriptblock]::Create($packageText)
Check ($source.IndexOf('# Prepared menu illustration depth is mandatory') -lt $source.IndexOf('Step "Checking toolchain"')) 'Menu depth preflight precedes all toolchain/dependency/install writes'
Check ($packageText.IndexOf('Get-MenuDepthFiles') -lt $packageText.IndexOf('if (Test-Path $stage)')) 'Installed depth set is checked before archive/stage replacement'

function Step([string]$Text) { }
$root = $Fixture
$runtimeDepsDir = Join-Path $root 'libs/RuntimeDeps'
$nativesDir = Join-Path $root 'libs/Natives'
$movieSourceRoot = Join-Path $root 'src/GloomhavenVR/Assets/Movies'
$moviePaths = @(Get-Content -LiteralPath (Join-Path $root 'packaging/movie-files.txt') -Encoding UTF8 |
    Where-Object { $_ -and -not $_.StartsWith('#') })
$GamePath = Join-Path $root 'depth-game'
if (Test-Path -LiteralPath $GamePath) { throw 'Depth game fixture must start in an unused path' }
Copy-Item -LiteralPath (Join-Path $root 'game') -Destination $GamePath -Recurse
$sentinel = Join-Path $GamePath 'unchanged-native-game.txt'
[IO.File]::WriteAllText($sentinel, 'native game source evidence stays unchanged')
. $preflight
Check ($menuDepthPaths.Count -eq 516) 'Actual Windows preflight admits the complete 516 companion fixture'
$expected = @{}
foreach ($name in $menuDepthPaths) { $expected[$name] = [IO.File]::ReadAllBytes((Join-Path $menuDepthSourceRoot $name)) }
foreach ($Configuration in @('Debug', 'Release')) {
    foreach ($name in $menuDepthPaths) {
        $stale = Join-Path $GamePath ('BepInEx/plugins/GloomhavenVR/MenuDepth/' + $name)
        [IO.File]::WriteAllText($stale, 'stale previous installation')
    }
    . $deploy
    foreach ($name in $menuDepthPaths) {
        $installed = Join-Path $pluginDir ('MenuDepth/' + $name)
        Check ((Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($installed))) -ceq (Get-MenuDepthChecksum $expected[$name])) `
            ("$Configuration installer replaces stale depth companion with exact source bytes: " + $name)
    }
    Write-Output ("Menu depth fixture: $Configuration complete516 deployment checked.")
}
Check ([IO.File]::ReadAllText($sentinel) -ceq 'native game source evidence stays unchanged') 'Native game sentinel survives actual deployment'
. $textHelpers
. (Join-Path $root 'scripts/frame-archive.ps1')
$figureBanks = @(Get-ChildItem (Join-Path $root 'prebuilt/ghvr-figure-meshes-*.bundle'))
$NoPackage = $false
. $package
$zipBefore = [IO.File]::ReadAllBytes($zip)
$zipHash = Get-MenuDepthChecksum $zipBefore
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $prefix = 'BepInEx/plugins/GloomhavenVR/MenuDepth/'
    $companions = @($archive.Entries | Where-Object { $_.FullName.StartsWith($prefix) -and -not $_.FullName.EndsWith('/') })
    Check ($companions.Count -eq 516) 'Windows archive contains exactly 516 external depth companions'
    foreach ($name in $menuDepthPaths) {
        $entry = $archive.GetEntry($prefix + $name)
        Check ($null -ne $entry) ('Windows ZIP has plugin-local companion: ' + $name)
        $buffer = [IO.MemoryStream]::new()
        $stream = $entry.Open()
        try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
        Check ((Get-MenuDepthChecksum ($buffer.ToArray())) -ceq (Get-MenuDepthChecksum $expected[$name])) `
            ('Windows ZIP preserves every exact depth payload: ' + $name)
        $buffer.Dispose()
    }
} finally { $archive.Dispose() }
Write-Output 'Menu depth fixture: complete516 Windows archive checked.'
$fields = @($menuDepthPaths | Where-Object { $_.EndsWith('.gmd') } | Sort-Object)
$controls = @($fields[0], $fields[[int][Math]::Floor($fields.Count / 2)], $fields[-1], 'Story/manifest.json')
$nativeBefore = Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($sentinel))
$props = Join-Path $root 'Directory.Build.props.user'
$propsBefore = if (Test-Path -LiteralPath $props) { Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($props)) } else { 'absent' }

function Assert-NoMutation([string]$Label) {
    Check ((Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($zip))) -ceq $zipHash) ("$Label preserves previous archive")
    Check ((Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($sentinel))) -ceq $nativeBefore) ("$Label preserves native game files")
    $now = if (Test-Path -LiteralPath $props) { Get-MenuDepthChecksum ([IO.File]::ReadAllBytes($props)) } else { 'absent' }
    Check ($now -ceq $propsBefore) ("$Label preserves local build-reference props")
}
foreach ($name in $controls) {
    $path = Join-Path $menuDepthSourceRoot $name
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { & (Join-Path $root 'scripts/install.ps1') -GamePath $GamePath -NoPackage } catch {
        if (-not $_.Exception.Message.Contains('Missing required menu depth companion: ' + $name)) { throw ("Missing-source control expected '$name', got: " + $_.Exception.Message) }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $expected[$name]) }
    Check $rejected ('Actual complete installer refuses missing source: ' + $name)
    Assert-NoMutation ('Missing source ' + $name)
}

$manifestPath = Join-Path $menuDepthSourceRoot 'Story/manifest.json'
$changed = [Text.Encoding]::UTF8.GetString($expected['Story/manifest.json']) | ConvertFrom-Json
$changed.images[0].depth_file = ('f' * 32) + '.gmd'
[IO.File]::WriteAllText($manifestPath, ($changed | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
$rejected = $false
try { & (Join-Path $root 'scripts/install.ps1') -GamePath $GamePath -NoPackage } catch {
    if (-not $_.Exception.Message.Contains('manifest and roster disagree')) { throw ('Manifest mismatch control got: ' + $_.Exception.Message) }
    $rejected = $true
} finally { [IO.File]::WriteAllBytes($manifestPath, $expected['Story/manifest.json']) }
Check $rejected 'Actual complete installer rejects manifest/roster mismatch'
Assert-NoMutation 'Manifest/roster mismatch'

$rosterBytes = [IO.File]::ReadAllBytes($menuDepthRoster)
$rosterText = [Text.Encoding]::UTF8.GetString($rosterBytes)
$rejected = $false
try {
    [IO.File]::WriteAllText($menuDepthRoster, $rosterText.Replace($fields[0], 'Story/../escape.gmd'), [Text.UTF8Encoding]::new($false))
    & (Join-Path $root 'scripts/install.ps1') -GamePath $GamePath -NoPackage
} catch {
    if (-not $_.Exception.Message.Contains('Invalid or duplicate menu depth roster path')) { throw }
    $rejected = $true
} finally { [IO.File]::WriteAllBytes($menuDepthRoster, $rosterBytes) }
Check $rejected 'Actual Windows installer rejects roster traversal'
Assert-NoMutation 'Unsafe roster'

foreach ($name in $controls) {
    $path = Join-Path $pluginDir ('MenuDepth/' + $name)
    Remove-Item -LiteralPath $path
    $rejected = $false
    try { . $package } catch {
        if (-not $_.Exception.Message.Contains('Missing required menu depth companion: ' + $name)) { throw ("Partial-install control expected '$name', got: " + $_.Exception.Message) }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($path, $expected[$name]) }
    Check $rejected ('Windows refuses partially installed companions before archive mutation: ' + $name)
    Assert-NoMutation ('Partial installation ' + $name)
}
$path = Join-Path $menuDepthSourceRoot $fields[0]
$rejected = $false
try {
    [IO.File]::WriteAllBytes($path, [byte[]]($expected[$fields[0]] + 42))
    & (Join-Path $root 'scripts/install.ps1') -GamePath $GamePath -NoPackage
} catch {
    if (-not $_.Exception.Message.Contains('size/checksum mismatch')) { throw }
    $rejected = $true
} finally { [IO.File]::WriteAllBytes($path, $expected[$fields[0]]) }
Check $rejected 'Actual Windows installer rejects corrupt source bytes'
Assert-NoMutation 'Corrupt source'

$needle = 'Remove-RetiredCalibrationMovies (Join-Path $stage "BepInEx\plugins\GloomhavenVR")'
Check ($packageText.Split([string[]]@($needle), [StringSplitOptions]::None).Length -eq 2) 'Depth ZIP causal controls bind actual production staging block'
foreach ($controlName in $controls) {
    $injection = $needle + "`n    Remove-Item -LiteralPath (Join-Path " + '$stage' + " 'BepInEx/plugins/GloomhavenVR/MenuDepth/$controlName')"
    $mutant = [scriptblock]::Create($packageText.Replace($needle, $injection))
    $rejected = $false
    try { . $mutant } catch {
        if (-not $_.Exception.Message.Contains('Missing required menu depth companion: ' + $controlName)) { throw ("Staged-loss control expected '$controlName', got: " + $_.Exception.Message) }
        $rejected = $true
    } finally { [IO.File]::WriteAllBytes($zip, $zipBefore) }
    Check $rejected ('Actual Windows ZIP validator rejects staged-copy loss: ' + $controlName)
}
foreach ($name in $menuDepthPaths) {
    Check ((Get-MenuDepthChecksum ([IO.File]::ReadAllBytes((Join-Path $menuDepthSourceRoot $name)))) -ceq (Get-MenuDepthChecksum $expected[$name])) `
        ('All source companions survive refusal and mutation controls: ' + $name)
}
Write-Output ("Menu depth PowerShell: $script:checks assertions; exact516 Debug/Release install and ZIP, complete-script missing/mismatch refusals, previous ZIP preservation and four archive-copy causal controls passed.")
