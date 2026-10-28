"""Retain the signed original mod bank ZIP for a loose menu Story update.

Only native Resources contracts and the authored mod archive are consumed.
Storage/error classes and the Story producer are the Builder's captured ABI;
isolated imports must not acquire fresh global storage aliases here.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import zipfile

ARCHIVE_MEMBER = "assets/quest-mod-content.zip"
ARCHIVE_NAME = "quest-mod-content.zip"
SCOPE = "quest-retained-mod-content-v1"


def _stamp(path):
    info = path.stat()
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns


def delivery(builder, source, inputs, output, base_apk, base, replacements=None):
    """Return archive/manifest/bank receipt and relative closed-cache inputs.

    Existing code/profile replacements are untouched. Native mod contracts and
    bytes deliberately come from the original signed base, before composition.
    A caller with a separate authored archive must use its own art-delivery path.
    """
    output = builder._ordinary_owned(Path(output))
    base_apk = builder._ordinary_owned(Path(base_apk))
    if replacements and ARCHIVE_MEMBER in replacements:
        raise builder.BuildError("Retained menu Story delivery cannot override a separate authored mod archive.")
    try:
        before = _stamp(base_apk)
        contracts = builder.apk_updates.read_resource_json(base_apk, ("quest-mod-content", "quest-mod-bundles"))
        content, bundles = contracts["quest-mod-content"], contracts["quest-mod-bundles"]
        if content != base["installation"]["mod"]:
            raise builder.BuildError("Native retained mod content differs from the base installation contract.")
        sha = content.get("archiveSha256")
        if content.get("archive") != ARCHIVE_NAME or not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{64}", sha):
            raise builder.BuildError("Retained mod archive identity is invalid.")
        with zipfile.ZipFile(base_apk) as apk:
            entries = builder.apk_updates._entries(apk)
            info = entries.get(ARCHIVE_MEMBER)
            if info is None or info.is_dir() or info.file_size <= 0:
                raise builder.BuildError("Base APK lacks its bounded retained mod archive.")
            size = info.file_size
        if _stamp(base_apk) != before:
            raise builder.BuildError("Base APK changed while reading retained mod contracts.")
        root = builder._ordinary_owned(output / "cache/retained-mod-content" / sha)
        archive = builder._ordinary_owned(root / ARCHIVE_NAME)
        owner_path = builder._ordinary_owned(root / "owner.json")
        owner = {"schema": 1, "scope": SCOPE, "archiveSha256": sha,
                 "files": [{"path": ARCHIVE_NAME, "size": size, "sha256": sha}]}

        def produce():
            root.mkdir(parents=True, exist_ok=True)
            temporary = builder._ordinary_owned(root / (ARCHIVE_NAME + ".partial"))
            try:
                observed, count = hashlib.sha256(), 0
                with zipfile.ZipFile(base_apk) as apk, apk.open(ARCHIVE_MEMBER) as source_stream, temporary.open("wb") as target:
                    while chunk := source_stream.read(1024 * 1024):
                        count += len(chunk)
                        if count > size: raise builder.BuildError("Retained mod archive exceeded its native APK extent.")
                        observed.update(chunk)
                        target.write(chunk)
                    target.flush()
                    os.fsync(target.fileno())
                if count != size or observed.hexdigest() != sha:
                    raise builder.BuildError("Retained mod archive differs from its signed SHA-256 contract.")
                if _stamp(base_apk) != before:
                    raise builder.BuildError("Base APK changed while extracting retained mod content.")
                os.replace(temporary, archive)
                builder.write_json(owner_path, owner)
            finally:
                temporary.unlink(missing_ok=True)
            return [archive, owner_path], {"scope": SCOPE, "archiveSha256": sha,
                "originalGameRebuilt": False, "artBanksRebuilt": False}

        builder.Stages(output).run("retained-mod-content", sha, produce)
        if not 0 < owner_path.stat().st_size <= 4096 or json.loads(owner_path.read_text(encoding="utf-8")) != owner:
            raise builder.BuildError("Retained mod archive has a different cache owner.")
        if _stamp(base_apk) != before:
            raise builder.BuildError("Base APK changed during retained mod delivery.")
        archive, manifest, receipt = builder.menu_depth_content.augment(builder, output, Path(source),
            archive, content, bundles, source_files=inputs["mod"]["files"])
        fast_inputs = set()
        for path in (root, builder._ordinary_owned(Path(archive).parent)):
            relative = path.relative_to(output)
            if not relative.parts or relative.parts[0] != "cache":
                raise builder.BuildError("Menu Story update inputs must remain in their owned output cache.")
            fast_inputs.add(relative.as_posix())
        return archive, manifest, receipt, sorted(fast_inputs)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        raise builder.BuildError("Retained menu Story update could not read its qualified mod archive: " + str(error)) from error
