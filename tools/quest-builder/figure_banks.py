"""Build/cache owned Android actor LOD banks independently of the Campaign bank.

Only the licensed PC figure bundles are read. Source releases contain the recipe,
never the input streams or derived vertices. Runtime consumers retain the Frame
index and AssetBundle format. APK updates stream these files directly.
"""
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import threading

from storage import BuildError, _ordinary_owned, canonical, digest, record_file, value_hash, write_json, build_progress

METADATA = "assets/Quest/figure-meshes.json"
PREFIX = "assets/QuestFigureMeshes/"
INDEX = "ghvr-figure-meshes-index.json"
SOURCE_ROOT = "StreamingAssets/aa/StandaloneWindows64/"
HASH = re.compile(r"[0-9a-f]{64}")
MEMBER = re.compile(r"ghvr-figure-meshes-quest-(?:75|45|20|5)-[0-9]{2,3}\.bundle")
UNITY_MODULES = {"com.unity.modules.assetbundle": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0"}


def _stamp(path):
    stat = _ordinary_owned(Path(path)).stat()
    return [stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns]


def _key_method(source):
    text = (source / "src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs").read_text(encoding="utf-8")
    start = text.index("    internal static string Key(")
    end = text.index("    internal static void Prepare(", start)
    return text[start:end]


def plan(source, game_info):
    source = Path(source)
    producers = ["tools/figure-mesh/Generator.csproj", "tools/figure-mesh/QuestFigureMeshGenerator.cs", "tools/figure-mesh/NativeFigureMeshStream.cs",
                 "tools/figure-mesh/ScenarioFigureMeshTopology.cs", "tools/figure-mesh/export-native.py"]
    producers += sorted(path.relative_to(source).as_posix() for path in
                        (source / "tools/figure-mesh/UnityMeshSimplifier").rglob("*.cs"))
    recipe = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "tiers": [75, 45, 20, 5],
              "sources": [record_file(source / name, name) for name in producers], "runtimeKey": _key_method(source)}
    rows = [row for row in game_info["files"] if row["path"].startswith(SOURCE_ROOT)
            and Path(row["path"]).name.startswith(("hero_", "npc_")) and row["path"].endswith("_assets_all.bundle")]
    if not rows or len(rows) > 1024: raise BuildError("Owned PC game lacks its original figure bundles.")
    for row in rows:
        if (row["path"] != SOURCE_ROOT + Path(row["path"]).name or any(c in row["path"] for c in "\\:\0\r\n") or not HASH.fullmatch(row.get("sha256", ""))
                or type(row.get("size")) is not int or row["size"] <= 0):
            raise BuildError("Owned PC figure bundle inventory is invalid.")
    rows = sorted(rows, key=lambda row: row["path"])
    # B690's metadata-only Android proof missed that the tiny project disabled
    # AssetBundle's native module. Unity still wrote Mesh objects, without the
    # AssetBundle container, and the actual player rejected every part. Modules
    # are compiler inputs: retain the old geometry identity separately so this
    # packaging repair does not repeat the expensive original simplification.
    geometry = {"recipe": recipe, "sources": rows}
    geometry_key = value_hash(geometry)
    recipe = {**recipe, "unityModules": UNITY_MODULES}
    return {"recipe": recipe, "sources": rows, "geometryKey": geometry_key,
            "producerKey": value_hash({"recipe": recipe, "sources": rows})}


