"""Durable, ordered preparation checkpoints for an owned mutable Unity project.

Receipts name actual outputs, not the entire imported project. Later phases can
replace an earlier output contract; only its latest owner is checked on resume.
An unfinished destructive phase restores its bounded undo set before retrying.
No Library, original game file, or source checkout belongs to that undo set.
"""
from __future__ import annotations

import hashlib
from contextlib import contextmanager
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import sqlite3

from storage import BuildError, CONTENT_PATHS, ValidatedFileWitnesses, _ordinary_owned, digest, invocation_file_matches, value_hash, write_json
import preparation_identity
import preparation_metadata
import import_workspace
import shaders as post_effects
import script_remap_resume

SCHEMA = 1
OWNER = "Quest preparation substage journal"


class Contracts(list):
    def __init__(self, paths, *, absent=()):
        super().__init__(paths)
        self.absent = set(absent)


def _relative(name):
    if not isinstance(name, str) or not name or "\\" in name or ":" in name:
        raise BuildError("Preparation contract has an invalid relative path.")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in name.split("/")):
        raise BuildError("Preparation contract escapes its generated project.")
    if path.parts[0] in ("Library", "Temp", "Logs", ".git"):
        raise BuildError("Preparation must not capture Unity caches or repository files.")
    return name


def _stamp(path):
    stat = _ordinary_owned(path).stat()
    return (stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns)


