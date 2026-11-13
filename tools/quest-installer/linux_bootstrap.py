"""Create/reuse the script-local installer venv without changing system Python."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import uuid

OWNER = "GloomhavenVR.QuestInstaller"
PROBE = "import importlib.util,json,os,sys;print(json.dumps(dict(prefix=os.path.abspath(sys.prefix),basePrefix=os.path.abspath(sys.base_prefix),baseExecutable=os.path.realpath(sys._base_executable),version='.'.join(map(str,sys.version_info[:3])),hasPip=importlib.util.find_spec('pip') is not None)))"


def python_info(executable):
    result = subprocess.run([str(executable), "-I", "-B", "-c", PROBE], capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def atomic_json(path, value):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as handle:
            json.dump(value, handle, sort_keys=True)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def owned_state(directory):
    if not directory.exists() and not directory.is_symlink():
        return None
    if directory.is_symlink() or not directory.is_dir():
        raise RuntimeError(f"Refusing an unmanaged file/link at {directory}.")
    receipt = directory / ".quest-owner.json"
    if receipt.is_symlink() or not receipt.is_file() or receipt.stat().st_nlink != 1:
        raise RuntimeError(f"Refusing to change unowned folder {directory}.")
    try:
        value = json.loads(receipt.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise RuntimeError(f"Refusing invalid ownership metadata at {directory}.") from error
    if not isinstance(value, dict) or value.get("schema") != 1 or value.get("owner") != OWNER or value.get("kind") != "venv":
        raise RuntimeError(f"Refusing to change unowned folder {directory}.")
    return value


def delete_owned(directory):
    owned_state(directory)
    # Python's fd-based rmtree does not follow the ordinary bin/python and lib64
    # symlinks created by Linux venv. Refuse foreign devices/sockets/FIFOs.
    for root, folders, files in os.walk(directory, followlinks=False):
        for name in folders + files:
            mode = (Path(root) / name).lstat().st_mode
            if not (stat.S_ISREG(mode) or stat.S_ISDIR(mode) or stat.S_ISLNK(mode)):
                raise RuntimeError(f"Refusing unusual content inside {directory}.")
    if not shutil.rmtree.avoids_symlink_attacks:
        raise RuntimeError("This Python lacks the Linux safe-directory removal implementation.")
    shutil.rmtree(directory)


def isolated(info, environment, base):
    return (info["prefix"] == str(environment) and info["prefix"] != info["basePrefix"]
            and info["basePrefix"] == base["basePrefix"] and info["baseExecutable"] == base["baseExecutable"]
            and info["version"] == base["version"])


def prepare(scripts, requirements):
    scripts = Path(scripts).absolute()
    requirements = Path(requirements).absolute()
    if scripts.is_symlink() or not scripts.is_dir() or requirements.is_symlink() or not requirements.is_file():
        raise RuntimeError("The ordinary script folder or pinned requirements manifest is missing.")
    base = python_info(sys.executable)
    if tuple(map(int, base["version"].split("."))) < (3, 11):
        raise RuntimeError("Python 3.11 or later is required.")
    manifest = requirements.read_bytes()
    requirement_hash = hashlib.sha256(manifest).hexdigest()
    dependencies = [line for line in manifest.decode("utf-8-sig").splitlines() if line.strip() and not line.strip().startswith("#")]
    environment = scripts / ".quest-venv"
    python = environment / "bin/python"
    state = owned_state(environment)
    valid = False
    if state and state.get("complete") and state.get("directory") == str(scripts) and state.get("version") == base["version"] and state.get("baseExecutable") == base["baseExecutable"]:
        try:
            info = python_info(python)
            valid = isolated(info, environment, base)
        except (OSError, ValueError, subprocess.CalledProcessError):
            pass
    if not valid:
        if state:
            delete_owned(environment)
        environment.mkdir()
        atomic_json(environment / ".quest-owner.json", {"schema": 1, "owner": OWNER, "kind": "venv", "complete": False})
        print(f"Creating isolated installer Python in {environment}...", file=sys.stderr)
        # Debian distributions often omit ensurepip. The current Wizard has no
        # third-party dependencies, so creating the venv needs no system pip.
        subprocess.run([sys.executable, "-I", "-B", "-m", "venv", "--without-pip", str(environment)], check=True)
        info = python_info(python)
        if not isolated(info, environment, base):
            raise RuntimeError("Installer Python/venv isolation checks failed; retry to recreate the owned environment.")
    if dependencies and (not valid or state.get("requirementsSha256") != requirement_hash or not info["hasPip"]):
        if not info["hasPip"]:
            subprocess.run([str(python), "-I", "-B", "-m", "ensurepip", "--default-pip"], check=True, stdout=sys.stderr)
        subprocess.run([str(python), "-I", "-B", "-m", "pip", "--isolated", "install", "--disable-pip-version-check", "--no-input", "--no-cache-dir", "--require-hashes", "-r", str(requirements)], check=True, stdout=sys.stderr)
    if hashlib.sha256(requirements.read_bytes()).hexdigest() != requirement_hash:
        raise RuntimeError("Requirements changed during setup; retry before running the Wizard.")
    atomic_json(environment / ".quest-owner.json", {"schema": 1, "owner": OWNER, "kind": "venv", "complete": True,
                "directory": str(scripts), "baseExecutable": base["baseExecutable"], "version": base["version"], "requirementsSha256": requirement_hash})
    return python


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scripts", type=Path, required=True)
    parser.add_argument("--requirements", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        print(prepare(args.scripts, args.requirements))
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"Quest Python: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
