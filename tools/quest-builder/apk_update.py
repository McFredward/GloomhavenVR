"""Code/profile updates of an owned Quest APK without importing original game assets.

The IL2CPP binary and metadata are inseparable. A compact Unity player supplies
their matching code sidecars; original serialized payloads are retained only
after every original MonoScript identity and serialized-property hash matches.
Unchanged ZIP records are copied compressed, not inflated and recompressed.
"""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile
import zipfile
from xml.sax.saxutils import escape

from storage import BuildError, digest, value_hash, write_json, _ordinary_owned
from profile import validate_identity, ProfileError

CAPSULE = "assets/Quest/update-capsule.json"
UPDATE = "assets/Quest/update-manifest.json"
PROFILE = "assets/Quest/offline-profile.json"
INPUT = "assets/Quest/input-manifest.json"
INSTALLATION = "assets/Quest/installation-manifest.json"
LIBRARY = "lib/arm64-v8a/libil2cpp.so"
METADATA = "assets/bin/Data/Managed/Metadata/global-metadata.dat"
INITIALIZERS = "assets/bin/Data/RuntimeInitializeOnLoads.json"
ASSEMBLIES = "assets/bin/Data/ScriptingAssemblies.json"
PROTON_HELPERS = {"lib/arm64-v8a/libQuestApparance.so", "lib/arm64-v8a/libquest_proton.so"}
PROTON_CONTRACT = "assets/Quest/procedural-native.json"
MANAGED_RESOURCES = "assets/bin/Data/Managed/Resources/"
MAX_JSON = 8 * 1024 * 1024
MAX_MEMBER = 1024 * 1024 * 1024
REPLACED_PACKAGES = {"UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager", "Unity.ScriptableBuildPipeline",
                     "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils"}
_SHA = re.compile(r"[0-9a-f]{64}")
_UNITY = re.compile(r"20\d{2}\.\d+\.\d+[abfp]\d+")
_resource_proofs = set()
_resource_value_proofs = {}
_UINT32_SENTINEL = 0xffffffff
_MAX_ZIP_DIRECTORY = 64 * 1024 * 1024


class RebuildRequired(BuildError):
    """A valid old player needs asset migration, not a code-only replacement.

    The update driver may hand this exact compatibility outcome to the regular
    cache-aware builder. Invalid ownership, signing evidence and incomplete
    compiler outputs deliberately remain ordinary BuildError instances.
    """


def _base_stamp(path):
    s = Path(path).stat()
    return str(Path(path).absolute()), s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns


def _external_member(path):
    if path == "Library/unity default resources": path = "unity default resources"
    _safe_name(path)
    return "assets/bin/Data/" + path


def _safe_name(name):
    if (not isinstance(name, str) or not name or "\\" in name or ":" in name
            or any(c in name for c in "\0\r\n") or name.startswith("/")
            or any(p in ("", ".", "..") for p in name.rstrip("/").split("/"))):
        raise BuildError("APK contains an unsafe member path.")
    return name


def _entries(apk):
    result, folded = {}, set()
    for entry in apk.infolist():
        name = _safe_name(entry.filename)
        if name.casefold() in folded or entry.flag_bits & 1 or entry.file_size > MAX_MEMBER:
            raise BuildError("APK contains duplicate, encrypted or excessive members: " + name)
        folded.add(name.casefold()); result[name] = entry
    return result


def _json(apk, name, *, optional=False):
    entries = _entries(apk)
    if optional and name not in entries: return None
    if name not in entries or not 0 < entries[name].file_size <= MAX_JSON:
        raise BuildError("APK lacks one bounded signed contract: " + name)
    try:
        value = json.loads(apk.read(entries[name]))
    except (UnicodeError, ValueError, zipfile.BadZipFile) as exc:
        raise BuildError("APK signed JSON contract is unreadable: " + name) from exc
    if not isinstance(value, dict): raise BuildError("APK contract must be an object: " + name)
    return value


def _json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")


