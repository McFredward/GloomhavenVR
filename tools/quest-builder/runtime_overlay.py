"""Exact late Quest runtime overlays for completed original preparation.

Only the whole shipped B692/B693/B717/current template profiles qualify. The current
Player still compiles the real new scripts. This transaction changes neither
original asset bytes, existing script GUIDs nor Unity Library.
"""
from __future__ import annotations

import base64
import hashlib
from pathlib import Path

from storage import BuildError, _ordinary_owned

PREFIX = "unity/GloomhavenVR.Quest/"
RUNTIME = PREFIX + "Assets/Quest/Runtime/"
TARGETS = tuple(RUNTIME + name for name in (
    "QuestGameBootstrap.cs", "QuestCameraVideoOutput.cs", "QuestGameVideos.cs",
    "QuestAudioClipContent.cs", "QuestMovieContent.cs"))
MAX_SCRIPT_BYTES = 512 * 1024


def _profile(*rows):
    return {name: {"path": name, "size": size, "sha256": checksum}
            for name, (size, checksum) in zip(TARGETS, rows) if size}


B692 = _profile(
    (39140, "57b478eb6ce0cbbc19b55156cf4d832a4d17ab3db48e3a74561db7c18dfa142c"),
    (28585, "931d23b2957c60d9fff8aa6396a4a921716c2a91e4256e96e5df016f14084b9e"),
    (20925, "25d222d3062f977463c4608a7d93047ccf13d213df9c504ea42f32525ab551b2"),
    (0, ""), (0, ""))
B693 = _profile(
    (40147, "13d342291f96a45fb06e1462b96edb7ef9d6cc87d47a64c15cbf9d68a56f2488"),
    (28585, "931d23b2957c60d9fff8aa6396a4a921716c2a91e4256e96e5df016f14084b9e"),
    (20925, "25d222d3062f977463c4608a7d93047ccf13d213df9c504ea42f32525ab551b2"),
    (8372, "d7a1e3c0ac643f75f37e5837cbb8849c334b1df8c5dde8ee2b09a72652a3cee2"), (0, ""))
B717 = _profile(
    (41145, "6ced6388681376258d397e891fdc70ce95570d3f920057343a92a80e3037c7e6"),
    (28763, "25f1b906d6666154b73d75fa3554f550f9ad9b106e2b60eff86e68d82a11b5c4"),
    (21068, "a555c4add725f73e6b75997aef78a42ac4d17c940dbf14f8ba8a1cb319788e90"),
    (8372, "d7a1e3c0ac643f75f37e5837cbb8849c334b1df8c5dde8ee2b09a72652a3cee2"),
    (10132, "720f83bbb1deabc6a08d1fabf3acdfb47f41682b17dd4c3ec2d3e1d45875e4d9"))
# Build735 extends only the startup reader's finite mod inventory admission:
# the existing four bank records may carry their exact numeric Story suffix.
# Retain the complete B717 template profile and refresh only this base-project
# script, preserving original assets, GUID metadata and imported Unity state.
CURRENT = {
    **B717,
    TARGETS[0]: {"path": TARGETS[0], "size": 43060,
                 "sha256": "52e6c2f59911441ee4bfdd8799a60df6b552702cd3809db7fe8f9970abd45597"},
}


def _selected(rows):
    selected = {}
    for row in rows:
        if row["path"] in TARGETS:
            if row["path"] in selected: return None
            selected[row["path"]] = row
    return selected


def normalize_rows(rows):
    """Normalize complete reviewed profiles, retaining every unknown input."""
    if _selected(rows) not in (B692, B693, B717, CURRENT): return rows
    return [B692[row["path"]] if row["path"] in B692 else row for row in rows
            if row["path"] not in TARGETS or row["path"] in B692]


def changes(previous, inputs):
    before, after = _selected(previous["mod"]["files"]), _selected(inputs["mod"]["files"])
    if before == after: return []
    if before not in (B692, B693, B717, CURRENT) or after != CURRENT:
        raise BuildError("Runtime overlay is outside its whole reviewed source profile.")
    return [(name, before.get(name), after[name]) for name in TARGETS
            if before.get(name) != after.get(name)]


