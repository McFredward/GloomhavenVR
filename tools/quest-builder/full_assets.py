"""Stage all recovered Campaign assets with exact original object provenance.

Recovery itself is bounded and resumable. This step accepts only its completed,
hashed checkpoint, resumes a private derived Unity project, preserves witnessed
B614 GUID contracts, and emits the complete original catalog association. It
does not call an asset export an Android build or a playable game.
"""
from contextlib import contextmanager
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import uuid

from storage import BuildError, build_progress, write_json
from staging_resume import Journal

RECOVERY_TOOLS = Path(__file__).resolve().parents[1] / "quest-recovery"
STAGING_SECTIONS = ("catalog", "canonical", "copy", "runtime", "guid", "layout",
                    "native", "catalog-final", "index", "tmp", "bindings", "audit", "scenes", "report")
COPY_CHUNK = 1024 * 1024
LARGE_FILE = 8 * COPY_CHUNK
WINDOWS_FILE_TIMES = sys.platform == "win32"


@contextmanager
def _section(name):
    """Publish actual derived-stage boundaries, never a duration-based percent."""
    if name not in STAGING_SECTIONS:
        raise ValueError("Unknown full asset staging section.")
    phase = "staging-section:" + name
    build_progress.event(phase, 0, 1, "steps", status="start")
    try:
        yield
    except BaseException as error:
        build_progress.event(phase, detail="Failed: " + type(error).__name__, status="failed")
        raise
    build_progress.event(phase, 1, 1, "steps", status="complete")


def _stamp(value):
    return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns


def _identity_stamp(value):
    # CPython Windows 3.14.8 exposes creation time through path stat but change
    # time through fstat (https://github.com/python/cpython/issues/157671).
    # Compare these APIs only on common identity/size/mtime; each API retains
    # its own full pre/post timestamp guard, including ctime.
    return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns


def _written_identity(value):
    # Win32 guarantees final write timestamps only after the writer closes.
    # Identity/size are already comparable while that owned handle is open.
    return value.st_dev, value.st_ino, value.st_size


class _StageProofs:
    """Actual byte proofs, with owned persistent change-stamp qualification.

    Each copy endpoint is qualified through startup.safe_path or the exact
    original runtime task. Repeating parent resolution for every later report
    lookup would add ancestor walks to all 100,000 files. Leaf lstat identity,
    size/mtime/ctime still invalidate every proof after a native/GUID rewrite.
    """
    def __init__(self):
        self.files = {}
        self.witnesses = None

    def remember(self, path, digest, stamp):
        path = Path(path).absolute()
        self.files[path] = stamp, digest
        if self.witnesses is not None and self.witnesses.root in path.parents:
            # Persistent metadata has its own Win32 ChangeTime seam. Do not
            # compare path stat creation time against fstat change time here.
            self.witnesses.remember(path, digest)

    @staticmethod
    def current(path):
        value = path.lstat()
        if not stat.S_ISREG(value.st_mode):
            raise BuildError("Staging evidence is not a regular file: " + str(path))
        return _stamp(value)

    def digest(self, path, hasher=None):
        path = Path(path).absolute()
        if self.witnesses is not None and self.witnesses.root in path.parents:
            digest = self.witnesses.observe(path, hasher=hasher or _hash_output)
            self.files[path] = self.current(path), digest
            return digest
        before = self.current(path)
        prior = self.files.get(path)
        if prior is not None and prior[0] == before:
            return prior[1]
        digest = (hasher or _hash_output)(path)
        if self.current(path) != before:
            raise BuildError("Staging evidence changed while being read: " + str(path))
        self.remember(path, digest, before)
        return digest

    def published(self, path):
        path = Path(path).absolute()
        prior = self.files.get(path)
        if prior is None or prior[0] != self.current(path):
            raise BuildError("Current staged output changed before publication: " + str(path))
        if self.witnesses is not None and self.witnesses.root in path.parents:
            if not self.witnesses.qualify(path, prior[1], prior[0][2], hasher=_hash_output):
                raise BuildError("Current staged output bytes changed before publication: " + str(path))
        return prior[1]


