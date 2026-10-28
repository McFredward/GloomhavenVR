"""Deliver supplied loose menu relief alongside unchanged Android mod banks.

No shader, environment, original game or depth-model producer runs here. The
existing bank ZIP and its signed file inventory stay authoritative; copy their
qualified compressed records and append the finite current numeric Story set.
"""
from __future__ import annotations

import copy
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import stat
import zipfile


def _installation():
    spec = importlib.util.spec_from_file_location("quest_menu_content_installation",
                                                Path(__file__).with_name("installation_manifest.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _read(builder, path):
    path = builder._ordinary_owned(Path(path))
    if not 0 < path.stat().st_size <= 8 * 1048576:
        raise builder.BuildError("Menu Story delivery metadata is missing or excessive.")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise builder.BuildError("Menu Story delivery metadata must be an object.")
    return value


def _qualify_archive(builder, output, archive, expected_sha256, storage_abi):
    """Persist actual native file proofs, never a signed hash without a read.

    The selected resource module captured this exact storage ABI during its
    isolated load. Reliable physical identity/change stamps retain the measured
    archive across processes; changed or unsupported metadata requires bytes.
    Keep the optional database inside the output, even for borrowed archives.
    """
    database = Path(output) / "cache/menu-depth-content-witnesses.sqlite3"
    safe = True
    try:
        builder._ordinary_owned(database)
        for path in (database, database.with_name(database.name + "-wal"), database.with_name(database.name + "-shm")):
            builder._ordinary_owned(path)
            if path.exists():
                info = path.lstat()
                if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > 8 * 1048576:
                    safe = False
        if safe:
            database.parent.mkdir(parents=True, exist_ok=True)
    except (OSError, builder.BuildError):
        safe = False

    def qualify(connection):
        witnesses = storage_abi.ValidatedFileWitnesses(connection, archive.parent,
            {"schema": 1, "owner": "Quest retained menu mod archive", "sha256": expected_sha256})
        result = witnesses.qualify(archive, expected_sha256, archive.stat().st_size, hasher=builder.digest)
        connection.commit()
        return result

    if safe:
        try:
            with closing(sqlite3.connect(database, timeout=30)) as connection:
                return qualify(connection)
        except (sqlite3.DatabaseError, OSError):
            # An optional damaged/unwritable cache never supplies a byte proof.
            # Preserve it and fall back to actual current archive qualification.
            pass
    with closing(sqlite3.connect(":memory:")) as connection:
        return qualify(connection)


def augment(builder, output, source, archive, content_manifest, bundle_receipt, *, source_files=None, resources=None):
    """Return closed (archive, content manifest, bank receipt) for either caller.

    `resources` is captured during the Builder's isolated import. Do not import
    a fresh globally aliased storage/profile dependency after that import ends.
    Input archive SHA qualification and the existing stage's strong witnesses
    retain exact byte proof without repeating an unchanged bank/depth sweep.
    """
    resources = resources or getattr(builder, "menu_depth_resources", None)
    if resources is None:
        raise builder.BuildError("Menu Story delivery needs the selected captured resource producer.")
    storage_abi = getattr(resources, "_storage", None)
    if storage_abi is None:
        raise builder.BuildError("Menu Story delivery needs its captured native file-witness ABI.")
    contract = _installation()
    packer = contract._native_packer()
    archive = builder._ordinary_owned(Path(archive))
    content = copy.deepcopy(content_manifest)
    bundles = copy.deepcopy(bundle_receipt)
    try:
        if not isinstance(content, dict) or not contract._hash(content.get("inputKey")) or not isinstance(bundles, dict):
            raise ValueError("existing mod identity/receipt is invalid")
        contract._validate_inventory(content, content.get("inputKey"), "quest-mod-content.zip", False, packer)
        banks, prior_story = contract.mod_file_groups(content["files"])
        receipt_banks = bundles.get("bundles", [bundles.get("bundle")])
        if (not isinstance(receipt_banks, list) or not receipt_banks
                or banks != [{**row, "path": "StreamingAssets/" + row["path"]} for row in receipt_banks]
                or bundles.get("bundle") != receipt_banks[0]):
            raise ValueError("existing bank receipt differs from its content inventory")
        if not _qualify_archive(builder, output, archive, content["archiveSha256"], storage_abi):
            raise ValueError("existing archive SHA differs from its signed inventory")
        with zipfile.ZipFile(archive) as retained:
            packer.verify_entries(retained, content["files"])
            if prior_story:
                contract.validate_story_rows(prior_story, retained.read(contract.STORY_MANIFEST))
        story = resources.story_records(source, source_files)
        # Use the same signed canonical namespace accepted by the installer.
        contract.mod_file_groups([*banks, *story])
    except (ValueError, KeyError, TypeError, OSError, zipfile.BadZipFile) as error:
        raise builder.BuildError("Menu Story delivery cannot retain an unqualified mod archive: " + str(error)) from error
    story_identity = {"schema": 1, "files": story, "contentKey": builder.value_hash(story)}
    if prior_story == story and bundles.get("menuDepthStory") == story_identity:
        return archive, content, bundles
    identity = {"scope": "quest-loose-menu-story-v1", "archive": content, "bundles": bundles,
                "story": story, "producerSha256": builder.digest(Path(__file__)),
                "resourceProducerSha256": builder.digest(Path(resources.__file__))}
    key = builder.value_hash(identity)
    root = builder._ordinary_owned(Path(output) / "cache/menu-depth-content" / key)
    result_archive = root / "quest-mod-content.zip"
    manifest_path, bundles_path = root / "quest-mod-content.json", root / "quest-mod-bundles.json"

    def produce():
        root.mkdir(parents=True, exist_ok=True)
        temporary = builder._ordinary_owned(result_archive.with_suffix(".zip.partial"))
        try:
            with zipfile.ZipFile(archive) as retained, zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED,
                                                                     compresslevel=1, allowZip64=True) as destination:
                packer.verify_entries(retained, content["files"])
                for row in banks:
                    packer.copy_compressed_entry(retained, destination, retained.getinfo(row["path"]))
                resources.write_story(destination, source, story)
            if not _qualify_archive(builder, output, archive, content["archiveSha256"], storage_abi):
                raise builder.BuildError("Retained mod archive changed during loose Story publication.")
            current = {**content, "files": [*banks, *story], "archiveSha256": builder.digest(temporary)}
            current_bundles = {**bundles, "menuDepthStory": story_identity}
            with zipfile.ZipFile(temporary) as delivered:
                packer.verify_entries(delivered, current["files"])
                contract.validate_story_rows(story, delivered.read(contract.STORY_MANIFEST))
            os.replace(temporary, result_archive)
            builder.write_json(manifest_path, current)
            builder.write_json(bundles_path, current_bundles)
        finally:
            temporary.unlink(missing_ok=True)
        return [result_archive, manifest_path, bundles_path], {
            "scope": identity["scope"], "inputKey": content["inputKey"],
            "storyKey": story_identity["contentKey"], "retainedBankFiles": banks,
            "originalGameRebuilt": False, "artBanksRebuilt": False, "depthInferred": False}

    builder.Stages(output).run("menu-depth-content", key, produce)
    return result_archive, _read(builder, manifest_path), _read(builder, bundles_path)
