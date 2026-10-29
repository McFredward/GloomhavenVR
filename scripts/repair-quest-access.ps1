<# Repair app access to the already-installed APK, without rebuilding or transferring game assets. #>
[CmdletBinding()]
param([string]$StateRoot = (Join-Path $env:USERPROFILE ".ghvrq"), [string]$Apk)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$options = @{
    Config = (Join-Path $StateRoot "device\wireless-install.json")
    RepairAppAccess = $true
}
if ($Apk) { $options.Apk = $Apk }
else { $options.OutputRoot = (Join-Path $StateRoot "build") }
& (Join-Path $PSScriptRoot "install-quest-wireless.ps1") @options
exit $LASTEXITCODE
