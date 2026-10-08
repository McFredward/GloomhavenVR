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

from storage import BuildError, digest, value_hash, write_json
from profile import validate_identity

CAPSULE = "assets/Quest/update-capsule.json"
UPDATE = "assets/Quest/update-manifest.json"
PROFILE = "assets/Quest/offline-profile.json"
INPUT = "assets/Quest/input-manifest.json"
INSTALLATION = "assets/Quest/installation-manifest.json"
LIBRARY = "lib/arm64-v8a/libil2cpp.so"
METADATA = "assets/bin/Data/Managed/Metadata/global-metadata.dat"
INITIALIZERS = "assets/bin/Data/RuntimeInitializeOnLoads.json"
ASSEMBLIES = "assets/bin/Data/ScriptingAssemblies.json"
MANAGED_RESOURCES = "assets/bin/Data/Managed/Resources/"
MAX_JSON = 8 * 1024 * 1024
MAX_MEMBER = 1024 * 1024 * 1024
REPLACED_PACKAGES = {"UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager", "Unity.ScriptableBuildPipeline",
                     "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils", "Unity.TextMeshPro"}
_SHA = re.compile(r"[0-9a-f]{64}")
_UNITY = re.compile(r"20\d{2}\.\d+\.\d+[abfp]\d+")
_resource_proofs = set()


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


