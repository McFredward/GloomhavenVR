"""Share closed, pinned compiler-tool receipts between owned wizard sessions.

The pointer chooses one receipt; it never grants authority to executable names,
old stats, extraction markers or a folder scan. Store.valid qualifies every
published tool/support file using the existing accepted-byte witness database.
Windows compiler provisioning is supported here. Linux's external system Git
and APK signing remain on their ordinary provisioning paths in this revision.
"""
from __future__ import annotations

from copy import deepcopy
import json
from pathlib import Path
import re
import stat

import provision
from state import WizardError, atomic_json, digest, ordinary, value_hash

OWNER = "Quest wizard shared pinned compiler tools"
MAX_POINTER_BYTES = 16384
MAX_METADATA_BYTES = 65536
MAX_RECEIPT_BYTES = 64 * 1048576
HEX = re.compile(r"[0-9a-f]{64}\Z")
SESSION = re.compile(r"[0-9a-f]{32}\Z")
NAMES = ("git", "dotnet8", "dotnet10")


def recipe(kind="compiler", *, lock=None, host=None):
    """Return an exact pinned selection; unknown kinds are never reusable."""
    selected = provision.LOCK if lock is None else lock
    host = provision.host_key() if host is None else host
    names = NAMES if kind == "compiler" else ("apkJdk", "apkBuildTools") if kind == "apk-signing" else ()
    recipes = selected if host == "windows-x64" else selected.get("linux", {})
    return {"schema": 1, "kind": kind, "host": host,
            "recipes": {name: deepcopy(recipes.get(name)) for name in names}}


def _supported(selected):
    if (not isinstance(selected, dict) or set(selected) != {"schema", "kind", "host", "recipes"}
            or selected.get("schema") != 1 or selected.get("kind") != "compiler"
            or selected.get("host") != "windows-x64" or not isinstance(selected.get("recipes"), dict)
            or set(selected["recipes"]) != set(NAMES)):
        return False
    for spec in selected["recipes"].values():
        if (not isinstance(spec, dict) or not isinstance(spec.get("version"), str)
                or not spec["version"] or not isinstance(spec.get("executable"), str)
                or not spec["executable"] or spec.get("algorithm") not in ("sha256", "sha512")
                or not isinstance(spec.get("hash"), str)
                or not re.fullmatch(r"[0-9a-f]{64}" if spec["algorithm"] == "sha256" else r"[0-9a-f]{128}", spec["hash"])):
            return False
        if any(character in spec["version"] for character in ("/", "\\", ":", "\0")):
            return False
        executable = Path(spec["executable"])
        if (executable.is_absolute() or ".." in executable.parts or "\\" in spec["executable"]
                or ":" in spec["executable"] or "\0" in spec["executable"]):
            return False
    return True


def _owned(store, path):
    root, path = ordinary(Path(store.root)), ordinary(Path(path))
    if root not in path.parents:
        raise WizardError("receipt_path", "Shared tools must stay inside the owned wizard workspace.")
    if path.exists():
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise WizardError("receipt_path", "Shared tool metadata must be an ordinary owned file.")
    return path


def _pointer(store, selected):
    return _owned(store, store.root / "tools/shared-receipts" / (value_hash(selected) + ".json"))


def _read(path, limit):
    if path.stat().st_size > limit:
        raise ValueError("Shared tool metadata exceeds its bounded size.")
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        raise ValueError("Shared tool metadata grew during its bounded read.")
    value = json.loads(raw)
    if not isinstance(value, dict) or type(value.get("schema")) is not int or value["schema"] != 1:
        raise ValueError("Shared tool metadata schema differs.")
    return value


