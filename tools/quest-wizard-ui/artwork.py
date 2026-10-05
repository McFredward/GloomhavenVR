"""Bounded optional artwork from a witnessed, locally recovered owned game.

The HTTP backend supplies the project and already-verified game fingerprint. No
HTTP path, UI choice, filename guessing, Unity decoding or downloads enter this
adapter. Returned raster files remain private; they are never release content.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct

MAX_MANIFEST = 64 * 1024 * 1024
MAX_IMAGE = 8 * 1024 * 1024
# Optional display ranking, not asset identity or DLC ownership. These original
# decorative files are considered only after exact source report/hash validation.
PREFERRED_ART = {name: 20 - index for index, name in enumerate((
    "HeroPortrait_Brute.png", "DLC_Promo_JawsOfTheLion.png", "DLC_Promo_SoloScenarios.png"))}


def _owned_file(project, relative):
    if not isinstance(relative, str) or "\\" in relative:
        return None
    logical = PurePosixPath(relative)
    if (logical.is_absolute() or logical.as_posix() != relative or
            any(part in ("", ".", "..") for part in logical.parts) or not relative.startswith("Assets/")):
        return None
    path = project.joinpath(*logical.parts)
    if any(parent.is_symlink() for parent in (path, *path.parents) if parent != project.parent):
        return None
    try:
        path.resolve().relative_to(project.resolve())
    except (OSError, ValueError):
        return None
    return path if path.is_file() else None


def verified_project_artwork(project_root, expected_game_key, limit=3):
    """Return at most three verified local PNGs, or [] when evidence is absent.

    Descriptors are server-private. Expose only id/altCode and a guarded opaque
    /api/artwork URL to the browser. The backend must call read_artwork for every
    response rather than serving arbitrary paths or trusting a stale descriptor.
    """
    if not isinstance(expected_game_key, str) or not re.fullmatch(r"[0-9a-f]{64}", expected_game_key):
        return []
    if not isinstance(project_root, (str, Path)):
        return []
    try:
        count = max(0, min(3, int(limit)))
    except (ValueError, TypeError):
        return []
    if not count:
        return []
    project = Path(project_root)
    report = project / "quest-campaign-report.json"
    try:
        if project.is_symlink() or report.is_symlink() or not report.is_file() or report.stat().st_size > MAX_MANIFEST:
            return []
        evidence = json.loads(report.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []
    if (not isinstance(evidence, dict) or not isinstance(evidence.get("readiness"), dict) or
            evidence.get("schema") != 1 or evidence.get("target") != "campaign" or
            evidence.get("sourceBuilderFingerprint") != expected_game_key or
            evidence.get("readiness", {}).get("fullOriginalCatalogRecovered") is not True or
            not isinstance(evidence.get("files"), list)):
        return []
    candidates = []
    for row in evidence["files"]:
        if not isinstance(row, dict):
            continue
        name = row.get("path", "")
        if not isinstance(name, str) or not name.startswith("Assets/Texture2D/") or not name.lower().endswith(".png"):
            continue
        # Ranking only picks decorative local artwork; it never labels an unknown
        # picture as a particular hero or changes game object identity/ownership.
        stem = PurePosixPath(name).stem.casefold()
        if any(word in stem for word in ("frame", "panel", "button", "textbox", "guild", "splash")):
            continue
        score = PREFERRED_ART.get(PurePosixPath(name).name, sum(word in stem for word in ("portrait", "background", "banner", "character", "campaign", "gloomhaven")))
        if (not score or type(row.get("size")) is not int or not 32 <= row["size"] <= MAX_IMAGE or
                not isinstance(row.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", row["sha256"])):
            continue
        candidates.append((-score, name, row))
    result = []
    for _, name, row in sorted(candidates)[:24]:
        try:
            path = _owned_file(project, name)
            if path is None or path.stat().st_size != row["size"]:
                continue
            data = path.read_bytes()
        except OSError:
            continue
        if hashlib.sha256(data).hexdigest() != row["sha256"] or len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
            continue
        width, height = struct.unpack_from(">II", data, 16)
        if not 128 <= width <= 4096 or not 128 <= height <= 4096 or max(width / height, height / width) > 3:
            continue
        identity = hashlib.sha256((expected_game_key + "|" + name + "|" + row["sha256"]).encode()).hexdigest()[:32]
        result.append({"id": identity, "assetPath": name, "projectRoot": str(project.resolve()), "sha256": row["sha256"],
                       "size": row["size"], "altCode": "ownedArtwork", "width": width, "height": height})
        if len(result) >= count:
            break
    return result


def read_artwork(descriptor):
    """Revalidate one private descriptor at response time, fail closed."""
    if (not isinstance(descriptor, dict) or type(descriptor.get("size")) is not int or
            not 32 <= descriptor["size"] <= MAX_IMAGE or not isinstance(descriptor.get("projectRoot"), str)):
        return None
    project = Path(descriptor.get("projectRoot", ""))
    try:
        path = _owned_file(project, descriptor.get("assetPath"))
        if path is None or path.stat().st_size != descriptor["size"]:
            return None
        data = path.read_bytes()
    except OSError:
        return None
    return data if hashlib.sha256(data).hexdigest() == descriptor.get("sha256") and data[:8] == b"\x89PNG\r\n\x1a\n" else None
