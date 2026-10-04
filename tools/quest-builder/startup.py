"""Validate a bounded original-game startup export without granting game readiness."""

from __future__ import annotations

import json
from pathlib import Path, PurePosixPath
import re
import shutil
import uuid

from storage import BuildError, digest, inventory, record_file, value_hash, write_json


REPORT = "quest-startup-report.json"
SCENE_NAMES = ("Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu")
MOVIES_REPORT = "quest-startup-movies.json"
VIDEO_EXTENSIONS = {".mp4", ".mov", ".webm", ".ogv"}
SERIALIZED_EXTENSIONS = {".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController", ".anim", ".playable", ".meta"}


def stage_startup_script_orders(project: Path, game: Path, source: Path, cache: Path, dotnet: Path) -> dict:
    """Restore original importer orders before Unity import or DLL weaving.

    Run after recovered SDK DLL exclusion. The editor then verifies/restores
    actual imported MonoScripts after QuestOriginalScriptBindings remapping.
    Kept lazy so importing the hardware installer does not invoke build tools.
    """
    from script_order import stage_script_orders
    return stage_script_orders(project, game, source, cache, dotnet)


def inspect_project(root: Path, game_key: str, game_files: list[dict], *, snapshot_receipt: bool = False) -> dict:
    """Accept only the declared original closure and bytes of this owned installation."""
    root = root.resolve()
    report_path = root / REPORT
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise BuildError("The startup project needs a valid " + REPORT + "; run the startup recovery tool.") from error
    if (not isinstance(report, dict) or report.get("schema") != 1 or
            report.get("target") != "startup" or report.get("fullGameReady") is not False):
        raise BuildError("Startup recovery evidence must identify its diagnostic scope and fullGameReady=false.")
    if report.get("sourceBuilderFingerprint") != game_key:
        raise BuildError("Startup recovery belongs to a different original installation; regenerate its closure.")
    if not re.fullmatch(r"[0-9a-f]{64}", str(report.get("recoveryReceiptSha256", ""))):
        raise BuildError("Startup recovery is missing its original recovery receipt fingerprint.")
    scenes = report.get("selectedScenes")
    if (not isinstance(scenes, list) or any(not isinstance(p, str) for p in scenes) or
            tuple(PurePosixPath(p).stem for p in scenes) != SCENE_NAMES):
        raise BuildError("Startup recovery must retain Bootstrap, Intro, Gloomhaven_unified and MainMenu in that order.")
    for scene in scenes:
        path = PurePosixPath(scene)
        if (path.is_absolute() or ".." in path.parts or "\\" in scene or
                not scene.startswith("Assets/") or path.suffix != ".unity"):
            raise BuildError("Startup recovery contains an unsafe scene path.")
    declared = report.get("files")
    if not isinstance(declared, list) or not declared:
        raise BuildError("Startup recovery has no declared asset inventory.")
    paths = []
    originals = {Path(f["path"]).name: f for f in game_files if f["path"].startswith("Managed/")}
    for item in declared:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise BuildError("Startup recovery contains an invalid asset inventory row.")
        relative = item["path"]
        path = PurePosixPath(relative)
        if (path.is_absolute() or ".." in path.parts or "\\" in relative or relative == REPORT or
                not relative.startswith(("Assets/", "ProjectSettings/", "Packages/"))):
            raise BuildError("Startup recovery contains an unsafe asset path: " + relative)
        if (not re.fullmatch(r"[0-9a-f]{64}", str(item.get("sha256", ""))) or
                type(item.get("size")) is not int or item["size"] < 0):
            raise BuildError("Startup recovery contains an invalid asset fingerprint: " + relative)
        if path.suffix.lower() in (".cs", ".exe", ".so", ".env", ".keystore", ".jks", ".key", ".pem"):
            raise BuildError("Startup exports cannot inject executable source/native code or secrets: " + relative)
        if path.suffix.lower() == ".dll":
            original = originals.get(path.name)
            if not original or original["sha256"] != item["sha256"] or original["size"] != item["size"]:
                raise BuildError("Startup managed plugins must match the owned original DLL: " + relative)
            if relative + ".meta" not in [row.get("path") for row in declared if isinstance(row, dict)]:
                raise BuildError("Startup managed plugin lost its recovered script GUID: " + relative)
        paths.append(relative)
    if len(paths) != len(set(paths)):
        raise BuildError("Startup recovery contains duplicate asset inventory paths.")
    files = inventory(root)
    if snapshot_receipt:
        files = [item for item in files if item["path"] != ".snapshot.json"]
    payload = [item for item in files if item["path"] != REPORT]
    expected = sorted(({"path": row["path"], "sha256": row["sha256"], "size": row["size"]}
                       for row in declared), key=lambda row: row["path"])
    if payload != expected:
        raise BuildError("Startup assets changed or include undeclared files; regenerate the startup export.")
    if any(scene not in paths or scene + ".meta" not in paths for scene in scenes):
        raise BuildError("Startup recovery is missing an original scene or its script/asset identity.")
    return {"key": value_hash({"files": files}), "files": files,
            "reportSha256": digest(report_path), "scenes": scenes,
            "scope": "original-startup-diagnostic", "fullGameReady": False}


