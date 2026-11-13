<# Reuse the same isolated local Python as the Quest installer/log collector. #>
[CmdletBinding()]
param([ValidateSet("export-pc", "import-pc", "export-quest", "import-quest", "validate")]
      [string]$Command = "export-quest", [string]$SaveRoot, [string]$Archive,
      [string]$Output, [string]$Config, [string]$Adb, [string]$QuestHost,
      [string]$Serial, [switch]$Setup, [switch]$Replace)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
try {
    $toolDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) "tools/quest-installer"
    . (Join-Path $toolDirectory "bootstrap.ps1")
    $pythonExecutable = Get-QuestInstallerPython -ScriptDirectory $PSScriptRoot `
        -RequirementsFile (Join-Path $toolDirectory "requirements.txt")
    if ($Command -eq "export-quest" -and -not $Output) {
        $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
        $Output = Join-Path (Split-Path $PSScriptRoot -Parent) "quest-saves-$stamp.zip"
    }
    $questSaveArguments = @((Join-Path $PSScriptRoot "quest-saves.py"), $Command)
    $options = @{ SaveRoot = "--save-root"; Archive = "--archive"; Output = "--output";
                  Config = "--config"; Adb = "--adb"; QuestHost = "--host"; Serial = "--serial" }
    foreach ($name in $options.Keys) {
        $value = Get-Variable -Name $name -ValueOnly
        if ($value) { $questSaveArguments += @($options[$name], $value) }
    }
    if ($Setup) { $questSaveArguments += "--setup" }
    if ($Replace) { $questSaveArguments += "--replace" }
    & $pythonExecutable -I -B -X utf8 @questSaveArguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Quest save transfer: " + $_.Exception.Message)
    exit 1
}
