"""Run the actual PowerShell bootstrap with local venvs and bounded fixtures."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
BOOTSTRAP = ROOT / "tools/quest-installer/bootstrap.ps1"
PWSH = os.environ.get("GHVR_PWSH_PATH") or shutil.which("pwsh")


def ps_string(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(PWSH, "PowerShell unavailable; set GHVR_PWSH_PATH to exercise bootstrap runtime tests")
class BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest bootstrap spaces ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.scripts = self.root / "script folder's space"
        self.scripts.mkdir()
        self.requirements = self.root / "requirements.txt"
        self.requirements.write_text("# standard library only\n")
        self.prefix = ("$ErrorActionPreference='Stop'\nSet-StrictMode -Version Latest\n. " + ps_string(BOOTSTRAP)
                       + "\n$d=" + ps_string(self.scripts) + "\n$r=" + ps_string(self.requirements) + "\n")

    def run_ps(self, body, success=True):
        script = self.root / "test.ps1"
        script.write_text(self.prefix + body, encoding="utf-8")
        result = subprocess.run([PWSH, "-NoProfile", "-File", str(script)], capture_output=True,
                                text=True, encoding="utf-8", timeout=30)
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def setup_venv(self):
        result = self.run_ps("$p=Get-QuestInstallerPython $d $r; Write-Output ('RESULT:' + $p)")
        return Path(next(line[7:] for line in result.stdout.splitlines() if line.startswith("RESULT:")))

    def state(self):
        return json.loads((self.scripts / ".quest-venv/.quest-owner.json").read_text(encoding="utf-8-sig"))

    def windows_fixture(self, malformed=None):
        archive = self.root / "python fixture.nupkg"
        with zipfile.ZipFile(archive, "w") as zip_file:
            zip_file.writestr("tools/python.exe", "never executed fixture")
            zip_file.writestr("tools/Lib/venv/__init__.py", "fixture")
            if malformed:
                zip_file.writestr(malformed, "must not be extracted")
        sha = hashlib.sha256(archive.read_bytes()).hexdigest()
        # Native runtime launch is the sole mock: the extracted Windows PE cannot
        # execute on Linux. Download/hash/extraction/inventory are the real code.
        return """
function Test-QuestWindows { return $true }
function Get-QuestPythonSpec { return @{version='3.14.8';package='python';uri='fixture';sha256='%s';bytes=%d} }
function Receive-QuestPythonPackage($Uri,$Destination) { Copy-Item -LiteralPath %s -Destination $Destination }
function Get-QuestPythonInfo($Executable) { return @{version='3.14.8';prefix=(Split-Path $Executable -Parent)} }
""" % (sha, archive.stat().st_size, ps_string(archive))

    def test_actual_first_setup_is_local_isolated_and_returns_one_path(self):
        python = self.setup_venv()
        self.assertTrue(python.is_file())
        info = subprocess.check_output([str(python), "-I", "-c",
                                       "import sys,json;print(json.dumps([sys.prefix,sys.base_prefix]))"], text=True)
        prefix, base = json.loads(info)
        self.assertEqual(Path(prefix), self.scripts / ".quest-venv")
        self.assertNotEqual(prefix, base)
        self.assertEqual(self.state()["requirementsSha256"], hashlib.sha256(self.requirements.read_bytes()).hexdigest())

    def test_valid_cache_reuse_is_offline_and_does_not_create_or_run_pip(self):
        self.setup_venv()
        result = self.run_ps("""
$native = ${function:Invoke-QuestPython}
function Invoke-QuestPython($Executable,[string[]]$Arguments) {
    if ($Arguments -contains 'venv' -or $Arguments -contains 'pip' -or $Arguments -contains 'ensurepip') { throw 'Unexpected environment mutation' }
    return & $native $Executable $Arguments
}
function Receive-QuestPythonPackage { throw 'Unexpected network request' }
$p=Get-QuestInstallerPython $d $r; Write-Output ('RESULT:' + $p)
""")
        self.assertIn("RESULT:", result.stdout)
        self.assertNotIn("Creating isolated", result.stdout)

    def test_moved_folder_is_recreated_for_its_current_location(self):
        self.setup_venv()
        original = self.state()["directory"]
        moved = self.root / "copied scripts"
        shutil.copytree(self.scripts, moved, symlinks=True)
        result = self.run_ps("$d=" + ps_string(moved) + "; $p=Get-QuestInstallerPython $d $r; Write-Output $p")
        state = json.loads((moved / ".quest-venv/.quest-owner.json").read_text(encoding="utf-8-sig"))
        self.assertNotEqual(original, state["directory"])
        self.assertEqual(state["directory"], str(moved))
        self.assertIn("Creating isolated", result.stdout)

    def test_incomplete_owned_environment_is_repaired(self):
        self.run_ps("New-QuestOwnedDirectory (Join-Path $d '.quest-venv') 'venv'")
        self.assertFalse(self.state()["complete"])
        self.setup_venv()
        self.assertTrue(self.state()["complete"])

    def test_broken_owned_interpreter_is_recreated(self):
        python = self.setup_venv()
        python.unlink()
        self.setup_venv()
        self.assertTrue(python.is_file())

    def test_unknown_environment_is_retained(self):
        directory = self.scripts / ".quest-venv"
        directory.mkdir()
        marker = directory / "valuable-user-file"
        marker.write_text("preserve")
        result = self.run_ps("Get-QuestInstallerPython $d $r", success=False)
        self.assertIn("unowned folder", result.stderr)
        self.assertEqual(marker.read_text(), "preserve")

    def test_exclusive_lock_blocks_a_second_bootstrap(self):
        self.run_ps("""