def _native(apk):
    entries = _entries(apk)
    if LIBRARY not in entries or METADATA not in entries:
        raise BuildError("An ARM64 IL2CPP Quest APK is required for this update.")
    with apk.open(LIBRARY) as stream: header = stream.read(64)
    if len(header) < 64 or header[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", header, 18)[0] != 183:
        raise BuildError("The existing IL2CPP player is not little-endian ARM64 ELF.")
    with apk.open(METADATA) as stream: header = stream.read(8)
    if len(header) != 8 or struct.unpack_from("<I", header)[0] != 0xFAB11BAF:
        raise BuildError("The existing IL2CPP metadata header is invalid.")
    return struct.unpack_from("<I", header, 4)[0]


def inspect_base(path, *, validate_update_contracts=True):
    """Bounded signed metadata for discovery; does not import assets or hash a bank."""
    try:
        with zipfile.ZipFile(path) as apk:
            metadata_version = _native(apk)
            manifest = _json(apk, INPUT); installation = _json(apk, INSTALLATION)
            key = manifest.get("inputKey")
            if (manifest.get("schema") != 1 or manifest.get("target") != "game" or not _SHA.fullmatch(key or "")
                    or installation.get("schema") != 1 or installation.get("inputKey") != key):
                raise BuildError("A complete Campaign APK with matching signed game identity is required.")
            capsule = _json(apk, CAPSULE, optional=True) if validate_update_contracts else None
            if capsule is not None:
                _validate_capsule(capsule, manifest)
                if capsule["metadataVersion"] != metadata_version:
                    raise BuildError("Signed APK capsule does not match the actual IL2CPP metadata format.")
            update = _json(apk, UPDATE, optional=True) if validate_update_contracts else None
            if update is not None and (update.get("schema") != 1 or update.get("gameInputKey") != key
                    or not _SHA.fullmatch(update.get("updateKey", "")) or type(update.get("modBuild")) is not int):
                raise BuildError("Signed APK update provenance is invalid.")
            return {"inputKey": key, "modBuild": update["modBuild"] if update else manifest.get("mod", {}).get("modBuild"),
                    "unityVersion": capsule.get("unityVersion") if capsule else None,
                    "manifest": manifest, "installation": installation,
                    "capsule": capsule, "update": update, "legacy": capsule is None,
                    "currentMod": update.get("mod", manifest.get("mod")) if update else manifest.get("mod"),
                    "currentProfile": update.get("profile", manifest.get("profile")) if update else manifest.get("profile"),
                    "dynamicProfile": bool(capsule and capsule.get("dynamicProfile"))}
    except (OSError, zipfile.BadZipFile) as exc:
        raise BuildError("The existing Quest APK could not be read.") from exc


def _managed(manifest):
    rows = manifest.get("game", {}).get("files", [])
    result = []
    for row in rows:
        if isinstance(row, dict) and str(row.get("path", "")).startswith("Managed/") and row["path"].endswith(".dll"):
            _safe_name(row["path"])
            if not _SHA.fullmatch(row.get("sha256", "")) or type(row.get("size")) is not int or row["size"] <= 0:
                raise BuildError("APK original managed-assembly evidence is invalid.")
            result.append(dict(row))
    if (not {"Managed/GH.Runtime.dll", "Managed/GH.Shared.dll"}.issubset({r["path"] for r in result})
            or len({r["path"].casefold() for r in result}) != len(result)):
        raise BuildError("APK has no unambiguous original game assembly contract; use a full build.")
    return sorted(result, key=lambda r: r["path"])


def preflight_owned_game(apk_path, game_data):
    """Qualify the locally owned PC assembly inputs against the original APK."""
    base = inspect_base(apk_path); root = Path(game_data).absolute()
    rows = _managed(base["manifest"])
    for row in rows:
        path = root / row["path"]
        for parent in (path, *path.parents):
            if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
                raise BuildError("Owned PC game validation rejects linked assembly inputs.")
        if not path.is_file() or path.stat().st_size != row["size"] or digest(path) != row["sha256"]:
            raise BuildError("The PC game does not match this APK's original assembly: " + row["path"] + "; use the matching PC installation or create a full build.")
    return {"schema": 1, "inputKey": base["inputKey"], "managedFiles": rows,
            "managedKey": value_hash(rows), "ownedGameAssembliesQualified": True}


def _hash128(value):
    if not isinstance(value, dict) or set(value) != {"bytes[" + str(i) + "]" for i in range(16)}:
        raise BuildError("Unity MonoScript property hash is invalid.")
    try: return bytes(value["bytes[" + str(i) + "]"] for i in range(16)).hex()
    except (TypeError, ValueError) as exc: raise BuildError("Unity MonoScript hash bytes are invalid.") from exc


def _script_conflict(key, first, current):
    """Keep the exact conflicting locations useful in bounded support logs."""
    fields = [name for name in ("propertiesHash", "executionOrder") if first["row"][name] != current["row"][name]]
    def candidate(value):
        return {"member": value["member"][:256], "fileIDs": value["fileIDs"][:8], "pathID": value["pathID"],
                "propertiesHash": value["row"]["propertiesHash"], "executionOrder": value["row"]["executionOrder"]}
    detail = {"identity": [part[:256] for part in key], "fields": fields,
              "candidates": [candidate(first), candidate(current)]}
    return BuildError("APK has conflicting MonoScript identities: " + json.dumps(detail, ensure_ascii=True, separators=(",", ":")))


def _admit_script_rows(candidates, expected_types):
    expected = {(row["assembly"], row["namespace"], row["class"]): row for row in expected_types or ()}
    roster, retained_orders = {}, 0
    for key, values in sorted(candidates.items()):
        first = values[0]
        conflict = next((item for item in values[1:] if item["row"] != first["row"]), None)
        if conflict is None:
            roster[key] = first["row"]
            continue
        # Support205558 reaches this gate AFTER the native compiler succeeded.
        # Compact preloaded roots and Unity's own resources can register the same
        # managed identity more than once. Only execution order may be resolved
        # against the exact retained original: its MonoManager/MonoScripts remain
        # in the published APK, and the compact registry is never transplanted.
        # Every physical candidate must prove the original serialized layout;
        # accepting one matching hash among incompatible copies would conceal a
        # genuine property-layout change. New ambiguous types have no authority.
        original = expected.get(key)
        if (original is not None and any(item["row"] == original for item in values)
                and all(item["row"]["propertiesHash"] == original["propertiesHash"] for item in values)):
            roster[key] = original
            retained_orders += 1
            continue
        # Prefer a hash difference in the diagnostic even if an order-only
        # duplicate appeared earlier; otherwise the relevant ABI defect is lost.
        conflict = next((item for item in values[1:] if item["row"]["propertiesHash"] != first["row"]["propertiesHash"]), conflict)
        raise _script_conflict(key, first, conflict)
    return roster, retained_orders


def script_roster(apk_path, *, expected_types=None):
    """Read player MonoScripts, never the external game bank or texture payloads.

    expected_types is used only after a compact native pair was qualified. It
    admits order-only duplicate registrations against the retained player, never
    conflicting serialized property hashes or an arbitrary first/last candidate.
    """
    import UnityPy
    candidates, version = {}, None
    with zipfile.ZipFile(apk_path) as apk:
        entries = _entries(apk)
        manager_name = "assets/bin/Data/globalgamemanagers"
        if manager_name not in entries:
            raise BuildError("APK does not expose original player script registry; use a full build.")
        manager = UnityPy.load(apk.read(manager_name)); manager_file = next(iter(manager.files.values()))
        version = manager_file.unity_version
        mono = [obj for obj in manager.objects if obj.type.name == "MonoManager"]
        if len(mono) != 1: raise BuildError("APK requires one unambiguous native MonoManager.")
        required = {}
        for ptr in mono[0].read_typetree().get("m_Scripts", []):
            index, path_id = ptr["m_FileID"], ptr["m_PathID"]
            if not path_id: raise BuildError("APK native script registry contains an empty pointer.")
            if index < 0 or index > len(manager_file.externals): raise BuildError("APK native script registry has an invalid external file index.")
            name = _external_member(manager_file.externals[index - 1].path) if index else manager_name
            required.setdefault(name, {}).setdefault(path_id, set()).add(index)
        if not required: raise BuildError("APK native MonoManager has no original script registrations.")
        for name, path_ids in sorted(required.items()):
            if name not in entries: raise BuildError("APK native script registry references a missing player file: " + name)
            env = UnityPy.load(apk.read(name))
            for file in env.files.values():
                current = getattr(file, "unity_version", None)
                if current:
                    # Unity 2021.3.5 ships its own default resources serialized
                    # by 2021.3.2. Their exact script property hashes still join
                    # the roster; the actual player manager owns editor identity.
                    if name != "assets/bin/Data/unity default resources" and current != version:
                        raise BuildError("APK player uses mixed Unity serialized formats.")
            objects = {obj.path_id: obj for obj in env.objects}
            for path_id in sorted(path_ids):
                obj = objects.get(path_id)
                if obj is None or obj.type.name != "MonoScript": raise BuildError("APK native script registration does not resolve to a MonoScript.")
                item = obj.read_typetree()
                row = {"assembly": item["m_AssemblyName"], "namespace": item["m_Namespace"], "class": item["m_ClassName"],
                       "propertiesHash": _hash128(item["m_PropertiesHash"]), "executionOrder": item["m_ExecutionOrder"]}
                key = row["assembly"], row["namespace"], row["class"]
                candidates.setdefault(key, []).append({"row": row, "member": name,
                    "fileIDs": sorted(path_ids[path_id]), "pathID": path_id})
    roster, retained_orders = _admit_script_rows(candidates, expected_types)
    if not roster or not _UNITY.fullmatch(version or ""): raise BuildError("APK original script roster/Unity version is missing.")
    if retained_orders:
        print("[Quest code update] retained original execution orders for " + str(retained_orders)
              + " duplicate MonoScript identities; every physical serialized property hash matches.", flush=True)
    return {"unityVersion": version, "scriptTypes": [roster[key] for key in sorted(roster)],
            "retainedOrderDuplicateTypes": retained_orders}


def make_capsule(apk_path, *, package_abi, dynamic_profile=False, repair_update_contracts=False, payload_producer=None):
    """Capture compatibility from an actual player, not a compiler success claim."""
    base = inspect_base(apk_path, validate_update_contracts=not repair_update_contracts); registry = script_roster(apk_path)
    if not _SHA.fullmatch(package_abi or ""): raise BuildError("Quest package/template ABI fingerprint is missing.")
    with zipfile.ZipFile(apk_path) as apk: metadata_version = _native(apk)
    result = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": base["inputKey"],
            "unityVersion": registry["unityVersion"], "packageAbi": package_abi,
            "metadataVersion": metadata_version, "managedKey": value_hash(_managed(base["manifest"])),
            "scriptTypes": registry["scriptTypes"], "modSchema": 1, "dynamicProfile": bool(dynamic_profile)}
    if payload_producer is not None:
        _validate_payload_producer(payload_producer, base["manifest"])
        result["payloadProducer"] = payload_producer
    return result


def _validate_payload_producer(value, manifest):
    """A signed normalized digest belongs to this original source inventory."""
    if (not isinstance(value, dict) or set(value) != {"schema", "scope", "source", "normalizer", "sha256"}
            or value.get("schema") != 1 or value.get("scope") != "quest-builder-producer-ast-v1"
            or not isinstance(value.get("sha256"), str) or not _SHA.fullmatch(value["sha256"])):
        raise BuildError("Signed APK payload producer fingerprint is invalid or incomplete.")
    files = manifest.get("mod", {}).get("files", [])
    if not isinstance(files, list):
        raise BuildError("Signed APK payload producer source provenance is missing.")
    for field, name in (("source", "tools/quest-builder/builder.py"), ("normalizer", "tools/quest-builder/preparation_identity.py")):
        row = value[field]
        if (not isinstance(row, dict) or set(row) != {"path", "size", "sha256"} or row.get("path") != name
                or type(row.get("size")) is not int or not 0 < row["size"] <= 2 * 1048576
                or not isinstance(row.get("sha256"), str) or not _SHA.fullmatch(row["sha256"])
                or [item for item in files if isinstance(item, dict) and item.get("path") == name] != [row]):
            raise BuildError("Signed APK payload producer source provenance does not match the original inventory.")