def _retain_previous_geometry(root, previous, expected):
    """Carry only exact old native exports/derivatives into the repaired producer.

    No old Android bank is reused. Source completion receipts still verify every
    derivative in Unity before import; an interrupted copy cannot certify work.
    """
    if root == previous or not (previous / "completion.json").is_file(): return
    try:
        old = json.loads((previous / "completion.json").read_text())["plan"]
        recipe = {key: value for key, value in expected["recipe"].items() if key != "unityModules"}
        if old != {"recipe": recipe, "sources": expected["sources"], "producerKey": expected["geometryKey"]}: return
        # Only our former producer's flat, known directories are admitted.
        for folder in ("derivatives", "extracted"):
            source = _ordinary_owned(previous / folder)
            if not source.is_dir(): continue
            for path in sorted(source.rglob("*")):
                _ordinary_owned(path)
                if not path.is_file(): continue
                if folder == "derivatives" and path.suffix not in (".asset", ".json"): continue
                if folder == "extracted" and path.suffix not in (".mesh", ".json"): continue
                relative = path.relative_to(previous)
                target = _ordinary_owned(root / relative)
                if target.exists(): continue
                target.parent.mkdir(parents=True, exist_ok=True)
                if folder == "derivatives" and path.suffix == ".json":
                    row = json.loads(path.read_text())
                    if row.get("producerKey") != expected["geometryKey"]: continue
                    write_json(target, {**row, "producerKey": expected["producerKey"]})
                else:
                    temporary = target.with_name(target.name + ".retaining-geometry")
                    try:
                        shutil.copy2(path, temporary); temporary.replace(target)
                    finally: temporary.unlink(missing_ok=True)
    except (OSError, ValueError, KeyError, TypeError):
        # Incomplete former geometry is optional. The normal per-source producer
        # repairs exactly the missing or hash-mismatched derivative group.
        return


def _validate_bank(path, identities):
    """Qualify the actual native AssetBundle container, not just readable Meshes."""
    import UnityPy
    environment = UnityPy.load(str(path))
    if len(environment.files) != 1: raise BuildError("Owned figure part has no unique UnityFS archive: " + path.name)
    archive = next(iter(environment.files.values()))
    if (getattr(archive, "signature", None) != "UnityFS" or archive.version != 7
            or archive.version_engine != "2021.3.5f1"):
        raise BuildError("Owned figure part uses an incompatible Unity archive: " + path.name)
    roots, meshes = [], {}
    for obj in environment.objects:
        if obj.assets_file.unity_version != "2021.3.5f1" or int(obj.assets_file.target_platform) != 13:
            raise BuildError("Owned figure part uses incompatible Android serialization: " + path.name)
        if obj.type.name == "AssetBundle": roots.append(obj.read_typetree())
        elif obj.type.name == "Mesh": meshes[obj.path_id] = obj.read_typetree()["m_Name"]
        else: raise BuildError("Owned figure part contains an unexpected native type: " + obj.type.name)
    if len(roots) != 1 or roots[0].get("m_Name") != path.name:
        raise BuildError("Owned figure part lacks its native AssetBundle container: " + path.name)
    container = roots[0].get("m_Container", [])
    references = {row[1]["asset"]["m_PathID"] for row in container if row[1]["asset"]["m_FileID"] == 0}
    if (references != set(meshes) or set(meshes.values()) != set(identities)
            or len(container) != len(meshes) or len(meshes) != len(identities)):
        raise BuildError("Owned figure part container differs from its mesh index: " + path.name)


def _receipt(folder, expected):
    try:
        receipt = json.loads((folder / "completion.json").read_text())
        if receipt.get("plan") != expected: return None
        refreshed = False
        for row in receipt["files"]:
            path = folder / row["path"]
            if _stamp(path) != row["stamp"]:
                if record_file(path, row["path"]) != {k: row[k] for k in ("path", "size", "sha256")}: return None
                row["stamp"] = _stamp(path)
                refreshed = True
        if refreshed: write_json(folder / "completion.json", receipt)
        return receipt
    except (OSError, ValueError, KeyError, TypeError): return None


