param([Parameter(Mandatory = $true)][string]$OutputZip)

$ErrorActionPreference = 'Stop'
$script:Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
. (Join-Path $PSScriptRoot 'frame-archive.ps1')

$stage = Join-Path (Split-Path -Parent $OutputZip) 'frame-stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$frameSetup = Join-Path $stage 'BepInEx/plugins/GloomhavenVR/FrameSetup'
New-Item -ItemType Directory -Force -Path $frameSetup | Out-Null
$shellSource = Join-Path $stage 'source.sh'
$desktopSource = Join-Path $stage 'source.desktop'
$pythonSource = Join-Path $stage 'source.py'
[System.IO.File]::WriteAllText($shellSource, "#!/usr/bin/env bash`r`necho ready`r`n", $script:Utf8Strict)
[System.IO.File]::WriteAllText($desktopSource, "[Desktop Entry]`r`nType=Application`r`n", $script:Utf8Strict)
[System.IO.File]::WriteAllText($pythonSource, "#!/usr/bin/env python3`r`nprint('ready')`r`n", $script:Utf8Strict)
Write-UnixLauncher $shellSource (Join-Path $frameSetup 'install-steam-frame.sh')
Write-UnixLauncher $desktopSource (Join-Path $frameSetup 'GloomhavenVR-Setup.desktop')
Write-UnixLauncher $pythonSource (Join-Path $frameSetup 'steam-frame-config.py')
Write-UnixLauncher $pythonSource (Join-Path $frameSetup 'frame-boot-config.py')
[System.IO.File]::WriteAllBytes((Join-Path $frameSetup 'GloomhavenVR-steam-logo.png'), [byte[]]@(137, 80, 78, 71))
[System.IO.File]::WriteAllBytes((Join-Path $frameSetup 'GloomhavenVR-steam-icon.png'), [byte[]]@(137, 80, 78, 71))
[System.IO.File]::WriteAllText((Join-Path $stage 'INSTALL.txt'), 'test', $script:Utf8Strict)
[System.IO.File]::WriteAllText((Join-Path $stage 'INSTALL-DEUTSCH.txt'), 'test', $script:Utf8Strict)
Remove-Item -LiteralPath $shellSource, $desktopSource, $pythonSource
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputZip -Force
Set-ZipUnixLaunchers $OutputZip
Write-Host 'PowerShell Frame archive: created.'
