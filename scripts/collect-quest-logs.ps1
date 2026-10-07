<#
.SYNOPSIS
    Collect private Quest diagnostics into a timestamped ZIP on this PC.
.DESCRIPTION
    Reuse the installer's local Python/ADB and verified Wi-Fi endpoint, or read
    an authorized USB Quest directly. No APK, Unity or app restart is required.
#>
[CmdletBinding()]
param([string]$OutputRoot, [string]$Config, [string]$Adb,
      [string]$QuestHost, [string]$Serial, [switch]$Setup)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
try {
    $toolDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) "tools/quest-installer"
    . (Join-Path $toolDirectory "bootstrap.ps1")
    $pythonExecutable = Get-QuestInstallerPython -ScriptDirectory $PSScriptRoot `
        -RequirementsFile (Join-Path $toolDirectory "requirements.txt")
    $collectorArguments = @((Join-Path $PSScriptRoot "collect-quest-logs.py"))
    $options = @{ OutputRoot = "--output-root"; Config = "--config"; Adb = "--adb"; QuestHost = "--host"; Serial = "--serial" }
    foreach ($name in $options.Keys) {
        $value = Get-Variable -Name $name -ValueOnly
        if ($value) { $collectorArguments += @($options[$name], $value) }
    }
    if ($Setup) { $collectorArguments += "--setup" }
    & $pythonExecutable -I -B -X utf8 @collectorArguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Quest log collector: " + $_.Exception.Message)
    exit 1
}
