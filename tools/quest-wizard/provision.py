"""Pinned user-local tools; resumable verified downloads and owned ZIP extraction."""
from __future__ import annotations
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import shutil
import sys
import discovery
import urllib.request
import zipfile

from state import WizardError, atomic_json, digest, ordinary, read_json, value_hash

HERE = Path(__file__).resolve().parent
LOCK = json.loads((HERE / "tools.lock.json").read_text(encoding="utf-8"))


def download(spec, destination, check_cancel=lambda: None, progress=lambda done, total: None, opener=urllib.request.urlopen):
    """Retain incomplete bytes; Range responses must match the requested offset."""
    destination = ordinary(destination); destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if digest(destination, spec["algorithm"]) != spec["hash"]:
            raise WizardError("tool_checksum", "A cached tool differs from its pinned checksum.", "Ein zwischengespeichertes Werkzeug stimmt nicht mit der Prüfsumme überein.")
        return destination
    partial = ordinary(destination.with_name(destination.name + ".partial"))
    offset = partial.stat().st_size if partial.exists() else 0
    # A process can die after the final byte and before atomic publication.
    # Complete verified bytes need no second HTTP request (often answered 416).
    if offset and (spec.get("size") is None or offset == spec["size"]) and digest(partial, spec["algorithm"]) == spec["hash"]:
        check_cancel(); os.replace(partial, destination); return destination
    headers = {"User-Agent": "GloomhavenVR-Quest-Wizard", "Accept-Encoding": "identity"}
    if offset: headers["Range"] = "bytes=" + str(offset) + "-"
    request = urllib.request.Request(spec["url"], headers=headers)
    check_cancel()
    with opener(request, timeout=60) as response:
        code = response.status
        if code == 206:
            match = re.fullmatch(r"bytes (\d+)-(\d+)/(\d+)", response.headers.get("Content-Range", ""))
            if not match or int(match[1]) != offset or int(match[2]) < offset or int(match[2]) >= int(match[3]):
                raise WizardError("download_range", "Download continuation did not match the retained bytes.")
            total = int(match[3])
        elif code == 200:
            offset = 0
            total = int(response.headers.get("Content-Length", "0")) or spec.get("size")
        else: raise WizardError("download_response", "The tool server returned an unsupported download response.")
        with partial.open("ab" if offset else "wb") as stream:
            done = offset
            while True:
                check_cancel(); block = response.read(1048576)
                if not block: break
                stream.write(block); done += len(block)
                if total and done > total: raise WizardError("download_range", "Tool response exceeded its declared size.")
                progress(done, total)
            stream.flush(); os.fsync(stream.fileno())
    check_cancel()
    if (spec.get("size") is not None and done != spec["size"]) or digest(partial, spec["algorithm"]) != spec["hash"]:
        partial.unlink(missing_ok=True)
        raise WizardError("tool_checksum", "A downloaded tool failed its pinned checksum.", "Ein heruntergeladenes Werkzeug hat eine falsche Prüfsumme.")
    os.replace(partial, destination)
    return destination


def extract_owned(archive, destination, spec, check_cancel=lambda: None):
    """Resume the same pinned extraction file by file; no unowned directory deletion."""
    destination = ordinary(destination); key = value_hash(spec)
    marker = destination / "wizard-tool.json"
    if destination.exists():
        if not marker.is_file() or read_json(marker).get("key") != key:
            raise WizardError("unowned_tool", "Tool extraction belongs to another input; it was retained.")
    else:
        destination.mkdir(parents=True)
        atomic_json(marker, {"schema": 1, "key": key, "complete": False})
    with zipfile.ZipFile(archive) as package:
        members = package.infolist()
        if len(members) > 100000 or sum(row.file_size for row in members) > 8 * 1024**3:
            raise WizardError("tool_archive", "Tool archive exceeds its supported bounds.")
        seen = set()
        for row in members:
            name = row.filename.rstrip("/")
            parts = PurePosixPath(name).parts
            if (not parts or PurePosixPath(name).is_absolute() or PureWindowsPath(name).is_absolute()
                    or any(p in (".", "..") or ":" in p for p in parts) or "\\" in name
                    or name.casefold() in seen or (row.external_attr >> 16) & 0o170000 == 0o120000):
                raise WizardError("tool_archive", "Tool archive has unsafe or duplicate members.")
            seen.add(name.casefold())
            target = ordinary(destination.joinpath(*parts))
            if row.is_dir(): target.mkdir(parents=True, exist_ok=True); continue
            check_cancel(); target.parent.mkdir(parents=True, exist_ok=True)
            # CRC is the exact pinned archive's member identity. Hash publication
            # follows extraction; size alone never authorizes reuse.
            if target.is_file() and target.stat().st_size == row.file_size:
                import zlib
                crc = 0
                with target.open("rb") as stream:
                    for block in iter(lambda: stream.read(1048576), b""): crc = zlib.crc32(block, crc)
                if crc & 0xFFFFFFFF == row.CRC: continue
            temp = target.with_name(target.name + ".extracting")
            ordinary(temp)
            with package.open(row) as source, temp.open("wb") as output:
                while True:
                    check_cancel(); block = source.read(1048576)
                    if not block: break
                    output.write(block)
                output.flush(); os.fsync(output.fileno())
            os.replace(temp, target)
    executable = destination / spec["executable"]
    if not executable.is_file(): raise WizardError("missing_tool", "Pinned tool archive lacks its executable.")
    atomic_json(marker, {"schema": 1, "key": key, "complete": True, "executableSha256": digest(executable)})
    return executable