def _validate_capsule(value, manifest):
    if (value.get("schema") != 1 or value.get("scope") != "quest-code-update-v1" or value.get("inputKey") != manifest.get("inputKey")
            or value.get("modSchema") != 1 or not _UNITY.fullmatch(value.get("unityVersion", ""))
            or not _SHA.fullmatch(value.get("packageAbi", "")) or value.get("managedKey") != value_hash(_managed(manifest))
            or type(value.get("metadataVersion")) is not int or type(value.get("dynamicProfile")) is not bool
            or not isinstance(value.get("scriptTypes"), list) or not 0 < len(value["scriptTypes"]) <= 32768):
        raise BuildError("Signed APK update capsule is incompatible or incomplete; use a full build.")
    if "payloadProducer" in value:
        _validate_payload_producer(value["payloadProducer"], manifest)
    keys = set()
    for row in value["scriptTypes"]:
        if (not isinstance(row, dict) or set(row) != {"assembly", "namespace", "class", "propertiesHash", "executionOrder"}
                or not all(isinstance(row.get(k), str) and len(row[k]) <= 1024 for k in ("assembly", "namespace", "class"))
                or not row["assembly"].endswith(".dll") or not row["class"]
                or not re.fullmatch(r"[0-9a-f]{32}", row.get("propertiesHash", "")) or type(row.get("executionOrder")) is not int):
            raise BuildError("Signed APK script roster contains an invalid serialized type.")
        key = row["assembly"], row["namespace"], row["class"]
        if key in keys: raise BuildError("Signed APK script roster repeats a serialized type.")
        keys.add(key)