def _export(source, game, output, selected, operation):
    spec = importlib.util.spec_from_file_location("quest_owned_figure_export", source / "tools/figure-mesh/export-native.py")
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    all_rows, excluded, seen = [], [], set()
    for index, row in enumerate(selected):
        folder = output / "extracted" / row["sha256"]
        extraction = {"source": row, "producer": record_file(source / "tools/figure-mesh/export-native.py", "export-native.py")}
        retained = _receipt(folder, extraction)
        if retained is None:
            path = _ordinary_owned(game / row["path"])
            if path.stat().st_size != row["size"]: raise BuildError("Owned figure input changed: " + row["path"])
            report = module.export_bundle(path, folder, row["sha256"])
            write_json(folder / "sources.json", report)
            files = [record_file(folder / name, name) for name in ["sources.json", *(m["file"] for m in report["meshes"])]]
            for item in files: item["stamp"] = _stamp(folder / item["path"])
            write_json(folder / "completion.json", {"plan": extraction, "files": files})
        report = json.loads((folder / "sources.json").read_text())
        for mesh in report["meshes"]:
            if mesh["geometry_sha256"] in seen: continue
            seen.add(mesh["geometry_sha256"])
            # A relative path into the durable per-bundle export avoids another
            # copy of every lossless native stream in each build/update project.
            all_rows.append({**mesh, "file": os.path.relpath(folder / mesh["file"], output / "native")})
        excluded += report["excluded"]
        build_progress.event("figure-banks", int(200 * (index + 1) / len(selected)), 1000, "work-shares",
            "Extract original figure bundles " + str(index + 1) + "/" + str(len(selected)) + ": " + Path(row["path"]).name, operation=operation)
    write_json(output / "native/sources.json", {"format": "GHFM1", "meshes": all_rows, "excluded": excluded})
    if not all_rows: raise BuildError("No eligible original figure meshes were found.")