$path=Join-Path $d '.quest-bootstrap.lock'
$first=Enter-QuestBootstrapLock $path
try {
    try { $second=Enter-QuestBootstrapLock $path -Attempts 1; $second.Dispose(); throw 'Second lock acquired' }
    catch { if ($_.Exception.Message -notmatch 'Another Quest Python bootstrap') { throw }; Write-Output 'LOCK-BLOCKED' }
} finally { $first.Dispose() }
$after=Enter-QuestBootstrapLock $path -Attempts 1; $after.Dispose()
""")

    def test_venv_creation_failure_never_gets_a_complete_receipt_and_retry_repairs(self):
        result = self.run_ps("""
$native=${function:Invoke-QuestPython}
function Invoke-QuestPython($Executable,[string[]]$Arguments) {
    if ($Arguments -contains 'venv') { throw 'fixture venv creation failure' }
    return & $native $Executable $Arguments
}
Get-QuestInstallerPython $d $r
""", success=False)
        self.assertIn("venv creation failure", result.stderr)
        self.assertFalse(self.state()["complete"])
        self.setup_venv()
        self.assertTrue(self.state()["complete"])

    def test_active_requirements_require_hashes_and_cache_only_actual_success(self):
        self.setup_venv()
        old = self.state()["requirementsSha256"]
        self.requirements.write_text("fixture-package==1.0 --hash=sha256:" + "0" * 64 + "\n")
        result = self.run_ps("""
$native=${function:Invoke-QuestPython}
function Invoke-QuestPython($Executable,[string[]]$Arguments) {
    if ($Arguments -contains 'ensurepip') { return 'fixture offline ensurepip' }
    if ($Arguments -contains 'pip') {
        if ($Arguments -notcontains '--require-hashes' -or $Arguments -notcontains '--isolated') { throw 'Missing pip hash/isolation flags' }
        throw 'fixture pip failure'
    }
    return & $native $Executable $Arguments
}
Get-QuestInstallerPython $d $r
""", success=False)
        self.assertIn("fixture pip failure", result.stderr)
        self.assertEqual(self.state()["requirementsSha256"], old)
        self.run_ps("""
$native=${function:Invoke-QuestPython}
function Invoke-QuestPython($Executable,[string[]]$Arguments) {
    if ($Arguments -contains 'ensurepip' -or $Arguments -contains 'pip') { return 'fixture successful local dependency operation' }
    return & $native $Executable $Arguments
}
Get-QuestInstallerPython $d $r | Out-Null
""")
        self.assertNotEqual(self.state()["requirementsSha256"], old)

    def test_comment_only_requirements_update_does_not_run_pip(self):
        self.setup_venv()
        self.requirements.write_text("# new comment-only manifest\n\n")
        self.run_ps("""
$native=${function:Invoke-QuestPython}
function Invoke-QuestPython($Executable,[string[]]$Arguments) {
    if ($Arguments -contains 'pip' -or $Arguments -contains 'ensurepip') { throw 'Unexpected pip operation' }
    return & $native $Executable $Arguments
}
Get-QuestInstallerPython $d $r | Out-Null
""")
        self.assertEqual(self.state()["requirementsSha256"], hashlib.sha256(self.requirements.read_bytes()).hexdigest())

    @unittest.skipUnless(importlib.util.find_spec("ensurepip"), "Optional dependency control needs the host's offline ensurepip")
    def test_actual_offline_hash_pinned_wheel_installs_only_inside_venv(self):
        wheel = self.root / "quest_bootstrap_fixture-1.0-py3-none-any.whl"
        files = {"quest_bootstrap_fixture.py": "VALUE = 'local fixture'\n",
                 "quest_bootstrap_fixture-1.0.dist-info/METADATA": "Metadata-Version: 2.1\nName: quest-bootstrap-fixture\nVersion: 1.0\n",
                 "quest_bootstrap_fixture-1.0.dist-info/WHEEL": "Wheel-Version: 1.0\nGenerator: local-test\nRoot-Is-Purelib: true\nTag: py3-none-any\n"}
        record = "quest_bootstrap_fixture-1.0.dist-info/RECORD"
        files[record] = "".join(path + ",,\n" for path in [*files, record])
        with zipfile.ZipFile(wheel, "w") as archive:
            for path, value in files.items():
                archive.writestr(path, value)
        sha = hashlib.sha256(wheel.read_bytes()).hexdigest()
        self.requirements.write_text("--no-index\n" + wheel.as_uri() + " --hash=sha256:" + sha + "\n")
        python = self.setup_venv()
        installed = subprocess.check_output([str(python), "-I", "-c",
                                            "import quest_bootstrap_fixture;print(quest_bootstrap_fixture.__file__)"], text=True).strip()
        self.assertIn(self.scripts / ".quest-venv", Path(installed).parents)
        cached = self.state()["requirementsSha256"]
        self.requirements.write_text("--no-index\n" + wheel.as_uri() + " --hash=sha256:" + "0" * 64 + "\n")
        result = self.run_ps("Get-QuestInstallerPython $d $r", success=False)
        self.assertIn("command failed", result.stderr)
        self.assertEqual(self.state()["requirementsSha256"], cached)

    def test_windows_package_pin_and_architecture_selection(self):
        result = self.run_ps("""