def _closed(store, session, stage_key, selected, expected_receipt=None):
    if (not isinstance(session, str) or SESSION.fullmatch(session) is None
            or not isinstance(stage_key, str) or HEX.fullmatch(stage_key) is None):
        return None
    receipt_path = _owned(store, store.receipt(session, "tools"))
    if not receipt_path.is_file() or receipt_path.stat().st_size > MAX_RECEIPT_BYTES:
        return None
    # Bind the exact complete output roster too. Dropping a support-file row
    # from an otherwise well-shaped receipt cannot make a broken SDK reusable.
    receipt_hash = digest(receipt_path)
    if expected_receipt is not None and receipt_hash != expected_receipt:
        return None
    receipt = store.valid(session, "tools", stage_key, report_progress=False)
    if (not isinstance(receipt, dict) or receipt.get("schema") != 1
            or not isinstance(receipt.get("outputs"), list) or not 1 <= len(receipt["outputs"]) <= 300000
            or not isinstance(receipt.get("details"), dict) or set(receipt["details"]) != set(NAMES)):
        return None
    outputs = {}
    for row in receipt["outputs"]:
        path = _owned(store, store.root / row["path"])
        if path in outputs:
            return None
        outputs[path] = row
    documents = [path for path in outputs if re.fullmatch(r"sessions/[0-9a-f]{32}/tools\.json",
                                                        path.relative_to(store.root).as_posix())]
    if len(documents) != 1:
        return None
    # A new stage can retain the previous session's small, already qualified
    # metadata document as one of its own outputs. The current closed receipt
    # owns that complete roster, so no new SDK files or copies are necessary.
    document = documents[0]
    value = _read(document, MAX_METADATA_BYTES)
    expected = {"schema": 1, **receipt["details"], "host": selected["host"],
                "pins": {name: value_hash(selected["recipes"][name]) for name in NAMES}}
    if value != expected:
        return None
    # A complete producer receipt contains all extracted support files, not
    # merely an executable marker. Check each pinned root's actual executable
    # and closed extraction marker membership without opening tool payloads.
    for name in NAMES:
        spec = selected["recipes"][name]
        root = ordinary(store.root / "tools" / (name + "-" + spec["version"]))
        executable = _owned(store, root / spec["executable"])
        marker = _owned(store, root / "wizard-tool.json")
        if executable not in outputs or marker not in outputs or receipt["details"][name] != str(executable):
            return None
        manifest = _read(marker, MAX_METADATA_BYTES)
        if (manifest != {"schema": 1, "key": value_hash(spec), "complete": True,
                         "executableSha256": outputs[executable]["sha256"], "links": {}}
                or not any(path != marker and root in path.parents for path in outputs)):
            return None
    if digest(receipt_path) != receipt_hash:
        return None
    return [*outputs], deepcopy(receipt["details"]), receipt_hash


def lookup(store, selected):
    """Return ``(paths, details)`` or None; caller still publishes its stage.

    Only one bounded current pointer is read. A broken cache or changed tool
    simply leaves normal pinned provisioning/repair authoritative. Validation
    never updates progress in the pointer's earlier session.
    """
    if not _supported(selected):
        return None
    try:
        path = _pointer(store, selected)
        if not path.is_file():
            return None
        value = _read(path, MAX_POINTER_BYTES)
        if (set(value) != {"schema", "owner", "recipe", "recipeKey", "session", "stageKey", "receiptSha256"}
                or value.get("schema") != 1 or value.get("owner") != OWNER
                or value.get("recipe") != selected or value.get("recipeKey") != value_hash(selected)
                or not isinstance(value.get("receiptSha256"), str) or HEX.fullmatch(value["receiptSha256"]) is None):
            return None
        result = _closed(store, value.get("session"), value.get("stageKey"), selected, value["receiptSha256"])
        return result[:2] if result else None
    except (OSError, ValueError, TypeError, KeyError, WizardError):
        return None


def record(store, session, stage_key, selected):
    """Publish the pointer only after a real, valid tools stage was closed."""
    if not _supported(selected):
        return False
    try:
        result = _closed(store, session, stage_key, selected)
        if result is None:
            return False
        path = _pointer(store, selected)
        atomic_json(path, {"schema": 1, "owner": OWNER, "recipe": selected,
                          "recipeKey": value_hash(selected), "session": session, "stageKey": stage_key,
                          "receiptSha256": result[2]})
        return True
    except (OSError, ValueError, TypeError, KeyError, WizardError):
        return False