def _copy_recovered(original, target, expected=None, *, expected_size=None, proofs=None,
                    phase="staging-copy-file"):
    """Qualify the exact bytes copied once into the fresh invocation-owned tree.

    The former verify_copy read every asset before copying and read the target
    again afterwards. The checkpoint hash can instead qualify the bytes passed
    to the writer. Require complete writes, unchanged source identity/stamps,
    exact size/hash and the same output inode before retaining a writer proof.
    Partial output is removed on every failure; original inputs stay read-only.
    """
    original, target = Path(original), Path(target)
    before = original.lstat()
    if not stat.S_ISREG(before.st_mode):
        raise BuildError("Immutable source is not a regular file: " + str(original))
    if expected_size is not None and (type(expected_size) is not int or expected_size != before.st_size):
        raise BuildError("Recovered output size differs from recovery receipt: " + str(original))
    if expected is not None and (not isinstance(expected, str) or not re.fullmatch(r"[0-9a-f]{64}", expected)):
        raise BuildError("Recovered output lacks an exact recovery receipt hash: " + str(original))
    target.parent.mkdir(parents=True, exist_ok=True)
    digest, count, created = hashlib.sha256(), 0, False
    counter = build_progress.Counter(phase, before.st_size, "bytes", original.name) if before.st_size >= LARGE_FILE else None
    try:
        with original.open("rb") as source:
            opened = os.fstat(source.fileno())
            if _identity_stamp(opened) != _identity_stamp(before):
                raise BuildError("Recovered output changed before staging: " + str(original))
            # Exclusive creation cannot overwrite an unexpected file or symlink.
            with target.open("xb") as destination:
                created = True
                for chunk in iter(lambda: source.read(COPY_CHUNK), b""):
                    if count + len(chunk) > before.st_size:
                        raise BuildError("Recovered output grew while staging: " + str(original))
                    if destination.write(chunk) != len(chunk):
                        raise BuildError("Staged copy write was incomplete: " + str(target))
                    count += len(chunk); digest.update(chunk)
                    if counter: counter.add(len(chunk), original.name)
                if _stamp(os.fstat(source.fileno())) != _stamp(opened) or _stamp(original.lstat()) != _stamp(before):
                    raise BuildError("Recovered output changed while staging: " + str(original))
                actual = digest.hexdigest()
                if count != before.st_size or expected is not None and actual != expected:
                    raise BuildError("Recovered output differs from recovery receipt: " + str(original))
                destination.flush()
                written = os.fstat(destination.fileno())
                if written.st_size != count:
                    raise BuildError("Staged copy write size differs: " + str(target))
                sealed = target.lstat()
                if (_written_identity(sealed) != _written_identity(written) or
                        _stamp(os.fstat(destination.fileno())) != _stamp(written)):
                    raise BuildError("Staged copy changed while being sealed: " + str(target))
        closed = target.lstat()
        changed = (_written_identity(closed) != _written_identity(sealed) or
                   closed.st_ctime_ns != sealed.st_ctime_ns) if WINDOWS_FILE_TIMES else _stamp(closed) != _stamp(sealed)
        if changed:
            raise BuildError("Staged copy changed before publication: " + str(target))
        # Retain the path API's final stamp after writer closure; using the
        # pre-close last-write time would invalidate a correct Windows proof.
        sealed = closed
        if proofs is not None:
            proofs.remember(target, actual, _stamp(sealed))
            proofs.remember(original, actual, _stamp(before))
        if counter: counter.finish()
        return actual
    except BaseException as error:
        if created: target.unlink(missing_ok=True)
        if counter: counter.fail(error)
        raise


