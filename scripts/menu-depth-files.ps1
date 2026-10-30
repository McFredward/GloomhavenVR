# Native PowerShell preflight: Windows installations do not require Python.
function Get-MenuDepthRoster([string]$RosterPath) {
    if (-not (Test-Path -LiteralPath $RosterPath -PathType Leaf)) { throw "Missing menu depth roster: $RosterPath" }
    $names = @(Get-Content -LiteralPath $RosterPath -Encoding UTF8 | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') })
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $names) {
        if (($name -cne 'Story/manifest.json' -and $name -cnotmatch '^Story/[0-9a-f]{32}\.gmd$') -or -not $seen.Add($name)) {
            throw "Invalid or duplicate menu depth roster path: $name"
        }
    }
    if ($names.Count -ne 516 -or -not $seen.Contains('Story/manifest.json')) {
        throw 'Menu depth roster must contain exactly 516 distinct companions'
    }
    return $names
}

function Get-MenuDepthChecksum([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Assert-MenuDepthDimensions($Axes, [int]$Maximum) {
    if ($null -eq $Axes -or @($Axes).Count -ne 2) { throw 'Invalid menu depth dimensions' }
    foreach ($axis in $Axes) {
        if (($axis -isnot [int] -and $axis -isnot [long]) -or $axis -lt 1 -or $axis -gt $Maximum) {
            throw 'Invalid menu depth dimensions'
        }
    }
}

function Assert-MenuDepthSet([string[]]$Names, [string[]]$Present, [scriptblock]$ReadBytes) {
    $actual = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $Present) {
        if ($name.EndsWith('.gmd', [StringComparison]::Ordinal) -or $name -ceq 'Story/manifest.json') {
            if (-not $actual.Add($name)) { throw "Duplicate menu depth archive companion: $name" }
        }
    }
    foreach ($name in $Names) {
        if (-not $actual.Contains($name)) { throw "Missing required menu depth companion: $name" }
    }
    if ($actual.Count -ne $Names.Count) { throw 'Menu depth files do not match finite roster' }
    [byte[]]$json = & $ReadBytes 'Story/manifest.json' (4MB)
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $document = $strictUtf8.GetString($json) | ConvertFrom-Json
    if ($document.schema -ne 1 -or $document.status -cne 'complete' -or $document.format -cne 'GMD1') {
        throw 'Invalid menu depth manifest schema/status/format'
    }
    if ($null -eq $document.images -or @($document.images).Count -ne 515) {
        throw 'Menu depth manifest must contain exactly 515 fields'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($image in $document.images) {
        $source = $image.source_id
        if ($source -isnot [string] -or $source -cnotmatch '^[0-9a-f]{32}$' -or -not $seen.Add($source)) {
            throw 'Invalid or duplicate menu depth source identity'
        }
        $relative = 'Story/' + $source + '.gmd'
        if ($image.depth_file -cne ($source + '.gmd') -or -not $actual.Contains($relative)) {
            throw "Menu depth manifest and roster disagree: $relative"
        }
        Assert-MenuDepthDimensions $image.depth_dimensions 512
        Assert-MenuDepthDimensions $image.source_dimensions 16384
        $width = [int]$image.depth_dimensions[0]; $height = [int]$image.depth_dimensions[1]
        $expectedSize = 32 + $width * $height * 2
        if (($image.depth_bytes -isnot [int] -and $image.depth_bytes -isnot [long]) -or $image.depth_bytes -ne $expectedSize) {
            throw "Invalid menu depth manifest byte count: $relative"
        }
        if ($image.depth_sha256 -isnot [string] -or $image.depth_sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Invalid menu depth manifest checksum: $relative"
        }
        [byte[]]$bytes = & $ReadBytes $relative (32 + 512 * 512 * 2)
        if ($bytes.Length -ne $expectedSize -or (Get-MenuDepthChecksum $bytes) -cne $image.depth_sha256) {
            throw "Menu depth companion size/checksum mismatch: $relative"
        }
        if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -cne 'GMD1' -or
            [BitConverter]::ToUInt16($bytes, 4) -ne 1 -or [BitConverter]::ToUInt16($bytes, 6) -ne 32 -or
            [BitConverter]::ToUInt16($bytes, 8) -ne $width -or [BitConverter]::ToUInt16($bytes, 10) -ne $height -or
            [BitConverter]::ToUInt32($bytes, 12) -ne 3 -or [BitConverter]::ToUInt32($bytes, 16) -ne ($expectedSize - 32) -or
            [BitConverter]::ToUInt32($bytes, 24) -ne $image.source_dimensions[0] -or
            [BitConverter]::ToUInt32($bytes, 28) -ne $image.source_dimensions[1]) {
            throw "Menu depth companion header mismatch: $relative"
        }
    }
}

function Get-MenuDepthFiles([string]$Directory, [string]$RosterPath) {
    $names = @(Get-MenuDepthRoster $RosterPath)
    $story = Join-Path $Directory 'Story'
    if ((Test-Path -LiteralPath $story) -and ((Get-Item -LiteralPath $story).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Menu depth Story folder must not be a symbolic link'
    }
    $present = @()
    if (Test-Path -LiteralPath $Directory -PathType Container) {
        $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $present = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object {
            $_.FullName.Substring($prefix.Length).Replace('\', '/')
        })
    }
    $read = {
        param([string]$Relative, [int]$Bound)
        $path = Join-Path $Directory $Relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required menu depth companion: $Relative" }
        $item = Get-Item -LiteralPath $path
        if ($item.Length -gt $Bound -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Invalid menu depth companion: $Relative"
        }
        return ,([IO.File]::ReadAllBytes($path))
    }.GetNewClosure()
    Assert-MenuDepthSet $names $present $read
    return $names
}

function Assert-MenuDepthArchive($Archive, [string]$RosterPath) {
    $names = @(Get-MenuDepthRoster $RosterPath)
    $prefix = 'BepInEx/plugins/GloomhavenVR/MenuDepth/'
    $present = @($Archive.Entries | Where-Object { $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal) -and -not $_.FullName.EndsWith('/') } |
        ForEach-Object { $_.FullName.Substring($prefix.Length) })
    $read = {
        param([string]$Relative, [int]$Bound)
        $entry = $Archive.GetEntry($prefix + $Relative)
        if ($entry.Length -gt $Bound) { throw "Oversized menu depth archive companion: $Relative" }
        $buffer = [IO.MemoryStream]::new()
        $stream = $entry.Open()
        try { $stream.CopyTo($buffer); return ,($buffer.ToArray()) }
        finally { $stream.Dispose(); $buffer.Dispose() }
    }.GetNewClosure()
    Assert-MenuDepthSet $names $present $read
}