def _stage_disabled_package_references(project, managed_dir):
    """Retain the full player's disabled original-package deployment boundary.

    Support143919: weaving legitimately emits UnityEngine.UI.dll, but the compact
    project omitted its reference plugin. Deployment then failed before SDK
    compilation. As in the full player, these copies retain intermediate woven
    outputs only; actual Unity package source supplies the compiled assemblies.
    Never add these references to the runtime asmdef or package-API input census.
    Reconcile this bounded set even for an already ready pre-repair project;
    preserve its regular woven plugins and expensive Unity/Bee Library.
    """
    plugins = _ordinary_owned(Path(project) / "Assets/Plugins/QuestGame")
    for name in sorted(REPLACED_PACKAGES):
        original = Path(managed_dir) / (name + ".dll")
        if not original.is_file():
            continue  # Only original packages actually present in the owned game.
        target = _ordinary_owned(plugins / original.name)
        metadata = _ordinary_owned(plugins / (original.name + ".meta"))
        if not target.is_file():
            plugins.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
        importer = ("fileFormatVersion: 2\nguid: " + hashlib.sha256(("quest-code-plugin:" + original.name).encode()).hexdigest()[:32] +
            "\nPluginImporter:\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n"
            "  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 1\n"
            "  validateReferences: 1\n  platformData:\n  - first:\n      Any:\n    second:\n"
            "      enabled: 0\n      settings: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
        if not metadata.is_file() or metadata.read_text(encoding="utf-8") != importer:
            metadata.write_text(importer, encoding="utf-8")


def stage_code_project(repo, project, base, managed_dir, *, profile, version_code, version_name, package_abi, packages_manifest=None, code_key=None,
                       retain_compatible_library=False):
    """Stage only runtime code, managed plugins and their script-reference roster.

    The caller may initially supply original PC DLLs and deploy its woven/bound
    replacements afterwards. No original exported assets, game scenes, data
    conversion or shared Unity Library is used.
"""
    repo, project, managed_dir = Path(repo), Path(project), Path(managed_dir)
    for ancestor in (project.absolute(), *project.absolute().parents):
        if ancestor.is_symlink() or (hasattr(ancestor, "is_junction") and ancestor.is_junction()):
            raise BuildError("Code-only project rejects linked output directories.")
    metadata = inspect_base(base)
    capsule = metadata["capsule"] or make_capsule(base, package_abi=package_abi)
    if capsule["packageAbi"] != package_abi:
        raise RebuildRequired("Quest package/template ABI changed; create a full build.")
    template = repo / "unity/GloomhavenVR.Quest"
    producer_files = [*sorted((template / "Assets/Quest/Runtime").glob("*.cs")),
                      template / "Assets/Quest/Editor/QuestCodeUpdateBuild.cs", repo / "src/GloomhavenVR/Core/Loc/QuestText.cs"]
    source_key = value_hash([{ "path": p.relative_to(repo).as_posix(), "sha256": digest(p)} for p in producer_files])
    owner = {"schema": 1, "gameInputKey": metadata["inputKey"], "codeKey": code_key or source_key,
             "sourceKey": source_key, "profileKey": value_hash(profile), "packageAbi": package_abi,
             "versionCode": version_code, "versionName": version_name, "packagesKey": value_hash(packages_manifest)}
    marker = project / "QuestCodeUpdate/code-project-owner.json"
    if marker.is_file():
        previous = json.loads(marker.read_bytes())
        matching = previous.get("owner") == owner
        prior_owner = previous.get("owner", {})
        compatible = (retain_compatible_library and isinstance(prior_owner, dict)
            and prior_owner.get("schema") == owner["schema"] and prior_owner.get("gameInputKey") == owner["gameInputKey"]
            and prior_owner.get("packageAbi") == owner["packageAbi"] and previous.get("state") in ("ready", "staging"))
        if not matching and not compatible:
            raise BuildError("Code-only project belongs to a different update; retain it and use this update's own output folder.")
        if matching and previous.get("state") == "ready":
            _stage_disabled_package_references(project, managed_dir)
            return {"project": str(project), "capsule": capsule, "gameInputKey": metadata["inputKey"],
                    "originalAssetsImported": False, "reused": True}
        if previous.get("state") not in ("ready", "staging"): raise BuildError("Code-only project owner state is invalid.")
        # Same owned game/compiler ABI, new mod: refresh only the small code
        # tree. Unity/Bee owns invalidation of changed native objects in Library.
        # This does not adopt code/build receipts from the previous update.
        for name in ("Assets", "Packages", "ProjectSettings", "Library"):
            path = project / name
            if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
                raise BuildError("Code-only project cannot reuse a linked code or compiler directory.")
        for name in ("Assets", "Packages", "ProjectSettings"):
            path = project / name
            if path.exists(): shutil.rmtree(path)
    elif project.exists() and any(project.iterdir()):
        raise BuildError("The code-only project has no matching owned staging receipt; use an empty private directory.")
    project.mkdir(parents=True, exist_ok=True)
    write_json(marker, {"owner": owner, "state": "staging"})
    shutil.copytree(template / "ProjectSettings", project / "ProjectSettings")
    shutil.copytree(template / "Packages", project / "Packages")
    if packages_manifest is not None: write_json(project / "Packages/manifest.json", packages_manifest)
    runtime = project / "Assets/Quest/Runtime"
    runtime.mkdir(parents=True)
    for path in sorted((template / "Assets/Quest/Runtime").glob("*.cs")): shutil.copyfile(path, runtime / path.name)
    text = (repo / "src/GloomhavenVR/Core/Loc/QuestText.cs").read_text(encoding="utf-8")
    (runtime / "QuestText.cs").write_text(text, encoding="utf-8")
    # Runtime defines, package API binding and plugin references are the same as
    # the full player. Only the Editor build helper differs.
    plugins = project / "Assets/Plugins/QuestGame"; plugins.mkdir(parents=True)
    with zipfile.ZipFile(base) as original:
        assemblies = _json(original, ASSEMBLIES)
    if (not isinstance(assemblies.get("names"), list) or not isinstance(assemblies.get("types"), list)
            or len(assemblies["names"]) != len(assemblies["types"])):
        raise BuildError("Original player assembly classification is invalid.")
    # Original plugins are distinct from the player's SDK/BCL assemblies.
    # Namespace prefixes cannot distinguish SpatialTracking or Unsafe from
    # engine modules/framework DLLs. Preserve the original plugin classification.
    original_plugins = {name for name, kind in zip(assemblies["names"], assemblies["types"]) if kind == 16}
    dlls = sorted(p for p in managed_dir.glob("*.dll") if p.stem not in REPLACED_PACKAGES
                  and not (p.name.startswith("UnityEngine.") and p.name.endswith("Module.dll"))
                  and (not p.name.startswith("System.") or p.name in original_plugins)
                  and p.name not in ("UnityEngine.dll", "mscorlib.dll", "netstandard.dll", "System.dll"))
    if not {"GH.Runtime.dll", "GH.Shared.dll"}.issubset({p.name for p in dlls}):
        raise BuildError("Code update requires the owned original game managed plugin set.")
    for dll in dlls:
        shutil.copyfile(dll, plugins / dll.name)
        meta = plugins / (dll.name + ".meta")
        if dll.with_suffix(".dll.meta").is_file():
            shutil.copyfile(dll.with_suffix(".dll.meta"), meta)
        else:
            orders = [r for r in capsule["scriptTypes"] if r["assembly"] == dll.name and r["executionOrder"]]
            order_text = "  executionOrder:\n" + ''.join("    " + json.dumps((r["namespace"] + "." if r["namespace"] else "") + r["class"]) + ": " + str(r["executionOrder"]) + "\n" for r in orders) if orders else "  executionOrder: {}\n"
            meta.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(("quest-code-plugin:" + dll.name).encode()).hexdigest()[:32] +
                "\nPluginImporter:\n  serializedVersion: 2\n  iconMap: {}\n" + order_text +
                "  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: " + ("1" if dll.name == "GH.Runtime.dll" else "0") +
                "\n  validateReferences: 1\n  platformData:\n  - first:\n      Any:\n    second:\n      enabled: 1\n      settings: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    _stage_disabled_package_references(project, managed_dir)
    write_json(runtime / "QuestGame.Campaign.asmdef", {"name": "QuestGame.Campaign", "rootNamespace": "GloomhavenVR.Quest",
        "references": ["Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager", "UnityEngine.UI", "Unity.TextMeshPro", "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils"],
        "overrideReferences": True, "precompiledReferences": [p.name for p in dlls], "autoReferenced": True,
        "defineConstraints": ["GHVR_QUEST_GAME"], "allowUnsafeCode": False})
    editor = project / "Assets/Quest/Editor"; editor.mkdir(parents=True)
    shutil.copyfile(template / "Assets/Quest/Editor/QuestCodeUpdateBuild.cs", editor / "QuestCodeUpdateBuild.cs")
    # Preserve all plugin bodies and generic AOT roots while the old scenes are
    # absent. Engine stripping must stay disabled for the retained native player.
    link = '<linker>\n' + ''.join('  <assembly fullname="' + escape(p.stem, {'"': '&quot;'}) + '" preserve="all"/>\n' for p in dlls) + '</linker>\n'
    (project / "Assets/link.xml").write_text(link, encoding="utf-8")
    write_json(project / "QuestCodeUpdate/input.json", {"schema": 1, "unityVersion": capsule["unityVersion"],
        "packageAbi": package_abi, "versionCode": version_code, "versionName": version_name,
        "scriptTypes": capsule["scriptTypes"]})
    write_json(project / "Assets/StreamingAssets/Quest/offline-profile.json", profile)
    write_json(project / "Assets/Quest/Resources/quest-profile.json", profile)
    write_json(marker, {"owner": owner, "state": "ready"})
    return {"project": str(project), "capsule": capsule, "gameInputKey": metadata["inputKey"],
            "originalAssetsImported": False, "managedAssemblies": [p.name for p in dlls], "reused": False}


def code_build_env(project, apk, *, package="dev.gloomhavenvr.quest"):
    return {"GHVR_QUEST_CODE_INPUT": str(Path(project) / "QuestCodeUpdate/input.json"),
            "GHVR_QUEST_CODE_OUTPUT": str(apk), "GHVR_QUEST_PACKAGE": package}


def update_android_versions(base, version_code, version_name, *, package="dev.gloomhavenvr.quest"):
    # Bind this sibling once without relying on the wizard's restored global
    # dependency aliases after its isolated builder import has completed.
    from_update = _manifest_module
    with zipfile.ZipFile(base) as apk:
        return from_update.update_versions(apk.read("AndroidManifest.xml"), version_code, version_name, package=package)


_manifest_spec = importlib.util.spec_from_file_location("_quest_apk_manifest", Path(__file__).with_name("apk_manifest.py"))
_manifest_module = importlib.util.module_from_spec(_manifest_spec)
_manifest_spec.loader.exec_module(_manifest_module)


def _required_types(old, new):
    current = {(r["assembly"], r["namespace"], r["class"]): r for r in new}
    for row in old:
        key = row["assembly"], row["namespace"], row["class"]
        if current.get(key) != row:
            raise RebuildRequired("The mod update changes or removes a serialized script: " + ":".join(key) + "; create a full build to migrate its assets.")


def _callbacks(value):
    if not isinstance(value.get("root"), list): raise BuildError("Player startup callback registry is invalid.")
    result = set()
    for row in value["root"]:
        try: key = tuple(row[k] for k in ("assemblyName", "nameSpace", "className", "methodName", "loadTypes", "isUnityClass"))
        except (KeyError, TypeError) as exc: raise BuildError("Player startup callback is incomplete.") from exc
        result.add(key)
    return result


def collect_code_update(base, compiled_apk, compiled_receipt, *, package_abi):
    """Accept a matching native pair and sidecars only after player ABI checks."""
    old = inspect_base(base); capsule = old["capsule"] or make_capsule(base, package_abi=package_abi)
    receipt = json.loads(Path(compiled_receipt).read_bytes()) if not isinstance(compiled_receipt, dict) else compiled_receipt
    if (receipt.get("schema") != 1 or receipt.get("scope") != "quest-code-update-v1"
            or receipt.get("buildResult") != "Succeeded" or receipt.get("originalAssetsImported") is not False
            or receipt.get("unityVersion") != capsule["unityVersion"] or receipt.get("packageAbi") != package_abi
            or package_abi != capsule["packageAbi"] or receipt.get("abi") != "arm64-v8a"):
        raise BuildError("The compact Unity code build is incompatible or incomplete.")
    # Qualification must precede an asset-migration verdict. An unrelated or
    # corrupted native pair must not consume the user's retained full project
    # merely because its script/metadata registry differs from the old player.
    pair = {}
    with zipfile.ZipFile(compiled_apk) as compiled:
        entries = _entries(compiled)
        for name in (LIBRARY, METADATA):
            rows = [row for row in receipt.get("files", []) if isinstance(row, dict) and row.get("path") == name]
            if name not in entries or len(rows) != 1:
                raise BuildError("Compact native output lacks one closed compiler receipt: " + name)
            actual = compiled.read(name)
            if rows[0].get("size") != len(actual) or rows[0].get("sha256") != hashlib.sha256(actual).hexdigest():
                raise BuildError("Compact native output differs from its closed compiler receipt: " + name)
            pair[name] = actual
    registry = script_roster(compiled_apk, expected_types=capsule["scriptTypes"])
    if registry["unityVersion"] != capsule["unityVersion"]: raise BuildError("Code update Unity version changed.")
    _required_types(capsule["scriptTypes"], registry["scriptTypes"])
    replacements = dict(pair)
    with zipfile.ZipFile(base) as original, zipfile.ZipFile(compiled_apk) as compiled:
        if _native(compiled) != capsule["metadataVersion"]: raise RebuildRequired("IL2CPP metadata ABI changed; use a full build.")
        old_assemblies, new_assemblies = _json(original, ASSEMBLIES), _json(compiled, ASSEMBLIES)
        def assembly_map(value, source):
            names, types = value.get("names"), value.get("types")
            def invalid(reason, **detail):
                context = {"source": source, "member": ASSEMBLIES, "reason": reason, **detail}
                raise BuildError("Player assembly registry is invalid: " + json.dumps(context, ensure_ascii=True, separators=(",", ":")))
            if not isinstance(names, list) or not isinstance(types, list):
                invalid("names-and-types-must-be-arrays")
            if not names or len(names) != len(types):
                invalid("array-lengths", names=len(names), types=len(types))
            result, indices = {}, {}
            for index, (name, kind) in enumerate(zip(names, types)):
                try:
                    valid_name = (isinstance(name, str) and 4 < len(name.encode("utf-8")) <= 1024
                                  and name.endswith(".dll") and name == name.strip()
                                  and not any(ord(c) < 32 or ord(c) == 127 or c in "/\\:" for c in name))
                except UnicodeError:
                    valid_name = False
                if not valid_name:
                    invalid("assembly-name", index=index, value=repr(name)[:256])
                if type(kind) is not int or not 0 <= kind <= 0x7fffffff:
                    invalid("classification", index=index, name=name[:256], value=repr(kind)[:256])
                if name in result and result[name] != kind:
                    invalid("conflicting-classifications", name=name[:256], indices=[indices[name], index], types=[result[name], kind])
                # The pinned Unity2021 player contains repeated registrations
                # under the same name/classification. Compare that exact
                # relation once; never deduplicate or reorder the emitted JSON,
                # whose arrays remain the compiler's ordered player registry.
                if name not in result:
                    result[name], indices[name] = kind, index
            return result
        old_map, new_map = assembly_map(old_assemblies, "original"), assembly_map(new_assemblies, "compiled")
        if any(new_map.get(name) != kind for name, kind in old_map.items()):
            raise RebuildRequired("Code build removed or reclassified an original player assembly; use a full build.")
        callbacks = _callbacks(_json(compiled, INITIALIZERS))
        for row in _callbacks(_json(original, INITIALIZERS)):
            if row[0] not in ("GloomhavenVR", "QuestGame.Campaign", "Assembly-CSharp") and row not in callbacks:
                raise BuildError("Code build lost an original game/package startup callback: " + str(row[:4]))
        if not any(r[:4] == ("QuestGame.Campaign", "GloomhavenVR.Quest", "QuestOfflineProfile", "Initialize") for r in callbacks):
            raise BuildError("Code build lacks the signed offline-profile startup boundary.")
        entries = _entries(compiled)
        previous_resources = {n for n in _entries(original) if n.startswith(MANAGED_RESOURCES) and n.endswith("-resources.dat")}
        if not previous_resources.issubset(entries):
            raise RebuildRequired("Code build removed original managed resource sidecars; create a full build.")
        names = [LIBRARY, METADATA, INITIALIZERS, ASSEMBLIES] + sorted(n for n in entries if n.startswith(MANAGED_RESOURCES) and n.endswith("-resources.dat"))
        for name in names:
            if name not in replacements: replacements[name] = compiled.read(name)
    # The original registry is the authority for retained game data. New mod
    # types need no original serialized references; old types must stay exact.
    capsule = {**capsule, "dynamicProfile": True}
    replacements[CAPSULE] = _json_bytes(capsule)
    return {"replacements": replacements, "capsule": capsule, "nativePairQualified": True,
            "serializedOriginalTypesQualified": len(capsule["scriptTypes"]), "gameInputKey": old["inputKey"],
            "replacementSha256": {name: hashlib.sha256(body).hexdigest() for name, body in replacements.items()}}


def profile_update(base, profile):
    old = inspect_base(base)
    if not old["dynamicProfile"]:
        raise BuildError("This older APK has compiled profile constants. Run one mod/code update first; later profile updates need no Unity build.")
    # Keep the current signed DLC/content scope; a profile patch cannot create
    # new game content or grant a store service.
    if not isinstance(profile, dict) or set(profile) - {"schema", "provider", "providerId", "steamId", "accountId", "displayName", "source", "logoSha256", "isDummy", "dlcOwnership"}:
        raise BuildError("Offline profile contains unexpected fields; credentials are never APK update inputs.")
    name = profile.get("displayName")
    if (profile.get("schema") != 1 or not isinstance(name, str) or not name.strip() or len(name.encode("utf-8")) > 256
            or any(ord(c) < 32 or ord(c) == 127 for c in name)):
        raise BuildError("Offline profile display identity is invalid.")
    selected = {k: v for k, v in profile.items() if k not in ("dlcOwnership", "isDummy")}
    if profile.get("isDummy") is True:
        if (profile.get("provider") != "steam" or profile.get("steamId") != "0" or profile.get("accountId") != 0
                or "DUMMY" not in profile.get("displayName", "").upper()):
            raise BuildError("Offline dummy profile must be explicitly labelled and use ID zero.")
    else:
        try: validate_identity(selected)
        except ProfileError as exc: raise BuildError(str(exc)) from exc
    ownership = profile.get("dlcOwnership")
    if ownership is not None:
        id = profile.get("providerId", profile.get("steamId"))
        apps = ownership.get("installedAppIds") if isinstance(ownership, dict) else None
        if (not isinstance(ownership, dict) or ownership.get("schema") != 1 or ownership.get("provider") != profile.get("provider")
                or ownership.get("appId") != 780290 or ownership.get("providerId", ownership.get("steamId")) != id
                or not isinstance(apps, list) or any(type(app) is not int or app not in (1809490, 1958560, 2584170) for app in apps)
                or len(set(apps)) != len(apps) or type(ownership.get("ownedMask")) is not int
                or ownership["ownedMask"] != sum({1809490: 1, 1958560: 2, 2584170: 4}[app] for app in apps)):
            raise BuildError("Offline profile DLC ownership is inconsistent with the selected account.")
    raw = _json_bytes(profile)
    if len(raw) > 4096: raise BuildError("Offline profile exceeds its runtime bound.")
    return {PROFILE: raw}


def patch_resource_json(base, name, value=None, *, replacements=None):
    """Patch existing Resources TextAssets while retaining every other object body.

Pass one name/value, or a name→value mapping. Supplying previously returned
    replacements allows multiple callers to compose patches to resources.assets.
    """
    import UnityPy
    updates = name if isinstance(name, dict) and value is None else {name: value}
    if not updates or set(updates) - {"quest-build", "quest-profile", "quest-mod-content", "quest-mod-bundles", "quest-procedural-native"}:
        raise BuildError("Unsupported Quest JSON TextAsset update scope.")
    result = dict(replacements or {})
    with zipfile.ZipFile(base) as apk:
        manager = UnityPy.load(apk.read("assets/bin/Data/globalgamemanagers")); manager_file = next(iter(manager.files.values()))
        resources = [obj for obj in manager.objects if obj.type.name == "ResourceManager"]
        if len(resources) != 1: raise BuildError("Quest requires one native ResourceManager.")
        groups, found = {}, set()
        for key, ptr in resources[0].read_typetree().get("m_Container", []):
            if key not in updates: continue
            if key in found: raise BuildError("Quest JSON resource path is ambiguous: " + key)
            found.add(key); index = ptr["m_FileID"]
            if not 0 < index <= len(manager_file.externals) or not ptr["m_PathID"]:
                raise BuildError("Quest JSON resource has an invalid external pointer.")
            member = _external_member(manager_file.externals[index - 1].path)
            if not _resource_member(member): raise BuildError("Quest JSON resource is outside its safe player asset scope.")
            groups.setdefault(member, {})[key] = ptr["m_PathID"]
        if found != set(updates): raise BuildError("Quest JSON resource is missing from the original native registry.")
        for member, selected in groups.items():
            raw = result[member] if member in result else apk.read(member)
            inherited = dict(_resource_value_proofs.get((_base_stamp(base), member, hashlib.sha256(raw).hexdigest()), {}))
            env = UnityPy.load(raw)
            if len(env.files) != 1: raise BuildError("Quest Resources must be one serialized player file.")
            file = next(iter(env.files.values())); objects = {obj.path_id: obj for obj in env.objects}
            originals = {key: obj.get_raw_data() for key, obj in objects.items()}
            for key, path_id in selected.items():
                obj = objects.get(path_id)
                if obj is None or obj.type.name != "TextAsset": raise BuildError("Quest JSON resource pointer does not resolve to a TextAsset.")
                fields = obj.read_typetree()
                if fields.get("m_Name") != key: raise BuildError("Quest JSON resource pointer names the wrong object.")
                fields["m_Script"] = _json_bytes(updates[key]).decode("utf-8"); obj.save_typetree(fields)
            cooked = file.save(); check = UnityPy.load(cooked); after = {obj.path_id: obj for obj in check.objects}
            if set(after) != set(originals): raise BuildError("Quest Resources object registry changed unexpectedly.")
            for key, body in originals.items():
                if key not in selected.values() and after[key].get_raw_data() != body:
                    raise BuildError("A non-target Quest Resources object changed.")
            for key, path_id in selected.items():
                actual = after[path_id].read_typetree()["m_Script"]
                if isinstance(actual, bytes): actual = actual.decode("utf-8")
                if json.loads(actual) != updates[key]: raise BuildError("Quest TextAsset update failed its readback.")
            result[member] = cooked
            proof_key = (_base_stamp(base), member, hashlib.sha256(cooked).hexdigest())
            _resource_proofs.add(proof_key)
            # Later build/profile patches may share this serialized file. Carry
            # forward only values proven in these exact input bytes; untargeted
            # object bodies above remain byte-identical through composition.
            inherited.update({key: hashlib.sha256(_json_bytes(updates[key])).hexdigest() for key in selected})
            _resource_value_proofs[proof_key] = inherited
    return result


def _resource_member(name):
    return name == "assets/bin/Data/resources.assets" or re.fullmatch(r"assets/bin/Data/[0-9a-f]{32}", name) is not None


def _replaceable(name):
    return name in {LIBRARY, METADATA, INITIALIZERS, ASSEMBLIES, CAPSULE, UPDATE, PROFILE,
                    "AndroidManifest.xml", INSTALLATION, "assets/quest-mod-content.zip", PROTON_CONTRACT} or name in PROTON_HELPERS or _resource_member(name) or (name.startswith(MANAGED_RESOURCES) and name.endswith("-resources.dat"))


def _qualify_proton_helpers(base, info, replacements, update_manifest, evidence):
    """Admit only the reviewed helper pair, with its actual old/new byte proof."""
    touched = set(replacements) & (PROTON_HELPERS | {PROTON_CONTRACT})
    if not touched:
        if evidence is not None:
            raise BuildError("Native helper evidence has no matching replacement pair.")
        return
    if touched != PROTON_HELPERS | {PROTON_CONTRACT}:
        raise BuildError("Proton helper replacements require the complete pair and native contract.")
    if (not isinstance(evidence, dict) or evidence.get("schema") != 1
            or evidence.get("scope") != "proton-nls-helper-update-v1"
            or evidence.get("backend") != "proton-arm64ec-fex"
            or evidence.get("androidDeviceExecutionVerified") is not False
            or any(evidence.get(key) is not False for key in
                   ("wineBinaryChanged", "winePayloadChanged", "originalGameAssetsRebuilt"))
            or update_manifest.get("nativeHelpers") != evidence):
        raise BuildError("Proton helper replacements lack their qualified static update evidence.")
    spec = importlib.util.spec_from_file_location("quest_apk_proton_helper", Path(__file__).with_name("proton_helper_update.py"))
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    original = info["manifest"]
    previous = (info.get("update") or {}).get("mod", original["mod"])["files"]
    current = update_manifest.get("mod", {}).get("files", [])
    allowed = helper.changed_rows(current, previous, original.get("proceduralBackend"))
    if not allowed:
        raise BuildError("Proton helper source transition is not the exact reviewed repair.")
    selected = {row["path"]: row for row in current}
    preceding = {row["path"]: row for row in previous}
    rows = evidence.get("sourceFiles")
    expected_paths = {helper.PREFIX + name for name in helper.SOURCE_NAMES}
    if (not isinstance(rows, list) or len(rows) != len(expected_paths)
            or {row.get("path") for row in rows if isinstance(row, dict)} != expected_paths
            or any(row != selected.get(row["path"]) for row in rows)
            or any(row != preceding.get(row["path"]) for row in rows if row["path"] not in allowed)):
        raise BuildError("Proton helper evidence differs from its unchanged native ABI inputs.")
    with zipfile.ZipFile(base) as archive:
        old = _json(archive, PROTON_CONTRACT)
        old_rows = {row["path"]: row for row in old.get("files", [])}
        actual_previous = []
        native_rows = evidence.get("nativeFiles", [])
        prefix = "Assets/Quest/Plugins/Android/arm64-v8a/"
        if (not isinstance(native_rows, list) or len(native_rows) != 2
                or not all(isinstance(row, dict) for row in native_rows)
                or {row.get("path") for row in native_rows} !=
                {prefix + Path(name).name for name in PROTON_HELPERS}):
            raise BuildError("Proton helper evidence has an invalid program inventory.")
        for row in native_rows:
            member = "lib/arm64-v8a/" + Path(row["path"]).name
            body = replacements[member]
            if (not isinstance(body, bytes) or not 64 <= len(body) <= 4 * 1024 * 1024
                    or body[:7] != b"\x7fELF\x02\x01\x01" or struct.unpack_from("<HHI", body, 16) != (3, 183, 1)
                    or row.get("size") != len(body) or row.get("sha256") != hashlib.sha256(body).hexdigest()):
                raise BuildError("Qualified Proton helper bytes changed before publication: " + member)
            prior = old_rows.get(row["path"])
            entry = archive.getinfo(member)
            if (prior is None or not 64 <= entry.file_size <= 4 * 1024 * 1024
                    or entry.file_size != prior.get("size")
                    or hashlib.sha256(archive.read(entry)).hexdigest() != prior.get("sha256")):
                raise BuildError("Source Proton helper differs from its signed native contract: " + member)
            actual_previous.append(prior)
    prior_rows = evidence.get("previousNativeFiles")
    if (not isinstance(prior_rows, list) or len(prior_rows) != 2
            or not all(isinstance(row, dict) and isinstance(row.get("path"), str) for row in prior_rows)
            or sorted(prior_rows, key=lambda row: row["path"]) != sorted(actual_previous, key=lambda row: row["path"])):
        raise BuildError("Proton helper evidence names a different source program pair.")
    updated = copy.deepcopy(old)
    replacements_by_path = {row["path"]: row for row in native_rows}
    updated["files"] = [replacements_by_path.get(row["path"], row) for row in old["files"]]
    updated["helperUpdate"] = evidence
    if json.loads(replacements[PROTON_CONTRACT]) != updated:
        raise BuildError("Repaired Proton native contract changed unrelated program records.")
    expected = hashlib.sha256(_json_bytes(updated)).hexdigest()
    if not any(_resource_value_proofs.get((_base_stamp(base), member, hashlib.sha256(body).hexdigest()), {}).get(
            "quest-procedural-native") == expected for member, body in replacements.items() if _resource_member(member)):
        raise BuildError("Repaired Proton Resources contract lacks its exact composed TextAsset proof.")


def _signature(name):
    return name.startswith("META-INF/") and (name == "META-INF/MANIFEST.MF" or name.upper().endswith((".SF", ".RSA", ".DSA", ".EC")))


class _ApkZip32(zipfile.ZipFile):
    """Write Android's ZIP32 directory, including valid offsets above 2 GiB.

    Capture075701 fails in zipalign after a successful Unity Player build. The
    original 2.7 GiB Player is ZIP32, but CPython's conservative 2 GiB threshold
    changes its copied central entries and end record to ZIP64. Android 30.0.2
    zipalign rejects that archive. Use the actual unsigned ZIP32 limits in this
    writer only; never alter the process-global reader/writer ZIP64 threshold.
    """

    def _write_end_record(self):
        count = len(self.filelist)
        # All-ones size/offset fields are ZIP64 sentinels, not ordinary values.
        if count >= 0xffff or not 0 <= self.start_dir < 0xffffffff:
            raise BuildError("APK exceeds Android ZIP32 limits; the completed Player is retained.")
        for info in self.filelist:
            if (info.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                    or any(not 0 <= n < 0xffffffff for n in
                           (info.file_size, info.compress_size, info.header_offset))):
                raise BuildError("APK member exceeds Android ZIP32 limits: " + info.filename)
            extra = bytearray(); cursor = 0
            while cursor < len(info.extra):
                if cursor + 4 > len(info.extra):
                    raise BuildError("APK central extra field is truncated: " + info.filename)
                kind, size = struct.unpack_from("<HH", info.extra, cursor)
                end = cursor + 4 + size
                if end > len(info.extra):
                    raise BuildError("APK central extra field is truncated: " + info.filename)
                if kind != 1: extra.extend(info.extra[cursor:end])
                cursor = end
            filename, flags = info._encodeFilenameFlags()
            year, month, day, hour, minute, second = info.date_time
            date = (year - 1980) << 9 | month << 5 | day
            time = hour << 11 | minute << 5 | second // 2
            self.fp.write(struct.pack("<4s4B4H3I5H2I", b"PK\x01\x02",
                info.create_version, info.create_system, info.extract_version, info.reserved,
                flags, info.compress_type, time, date, info.CRC, info.compress_size,
                info.file_size, len(filename), len(extra), len(info.comment), 0,
                info.internal_attr, info.external_attr, info.header_offset))
            self.fp.write(filename); self.fp.write(extra); self.fp.write(info.comment)
        size = self.fp.tell() - self.start_dir
        if size >= 0xffffffff or self.fp.tell() + 22 + len(self._comment) >= 0xffffffff:
            raise BuildError("APK exceeds Android ZIP32 limits; the completed Player is retained.")
        self.fp.write(struct.pack("<4s4H2IH", b"PK\x05\x06", 0, 0, count, count,
                                  size, self.start_dir, len(self._comment)))
        self.fp.write(self._comment)
        if self.mode == "a": self.fp.truncate()
        self.fp.flush()


def _local_record(source, info, boundary):
    """Read one bounded ZIP32 header/descriptor without reading its payload."""
    source.seek(info.header_offset); header = source.read(30)
    if len(header) != 30 or header[:4] != b"PK\x03\x04": raise BuildError("APK local ZIP header is invalid.")
    fields = struct.unpack("<4s5H3I2H", header)
    if fields[2] != info.flag_bits or fields[3] != info.compress_type: raise BuildError("APK local and central ZIP headers disagree.")
    if fields[7] == _UINT32_SENTINEL or fields[8] == _UINT32_SENTINEL:
        raise BuildError("APK local ZIP64 sizes are unsupported by Android signing: " + info.filename)
    if (fields[1] >= 45 or info.flag_bits & ~0x080e
            or info.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)):
        raise BuildError("APK local ZIP method, flags or version are unsupported: " + info.filename)
    if (info.header_offset < 0 or info.header_offset + 30 + fields[-2] + fields[-1] > boundary
            or not fields[-2]):
        raise BuildError("APK local ZIP record is truncated: " + info.filename)
    local_name = source.read(fields[-2])
    try: name = local_name.decode("utf-8" if info.flag_bits & 0x800 else "cp437")
    except UnicodeError as exc: raise BuildError("APK local member path is unreadable.") from exc
    if name != info.filename:
        raise BuildError("APK local and central member paths disagree.")
    extra = source.read(fields[-1]); data_start = source.tell()
    if len(extra) != fields[-1] or data_start + info.compress_size > boundary:
        raise BuildError("APK local ZIP record is truncated: " + info.filename)
    expected = (info.CRC, info.compress_size, info.file_size)
    if any(not 0 <= n < _UINT32_SENTINEL for n in expected[1:]):
        raise BuildError("APK local ZIP64 sizes are unsupported by Android signing: " + info.filename)
    if info.compress_type == zipfile.ZIP_STORED and info.compress_size != info.file_size:
        raise BuildError("APK stored member sizes disagree: " + info.filename)
    actual = fields[6:9]
    matches = all(n in (0, expected[i]) for i, n in enumerate(actual)) if info.flag_bits & 8 else actual == expected
    if not matches:
        raise BuildError("APK local and central member CRC/sizes disagree: " + info.filename)
    record_end = data_start + info.compress_size
    if info.flag_bits & 8:
        source.seek(record_end)
        descriptor = source.read(min(16, max(0, boundary - record_end)))
        # ZIP32 permits descriptors with or without the optional signature.
        # An unsigned descriptor's real CRC can itself equal that signature.
        if (len(descriptor) == 16 and descriptor[:4] == b"PK\x07\x08"
                and struct.unpack_from("<III", descriptor, 4) == expected):
            record_end += 16
        elif len(descriptor) >= 12 and struct.unpack_from("<III", descriptor) == expected:
            record_end += 12
        else:
            raise BuildError("APK ZIP32 data descriptor is invalid: " + info.filename)
    return header, local_name, extra, data_start, record_end


