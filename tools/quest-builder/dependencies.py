"""Provision pinned conversion tools in the builder's private output cache."""
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import site
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

from storage import BuildError, digest, write_json


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
    root = Path(cache) / "build-python"
    marker = root / ".ghvr-build-python.json"
    executable = root / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    root.parent.mkdir(parents=True, exist_ok=True)
    if root.exists() and (root.is_symlink() or not marker.is_file()):
        raise BuildError("Builder Python cache is not owned by this builder; choose a fresh output directory.")
    if not root.exists():
        root.mkdir()
        write_json(marker, {"schema": 1, "owner": "GloomhavenVR builder", "requirementsKey": None})
    state = json.loads(marker.read_text())
    if state.get("owner") != "GloomhavenVR builder" or state.get("schema") != 1:
        raise BuildError("Builder Python ownership receipt differs.")
    log_path = root.parent / "build-python.log"
    with log_path.open("a", encoding="utf-8") as log:
        if not executable.is_file():
            subprocess.run([sys.executable, "-m", "venv", str(root)], check=True, stdout=log, stderr=subprocess.STDOUT)
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
        if root.is_symlink() or not marker.is_file() or not executable.is_file():
            raise BuildError("Recovery SDK cache is incomplete or unowned; choose a new output directory.")
        state = json.loads(marker.read_text())
        if state.get("version") != version or state.get("archiveSha512") != lock["dotnet"][rid]["hash"]:
            raise BuildError("Recovery SDK cache identity differs.")
        return executable
    root.parent.mkdir(parents=True, exist_ok=True)
    spec = lock["dotnet"][rid]
    archive = root.parent / Path(spec["url"]).name
    if not archive.exists():
        temporary = archive.with_suffix(archive.suffix + ".download")
        try:
            print("dependencies: downloading official pinned .NET " + version, flush=True)
            with urllib.request.urlopen(spec["url"], timeout=60) as response, temporary.open("wb") as output:
                shutil.copyfileobj(response, output, 1048576)
            temporary.replace(archive)
        finally: temporary.unlink(missing_ok=True)
    with archive.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha512").hexdigest()
    if actual != spec["hash"]: raise BuildError("Official recovery SDK archive fails its pinned SHA512.")
    stage = root.with_name(root.name + ".extracting")
    if stage.exists(): raise BuildError("Interrupted recovery SDK extraction exists; inspect its private cache.")
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
    except BaseException:
        shutil.rmtree(stage)
        raise
    return executable