def _watch(log, stop, operation):
    offset = 0
    while not stop.wait(.25):
        try:
            with log.open(encoding="utf-8", errors="replace") as stream:
                stream.seek(offset); lines = stream.readlines(); offset = stream.tell()
            for line in lines:
                match = re.search(r"GHVRQ_FIGURES (source|bank) (\d+)/(\d+) (.+)", line)
                if match:
                    phase, done, total, detail = match.groups()
                    fraction = (200 + int(done) * 600 // int(total)) if phase == "source" else (800 + int(done) * 199 // int(total))
                    build_progress.event("figure-banks", fraction, 1000, "work-shares",
                        ("Prepare figure meshes " if phase == "source" else "Export Android figure banks ")
                        + done + "/" + total + ": " + detail.strip(), operation=operation)
        except OSError: pass


def produce(builder, args, source, game, output, game_info, editor):
    source, game, output, editor = [Path(path).absolute() for path in (source, game, output, editor)]
    expected = plan(source, game_info)
    operation = "update-art" if getattr(args, "command", None) == "update-mod" else "figure-banks"
    root = output / "cache/quest-figure-meshes" / expected["producerKey"]
    retained = _receipt(root, expected)
    if retained is not None:
        build_progress.event("figure-banks", 1000, 1000, "work-shares", "Retained owned Android figure LOD banks", operation=operation)
        if operation == "figure-banks": build_progress.operation(operation, complete=True, detail="Retained owned Android figure LOD banks")
        return root, retained["manifest"]
    build_progress.operation(operation, detail="Preparing owned Android figure LOD banks")
    build_progress.event("figure-banks", 0, 1000, "work-shares", "Preparing owned Android figure LOD banks", operation=operation, status="start")
    _retain_previous_geometry(root, output / "cache/quest-figure-meshes" / expected["geometryKey"], expected)
    _export(source, game, root, expected["sources"], operation)
    project = root / "project"
    builder.project_access.wait_for_editor(project, build_progress)
    (project / "Assets/Editor").mkdir(parents=True, exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    write_json(project / "Packages/manifest.json", {"dependencies": UNITY_MODULES})
    boundary = root / "Boundaries.cs"
    boundary.write_text('namespace GloomhavenVR.Core { internal static class VRLog { internal static bool WantsDebug => false; internal static void Debug(string a,string b) {} internal static void Note(string a,string b) {} } internal static class RuntimeDepsLoader { internal static string PluginDir => ""; } }\n')
    builder.command([str(builder.tool_path(args.dotnet, "dotnet")), "build", str(source / "tools/figure-mesh/Generator.csproj"),
        "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:GeneratorSource=" + str(source / "tools/figure-mesh"),
        "-p:RuntimeSource=" + str(source / "src/GloomhavenVR/Core/Perf"), "-p:GeneratedBoundaries=" + str(boundary),
        "-p:UnityManaged=" + str(editor.parent / "Data/Managed"), "-p:BaseIntermediateOutputPath=" + str(root / "obj") + os.sep,
        "-o", str(root / "generator")], output / "logs/figure-generator-compile.log", cwd=source)
    shutil.copyfile(root / "generator/Generator.dll", project / "Assets/Editor/Generator.dll")
    log = output / "logs/figure-banks-unity.log"
    log.unlink(missing_ok=True)
    stop = threading.Event(); watcher = threading.Thread(target=_watch, args=(log, stop, operation), daemon=True); watcher.start()
    try:
        builder.command(builder.unity_launcher(editor) + ["-quit", "-projectPath", str(project), "-buildTarget", "Android",
            "-executeMethod", "QuestFigureMeshGenerator.Build", "-nativeMeshInput", str(root / "native"),
            "-nativeMeshOutput", str(root / "banks"), "-figureProducerKey", expected["producerKey"], "-logFile", str(log)],
            output / "logs/figure-banks-launch.log", cwd=source)
    finally: stop.set(); watcher.join()
    index = json.loads((root / "banks" / INDEX).read_text())
    names = sorted({row["bank"] for row in index["entries"]})
    if not names or len(names) > 256 or any(not MEMBER.fullmatch(name) for name in names):
        raise BuildError("Owned Android figure banks have an invalid index.")
    for name in names:
        _validate_bank(root / "banks" / name, [row["mesh"] for row in index["entries"] if row["bank"] == name])
    files = [record_file(root / "banks" / name, name) for name in [*names, INDEX]]
    if sum(row["size"] for row in files) > 1024 * 1024 * 1024: raise BuildError("Owned figure bank set exceeds its delivery bound.")
    manifest = {"schema": 1, "producerKey": expected["producerKey"], "target": "Android", "files": files}
    write_json(root / "figure-meshes.json", manifest)
    retained_files = [record_file(root / "figure-meshes.json", "figure-meshes.json")]
    retained_files += [{**row, "path": "banks/" + row["path"]} for row in files]
    for row in retained_files: row["stamp"] = _stamp(root / row["path"])
    write_json(root / "completion.json", {"plan": expected, "files": retained_files, "manifest": manifest})
    build_progress.event("figure-banks", 1000, 1000, "work-shares", "Owned Android figure banks completed", operation=operation)
    if operation == "figure-banks": build_progress.operation(operation, complete=True, detail="Owned Android figure LOD banks completed")
    return root, manifest


def replacements(root, manifest):
    return {METADATA: canonical(manifest), **{PREFIX + row["path"]: Path(root) / "banks" / row["path"] for row in manifest["files"]}}


def stage(project, root, manifest):
    folder = Path(project) / "Assets/StreamingAssets"
    target = folder / "QuestFigureMeshes"; target.mkdir(parents=True, exist_ok=True)
    for row in manifest["files"]:
        source = Path(root) / "banks" / row["path"]
        destination = target / row["path"]
        # Hard links to mutable Unity inputs violate the immutable producer;
        # keep one streaming copy in the active player project only.
        if destination.is_file() and destination.stat().st_size == row["size"] and digest(destination) == row["sha256"]: continue
        shutil.copyfile(source, destination)
    write_json(folder / "Quest/figure-meshes.json", manifest)