def _aligned_extra(offset, name_length, extra, filename, method):
    if method != zipfile.ZIP_STORED: return extra
    alignment = 16384 if filename.endswith(".so") else 4
    padding = -(offset + 30 + name_length + len(extra)) % alignment
    if len(extra) + padding > 0xffff:
        raise BuildError("APK local alignment exceeds its ZIP32 extra-field bound: " + filename)
    # Android zipalign appends zero bytes to the local extra field only. Keep
    # the original central extra untouched, including its real metadata.
    return extra + bytes(padding)


def _copy_raw_record(source, target, info, *, boundary=None):
    if boundary is None:
        source.seek(0, 2); boundary = source.tell()
    header, local_name, extra, data_start, record_end = _local_record(source, info, boundary)
    extra = _aligned_extra(target.tell(), len(local_name), extra, info.filename, info.compress_type)
    header = bytearray(header); struct.pack_into("<H", header, 28, len(extra))
    target.write(header); target.write(local_name); target.write(extra)
    source.seek(data_start); remaining = record_end - data_start
    while remaining:
        block = source.read(min(1024 * 1024, remaining))
        if not block: raise BuildError("APK local ZIP record is truncated.")
        target.write(block); remaining -= len(block)


def _zip32_directory(source, size):
    """Find a normal APK end record using 64-bit offsets, even on Windows."""
    if not 22 <= size < _UINT32_SENTINEL:
        raise BuildError("APK exceeds Android ZIP32 limits or has no ZIP end record.")
    source.seek(max(0, size - 22 - 0xffff)); tail_start = source.tell(); tail = source.read(22 + 0xffff)
    offset = len(tail)
    while True:
        offset = tail.rfind(b"PK\x05\x06", 0, offset)
        if offset < 0: raise BuildError("APK ZIP32 end record is missing or truncated.")
        if offset + 22 <= len(tail):
            fields = struct.unpack_from("<4s4H2IH", tail, offset)
            if offset + 22 + fields[-1] == len(tail): break
    end_position = tail_start + offset
    _, disk, directory_disk, disk_count, count, directory_size, directory_offset, _ = fields
    if (disk or directory_disk or disk_count != count or not 0 < count < 0xffff
            or directory_size == _UINT32_SENTINEL or directory_offset == _UINT32_SENTINEL):
        raise BuildError("APK ZIP64 or split ZIP directories are unsupported by Android signing.")
    if (not 46 * count <= directory_size <= _MAX_ZIP_DIRECTORY
            or directory_offset + directory_size != end_position):
        raise BuildError("APK ZIP32 central directory bounds are invalid.")
    if end_position >= 20:
        source.seek(end_position - 20)
        if source.read(4) == b"PK\x06\x07":
            raise BuildError("APK ZIP64 directory is unsupported by Android signing.")
    return count, directory_offset, end_position


