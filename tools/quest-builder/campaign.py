"""Verify a completed local Campaign export before reusing it in the builder."""
import json
from pathlib import Path, PurePosixPath
import re
import shutil

from storage import BuildError, digest, value_hash, write_json

REPORT = "quest-campaign-report.json"
# Pinned exporter's Editor-only helper restores missing audio-effect parameter
# names through Unity's original importer while retaining existing native GUIDs.
RECOVERY_EDITOR_SOURCES = {
    "Assets/Editor/AssetRipperPatches/AudioMixerPostprocessor.cs":
        "9dcc222df079f5e4c15c6a9f2d50d0eb2b11c7eeeb7c36aa16f21530ced7bd5c"
}


def ensure_packed_sprite_manifest(project: Path) -> None:
    """Complete the generated import contract from witnessed legacy stage receipts."""
    target = project / "Assets/QuestOriginalCampaign/packed-sprites.json"
    if target.is_file(): return
    pointer = json.loads((project / "QuestRecovery/full-native-pointer-repair.json").read_text())
    if pointer.get("complete") is not True or pointer.get("remainingMissingPointerCount"):
        raise BuildError("Packed Sprite import requires completed original native pointer recovery.")
    sprites, atlases = [], []
    for row in pointer["additionalNativeTargets"]:
        if row["classId"] != 687078895: continue
        relative = "QuestRecovery/packed-sprites-" + row["guid"] + ".json"
        receipt = json.loads((project / relative).read_text())
        if (receipt.get("schema") != 1 or receipt["originalAtlasCollection"] != row["collection"]
                or receipt["originalAtlasPathId"] != row["pathId"]
                or len(receipt["restoredMembers"]) != receipt["originalMemberCount"]):
            raise BuildError("Packed Sprite receipt differs from its original native atlas.")
        for sprite in receipt["restoredMembers"]:
            if digest(project / sprite["assetPath"]) != sprite["sha256"]:
                raise BuildError("Packed Sprite bytes changed after native restoration.")
        sprites.extend(receipt["restoredMembers"])
        atlases.append({"assetPath": row["path"], "guid": row["guid"], "sourceCollection": row["collection"],
                       "sourcePathId": row["pathId"], "spriteCount": receipt["originalMemberCount"], "receiptPath": relative})
    if not atlases or not sprites:
        raise BuildError("Campaign import has no restored original packed Sprite atlas.")
    write_json(target, {"schema": 1, "spriteCount": len(sprites), "atlases": atlases, "sprites": sprites,
                        "unityImportVerified": False, "headsetPictureVerified": False})


def stage_file_backed_extras(project: Path, game: Path) -> list[str]:
    """Retain original Campaign auxiliary files alongside rebuilt native bundles."""
    source = game / "StreamingAssets"
    destination = project / "Assets/StreamingAssets"
    copied = []
    for path in sorted(source.rglob("*")):
        if not path.is_file() or path.suffix == ".meta": continue
        relative = path.relative_to(source)
        # Native Android bundles replace aa; chosen original .mov files, rules,
        # procedures and engine data have their own exact conversion contracts.
        # Store-service configuration and the excluded mode are outside scope.
        if relative.parts[0] in ("aa", "Rulebase", "Procedures", "EOS", "Movies", "Movies_MP4", "Movies_MP4_30") or relative.name in ("GloomData.dat", "GuildmasterAutoComplete.yml"):
            continue
        if path.is_symlink() or path.suffix.lower() in (".exe", ".dll", ".so", ".env", ".key", ".pem"):
            raise BuildError("Original Campaign auxiliary path is not an ordinary owned data file: " + relative.as_posix())
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        if digest(path) != digest(target):
            raise BuildError("Owned Campaign auxiliary file changed during staging: " + relative.as_posix())
        copied.append(relative.as_posix())
    return copied


