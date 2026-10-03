# Dot-source this file, then call Get-QuestInstallerPython. Windows uses a private,
# pinned CPython NuGet runtime; no system Python, registry or PATH is changed.
# Official distribution: https://docs.python.org/3/using/windows.html#the-nuget-org-packages

function Test-QuestWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Write-QuestBootstrapJson($Path, $Value) {
    $temporary = $Path + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $Path -Force -ErrorAction Stop
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

function Get-QuestOwnedState($Directory, $Kind) {
    if (-not (Test-Path -LiteralPath $Directory)) { return $null }
    $item = Get-Item -LiteralPath $Directory -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing an unmanaged file/link at $Directory. Choose a writable ordinary script folder."
    }
    try {
        $state = Get-Content -LiteralPath (Join-Path $Directory ".quest-owner.json") -Raw -ErrorAction Stop | ConvertFrom-Json
        if ($state.schema -eq 1 -and $state.owner -eq "GloomhavenVR.QuestInstaller" -and $state.kind -eq $Kind) {
            return $state
        }
    } catch { }
    throw "Refusing to change unowned folder $Directory. Move it aside yourself or choose another script folder."
}

function New-QuestOwnedDirectory($Directory, $Kind) {
    New-Item -ItemType Directory -Path $Directory -ErrorAction Stop | Out-Null
    Write-QuestBootstrapJson (Join-Path $Directory ".quest-owner.json") @{
        schema = 1; owner = "GloomhavenVR.QuestInstaller"; kind = $Kind; complete = $false
    }
}

function Remove-QuestOwnedDirectory($Directory, $Kind) {
    $null = Get-QuestOwnedState $Directory $Kind
    if (Test-QuestWindows) {
        $links = @(Get-ChildItem -LiteralPath $Directory -Force -Recurse -ErrorAction Stop |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw "Refusing to remove linked content inside $Directory. Move that folder aside yourself." }
    }
    Remove-Item -LiteralPath $Directory -Recurse -Force -ErrorAction Stop
}

function Enter-QuestBootstrapLock($Path, [int]$Attempts = 40) {
    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        try { return [IO.File]::Open($Path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [IO.IOException] { if ($attempt + 1 -lt $Attempts) { Start-Sleep -Milliseconds 250 } }
    }
    throw "Another Quest Python bootstrap is using this script folder. Wait for it to finish, then retry."
}

function Invoke-QuestPython($Executable, [string[]]$Arguments) {
    # PS 5.1 can classify harmless native stderr as an ErrorRecord. Capture it,
    # inspect the native exit status, and keep it out of the function's result.
    $ErrorActionPreference = "Continue"
    $PSNativeCommandUseErrorActionPreference = $false
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Local Python executable is missing: $Executable" }
    # Native launch errors need not set LASTEXITCODE. A stale successful probe
    # must never turn an unexecuted pip/venv command into a success receipt.
    $global:LASTEXITCODE = $null
    $output = @(& $Executable "-B" @Arguments 2>&1)
    $nativeExit = $global:LASTEXITCODE
    if ($null -eq $nativeExit -or $nativeExit -ne 0) {
        throw "Local Python command failed ($nativeExit): $($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine)
}

function Get-QuestPythonInfo($Executable) {
    $code = 'import importlib.util,json,os,sys;print(json.dumps(dict(prefix=os.path.abspath(sys.prefix),basePrefix=os.path.abspath(sys.base_prefix),baseExecutable=os.path.abspath(sys._base_executable),version=".".join(map(str,sys.version_info[:3])),hasPip=importlib.util.find_spec("pip") is not None)))'
    return (Invoke-QuestPython $Executable @("-I", "-c", $code) | ConvertFrom-Json)
}

function Get-QuestPythonSpec {
    $architecture = $env:PROCESSOR_ARCHITEW6432
    if (-not $architecture) { $architecture = $env:PROCESSOR_ARCHITECTURE }
    if (-not $architecture) { throw "Windows OS architecture could not be detected." }
    switch ($architecture.ToUpperInvariant()) {
        "AMD64" { $package = "python"; $sha = "ce85f674d9a63029f709cbff7a3da1c6bc5bfcfaefdd9999f98fa0290470c454"; $bytes = 15554015 }
        "ARM64" { $package = "pythonarm64"; $sha = "c6d3090da526fdd9d4ef2f7c27e1f85701033ccc19c1570d5bfc9281bbca4a1f"; $bytes = 14859489 }
        default { throw "This installer supports Windows 10/11 x64 or ARM64; unsupported OS architecture: $architecture" }
    }
    return @{ version = "3.14.8"; package = $package; sha256 = $sha; bytes = $bytes
        uri = "https://api.nuget.org/v3-flatcontainer/$package/3.14.8/$package.3.14.8.nupkg" }
}

function Receive-QuestPythonPackage($Uri, $Destination) {
    $originalTls = [Net.ServicePointManager]::SecurityProtocol
    try {
        [Net.ServicePointManager]::SecurityProtocol = $originalTls -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing -TimeoutSec 120 -ErrorAction Stop | Out-Null
    } finally { [Net.ServicePointManager]::SecurityProtocol = $originalTls }
}

function Test-QuestPackage($Path, $Spec) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        ((Get-Item -LiteralPath $Path).Length -eq $Spec.bytes) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $Spec.sha256)
}