def check_alignment(path, *, shared_library_alignment=16384):
    """Check APK ZIP32 bounds/alignment from headers, never inflate/hash bodies.

    Capture 085642's Windows 30.0.2 zipalign uses 32-bit C ftell/fseek and rejects
    a legal ZIP32 APK above 2 GiB before reading its directory. Aligning during
    the existing compressed-record copy avoids that tool and a second whole
    APK copy. New outputs use 16-KiB .so alignment; existing signed 4-KiB outputs
    can be qualified without rewriting by passing shared_library_alignment=4096.
    Android's contract: https://developer.android.com/tools/zipalign
    """
    if shared_library_alignment not in (4096, 16384):
        raise BuildError("Unsupported APK shared-library alignment requirement.")
    try:
        with Path(path).open("rb") as source:
            size = os.fstat(source.fileno()).st_size
            count, directory_offset, directory_end = _zip32_directory(source, size)
            cursor, stored, libraries = directory_offset, 0, 0
            seen, ranges = set(), []
            for _ in range(count):
                source.seek(cursor); header = source.read(46)
                if len(header) != 46 or header[:4] != b"PK\x01\x02":
                    raise BuildError("APK central ZIP header is invalid.")
                fields = struct.unpack("<4s6H3I5H2I", header)
                name_size, extra_size, comment_size, disk = fields[10:14]
                cursor += 46 + name_size + extra_size + comment_size
                if cursor > directory_end or not name_size or disk:
                    raise BuildError("APK central member bounds or disk are invalid.")
                name_bytes = source.read(name_size); extra = source.read(extra_size)
                if len(name_bytes) != name_size or len(extra) != extra_size:
                    raise BuildError("APK central member metadata is truncated.")
                try: name = name_bytes.decode("utf-8" if fields[3] & 0x800 else "cp437")
                except UnicodeError as exc: raise BuildError("APK central member path is unreadable.") from exc
                _safe_name(name)
                if name.casefold() in seen: raise BuildError("APK has duplicate central member paths: " + name)
                seen.add(name.casefold())
                info = zipfile.ZipInfo(name)
                info.extract_version, info.flag_bits, info.compress_type = fields[2:5]
                info.CRC, info.compress_size, info.file_size = fields[7:10]
                info.header_offset = fields[-1]
                if (info.extract_version >= 45 or info.header_offset == _UINT32_SENTINEL
                        or info.compress_size == _UINT32_SENTINEL or info.file_size == _UINT32_SENTINEL):
                    raise BuildError("APK ZIP64 member is unsupported by Android signing: " + name)
                extra_cursor = 0
                while extra_cursor < len(extra):
                    if extra_cursor + 4 > len(extra): raise BuildError("APK central extra field is truncated: " + name)
                    kind, length = struct.unpack_from("<HH", extra, extra_cursor)
                    extra_cursor += 4 + length
                    if kind == 1: raise BuildError("APK ZIP64 member is unsupported by Android signing: " + name)
                    if extra_cursor > len(extra): raise BuildError("APK central extra field is truncated: " + name)
                _, _, _, data_start, record_end = _local_record(source, info, directory_offset)
                if info.compress_type == zipfile.ZIP_STORED:
                    stored += 1
                    alignment = shared_library_alignment if name.endswith(".so") else 4
                    if name.endswith(".so"): libraries += 1
                    if data_start % alignment:
                        raise BuildError("APK stored member is not aligned to " + str(alignment) + " bytes: " + name)
                ranges.append((info.header_offset, record_end))
            if cursor != directory_end: raise BuildError("APK central directory count disagrees with its bounds.")
            end = 0
            for start, stop in sorted(ranges):
                if start < end: raise BuildError("APK local ZIP records overlap.")
                end = stop
            return {"schema": 1, "format": "apk-zip32-aligned-v1", "entries": count,
                    "storedEntries": stored, "sharedLibraries": libraries, "size": size,
                    "storedAlignment": 4, "sharedLibraryAlignment": shared_library_alignment}
    except (OSError, ValueError, struct.error) as exc:
        raise BuildError("APK alignment headers could not be read: " + str(exc)) from exc


