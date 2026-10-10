"""Reconstruct one damaged generated preparation output from retained sources.

Opening the generated project in Unity can serialize LightingData from its
recovered YAML into a binary asset. That is unrelated to SDK script remapping.
The completed recovery tree remains an immutable source for restoring this one
file; regenerating the project or deleting Library would repeat hours of work.

Every candidate must reproduce the complete recorded output hash. This provider
writes only the caller's isolated staging file. The preparation owner publishes
the replacement and its durable transaction; no converter or Unity job runs here.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

from storage import BuildError, _ordinary_owned, canonical, write_json
import import_workspace
import script_remap_resume
import shaders

CASE_RECEIPT = "QuestStartupEvidence/case-path-migration.json"
JSON_LIMIT = 16 * 1024 * 1024
TRANSFORM_LIMIT = 64 * 1024 * 1024
CHUNK = 1024 * 1024
CACHE_LIMIT = 64 * 1024 * 1024
CONTROL_LIMIT = 1024 * 1024
HASH = re.compile(r"[0-9a-f]{64}\Z")


def _relative(value):
    if (not isinstance(value, str) or "\\" in value or ":" in value
            or PurePosixPath(value).is_absolute()
            or any(part in ("", ".", "..") for part in value.split("/"))):
        raise BuildError("Preparation repair contains an unconfined path.")
    return value


def _stamp(path):
    info = path.stat()
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns


def _helper(name):
    # The isolated Wizard loader removes temporary import aliases after loading
    # the Builder. Capture these already-registered local dependency objects now;
    # later repair callbacks must not re-import an absent/global storage module.
    return {"import_workspace": import_workspace, "script_remap_resume": script_remap_resume,
            "shaders": shaders}[name]


class Provider:
    """Lazy file-level recipes, restricted by the journal's complete SHA proof.

    Roots are the already selected immutable snapshots, never the live PC install.
    bind receives existing latest-owner rows without opening or hashing a tree.
    Missing reconstruction sources return None so the owner can retain all other
    completed work and report the particular unsupported file, rather than reset.
    """
    def __init__(self, project, recovered, source, output, inputs, target="game", *, original_files=None):
        self.project = _ordinary_owned(Path(project))
        self.recovered = _ordinary_owned(Path(recovered)) if recovered is not None else None
        self.source = _ordinary_owned(Path(source))
        self.output = _ordinary_owned(Path(output))
        self.inputs = inputs
        self.target = target
        self.latest = {}
        self.case_mappings = None
        self.remap = None
        self.control_cache = self.output / "cache/preparation-repair" / self.project.name
        self.cache_index = None
        self.previous_source = None
        self.previous_input = None
        self.prior_input_key = None
        originals = original_files
        if originals is None:
            originals = (inputs.get("campaignProject") or inputs.get("startupProject") or {}).get("files", [])
        self.original_files = originals if isinstance(originals, dict) else {
            row["path"]: row for row in originals if isinstance(row, dict) and isinstance(row.get("path"), str)}

    def bind(self, latest):
        self.latest = latest

    def bind_journal(self, value):
        key = value.get("inputKey") if isinstance(value, dict) else None
        self.prior_input_key = key if isinstance(key, str) and HASH.fullmatch(key) else None

    @staticmethod
    def _control(relative, row):
        # Retain only inexpensive, generated control documents. Native asset
        # payloads and their tens of thousands of metadata files are never copied.
        return (row.get("size", CONTROL_LIMIT + 1) <= CONTROL_LIMIT and (
            relative in ("QuestBuilderSettings.json", "Packages/manifest.json",
                         "ProjectSettings/ProjectSettings.asset", "Assets/csc.rsp")
            or relative.endswith(".json") and relative.startswith((
                "QuestStartupEvidence/", "QuestCampaignEvidence/", "QuestRecovery/",
                "Assets/Quest/Resources/", "Assets/StreamingAssets/Quest/",
                "Assets/QuestOriginalStartup/", "Assets/QuestOriginalCampaign/"))))

    def _cache_entries(self):
        if self.cache_index is not None:
            return self.cache_index
        path = _ordinary_owned(self.control_cache / "index.json")
        if not path.exists():
            if self.control_cache.is_dir():
                return self._recover_cache_index()
            self.cache_index = {"schema": 1, "owner": "Quest bounded preparation controls",
                                "project": self.project.name, "bytes": 0, "entries": {}}
            return self.cache_index
        if not path.is_file() or path.stat().st_size > CONTROL_LIMIT:
            return self._recover_cache_index()
        try:
            value = json.loads(path.read_bytes())
        except (OSError, ValueError, UnicodeError):
            return self._recover_cache_index()
        entries = value.get("entries") if isinstance(value, dict) else None
        if (not isinstance(value, dict) or value.get("schema") != 1 or value.get("owner") != "Quest bounded preparation controls"
                or value.get("project") != self.project.name or not isinstance(entries, dict)
                or type(value.get("bytes")) is not int or not 0 <= value["bytes"] <= CACHE_LIMIT
                or any(not isinstance(key, str) or not HASH.fullmatch(key) or type(size) is not int or not 0 <= size <= CONTROL_LIMIT
                       for key, size in entries.items()) or sum(entries.values()) != value["bytes"]):
            return self._recover_cache_index()
        self.cache_index = value
        return value

    def _recover_cache_index(self):
        """Recover capacity accounting from bounded cache names and sizes only.

        Cache contents are still untrusted until the requested file's complete
        journal hash qualifies them. A damaged index must not discard good backups
        or disable saving further control receipts; no native tree is inspected.
        """
        root = _ordinary_owned(self.control_cache)
        if not root.is_dir():
            return None
        entries, size, seen = {}, 0, 0
        with os.scandir(root) as members:
            for member in members:
                seen += 1
                if seen > 8192:
                    return None
                name = member.name
                if not name.endswith(".bin") or not HASH.fullmatch(name[:-4]):
                    continue
                info = member.stat(follow_symlinks=False)
                if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1
                        or not 0 <= info.st_size <= CONTROL_LIMIT):
                    return None
                entries[name[:-4]] = info.st_size
                size += info.st_size
                if size > CACHE_LIMIT:
                    return None
        self.cache_index = {"schema": 1, "owner": "Quest bounded preparation controls",
                            "project": self.project.name, "bytes": size, "entries": entries}
        return self.cache_index

    def remember(self, relative, row):
        """Optionally save a hash-qualified small control after its owner closes.

        This is not a build requirement. Existing matching cache members need no
        payload read. A bounded cache prevents a growing update history consuming
        the disk; the immutable original remains the primary repair source.
        """
        relative = _relative(relative)
        if (not isinstance(row, dict) or not HASH.fullmatch(row.get("sha256", ""))
                or type(row.get("size")) is not int or row["size"] < 0 or not self._control(relative, row)):
            return False
        try:
            index = self._cache_entries()
            if index is None:
                return False
            cached = _ordinary_owned(self.control_cache / (row["sha256"] + ".bin"))
            if row["sha256"] in index["entries"]:
                return cached.is_file() and cached.stat().st_size == row["size"]
            if index["bytes"] + row["size"] > CACHE_LIMIT:
                return False
            self.control_cache.mkdir(parents=True, exist_ok=True)
            temporary = _ordinary_owned(cached.with_suffix(".part"))
            if temporary.exists() and (not stat.S_ISREG(temporary.lstat().st_mode) or temporary.stat().st_nlink != 1):
                return False
            if not self._copy(self.project / relative, temporary, row, "retained-generated-control"):
                return False
            temporary.replace(cached)
            index["entries"][row["sha256"]] = row["size"]
            index["bytes"] += row["size"]
            write_json(self.control_cache / "index.json", index)
            return True
        except (OSError, BuildError):
            # Cache seeding never turns otherwise accepted work into a failure.
            return False

    def _frozen(self, relative):
        owned = self.latest.get(relative)
        row = owned[1] if isinstance(owned, tuple) else owned
        if not isinstance(row, dict) or not HASH.fullmatch(row.get("sha256", "")):
            return None
        path = self.project / _relative(relative)
        return self._recorded_json(path, row)

    @staticmethod
    def _recorded_json(path, row):
        path = _ordinary_owned(path)
        if not path.is_file() or path.stat().st_size != row.get("size") or row["size"] > JSON_LIMIT:
            return None
        before = _stamp(path)
        raw = path.read_bytes()
        if before != _stamp(path) or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            return None
        try:
            value = json.loads(raw)
        except (ValueError, UnicodeError):
            return None
        return value if isinstance(value, dict) else None

    def _old_input(self):
        relative = "Assets/StreamingAssets/Quest/input-manifest.json"
        previous = self._frozen(relative)
        if previous is not None:
            return previous
        owned = self.latest.get(relative)
        row = owned[1] if isinstance(owned, tuple) else owned
        if not isinstance(row, dict) or not HASH.fullmatch(row.get("sha256", "")):
            return None
        candidates = [self.control_cache / (row["sha256"] + ".bin")]
        if self.prior_input_key:
            candidates.append(self.output / "manifests" / (self.prior_input_key + ".json"))
        for path in candidates:
            previous = self._recorded_json(path, row)
            if previous is not None:
                return previous
        return None

    def _original_paths(self, relative):
        yield relative
        if self.case_mappings is None:
            receipt = self._frozen(CASE_RECEIPT)
            mappings = receipt.get("pathMappings", {}) if receipt else {}
            if (not isinstance(mappings, dict) or not receipt
                    or receipt.get("assetContentChanged") is not False
                    or receipt.get("serializedReferencesChanged") is not False):
                mappings = {}
            checked = []
            for old, new in mappings.items():
                old, new = _relative(old), _relative(new)
                if not old.startswith("Assets/") or not new.startswith("Assets/"):
                    raise BuildError("Retained case repair mapping leaves original Assets.")
                checked.append((old, new))
            self.case_mappings = sorted(checked, key=lambda row: len(row[1]), reverse=True)
        for old, new in self.case_mappings:
            if relative == new or relative.startswith(new + "/"):
                yield old + relative[len(new):]

    def _sources(self, relative):
        if self.previous_source is None:
            previous = self._old_input()
            self.previous_input = previous
            key = previous.get("mod", {}).get("key") if previous and isinstance(previous.get("mod"), dict) else None
            self.previous_source = (self.output / "inputs/mod" / key
                                    if isinstance(key, str) and HASH.fullmatch(key) else False)
        selected_sources = [self.source] + ([self.previous_source] if self.previous_source else [])
        if relative.startswith(("Assets/", "ProjectSettings/", "Packages/")):
            for source in selected_sources:
                yield source / "unity/GloomhavenVR.Quest" / relative, "selected-template"
        if self.recovered is not None:
            for old in self._original_paths(relative):
                yield self.recovered / old, "retained-original-recovery"
        # These are existing per-file converter outputs, not new workspaces.
        # Address exactly this file in the selected game/input caches; do not
        # enumerate converter variants or hash unrelated payloads.
        game = self.inputs.get("game", {}).get("key")
        if isinstance(game, str) and HASH.fullmatch(game):
            yield self.output / "tool-cache/cs" / game / "o" / relative, "retained-shader-overlay"
            yield self.output / "tool-cache/campaign-shaders" / game / "o" / relative, "retained-shader-overlay"
        keys = [self.inputs.get("inputKey"), self.previous_input.get("inputKey") if self.previous_input else None]
        for key in dict.fromkeys(key for key in keys if isinstance(key, str) and HASH.fullmatch(key)):
            yield self.output / "tool-cache/campaign-compute" / key / "overlay" / relative, "retained-compute-overlay"
        if relative == "Assets/Quest/Runtime/QuestText.cs":
            for source in selected_sources:
                yield source / "src/GloomhavenVR/Core/Loc/QuestText.cs", "selected-localization"
        if relative == "Assets/Quest/Resources/quest-loading-logo.png":
            for source in selected_sources:
                yield source / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png", "selected-loading-logo"
        if relative in ("Assets/Quest/Resources/quest-profile.json", "Assets/Quest/Resources/quest-steam-logo.png"):
            keys = [self.inputs.get("profileKey"), self.previous_input.get("profileKey") if self.previous_input else None]
            for key in dict.fromkeys(key for key in keys if isinstance(key, str) and HASH.fullmatch(key)):
                yield self.output / "identities" / key / Path(relative).name, "selected-local-identity"
        if relative == "Assets/StreamingAssets/Quest/input-manifest.json" and self.prior_input_key:
            yield self.output / "manifests" / (self.prior_input_key + ".json"), "retained-input-manifest"
        if relative.startswith("Assets/Quest/Recovered/"):
            probe = self.inputs.get("probeAssets", {}).get("key")
            if isinstance(probe, str) and HASH.fullmatch(probe):
                yield self.output / "inputs/probe" / probe / relative[len("Assets/Quest/Recovered/"):], "selected-probe"

    @staticmethod
    def _result(row, source, recipe):
        return {"size": row["size"], "sha256": row["sha256"], "source": str(source),
                "recipe": recipe, "detail": "Restored only the recorded preparation output"}

    def _copy(self, source, stage, row, recipe):
        source = _ordinary_owned(source)
        if not source.is_file() or source.stat().st_size != row["size"]:
            return None
        before = _stamp(source)
        hashed, count = hashlib.sha256(), 0
        with source.open("rb") as original, stage.open("wb") as destination:
            for chunk in iter(lambda: original.read(CHUNK), b""):
                hashed.update(chunk)
                count += len(chunk)
                destination.write(chunk)
        if before != _stamp(source):
            stage.unlink(missing_ok=True)
            raise BuildError("A retained repair source changed while read: " + source.name)
        if count != row["size"] or hashed.hexdigest() != row["sha256"]:
            stage.unlink(missing_ok=True)
            return None
        return self._result(row, source, recipe)

    def _bytes(self, raw, stage, row, source, recipe):
        if len(raw) != row["size"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            return None
        stage.write_bytes(raw)
        return self._result(row, source, recipe)

    def _small_recipes(self, relative, stage, row):
        if relative == "Assets/csc.rsp":
            defines = "GHVR_QUEST_STARTUP;GHVR_QUEST_GAME" if self.target == "game" else "GHVR_QUEST_STARTUP"
            return self._bytes(("-define:" + defines + "\n").encode(), stage, row, "selected-target", "compiler-defines")
        if relative == "Assets/StreamingAssets/Quest/input-manifest.json":
            return self._bytes(canonical(self.inputs) + b"\n", stage, row, "selected-inputs", "input-manifest")
        if relative == "Assets/Quest/Resources/quest-dlc-ownership.json":
            ownership = self.inputs.get("profile", {}).get("dlcOwnership")
            if isinstance(ownership, dict):
                return self._bytes(canonical(ownership) + b"\n", stage, row, "selected-entitlements", "dlc-ownership")
        if relative == "QuestBuilderSettings.json":
            for inputs in (self.inputs, self.previous_input):
                if not isinstance(inputs, dict) or not isinstance(inputs.get("profile"), dict) or not isinstance(inputs.get("mod"), dict):
                    continue
                settings = {"schema": 1, "target": self.target, "inputKey": inputs.get("inputKey"),
                            "profileSha256": hashlib.sha256(canonical(inputs["profile"]) + b"\n").hexdigest(),
                            "package": "dev.gloomhavenvr.quest", "modBuild": inputs["mod"].get("modBuild")}
                result = self._bytes(canonical(settings) + b"\n", stage, row, "retained-input-manifest", "builder-settings")
                if result:
                    return result
        if relative == "ProjectSettings/ProjectSettings.asset":
            module = _helper("import_workspace")
            for source, _ in self._sources(relative):
                source = _ordinary_owned(source)
                if source.is_file() and source.stat().st_size <= JSON_LIMIT:
                    before = _stamp(source)
                    raw = source.read_bytes()
                    if before != _stamp(source):
                        raise BuildError("A retained PlayerSettings repair source changed while read.")
                    try:
                        changed = module.patch_settings(raw, self.target)
                    except BuildError:
                        continue
                    result = self._bytes(changed, stage, row, source, "audited-android-bootstrap-settings")
                    if result:
                        return result
        if relative.startswith("Assets/Shader/Hidden_") and relative.endswith(".shader"):
            module = _helper("shaders")
            name = Path(relative).stem[len("Hidden_"):]
            spec = module.SHADERS.get(name)
            if spec:
                source = _ordinary_owned(self.output / "tool-cache/legacy-post-effects" / module.CACHE_NAME / (name + ".shader"))
                if source.is_file() and source.stat().st_size == spec["sourceBytes"]:
                    raw = source.read_bytes()
                    if hashlib.sha256(raw).hexdigest() == spec["sourceSha256"]:
                        result = self._bytes(raw, stage, row, source, "pinned-legacy-shader")
                        if result:
                            return result
                        upgraded = (b"// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'\n\n"
                                    + re.sub(rb"mul\(UNITY_MATRIX_MVP,\s*([^()]+)\)", rb"UnityObjectToClipPos(\1)", raw))
                        if hashlib.sha256(upgraded).hexdigest() == spec["importUpgradeSha256"]:
                            return self._bytes(upgraded, stage, row, source, "pinned-unity-legacy-shader-upgrade")
        return None

    def _script_variant(self, relative, stage, row, candidates):
        if (not relative.startswith("Assets/") or Path(relative).suffix not in
                (".asset", ".prefab", ".unity", ".anim", ".controller", ".overrideController", ".playable")):
            return None
        possible = [(path, recipe) for path, recipe in candidates if path.is_file()
                    and path.stat().st_size <= TRANSFORM_LIMIT]
        if not possible or not self.latest:
            return None
        module = _helper("script_remap_resume")
        if self.remap is None:
            self.remap = module.ScriptRemap(self.project, self.latest)
            try:
                self.remap._load()
            except BuildError:
                self.remap = False
        if self.remap is False or relative not in self.remap.paths:
            return None
        forward = {old: new for new, old in self.remap.inverse.items()}
        def replace(match):
            old = (match[2], int(match[1]))
            new = forward.get(old)
            canonical_pointer = (b"m_Script: {fileID: " + match[1] + b", guid: " + match[2] + b", type: 3}")
            if new is None or match[0] != canonical_pointer:
                return match[0]
            return b"m_Script: {fileID: " + str(new[1]).encode() + b", guid: " + new[0] + b", type: 3}"
        for source, _ in possible:
            before = _stamp(source)
            raw = source.read_bytes()
            if before != _stamp(source):
                raise BuildError("A retained serialized repair source changed while read.")
            result = self._bytes(module.POINTER.sub(replace, raw), stage, row, source, "frozen-package-script-remap")
            if result:
                return result
        return None

    def _archive_member(self, relative, stage, row):
        if not relative.startswith("Assets/"):
            return None
        # Address the one requested member. The full archive is never hashed or
        # extracted: the recorded output's complete hash qualifies these bytes.
        archives = (self.project / "Assets/StreamingAssets/quest-startup-content.zip",
                    self.project / "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip",
                    self.project / "Assets/StreamingAssets/quest-mod-content.zip")
        member = relative[len("Assets/"):]
        for archive in archives:
            archive = _ordinary_owned(archive)
            if not archive.is_file():
                continue
            before = _stamp(archive)
            try:
                with zipfile.ZipFile(archive) as package:
                    try:
                        entry = package.getinfo(member)
                    except KeyError:
                        continue
                    if entry.file_size != row["size"] or entry.flag_bits & 1:
                        continue
                    hashed, count = hashlib.sha256(), 0
                    with package.open(entry) as original, stage.open("wb") as destination:
                        for chunk in iter(lambda: original.read(CHUNK), b""):
                            hashed.update(chunk); count += len(chunk); destination.write(chunk)
                    if before != _stamp(archive):
                        raise BuildError("A retained content archive changed during targeted repair.")
                    if count == row["size"] and hashed.hexdigest() == row["sha256"]:
                        return self._result(row, archive, "retained-single-archive-member")
                    stage.unlink(missing_ok=True)
            except (OSError, zipfile.BadZipFile, RuntimeError):
                stage.unlink(missing_ok=True)
        return None

    def _companions(self, relative, stage, result):
        if (self.recovered is None or not relative.startswith("Assets/") or relative.endswith(".meta")):
            return result
        originals = list(self._original_paths(relative))
        retained = [(old, self.original_files.get(old + ".meta")) for old in originals]
        retained = [(old, row) for old, row in retained if isinstance(row, dict)]
        if not retained:
            # A missing original sidecar must not be replaced by Unity assigning
            # a new GUID. Without its immutable producer record there is no
            # qualified companion reconstruction to publish.
            if any((self.recovered / (old + ".meta")).is_file() for old in originals):
                return None
            return result
        old, row = retained[0]
        if (not HASH.fullmatch(row.get("sha256", "")) or type(row.get("size")) is not int
                or not 0 <= row["size"] <= 65536):
            return None
        source = _ordinary_owned(self.recovered / (old + ".meta"))
        if not source.is_file() or source.stat().st_size != row["size"]:
            return None
        before = _stamp(source)
        raw = source.read_bytes()
        if before != _stamp(source) or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            return None
        guids = re.findall(rb"(?m)^guid:\s*([0-9a-f]{32})\s*$", raw)
        if len(guids) != 1:
            return None
        relative_meta = relative + ".meta"
        live = _ordinary_owned(self.project / relative_meta)
        if live.exists():
            if not live.is_file() or live.stat().st_size > 65536:
                return None
            live_before = _stamp(live)
            live_raw = live.read_bytes()
            if (live_before != _stamp(live)
                    or re.findall(rb"(?m)^guid:\s*([0-9a-f]{32})\s*$", live_raw) != guids):
                return None
            # Unity's existing importer metadata may contain legitimate later
            # fields. Its retained original GUID is enough here; leave it alone.
            return result
        metadata_stage = _ordinary_owned(live.with_name(
            ".quest-repair-" + hashlib.sha256(relative_meta.encode()).hexdigest()[:20] + ".part"))
        if metadata_stage.exists() and (not stat.S_ISREG(metadata_stage.lstat().st_mode)
                                       or metadata_stage.stat().st_nlink != 1):
            raise BuildError("Preparation metadata staging must be an ordinary isolated file.")
        metadata_stage.parent.mkdir(parents=True, exist_ok=True)
        metadata_stage.write_bytes(raw)
        result["companions"] = [{"path": relative_meta, "size": len(raw), "sha256": row["sha256"],
                                 "stagingPath": str(metadata_stage), "source": str(source),
                                 "recipe": "retained-original-guid-metadata"}]
        return result

    def __call__(self, relative, expected_row, owner, stage):
        relative = _relative(relative)
        if (not isinstance(expected_row, dict) or not HASH.fullmatch(expected_row.get("sha256", ""))
                or type(expected_row.get("size")) is not int or expected_row["size"] < 0):
            return None
        stage = _ordinary_owned(Path(stage))
        target = self.project / relative
        project_stage = target.with_name(".quest-repair-" + hashlib.sha256(relative.encode()).hexdigest()[:20] + ".part")
        if self.project in stage.parents and stage != project_stage:
            raise BuildError("Preparation repair staging cannot modify another live project path.")
        roots = [self.source, self.output / "inputs"] + ([self.recovered] if self.recovered else [])
        if any(stage == root or root in stage.parents for root in roots):
            raise BuildError("Preparation repair staging cannot modify a selected source or live project.")
        if stage.exists() and (not stat.S_ISREG(stage.lstat().st_mode) or stage.stat().st_nlink != 1):
            raise BuildError("Preparation repair staging must be an ordinary isolated file.")
        stage.parent.mkdir(parents=True, exist_ok=True)
        if self._control(relative, expected_row):
            result = self._copy(self.control_cache / (expected_row["sha256"] + ".bin"),
                                stage, expected_row, "retained-generated-control")
            if result:
                return self._companions(relative, stage, result)
        candidates = list(dict.fromkeys(self._sources(relative)))
        for path, recipe in candidates:
            result = self._copy(path, stage, expected_row, recipe)
            if result:
                return self._companions(relative, stage, result)
        result = self._small_recipes(relative, stage, expected_row)
        if result:
            return self._companions(relative, stage, result)
        result = self._script_variant(relative, stage, expected_row, candidates)
        if result:
            return self._companions(relative, stage, result)
        result = self._archive_member(relative, stage, expected_row)
        return self._companions(relative, stage, result) if result else None