function Expand-QuestPythonPackage($Package, $Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $root = [IO.Path]::GetFullPath($Destination) + [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -match '(^/|:|(^|/)\.\.(/|$)|(^|/)\.quest-owner\.json$)' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw "The Python archive contains an unsafe path: $name"
            }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $name))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Archive path escapes runtime cache: $name" }
        }
    } finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Package, $Destination)
}

function Test-QuestRuntime($Directory, $State, $Spec) {
    try {
        if (-not $State.complete -or $State.packageSha256 -ne $Spec.sha256 -or -not $State.files.Count) { return $false }
        foreach ($record in $State.files) {
            $file = Join-Path $Directory $record.path
            $root = [IO.Path]::GetFullPath($Directory) + [IO.Path]::DirectorySeparatorChar
            if (-not [IO.Path]::GetFullPath($file).StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $file -PathType Leaf) -or
                (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.sha256) { return $false }
        }
        return Test-Path -LiteralPath (Join-Path $Directory "tools/python.exe") -PathType Leaf
    } catch { return $false }
}

function Get-QuestBasePython($ScriptDirectory) {
    if (-not (Test-QuestWindows)) {
        foreach ($name in @("python3", "python")) {
            $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($command) {
                try {
                    $info = Get-QuestPythonInfo $command.Source
                    if ([version]$info.version -ge [version]"3.9") { return $command.Source }
                } catch { }
            }
        }
        throw "Python 3.9+ is required only for the non-Windows developer PowerShell path."
    }
    $spec = Get-QuestPythonSpec
    $cache = Join-Path $ScriptDirectory ".quest-python"
    $state = Get-QuestOwnedState $cache "cache"
    if (-not $state) { New-QuestOwnedDirectory $cache "cache" }
    $runtime = Join-Path $cache ($spec.package + "-" + $spec.version)
    $state = Get-QuestOwnedState $runtime "runtime"
    if (-not $state -or -not (Test-QuestRuntime $runtime $state $spec)) {
        $archive = Join-Path $cache ($spec.package + "-" + $spec.version + ".nupkg")
        if (-not (Test-QuestPackage $archive $spec)) {
            $partial = $archive + "." + [Guid]::NewGuid().ToString("N") + ".partial"
            try {
                Write-Host "Downloading pinned local CPython $($spec.version) ($($spec.package))..."
                Receive-QuestPythonPackage $spec.uri $partial
                if (-not (Test-QuestPackage $partial $spec)) { throw "CPython download SHA-256/size mismatch. No downloaded program was executed; retry on a reliable connection." }
                Move-Item -LiteralPath $partial -Destination $archive -Force -ErrorAction Stop
            } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
        }
        $stage = Join-Path $cache ("stage-" + [Guid]::NewGuid().ToString("N"))
        try {
            New-QuestOwnedDirectory $stage "runtime"
            Expand-QuestPythonPackage $archive $stage
            if (-not (Test-Path -LiteralPath (Join-Path $stage "tools/python.exe") -PathType Leaf)) { throw "The pinned package is missing tools/python.exe." }
            $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Where-Object { $_.Name -ne ".quest-owner.json" } | ForEach-Object {
                @{ path = $_.FullName.Substring($stage.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
            })
            Write-QuestBootstrapJson (Join-Path $stage ".quest-owner.json") @{
                schema = 1; owner = "GloomhavenVR.QuestInstaller"; kind = "runtime"; complete = $true
                packageSha256 = $spec.sha256; files = $files
            }
            if ($state) { Remove-QuestOwnedDirectory $runtime "runtime" }
            Move-Item -LiteralPath $stage -Destination $runtime -ErrorAction Stop
        } finally { if (Test-Path -LiteralPath $stage) { Remove-QuestOwnedDirectory $stage "runtime" } }
    }
    $python = Join-Path $runtime "tools/python.exe"
    $info = Get-QuestPythonInfo $python
    if ($info.version -ne $spec.version -or $info.prefix -ne (Join-Path $runtime "tools")) {
        throw "Pinned CPython runtime identity/version check failed; no installer or pip operation ran."
    }
    return $python
}

function Get-QuestInstallerPython {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ScriptDirectory,
          [Parameter(Mandatory = $true)][string]$RequirementsFile)
    $directory = [IO.Path]::GetFullPath($ScriptDirectory)
    $requirements = [IO.Path]::GetFullPath($RequirementsFile)
    if (-not (Test-Path -LiteralPath $directory -PathType Container) -or
        -not (Test-Path -LiteralPath $requirements -PathType Leaf)) { throw "The installer script directory or pinned requirements manifest is missing." }
    $lock = Enter-QuestBootstrapLock (Join-Path $directory ".quest-bootstrap.lock")
    try {
        $base = Get-QuestBasePython $directory
        $baseInfo = Get-QuestPythonInfo $base
        $hash = (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash.ToLowerInvariant()
        $dependencies = @(Get-Content -LiteralPath $requirements | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith("#") })
        $environment = Join-Path $directory ".quest-venv"
        $python = if (Test-QuestWindows) { Join-Path $environment "Scripts/python.exe" } else { Join-Path $environment "bin/python" }
        $state = Get-QuestOwnedState $environment "venv"
        $valid = $false
        try {
            if ($state.complete -and $state.directory -eq $directory -and $state.baseExecutable -eq $baseInfo.baseExecutable -and
                $state.version -eq $baseInfo.version) {
                $info = Get-QuestPythonInfo $python
                $valid = $info.prefix -eq $environment -and $info.prefix -ne $info.basePrefix -and
                    $info.basePrefix -eq $baseInfo.basePrefix -and $info.baseExecutable -eq $baseInfo.baseExecutable -and $info.version -eq $baseInfo.version
            }
        } catch { $valid = $false }
        if (-not $valid) {
            if ($state) { Remove-QuestOwnedDirectory $environment "venv" }
            New-QuestOwnedDirectory $environment "venv"
            Write-Host "Creating isolated installer Python in $environment..."
            $null = Invoke-QuestPython $base @("-I", "-m", "venv", "--without-pip", $environment)
            $info = Get-QuestPythonInfo $python
            if ($info.prefix -ne $environment -or $info.prefix -eq $info.basePrefix -or
                $info.basePrefix -ne $baseInfo.basePrefix -or $info.baseExecutable -ne $baseInfo.baseExecutable -or $info.version -ne $baseInfo.version) {
                throw "The new installer environment failed its Python/venv isolation checks. Retry to recreate the owned environment."
            }
        }
        if (-not $valid -or $state.requirementsSha256 -ne $hash -or ($dependencies.Count -and -not $info.hasPip)) {
            if ($dependencies.Count) {
                if (-not $info.hasPip) { $null = Invoke-QuestPython $python @("-I", "-m", "ensurepip", "--default-pip") }
                Write-Host "Installing the installer's hash-pinned packages into its local environment..."
                $null = Invoke-QuestPython $python @("-I", "-m", "pip", "--isolated", "install", "--disable-pip-version-check", "--no-input", "--no-cache-dir", "--require-hashes", "-r", $requirements)
            }
        }
        if ((Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Requirements changed during setup. Retry before installing an APK." }
        Write-QuestBootstrapJson (Join-Path $environment ".quest-owner.json") @{
            schema = 1; owner = "GloomhavenVR.QuestInstaller"; kind = "venv"; complete = $true
            directory = $directory; baseExecutable = $baseInfo.baseExecutable; version = $baseInfo.version; requirementsSha256 = $hash
        }
        return $python
    } finally { $lock.Dispose() }
}
