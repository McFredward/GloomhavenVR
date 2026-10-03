"""Validate a bounded original-game startup export without granting game readiness."""

from __future__ import annotations

import json
from pathlib import Path, PurePosixPath
import re

from storage import BuildError, digest, inventory, value_hash


REPORT = "quest-startup-report.json"
SCENE_NAMES = ("Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu")


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