def _runtime_file(original, target, proofs):
    """Keep identical bytes already copied from the completed raw checkpoint."""
    if not target.exists():
        return _copy_recovered(original, target, proofs=proofs, phase="staging-runtime-file")
    # Runtime metadata may intentionally come from the preceding canonical
    # proof tree. Only our qualified current output is eligible for replacement.
    previous = proofs.published(target)
    if proofs.digest(original) == previous:
        return previous
    temporary = target.with_name(target.name + ".staging-" + uuid.uuid4().hex)
    try:
        digest = _copy_recovered(original, temporary, proofs=proofs, phase="staging-runtime-file")
        if proofs.published(target) != previous:
            raise BuildError("Runtime metadata changed before replacement: " + str(target))
        temporary.replace(target)
        proofs.remember(target, digest, proofs.current(target))
        return digest
    finally:
        proofs.files.pop(temporary.absolute(), None)
        temporary.unlink(missing_ok=True)


def _resume_copy(original, target, row, proofs, journal):
    """Reuse qualified copy bytes; repair only an unfinished scheduled target.

    No transformed target enters this path: a completed copy phase reuses its
    context instead. A process killed before the batched SQLite commit can
    leave an unrecorded complete/partial copy; its exact checkpoint hash decides
    whether to retain it or replace that single invocation-owned target.
    """
    expected_size = row.get("bytes", row.get("size"))
    if not isinstance(row.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", row["sha256"]):
        raise BuildError("Recovered output lacks an exact recovery receipt hash: " + str(original))
    if expected_size is not None and (type(expected_size) is not int or expected_size < 0):
        raise BuildError("Recovered output has an invalid receipt size: " + str(original))
    if target.exists():
        if (expected_size is None or proofs.current(target)[2] == expected_size) and proofs.digest(target) == row["sha256"]:
            journal.record(target)
            return row["sha256"]
        if target.is_symlink() or not target.is_file():
            raise BuildError("Unfinished staged copy is not a regular owned target: " + str(target))
        target.unlink()
        proofs.files.pop(target.absolute(), None)
    actual = _copy_recovered(original, target, row["sha256"], expected_size=expected_size, proofs=proofs)
    journal.record(target)
    return actual


def _hash_output(path):
    size = path.stat().st_size
    counter = build_progress.Counter("staging-report-file", size, "bytes", path.name) if size >= LARGE_FILE else None
    result = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(COPY_CHUNK), b""):
                result.update(chunk)
                if counter: counter.add(len(chunk), path.name)
        if counter: counter.finish()
        return result.hexdigest()
    except BaseException as error:
        if counter: counter.fail(error)
        raise


def _output_inventory(output, proofs):
    # This is the required final provenance inventory, not an extra scan merely
    # to obtain a progress denominator. Reuse unchanged current writer proofs;
    # mutated native/GUID outputs are read and hashed before publication.
    reports = {output / "quest-startup-report.json", output / "quest-campaign-report.json"}
    paths = [path for path in sorted(output.rglob("*")) if path.is_file() and path not in reports]
    counter = build_progress.Counter("staging-report-files", len(paths), "files")
    records = []
    try:
        for path in paths:
            relative = path.relative_to(output).as_posix()
            records.append({"path": relative, "sha256": proofs.digest(path, hasher=_hash_output), "size": path.stat().st_size})
            counter.add(1, relative)
        counter.finish()
        return records
    except BaseException as error:
        counter.fail(error)
        raise


def _modules():
    if str(RECOVERY_TOOLS) not in sys.path:
        sys.path.append(str(RECOVERY_TOOLS))
    import canonical_guids
    import full_catalog
    import export_identity
    import serialized_repairs
    import tmp_shaders
    import recover
    spec = importlib.util.spec_from_file_location("quest_recovery_full_startup", RECOVERY_TOOLS / "startup.py")
    startup = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(startup)
    return canonical_guids, full_catalog, export_identity, serialized_repairs, tmp_shaders, recover, startup


