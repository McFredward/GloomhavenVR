param([Parameter(Mandatory = $true)][string]$OutputZip)

$ErrorActionPreference = 'Stop'
$script:Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
. (Join-Path $PSScriptRoot 'frame-archive.ps1')

$stage = Join-Path (Split-Path -Parent $OutputZip) 'frame-stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$shellSource = Join-Path $stage 'source.sh'
$desktopSource = Join-Path $stage 'source.desktop'
[System.IO.File]::WriteAllText($shellSource, "#!/usr/bin/env bash`r`necho ready`r`n", $script:Utf8Strict)
[System.IO.File]::WriteAllText($desktopSource, "[Desktop Entry]`r`nType=Application`r`n", $script:Utf8Strict)
Write-UnixLauncher $shellSource (Join-Path $stage 'install-steam-frame.sh')
Write-UnixLauncher $desktopSource (Join-Path $stage 'GloomhavenVR-Setup.desktop')
Remove-Item -LiteralPath $shellSource, $desktopSource
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputZip -Force
Set-ZipUnixLaunchers $OutputZip
Write-Host 'PowerShell Frame archive: created.'
