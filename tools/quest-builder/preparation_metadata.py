"""Journal a small current-input refresh after fully qualified asset preparation.

Every original/Android asset producer and latest contract remains qualified by
preparation_identity and Preparation. This transaction changes only four JSON
documents whose input identity was written after the immutable asset producers.
It never copies or recompresses archives, discards a later owner, or edits Unity
Library. A kill between document publication and journal publication is replayed
from the exact old/new byte records before normal checkpoint qualification.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re

from storage import BuildError, _ordinary_owned, canonical, digest, value_hash, write_json

OWNER = "Quest completed preparation identity refresh"
MANIFESTS = {
    "Assets/Quest/Resources/quest-startup-content.json": ("archive-cleanup", "quest-startup-content.zip"),
    "Assets/Quest/Resources/quest-mod-content.json": ("mod-resource-banks", "quest-mod-content.zip"),
}
INPUTS = "Assets/StreamingAssets/Quest/input-manifest.json"
SETTINGS = "QuestBuilderSettings.json"
PATHS = (*MANIFESTS, INPUTS, SETTINGS)
MAX_BYTES = 8 * 1024 * 1024
HEX = re.compile(r"[0-9a-f]{64}\Z")


def _read(path):
    path = _ordinary_owned(Path(path))
    if not path.is_file() or path.stat().st_size > MAX_BYTES:
        raise BuildError("Preparation identity refresh has a missing/oversized JSON file: " + path.name)
    try:
        with path.open("rb") as stream: raw = stream.read(MAX_BYTES + 1)
        if len(raw) > MAX_BYTES:
            raise BuildError("Preparation identity refresh JSON exceeded its read limit: " + path.name)
        value = json.loads(raw.decode("utf-8"))
    except (OSError, ValueError) as error:
        raise BuildError("Preparation identity refresh JSON cannot be read: " + path.name) from error
    if not isinstance(value, dict):
        raise BuildError("Preparation identity refresh JSON is not an object: " + path.name)
    return value


def _record(relative, payload):
    raw = canonical(payload) + b"\n"
    return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def _latest(value):
    return {row["path"]: (index, row) for index, step in enumerate(value["steps"])
            for row in step["outputs"]}


def _replacement_journal(value, plan, sources):
    updated = {**value, "inputKey": plan["toInputKey"], "sources": sources}
    changed = {row["path"]: row for row in plan["files"]}
    updated["steps"] = []
    for index, step in enumerate(value["steps"]):
        outputs = []
        for row in step["outputs"]:
            replacement = changed.get(row["path"])
            outputs.append(replacement["after"] if replacement and replacement["step"] == index else row)
        updated["steps"].append({**step, "outputs": outputs})
    return updated


def _previous(preparation, input_key):
    if not isinstance(input_key, str) or not HEX.fullmatch(input_key):
        raise BuildError("Preparation identity refresh preceding input key is invalid.")
    previous = _read(preparation.output / "manifests" / (input_key + ".json"))
    if (previous.get("inputKey") != input_key
            or value_hash({name: item for name, item in previous.items() if name != "inputKey"}) != input_key):
        raise BuildError("Preparation identity refresh lost its preceding input manifest.")
    return previous


def _replacement(relative, document, previous, inputs, latest):
    """Change only current-input identity; retain every archived asset byte contract."""
    if not isinstance(document, dict) or document.get("inputKey") != previous["inputKey"]:
        raise BuildError("Completed preparation current-input document differs from its preceding key: " + relative)
    if relative in MANIFESTS:
        archive = MANIFESTS[relative][1]
        archive_record = latest.get("Assets/StreamingAssets/" + archive)
        if (set(document) != {"schema", "inputKey", "archive", "archiveSha256", "files"}
                or document.get("schema") != 1 or document.get("archive") != archive
                or not isinstance(document.get("files"), list) or not archive_record
                or document.get("archiveSha256") != archive_record[1].get("sha256")):
            raise BuildError("Completed preparation archive metadata lost its exact asset-byte contract: " + relative)
        return {**document, "inputKey": inputs["inputKey"]}
    if relative == INPUTS:
        if document != previous:
            raise BuildError("Completed preparation input document differs from its immutable manifest.")
        return inputs
    if (set(document) != {"schema", "target", "inputKey", "profileSha256", "package", "modBuild"}
            or document.get("schema") != 1 or document.get("target") != "game"
            or document.get("modBuild") != previous["mod"]["modBuild"]):
        raise BuildError("Completed preparation settings have an unsupported input identity.")
    return {**document, "inputKey": inputs["inputKey"], "modBuild": inputs["mod"]["modBuild"]}


def _plan(preparation, inputs):
    value = preparation.value
    previous = _previous(preparation, value["inputKey"])
    latest = _latest(value)
    plan = {"schema": 1, "owner": OWNER, "project": value["project"],
            "fromInputKey": value["inputKey"], "toInputKey": inputs["inputKey"],
            "beforeJournalSha256": value_hash(value), "files": []}
    for relative in PATHS:
        expected_owner = MANIFESTS[relative][0] if relative in MANIFESTS else "final-settings"
        if relative not in latest:
            raise BuildError("Completed preparation has no owned current-input document: " + relative)
        index, record = latest[relative]
        if value["steps"][index]["name"] != expected_owner or record.get("absent"):
            raise BuildError("Completed preparation current-input document has an unexpected latest owner: " + relative)
        document = _read(preparation.project / relative)
        if _record(relative, document) != record:
            raise BuildError("Completed preparation current-input bytes differ from their latest contract: " + relative)
        replacement = _replacement(relative, document, previous, inputs, latest)
        plan["files"].append({"path": relative, "step": index, "before": record,
                              "after": _record(relative, replacement), "original": document,
                              "replacement": replacement})
    updated = _replacement_journal(value, plan, preparation.source_stamps)
    plan["afterJournalSha256"] = value_hash(updated)
    return plan


def refresh(preparation, inputs, *, recovering=False):
    """Refresh or replay the exact four documents, then atomically publish their owner."""
    path = _ordinary_owned(preparation.root / "metadata-refresh.json")
    if (not isinstance(inputs, dict) or inputs.get("inputKey") != preparation.identity["inputKey"]
            or value_hash({name: item for name, item in inputs.items() if name != "inputKey"}) != inputs["inputKey"]):
        raise BuildError("Preparation identity refresh requires the qualified current immutable input manifest.")
    if not path.exists():
        if recovering: return False
        plan = _plan(preparation, inputs)
        write_json(path, plan)  # Durable before the first project mutation.
    else: plan = _read(path)
    value = preparation.value
    if (not isinstance(inputs, dict) or inputs.get("inputKey") != preparation.identity["inputKey"]
            or plan.get("schema") != 1 or plan.get("owner") != OWNER
            or plan.get("project") != value["project"] or plan.get("toInputKey") != inputs["inputKey"]
            or value["inputKey"] not in (plan.get("fromInputKey"), plan.get("toInputKey"))
            or not isinstance(plan.get("files"), list)
            or not all(isinstance(row, dict) for row in plan["files"])
            or [row.get("path") for row in plan["files"]] != list(PATHS)):
        raise BuildError("Preparation identity refresh transaction belongs to another input/project.")
    old = value["inputKey"] == plan["fromInputKey"]
    expected_journal = plan.get("beforeJournalSha256") if old else plan.get("afterJournalSha256")
    if value_hash(value) != expected_journal:
        raise BuildError("Preparation identity refresh journal changed; existing assets and transaction were retained.")
    latest = _latest(value)
    previous = _previous(preparation, plan["fromInputKey"])
    # Validate the whole bounded write set before publishing any new bytes.
    for row in plan["files"]:
        relative = row["path"]
        expected_owner = MANIFESTS[relative][0] if relative in MANIFESTS else "final-settings"
        if (type(row.get("step")) is not int or not 0 <= row["step"] < len(value["steps"])
                or value["steps"][row["step"]]["name"] != expected_owner
                or not isinstance(row.get("original"), dict) or not isinstance(row.get("replacement"), dict)
                or latest.get(relative) != (row["step"], row.get("before") if old else row.get("after"))
                or row.get("before") != _record(relative, row["original"])
                or row.get("after") != _record(relative, row["replacement"])
                or row["replacement"] != _replacement(relative, row["original"], previous, inputs, latest)):
            raise BuildError("Preparation identity refresh output contract changed: " + relative)
        member = _ordinary_owned(preparation.project / relative)
        if not member.is_file(): raise BuildError("Preparation identity refresh document is missing: " + relative)
        actual = {"path": relative, "size": member.stat().st_size, "sha256": digest(member)}
        if actual not in (row["before"], row["after"]):
            raise BuildError("Preparation identity refresh document changed outside its transaction: " + relative)
    counter = preparation.progress.Counter("prepare-metadata-refresh", len(PATHS), "files",
            "Refreshing current mod identity in retained preparation") if preparation.progress else None
    # Persist the transferred warm proofs before the journal can acquire its
    # new identity. A kill just after journal publication then requalifies only
    # these four changed documents, rather than the whole original project.
    preparation.copies.commit()
    for row in plan["files"]:
        member = preparation.project / row["path"]
        if not old or _record(row["path"], _read(member)) == row["after"]:
            if counter: counter.add(1, row["path"])
            continue
        preparation.witnesses.invalidate(member)
        preparation.checked.pop(row["path"], None)
        write_json(member, row["replacement"])
        preparation.witnesses.remember(member, row["after"]["sha256"])
        if counter: counter.add(1, row["path"])
    updated = _replacement_journal(value, plan, preparation.source_stamps)
    if value_hash(updated) != plan["afterJournalSha256"]:
        raise BuildError("Preparation identity refresh source stamps changed; transaction retained for retry.")
    write_json(preparation.journal, updated)
    preparation.value = updated
    preparation.copies.commit()
    path.unlink()
    if counter: counter.finish()
    print("preparation resume: refreshed four current-input JSON documents; all completed asset producers and Unity Library retained", flush=True)
    return True
