"""Provision pinned conversion tools in the builder's private output cache."""
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import site
import subprocess
import sys
import sysconfig
import tarfile
import urllib.request
import zipfile

from storage import BuildError, digest, write_json, value_hash, _ordinary_owned


def download_sdk(spec, archive, *, algorithm="sha512"):
    """Retain exact partial bytes; publish only the pinned complete archive."""
    if archive.exists(): return
    partial = _ordinary_owned(archive.with_suffix(archive.suffix + ".download"))
    if partial.exists():
        with partial.open("rb") as stream:
            if hashlib.file_digest(stream, algorithm).hexdigest() == spec["hash"]:
                os.replace(partial, archive); return
    offset = partial.stat().st_size if partial.exists() else 0
    request = urllib.request.Request(spec["url"], headers={"Range": "bytes=" + str(offset) + "-"} if offset else {})
    with urllib.request.urlopen(request, timeout=60) as response:
        status = getattr(response, "status", 200)
        if status == 206:
            match = re.fullmatch(r"bytes ([0-9]+)-([0-9]+)/([0-9]+)", response.headers.get("Content-Range", ""))
            if not match or int(match[1]) != offset or int(match[2]) < offset or int(match[2]) >= int(match[3]):
                raise BuildError("Recovery SDK server returned an inconsistent resume range.")
            expected = int(match[2]) + 1
        elif status == 200:
            offset = 0; expected = int(response.headers.get("Content-Length", "0")) or None
        else: raise BuildError("Recovery SDK server did not return a usable archive.")
        with partial.open("ab" if offset else "wb") as stream:
            shutil.copyfileobj(response, stream, 1048576); stream.flush(); os.fsync(stream.fileno())
    if expected is not None and partial.stat().st_size != expected:
        raise BuildError("Recovery SDK download ended early; its verified offset can resume.")
    with partial.open("rb") as stream: actual = hashlib.file_digest(stream, algorithm).hexdigest()
    if actual != spec["hash"]:
        partial.unlink(); raise BuildError("Official tool archive fails its pinned " + algorithm.upper() + ".")
    os.replace(partial, archive)


def python_abi():
    return {"implementation": sys.implementation.name, "cacheTag": sys.implementation.cache_tag,
            "version": list(sys.version_info[:3]), "platform": sysconfig.get_platform(),
            "soabi": sysconfig.get_config_var("SOABI"),
            "baseExecutable": str(Path(getattr(sys, "_base_executable", sys.executable)).resolve())}


def matching_python(executable, root):
    """Probe only the child's standard library before adding any native wheels."""
    code = ('import json,sys;from pathlib import Path;print(json.dumps(dict('
            'implementation=sys.implementation.name,cacheTag=sys.implementation.cache_tag,'
            'version=list(sys.version_info[:3]),prefix=str(Path(sys.prefix).resolve()),'
            'baseExecutable=str(Path(sys._base_executable).resolve()))))')
    try:
        state = json.loads(subprocess.check_output([str(executable), "-I", "-c", code], text=True))
        expected = python_abi()
        return all(state.get(key) == expected[key] for key in ("implementation", "cacheTag", "version", "baseExecutable")) and state.get("prefix") == str(root.resolve())
    except (OSError, ValueError, subprocess.CalledProcessError): return False


def requirements_key(paths):
    seen = {}
    def visit(path):
        path = Path(path).resolve()
        if path in seen: return
        seen[path] = digest(path)
        for line in path.read_text().splitlines():
            if line.startswith("-r "): visit(path.parent / line[3:].strip())
    for path in paths: visit(path)
    return hashlib.sha256("\n".join(sorted(p.name + ":" + h for p,h in seen.items())).encode()).hexdigest()


