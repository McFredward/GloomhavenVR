#!/usr/bin/env python3
"""Pinned, local-only recovery of an owned Gloomhaven Unity player.

The core export is deliberately not described as a playable Quest conversion:
compiled desktop shaders and deferred Addressables remain explicit gates. The
receipt records exactly what was recovered, and Unity import/player evidence is
a later stage. No reconstructed content is written into the source checkout.
"""
import argparse
import collections
import hashlib
import html
import json
import os
from pathlib import Path
import platform
import re
import shutil
import socket
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.parse
import urllib.request
import zipfile

from md4 import script_file_id

HERE = Path(__file__).resolve().parent
RECIPE_VERSION = 1
RECEIPT = "quest-recovery-report.json"
YAML_EXTENSIONS = {".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController", ".anim", ".mask"}
SCRIPT_POINTER = re.compile(r"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})")
GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)


class RecoveryError(RuntimeError):
    pass


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def resolve_game_data(root):
    root = Path(root).resolve()
    if (root / "Managed/GH.Runtime.dll").is_file():
        result = root
    else:
        matches = [p for p in root.glob("*_Data") if (p / "Managed/GH.Runtime.dll").is_file()]
        if len(matches) != 1:
            raise RecoveryError("Expected exactly one owned *_Data/Managed/GH.Runtime.dll; select --game-data explicitly.")
        result = matches[0]
    for required in ("globalgamemanagers", "level0", "resources.assets", "ScriptingAssemblies.json"):
        if not (result / required).is_file():
            raise RecoveryError(f"Incomplete game input: missing {required} in {result}.")
    return result


def validate_output(source, output):
    source, output = Path(source).resolve(), Path(output).resolve()
    if output == source or source in output.parents or output in source.parents:
        raise RecoveryError("Recovered output must be separate from the read-only game tree.")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise RecoveryError("Output already contains files. Use --resume for a matching receipt or choose a fresh output.")