def tools(store, session, supervisor):
    if os.name != "nt":
        raise WizardError("windows_required", "The conversion wizard currently provisions Windows x64 tools.", "Der Konvertierungs-Wizard unterstützt derzeit Windows x64.")
    found = {}; outputs = []
    for name in ("git", "dotnet8", "dotnet10"):
        spec = LOCK[name]
        archive = download(spec, store.root / "tools/downloads" / (name + "-" + spec["version"] + ".zip"),
                           lambda: store.check_cancel(session))
        executable = extract_owned(archive, store.root / "tools" / (name + "-" + spec["version"]), spec,
                                   lambda: store.check_cancel(session))
        log = store.session_dir(session) / "logs" / (name + "-version.log")
        supervisor.run([executable, "--version"], log)
        expected = "git version " + spec["version"] if name == "git" else spec["version"]
        if log.read_text(encoding="utf-8").strip() != expected:
            raise WizardError("tool_version", "A provisioned tool reported an unexpected version.")
        found[name] = str(executable)
        tool_root = executable.parent.parent if name == "git" else executable.parent
        # A missing SDK support DLL must invalidate this receipt even when the
        # launcher EXE itself still matches. Extraction then repairs exact ZIP
        # members without redownloading a valid pinned archive.
        outputs += [*sorted(path for path in tool_root.rglob("*") if path.is_file()), log]
    path = store.session_dir(session) / "tools.json"
    atomic_json(path, {"schema": 1, **found, "pins": {name: value_hash(LOCK[name]) for name in found}})
    return [path, *outputs], found


