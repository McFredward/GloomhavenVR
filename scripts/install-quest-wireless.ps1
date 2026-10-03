<#
.SYNOPSIS
    Connect the remembered Quest over Wi-Fi, install the latest local APK and launch it.
.DESCRIPTION
    On the first run, connect and authorize the Quest over USB. The installer
    discovers its Wi-Fi address and remembers the device and APK source locally.
    Subsequent runs reconnect wirelessly. A script-local Python runtime and
    virtual environment are provisioned automatically on Windows. Android
    platform-tools are required; Unity and the game installation are not needed.
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
    $toolDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) "tools/quest-installer"
    . (Join-Path $toolDirectory "bootstrap.ps1")
    $pythonExecutable = Get-QuestInstallerPython -ScriptDirectory $PSScriptRoot `
        -RequirementsFile (Join-Path $toolDirectory "requirements.txt")

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
    & $pythonExecutable -I -B -X utf8 @installerArguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Quest installer: " + $_.Exception.Message)
    exit 1
}
