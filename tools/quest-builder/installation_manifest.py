"""Verify the signed PC-installation contract without a second Campaign byte sweep.

The caller already proves the adjacent bank SHA against signed content delivery.
This gate checks its exact ZIP member metadata and hashes the much smaller nested
mod bank plus its manifested entries. Headset installation validates changed file bytes
before publishing the application-private completion receipt.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import zipfile

MANIFEST = "assets/Quest/installation-manifest.json"
MAXIMUM_MANIFEST_BYTES = 8 * 1024 * 1024
MAXIMUM_FILES = 32768
MAXIMUM_PATH_BYTES = 8192
MAXIMUM_MOD_ARCHIVE_BYTES = 4 * 1024 * 1024 * 1024
LEGACY_MOD_PATHS = ("StreamingAssets/gloomhavenvr.bundle", "StreamingAssets/ghvr-town.bundle", "StreamingAssets/ghvr-town-voices.bundle")
MOD_PATHS = LEGACY_MOD_PATHS + ("StreamingAssets/ghvr-environment.bundle",)


def _native_packer():
    # Loading by sibling path works with the installer's isolated builder import;
    # do not occupy an application's existing global dependency-module aliases.
    spec = importlib.util.spec_from_file_location("_ghvr_installation_packer", Path(__file__).with_name("native_content_pack.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _hash(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _read_manifest(apk):
    matches = [entry for entry in apk.infolist() if entry.filename == MANIFEST]
    if len(matches) != 1 or matches[0].is_dir() or not 0 < matches[0].file_size <= MAXIMUM_MANIFEST_BYTES:
        raise ValueError("Complete Campaign APK lacks one bounded signed PC installation manifest.")
    raw = apk.read(matches[0])
    value = json.loads(raw)
    if not isinstance(value, dict) or set(value) != {"schema", "inputKey", "game", "mod"} or type(value.get("schema")) is not int or value["schema"] != 1:
        raise ValueError("Signed PC installation manifest must declare its exact schema1 contract.")
    return value, raw


def _validate_inventory(value, input_key, archive, external, packer):
    if (not isinstance(value, dict) or type(value.get("schema")) is not int or value["schema"] != 1
            or value.get("inputKey") != input_key or value.get("archive") != archive
            or not _hash(value.get("archiveSha256")) or not isinstance(value.get("files"), list)
            or not 0 < len(value["files"]) <= MAXIMUM_FILES
            or (external and value.get("externalDelivery") is not True)
            or (not external and value.get("externalDelivery", False) is not False)):
        raise ValueError("Signed PC installation inventory identity/scope is invalid: " + archive)
    packer.validate_files(value["files"])
    insensitive = set()
    for row in value["files"]:
        path = row["path"]
        if (not path.startswith("StreamingAssets/") or len(path.encode("utf-8")) > MAXIMUM_PATH_BYTES
                or any(char in path for char in "\0\r\n\t") or path.casefold() in insensitive):
            raise ValueError("Signed PC installation path is unsafe, excessive or duplicated.")
        insensitive.add(path.casefold())
    # Older signed hardware packages remain installable. Current four-bank
    # payloads must preserve their complete entry set through the ZIP gate below.
    if not external and tuple(row["path"] for row in value["files"]) not in (LEGACY_MOD_PATHS, MOD_PATHS):
        raise ValueError("Signed PC mod inventory must preserve its complete native-bank paths/order.")


def validate(apk, input_key, bank, delivery):
    """Validate an open signed APK against its already-hashed adjacent bank."""
    if not _hash(input_key):
        raise ValueError("Signed PC installation input identity is invalid.")
    manifest, raw = _read_manifest(apk)
    if manifest["inputKey"] != input_key:
        raise ValueError("Signed PC installation manifest differs from the selected build.")
    packer = _native_packer()
    _validate_inventory(manifest["game"], input_key, "quest-startup-content.zip", True, packer)
    _validate_inventory(manifest["mod"], input_key, "quest-mod-content.zip", False, packer)
    game, mod = manifest["game"], manifest["mod"]
    if (not isinstance(delivery, dict) or delivery.get("archive") != game["archive"]
            or delivery.get("sha256") != game["archiveSha256"]):
        raise ValueError("Signed PC game inventory differs from signed archive delivery.")
    # This is deliberately metadata-only: validate_apk already hashes this bank,
    # and the native packer's accepted ledger established individual entry SHA.
    with zipfile.ZipFile(bank) as external:
        packer.verify_entries(external, game["files"])
    matches = [entry for entry in apk.infolist() if entry.filename == "assets/quest-mod-content.zip"]
    if len(matches) != 1 or matches[0].is_dir() or not 22 <= matches[0].file_size <= MAXIMUM_MOD_ARCHIVE_BYTES:
        raise ValueError("Signed PC mod archive is missing, duplicated or excessive.")
    # ZIP streams can be compressed inside the APK. Spill to a bounded temporary
    # file for native ZIP seeks rather than retain an entire bank in RAM or
    # repeatedly reinflate the APK member for every nested seek.
    with tempfile.SpooledTemporaryFile(max_size=MAXIMUM_MANIFEST_BYTES, mode="w+b") as staged:
        sha, copied = hashlib.sha256(), 0
        with apk.open(matches[0]) as source:
            for block in iter(lambda: source.read(1024 * 1024), b""):
                copied += len(block)
                if copied > matches[0].file_size:
                    raise ValueError("Signed PC mod archive exceeds its declared size.")
                sha.update(block); staged.write(block)
        if copied != matches[0].file_size or sha.hexdigest() != mod["archiveSha256"]:
            raise ValueError("Signed PC mod archive SHA differs from its inventory.")
        staged.seek(0)
        with zipfile.ZipFile(staged) as nested:
            packer.verify_entries(nested, mod["files"])
            for row in mod["files"]:
                with nested.open(row["path"]) as source:
                    if packer.stream_hash(source) != row["sha256"]:
                        raise ValueError("Signed PC mod entry SHA differs from its inventory: " + row["path"])
    return {"schema": 1, "inputKey": input_key, "manifestSha256": hashlib.sha256(raw).hexdigest(),
            "gameFileCount": len(game["files"]), "modFileCount": len(mod["files"]),
            "gameArchiveSha256": game["archiveSha256"], "modArchiveSha256": mod["archiveSha256"],
            "gameZipMetadataVerified": True, "modEntryBytesVerified": True}