def _yaml_blocks(text: str) -> list[tuple[str, str, str]]:
    return [(match.group(1), match.group(2), match.group(0)) for match in
            re.finditer(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)", text, re.MULTILINE | re.DOTALL)]


def _yaml_field(block: str, name: str) -> str:
    values = re.findall(r"^  " + re.escape(name) + r":[ \t]*([^\n]*)$", block, re.MULTILINE)
    if len(values) != 1:
        raise BuildError("A recovered video binding has no unique " + name + ".")
    return values[0]


def _yaml_name(value: str) -> str:
    if value.startswith('"'):
        try:
            return json.loads(value)
        except ValueError as error:
            raise BuildError("A recovered video object's quoted name is invalid.") from error
    if value.startswith("'") and value.endswith("'"):
        return value[1:-1].replace("''", "'")
    return value


def _file_id(value: str) -> str:
    match = re.fullmatch(r"\{fileID: (-?\d+)\}", value)
    if not match:
        raise BuildError("A recovered video object reference is not local to its scene.")
    return match.group(1)


def _copy_movie(source: Path, target: Path, relative: str) -> dict:
    if source.is_symlink() or not source.is_file():
        raise BuildError("A required original movie is missing or linked: " + relative)
    for parent in source.parents:
        if parent.is_symlink():
            raise BuildError("Original movie parents must not be symbolic links: " + relative)
    record = record_file(source, relative)
    for parent in (target, *target.parents):
        if parent.is_symlink():
            raise BuildError("Generated movie destinations must not be symbolic links: " + relative)
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.is_file() and target.stat().st_size == record["size"] and digest(target) == record["sha256"]:
        return record
    temporary = target.with_name(target.name + ".tmp-" + uuid.uuid4().hex)
    try:
        shutil.copyfile(source, temporary)
        if temporary.stat().st_size != record["size"] or digest(temporary) != record["sha256"]:
            raise BuildError("An original movie changed while copied: " + relative)
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)
    return record


