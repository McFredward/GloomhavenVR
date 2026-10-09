"""Exact late Editor repairs inside the completed-preparation transaction.

The preparation selector separately qualifies all original producers and the
complete input scope. This module permits only six reviewed existing Editor
scripts to change in an otherwise retained project. It never enumerates assets,
writes Unity Library, or replaces the scripts' existing .meta/GUID files.
"""
from __future__ import annotations

import base64
import hashlib
import os
from pathlib import Path
import uuid

from storage import BuildError, _ordinary_owned

PREFIX = "unity/GloomhavenVR.Quest/"
TARGETS = tuple(PREFIX + "Assets/Quest/Editor/" + name for name in (
    "QuestBuild.cs", "QuestWizardProgress.cs", "QuestStartupAddressablesBuild.cs",
    "QuestCampaignContentBuild.cs", "QuestSpriteGeometryValidation.cs", "QuestCampaignSpriteValidation.cs"))
MAX_SCRIPT_BYTES = 512 * 1024
# Filled only with exact source inventory rows reviewed for this repair. An
# unknown version remains a consumed producer, never a generally ignored file.
REVIEWED = {}


def _record(relative, raw):
    return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def _read(path):
    path = _ordinary_owned(Path(path))
    if not path.is_file() or path.stat().st_size > MAX_SCRIPT_BYTES:
        raise BuildError("Editor overlay has a missing/oversized script: " + path.name)
    with path.open("rb") as stream: raw = stream.read(MAX_SCRIPT_BYTES + 1)
    if len(raw) > MAX_SCRIPT_BYTES:
        raise BuildError("Editor overlay exceeded its bounded script read: " + path.name)
    return raw


def changes(previous, inputs):
    """Return an exact reviewed path pair, including during transaction replay."""
    before = {row["path"]: row for row in previous["mod"]["files"]}
    after = {row["path"]: row for row in inputs["mod"]["files"]}
    result = []
    for name in TARGETS:
        left, right = before.get(name), after.get(name)
        if left == right: continue
        profile = REVIEWED.get(name)
        if (not isinstance(profile, tuple) or len(profile) != 2
                or (left, right) != profile or left is None or right is None
                or set(left) != {"path", "size", "sha256"}
                or set(right) != {"path", "size", "sha256"}
                or left["path"] != name or right["path"] != name
                or not 0 < left["size"] <= MAX_SCRIPT_BYTES
                or not 0 < right["size"] <= MAX_SCRIPT_BYTES):
            raise BuildError("Editor overlay script is outside its exact reviewed source profile: " + name)
        result.append((name, left, right))
    return result


def plan(preparation, previous, inputs, latest):
    result = []
    for name, before, after in changes(previous, inputs):
        relative = name[len(PREFIX):]
        owned = latest.get(relative)
        expected = {**before, "path": relative}
        if (not owned or owned[1] != expected
                or preparation.value["steps"][owned[0]]["name"] != "base-project"):
            raise BuildError("Editor overlay lost its existing latest script owner: " + relative)
        candidates = [path for path, row in preparation.sources if row == after]
        if len(candidates) != 1:
            raise BuildError("Editor overlay has no unique qualified current snapshot source: " + name)
        original, replacement = _read(preparation.project / relative), _read(candidates[0])
        if _record(relative, original) != expected or _record(name, replacement) != after:
            raise BuildError("Editor overlay project/snapshot bytes changed: " + relative)
        result.append({"path": relative, "step": owned[0], "kind": "editor",
                       "before": expected, "after": {**after, "path": relative},
                       "original": base64.b64encode(original).decode("ascii"),
                       "replacement": base64.b64encode(replacement).decode("ascii")})
    return result


def validate(preparation, row, before, after, latest, *, old):
    """Requalify persisted payloads and their live owner before any publication."""
    relative = before["path"][len(PREFIX):]
    try:
        original = base64.b64decode(row.get("original", ""), validate=True)
        replacement = base64.b64decode(row.get("replacement", ""), validate=True)
    except (TypeError, ValueError) as error:
        raise BuildError("Editor overlay transaction contains an invalid script payload.") from error
    expected_before, expected_after = {**before, "path": relative}, {**after, "path": relative}
    if (row.get("kind") != "editor" or row.get("path") != relative
            or type(row.get("step")) is not int
            or not 0 <= row["step"] < len(preparation.value["steps"])
            or preparation.value["steps"][row["step"]]["name"] != "base-project"
            or len(original) > MAX_SCRIPT_BYTES or len(replacement) > MAX_SCRIPT_BYTES
            or row.get("before") != expected_before or row.get("after") != expected_after
            or _record(relative, original) != expected_before
            or _record(relative, replacement) != expected_after
            or latest.get(relative) != (row["step"], expected_before if old else expected_after)):
        raise BuildError("Editor overlay transaction lost its exact source/output contract: " + relative)
    candidates = [path for path, source in preparation.sources if source == after]
    if len(candidates) != 1 or _read(candidates[0]) != replacement:
        raise BuildError("Editor overlay current frozen snapshot changed: " + relative)
    actual = _record(relative, _read(preparation.project / relative))
    if actual not in ((expected_before, expected_after) if old else (expected_after,)):
        raise BuildError("Editor overlay script changed outside its transaction: " + relative)
    return replacement


def publish(path, payload):
    """Publish one already-qualified small script without changing its .meta."""
    path = _ordinary_owned(Path(path))
    temporary = _ordinary_owned(path.with_name(path.name + ".quest-editor-" + uuid.uuid4().hex))
    try:
        with temporary.open("wb") as stream:
            if stream.write(payload) != len(payload):
                raise BuildError("Editor overlay wrote fewer script bytes than requested: " + path.name)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