def source_inventory(game_data):
    files = []
    for path in sorted(game_data.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(game_data).as_posix()
        files.append({"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path)})
    encoded = json.dumps(files, sort_keys=True, separators=(",", ":")).encode()
    return files, hashlib.sha256(encoded).hexdigest()


def recipe_hash():
    files = []
    for path in sorted(HERE.rglob("*")):
        if path.is_file() and path.suffix in (".py", ".cs", ".csproj", ".json") and "__pycache__" not in path.parts:
            files.append((path.relative_to(HERE).as_posix(), sha256(path)))
    return hashlib.sha256(json.dumps(files, separators=(",", ":")).encode()).hexdigest()


def bundle_inventory(game_data, files):
    bundles = []
    for item in files:
        if not item["path"].startswith("StreamingAssets/"):
            continue
        with (game_data / item["path"]).open("rb") as stream:
            magic = stream.read(8)
        if magic.startswith((b"UnityFS\0", b"UnityWeb", b"UnityRaw")):
            bundles.append(item)
    catalog = game_data / "StreamingAssets/aa/catalog.json"
    result = {"bundles": bundles, "bundleCount": len(bundles),
              "bundleBytes": sum(x["bytes"] for x in bundles)}
    if catalog.is_file():
        data = json.loads(catalog.read_text(encoding="utf-8-sig"))
        result["catalog"] = {"sha256": sha256(catalog),
                             "internalIds": data.get("m_InternalIds", []),
                             "providerIds": data.get("m_ProviderIds", [])}
    return result


def safe_extract(archive, destination):
    destination = Path(destination).resolve()

    def check(name):
        candidate = (destination / name).resolve()
        if candidate != destination and destination not in candidate.parents:
            raise RecoveryError(f"Tool archive entry escapes destination: {name}")

    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as content:
            for member in content.infolist():
                check(member.filename)
                if (member.external_attr >> 16) & 0o170000 == 0o120000:
                    raise RecoveryError("Unexpected symbolic link in tool archive.")
            content.extractall(destination)
    else:
        with tarfile.open(archive) as content:
            for member in content.getmembers():
                check(member.name)
                if member.issym() or member.islnk() or not (member.isdir() or member.isfile()):
                    raise RecoveryError("Unexpected link/device in tool archive.")
            content.extractall(destination, filter="data")


def ensure_tool(tool_root, lock):
    system = {"Linux": "linux", "Windows": "windows"}.get(platform.system())
    if system is None or platform.machine().lower() not in ("amd64", "x86_64"):
        raise RecoveryError("Pinned recovery currently supports Linux x64 and Windows x64; unsupported host is not silently substituted.")
    artifact = lock["artifacts"][system + "-x64"]
    cache = Path(tool_root).resolve() / ("assetripper-" + lock["version"] + "-" + system + "-x64")
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / artifact["filename"]
    if not archive.exists():
        url = lock["repository"] + "/releases/download/" + lock["version"] + "/" + artifact["filename"]
        temporary = archive.with_name(archive.name + ".download")
        urllib.request.urlretrieve(url, temporary)
        if sha256(temporary) != artifact["sha256"]:
            temporary.unlink()
            raise RecoveryError("Official tool download did not match the pinned SHA-256.")
        temporary.replace(archive)
    if sha256(archive) != artifact["sha256"]:
        raise RecoveryError("Cached tool archive differs from lock; remove only that archive and download again.")
    # Verify extracted executable/content on every reuse; archive verification
    # alone must not bless an independently modified executable.
    executable = cache / "distribution" / artifact["executable"]
    marker = cache / "distribution-hashes.json"
    if executable.exists() and marker.exists():
        expected = json.loads(marker.read_text())
        for relative, digest in expected.items():
            path = executable.parent / relative
            if not path.is_file() or sha256(path) != digest:
                raise RecoveryError(f"Cached recovery executable/dependency was modified: {relative}")
    else:
        if executable.parent.exists() and any(executable.parent.iterdir()):
            raise RecoveryError("Incomplete tool extraction; choose a fresh --tool-root.")
        executable.parent.mkdir(parents=True, exist_ok=True)
        safe_extract(archive, executable.parent)
        write_json(marker, {p.relative_to(executable.parent).as_posix(): sha256(p)
                            for p in executable.parent.rglob("*") if p.is_file()})
    if not executable.is_file():
        raise RecoveryError("Pinned archive does not contain the expected AssetRipper executable.")
    if system == "linux":
        executable.chmod(executable.stat().st_mode | 0o111)
    return executable, artifact["sha256"]


def stage_input(game_data, stage, selected_bundles):
    stage.mkdir(parents=True)
    selected = []
    # A directory load scans bundled StreamingAssets even when their copy/export
    # setting says Ignore. Stage core inputs explicitly to bound resident memory.
    for path in sorted(game_data.iterdir()):
        if path.name in ("StreamingAssets", "Plugins"):
            continue
        target = stage / path.name
        if path.is_dir():
            shutil.copytree(path, target)
        else:
            shutil.copy2(path, target)
        selected.append(path.name)
    for relative in selected_bundles:
        path = (game_data / relative).resolve()
        if game_data not in path.parents or not path.is_file():
            raise RecoveryError(f"Bundle must name an existing file inside game data: {relative}")
        with path.open("rb") as stream:
            if not stream.read(8).startswith((b"UnityFS\0", b"UnityWeb", b"UnityRaw")):
                raise RecoveryError(f"Selected bundle has no Unity bundle header: {relative}")
        target = stage / "SelectedBundles" / Path(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    return selected


def request(base, endpoint, values=None, timeout=7200):
    data = None if values is None else urllib.parse.urlencode(values).encode()
    with urllib.request.urlopen(base + endpoint, data, timeout=timeout) as result:
        return result.read()


def export_shader_recipes(base, output):
    results = request(base, "/Search/View?q=Shader").decode()
    paths = re.findall(r'<tr data-class="Shader">.*?<a href="(/Assets/View\?Path=[^\"]+)"', results)
    directory = output / "QuestRecovery/ShaderRecipes"
    directory.mkdir(parents=True, exist_ok=True)
    entries = []
    for link in paths:
        raw_path = urllib.parse.parse_qs(urllib.parse.urlsplit(html.unescape(link)).query)["Path"][0]
        value = json.loads(request(base, "/Assets/Json?" + urllib.parse.urlencode({"Path": raw_path})))
        parsed = value.get("m_ParsedForm")
        if not parsed:
            continue
        name = parsed.get("m_Name", value.get("m_Name", "Unnamed"))
        key = re.sub(r"[^A-Za-z0-9._-]+", "_", name) + "-" + hashlib.sha256(raw_path.encode()).hexdigest()[:10]
        path = directory / (key + ".json")
        write_json(path, {"sourcePath": json.loads(raw_path), "parsedForm": parsed,
                          "compiledPlatforms": value.get("m_Platforms", [])})
        entries.append({"name": name, "recipe": path.relative_to(output).as_posix(),
                        "sha256": sha256(path), "compiledPlatforms": value.get("m_Platforms", [])})
    return entries


def export_script_identities(base, output):
    """Retain original serialized type names to distinguish source defects."""
    results = request(base, "/Search/View?q=MonoScript").decode()
    paths = re.findall(r'<tr data-class="MonoScript">.*?<a href="(/Assets/View\?Path=[^\"]+)"', results)
    identities = set()
    for link in paths:
        raw_path = urllib.parse.parse_qs(urllib.parse.urlsplit(html.unescape(link)).query)["Path"][0]
        value = json.loads(request(base, "/Assets/Json?" + urllib.parse.urlencode({"Path": raw_path})))
        identities.add((value.get("m_AssemblyName", ""), value.get("m_Namespace", ""), value.get("m_ClassName", "")))
    entries = [{"assembly": a, "namespace": n, "name": c,
                "fileId": script_file_id(n, c)} for a, n, c in sorted(identities)]
    directory = output / "QuestRecovery"
    directory.mkdir(parents=True, exist_ok=True)
    write_json(directory / "original-script-identities.json", entries)
    return entries


def run_export(executable, stage, export, log_path, shader_root, settings):
    with socket.socket() as available:
        available.bind(("127.0.0.1", 0))
        port = available.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    with log_path.with_suffix(".console.log").open("wb") as console:
        process = subprocess.Popen([str(executable), "--headless", "--port", str(port),
                                    "--log-path", str(log_path)], stdout=console, stderr=subprocess.STDOUT,
                                   cwd=stage.parent)
        try:
            deadline = time.monotonic() + 60
            while True:
                if process.poll() is not None:
                    raise RecoveryError(f"AssetRipper exited during startup ({process.returncode}); see {log_path}.")
                try:
                    request(base, "/", timeout=2)
                    break
                except (OSError, urllib.error.URLError):
                    if time.monotonic() >= deadline:
                        raise RecoveryError(f"AssetRipper did not start; see {log_path}.")
                    time.sleep(0.2)
            request(base, "/Settings/Update", settings)
            print("[Quest recovery] Loading owned core assets; detailed progress is in", log_path, flush=True)
            request(base, "/LoadFolder", {"Path": str(stage)})
            request(base, "/Export/UnityProject", {"Path": str(export)})
            project = export / "ExportedProject"
            if not (project / "ProjectSettings/EditorBuildSettings.asset").is_file():
                raise RecoveryError(f"Export did not produce original scene settings; see {log_path}.")
            recipes = export_shader_recipes(base, shader_root)
            identities = export_script_identities(base, shader_root)
            return project, recipes, identities
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


def repair_managed_plugins(project, game_data):
    preserved, enabled = [], []
    for plugin in sorted((project / "Assets/Plugins").glob("*.dll")):
        source = game_data / "Managed" / plugin.name
        if not source.is_file():
            raise RecoveryError(f"Exported plugin has no original managed input: {plugin.name}")
        # Exporters serialize assembly metadata through their own writer. Restore
        # original bytes and tokens; Unity script fileIDs derive from type names.
        shutil.copy2(source, plugin)
        preserved.append({"path": plugin.relative_to(project).as_posix(), "sha256": sha256(plugin)})
        metadata = plugin.with_name(plugin.name + ".meta")
        text = metadata.read_text(encoding="utf-8")
        text, count = re.subn(r"(Editor: Editor\s+second:\s+enabled:) 0", r"\g<1> 1", text)
        if count:
            metadata.write_text(text, encoding="utf-8")
            enabled.append(plugin.name)
    duplicates = [p.relative_to(project).as_posix() for p in (project / "Assets").rglob("*.cs")
                  if "AssetRipperPatches" not in p.parts]
    if duplicates:
        raise RecoveryError("DLL mode unexpectedly exported duplicate C# types: " + ", ".join(duplicates[:10]))
    return {"preservedOriginalAssemblies": preserved, "enabledForEditor": enabled,
            "duplicateGameSourceFiles": duplicates}


def managed_inventory(game_data, destination, tool_root):
    dotnet = shutil.which("dotnet")
    if not dotnet:
        candidates = [Path(os.environ.get("DOTNET_ROOT", "/nonexistent")) / "dotnet",
                      Path.home() / ".dotnet/dotnet"]
        dotnet = next((str(p) for p in candidates if p.is_file()), None)
    if not dotnet:
        raise RecoveryError("A .NET 8 SDK is required for metadata-only script identity validation.")
    build = Path(tool_root).resolve() / "managed-inventory-v1"
    command = [dotnet, "build", str(HERE / "ManagedInventory/ManagedInventory.csproj"),
               "--output", str(build), "--nologo", "--verbosity", "quiet",
               "-p:BaseIntermediateOutputPath=" + str(build / "obj") + os.sep]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode:
        raise RecoveryError("Metadata reader build failed: " + (result.stdout + result.stderr)[-4000:])
    subprocess.run([dotnet, str(build / "ManagedInventory.dll"), str(game_data / "Managed"), str(destination)], check=True)
    return json.loads(destination.read_text())


def audit_script_bindings(project, types, original_identities=()):
    scripts = {}
    assembly_guids = {}
    for plugin in (project / "Assets/Plugins").glob("*.dll"):
        match = GUID_PATTERN.search(plugin.with_name(plugin.name + ".meta").read_text())
        if not match or plugin.name not in types:
            raise RecoveryError(f"Cannot resolve exported assembly identity: {plugin.name}")
        assembly_guids[plugin.name] = match[1]
        for type_info in types[plugin.name]["types"]:
            if type_info["nested"]:
                continue
            fid = script_file_id(type_info["namespace"], type_info["name"])
            scripts[(match[1], str(fid))] = types[plugin.name]["name"] + ":" + (
                type_info["namespace"] + "." if type_info["namespace"] else "") + type_info["name"]
    original_orphans = {}
    for identity in original_identities:
        guid = assembly_guids.get(identity["assembly"])
        key = (guid, str(identity["fileId"]))
        if guid is not None and key not in scripts:
            original_orphans[key] = identity
    total = 0
    unresolved = []
    scenes = []
    for path in sorted((project / "Assets").rglob("*")):
        if path.suffix not in YAML_EXTENSIONS:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        local_types = collections.Counter()
        for fid, guid in SCRIPT_POINTER.findall(text):
            total += 1
            if guid.startswith("0000000000000000"):
                continue
            name = scripts.get((guid, fid))
            if name is None:
                item = {"path": path.relative_to(project).as_posix(), "guid": guid, "fileId": int(fid)}
                if (guid, fid) in original_orphans:
                    item["originalSerializedIdentity"] = original_orphans[(guid, fid)]
                    item["presentInOriginalManagedMetadata"] = False
                unresolved.append(item)
            else:
                local_types[name] += 1
        if path.suffix == ".unity":
            scenes.append({"path": path.relative_to(project).as_posix(), "scripts": dict(sorted(local_types.items()))})
    unexpected = [x for x in unresolved if "originalSerializedIdentity" not in x]
    return {"scriptReferenceCount": total, "unresolvedCount": len(unresolved),
            "unexpectedUnresolvedCount": len(unexpected),
            "unresolved": unresolved, "sceneScriptTypes": scenes,
            "status": ("metadata-resolved" if not unresolved else
                       "blocked-unresolved-script-identities" if unexpected else "original-orphans-retained"),
            "unityImportVerified": False}


def audit_asset_references(project):
    """Check GUID closure without pretending to replace Unity's importer."""
    guids = {}
    for metadata in (project / "Assets").rglob("*.meta"):
        match = GUID_PATTERN.search(metadata.read_text(encoding="utf-8", errors="replace"))
        if match:
            guids.setdefault(match[1], []).append(metadata.relative_to(project).as_posix())
    missing = collections.defaultdict(set)
    references = 0
    for asset in (project / "Assets").rglob("*"):
        if asset.suffix not in YAML_EXTENSIONS:
            continue
        for guid in re.findall(r"guid:\s*([0-9a-f]{32})", asset.read_text(encoding="utf-8", errors="replace")):
            references += 1
            if not guid.startswith("0000000000000000") and guid not in guids:
                missing[guid].add(asset.relative_to(project).as_posix())
    duplicates = {guid: paths for guid, paths in guids.items() if len(paths) > 1}
    return {"referenceCount": references, "missingGuidCount": len(missing),
            "missing": {g: sorted(paths) for g, paths in sorted(missing.items())},
            "duplicateGuidCount": len(duplicates), "duplicates": duplicates,
            "unityImportVerified": False}


def audit_export_log(log_path):
    text = Path(log_path).read_text(encoding="utf-8", errors="replace")
    errors = [line for line in text.splitlines() if "[Error]" in line]
    layout = [line for line in errors if "Unable to read MonoBehaviour Structure" in line]
    return {"errorCount": len(errors), "errors": dict(sorted(collections.Counter(errors).items())),
            "monoBehaviourLayoutFailureCount": len(layout),
            "status": "blocked-serialized-behaviour-recovery" if layout else "requires-unity-import"}


def project_inventory(project):
    output = []
    for path in sorted(project.rglob("*")):
        if path.is_file() and path.name != RECEIPT and "Library" not in path.relative_to(project).parts:
            output.append({"path": path.relative_to(project).as_posix(), "bytes": path.stat().st_size,
                           "sha256": sha256(path)})
    return output


def audit_project(project, game_data, types, bundles, selected_bundles, shaders, original_identities=()):
    version = (project / "ProjectSettings/ProjectVersion.txt").read_text()
    if "m_EditorVersion: 2021.3.5f1" not in version:
        raise RecoveryError("Recovered project does not target the original Unity 2021.3.5f1.")
    build_settings = (project / "ProjectSettings/EditorBuildSettings.asset").read_text()
    build_scenes = re.findall(r"^\s+path:\s*(.+)$", build_settings, re.MULTILINE)
    if not build_scenes or not build_scenes[0].endswith("Bootstrap.unity"):
        raise RecoveryError("Original Bootstrap build-scene order was not retained.")
    missing = [s for s in build_scenes if not (project / s).is_file()]
    if missing:
        raise RecoveryError("Build scenes are missing: " + ", ".join(missing))
    shader_files = list((project / "Assets").rglob("*.shader"))
    placeholders = [p.relative_to(project).as_posix() for p in shader_files
                    if "DummyShaderTextExporter" in p.read_text()]
    bindings = audit_script_bindings(project, types, original_identities)
    references = audit_asset_references(project)
    selected = set(selected_bundles)
    deferred = [x for x in bundles["bundles"] if x["path"] not in selected]
    blockers = ["unity-import-unverified", "android-player-unbuilt", "campaign-unverified"]
    if placeholders:
        blockers.append("compiled-desktop-shaders-require-reconstruction")
    if deferred:
        blockers.append("addressable-bundles-require-recovery-and-android-rebuild")
    if bindings["unexpectedUnresolvedCount"]:
        blockers.append("unexpected-unresolved-script-identities")
    if bindings["unresolvedCount"]:
        blockers.append("original-missing-script-requires-runtime-disposition")
    if references["missingGuidCount"] or references["duplicateGuidCount"]:
        blockers.append("asset-reference-closure-incomplete")
    return {"editorVersion": "2021.3.5f1", "originalBuildScenes": build_scenes,
            "blockers": blockers,
            "managedScriptBindings": bindings,
            "assetReferences": references,
            "shaders": {"count": len(shader_files), "placeholderCount": len(placeholders),
                        "placeholders": placeholders, "originalParsedRecipes": shaders,
                        "status": "blocked-shader-reconstruction" if placeholders else "requires-player-validation"},
            "addressables": {**bundles, "selectedBundles": sorted(selected),
                             "deferredBundleCount": len(deferred), "deferredBundleBytes": sum(x["bytes"] for x in deferred),
                             "status": "blocked-unconverted-bundles" if deferred else "requires-android-rebuild"},
            "readiness": {"originalCoreProjectRecovered": True,
                          "unityImportVerified": False, "androidPlayerBuilt": False,
                          "playableCampaignVerified": False, "faithfulGraphicsVerified": False,
                          "fullGameReady": False}}


def verify_resume(project, source_hash, lock_hash, selected_bundles, expected_recipe_hash=None):
    receipt = project / RECEIPT
    if not receipt.is_file():
        raise RecoveryError("Resume requested but no successful recovery receipt exists.")
    value = json.loads(receipt.read_text())
    if (value.get("recipeVersion") != RECIPE_VERSION or value.get("sourceHash") != source_hash
            or value.get("toolLockHash") != lock_hash or value.get("selectedBundles") != sorted(selected_bundles)):
        raise RecoveryError("Recovery inputs/recipe differ; select a new output project instead of reusing stale content.")
    if expected_recipe_hash is not None and value.get("recipeHash") != expected_recipe_hash:
        raise RecoveryError("Recovery code changed; select a fresh output to avoid reusing stale conversion logic.")
    inventory = value.get("outputInventory")
    if not inventory:
        raise RecoveryError("Recovery receipt has no verified output inventory.")
    for item in inventory:
        path = project / item["path"]
        if project.resolve() not in path.resolve().parents:
            raise RecoveryError("Recovery inventory contains an unsafe output path.")
        if not path.is_file() or sha256(path) != item["sha256"]:
            raise RecoveryError("Recovered output was modified or lost: " + item["path"])
    if project_inventory(project) != inventory:
        raise RecoveryError("Recovered project gained unrecorded content; select a fresh immutable recovery output.")
    return value


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", "--game-root", dest="game_root", required=True)
    parser.add_argument("--output-project", required=True)
    parser.add_argument("--tool-root", required=True)
    parser.add_argument("--manifest", help="Optional immutable builder input manifest; its hash is recorded.")
    parser.add_argument("--bundle", action="append", default=[], help="Explicit original relative bundle input; repeat to recover a bounded closure.")
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args(argv)
    try:
        source = resolve_game_data(args.game_root)
        output = Path(args.output_project).resolve()
        lock_path = HERE / "tool-lock.json"
        lock = json.loads(lock_path.read_text())
        lock_hash = sha256(lock_path)
        current_recipe_hash = recipe_hash()
        print("[Quest recovery] Hashing immutable owned inputs.", flush=True)
        files, source_hash = source_inventory(source)
        if args.resume:
            value = verify_resume(output, source_hash, lock_hash, args.bundle, current_recipe_hash)
            print("[Quest recovery] Verified cached original core project:", output)
            print(json.dumps(value["audit"]["readiness"], sort_keys=True))
            return 0
        validate_output(source, output)
        executable, tool_hash = ensure_tool(args.tool_root, lock)
        output.parent.mkdir(parents=True, exist_ok=True)
        # Preserve failed evidence without ever overwriting the requested project.
        scratch = Path(tempfile.mkdtemp(prefix="quest-recovery-", dir=output.parent))
        stage = scratch / "Input/GH_Data"
        stage.parent.mkdir()
        selected_core = stage_input(source, stage, args.bundle)
        export = scratch / "Export"
        log_path = scratch / "assetripper.log"
        project, shaders, identities = run_export(executable, stage, export, log_path, scratch, lock["settings"])
        repair = repair_managed_plugins(project, source)
        types_path = scratch / "managed-types.json"
        types = managed_inventory(source, types_path, args.tool_root)
        shutil.copytree(scratch / "QuestRecovery", project / "QuestRecovery")
        shutil.copy2(types_path, project / "QuestRecovery/managed-types.json")
        # Shader recipe paths are rooted at the final project, not scratch.
        audit = audit_project(project, source, types, bundle_inventory(source, files), args.bundle, shaders, identities)
        audit["exportLog"] = audit_export_log(log_path)
        if audit["exportLog"]["errorCount"]:
            audit["blockers"].append("asset-export-errors-require-resolution")
        if audit["exportLog"]["monoBehaviourLayoutFailureCount"]:
            audit["blockers"].append("serialized-behaviour-layouts-not-recovered")
        if audit["managedScriptBindings"]["unexpectedUnresolvedCount"]:
            write_json(scratch / "failed-script-audit.json", audit)
            raise RecoveryError(f"Unresolved MonoScript identities; see {scratch / 'failed-script-audit.json'}.")
        # Source hash after conversion catches changes during a long running export.
        _, after_hash = source_inventory(source)
        if after_hash != source_hash:
            raise RecoveryError("Owned input changed during recovery. Discard this export and snapshot inputs again.")
        if recipe_hash() != current_recipe_hash:
            raise RecoveryError("Recovery code changed during export. Use a coherent snapshot and retry.")
        value = {"schema": 1, "recipeVersion": RECIPE_VERSION, "sourceHash": source_hash,
                 "recipeHash": current_recipe_hash,
                 "toolLockHash": lock_hash, "toolVersion": lock["version"], "toolArchiveHash": tool_hash,
                 "sourceInventory": files, "selectedCore": selected_core, "selectedBundles": sorted(args.bundle),
                 "managedRepair": repair, "audit": audit, "log": str(log_path),
                 "inputManifestHash": sha256(args.manifest) if args.manifest else None,
                 "outputInventory": project_inventory(project)}
        write_json(project / RECEIPT, value)
        if output.exists():
            output.rmdir()
        project.replace(output)
        print("[Quest recovery] Original core project recovered:", output)
        print(json.dumps(audit["readiness"], sort_keys=True))
        print("[Quest recovery] Remaining gates:", audit["shaders"]["placeholderCount"],
              "shader placeholders,", audit["addressables"]["deferredBundleCount"], "deferred bundles.")
        return 0
    except (RecoveryError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print("[Quest recovery] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
