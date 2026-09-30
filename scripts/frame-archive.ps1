# Shared by the Windows installer and its focused archive regression test.
# ASCII source keeps Windows PowerShell 5.1 decoding independent of the system codepage.
function Write-UnixLauncher([string]$Source, [string]$Destination) {
    # PowerShell installations may run from a CRLF checkout. SteamOS needs LF
    # even in the Windows-built archive; a CRLF shebang resolves to "bash\r".
    $text = [System.IO.File]::ReadAllText($Source, $script:Utf8Strict)
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    [System.IO.File]::WriteAllText($Destination, $text, $script:Utf8Strict)
}

function Set-ZipUnixLaunchers([string]$ArchivePath) {
    # Compress-Archive writes DOS ZIP entries, which Dolphin extracts without
    # executable bits. Mark only the two launcher entries as Unix regular files
    # with mode 0755; the rest of the cross-platform archive is unchanged.
    $bytes = [System.IO.File]::ReadAllBytes($ArchivePath)
    $eocd = -1
    for ($i = $bytes.Length - 22; $i -ge [Math]::Max(0, $bytes.Length - 65557); $i--) {
        if ([BitConverter]::ToUInt32($bytes, $i) -eq 0x06054b50) { $eocd = $i; break }
    }
    if ($eocd -lt 0) { throw "ZIP central directory not found: $ArchivePath" }
    $count = [BitConverter]::ToUInt16($bytes, $eocd + 10)
    $offset = [BitConverter]::ToUInt32($bytes, $eocd + 16)
    if ($count -eq 0xFFFF -or $offset -eq 0xFFFFFFFF) {
        throw "ZIP64 archive is not supported by the Frame launcher mode patch: $ArchivePath"
    }
    $wanted = @(
        'BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh',
        'BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop')
    $found = @()
    $position = [int]$offset
    for ($n = 0; $n -lt $count; $n++) {
        if ([BitConverter]::ToUInt32($bytes, $position) -ne 0x02014b50) {
            throw "Invalid ZIP central directory entry $n in $ArchivePath"
        }
        $nameLength = [BitConverter]::ToUInt16($bytes, $position + 28)
        $extraLength = [BitConverter]::ToUInt16($bytes, $position + 30)
        $commentLength = [BitConverter]::ToUInt16($bytes, $position + 32)
        $name = [Text.Encoding]::UTF8.GetString($bytes, $position + 46, $nameLength).Replace('\', '/')
        if ($wanted -contains $name) {
            $bytes[$position + 5] = 3  # ZIP creator system: Unix
            # 0x81ED0000 = Unix regular file (0100755) in the ZIP upper word.
            # PowerShell 5.1 shifts Int32 before the UInt32 cast, yielding a
            # negative value that cannot be converted back to UInt32.
            $mode = [BitConverter]::GetBytes([uint32]2179792896)
            [Array]::Copy($mode, 0, $bytes, $position + 38, 4)
            $found += $name
        }
        $position += 46 + $nameLength + $extraLength + $commentLength
    }
    if ($found.Count -ne $wanted.Count) {
        throw "Frame launcher entries missing from ZIP: $ArchivePath"
    }
    [System.IO.File]::WriteAllBytes($ArchivePath, $bytes)
}