def inspect_base(path):
    """Bounded signed metadata for discovery; does not import assets or hash a bank."""
    try:
        with zipfile.ZipFile(path) as apk:
            metadata_version = _native(apk)
            manifest = _json(apk, INPUT); installation = _json(apk, INSTALLATION)
            key = manifest.get("inputKey")
            if (manifest.get("schema") != 1 or manifest.get("target") != "game" or not _SHA.fullmatch(key or "")
                    or installation.get("schema") != 1 or installation.get("inputKey") != key):
                raise BuildError("A complete Campaign APK with matching signed game identity is required.")
            capsule = _json(apk, CAPSULE, optional=True)
            if capsule is not None:
                _validate_capsule(capsule, manifest)
                if capsule["metadataVersion"] != metadata_version:
                    raise BuildError("Signed APK capsule does not match the actual IL2CPP metadata format.")
            update = _json(apk, UPDATE, optional=True)
            if update is not None and (update.get("schema") != 1 or update.get("gameInputKey") != key
                    or not _SHA.fullmatch(update.get("updateKey", "")) or type(update.get("modBuild")) is not int):
                raise BuildError("Signed APK update provenance is invalid.")
            return {"inputKey": key, "modBuild": update["modBuild"] if update else manifest.get("mod", {}).get("modBuild"),
                    "unityVersion": capsule.get("unityVersion") if capsule else None,
                    "manifest": manifest, "installation": installation,
                    "capsule": capsule, "update": update, "legacy": capsule is None,
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


def script_roster(apk_path):
    """Read player MonoScripts, never the external game bank or texture payloads."""
    import UnityPy
    roster, version = {}, None
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
            required.setdefault(name, set()).add(path_id)
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
            for path_id in path_ids:
                obj = objects.get(path_id)
                if obj is None or obj.type.name != "MonoScript": raise BuildError("APK native script registration does not resolve to a MonoScript.")
                item = obj.read_typetree()
                row = {"assembly": item["m_AssemblyName"], "namespace": item["m_Namespace"], "class": item["m_ClassName"],
                       "propertiesHash": _hash128(item["m_PropertiesHash"]), "executionOrder": item["m_ExecutionOrder"]}
                key = row["assembly"], row["namespace"], row["class"]
                if key in roster and roster[key] != row: raise BuildError("APK has conflicting MonoScript identities.")
                roster[key] = row
    if not roster or not _UNITY.fullmatch(version or ""): raise BuildError("APK original script roster/Unity version is missing.")
    return {"unityVersion": version, "scriptTypes": [roster[key] for key in sorted(roster)]}


def make_capsule(apk_path, *, package_abi, dynamic_profile=False):
    """Capture compatibility from an actual player, not a compiler success claim."""
    base = inspect_base(apk_path); registry = script_roster(apk_path)
    if not _SHA.fullmatch(package_abi or ""): raise BuildError("Quest package/template ABI fingerprint is missing.")
    with zipfile.ZipFile(apk_path) as apk: metadata_version = _native(apk)
    return {"schema": 1, "scope": "quest-code-update-v1", "inputKey": base["inputKey"],
            "unityVersion": registry["unityVersion"], "packageAbi": package_abi,
            "metadataVersion": metadata_version, "managedKey": value_hash(_managed(base["manifest"])),
            "scriptTypes": registry["scriptTypes"], "modSchema": 1, "dynamicProfile": bool(dynamic_profile)}


def _validate_capsule(value, manifest):
    if (value.get("schema") != 1 or value.get("scope") != "quest-code-update-v1" or value.get("inputKey") != manifest.get("inputKey")
            or value.get("modSchema") != 1 or not _UNITY.fullmatch(value.get("unityVersion", ""))
            or not _SHA.fullmatch(value.get("packageAbi", "")) or value.get("managedKey") != value_hash(_managed(manifest))
            or type(value.get("metadataVersion")) is not int or type(value.get("dynamicProfile")) is not bool
            or not isinstance(value.get("scriptTypes"), list) or not 0 < len(value["scriptTypes"]) <= 32768):
        raise BuildError("Signed APK update capsule is incompatible or incomplete; use a full build.")
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


def stage_code_project(repo, project, base, managed_dir, *, profile, version_code, version_name, package_abi, packages_manifest=None, code_key=None):
    """Stage only runtime code, managed plugins and their script-reference roster.

    The caller may initially supply original PC DLLs and deploy its woven/bound
    replacements afterwards. No original exported assets, game scenes, data
    conversion or shared Unity Library is used.
"""
    repo, project, managed_dir = Path(repo), Path(project), Path(managed_dir)
    metadata = inspect_base(base)
    capsule = metadata["capsule"] or make_capsule(base, package_abi=package_abi)
    if capsule["packageAbi"] != package_abi:
        raise BuildError("Quest package/template ABI changed; create a full build.")
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
        if previous.get("owner") != owner: raise BuildError("Code-only project belongs to a different update; retain it and use this update's own output folder.")
        if previous.get("state") == "ready":
            return {"project": str(project), "capsule": capsule, "gameInputKey": metadata["inputKey"],
                    "originalAssetsImported": False, "reused": True}
        if previous.get("state") != "staging": raise BuildError("Code-only project owner state is invalid.")
        for name in ("Assets", "Packages", "ProjectSettings"):
            path = project / name
            if path.is_symlink(): raise BuildError("Code-only project cannot regenerate a linked directory.")
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
    text = text.replace("namespace GloomhavenVR.Core", "namespace GloomhavenVR.Quest")
    (runtime / "QuestText.cs").write_text(text, encoding="utf-8")
    # Runtime defines, package API binding and plugin references are the same as
    # the full player. Only the Editor build helper differs.
    plugins = project / "Assets/Plugins/QuestGame"; plugins.mkdir(parents=True)
    dlls = sorted(p for p in managed_dir.glob("*.dll") if p.stem not in REPLACED_PACKAGES
                  and not p.name.startswith(("UnityEngine.", "System.")) and p.name not in ("UnityEngine.dll", "mscorlib.dll", "netstandard.dll", "System.dll"))
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
            raise BuildError("The mod update changes or removes a serialized script: " + ":".join(key) + "; create a full build to migrate its assets.")


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
    registry = script_roster(compiled_apk)
    if registry["unityVersion"] != capsule["unityVersion"]: raise BuildError("Code update Unity version changed.")
    _required_types(capsule["scriptTypes"], registry["scriptTypes"])
    replacements = {}
    with zipfile.ZipFile(base) as original, zipfile.ZipFile(compiled_apk) as compiled:
        if _native(compiled) != capsule["metadataVersion"]: raise BuildError("IL2CPP metadata ABI changed; use a full build.")
        old_assemblies, new_assemblies = _json(original, ASSEMBLIES), _json(compiled, ASSEMBLIES)
        def assembly_map(value):
            names, types = value.get("names"), value.get("types")
            if not isinstance(names, list) or not isinstance(types, list) or len(names) != len(types) or len(set(names)) != len(names):
                raise BuildError("Player assembly registry is invalid.")
            return dict(zip(names, types))
        old_map, new_map = assembly_map(old_assemblies), assembly_map(new_assemblies)
        if any(new_map.get(name) != kind for name, kind in old_map.items()):
            raise BuildError("Code build removed or reclassified an original player assembly; use a full build.")
        callbacks = _callbacks(_json(compiled, INITIALIZERS))
        for row in _callbacks(_json(original, INITIALIZERS)):
            if row[0] not in ("GloomhavenVR", "QuestGame.Campaign", "Assembly-CSharp") and row not in callbacks:
                raise BuildError("Code build lost an original game/package startup callback: " + str(row[:4]))
        if not any(r[:4] == ("QuestGame.Campaign", "GloomhavenVR.Quest", "QuestOfflineProfile", "Initialize") for r in callbacks):
            raise BuildError("Code build lacks the signed offline-profile startup boundary.")
        entries = _entries(compiled)
        previous_resources = {n for n in _entries(original) if n.startswith(MANAGED_RESOURCES) and n.endswith("-resources.dat")}
        if not previous_resources.issubset(entries):
            raise BuildError("Code build removed original managed resource sidecars; create a full build.")
        names = [LIBRARY, METADATA, INITIALIZERS, ASSEMBLIES] + sorted(n for n in entries if n.startswith(MANAGED_RESOURCES) and n.endswith("-resources.dat"))
        for name in names:
            replacements[name] = compiled.read(name)
        for name in (LIBRARY, METADATA):
            rows = receipt.get("files", [])
            row = next((r for r in rows if isinstance(r, dict) and r.get("path") == name), None)
            actual = replacements[name]
            if not isinstance(row, dict) or row.get("size") != len(actual) or row.get("sha256") != hashlib.sha256(actual).hexdigest():
                raise BuildError("Compact native output differs from its closed compiler receipt: " + name)
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
    selected = {k: v for k, v in profile.items() if k not in ("dlcOwnership", "isDummy")}
    if profile.get("isDummy") is True:
        if (profile.get("provider") != "steam" or profile.get("steamId") != "0" or profile.get("accountId") != 0
                or "DUMMY" not in profile.get("displayName", "").upper()):
            raise BuildError("Offline dummy profile must be explicitly labelled and use ID zero.")
    else:
        validate_identity(selected)
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
    if not updates or set(updates) - {"quest-build", "quest-profile", "quest-mod-content", "quest-mod-bundles"}:
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
            _resource_proofs.add((_base_stamp(base), member, hashlib.sha256(cooked).hexdigest()))
    return result


def _resource_member(name):
    return name == "assets/bin/Data/resources.assets" or re.fullmatch(r"assets/bin/Data/[0-9a-f]{32}", name) is not None


def _replaceable(name):
    return name in {LIBRARY, METADATA, INITIALIZERS, ASSEMBLIES, CAPSULE, UPDATE, PROFILE,
                    "AndroidManifest.xml", INSTALLATION, "assets/quest-mod-content.zip"} or _resource_member(name) or (name.startswith(MANAGED_RESOURCES) and name.endswith("-resources.dat"))


def _signature(name):
    return name.startswith("META-INF/") and (name == "META-INF/MANIFEST.MF" or name.upper().endswith((".SF", ".RSA", ".DSA", ".EC")))


def _copy_raw_record(source, target, info):
    source.seek(info.header_offset); header = source.read(30)
    if len(header) != 30 or header[:4] != b"PK\x03\x04": raise BuildError("APK local ZIP header is invalid.")
    fields = struct.unpack("<4s5H3I2H", header)
    if fields[2] != info.flag_bits or fields[3] != info.compress_type: raise BuildError("APK local and central ZIP headers disagree.")
    local_name = source.read(fields[-2])
    if local_name.decode("utf-8" if info.flag_bits & 0x800 else "cp437") != info.filename:
        raise BuildError("APK local and central member paths disagree.")
    length = 30 + fields[-2] + fields[-1] + info.compress_size
    if info.flag_bits & 8:
        source.seek(info.header_offset + length)
        prefix = source.read(4)
        descriptor_size = 16 if prefix == b"PK\x07\x08" else 12
        if info.file_size >= zipfile.ZIP64_LIMIT or info.compress_size >= zipfile.ZIP64_LIMIT:
            descriptor_size += 8
        length += descriptor_size
    source.seek(info.header_offset)
    remaining = length
    while remaining:
        block = source.read(min(1024 * 1024, remaining))
        if not block: raise BuildError("APK local ZIP record is truncated.")
        target.write(block); remaining -= len(block)


def repack(base, output, replacements, *, update_manifest, signer, verifier, progress=None, code_evidence=None):
    """Publish a verified signed update atomically, retaining compressed game bytes.

signer(temp_path) and verifier(temp_path) must raise on failure. Neither is
optional: a rewritten unsigned APK is never exposed as an installable result.
"""
    base, output = Path(base).absolute(), Path(output).absolute()
    if base == output: raise BuildError("APK update output must be separate from its input.")
    if not callable(signer) or not callable(verifier): raise BuildError("APK update requires signing and verification callbacks.")
    replacements = dict(replacements)
    if set(replacements) & {LIBRARY, METADATA} and not {LIBRARY, METADATA}.issubset(replacements):
        raise BuildError("IL2CPP library and global metadata must be updated together.")
    info = inspect_base(base)
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
        with zipfile.ZipFile(base) as original, base.open("rb") as raw, zipfile.ZipFile(temporary, "w", allowZip64=True) as updated:
            entries = _entries(original); total = sum(e.compress_size for n, e in entries.items() if n not in replacements and not _signature(n)); done = 0
            for name, entry in entries.items():
                if name in replacements or _signature(name): continue
                kept = copy.copy(entry); kept.header_offset = updated.fp.tell()
                # ZipFile owns the central directory. This bounded use of its
                # pinned CPython writer avoids payload decompression altogether.
                updated._writecheck(kept); updated._didModify = True
                _copy_raw_record(raw, updated.fp, entry)
                updated.filelist.append(kept); updated.NameToInfo[name] = kept; updated.start_dir = updated.fp.tell()
                done += entry.compress_size
                if progress: progress(done, total, name)
            for name, value in replacements.items():
                entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0)); entry.compress_type = zipfile.ZIP_STORED
                updated.writestr(entry, value)
        signer(temporary); verifier(temporary)
        with zipfile.ZipFile(temporary) as final:
            actual = _entries(final)
            for name, value in replacements.items():
                if name not in actual or final.read(name) != value: raise BuildError("Signed APK replacement did not survive signing: " + name)
            for name, entry in entries.items():
                if name in replacements or _signature(name): continue
                current = actual.get(name)
                if current is None or (current.CRC, current.file_size, current.compress_size, current.compress_type) != (entry.CRC, entry.file_size, entry.compress_size, entry.compress_type):
                    raise BuildError("An unrelated APK payload changed during signing: " + name)
        os.replace(temporary, output)
        return {"schema": 1, "gameInputKey": info["inputKey"], "updateKey": update_manifest["updateKey"],
                "replacedMembers": sorted(replacements), "preservedCompressedBytes": total,
                "output": str(output), "sha256": digest(output)}
    finally:
        temporary.unlink(missing_ok=True)