def inspect_project(root: Path, game_key: str, game_files: list[dict]) -> dict:
    root = Path(root).resolve()
    try:
        report = json.loads((root / REPORT).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise BuildError("Campaign reuse requires its completed native recovery report.") from error
    readiness = report.get("readiness", {})
    if (report.get("schema") != 1 or report.get("target") != "campaign"
            or report.get("sourceBuilderFingerprint") != game_key
            or readiness.get("originalSceneClosureStaged") is not True
            or readiness.get("fullOriginalCatalogRecovered") is not True):
        raise BuildError("Campaign recovery is incomplete or belongs to another owned installation.")
    refs = report.get("missingReferences", {})
    if (report.get("unresolvedAddressables") or refs.get("missingGuidCount", 0)
            or refs.get("duplicateGuidCount", 0)
            or report.get("managedScriptBindings", {}).get("unexpectedUnresolvedCount", 0)):
        raise BuildError("Campaign recovery still has unresolved native content or script references.")
    scenes = report.get("selectedScenes", [])
    if len(scenes) != 13 or len(set(scenes)) != 13:
        raise BuildError("Campaign reuse must retain all13 original scenes in their original order.")
    rows = report.get("files")
    if not isinstance(rows, list) or not rows:
        raise BuildError("Campaign recovery has no witnessed file inventory.")
    originals = {Path(row["path"]).name: row for row in game_files if row["path"].startswith("Managed/")}
    seen = set()
    for row in rows:
        relative = row.get("path", "")
        path = PurePosixPath(relative)
        if (not isinstance(relative, str) or path.is_absolute() or ".." in path.parts or "\\" in relative
                or relative in seen or not relative.startswith(("Assets/", "ProjectSettings/", "Packages/", "QuestRecovery/"))
                or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))
                or type(row.get("size")) is not int or row["size"] < 0):
            raise BuildError("Campaign recovery contains an invalid file identity.")
        seen.add(relative)
        local = root / relative
        if (local.is_symlink() or not local.is_file() or local.stat().st_size != row["size"]
                or digest(local) != row["sha256"]):
            raise BuildError("Campaign recovery bytes changed: " + relative)
        if path.suffix.lower() == ".cs" and RECOVERY_EDITOR_SOURCES.get(relative) != row["sha256"]:
            raise BuildError("Recovered Editor source differs from the pinned exporter helper: " + relative)
        if path.suffix.lower() in (".exe", ".so", ".env", ".keystore", ".jks", ".key", ".pem"):
            raise BuildError("A reused native export cannot inject executable source or secrets: " + relative)
        if path.suffix.lower() == ".dll":
            original = originals.get(path.name)
            if not original or original["sha256"] != row["sha256"] or original["size"] != row["size"]:
                raise BuildError("Recovered Campaign DLL differs from the owned game: " + relative)
    if any(scene not in seen or scene + ".meta" not in seen for scene in scenes):
        raise BuildError("An original Campaign scene lost its native bytes or GUID.")
    actual = {path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()}
    if actual - seen - {REPORT, "quest-startup-report.json"}:
        raise BuildError("Campaign reuse contains undeclared files; regenerate its native recovery receipt.")
    return {"key": value_hash({"files": rows}), "reportSha256": digest(root / REPORT),
            "scenes": scenes, "scope": "complete-original-campaign-assets"}


def verify_copy(root: Path, expected: dict) -> None:
    """Detect edits during copying before the generated tree is adapted."""
    report = json.loads((root / REPORT).read_text(encoding="utf-8"))
    if digest(root / REPORT) != expected["reportSha256"] or value_hash({"files": report["files"]}) != expected["key"]:
        raise BuildError("Campaign reuse evidence changed during copying.")
    for row in report["files"]:
        path = root / row["path"]
        if not path.is_file() or path.stat().st_size != row["size"] or digest(path) != row["sha256"]:
            raise BuildError("Campaign recovery changed during copying: " + row["path"])