def repack(base, output, replacements, *, update_manifest, signer, verifier, progress=None, code_evidence=None,
           repair_update_contracts=False, native_helper_evidence=None):
    """Publish a verified signed update atomically, retaining compressed game bytes.

signer(temp_path) and verifier(temp_path) must raise on failure. A verifier may
return its verified certificate digest; malformed returned evidence is rejected.
Neither callback is optional: unsigned output is never an installable result.
"""
    base, output = Path(base).absolute(), Path(output).absolute()
    if base == output: raise BuildError("APK update output must be separate from its input.")
    if not callable(signer) or not callable(verifier): raise BuildError("APK update requires signing and verification callbacks.")
    replacements = dict(replacements)
    if set(replacements) & {LIBRARY, METADATA} and not {LIBRARY, METADATA}.issubset(replacements):
        raise BuildError("IL2CPP library and global metadata must be updated together.")
    info = inspect_base(base, validate_update_contracts=not repair_update_contracts)
    _qualify_proton_helpers(base, info, replacements, update_manifest, native_helper_evidence)
    if LIBRARY in replacements:
        if (not isinstance(code_evidence, dict) or code_evidence.get("nativePairQualified") is not True
                or code_evidence.get("gameInputKey") != info["inputKey"]
                or type(code_evidence.get("serializedOriginalTypesQualified")) is not int
                or code_evidence["serializedOriginalTypesQualified"] <= 0):
            raise BuildError("Native pair replacements require the compact compiler/script-registry qualification.")
        for name, expected in code_evidence.get("replacementSha256", {}).items():
            if name not in replacements or hashlib.sha256(replacements[name]).hexdigest() != expected:
                raise BuildError("A qualified code payload changed before APK publication: " + name)
        if not {LIBRARY, METADATA, INITIALIZERS, ASSEMBLIES, CAPSULE}.issubset(code_evidence.get("replacementSha256", {})):
            raise BuildError("Native code evidence lacks the required matching player sidecars.")
    if (update_manifest.get("schema") != 1 or update_manifest.get("gameInputKey") != info["inputKey"]
            or not _SHA.fullmatch(update_manifest.get("updateKey", ""))):
        raise BuildError("APK update provenance must preserve the original game identity.")
    replacements[UPDATE] = _json_bytes(update_manifest)
    for name, value in replacements.items():
        _safe_name(name)
        if not _replaceable(name) or not isinstance(value, (bytes, bytearray)) or len(value) > MAX_MEMBER:
            raise BuildError("Unsafe APK replacement scope: " + name)
        if _resource_member(name) and (_base_stamp(base), name, hashlib.sha256(value).hexdigest()) not in _resource_proofs:
            raise BuildError("Player Resources replacement lacks exact native-registry/object qualification: " + name)
    output.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary = tempfile.mkstemp(prefix=output.name + ".", suffix=".apk", dir=output.parent); os.close(handle)
    temporary = Path(temporary)
    try:
        with zipfile.ZipFile(base) as original, base.open("rb") as raw, _ApkZip32(temporary, "w", allowZip64=True) as updated:
            entries = _entries(original); total = sum(e.compress_size for n, e in entries.items() if n not in replacements and not _signature(n)); done = 0
            updated.comment = original.comment
            for name, entry in entries.items():
                if name in replacements or _signature(name): continue
                kept = copy.copy(entry); kept.header_offset = updated.fp.tell()
                # ZipFile owns the central directory. This bounded use of its
                # pinned CPython writer avoids payload decompression altogether.
                updated._writecheck(kept); updated._didModify = True
                _copy_raw_record(raw, updated.fp, entry, boundary=original.start_dir)
                updated.filelist.append(kept); updated.NameToInfo[name] = kept; updated.start_dir = updated.fp.tell()
                done += entry.compress_size
                if progress: progress(done, total, name)
            for name, value in replacements.items():
                entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0)); entry.compress_type = zipfile.ZIP_STORED
                # FileHeader/its close-time rewrite use the padded local extra;
                # the directory keeps the original (empty) central extra.
                entry.extra = _aligned_extra(updated.fp.tell(), len(entry._encodeFilenameFlags()[0]),
                                             entry.extra, name, entry.compress_type)
                updated.writestr(entry, value)
                entry.extra = b""
        check_alignment(temporary)
        signer(temporary); verification = verifier(temporary)
        if verification is not None and (not isinstance(verification, dict)
                or set(verification) != {"certificateSha256"}
                or not isinstance(verification.get("certificateSha256"), str)
                or not _SHA.fullmatch(verification["certificateSha256"])):
            raise BuildError("APK verifier returned malformed certificate evidence.")
        with zipfile.ZipFile(temporary) as final:
            actual = _entries(final)
            for name, value in replacements.items():
                if name not in actual or final.read(name) != value: raise BuildError("Signed APK replacement did not survive signing: " + name)
            for name, entry in entries.items():
                if name in replacements or _signature(name): continue
                current = actual.get(name)
                if current is None or (current.CRC, current.file_size, current.compress_size, current.compress_type) != (entry.CRC, entry.file_size, entry.compress_size, entry.compress_type):
                    raise BuildError("An unrelated APK payload changed during signing: " + name)
        # Hash the verified temporary once before publication. Bind that proof
        # to its actual file identity so the caller can record the unchanged
        # output in the normal stage witness without another multi-GiB read.
        verified_stamp = _base_stamp(temporary)
        output_hash = digest(temporary)
        if _base_stamp(temporary) != verified_stamp:
            raise BuildError("Signed APK changed while closing its verified output hash.")
        os.replace(temporary, output)
        output_stamp = _base_stamp(output)
        if output_stamp[1:5] != verified_stamp[1:5]:
            raise BuildError("Signed APK changed during final output publication.")
        return {"schema": 1, "gameInputKey": info["inputKey"], "updateKey": update_manifest["updateKey"],
                "replacedMembers": sorted(replacements), "preservedCompressedBytes": total,
                "output": str(output), "sha256": output_hash, "outputStamp": list(output_stamp),
                **(verification or {})}
    finally:
        temporary.unlink(missing_ok=True)