def _record(relative, raw):
    return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def _read(path):
    path = _ordinary_owned(Path(path))
    if not path.is_file() or path.stat().st_size > MAX_SCRIPT_BYTES:
        raise BuildError("Runtime overlay has a missing/oversized script: " + path.name)
    with path.open("rb") as stream: raw = stream.read(MAX_SCRIPT_BYTES + 1)
    if len(raw) > MAX_SCRIPT_BYTES:
        raise BuildError("Runtime overlay exceeded its bounded script read: " + path.name)
    return raw


def actual(path, relative):
    path = _ordinary_owned(Path(path))
    return _record(relative, _read(path)) if path.exists() else {"path": relative, "absent": True}


def plan(preparation, previous, inputs, latest):
    result = []
    owners = [index for index, step in enumerate(preparation.value["steps"]) if step["name"] == "base-project"]
    for name, before, after in changes(previous, inputs):
        relative = name[len(PREFIX):]
        expected = {**before, "path": relative} if before else {"path": relative, "absent": True}
        if len(owners) != 1 or latest.get(relative) != ((owners[0], expected) if before else None):
            raise BuildError("Runtime overlay lost its exact latest script owner: " + relative)
        candidates = [path for path, row in preparation.sources if row == after]
        if len(candidates) != 1:
            raise BuildError("Runtime overlay has no unique qualified snapshot source: " + name)
        replacement = _read(candidates[0])
        after_record = {**after, "path": relative}
        live = actual(preparation.project / relative, relative)
        # A missing new script can already have been atomically copied before a
        # previous transaction was published. Only exact current bytes qualify.
        if live not in ((expected, after_record) if before is None else (expected,)) or _record(name, replacement) != after:
            raise BuildError("Runtime overlay project/snapshot bytes changed: " + relative)
        original = _read(preparation.project / relative) if before else b""
        result.append({"path": relative, "step": owners[0], "kind": "runtime",
                       "before": expected, "after": after_record,
                       "original": base64.b64encode(original).decode("ascii"),
                       "replacement": base64.b64encode(replacement).decode("ascii")})
    return result


def validate(preparation, row, name, before, after, latest, *, old):
    """Qualify old/new payloads and an optional new base-project contract."""
    relative = name[len(PREFIX):]
    try:
        original = base64.b64decode(row.get("original", ""), validate=True)
        replacement = base64.b64decode(row.get("replacement", ""), validate=True)
    except (TypeError, ValueError) as error:
        raise BuildError("Runtime overlay transaction has an invalid script payload.") from error
    expected_before = {**before, "path": relative} if before else {"path": relative, "absent": True}
    expected_after = {**after, "path": relative}
    expected_owner = (row.get("step"), expected_before if old else expected_after)
    if old and before is None: expected_owner = None
    if (row.get("kind") != "runtime" or row.get("path") != relative
            or type(row.get("step")) is not int or not 0 <= row["step"] < len(preparation.value["steps"])
            or preparation.value["steps"][row["step"]]["name"] != "base-project"
            or len(original) > MAX_SCRIPT_BYTES or len(replacement) > MAX_SCRIPT_BYTES
            or row.get("before") != expected_before or row.get("after") != expected_after
            or (_record(relative, original) != expected_before if before else bool(original))
            or _record(relative, replacement) != expected_after
            or latest.get(relative) != expected_owner):
        raise BuildError("Runtime overlay transaction lost its exact source/output contract: " + relative)
    candidates = [path for path, source in preparation.sources if source == after]
    if len(candidates) != 1 or _read(candidates[0]) != replacement:
        raise BuildError("Runtime overlay frozen source changed: " + relative)
    if actual(preparation.project / relative, relative) not in ((expected_before, expected_after) if old else (expected_after,)):
        raise BuildError("Runtime overlay script changed outside its transaction: " + relative)
    return replacement
