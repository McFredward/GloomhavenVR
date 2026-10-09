"""Recognize only the original-to-package script-pointer changes made by the Editor.

This reader does not write assets or trust an after-hash as proof. It reverses the
exact canonical m_Script mapping and requires the retained owner's original full
file hash. Receipt/mapping/package inspection is lazy and shared for one resume.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
import re

from storage import BuildError, _ordinary_owned

MANIFESTS = ("Assets/QuestOriginalCampaign/script-bindings.json", "Assets/QuestOriginalStartup/script-bindings.json")
RECEIPT = "QuestStartupEvidence/script-remap.json"
JSON_LIMIT = 16 * 1024 * 1024
ASSET_LIMIT = 64 * 1024 * 1024
GUID = re.compile(r"^[0-9a-f]{32}$")
HASH = re.compile(r"^[0-9a-f]{64}$")
POINTER = re.compile(rb"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}")
PACKAGES = {"UnityEngine.UI": "com.unity.ugui", "Unity.InputSystem": "com.unity.inputsystem"}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def relative(value, prefix="Assets/"):
    if (not isinstance(value, str) or not value.startswith(prefix) or "\\" in value or ":" in value
            or PurePosixPath(value).is_absolute() or any(part in ("", ".", "..") for part in value.split("/"))):
        raise BuildError("Script remap evidence contains an unconfined asset path.")
    return value


def read(path, limit):
    path = _ordinary_owned(path)
    if not path.is_file() or path.stat().st_size > limit:
        raise BuildError("Script remap evidence is missing or too large: " + path.name)
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        raise BuildError("Script remap evidence grew beyond its bound.")
    return raw


def document(path):
    try:
        value = json.loads(read(path, JSON_LIMIT))
    except (ValueError, UnicodeError) as error:
        raise BuildError("Script remap evidence is not valid JSON.") from error
    if not isinstance(value, dict):
        raise BuildError("Script remap evidence is not an object.")
    return value


class ScriptRemap:
    """Accept a changed row only after independently recovering its exact old bytes.

    latest is the retained journal's mapping path -> (owner index, output row).
    Call accept only on an otherwise changed retained serialized asset. The
    caller binds this closed read to its existing strong before/after file stamp
    before publishing returned rows; this helper never updates a journal.
    """
    def __init__(self, project, latest):
        self.project = _ordinary_owned(Path(project))
        self.latest = latest
        self.loaded = False
        self.paths = set()
        self.inverse = {}

    def _frozen(self, name):
        previous = self.latest.get(name)
        row = previous[1] if isinstance(previous, tuple) else previous
        if not isinstance(row, dict) or not HASH.fullmatch(row.get("sha256", "")):
            raise BuildError("Script remap requires its unchanged retained input: " + name)
        raw = read(self.project / name, JSON_LIMIT)
        if len(raw) != row.get("size") or sha(raw) != row["sha256"]:
            raise BuildError("Script remap input changed: " + name)
        try:
            result = json.loads(raw)
        except (ValueError, UnicodeError) as error:
            raise BuildError("Script remap retained input is invalid JSON.") from error
        if not isinstance(result, dict):
            raise BuildError("Script remap retained input is not an object.")
        return result

    def _package_identities(self, mapping):
        dependencies = self._frozen("Packages/manifest.json").get("dependencies")
        if not isinstance(dependencies, dict):
            raise BuildError("Script remap package dependencies are invalid.")
        required = {}
        for value in mapping:
            package = PACKAGES.get(value["assemblyName"])
            if package is None or package not in dependencies or value["newFileId"] != 11500000:
                raise BuildError("Script remap target is outside the imported package-script contract.")
            required.setdefault(package, {})[value["newGuid"]] = value
        for package, targets in required.items():
            root = _ordinary_owned(self.project / "Packages" / package)
            if not root.is_dir():
                version = dependencies[package]
                if not isinstance(version, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
                    raise BuildError("Script remap registry package has no exact version.")
                root = _ordinary_owned(self.project / "Library/PackageCache" / (package + "@" + version))
            if not root.is_dir():
                raise BuildError("Script remap imported package source is missing: " + package)
            found = set()
            # Scan the two actual runtime source trees once (UGUI: Runtime,
            # InputSystem: InputSystem), never the game assets or artifacts.
            runtime = root / ("InputSystem" if package == "com.unity.inputsystem" else "Runtime")
            for metadata in runtime.rglob("*.cs.meta"):
                raw = read(metadata, 65536)
                guids = re.findall(rb"(?m)^guid: ([0-9a-f]{32})\r?$", raw)
                if len(guids) != 1 or guids[0].decode() not in targets:
                    continue
                guid = guids[0].decode()
                value = targets[guid]
                source = _ordinary_owned(Path(str(metadata)[:-5]))
                expected_name = value["fullName"].rsplit(".", 1)[-1]
                if guid in found or source.name != expected_name + ".cs" or not source.is_file():
                    raise BuildError("Script remap imported SDK script identity is ambiguous.")
                if value.get("newAssetPath"):
                    expected_path = "Packages/" + package + "/" + source.relative_to(root).as_posix()
                    if value["newAssetPath"] != expected_path:
                        raise BuildError("Script remap imported SDK script path changed.")
                found.add(guid)
            if found != set(targets):
                raise BuildError("Script remap target GUID is not a current imported SDK script.")

    def _load(self):
        available = [name for name in MANIFESTS if name in self.latest]
        if not available:
            raise BuildError("Script remap has no retained binding manifest.")
        # A Campaign project can retain its Startup provenance too. Campaign is
        # the actual Editor's selected closure, as in GHVR_QUEST_GAME.
        value = self._frozen(available[0])
        receipt = document(self.project / RECEIPT)
        if (type(value.get("schema")) is not int or value["schema"] != 1
                or type(receipt.get("schema")) is not int or receipt["schema"] != 1
                or receipt.get("callbacksAndOtherSerializedBytesPreserved") is not True):
            raise BuildError("Script remap evidence does not describe the original pointer-only adapter.")
        paths, bindings, disabled = value.get("assetPaths"), value.get("bindings"), value.get("disabledPluginGuids")
        mapping = receipt.get("replacements")
        if not all(isinstance(rows, list) for rows in (paths, bindings, disabled, mapping)):
            raise BuildError("Script remap input arrays are invalid.")
        paths = [relative(path) for path in paths]
        if (len(set(paths)) != len(paths) or not all(isinstance(g, str) and GUID.fullmatch(g) for g in disabled)
                or len(set(disabled)) != len(disabled)):
            raise BuildError("Script remap input contains ambiguous paths or plugin identities.")
        source = {}
        for row in bindings:
            if (not isinstance(row, dict) or not isinstance(row.get("oldGuid"), str) or not GUID.fullmatch(row["oldGuid"])
                    or row["oldGuid"] not in disabled or type(row.get("oldFileId")) is not int or row["oldFileId"] == 0
                    or not isinstance(row.get("assemblyName"), str) or not isinstance(row.get("fullName"), str)):
                raise BuildError("Script remap original binding is invalid.")
            key = (row["oldGuid"], row["oldFileId"])
            if key in source:
                raise BuildError("Script remap original pointer mapping is ambiguous.")
            source[key] = row
        inverse, seen = {}, set()
        for row in mapping:
            if (not isinstance(row, dict) or not isinstance(row.get("oldGuid"), str)
                    or type(row.get("oldFileId")) is not int):
                raise BuildError("Script remap replacement is invalid.")
            key = (row.get("oldGuid"), row.get("oldFileId"))
            original = source.get(key)
            if (original is None or key in seen or any(row.get(k) != original[k] for k in ("assemblyName", "fullName"))
                    or type(row.get("newFileId")) is not int or row["newFileId"] == 0
                    or not isinstance(row.get("newGuid"), str) or not GUID.fullmatch(row["newGuid"])
                    or row["newGuid"] in disabled or row["newGuid"] == "0" * 32):
                raise BuildError("Script remap replacement differs from its original binding.")
            target = (row["newGuid"].encode(), row["newFileId"])
            if target in inverse:
                raise BuildError("Script remap inverse pointer mapping is ambiguous.")
            inverse[target] = (row["oldGuid"].encode(), row["oldFileId"])
            seen.add(key)
        if seen != set(source) or not seen:
            raise BuildError("Script remap replacement census differs from its original bindings.")
        self._package_identities(mapping)
        self.paths, self.inverse, self.loaded = set(paths), inverse, True

    def accept(self, name, previous):
        if (not isinstance(name, str) or not name.startswith("Assets/") or Path(name).suffix not in (".asset", ".prefab", ".unity", ".anim")):
            return None
        if not self.loaded:
            self._load()
        if name not in self.paths or not isinstance(previous, dict) or not HASH.fullmatch(previous.get("sha256", "")):
            return None
        raw = read(self.project / relative(name), ASSET_LIMIT)
        replacements = 0
        def reverse(match):
            nonlocal replacements
            file_id = int(match[1]); guid = match[2]
            source = self.inverse.get((guid, file_id))
            if source is None:
                return match[0]
            canonical = b"m_Script: {fileID: " + str(file_id).encode() + b", guid: " + guid + b", type: 3}"
            if match[0] != canonical:
                return match[0]
            replacements += 1
            return b"m_Script: {fileID: " + str(source[1]).encode() + b", guid: " + source[0] + b", type: 3}"
        original = POINTER.sub(reverse, raw)
        if not replacements or len(original) != previous.get("size") or sha(original) != previous["sha256"]:
            return None
        return {"path": name, "size": len(raw), "sha256": sha(raw)}