def _catalog_roots(canonical_startup, full_manifest):
    """Existing typed original catalog aliases witness old bundle roots."""
    manifest = Path(canonical_startup) / "Assets/QuestOriginalStartup/startup-addressables.json"
    old = json.loads(manifest.read_text())
    if old["catalogSha256"] != full_manifest["catalogSha256"]:
        raise BuildError("Canonical startup and full recovery use different original catalogs.")
    by_location = {}
    for row in full_manifest["entries"]:
        if row["status"] == "associated":
            by_location.setdefault(row["originalLocationIndex"], []).append(row)
    roots = []
    for row in old["entries"]:
        if row["status"] != "associated":
            continue
        # Historical startup maps predate native exporter instrumentation.
        # Container/type aliases alone cannot distinguish a Resources object
        # from a byte-identical object in a different original CAB. Only maps
        # carrying that actual native identity can witness an additional root.
        if not row.get("originalCollection") or not row.get("originalPathId"):
            continue
        incoming = [target for target in by_location.get(row["entryIndex"], [])
                    if target["resourceTypeName"] == row["resourceTypeName"] and
                    target["originalAssetPath"].casefold() == row["originalAssetPath"].casefold() and
                    target["originalCollection"] == row["originalCollection"] and
                    target["originalPathId"] == row["originalPathId"]]
        if len(incoming) == 1:
            roots.append((incoming[0]["recoveredGuid"], row["recoveredGuid"],
                          "original-typed-catalog-location:" + str(row["entryIndex"])))
    return roots


def repair_reused_stage(project):
    """Upgrade a generated copy of an older full stage using retained proofs.

    Call after copying the immutable recovered stage, before Unity import. This
    derives missing canonical PPtr fixes and the typed packed-sprite manifest
    from that copy's own source receipts; no private overlay is required.
    """
    canonical, _, _, _, _, recover, _ = _modules()
    project = Path(project).resolve()
    canonical_report = canonical.repair_remaining_references(project)
    pointer = json.loads((project / "QuestRecovery/full-native-pointer-repair.json").read_text())
    if pointer.get("complete") is not True or pointer.get("remainingMissingPointerCount") != 0:
        raise BuildError("Reused Campaign stage has incomplete native pointer evidence.")
    sprites, atlases = [], []
    for target in pointer["additionalNativeTargets"]:
        if target["classId"] != 687078895:
            continue
        receipt_name = "packed-sprites-" + target["guid"] + ".json"
        source = project / "QuestRecovery" / receipt_name
        receipt = json.loads(source.read_text())
        if len(receipt["restoredMembers"]) != receipt["originalMemberCount"]:
            raise BuildError("Reused Campaign stage lacks exact native packed-Sprite members.")
        for row in receipt["restoredMembers"]:
            if recover.sha256(project / row["assetPath"]) != row["sha256"]:
                raise BuildError("Reused packed-Sprite source bytes changed: " + row["assetPath"])
        sprites.extend(receipt["restoredMembers"])
        path = target["path"].replace(".spriteatlas", ".asset")
        if not (project / path).is_file():
            raise BuildError("Reused native packed atlas is missing.")
        atlases.append({"assetPath": path, "guid": target["guid"], "sourceCollection": target["collection"],
                       "sourcePathId": target["pathId"], "spriteCount": receipt["originalMemberCount"],
                       "receiptPath": "QuestRecovery/" + receipt_name})
    manifest = project / "Assets/QuestOriginalCampaign/packed-sprites.json"
    write_json(manifest, {"schema": 1, "spriteCount": len(sprites), "atlases": atlases, "sprites": sprites,
                         "unityImportVerified": False, "headsetPictureVerified": False})
    return {"canonicalReferenceRepair": canonical_report, "packedSpriteManifest": manifest.relative_to(project).as_posix(),
            "packedSpriteManifestSha256": recover.sha256(manifest), "nativeSpriteCount": len(sprites)}


