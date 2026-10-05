<#
.SYNOPSIS
    Open the local GloomhavenVR Quest build wizard in the default browser.
.DESCRIPTION
    Reuse the installer-owned pinned Python and script-local virtual environment.
    No global Python, PATH, registry or browser extension is installed. The wizard
    backend owns its loopback server, planning, build and cancellation lifecycle.
#>
[CmdletBinding()]
param([string]$StateRoot, [switch]$NoBrowser)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
try {
    $sourceDirectory = Split-Path $PSScriptRoot -Parent
    $bootstrapDirectory = Join-Path $sourceDirectory "tools/quest-installer"
    $wizardScript = Join-Path $sourceDirectory "tools/quest-wizard/wizard.py"
    $uiDirectory = Join-Path $sourceDirectory "tools/quest-wizard-ui"
    if (-not (Test-Path -LiteralPath $wizardScript -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $uiDirectory "index.html") -PathType Leaf)) {
        throw "The Quest Wizard files are missing. Extract the complete matching release or checkout."
    }
    . (Join-Path $bootstrapDirectory "bootstrap.ps1")
    $pythonExecutable = Get-QuestInstallerPython -ScriptDirectory $PSScriptRoot `
        -RequirementsFile (Join-Path $bootstrapDirectory "requirements.txt")
    if (-not $StateRoot) {
        # Keep generated Unity paths short and retain resumable builds when the
        # downloaded source folder is replaced or moved. Python stays script-local.
        $questUserProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if (-not $questUserProfile) { throw "The Windows user profile folder could not be determined." }
        $StateRoot = Join-Path $questUserProfile ".ghvrq"
    }
    $wizardArguments = @($wizardScript, "serve", "--state-root", $StateRoot, "--ui-root", $uiDirectory)
    if (-not $NoBrowser) { $wizardArguments += "--open-browser" }
    Write-Host "GloomhavenVR Quest Wizard - local browser interface"
    Write-Host "Keep this launch window open while using the wizard."
    & $pythonExecutable -I -B -X utf8 @wizardArguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Quest Wizard: " + $_.Exception.Message)
    exit 1
}