def environment(details):
    env = dict(os.environ)
    env["PATH"] = os.pathsep.join([str(Path(details["git"]).parent), str(Path(details["dotnet8"]).parent), env.get("PATH", "")])
    env.update(DOTNET_ROOT=str(Path(details["dotnet8"]).parent), DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", PYTHONUTF8="1")
    return env


def derive_runtime_dependencies(checkout, game_root, details, supervisor, logs, check_cancel):
    """Run the selected source's existing package projects without a Bash dependency."""
    module = __import__("discovery").builder(checkout)
    managed = module.game_data(Path(game_root)) / "Managed"
    recipe = (checkout / "scripts/build-runtimedeps.sh").read_text(encoding="utf-8")
    packages = re.findall(r'"(com\.unity\.xr\.[a-z-]+) ([0-9]+\.[0-9]+\.[0-9]+)"', recipe)
    projects = sorted((checkout / "tools/RuntimeDepsBuild").glob("*/*.csproj"))
    if len(packages) != 3 or len(projects) != 3:
        raise WizardError("runtime_recipe", "The selected mod has changed its XR dependency recipe; its declared package projects need review.")
    versions = dict(packages); actual = {}; pending = []
    for project in projects:
        text = project.read_text(encoding="utf-8")
        version = re.search(r"<PackageSourceVersion>([^<]+)</PackageSourceVersion>", text)
        package = "com.unity.xr." + {"Management": "management", "CoreUtils": "core-utils", "OpenXR": "openxr"}.get(project.stem.rsplit(".", 1)[-1], "")
        if not version or versions.get(package) != version[1]: raise WizardError("runtime_recipe", "XR source package/project versions disagree.")
        directory = checkout / "tools/RuntimeDepsBuild/sources" / package
        target = checkout / "libs/RuntimeDeps" / (project.stem + ".dll")
        if target.is_file(): actual[project.stem] = {"sha256": digest(target), "source": "supplied-local-assembly"}; continue
        directory.mkdir(parents=True, exist_ok=True)
        env = environment(details); git = details["git"]
        if not (directory / ".git").is_dir():
            supervisor.run([git, "-C", directory, "init", "-q"], logs / (package + "-init.log"), env=env)
        if not (directory / "package.json").is_file():
            supervisor.run([git, "-C", directory, "fetch", "--depth", "1", "https://github.com/needle-mirror/" + package + ".git", "refs/tags/" + version[1]], logs / (package + "-fetch.log"), env=env)
            supervisor.run([git, "-C", directory, "checkout", "--detach", "FETCH_HEAD"], logs / (package + "-checkout.log"), env=env)
        if json.loads((directory / "package.json").read_text(encoding="utf-8"))["version"] != version[1]: raise WizardError("runtime_recipe", "Cached XR package has another version.")
        pending.append((project, target, package, version[1]))
    # Project references may build another XR package, so all sources must be
    # present before the first .NET build (the Bash recipe has the same order).
    for project, target, package, version in pending:
        supervisor.run([details["dotnet8"], "build", project, "-c", "Release", "--nologo", "-v", "quiet", "-p:GameManaged=" + str(managed)], logs / (package + "-build.log"), env=environment(details))
        check_cancel(); built = project.parent / "bin/Release/net472" / target.name
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(built, target)
        actual[project.stem] = {"sha256": digest(target), "source": "declared-provisional-project", "package": package, "version": version}
    manifest = checkout / "libs/RuntimeDeps/wizard-dependencies.json"
    atomic_json(manifest, {"schema": 1, "kind": "compile-time dependency inputs; Quest uses Android PlayerSdk packages", "assemblies": actual})
    return manifest


def release_source(store, session, choices, details, supervisor, source):
    """Copy only verified release inputs; keep immutable per-release workspaces."""
    release = discovery.local_support_module(source, "release")
    records, commit, dirty = release.verified_source_inventory(source)
    if choices.get("sourceCommit") and choices["sourceCommit"] != commit:
        raise WizardError("source_changed", "Selected commit differs from the shipped release.")
    identity = value_hash(records)
    checkout = ordinary(store.root / "source" / (session + "-" + identity[:16]))
    owner = checkout.with_name(checkout.name + ".owner.json")
    expected = {"schema": 1, "session": session, "releaseHash": identity, "commit": commit}
    if owner.exists():
        if read_json(owner) != expected: raise WizardError("unowned_source", "Release workspace ownership differs.")
    elif checkout.exists(): raise WizardError("unowned_source", "Release workspace has no ownership record.")
    else: atomic_json(owner, expected)
    checkout.mkdir(parents=True, exist_ok=True)
    for row in records:
        store.check_cancel(session)
        target = ordinary(checkout / row["path"]); target.parent.mkdir(parents=True, exist_ok=True)
        if not target.is_file() or target.stat().st_size != row["size"] or digest(target) != row["sha256"]:
            shutil.copyfile(source / row["path"], target)
        if digest(target) != row["sha256"]: raise WizardError("source_changed", "A release input changed while copied.")
    derived = derive_runtime_dependencies(checkout, choices["gameRoot"], details, supervisor,
                                          store.session_dir(session) / "logs", lambda: store.check_cancel(session))
    # The builder validates shipped sources and adds local derived references
    # separately. No game or license files are copied into the release itself.
    current_records, current, _ = discovery.builder(checkout).source_inventory(checkout)
    receipt = store.session_dir(session) / "source.json"
    atomic_json(receipt, {"schema": 1, "sourceRoot": str(checkout), "commit": current, "dirty": False,
                          "sourceHash": value_hash(current_records), "releaseHash": identity})
    return [receipt, owner, derived, *(checkout / row["path"] for row in current_records)], {
        "sourceRoot": str(checkout), "commit": current, "sourceHash": value_hash(current_records), "releaseHash": identity}


def source_checkout(store, session, choices, details, supervisor, repo):
    source = ordinary(Path(choices.get("sourceRoot") or repo))
    if (source / "quest-builder-release.json").is_file() and not (source / ".git").exists():
        return release_source(store, session, choices, details, supervisor, source)
    checkout = ordinary(store.root / "source" / session)
    git = details["git"]; env = environment(details)
    log_root = store.session_dir(session) / "logs"
    owner = store.session_dir(session) / "source-attempt.json"
    source = Path(choices.get("sourceRoot") or repo)
    local = (source / ".git").exists()
    origin = str(source) if local else LOCK["source"]["url"]
    if checkout.exists() and not owner.exists(): raise WizardError("unowned_source", "Source checkout has no wizard ownership record.")
    if not owner.exists(): atomic_json(owner, {"schema": 1, "session": session, "origin": origin})
    if read_json(owner).get("origin") != origin: raise WizardError("source_changed", "Source origin changed; use a new session.")
    # git removes failed clone directories itself when possible. A hard kill can
    # leave its owned incomplete clone; retaining the receipt lets us safely retry.
    if checkout.exists() and not (checkout / ".git").is_dir(): shutil.rmtree(checkout)
    if checkout.exists() and not (store.session_dir(session) / "source-resolution.json").exists():
        try:
            supervisor.run([git, "-C", checkout, "rev-parse", "--verify", "HEAD"], log_root / "source-clone-complete.log", env=env)
        except WizardError as error:
            if error.code != "child_failed": raise
            # Only the matching attempt-owned clone with no resolved commit is
            # repaired. A cancelled command or completed checkout is retained.
            shutil.rmtree(checkout)
    if not checkout.exists():
        argv = [git, "-c", "core.longpaths=true", "clone", "--no-checkout", "--no-hardlinks"]
        if not local: argv += ["--depth", "1", "--single-branch", "--branch", choices.get("sourceRef") or LOCK["source"]["ref"]]
        supervisor.run([*argv, origin, checkout], log_root / "source-clone.log", env=env)
    commit = choices.get("sourceCommit")
    resolution = store.session_dir(session) / "source-resolution.json"
    if resolution.is_file():
        resolved = read_json(resolution)["commit"]
        if commit and commit != resolved: raise WizardError("source_changed", "This session is pinned to another source commit; use a new session.")
        commit = resolved
    if not commit:
        supervisor.run([git, "-C", checkout, "rev-parse", "HEAD"], log_root / "source-commit.log", env=env)
        commit = (log_root / "source-commit.log").read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"[0-9a-f]{40}", commit): raise WizardError("source_commit", "Source did not resolve to one immutable commit.")
    if not local and choices.get("sourceCommit"):
        # An explicitly selected older commit may lie outside the shallow ref.
        # Fetch that immutable object alone instead of the project's asset history.
        try:
            supervisor.run([git, "-C", checkout, "cat-file", "-e", commit + "^{commit}"],
                           log_root / "source-selected-present.log", env=env)
        except WizardError as error:
            if error.code != "child_failed": raise
            supervisor.run([git, "-C", checkout, "fetch", "--depth", "1", origin, commit],
                           log_root / "source-selected-commit.log", env=env)
    atomic_json(resolution, {"schema": 1, "commit": commit})
    supervisor.run([git, "-C", checkout, "checkout", "--detach", commit], log_root / "source-checkout.log", env=env)
    def source_inventory(directory, filename):
        # The existing inventory invokes `git` on PATH. Run in a private child
        # with portable Git instead of mutating the HTTP server's environment.
        record = store.session_dir(session) / filename
        code = ("import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);"
                "import discovery;from state import atomic_json;"
                "files,commit,dirty=discovery.builder(Path(sys.argv[2])).source_inventory(Path(sys.argv[3]));"
                "atomic_json(Path(sys.argv[4]),dict(schema=1,files=files,commit=commit,dirty=dirty))")
        supervisor.run([sys.executable, "-I", "-B", "-c", code, HERE, checkout, directory, record],
                       log_root / (filename + ".log"), env=env)
        value = read_json(record, limit=64 * 1048576)
        return value["files"], value["commit"], value["dirty"]
    if local:
        records, original_commit, _ = source_inventory(source, "source-local-inventory.json")
        if original_commit != commit: raise WizardError("source_changed", "The local checkout differs from the session's source commit; select a new session.")
        # Copy the exact source inventory, including ordinary uncommitted mod
        # work and explicitly declared ignored XR DLLs. Secret/output rules stay
        # in the existing builder; source is read-only.
        for row in records:
            store.check_cancel(session)
            original = source / row["path"]; target = ordinary(checkout / row["path"])
            target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(original, target)
            if digest(target) != row["sha256"]: raise WizardError("source_changed", "A source file changed while copied; retry when editing stops.")
    derived = derive_runtime_dependencies(checkout, choices["gameRoot"], details, supervisor, log_root, lambda: store.check_cancel(session))
    records, current, dirty = source_inventory(checkout, "source-owned-inventory.json")
    receipt = store.session_dir(session) / "source.json"
    atomic_json(receipt, {"schema": 1, "sourceRoot": str(checkout), "commit": current, "dirty": dirty,
                          "sourceHash": value_hash(records)})
    return [receipt, resolution, owner, derived, *(checkout / row["path"] for row in records)], {"sourceRoot": str(checkout), "commit": current, "sourceHash": value_hash(records)}