def python_environment(cache, source, *, procedural=True):
    if sys.version_info[:2] < (3, 11):
        raise BuildError("The full Campaign builder requires Python 3.11 or later; use the Windows local Python launcher.")
    requirements = [source / "tools/quest-builder/requirements.txt"]
    bootstrap = source / "tools/quest-builder/requirements-bootstrap.txt"
    pure_source = source / "tools/quest-builder/requirements-source.txt"
    if procedural: requirements.append(source / "tools/quest-procedural-runtime/requirements.txt")
    key = requirements_key([*requirements, bootstrap, pure_source])
    abi = python_abi()
    # ABI-separated caches never import a cp311 wheel into a cp314 launcher.
    # Existing older venvs are retained without being trusted or overwritten.
    root = _ordinary_owned(Path(cache) / ("build-python-" + value_hash(abi)[:16]))
    marker = root / ".ghvr-build-python.json"
    executable = root / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    root.parent.mkdir(parents=True, exist_ok=True)
    if root.exists() and (root.is_symlink() or not marker.is_file()):
        raise BuildError("Builder Python cache is not owned by this builder; choose a fresh output directory.")
    if not root.exists():
        root.mkdir()
        write_json(marker, {"schema": 1, "owner": "GloomhavenVR builder", "requirementsKey": None, "pythonAbi": abi})
    state = json.loads(marker.read_text())
    if state.get("owner") != "GloomhavenVR builder" or state.get("schema") != 1 or state.get("pythonAbi") != abi:
        raise BuildError("Builder Python ownership receipt differs.")
    log_path = root.parent / "build-python.log"
    with log_path.open("a", encoding="utf-8") as log:
        if executable.is_file() and not matching_python(executable, root):
            # Only a marker-owned cache with this exact ABI may be repaired.
            shutil.rmtree(root); root.mkdir()
            state["requirementsKey"] = None
            write_json(marker, state)
        if not executable.is_file():
            subprocess.run([sys.executable, "-m", "venv", str(root)], check=True, stdout=log, stderr=subprocess.STDOUT)
        if not matching_python(executable, root): raise BuildError("Builder Python did not produce the exact launcher ABI.")
        if state.get("requirementsKey") != key:
            env = dict(os.environ)
            env.update(PIP_CONFIG_FILE=os.devnull, PIP_EXTRA_INDEX_URL="", PIP_DISABLE_PIP_VERSION_CHECK="1")
            common = [str(executable), "-m", "pip", "install", "--index-url", "https://pypi.org/simple", "--require-hashes", "--only-binary=:all:"]
            print("dependencies: installing pinned build packages in " + str(root), flush=True)
            subprocess.run(common + ["-r", str(bootstrap)],
                           check=True, stdout=log, stderr=subprocess.STDOUT, env=env)
            # tpk_ar's verified source distribution contains only Python and
            # uses the separately pinned, already-installed setuptools/wheel.
            # The network requirements deliberately enforce binary-only tools.
            # Install the one witnessed source package separately before that
            # file can reset pip's format policy.
            subprocess.run(common + ["--no-binary=:all:", "--no-build-isolation", "--no-deps", "-r", str(pure_source)],
                           check=True, stdout=log, stderr=subprocess.STDOUT, env=env)
            command = common + ["--no-build-isolation"]
            for requirement in requirements: command += ["-r", str(requirement)]
            try:
                subprocess.run(command, check=True, stdout=log, stderr=subprocess.STDOUT, env=env)
            except subprocess.CalledProcessError as error:
                raise BuildError("Pinned builder packages could not be installed; inspect " + str(log_path)) from error
            state["requirementsKey"] = key
            write_json(marker, state)
    if os.name == "nt": paths = [root / "Lib/site-packages"]
    else: paths = list((root / "lib").glob("python*/site-packages"))
    if len(paths) != 1: raise BuildError("Builder Python has no unique isolated site-packages directory.")
    # The venv uses this exact interpreter ABI. Import its verified packages
    # locally without modifying the player's system Python or environment.
    if str(paths[0]) in sys.path:
        sys.path.remove(str(paths[0]))
    sys.path.insert(0, str(paths[0]))
    site.addsitedir(str(paths[0]))
    return executable


def dotnet10(cache, source, explicit=None):
    lock = json.loads((source / "tools/quest-builder/dependencies-lock.json").read_text())
    version = lock["dotnetSdkVersion"]
    if explicit:
        executable = Path(explicit).resolve()
        actual = subprocess.check_output([str(executable), "--version"], text=True).strip()
        if actual != version: raise BuildError("Full recovery requires pinned .NET SDK " + version + ".")
        return executable
    system = {"win32": "win", "linux": "linux", "darwin": "osx"}.get(sys.platform)
    architecture = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
    rid = str(system) + "-" + architecture
    if rid not in lock["dotnet"]: raise BuildError("No pinned recovery SDK for this build host: " + rid)
    root = Path(cache) / ("dotnet-" + version + "-" + rid)
    marker = root / ".ghvr-recovery-sdk.json"
    executable = root / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if root.exists():
        _ordinary_owned(root)
        if not marker.is_file():
            raise BuildError("Recovery SDK cache is incomplete or unowned; choose a new output directory.")
        state = json.loads(marker.read_text())
        if state.get("version") != version or state.get("archiveSha512") != lock["dotnet"][rid]["hash"]:
            raise BuildError("Recovery SDK cache identity differs.")
        if executable.is_file(): return executable
        shutil.rmtree(root)  # repair only the exact matching owned SDK generation
    root.parent.mkdir(parents=True, exist_ok=True)
    spec = lock["dotnet"][rid]
    archive = root.parent / Path(spec["url"]).name
    if not archive.exists():
        print("dependencies: downloading official pinned .NET " + version, flush=True)
        download_sdk(spec, archive)
    with archive.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha512").hexdigest()
    if actual != spec["hash"]: raise BuildError("Official recovery SDK archive fails its pinned SHA512.")
    stage = root.with_name(root.name + ".extracting")
    owner = stage.with_name(stage.name + ".json")
    expected_owner = {"schema": 1, "owner": "Quest recovery SDK extraction", "version": version, "archiveSha512": actual}
    _ordinary_owned(stage); _ordinary_owned(owner)
    if owner.exists():
        if json.loads(owner.read_text()) != expected_owner: raise BuildError("Recovery SDK extraction owner differs.")
        if stage.exists(): shutil.rmtree(stage)
    elif stage.exists(): raise BuildError("Recovery SDK extraction has no matching ownership record.")
    else: write_json(owner, expected_owner)
    stage.mkdir()
    try:
        if archive.suffix == ".zip":
            with zipfile.ZipFile(archive) as package:
                for row in package.infolist():
                    path = Path(row.filename)
                    if path.is_absolute() or ".." in path.parts or "\\" in row.filename:
                        raise BuildError("Recovery SDK archive has a nonlocal member.")
                package.extractall(stage)
        else:
            with tarfile.open(archive) as package: package.extractall(stage, filter="data")
        write_json(stage / marker.name, {"schema": 1, "version": version, "archiveSha512": actual})
        stage.rename(root)
        owner.unlink()
    except BaseException:
        shutil.rmtree(stage)
        raise
    return executable