def stage_startup_movies(project: Path, game: Path) -> dict:
    """Deliver exactly the native menu movies and replace unsupported editor clip imports.

    B614 successfully starts the real rig, but its capture reports the absent native
    Movies/Ambient directory. Its recovered MP4 VideoClips also fail import under our
    Linux Unity2021.3 editor. Preserve the original H264 bytes as file-backed media;
    runtime scene binding supplies the verified extracted URL before native Start.
    Only generated VideoPlayer data-source fields change. Native timing, rendering,
    audio and callbacks remain authored. Narrative movies are outside this menu
    closure; a later campaign export must declare them independently.
    """
    project, game = project.resolve(), game.resolve()
    if project == game or game in project.parents or project in game.parents:
        raise BuildError("Movie staging must use a generated project separate from the owned game.")
    try:
        recovered = json.loads((project / REPORT).read_text(encoding="utf-8"))
        scenes = recovered["selectedScenes"]
    except (OSError, ValueError, KeyError) as error:
        raise BuildError("Movie staging needs the verified original startup scene closure.") from error
    if not isinstance(scenes, list) or tuple(PurePosixPath(str(path)).stem for path in scenes) != SCENE_NAMES:
        raise BuildError("Movie staging needs the declared original menu scene order.")
    for scene in scenes:
        path = PurePosixPath(scene)
        if path.is_absolute() or ".." in path.parts or "\\" in scene or not scene.startswith("Assets/") or path.suffix != ".unity":
            raise BuildError("Movie staging contains an unsafe scene path.")

    # The original Android branches of SceneController and VideoCamera select
    # Movies/*.mov, not the Switch's Movies_MP4_30. The actual owned .mov files are
    # MP4/H264 media; changing names/platform branches is unnecessary here.
    ambient = game / "StreamingAssets/Movies/Ambient"
    external = sorted(ambient.glob("*.mov")) if ambient.is_dir() else []
    if not external:
        raise BuildError("Original startup requires the owned Movies/Ambient menu alternatives.")

    clips_by_guid = {}
    for path in sorted((project / "Assets/VideoClip").rglob("*")):
        if not path.is_file() or path.suffix.lower() not in VIDEO_EXTENSIONS:
            continue
        meta = Path(str(path) + ".meta")
        try:
            guids = re.findall(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8"), re.MULTILINE)
        except OSError as error:
            raise BuildError("A recovered VideoClip lost its source GUID: " + path.name) from error
        if len(guids) != 1 or guids[0] in clips_by_guid:
            raise BuildError("Recovered VideoClips have a missing or duplicate GUID: " + path.name)
        clips_by_guid[guids[0]] = path

    selected, rewrites = {}, []
    for relative in scenes:
        scene = project / relative
        if scene.is_symlink() or not scene.is_file():
            raise BuildError("A generated movie scene is missing or linked: " + relative)
        text = scene.read_text(encoding="utf-8")
        blocks = _yaml_blocks(text)
        objects = {identity: _yaml_name(_yaml_field(block, "m_Name")) for kind, identity, block in blocks if kind == "1"}
        transforms = {}
        object_transform = {}
        for kind, identity, block in blocks:
            if kind in ("4", "224"):
                owner, parent = _file_id(_yaml_field(block, "m_GameObject")), _file_id(_yaml_field(block, "m_Father"))
                transforms[identity] = (owner, parent)
                object_transform[owner] = identity
        def hierarchy(owner: str) -> str:
            names, visited, transform = [], set(), object_transform.get(owner)
            while transform and transform != "0":
                if transform in visited or transform not in transforms:
                    raise BuildError("A recovered video transform hierarchy is cyclic or incomplete.")
                visited.add(transform)
                current, transform = transforms[transform]
                if current not in objects or "/" in objects[current]:
                    raise BuildError("A recovered video object has an ambiguous hierarchy name.")
                names.append(objects[current])
            if not names:
                raise BuildError("A recovered video player has no scene transform.")
            return "/".join(reversed(names))
        bound_paths = set()
        for kind, identity, block in blocks:
            if kind != "328":
                continue
            clip = _yaml_field(block, "m_VideoClip")
            if clip == "{fileID: 0}":
                continue  # Authored URL/menu background players retain their native path.
            match = re.fullmatch(r"\{fileID: 32900000, guid: ([0-9a-f]{32}), type: 3\}", clip)
            if not match or match.group(1) not in clips_by_guid:
                raise BuildError("A native menu VideoPlayer references an unrecovered clip: " + relative)
            guid = match.group(1)
            owner = _file_id(_yaml_field(block, "m_GameObject"))
            player_path = hierarchy(owner)
            if player_path in bound_paths:
                raise BuildError("Two native movie players have the same scene hierarchy: " + player_path)
            bound_paths.add(player_path)
            binding = {"scene": PurePosixPath(relative).stem, "playerName": objects[owner], "playerPath": player_path,
                       "playerFileId": identity, "sourceScene": relative}
            selected.setdefault(guid, []).append(binding)
            if _yaml_field(block, "m_DataSource") != "0" or _yaml_field(block, "m_Url"):
                raise BuildError("An embedded native movie has an unexpected data-source shape: " + player_path)
            changed = re.sub(r"^  m_VideoClip:.*$", "  m_VideoClip: {fileID: 0}", block, flags=re.MULTILINE)
            changed = re.sub(r"^  m_DataSource: 0$", "  m_DataSource: 1", changed, flags=re.MULTILINE)
            text = text.replace(block, changed, 1)
        rewrites.append((scene, text))
    if not selected:
        raise BuildError("Original menu movie delivery found no embedded clip bindings.")

    # Preflight references before copying/removing anything. A future export may
    # add another consumer; report it rather than deleting an asset still in use.
    remaining = {}
    clip_metas = {Path(str(clips_by_guid[guid]) + ".meta") for guid in selected}
    rewritten_scenes = dict(rewrites)
    for path in sorted((project / "Assets").rglob("*")):
        if not path.is_file() or path.suffix not in SERIALIZED_EXTENSIONS or path in clip_metas:
            continue
        text = rewritten_scenes.get(path)
        if text is None:
            text = path.read_text(encoding="utf-8", errors="ignore")
        for guid in selected:
            if "guid: " + guid in text:
                remaining.setdefault(guid, []).append(path.relative_to(project).as_posix())
    if remaining:
        raise BuildError("File-backed startup movies retain unsupported clip references: " + json.dumps(remaining, sort_keys=True))

    clip_records = []
    for guid, bindings in sorted(selected.items()):
        source = clips_by_guid[guid]
        relative = "StreamingAssets/QuestOriginalMovies/" + guid + source.suffix.lower()
        copied = _copy_movie(source, project / "Assets" / relative, relative)
        clip_records.append({**copied, "guid": guid, "name": source.stem, "source": source.relative_to(project).as_posix(), "bindings": bindings})
    movie_records = []
    for source in external:
        relative = source.relative_to(game).as_posix()
        copied = _copy_movie(source, project / "Assets" / relative, relative)
        movie_records.append({**copied, "source": relative})

    for scene, text in rewrites:
        scene.write_text(text, encoding="utf-8")

    # Drop importer inputs only after proving every reference in this generated
    # closure was replaced. Leaving a Linux-invalid importer is neither a usable
    # fallback nor a reason to include a second 220MB trailer payload in the APK.
    for guid in selected:
        source = clips_by_guid[guid]
        source.unlink()
        Path(str(source) + ".meta").unlink()
    result = {"schema": 1, "scope": "original-startup-menu-movies", "fullGameReady": False,
              "clips": clip_records, "externalMovies": movie_records,
              "totalBytes": sum(row["size"] for row in clip_records + movie_records)}
    write_json(project / "Assets/Quest/Resources" / MOVIES_REPORT, result)
    return result