def copy_changed(source, target, *, observed=None, transfer=None):
    """Resume an immutable copy by bytes, and reject mutations/short writes.

    Completed same-size/time copies are compared before retention. Source and
    target stamps here use the same stat API, including on Windows CPython.
    Writes use a sibling temporary, so a killed copy never becomes a whole file.
    """
    source, target = _ordinary_owned(Path(source)), _ordinary_owned(Path(target))
    before = _stamp(source)
    if not source.is_file(): raise BuildError("Preparation source is not an ordinary file: " + source.name)
    if target.exists() and target.is_file() and target.stat().st_size == before[2]:
        retained = _stamp(target)
        with source.open("rb") as left, target.open("rb") as right:
            same = True
            checksum = hashlib.sha256()
            while True:
                a, b = left.read(1024 * 1024), right.read(1024 * 1024)
                if a != b: same = False; break
                checksum.update(a)
                if not a: break
        if before != _stamp(source): raise BuildError("Preparation source changed during copy: " + source.name)
        if retained != _stamp(target):
            raise BuildError("Preparation target changed while qualifying its retained bytes: " + target.name)
        if same:
            if observed: observed(checksum.hexdigest())
            return str(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = _ordinary_owned(target.with_name(target.name + ".quest-prepare-copy"))
    try:
        with source.open("rb") as original, temporary.open("wb") as destination:
            size = 0
            checksum = hashlib.sha256()
            for chunk in iter(lambda: original.read(1024 * 1024), b""):
                if destination.write(chunk) != len(chunk):
                    raise BuildError("Preparation copy wrote fewer bytes than requested: " + target.name)
                size += len(chunk)
                checksum.update(chunk)
                if transfer: transfer(len(chunk))
            destination.flush()
        if size != before[2] or before != _stamp(source) or temporary.stat().st_size != size:
            raise BuildError("Preparation source changed during copy: " + source.name)
        shutil.copystat(source, temporary)
        published = _stamp(temporary)
        if before != _stamp(source):
            raise BuildError("Preparation source changed before publishing its copy: " + source.name)
        os.replace(temporary, target)
        # Rename can update ctime, but the accepted writer's identity, size and
        # final mtime must survive publication. Capture these only after closing
        # the writer, including on Windows. Never attach the streamed source hash
        # to a concurrent replacement/mutation of the destination.
        if _stamp(target)[:4] != published[:4]:
            raise BuildError("Preparation target changed while publishing its copy: " + target.name)
        if observed: observed(checksum.hexdigest())
    finally:
        temporary.unlink(missing_ok=True)
    return str(target)


class Preparation:
    def __init__(self, output, project, *, input_key, target, recipe, source_files=(), progress=None, reset=None, content_proofs=(), compatible_input_key=None, current_inputs=None):
        self.output, self.project = Path(output), _ordinary_owned(Path(project))
        self.progress, self.checked, self.index = progress, {}, 0
        self.current_inputs = current_inputs
        self.copy_qualified = set()
        self.copy_counter = None
        self.content_proofs = {}
        for proof in content_proofs:
            if proof.get("path") not in CONTENT_PATHS or proof.get("inputKey") != input_key or proof.get("project") != self.project.relative_to(self.output).as_posix():
                raise BuildError("Preparation mutable-content proof has an unknown scope or input.")
            self.content_proofs[proof["path"]] = proof
        if self.content_proofs and set(self.content_proofs) != set(CONTENT_PATHS):
            raise BuildError("Preparation mutable-content proofs require the exact archive/manifest pair.")
        self.group = None
        relative = self.project.relative_to(self.output).as_posix()
        self.root = _ordinary_owned(self.output / "cache/prepare-resume" / self.project.name)
        self.journal = _ordinary_owned(self.root / "journal.json")
        self.identity = {"schema": SCHEMA, "owner": OWNER, "project": relative,
                         "inputKey": input_key, "target": target, "recipe": recipe}
        if compatible_input_key is not None and (not isinstance(compatible_input_key, str) or not re.fullmatch(r"[0-9a-f]{64}", compatible_input_key)):
            raise BuildError("Preparation compatibility evidence has an invalid prior input key.")
        migration = False
        self.sources = [(Path(path), dict(row)) for path, row in source_files]
        # Read-only immutable snapshot files were already hashed while selecting
        # inputs. Store metadata qualification once, not a tree sweep per phase.
        self.source_stamps = {str(path): list(_stamp(path)) for path, _ in self.sources}
        if self.journal.exists():
            value = self._read(self.journal)
            if value.get("owner") != OWNER or value.get("project") != relative or value.get("schema") != SCHEMA:
                raise BuildError("Preparation journal ownership differs; existing files were retained.")
            if not re.fullmatch(r"[0-9a-f]{64}", value.get("inputKey", "")) or value.get("target") not in ("game", "startup", "probe") or type(value.get("recipe")) is not int or not isinstance(value.get("steps"), list):
                raise BuildError("Preparation journal identity/order is invalid; existing files were retained.")
            for step in value["steps"]:
                if not isinstance(step, dict) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", step.get("name", "")):
                    raise BuildError("Preparation journal step ownership differs; existing files were retained.")
            if any(value.get(key) != expected for key, expected in self.identity.items()):
                if compatible_input_key is not None:
                    # The builder has proved the exact old/new immutable input
                    # manifests and unchanged producers before supplying this
                    # private key. This is never inferred from matching stats.
                    if value.get("inputKey") != compatible_input_key or any(value.get(key) != expected for key, expected in self.identity.items() if key != "inputKey"):
                        raise BuildError("Preparation compatibility evidence does not match this journal identity.")
                    migration = True
                else:
                    if reset is None: raise BuildError("Preparation inputs changed; owned project requires a new preparation.")
                    reset()
                    self._discard_owned_journal(value)
                    value = None
            if value is not None and value.get("sources") != self.source_stamps:
                # Snapshot qualification already populated exact invocation
                # proofs. A compatible new mod snapshot has different paths;
                # do not read its unchanged bytes again just for that move.
                # Unqualified or genuinely changed sources still need a hash.
                for path, row in self.sources:
                    if value.get("sources", {}).get(str(path)) != self.source_stamps[str(path)]:
                        if not invocation_file_matches(path, row["sha256"], row["size"]) and (path.stat().st_size != row["size"] or digest(path) != row["sha256"]):
                            raise BuildError("Preparation source changed since its checkpoint: " + path.name)
                if not migration: value["sources"] = self.source_stamps
        else:
            value = None
            if self.project.exists() and reset is not None: reset()
        self.value = value or {**self.identity, "sources": self.source_stamps, "steps": [], "pending": None}
        if not isinstance(self.value.get("steps"), list): raise BuildError("Preparation journal has no ordered steps.")
        if not migration: write_json(self.journal, self.value)
        self.project.mkdir(parents=True, exist_ok=True)
        copy_path = _ordinary_owned(self.root / ("copy-" + value_hash(self.identity) + ".sqlite"))
        self.copies = sqlite3.connect(copy_path)
        self.copies.execute("CREATE TABLE IF NOT EXISTS copies (path TEXT PRIMARY KEY, source TEXT, target TEXT, sha256 TEXT)")
        self.copy_writes = 0
        self.witnesses = ValidatedFileWitnesses(self.copies, self.project, self.identity)
        self.prior_copies, self.prior_witnesses = None, None
        try:
            if migration:
                prior_identity = {key: self.value[key] for key in self.identity}
                prior_path = _ordinary_owned(self.root / ("copy-" + value_hash(prior_identity) + ".sqlite"))
                if prior_path.is_file():
                    self.prior_copies = sqlite3.connect(prior_path)
                    self.prior_witnesses = ValidatedFileWitnesses(self.prior_copies, self.project, prior_identity)
            if (self.root / "metadata-refresh.json").exists():
                if not preparation_identity._completed_game_preparation(self.value, target):
                    raise BuildError("Preparation identity refresh requires its complete ordered game journal.")
                preparation_metadata.refresh(self, current_inputs, recovering=True)
            self._rollback_pending()
            try:
                self._qualify()
            except BuildError:
                # A corrupt base overlay is repairable from the exact selected
                # snapshot. An interrupted later substage retains its good base.
                if not migration and getattr(self, "invalid_step", None) == "base-project" and reset is not None:
                    reset()
                    self.project.mkdir(parents=True, exist_ok=True)
                    # An authorized reset replaces the project's directory
                    # identity. Drop only this owner's old proofs and rebind to
                    # the new owned root before any replacement copy is read.
                    self.witnesses.invalidate(self.project)
                    self.witnesses = ValidatedFileWitnesses(self.copies, self.project, self.identity)
                    self.value["steps"] = []
                    self.checked.clear()
                    write_json(self.journal, self.value)
                else: raise
            if migration: self._rebind_compatible_input()
            # A kill after publication can leave an already committed undo tree.
            for step in self.value["steps"]:
                undo = _ordinary_owned(self.root / ("undo-" + step["name"]))
                if undo.exists(): shutil.rmtree(undo)
            self.copies.commit()
        except BaseException:
            self._close_prior_witnesses(commit=False)
            self.copies.close()
            raise

    def copy(self, source, target):
        """Retain qualified base-copy files, committing in bounded small batches."""
        source, target = Path(source), Path(target)
        relative = _relative(target.relative_to(self.project).as_posix())
        before = list(_stamp(source))
        previous = self.copies.execute("SELECT source,target,sha256 FROM copies WHERE path=?", (relative,)).fetchone()
        if previous and json.loads(previous[0]) == before and target.is_file() and json.loads(previous[1]) == list(_stamp(target)):
            stamp = _stamp(target)
            # A previous byte witness binds to this project/input and real file
            # ChangeTime. Older ledgers and unsupported metadata still read the
            # retained target once; a warm unchanged file needs no second read.
            if self.witnesses.qualify(target, previous[2], before[2], hasher=digest):
                if stamp != _stamp(target): raise BuildError("Retained preparation copy changed while read: " + relative)
                self.copy_qualified.add(relative)
                self.checked[relative] = (stamp, previous[2])
                if self.copy_counter: self.copy_counter.add(1, relative)
                return str(target)
        def observed(checksum):
            if before != list(_stamp(source)): raise BuildError("Preparation copy source changed: " + source.name)
            stamp = _stamp(target)
            self.copies.execute("INSERT OR REPLACE INTO copies VALUES (?,?,?,?)",
                                (relative, json.dumps(before), json.dumps(stamp), checksum))
            self.checked[relative] = (stamp, checksum)
            self.witnesses.remember(target, checksum)
            self.copy_qualified.add(relative)
            self.copy_writes += 1
            if self.copy_writes % 128 == 0: self.copies.commit()
        result = copy_changed(source, target, observed=observed)
        if self.copy_counter: self.copy_counter.add(1, relative)
        return result

    def begin_copy(self, total):
        if self.progress:
            self.copy_counter = self.progress.Counter("prepare-project-copy", total, "files", "Copying qualified project inputs")

    def end_copy(self):
        if self.copy_counter: self.copy_counter.finish()
        self.copy_counter = None

    def _read(self, path):
        if not path.is_file(): raise BuildError("Preparation journal is not an ordinary file.")
        try: return json.loads(path.read_text(encoding="utf-8"))
        except (ValueError, OSError) as error: raise BuildError("Preparation journal cannot be read: " + path.name) from error

    def _path(self, relative): return _ordinary_owned(self.project / _relative(relative))

    def _discard_owned_journal(self, value):
        pending = value.get("pending")
        if pending:
            name = pending.get("name", "")
            if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
            undo = _ordinary_owned(self.root / ("undo-" + name))
            if undo.exists(): shutil.rmtree(undo)
        for step in value.get("steps", []):
            name = step.get("name", "")
            if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
            undo = _ordinary_owned(self.root / ("undo-" + name))
            if undo.exists(): shutil.rmtree(undo)
        prior_identity = {key: value[key] for key in self.identity}
        prior_copy = self.root / ("copy-" + value_hash(prior_identity) + ".sqlite")
        for suffix in ("", "-journal", "-wal", "-shm"):
            _ordinary_owned(Path(str(prior_copy) + suffix)).unlink(missing_ok=True)
        self.journal.unlink(missing_ok=True)

    def _observe(self, relative):
        path = self._path(relative)
        if not path.exists():
            self.witnesses.invalidate(path)
            return {"path": relative, "absent": True}
        if not path.is_file(): raise BuildError("Preparation output is not a file: " + relative)
        before = _stamp(path)
        content_proof = self.content_proofs.get(relative)
        if content_proof and list(before) == content_proof["stamp"] and before[2] == content_proof["size"]:
            # The caller just hashed this exact coherent pair under its output
            # lock. These are invocation-local producer proofs, never old stats.
            self.checked[relative] = (before, content_proof["sha256"])
            self.witnesses.remember(path, content_proof["sha256"])
        if self.prior_witnesses:
            # Only the builder's proven compatible-input seam opens the exact
            # old owner database. Its accepted byte witness must still match the
            # live file's strong identity/change stamp and this receipt's SHA.
            stamp = self.prior_witnesses.current(path)
            checksum = self.prior_witnesses.observe(path, hasher=digest)
            self.witnesses.remember(path, checksum, stamp=stamp)
        else:
            checksum = self.witnesses.observe(path, hasher=digest)
        if before != _stamp(path): raise BuildError("Preparation output changed while read: " + relative)
        self.checked[relative] = (before, checksum)
        return {"path": relative, "size": before[2], "sha256": checksum}

    def _close_prior_witnesses(self, *, commit):
        if self.prior_copies is None: return
        if commit:
            for key, count in self.prior_witnesses.counters.items(): self.witnesses.counters[key] += count
            self.prior_copies.commit()
        self.prior_copies.close()
        self.prior_copies, self.prior_witnesses = None, None

    def _accept_post_effect_import(self, latest, relative, row):
        """Recognize the three pinned Unity upgrades using their real producers.

        Capture232406 still rejected BlendForBloom after the prior repair:
        post-effects records four outputs (its receipt and three sources), and
        case-paths references those sources through campaign-shaders. Neither
        producer owns their unchanged metas. The frozen restoration receipt
        already binds each meta hash and original GUID; require that proof,
        rather than inventing a meta output contract. Unlisted sources, changed
        receipts/metas and every unpinned upgrade remain errors. Warm qualified
        files never enter this reader or open these controls again.
        """
        name = next((name for name in post_effects.SHADERS
                     if relative == "Assets/Shader/Hidden_" + name + ".shader"), None)
        if name is None: return False
        def reject(reason):
            self.import_rejection = "audited Shader import rejected: " + reason
            return False
        if not preparation_identity._completed_game_preparation(self.value, self.identity["target"]):
            return reject("the complete ordered game preparation is missing")
        if self.value["steps"][latest[relative][0]]["name"] != "case-paths":
            return reject("the Shader is outside its retained case-paths owner")
        spec = post_effects.SHADERS[name]
        if row != {"path": relative, "size": spec["sourceBytes"], "sha256": spec["sourceSha256"]}:
            return reject("retained original fingerprint differs from the official source pin")
        receipt_owner, meta_owner = latest.get(post_effects.RECEIPT), latest.get(relative + ".meta")
        if receipt_owner is None or self.value["steps"][receipt_owner[0]]["name"] != "post-effects":
            return reject("the original restoration receipt has no retained post-effects owner")
        receipt_path, meta_path, shader_path = (self._path(path) for path in
                                               (post_effects.RECEIPT, relative + ".meta", relative))
        if any(not path.is_file() or path.stat().st_size > 65536 or path.stat().st_nlink != 1
               for path in (receipt_path, meta_path, shader_path)):
            return reject("a Shader/provenance file is missing, linked or exceeds its byte bound")
        before = [_stamp(path) for path in (receipt_path, meta_path)]
        # A producer which does record this meta still retains its own exact
        # contract. Ordinarily the meta has no journal row; its full hash lives
        # in the unchanged original restoration receipt instead.
        if self._observe(post_effects.RECEIPT) != receipt_owner[1]:
            return reject("the retained restoration receipt changed")
        if meta_owner is not None and self._observe(relative + ".meta") != meta_owner[1]:
            return reject("the independently retained Shader meta changed")
        try:
            with receipt_path.open("rb") as stream: receipt_bytes = stream.read(65537)
            with meta_path.open("rb") as stream: metadata = stream.read(65537)
            if (len(receipt_bytes) > 65536 or len(metadata) > 65536
                    or len(receipt_bytes) != receipt_owner[1].get("size")
                    or hashlib.sha256(receipt_bytes).hexdigest() != receipt_owner[1]["sha256"]):
                return reject("the bounded restoration receipt read differs from its original fingerprint")
            receipt = json.loads(receipt_bytes)
            if (post_effects.GUID.findall(metadata.decode("utf-8")) != [spec["guid"]]
                    or b"ShaderImporter:" not in metadata
                    or receipt.get("schema") != 1 or receipt.get("target") != "startup"
                    or receipt.get("changeset") != post_effects.CHANGESET
                    or receipt.get("installerSha256") != post_effects.SOURCE_SHA256
                    or receipt.get("effectsPackageSha256") != post_effects.PACKAGE_SHA256
                    or [entry for entry in receipt.get("shaders", []) if entry.get("assetPath") == relative]
                    != [post_effects._entry(name, spec, metadata)]):
                return reject("Shader GUID, complete meta hash or official source receipt differs")
        except (UnicodeError, ValueError, AttributeError, TypeError):
            return reject("the Shader restoration receipt or meta is malformed")
        if before != [_stamp(path) for path in (receipt_path, meta_path)]:
            return reject("Shader provenance changed during its bounded read")
        observed = self._observe(relative)
        if observed.get("sha256") != spec["importUpgradeSha256"]:
            return reject("current Shader fingerprint=" + str(observed.get("sha256", "missing")) +
                          "; expected Unity upgrade=" + spec["importUpgradeSha256"])
        row.clear(); row.update(observed)
        return True

    def _accept_editor_settings(self, latest, relative, row):
        """Retain the one Editor-authoritative build configuration, not assets.

        The final-settings producer bootstraps Linear/Vulkan before import and
        freezes its receipt. CompileStartupSdk then calls ConfigureAndroid and
        saves PlayerSettings (IL2CPP, ABI, input, product identity, GC and more).
        A completed import must not turn those expected build-configuration
        writes into an original-asset error. Bind this exception to the original
        unchanged bootstrap receipt and a durable chain of accepted rows. The
        two mandatory import fields must still be exact; no other settings file
        or original serialized content is mutable through this path.
        """
        if relative != "ProjectSettings/ProjectSettings.asset": return False
        def reject(reason):
            self.import_rejection = "audited Editor settings rejected: " + reason
            return False
        if (not preparation_identity._completed_game_preparation(self.value, self.identity["target"])
                or self.value["steps"][latest[relative][0]]["name"] != "final-settings"):
            return reject("the complete game preparation/final-settings owner is missing")
        receipt_owner = latest.get(import_workspace.RECEIPT)
        if receipt_owner is None or self.value["steps"][receipt_owner[0]]["name"] != "final-settings":
            return reject("the original Android import receipt has no retained final-settings owner")
        receipt_path, settings_path = self._path(import_workspace.RECEIPT), self._path(relative)
        if any(not path.is_file() or path.stat().st_nlink != 1 for path in (receipt_path, settings_path)):
            return reject("the receipt or settings is missing or linked")
        if receipt_path.stat().st_size > 65536 or settings_path.stat().st_size > 1024 * 1024:
            return reject("the receipt or PlayerSettings exceeds its bounded serialized size")
        before = [_stamp(path) for path in (receipt_path, settings_path)]
        accepted_stamp = self.witnesses.current(settings_path)
        with receipt_path.open("rb") as stream: receipt_bytes = stream.read(65537)
        with settings_path.open("rb") as stream: settings = stream.read(1024 * 1024 + 1)
        if (len(receipt_bytes) != receipt_owner[1].get("size") or len(receipt_bytes) > 65536
                or hashlib.sha256(receipt_bytes).hexdigest() != receipt_owner[1].get("sha256")
                or len(settings) > 1024 * 1024):
            return reject("the bounded original import receipt fingerprint changed")
        try:
            receipt = json.loads(receipt_bytes)
            if (not isinstance(receipt, dict) or type(receipt.get("schema")) is not int
                    or set(receipt) != {*import_workspace.contract("game"), "source", "assetPath", "beforeSha256", "sha256", "changed", "unityImportTimingVerified"}
                    or any(receipt.get(key) != value for key, value in import_workspace.contract("game").items())
                    or receipt.get("source") != "QuestBuild.ConfigureAndroid audited serialized Unity2021 fields"
                    or receipt.get("assetPath") != relative
                    or any(not isinstance(receipt.get(key), str) or not re.fullmatch(r"[0-9a-f]{64}", receipt[key])
                           for key in ("beforeSha256", "sha256"))
                    or type(receipt.get("changed")) is not bool or receipt.get("unityImportTimingVerified") is not False):
                return reject("the original Android import receipt contract differs")
            settings.decode("utf-8")
            if (b"\0" in settings or not re.match(rb"\A%YAML 1\.1\r?\n", settings)
                    or re.findall(rb"(?m)^--- [^\r\n]+", settings) != [b"--- !u!129 &1"]
                    or re.findall(rb"(?m)^[A-Za-z_][A-Za-z0-9_]*:", settings) != [b"PlayerSettings:"]
                    or len(re.findall(rb"(?m)^PlayerSettings:\r?$", settings)) != 1
                    or import_workspace.patch_settings(settings, "game") != settings):
                return reject("PlayerSettings is malformed or its mandatory Linear/Vulkan import fields changed")
        except (ValueError, UnicodeError, TypeError, BuildError):
            return reject("the import receipt or mandatory PlayerSettings fields are malformed")
        marker = self.value.get("editorSettings")
        previous = {"schema": 1, "assetPath": relative, "receiptSha256": receipt_owner[1]["sha256"],
                    "preparedSha256": receipt["sha256"], "accepted": dict(row)}
        if marker is not None and marker != previous:
            return reject("the prior accepted PlayerSettings row is not bound to this original receipt")
        if marker is None and row.get("sha256") != receipt["sha256"]:
            return reject("the retained preparation row is not the original bootstrapped PlayerSettings")
        if before != [_stamp(path) for path in (receipt_path, settings_path)]:
            return reject("PlayerSettings/provenance changed during the bounded read")
        observed = {"path": relative, "size": len(settings), "sha256": hashlib.sha256(settings).hexdigest()}
        self.witnesses.remember(settings_path, observed["sha256"], stamp=accepted_stamp)
        row.clear(); row.update(observed)
        self.value["editorSettings"] = {**previous, "accepted": dict(observed)}
        return True

    def _qualify(self):
        latest = {}
        for index, step in enumerate(self.value["steps"]):
            if not isinstance(step, dict) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", step.get("name", "")):
                raise BuildError("Preparation checkpoint has an unknown step.")
            if not isinstance(step.get("outputs"), list) or not step["outputs"]:
                raise BuildError("Preparation checkpoint has no output contracts: " + step["name"])
            for row in step["outputs"]:
                relative = _relative(row.get("path"))
                latest[relative] = (index, row)
        counter = self.progress.Counter("prepare-resume-verify-files", len(latest), "files", "Qualifying retained preparation contracts") if self.progress else None
        replaced = False
        imported_effects, remapped_assets, editor_settings = 0, 0, 0
        remapping = None
        # Closed output contracts share the same reliable metadata proofs as
        # outer stages. Group their observation by directory instead of opening
        # a Windows handle, walking every ancestor and querying SQLite per file.
        # Mutable Addressables pairs and absence contracts retain their explicit
        # individual semantics; changed/legacy bytes still use the old reader.
        ordinary = [row for relative, (_, row) in latest.items()
                    if not row.get("absent") and relative not in self.content_proofs]
        witness = self.prior_witnesses or self.witnesses
        qualified = set()
        for row, valid in witness.qualify_many(ordinary, hasher=digest):
            relative = row["path"]
            self.import_rejection = None
            if not valid and self._accept_post_effect_import(latest, relative, row):
                valid, replaced = True, True
                imported_effects += 1
            if not valid and self._accept_editor_settings(latest, relative, row):
                valid, replaced = True, True
                editor_settings += 1
            if (not valid and self.value["steps"][latest[relative][0]]["name"] in ("case-paths", "script-orders")
                    and preparation_identity._completed_game_preparation(self.value, self.identity["target"])
                    and relative.startswith("Assets/") and not relative.endswith((".meta", ".shader", ".cs", ".cginc"))):
                # Final Editor validation legitimately maps original SDK DLL
                # pointers to imported package scripts. script-orders records
                # the complete assetIdentityEvidence census after case-paths,
                # and is therefore the latest owner of most serialized assets.
                # Accept only unchanged original bytes under the inverse of
                # that exact mapping, whichever of those producers owns it. The
                # helper reads its small controls once, on the first changed
                # eligible file; unchanged warm owners take no extra read.
                if remapping is None: remapping = script_remap_resume.ScriptRemap(self.project, latest)
                path = self._path(relative)
                before = self.witnesses.current(path) if path.is_file() else None
                try:
                    observed = remapping.accept(relative, row)
                except BuildError as error:
                    name = self.value["steps"][latest[relative][0]]["name"]
                    self.invalid_step = name
                    raise BuildError("Retained preparation output changed in " + name + ": " + relative +
                                     "; audited package-script import rejected: " + str(error) +
                                     "; completed steps and Unity Library were retained.") from error
                if observed is None:
                    self.import_rejection = ("audited package-script import rejected: retained manifest membership or "
                                             "inverse complete original fingerprint/size differs; original=" + row.get("sha256", "missing"))
                if observed is not None:
                    # The helper hashes the complete bytes while proving the
                    # inverse. Bind that read to its surrounding strong stamp;
                    # do not hash the same scene/prefab again on this cold repair.
                    if before is None: raise BuildError("Remapped preparation output disappeared: " + relative)
                    self.witnesses.remember(path, observed["sha256"], stamp=before)
                    row.clear(); row.update(observed)
                    valid, replaced = True, True
                    remapped_assets += 1
            if not valid:
                name = self.value["steps"][latest[relative][0]]["name"]
                self.invalid_step = name
                raise BuildError("Retained preparation output changed in " + name + ": " + relative +
                                 ("; " + self.import_rejection if self.import_rejection else "") +
                                 "; completed steps and Unity Library were retained. Restore the file or use a fresh output folder.")
            if self.prior_witnesses:
                # The old owner just qualified this exact ID/ChangeTime/hash.
                # Transfer that stamp rather than resampling and attaching old
                # bytes to a possibly new file during input-key migration.
                self.witnesses.adopt_qualified(self.project / relative)
            qualified.add(relative)
            if counter: counter.add(1, relative)
        for relative, (index, row) in latest.items():
            if relative in qualified: continue
            observed = self._observe(relative)
            proof = self.content_proofs.get(relative)
            if proof and observed == {"path": relative, "size": proof["size"], "sha256": proof["sha256"]}:
                # Addressables owns this accepted mutable pair after preparation.
                # Refresh only its latest phase owner; other assets stay exact.
                if row != observed: row.clear(); row.update(observed); replaced = True
            if observed != row:
                name = self.value["steps"][index]["name"]
                self.invalid_step = name
                raise BuildError("Retained preparation output changed in " + name + ": " + relative +
                                 "; completed steps and Unity Library were retained. Restore the file or use a fresh output folder.")
            if counter: counter.add(1, relative)
        self._close_prior_witnesses(commit=True)
        if counter:
            counter.detail = "Qualifying retained preparation contracts: " + self.witnesses.summary()
            if imported_effects or remapped_assets or editor_settings:
                counter.detail += "; retained Unity upgrades: " + str(imported_effects) + " shaders, " + str(remapped_assets) + " script-bound assets, " + str(editor_settings) + " Editor settings"
            counter.finish()
        if replaced:
            # Publish the accepted closed-read stamps before their new rows.
            # A cut after journal publication can then retain the imported work
            # without reopening all shader/asset bytes to establish witnesses.
            self.copies.commit()
            write_json(self.journal, self.value)

    def _rebind_compatible_input(self):
        """Retain a byte-qualified pre-archive prefix after a proved tool repair."""
        if self.value["inputKey"] == self.identity["inputKey"]: return
        if preparation_identity._completed_game_preparation(self.value, self.identity["target"]):
            if self.current_inputs is None:
                raise BuildError("Completed preparation reuse requires the qualified current input manifest.")
            preparation_metadata.refresh(self, self.current_inputs)
            return
        steps = self.value["steps"]
        boundary = next((index for index, step in enumerate(steps) if step["name"] == "startup-archive"), len(steps))
        prefix = {row["path"] for step in steps[:boundary] for row in step["outputs"]}
        # Later package/compiler/case phases may have rewritten prefix files.
        # Dropping their last owner would expose stale earlier contracts on the
        # next restart. Such ownership migration needs its own explicit recipe;
        # retain everything and fail rather than guess replacement contracts.
        for step in steps[boundary:]:
            if any(row["path"] in prefix for row in step["outputs"]):
                raise BuildError("Preparation compatibility cannot discard a later owner of retained output contracts: " + step["name"] + "; project and journal were retained.")
        self.value["steps"] = steps[:boundary]
        self.value["inputKey"] = self.identity["inputKey"]
        self.value["sources"] = self.source_stamps
        write_json(self.journal, self.value)

    def _emit(self, operation, status, detail):
        if self.progress:
            self.progress.event("operation:" + operation, 1 if status in ("complete", "reuse") else 0,
                                1, "operations", detail, status=status, operation=operation)

    @contextmanager
    def operation(self, name, count):
        if self.group is not None or type(count) is not int or count < 1:
            raise BuildError("Preparation operation requires an exact positive substage count.")
        self.group = {"name": name, "done": 0, "total": count, "reused": True}
        self._emit(name, "start", "Preparing operation: " + name)
        try:
            yield
            if self.group["done"] != count: raise BuildError("Preparation operation did not finish every planned substage: " + name)
            self._emit(name, "reuse" if self.group["reused"] else "complete", "Preparation operation finished: " + name)
        except BaseException as error:
            self._emit(name, "failed", "Preparation operation failed: " + name + "; " + str(error))
            raise
        finally:
            self.group = None

    def _substage(self, name, operation, status):
        if self.group is None or self.group["name"] != operation:
            raise BuildError("Preparation substage is outside its planned operation: " + name)
        if status in ("complete", "reuse"): self.group["done"] += 1
        if status != "reuse": self.group["reused"] = False
        if self.progress:
            self.progress.event("prepare-substage:" + name, self.group["done"], self.group["total"], "steps",
                                "Preparation substage: " + name, status=status, operation=operation)

    def _rollback_pending(self):
        pending = self.value.get("pending")
        if not pending: return
        name = pending.get("name", "")
        if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
        undo = _ordinary_owned(self.root / ("undo-" + name))
        rows = pending.get("undo", [])
        if not isinstance(rows, list): raise BuildError("Preparation undo records are invalid.")
        # Check all retained originals before changing the first generated file.
        files = []
        for row in rows:
            self._path(row["path"])
            if row.get("directory"):
                for directory in row.get("directories", []): self._path(directory)
                files.extend(row["files"])
            elif not row.get("absent"): files.append(row)
        total = sum(row["size"] for row in files if not row.get("absent"))
        verify_counter = self.progress.Counter("prepare-items:" + name + "-rollback-verify", total, "bytes",
                                               "Qualifying saved inputs before interrupted-build restoration") if self.progress else None
        for row in files:
            self._path(row["path"])
            if row.get("absent"): continue
            saved = _ordinary_owned(undo / row["backup"])
            if saved.parent != undo or not saved.is_file() or saved.stat().st_size != row["size"]:
                raise BuildError("Preparation undo bytes changed; no project file was overwritten: " + row["path"])
            checksum = digest(saved, progress=lambda size: verify_counter.add(size, row["path"])) if verify_counter else digest(saved)
            if checksum != row["sha256"]:
                raise BuildError("Preparation undo bytes changed; no project file was overwritten: " + row["path"])
        if verify_counter: verify_counter.finish()
        restore_counter = self.progress.Counter("prepare-items:" + name + "-rollback-restore", total, "bytes",
                                                "Restoring saved inputs for interrupted-build recovery") if self.progress else None
        def restore(row):
            streamed = 0
            def transfer(size):
                nonlocal streamed
                streamed += size
                restore_counter.add(size, row["path"])
            def observed(_checksum):
                # A second interruption can leave this original already
                # restored. The existing copy qualifier reads it once and
                # accepts it without another write; credit that accepted file
                # only after its bytes and source/destination stamps qualify.
                restore_counter.add(row["size"] - streamed, row["path"])
            if restore_counter:
                copy_changed(undo / row["backup"], self._path(row["path"]), observed=observed, transfer=transfer)
            else:
                copy_changed(undo / row["backup"], self._path(row["path"]))
        for row in reversed(rows):
            target = self._path(row["path"])
            self.witnesses.invalidate(target)
            if row.get("directory"):
                if target.exists(): shutil.rmtree(target)
                target.mkdir(parents=True)
                for directory in row["directories"]: self._path(directory).mkdir(parents=True, exist_ok=True)
                for member in row["files"]: restore(member)
            elif row.get("absent"):
                if target.is_dir(): shutil.rmtree(target)
                else: target.unlink(missing_ok=True)
            else:
                restore(row)
        # Keep the accepted pending journal until restoration is entirely done;
        # a second interruption can safely restore the same original files.
        self.value["pending"] = None
        write_json(self.journal, self.value)
        if restore_counter: restore_counter.finish()
        if undo.exists(): shutil.rmtree(undo)

    def _undo(self, name, paths):
        paths = list(dict.fromkeys(paths))
        # The 093454 Windows capture includes long preparation gaps around
        # native conversion. Every destructive phase needs these bounded undo
        # copies, not only startup movies. Enumerate each declared directory
        # once, then report bytes from the existing writer; do not qualify its
        # content a second time merely to obtain a percentage.
        planned, total = [], 0
        for relative in paths:
            path = self._path(relative)
            if not path.exists():
                planned.append({"path": relative, "absent": True})
            elif path.is_dir():
                directories, members = [], []
                for member in path.rglob("*"):
                    member = _ordinary_owned(member)
                    member_name = member.relative_to(self.project).as_posix()
                    if member.is_dir(): directories.append(member_name)
                    elif member.is_file():
                        members.append(member_name)
                        total += member.stat().st_size
                    else: raise BuildError("Preparation undo subtree contains a non-file: " + member_name)
                planned.append({"path": relative, "directory": True, "directories": directories, "files": members})
            elif path.is_file():
                total += path.stat().st_size
                planned.append({"path": relative})
            else: raise BuildError("Preparation undo can preserve only owned files/directories: " + relative)
        backup_counter = self.progress.Counter("prepare-items:" + name + "-backup", total, "bytes",
                                               "Preserving original inputs for interrupted-build recovery") if self.progress and total else None
        root = _ordinary_owned(self.root / ("undo-" + name))
        if root.exists(): shutil.rmtree(root)  # same exact journal-owned basename, before any phase mutation
        root.mkdir(parents=True)
        rows = []
        sequence = 0
        def preserve(relative):
            nonlocal sequence
            path = self._path(relative)
            saved = root / str(sequence)
            def observed(checksum):
                self.checked[relative] = (_stamp(path), checksum)
                self.witnesses.remember(path, checksum)
            def transfer(size): backup_counter.add(size, relative)
            if backup_counter: copy_changed(path, saved, observed=observed, transfer=transfer)
            else: copy_changed(path, saved, observed=observed)
            row = {**self._observe(relative), "backup": str(sequence)}
            sequence += 1
            return row
        for row in planned:
            if row.get("absent"): rows.append(row)
            elif row.get("directory"):
                rows.append({**row, "files": [preserve(relative) for relative in row["files"]]})
            else: rows.append(preserve(row["path"]))
        if backup_counter: backup_counter.finish()
        return rows

    def run(self, name, operation, action, contracts, *, mutations=()):
        """Run one real function, committing only after its contracts qualify."""
        if self.index < len(self.value["steps"]):
            step = self.value["steps"][self.index]
            if (step["name"], step["operation"]) != (name, operation):
                raise BuildError("Preparation phase order changed; use a new recipe/input identity.")
            self.index += 1
            self._substage(name, operation, "reuse")
            return step.get("result")
        self._substage(name, operation, "start")
        paths = mutations() if callable(mutations) else mutations
        undo = self._undo(name, paths) if paths else []
        self.value["pending"] = {"name": name, "operation": operation, "undo": undo}
        write_json(self.journal, self.value)
        for relative in paths: self.witnesses.invalidate(self._path(relative))
        try:
            result = action()
            paths = contracts(result) if callable(contracts) else contracts
            required = list(dict.fromkeys(paths))
            counter = self.progress.Counter("prepare-items:" + name + "-contracts", len(required), "files",
                                            "Recording produced output contracts") if self.progress else None
            outputs = []
            for path in required:
                relative = _relative(str(path))
                outputs.append(self._observe(relative))
                if counter: counter.add(1, relative)
            allowed_absent = getattr(paths, "absent", set())
            for row in outputs:
                if row.get("absent") and row["path"] not in allowed_absent:
                    raise BuildError("Preparation substage is missing its required output: " + name + ": " + row["path"])
            if not outputs or all(row.get("absent") for row in outputs):
                raise BuildError("Preparation substage produced no verifiable output: " + name)
            if counter: counter.finish()
            self.assert_sources()
            step = {"name": name, "operation": operation, "outputs": outputs}
            self.value["steps"].append(step)
            self.value["pending"] = None
            write_json(self.journal, self.value)
            self.index += 1
            self.copies.commit()
            root = self.root / ("undo-" + name)
            if root.exists(): shutil.rmtree(root)
            self._substage(name, operation, "complete")
            return result
        except BaseException as error:
            self._substage(name, operation, "failed")
            raise

    def assert_sources(self):
        # Stat the declared immutable files, not a recursive project/source scan.
        # A changed source is never accepted merely because an old inputKey exists.
        for path, _ in self.sources:
            if list(_stamp(path)) != self.source_stamps[str(path)]:
                raise BuildError("Preparation source changed while running: " + path.name)

    def finish(self):
        if self.value.get("pending") or self.index != len(self.value["steps"]):
            raise BuildError("Preparation contains an unfinished checkpoint.")
        self.assert_sources()
        self.copies.commit()

    def close(self):
        self.copies.commit()
        self.copies.close()


def manifest_contracts(project, manifests, *, extra=()):
    """Resolve declared produced files, retaining source provenance separately."""
    project, result, absent = Path(project), list(manifests) + list(extra), set()
    roots = ("Assets/", "Packages/", "ProjectSettings/", "QuestRecovery/", "QuestStartupEvidence/", "QuestCampaignEvidence/")
    sources = {}
    def source_containers(rows):
        # Sprite/Texture2D receipts record the original PC CAB containers beside
        # their produced assets. The 142900 Windows run restored every Sprite,
        # then mistook sourceContainers[].path for copied StreamingAssets. These
        # exact {path, sha256} records are input provenance, never output files.
        # Validate the role instead of skipping arbitrary nested declarations:
        # an extra assetPath/output or generated-project root is ambiguous.
        if not isinstance(rows, list): raise BuildError("Preparation source-container provenance is not a list.")
        for row in rows:
            if not isinstance(row, dict) or set(row) != {"path", "sha256"}:
                raise BuildError("Preparation source-container provenance has ambiguous fields.")
            name = _relative(row["path"])
            if name.split("/", 1)[0] in {root[:-1] for root in roots}:
                raise BuildError("Preparation source-container provenance names a generated output: " + name)
            checksum = row["sha256"]
            if not isinstance(checksum, str) or not re.fullmatch(r"[0-9a-f]{64}", checksum):
                raise BuildError("Preparation source-container provenance has an invalid byte hash: " + name)
            if name in sources and sources[name] != checksum:
                raise BuildError("Preparation source-container provenance disagrees on original bytes: " + name)
            sources[name] = checksum
    def visit(value):
        if isinstance(value, dict):
            for key in ("assetPath", "asset", "path", "metaPath", "sourceScene", "source"):
                name = value.get(key)
                if isinstance(name, str) and name.startswith(roots):
                    if not (project / _relative(name)).is_dir(): result.append(name)
                    if (key == "source" and name.startswith("Assets/") and Path(name).suffix.lower() in (".mp4", ".mov", ".webm", ".ogv")
                            and isinstance(value.get("path"), str) and value["path"].startswith("StreamingAssets/") and not (project / name).exists()):
                        absent.add(name)
                elif isinstance(name, str) and name.startswith("StreamingAssets/"):
                    result.append(_relative("Assets/" + name))
            if isinstance(value.get("files"), dict):
                for name in value["files"]:
                    if name.startswith("Assets/"): result.append(_relative(name))
            if "originalPath" in value and "sha256" in value and "size" in value:
                name = _relative(value["originalPath"])
                if name != value.get("assetPath"):
                    result.append(name); absent.add(name)
            if "manifestSha256" in value:
                checksums = value["manifestSha256"]
                # Compute restoration declares one scalar evidence hash; case
                # migration declares a map of rewritten files to their hashes.
                # The 093454 Windows failure iterated the scalar and mistook
                # its first hex character, `e`, for a required output filename.
                # A scalar is provenance for the already required manifest,
                # while every key in a valid map remains an output contract.
                if isinstance(checksums, str):
                    if not re.fullmatch(r"[0-9a-f]{64}", checksums):
                        raise BuildError("Preparation manifest hash evidence has an invalid SHA-256.")
                elif isinstance(checksums, dict):
                    for name, checksum in checksums.items():
                        if not isinstance(checksum, str) or not re.fullmatch(r"[0-9a-f]{64}", checksum):
                            raise BuildError("Preparation manifest hash map has an invalid SHA-256.")
                        result.append(_relative(name))
                else:
                    raise BuildError("Preparation manifest hash evidence must be a SHA-256 or a path-to-SHA-256 map.")
            for key in ("updatedManifests", "refreshedManifests"):
                for name in value.get(key, []):
                    if isinstance(name, str): result.append(_relative(name))
            for name, destination in value.get("pathMap", {}).items():
                result.extend((_relative(name), _relative(destination)))
                result.extend((_relative(name + ".meta"), _relative(destination + ".meta")))
                if name != destination: absent.update((name, name + ".meta"))
            for key, nested in value.items():
                if key == "sourceContainers": source_containers(nested)
                elif isinstance(nested, (dict, list)): visit(nested)
        elif isinstance(value, list):
            for nested in value:
                if isinstance(nested, (dict, list)): visit(nested)
    for name in manifests:
        path = _ordinary_owned(project / _relative(name))
        if not path.is_file(): raise BuildError("Preparation phase evidence is missing: " + name)
        visit(json.loads(path.read_text(encoding="utf-8")))
    # Only explicit removed movie importer sources may be absent. Delivered
    # StreamingAssets bytes remain required, even when their suffix is a movie.
    return Contracts(list(dict.fromkeys(result)), absent=absent)
