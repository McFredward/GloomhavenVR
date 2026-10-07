"""Pinned managed codecs; no compiler or native decoder toolchain is required."""
import hashlib
import json
from pathlib import Path
import subprocess

from recover import RecoveryError, sha256, write_json

SOURCE = Path(__file__).with_name("NativePortableRecovery")


def build(cache, dotnet):
    inputs = [SOURCE / name for name in ("NativePortableRecovery.csproj", "Program.cs", "packages.lock.json")]
    fingerprint = hashlib.sha256(b"".join(path.read_bytes() for path in inputs)).hexdigest()
    directory = Path(cache) / ("native-codecs-" + fingerprint[:16])
    directory.mkdir(parents=True, exist_ok=True)
    receipt = directory / "codec-build.json"
    assembly = directory / "NativePortableRecovery.dll"
    if receipt.is_file():
        previous = json.loads(receipt.read_text())
        if previous["sourceFingerprint"] != fingerprint:
            raise RecoveryError("Managed native-codec cache source changed.")
        for row in previous["files"]:
            if sha256(directory / row["path"]) != row["sha256"]:
                raise RecoveryError("Managed native-codec executable cache changed.")
    else:
        log = directory / "build.log"
        with log.open("w") as stream:
            result = subprocess.run([str(dotnet), "build", str(inputs[0]), "--output", str(directory),
                        "-p:RestoreLockedMode=true", "--nologo", "--verbosity", "quiet",
                        "-p:BaseIntermediateOutputPath=" + str(directory / "obj") + "/"],
                        stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode or not assembly.is_file():
            raise RecoveryError("Pinned managed native-codec build failed; see " + str(log))
        write_json(receipt, {"schema": 1, "sourceFingerprint": fingerprint,
                   "packageLockSha256": sha256(inputs[-1]), "nativeToolchainRequired": False,
                   "licenses": {"AssetRipper.TextureDecoder-2.6.3": "MIT", "Fmod5Sharp-3.1.0": "MIT"},
                   "files": [{"path": path.relative_to(directory).as_posix(), "sha256": sha256(path)}
                             for path in sorted(directory.rglob("*")) if path.is_file() and "obj" not in path.relative_to(directory).parts and path.name != "build.log"]})
    return [str(dotnet), str(assembly)]


def execute(command, arguments):
    result = subprocess.run(command + list(map(str, arguments)), capture_output=True, text=True)
    if result.returncode:
        raise RecoveryError("Pinned native codec failed: " + (result.stdout + result.stderr)[-2000:])
    return result.stdout
