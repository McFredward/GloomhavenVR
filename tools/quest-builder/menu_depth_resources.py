"""Private Android menu-depth resources from authored code and one owned shader.

The public Builder contains no derived native card bytecode. The first update
recovers only the pinned original GUI/AbilityCard_Shd, then compiles five shaders
in a tiny Android project. Later code updates reuse its closed cache or the
matching embedded resources in the installed APK. Nothing converts game scenes.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import zipfile

import storage as _storage
from storage import BuildError, _ordinary_owned, digest, record_file, value_hash, write_json

BUNDLE_RESOURCE = "GloomhavenVR.MenuDepth.Rendering.bundle"
PROVENANCE_RESOURCE = "GloomhavenVR.MenuDepth.Rendering.provenance.json"
RESOURCE_MEMBER = "assets/bin/Data/Managed/Resources/GloomhavenVR.dll-resources.dat"
BANK = "menu-depth-android.bundle"
PROVENANCE = "menu-depth-android.provenance.json"
RENDERING = "src/GloomhavenVR/Assets/MenuDepth/Rendering/"
STORY = "src/GloomhavenVR/Assets/MenuDepth/Story/"
STORY_DESTINATION = "StreamingAssets/MenuDepth/Story/"
NATIVE_BUNDLE = "StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle"
NATIVE_BUNDLE_SHA = "15b6ac4f91858306e3c6136efbdcf89c6118486dd2191eb27c3c1e9b92018102"
NATIVE_ID = 7314317428712670795
NATIVE_SHA = "fbd49fffa5bb88c6a448a659d87f41b6d2507f37ddd5def25afde34c00f9bb49"
NATIVE_GUID = "36562efa53e6bc540a6e20b7d1c1782f"
NATIVE_NAME = "GUI/AbilityCard_Shd"
DERIVED_NAME = "GloomhavenVR/MenuDepthNativeCard"
NATIVE_ASSET = "Assets/MenuDepth/MenuDepthNativeCard.shader"
SHADERS = ("MenuDepthDisplay.shader", "MenuDepthGraphic.shader", "MenuDepthCharacter.shader",
    "MenuDepthHeadResolve.shader")
EDITOR = "tools/quest-menu-depth/Editor/MenuDepthResourceBuilder.cs"
HELPERS = ("tools/quest-builder/menu_depth_resources.py", "tools/quest-builder/full_shaders.py",
    "tools/quest-builder/campaign_native_shaders.py", "tools/quest-shaders/converters.py",
    "tools/quest-shaders/produce.py", "tools/quest-shaders/manifest.py", "tools/quest-shaders/integer_bits.py",
    "tools/quest-shaders/load_bounds.py", "tools/quest-shaders/instance_nan.py",
    "tools/quest-builder/storage.py", "tools/quest-builder/progress.py", "tools/quest-builder/dependencies.py",
    "tools/quest-builder/requirements.txt", "tools/quest-builder/requirements-bootstrap.txt",
    "tools/quest-builder/requirements-source.txt", "tools/quest-network/requirements.txt",
    "prebuilt/quest-converters-win64-v1.zip")
SHADER_SOURCE_SUFFIXES = {".py", ".cs", ".c", ".cpp", ".h", ".json", ".md", ".txt", ".shader", ".cginc", ".lock"}
GENERATED_PARTS = {"bin", "obj", "Library", "Temp", "Logs", "__pycache__", ".git"}
MAX_BANK = 16 * 1024 * 1024
MAX_RESOURCE = 64 * 1024 * 1024


def _require(value, message):
    if not value:
        raise BuildError("Menu-depth resources: " + message)


def _record(source, name):
    return record_file(_ordinary_owned(Path(source) / name), name)


def plan(source, source_files=None):
    """Stable consumed-input identity, independent of build number/game scenes."""
    # Bind the conservative authored shader-tool closure, including converter
    # locks/vendor source and the public checked Windows converter archive.
    # Generated caches never enter this set; a newly delivered source module
    # cannot silently change an eligible retained bank's transitive producer.
    def shader_source(name):
        path = Path(name)
        return (name.startswith("tools/quest-shaders/") and path.suffix.lower() in SHADER_SOURCE_SUFFIXES
            and not set(path.parts) & GENERATED_PARTS)
    shader_names = ({row["path"] for row in source_files if shader_source(row["path"])} if source_files is not None else
        {path.relative_to(source).as_posix() for path in (Path(source) / "tools/quest-shaders").rglob("*")
            if path.is_file() and shader_source(path.relative_to(source).as_posix())})
    names = sorted({*(RENDERING + name for name in SHADERS), RENDERING + "Unity-LICENSE.txt",
        RENDERING + "NativeCard.provenance.json", EDITOR, *HELPERS, *shader_names})
    if source_files is None:
        records = sorted((_record(source, name) for name in names), key=lambda row: row["path"])
    else:
        known = {row["path"]: row for row in source_files}
        records = [known.get(name) for name in sorted(names)]
        _require(all(isinstance(row, dict) and type(row.get("size")) is int and row["size"] > 0
            and re.fullmatch(r"[0-9a-f]{64}", row.get("sha256", "")) for row in records),
            "producer source is absent or changed")
        # The caller has already qualified this immutable source roster. Planning
        # a compiler-only snapshot must not force unrelated source restoration.
    native_path = Path(source) / RENDERING / "NativeCard.provenance.json"
    if source_files is None or native_path.is_file():
        native = json.loads(_ordinary_owned(native_path).read_text())
        _require((native.get("source_bundle_sha256"), native.get("source_shader_path_id"), native.get("source_shader_sha256"))
            == (NATIVE_BUNDLE_SHA, NATIVE_ID, NATIVE_SHA), "native provenance pins changed; audit the new original first")
    recipe = {"schema": 1, "scope": "quest-menu-depth-native-card-v1", "target": "Android",
        "unityVersion": "2021.3.5f1", "graphicsApi": "Vulkan", "colorSpace": "Linear",
        "stereoRenderingPath": "MultiPass", "typeTrees": True, "nativeBundleSha256": NATIVE_BUNDLE_SHA,
        "nativeShaderSha256": NATIVE_SHA, "nativePathId": NATIVE_ID, "sources": records}
    return {"producerKey": value_hash(recipe), "recipe": recipe, "sources": records}


def _load(source, relative, extra=None):
    previous = list(sys.path)
    previous_storage = sys.modules.get("storage")
    try:
        # Installer/API loaders remove temporary aliases after capturing us. A
        # later native audit must use this exact selected storage/BuildError ABI,
        # without depending on or permanently replacing an application's module.
        sys.modules["storage"] = _storage
        if extra:
            sys.path.insert(0, str(Path(source) / extra))
        spec = importlib.util.spec_from_file_location("quest_menu_depth_" + Path(relative).stem, Path(source) / relative)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    finally:
        sys.path[:] = previous
        if previous_storage is None:
            sys.modules.pop("storage", None)
        else:
            sys.modules["storage"] = previous_storage


def resource_records(raw):
    """Parse the actual Unity2021 IL2CPP resource sidecar, with finite bounds.

    RuntimeAssembly.cpp reads header byte length, count, then (size,name-length,
    UTF8 name) entries. Payloads are contiguous after the header, without padding.
    Require an exact partition, so an untrusted length cannot select other bytes.
    """
    _require(8 <= len(raw) <= MAX_RESOURCE, "managed resource sidecar is missing or excessive")
    header, count = struct.unpack_from("<ii", raw)
    _require(4 <= header <= len(raw) - 4 and 0 <= count <= 4096, "invalid managed resource directory")
    position, payload, result = 8, 4 + header, {}
    for _ in range(count):
        _require(position + 8 <= 4 + header, "truncated managed resource entry")
        size, length = struct.unpack_from("<ii", raw, position)
        position += 8
        _require(size >= 0 and 0 < length <= 4096 and position + length <= 4 + header,
            "invalid managed resource extent")
        try:
            name = raw[position:position + length].decode("utf-8", errors="strict")
        except UnicodeError as error:
            raise BuildError("Menu-depth resources: invalid managed resource name") from error
        _require("\0" not in name and name not in result and payload + size <= len(raw),
            "duplicate or invalid managed resource payload")
        result[name] = (payload, size)
        position += length
        payload += size
    _require(position == 4 + header and payload == len(raw), "managed resources do not exactly partition the sidecar")
    return result


def _metadata(raw, expected):
    _require(len(raw) <= 2 * 1024 * 1024, "provenance is excessive")
    try:
        value = json.loads(raw)
    except (ValueError, UnicodeError) as error:
        raise BuildError("Menu-depth resources: invalid provenance") from error
    _require(isinstance(value, dict) and value.get("schema") == 1
        and value.get("producerKey") == expected["producerKey"] and value.get("recipe") == expected["recipe"],
        "resource producer differs from the selected sources")
    return value


def inspect_bank(raw, source):
    """Read actual Android target, native Vulkan programs and card keyword banks."""
    import UnityPy
    _require(0 < len(raw) <= MAX_BANK and raw.startswith(b"UnityFS\0"), "missing real finite UnityFS bank")
    environment = UnityPy.load(raw)
    objects = list(environment.objects)
    _require(objects and all(int(obj.assets_file.target_platform) == 13 for obj in objects),
        "bank is not serialized for Android")
    expected = {"assets/menudepth/" + name.lower(): "GloomhavenVR/" + Path(name).stem
        for name in (*SHADERS, "MenuDepthNativeCard.shader")}
    shaders = {path: obj for path, obj in environment.container.items() if obj.type.name == "Shader"}
    _require(set(shaders) == set(expected), "five exact runtime shader roots are required")
    auditor = _load(source, "tools/quest-builder/campaign_native_shaders.py")
    result = []
    for path, name in expected.items():
        obj = shaders[path]
        tree = obj.read_typetree()
        _require(tree["m_ParsedForm"]["m_Name"] == name and tree["platforms"] == [18]
            and tree.get("m_ShaderIsBaked") is True and bool(tree["compressedBlob"]),
            "shader lacks its exact baked Vulkan identity: " + path)
        aliases = auditor.native_aliases(tree, {"originalName": name})
        programs = auditor.program_sample(tree, sorted(set(aliases.values())))
        _require(bool(aliases) and bool(programs), "shader contains no executable Vulkan stages: " + path)
        if name == DERIVED_NAME:
            passes = tree["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]
            _require(len(passes) == 1 and passes[0]["m_State"]["m_Name"] == "Default"
                and passes[0]["m_State"]["zWrite"]["val"] == 1, "native card depth-write state changed")
            native_keys = {tuple(sorted(keys)) for _, _, _, keys in aliases
                if not set(keys) & {"INSTANCING_ON", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON"}}
            _require(native_keys == {(), ("UNITY_UI_ALPHACLIP",), ("UNITY_UI_CLIP_RECT",),
                ("UNITY_UI_ALPHACLIP", "UNITY_UI_CLIP_RECT")}, "original card alpha-clip/rect aliases were stripped")
        result.append({"asset": path, "shader": name, "aliasCount": len(aliases),
            "executablePrograms": len(programs), "compiledBlobSha256": hashlib.sha256(bytes(tree["compressedBlob"])).hexdigest(),
            "programs": programs})
    return {"target": "Android", "platform": 13, "graphicsApi": "Vulkan", "shaderPlatform": 18,
        "shaders": result, "headsetPixelsVerified": False}


def _validate(folder, expected, source):
    bank, provenance = folder / BANK, folder / PROVENANCE
    if not bank.is_file() or not provenance.is_file():
        return None
    value = _metadata(_ordinary_owned(provenance).read_bytes(), expected)
    raw = _ordinary_owned(bank).read_bytes()
    _require(value.get("bundle") == {"path": BANK, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()},
        "cached bank differs from its closed provenance")
    actual = inspect_bank(raw, source)
    _require(value.get("nativePlatformAudit") == actual, "cached native platform proof changed")
    return {"bundle": bank, "provenance": provenance, "producerKey": expected["producerKey"], "reused": True}


def _from_apk(apk, folder, expected, source):
    with zipfile.ZipFile(_ordinary_owned(Path(apk))) as archive:
        matches = [row for row in archive.infolist() if row.filename == RESOURCE_MEMBER]
        _require(len(matches) <= 1, "duplicate mod managed resource sidecar in installed APK")
        if not matches:
            return None
        _require(matches[0].file_size <= MAX_RESOURCE, "installed managed resource sidecar exceeds its bound")
        raw = archive.read(matches[0])
    entries = resource_records(raw)
    if BUNDLE_RESOURCE not in entries or PROVENANCE_RESOURCE not in entries:
        return None
    start, size = entries[PROVENANCE_RESOURCE]
    metadata = raw[start:start + size]
    try:
        value = json.loads(metadata)
    except (ValueError, UnicodeError) as error:
        raise BuildError("Menu-depth resources: invalid installed provenance") from error
    if not isinstance(value, dict) or value.get("producerKey") != expected["producerKey"]:
        return None
    _metadata(metadata, expected)
    start, size = entries[BUNDLE_RESOURCE]
    bank = raw[start:start + size]
    _require(value.get("bundle") == {"path": BANK, "size": len(bank), "sha256": hashlib.sha256(bank).hexdigest()},
        "installed bank differs from its resource provenance")
    _require(value.get("nativePlatformAudit") == inspect_bank(bank, source), "installed native platform proof differs")
    folder.mkdir(parents=True, exist_ok=True)
    (folder / BANK).write_bytes(bank)
    (folder / PROVENANCE).write_bytes(metadata)
    return _validate(folder, expected, source)


def _seed_native(cache, seed, native, original):
    """Borrow only five verified conversion inputs; never write a shared cache."""
    if seed is None:
        return
    seed = Path(seed)
    for program in native.original_programs(original.read()):
        dxbc = program["originalDxbcSha256"]
        interface = value_hash(program["interface"])
        for directory, name in (("bound", dxbc + "-" + interface + ".hlsl"),
                ("bound", dxbc + "-" + interface + ".json"), ("translated", dxbc + ".dxbc"),
                ("translated", dxbc + ".hlsl"), ("translated", dxbc + ".spv")):
            previous = seed / directory / name
            target = cache / directory / name
            if previous.is_file() and not target.exists():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(previous, target)


def _stage_native(source, game, folder, native_cache):
    import UnityPy
    original_path = _ordinary_owned(Path(game) / NATIVE_BUNDLE)
    _require(digest(original_path) == NATIVE_BUNDLE_SHA, "the exact owned original card bundle is required")
    objects = [obj for obj in UnityPy.load(original_path.read_bytes()).objects if obj.path_id == NATIVE_ID]
    _require(len(objects) == 1 and hashlib.sha256(objects[0].get_raw_data()).hexdigest() == NATIVE_SHA,
        "owned original shader object changed")
    original = objects[0]
    native = _load(source, "tools/quest-builder/full_shaders.py")
    project, cache, overlay = folder / "native-source", folder / "native-cache", folder / "native-overlay"
    placeholder = project / NATIVE_ASSET
    placeholder.parent.mkdir(parents=True, exist_ok=True)
    placeholder.write_text('Shader "' + NATIVE_NAME + '" {}\n')
    placeholder.with_suffix(".shader.meta").write_text("fileFormatVersion: 2\nguid: " + NATIVE_GUID + "\n")
    identities = [{"guid": NATIVE_GUID, "path": NATIVE_ASSET, "objects": [{
        "collection": original.assets_file.name, "pathId": NATIVE_ID, "classId": 48}]}]
    _seed_native(cache, native_cache, native, original)
    converters = _load(source, "tools/quest-shaders/converters.py")
    tools = converters.ensure(cache / "tools", Path(source) / "prebuilt/quest-converters-win64-v1.zip")
    inventory = native.inventory(project, game, identities, {original.assets_file.name: NATIVE_BUNDLE},
        cache, bind_programs=True, **tools)
    _require(not inventory.get("errors") and not inventory.get("blockedShaderCount")
        and len(inventory["shaders"]) == 1, "bounded original shader recovery failed")
    producer = _load(source, "tools/quest-shaders/produce.py", "tools/quest-shaders")
    restored = producer.restore_project(project, cache / "original-shader-inventory.json", cache, overlay)
    target = overlay / NATIVE_ASSET
    original_text = target.read_text()
    _require(original_text.count('Shader "' + NATIVE_NAME + '" {') == 1
        and original_text.count("\nZWrite Off\n") == 1, "derived shader has an unexpected native pass")
    derived = original_text.replace('Shader "' + NATIVE_NAME + '" {', 'Shader "' + DERIVED_NAME + '" {', 1)
    derived = derived.replace("\nZWrite Off\n", "\nZWrite On\n", 1)
    target.write_text(derived)
    _require(digest(original_path) == NATIVE_BUNDLE_SHA, "owned source changed during recovery")
    return overlay, {"sourceBundleSha256": NATIVE_BUNDLE_SHA, "sourceShaderSha256": NATIVE_SHA,
        "originalPathId": NATIVE_ID, "originalName": NATIVE_NAME, "derivedName": DERIVED_NAME,
        "originalDxbcSha256": inventory["shaders"][0]["originalDxbcSha256"],
        "originalStageAliasCount": len(inventory["shaders"][0]["variants"]),
        "translatedSourceSha256": hashlib.sha256(original_text.encode()).hexdigest(),
        "derivedSourceSha256": digest(target), "onlyShaderIdentityAndZWriteChanged": True,
        "compilerManifest": restored}


def produce(builder, source, output, *, game=None, editor=None, base_apk=None, native_cache=None, source_files=None):
    """Return private bundle/provenance Paths, reusing before requiring PC inputs.

    The caller passes both paths to MSBuild as GloomhavenVRMenuDepthBundle and
    GloomhavenVRMenuDepthProvenance. No desktop resource is an Android fallback.
    The containing Builder operation owns subprocess cleanup and workspace access.
    """
    source, output = Path(source), Path(output)
    expected = plan(source, source_files)
    folder = _ordinary_owned(output / "cache/menu-depth-resources" / expected["producerKey"])
    retained = _validate(folder, expected, source)
    if retained:
        return retained
    if base_apk is not None:
        retained = _from_apk(base_apk, folder, expected, source)
        if retained:
            return retained
    _require(game is not None and editor is not None,
        "this shader recipe is absent from the installed APK/cache; select the owned PC game once to generate its tiny Android bank")
    _require(plan(source) == expected, "producer snapshot bytes differ from their qualified source roster")
    folder.mkdir(parents=True, exist_ok=True)
    overlay, native_proof = _stage_native(source, game, folder, native_cache)
    project = folder / "unity"
    for name in ("Assets/Editor", "Assets/MenuDepth", "Packages", "ProjectSettings"):
        (project / name).mkdir(parents=True, exist_ok=True)
    shutil.copytree(overlay / "Assets", project / "Assets", dirs_exist_ok=True)
    for name in SHADERS + ("Unity-LICENSE.txt",):
        shutil.copyfile(source / RENDERING / name, project / "Assets/MenuDepth" / name)
    shutil.copyfile(source / EDITOR, project / "Assets/Editor/MenuDepthResourceBuilder.cs")
    write_json(project / "Packages/manifest.json", {"dependencies": {"com.unity.modules.assetbundle": "1.0.0"}})
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    bundle_output = folder / "bundles"
    command = builder.unity_launcher(Path(editor)) + ["-projectPath", str(project), "-buildTarget", "Android",
        "-executeMethod", "MenuDepthResourceBuilder.Build", "-menuDepthOutput", str(bundle_output),
        "-logFile", str(folder / "unity.log")]
    builder.command(command, folder / "launch.log")
    _require(plan(source) == expected, "producer inputs changed while compiling")
    bank = bundle_output / "menu-depth.bundle"
    raw = _ordinary_owned(bank).read_bytes()
    audit = inspect_bank(raw, source)
    shutil.copyfile(bank, folder / BANK)
    value = {"schema": 1, "producerKey": expected["producerKey"], "recipe": expected["recipe"],
        "bundle": {"path": BANK, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()},
        "nativeRestoration": native_proof, "nativePlatformAudit": audit,
        "limits": "Actual Android/Vulkan serialized programs; no device GPU, original FX pixel parity or headset acceptance."}
    write_json(folder / PROVENANCE, value)
    result = _validate(folder, expected, source)
    result["reused"] = False
    return result


def story_records(source, source_files=None):
    """Finite loose numeric Content, separate from shader producer/cache identity."""
    source = Path(source)
    manifest = source / STORY / "manifest.json"
    value = json.loads(_ordinary_owned(manifest).read_text())
    _require(value.get("schema") == 1 and value.get("status") == "complete"
        and value.get("format") == "GMD1" and 0 < len(value.get("images", [])) <= 4096, "incomplete story manifest")
    expected = {}
    for row in value["images"]:
        name = row.get("depth_file", "")
        _require(re.fullmatch(r"[0-9a-f]{32}\.gmd", name) is not None and name not in expected,
            "invalid or duplicate story depth identity")
        expected[name] = {"path": STORY + name, "size": row["depth_bytes"], "sha256": row["depth_sha256"]}
    _require({path.name for path in (source / STORY).glob("*.gmd")} == set(expected), "story fields differ from their finite manifest")
    known = {row["path"]: row for row in source_files} if source_files is not None else None
    records = []
    for name in ["manifest.json", *sorted(expected)]:
        row = _record(source, STORY + name)
        _require(name == "manifest.json" or row == expected[name], "story field bytes differ from their manifest")
        _require(known is None or known.get(row["path"]) == row, "story field differs from the source snapshot")
        records.append({**row, "path": STORY_DESTINATION + name})
    return records


def write_story(archive, source, records):
    """Append the already qualified fields to the existing mod-content ZIP."""
    _require(records == story_records(source), "story inputs changed before publication")
    _require(not set(archive.namelist()) & {row["path"] for row in records}, "duplicate story archive entries")
    for row in records:
        path = _ordinary_owned(Path(source) / STORY / Path(row["path"]).name)
        before = path.stat()
        entry = zipfile.ZipInfo(row["path"], (2020, 1, 1, 0, 0, 0))
        entry.file_size = row["size"]
        entry.compress_type = zipfile.ZIP_DEFLATED
        transferred, sha = 0, hashlib.sha256()
        with path.open("rb") as input_stream, archive.open(entry, "w", force_zip64=True) as output_stream:
            for block in iter(lambda: input_stream.read(1024 * 1024), b""):
                transferred += len(block)
                _require(transferred <= row["size"], "story source grew during archive transfer")
                sha.update(block)
                output_stream.write(block)
        stamp = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
        _require(transferred == row["size"] and sha.hexdigest() == row["sha256"] and stamp(before) == stamp(path.stat()),
            "story bytes changed during archive transfer")