def stage(source, game_data, output, tmp_archive, *, canonical_project=None,
          canonical_startup=None, managed_types, cab_bundles, unitypy=None, resume_owner=None):
    """Return a full asset manifest/report; resume only the owned derived tree.

    ``source`` is bundle_recovery.py's completed project. ``canonical_project``
    is its previous read-only full core export; ``canonical_startup`` is B614's
    verified staging tree. Managed metadata and CAB bundle mapping are paths to
    actual locally recovered inventories. All 13 original scene indices remain.
    """
    canonical, catalogs, identities_module, layouts, tmp, recover, startup = _modules()
    source, game_data, output = [Path(path).resolve() for path in (source, game_data, output)]
    read_only = [source, game_data]
    read_only.extend(Path(path).resolve() for path in (canonical_project, canonical_startup) if path is not None)
    if any(root == output or root in output.parents or output in root.parents for root in read_only):
        raise BuildError("Full asset staging output must be outside read-only recovery/game inputs.")
    checkpoint = source / "quest-full-recovery-progress.json"
    progress = json.loads(checkpoint.read_text())
    if progress.get("schema") != 1 or progress.get("assetsRecovered") is not True:
        raise BuildError("Full Campaign staging requires all original catalog bundle batches to finish.")
    catalog_path = game_data / "StreamingAssets/aa/catalog.json"
    if recover.sha256(catalog_path) != progress["catalogSha256"]:
        raise BuildError("Original game catalog changed after full recovery.")
    types = json.loads(Path(managed_types).read_text())
    owners = json.loads(Path(cab_bundles).read_text())
    rows = progress["identities"]
    proofs = _StageProofs()
    identity = {"source": str(source), "game": str(game_data), "output": str(output),
                "checkpointSha256": recover.sha256(checkpoint), "catalogSha256": progress["catalogSha256"],
                "managedTypesSha256": recover.sha256(Path(managed_types)), "cabBundlesSha256": recover.sha256(Path(cab_bundles)),
                "tmpArchiveSha256": recover.sha256(Path(tmp_archive)) if Path(tmp_archive).is_file() else None,
                "canonicalProject": str(Path(canonical_project).resolve()) if canonical_project is not None else None,
                "canonicalStartup": str(Path(canonical_startup).resolve()) if canonical_startup is not None else None,
                "resumeOwner": resume_owner}
    with Journal(output, identity, proofs, resume_owner=resume_owner) as journal:
        def _catalog():
            original_catalog = catalogs.associate(catalog_path, source, rows, owners, types)
            return original_catalog
        original_catalog = journal.run("catalog", 0, _catalog)
        def _canonical():
            if canonical_project is not None:
                if canonical_startup is None:
                    raise BuildError("A previous canonical project requires its actual startup provenance report.")
                roots = _catalog_roots(canonical_startup, original_catalog)
                guid_proof = canonical.witness(canonical_project, source, rows, roots)
            else:
                import canonical_contracts
                guid_proof = canonical_contracts.witness(game_data, source, rows, unitypy)
            return guid_proof
        guid_proof = journal.run("canonical", 1, _canonical)
        def _copy():
            copied = []
            # The retained checkpoint already contains the exact copy schedule.
            # Exclude the same unused debug resources before counting; do not walk
            # the input tree a second time merely for a progress denominator.
            copy_rows = [row for row in progress["files"] if not row["path"].startswith("Assets/Resources/srdebugger/")]
            counter = build_progress.Counter("staging-copy", len(copy_rows), "files")
            try:
                for row in copy_rows:
                    relative = row["path"]
                    original, target = startup.safe_path(source, relative), startup.safe_path(output, relative)
                    _resume_copy(original, target, row, proofs, journal)
                    copied.append({"path": relative, "sha256": row["sha256"]})
                    counter.add(1, relative)
                counter.finish()
            except BaseException as error:
                counter.fail(error)
                raise
            return copied
        copied = journal.run("copy", 2, _copy, copy=True)
        original_metadata = Path(canonical_project) if canonical_project is not None else source
        old_identity = original_metadata / "QuestRecovery/original-script-identities.json"
        def _runtime():
            # Keep exact original DLL identity while avoiding another read of the
            # just-qualified current writer bytes in the fresh output.
            assemblies = list((output / "Assets/Plugins").glob("*.dll"))
            counter = build_progress.Counter("staging-managed-assemblies", len(assemblies), "files")
            for path in assemblies:
                original = game_data / "Managed" / path.name
                if not original.is_file() or proofs.digest(path, hasher=_hash_output) != proofs.digest(original, hasher=_hash_output):
                    raise BuildError("Recovered full project lost original managed assembly bytes: " + path.name)
                counter.add(1, path.name)
            counter.finish()
            (output / "QuestRecovery").mkdir(exist_ok=True)
            tasks = [(Path(managed_types), output / "QuestRecovery/managed-types.json")]
            if old_identity.is_file():
                tasks.append((old_identity, output / "QuestRecovery/original-script-identities.json"))
            # Listing each selected runtime tree is the actual copy plan, reused
            # directly by the writer; there is no additional directory-size scan.
            for name in ("Rulebase", "GloomData.dat", "Apparance", "Procedures"):
                original = game_data / "StreamingAssets" / name
                target = output / "Assets/StreamingAssets" / name
                if original.is_file():
                    tasks.append((original, target))
                elif original.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    for path in sorted(original.rglob("*")):
                        destination = target / path.relative_to(original)
                        if path.is_dir(): destination.mkdir(parents=True, exist_ok=True)
                        elif path.is_file(): tasks.append((path, destination))
            counter = build_progress.Counter("staging-runtime-copy", len(tasks), "files")
            try:
                for original, target in tasks:
                    _runtime_file(original, target, proofs)
                    journal.accept()
                    counter.add(1, target.relative_to(output).as_posix())
                counter.finish()
            except BaseException as error:
                counter.fail(error)
                raise
            return None
        journal.run("runtime", 3, _runtime)
        def _guid():
            new_rows = canonical.apply(output, rows, guid_proof)
            write_json(output / "QuestRecovery/original-asset-identities.json", {"schema": 1, "identities": new_rows})
            return new_rows
        rows = journal.run("guid", 4, _guid)
        def _layout():
            original_objects = identities_module.object_index(rows)
            layout_report = layouts.restore(game_data, output, original_objects, unitypy)
            return layout_report
        layout_report = journal.run("layout", 5, _layout)
        def _native():
            import native_stage
            new_rows, native_report = native_stage.restore(output, game_data, rows, owners, unitypy=unitypy, audit_references=False)
            return [new_rows, native_report]
        rows, native_report = journal.run("native", 6, _native, auxiliary=output.with_name(output.name + "-native-restoration"))
        def _catalog_final():
            manifest = catalogs.associate(catalog_path, output, rows, owners, types)
            folder = output / "Assets/QuestOriginalCampaign"
            folder.mkdir(parents=True, exist_ok=True)
            write_json(folder / "campaign-addressables.json", manifest)
            return manifest
        manifest = journal.run("catalog-final", 7, _catalog_final)
        folder = output / "Assets/QuestOriginalCampaign"
        def _index():
            index, paths = startup.asset_index(output)
            scripts, plugins = startup.script_index(output, types)
            return {"index": index, "paths": paths, "scripts": [[list(key), list(value)] for key, value in scripts.items()], "plugins": plugins}
        indexed = journal.run("index", 8, _index)
        index, paths, plugins = indexed["index"], indexed["paths"], indexed["plugins"]
        scripts = {tuple(key): tuple(value) for key, value in indexed["scripts"]}
        selected = set(paths)
        def _tmp():
            restored_tmp = tmp.restore(output, original_metadata, selected, tmp_archive)
            return restored_tmp
        restored_tmp = journal.run("tmp", 9, _tmp)
        restored_paths = {row["assetPath"] for row in restored_tmp}
        def _bindings():
            bindings = startup.binding_manifest(output, selected, scripts, plugins)
            write_json(folder / "script-bindings.json", bindings)
            return bindings
        bindings = journal.run("bindings", 10, _bindings)
        def _audit():
            script_origins = json.loads(old_identity.read_text()) if old_identity.is_file() else []
            if isinstance(script_origins, dict):
                script_origins = script_origins.get("identities", [])
            script_audit = recover.audit_script_bindings(output, types, script_origins)
            references = recover.audit_asset_references(output)
            if references["missingGuidCount"] or references["duplicateGuidCount"]:
                raise BuildError("Full original native source closure remains unresolved after exact repair.")
            return [script_audit, references]
        script_audit, references = journal.run("audit", 11, _audit)
        def _scenes():
            scene_settings = (output / "ProjectSettings/EditorBuildSettings.asset").read_text()
            scenes = re.findall(r"^\s+path: (Assets/.+\.unity)\s*$", scene_settings, re.M)
            scene_objects = identities_module.object_index(rows)
            scene_rows = []
            for index, relative in enumerate(scenes):
                original = [obj for obj in scene_objects.values() if obj["path"] == relative]
                if not original or {obj["collection"] for obj in original} != {"level" + str(index)} or len({obj["guid"] for obj in original}) != 1:
                    raise BuildError("Original Campaign scene identity/order is unproven: " + relative)
                scene_rows.append({"index": index, "path": relative, "guid": original[0]["guid"], "originalCollection": "level" + str(index)})
            write_json(folder / "campaign-scenes.json", {"schema": 1, "scenes": scene_rows})
            shader_rows = [{"path": relative, "name": re.search(r'Shader\s+"([^\"]+)"', (output / relative).read_text())[1],
                            "status": "official-compatible-TMP-source" if relative in restored_paths else "unresolved-original-dummy",
                            "originalShaderFidelity": False}
                           for relative in sorted(selected) if relative.endswith(".shader")]
            return [scenes, shader_rows]
        scenes, shader_rows = journal.run("scenes", 12, _scenes)
        def _report():
            if canonical_startup is not None:
                old_report = json.loads((Path(canonical_startup) / "quest-startup-report.json").read_text())
            else:
                old_report = {"sourceFingerprint": progress["sourceFingerprint"],
                              "sourceBuilderFingerprint": startup.builder_fingerprint(progress["sourceInventory"]),
                              "originalBuildScenes": [{"index": index, "path": path} for index, path in enumerate(scenes)]}
            report = {"schema": 1, "target": "campaign", "fullGameReady": False,
                      "sourceFingerprint": old_report["sourceFingerprint"],
                      "sourceBuilderFingerprint": old_report["sourceBuilderFingerprint"],
                      "recoveryReceiptSha256": recover.sha256(checkpoint), "selectedScenes": scenes,
                      "originalBuildScenes": old_report["originalBuildScenes"],
                      "startupAddressablesManifest": "Assets/QuestOriginalCampaign/campaign-addressables.json",
                      "campaignAddressablesManifest": "Assets/QuestOriginalCampaign/campaign-addressables.json",
                      "scriptBindingsManifest": "Assets/QuestOriginalCampaign/script-bindings.json",
                      "managedScriptBindings": script_audit, "missingReferences": references,
                      "unresolvedAddressables": [row for row in manifest["entries"] if row["status"] not in ("associated", "serialized-value-location-excluded")],
                      "shaders": shader_rows, "officialTmpRestorations": restored_tmp, "originalDerivedFiles": copied,
                      "serializedLayoutRestoration": layout_report, "canonicalGuidRestoration": {
                          "assetCount": len(guid_proof["mappings"]), "originalObjectCount": guid_proof["mappedOriginalObjectCount"],
                          "rejectedWitnessCount": len(guid_proof["rejected"])},
                      "readiness": {"originalSceneClosureStaged": len(scenes) == 13,
                                    "fullOriginalCatalogRecovered": True, "unityImportVerified": False,
                                    "androidPlayerBuilt": False, "faithfulGraphicsVerified": False,
                                    "playableCampaignVerified": False},
                      "limits": ["Asset recovery is not a hardware or rendering validation.",
                                 "Original custom shader instruction streams require complete portable shader reconstruction."]}
            report.update(native_report)
            report["files"] = _output_inventory(output, proofs)
            write_json(output / "quest-startup-report.json", report)
            write_json(output / "quest-campaign-report.json", report)
            return report
        report = journal.run("report", 13, _report)
        return report