$env:PROCESSOR_ARCHITEW6432='AMD64'; $env:PROCESSOR_ARCHITECTURE='x86'
$a=Get-QuestPythonSpec
$env:PROCESSOR_ARCHITEW6432='ARM64'; $b=Get-QuestPythonSpec
Write-Output ('RESULT:' + (@($a,$b) | ConvertTo-Json -Compress))
""")
        specs = json.loads(next(line[7:] for line in result.stdout.splitlines() if line.startswith("RESULT:")))
        self.assertEqual([spec["package"] for spec in specs], ["python", "pythonarm64"])
        self.assertEqual(specs[0]["sha256"], "ce85f674d9a63029f709cbff7a3da1c6bc5bfcfaefdd9999f98fa0290470c454")
        self.assertEqual(specs[1]["bytes"], 14859489)
        self.assertTrue(all(spec["uri"].startswith("https://api.nuget.org/v3-flatcontainer/") for spec in specs))

    def test_windows_package_cache_repairs_tampering_offline(self):
        result = self.run_ps(self.windows_fixture() + """
$p=Get-QuestBasePython $d
[IO.File]::WriteAllText($p,'corrupted interpreter')
function Receive-QuestPythonPackage { throw 'Unexpected download during cached repair' }
$p=Get-QuestBasePython $d
if ((Get-Content -LiteralPath $p -Raw) -ne 'never executed fixture') { throw 'Cache not repaired' }
Write-Output 'CACHE-REPAIRED'
""")
        self.assertIn("CACHE-REPAIRED", result.stdout)

    def test_hash_mismatched_download_is_never_extracted_or_executed(self):
        result = self.run_ps(self.windows_fixture() + """
function Receive-QuestPythonPackage($Uri,$Destination) { [IO.File]::WriteAllText($Destination,'tampered download') }
function Expand-QuestPythonPackage { throw 'UNSAFE-EXTRACTION' }
function Get-QuestPythonInfo { throw 'UNSAFE-EXECUTION' }
Get-QuestBasePython $d
""", success=False)
        self.assertIn("SHA-256/size mismatch", result.stderr)
        self.assertNotIn("UNSAFE", result.stderr)
        self.assertFalse(list((self.scripts / ".quest-python").glob("*.partial")))

    def test_network_failure_is_actionable_and_removes_partial(self):
        result = self.run_ps(self.windows_fixture() + """
function Receive-QuestPythonPackage($Uri,$Destination) {
    [IO.File]::WriteAllText($Destination,'partial'); throw 'fixture download unavailable'
}
Get-QuestBasePython $d
""", success=False)
        self.assertIn("download unavailable", result.stderr)
        self.assertFalse(list((self.scripts / ".quest-python").glob("*.partial")))

    def test_zip_path_escape_is_rejected_without_outside_writes(self):
        result = self.run_ps(self.windows_fixture("../../outside.txt") + "Get-QuestBasePython $d", success=False)
        self.assertIn("unsafe path", result.stderr)
        self.assertFalse((self.scripts / "outside.txt").exists())
        self.assertFalse(list((self.scripts / ".quest-python").glob("stage-*")))

    def test_unowned_windows_cache_is_never_modified(self):
        cache = self.scripts / ".quest-python"
        cache.mkdir()
        marker = cache / "user-file"
        marker.write_text("retain")
        result = self.run_ps(self.windows_fixture() + "Get-QuestBasePython $d", success=False)
        self.assertIn("unowned folder", result.stderr)
        self.assertEqual(marker.read_text(), "retain")

    def test_real_native_nonzero_exit_is_checked(self):
        result = self.run_ps("$p=Get-QuestBasePython $d; Invoke-QuestPython $p @('-I','-c','import sys;sys.exit(17)')", success=False)
        self.assertIn("command failed (17)", result.stderr)

    def test_missing_executable_cannot_reuse_stale_zero_exit_status(self):
        result = self.run_ps("$global:LASTEXITCODE=0; Invoke-QuestPython (Join-Path $d 'missing-python.exe') @('--version')", success=False)
        self.assertIn("executable is missing", result.stderr)

    def test_existing_invalid_executable_is_a_launch_failure(self):
        invalid = self.root / "invalid-python.exe"
        invalid.write_bytes(b"not an executable")
        result = self.run_ps("$global:LASTEXITCODE=0; Invoke-QuestPython " + ps_string(invalid) + " @('--version')", success=False)
        self.assertIn("command failed", result.stderr)


if __name__ == "__main__":
    unittest.main()
