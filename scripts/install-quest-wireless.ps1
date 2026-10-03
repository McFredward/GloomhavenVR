<#
.SYNOPSIS
    Connect the remembered Quest over Wi-Fi, install the latest local APK and launch it.
.DESCRIPTION
    On the first run, connect and authorize the Quest over USB. The installer
    discovers its Wi-Fi address and remembers the device and APK source locally.
    Subsequent runs reconnect wirelessly. Python 3.9+ and Android platform-tools
    are required; Unity and the game installation are not needed to install.
.EXAMPLE
    .\scripts\install-quest-wireless.ps1
.EXAMPLE
    .\scripts\install-quest-wireless.ps1 -Handoff "D:\Quest builds\handoff.json"
.EXAMPLE
    .\scripts\install-quest-wireless.ps1 -OutputRoot "D:\Quest builds"
.EXAMPLE
    .\scripts\install-quest-wireless.ps1 -Setup
#>
[CmdletBinding()]
param(
    [string]$QuestHost,
    [string]$OutputRoot,
    [string]$Handoff,
    [string]$Apk,
    [string]$Adb,
    [string]$Serial,
    [string]$Config,
    [switch]$Setup,
    [switch]$NoLaunch,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    $pythonExecutable = $null
    $pythonPrefix = @()
    $candidates = @(
        @{ Name = "py"; Prefix = @("-3") },
        @{ Name = "python"; Prefix = @() },
        @{ Name = "python3"; Prefix = @() }
    )
    foreach ($candidate in $candidates) {
        $found = Get-Command $candidate.Name -CommandType Application -ErrorAction SilentlyContinue
        if (-not $found) { continue }
        # Store aliases can open a GUI instead of running an installed interpreter.
        if ($found.Source -match '[\\/]WindowsApps[\\/]') { continue }
        $prefix = $candidate.Prefix
        try {
            $versionText = & $found.Source @prefix --version
            if ($LASTEXITCODE -ne 0) { continue }
            if ($versionText -match '^Python (\d+)\.(\d+)') {
                $major = [int]$Matches[1]
                $minor = [int]$Matches[2]
                if ($major -gt 3 -or ($major -eq 3 -and $minor -ge 9)) {
                    $pythonExecutable = $found.Source
                    $pythonPrefix = $prefix
                    break
                }
            }
        } catch { continue }
    }
    if (-not $pythonExecutable) {
        throw "Python 3.9+ was not found. Install Python with its launcher or add an installed Python to PATH."
    }

    $installerArguments = @((Join-Path $PSScriptRoot "install-quest-wireless.py"))
    $options = @{
        QuestHost = "--host"; OutputRoot = "--output-root"; Handoff = "--handoff"
        Apk = "--apk"; Adb = "--adb"; Serial = "--serial"; Config = "--config"
    }
    foreach ($name in $options.Keys) {
        $value = (Get-Variable -Name $name -ValueOnly)
        if ($value) { $installerArguments += @($options[$name], $value) }
    }
    if ($Setup) { $installerArguments += "--setup" }
    if ($NoLaunch) { $installerArguments += "--no-launch" }
    if ($DryRun) { $installerArguments += "--dry-run" }
    & $pythonExecutable @pythonPrefix @installerArguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Quest installer: " + $_.Exception.Message)
    exit 1
}
