"""Closed Android default-sky assets, independent of scenes and mod art banks.

Installed updates copy the existing Android player's exact seven render assets.
A first PC build recovers only those originals and compiles their three shaders.
Warm resources need neither a PC installation nor an Editor. No desktop shader,
generic sky or whole-game reconstruction is an implicit fallback.
"""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import shutil
import struct
import sys
import types
import uuid
import zipfile

import storage as _storage
from storage import BuildError, _ordinary_owned, digest, record_file, value_hash, write_json

SCOPE = "quest-default-sky-resources-v1"
WORK_SCOPE = "quest-default-sky-work-v1"
CACHE_ROOT = "cache/default-sky-resources"
BANK = "native-default-sky-android.bundle"
PROVENANCE = "native-default-sky-android.provenance.json"
BUNDLE_RESOURCE = "GloomhavenVR.DefaultSky.bundle"
PROVENANCE_RESOURCE = "GloomhavenVR.DefaultSky.provenance.json"
RESOURCE_MEMBER = "assets/bin/Data/Managed/Resources/GloomhavenVR.dll-resources.dat"
EDITOR = "tools/quest-default-sky/Editor/DefaultSkyResourceBuilder.cs"
NATIVE_PROVENANCE = "src/GloomhavenVR/Assets/DefaultSky/native-default-sky.provenance.json"
TEMPLATE = "src/GloomhavenVR/Assets/QuestLoading/ghvr-quest-loading.bundle"
MAX_BANK = 64 * 1048576
MAX_SERIALIZED = 512 * 1048576
MAX_OBJECT = 48 * 1048576
MAX_METADATA = 16 * 1048576
MAX_RESOURCE = 96 * 1048576
SPLIT_SIZE = 1048576
SHADERS = {"shader": "AMP_SkyShader", "diffuse": "Legacy Shaders/Diffuse", "vertex-lit": "Legacy Shaders/VertexLit"}
IDS = {"material": 4, "noise": 6, "sky": 8, "mesh": 12, "shader": 18, "diffuse": 1007, "vertex-lit": 1006}
NAMES = {**SHADERS, "material": "BG_SkyClouds 2", "mesh": "GH_SkySphere", "sky": "EN_SkyTest_01", "noise": "PerlinNoiseGray2"}
TYPES = {"material": "Material", "mesh": "Mesh", "sky": "Texture2D", "noise": "Texture2D", **{r: "Shader" for r in SHADERS}}
ROOTS = {"native/default-sky/mesh": "mesh", "native/default-sky/material": "material", "native/default-sky/shader": "shader"}
PINS = {
    "sharedassets8.assets": "a106023a2b9fe28b169e9c9dee717f33defd8ff2b85df469aeb0bce03c471619",
    "sharedassets8.assets.resS": "dad4d354fad6d1be1cdd3ad433a1032cd5ee4f4a65b3522d8f1e266eff217b0f",
    "Resources/unity_builtin_extra": "2008bf48f31b2933d923cbc6e0c5b354b291710c6e1d825edd23400847f8819b",
}
HELPERS = ("tools/quest-builder/sky_resources.py", "tools/default-sky-native/export.py", EDITOR,
    NATIVE_PROVENANCE, TEMPLATE, "src/GloomhavenVR/Assets/QuestLoading/provenance.json",
    "tools/quest-builder/storage.py", "tools/quest-builder/progress.py", "tools/quest-builder/dependencies.py",
    "tools/quest-builder/menu_depth_resources.py", "tools/quest-builder/full_shaders.py",
    "tools/quest-shaders/produce.py", "tools/quest-shaders/converters.py", "tools/quest-shaders/manifest.py",
    "tools/quest-shaders/integer_bits.py", "tools/quest-shaders/load_bounds.py", "tools/quest-shaders/instance_nan.py",
    "tools/quest-builder/campaign_native_shaders.py", "tools/quest-builder/requirements.txt",
    "tools/quest-builder/requirements-bootstrap.txt", "tools/quest-builder/requirements-source.txt",
    "tools/quest-network/requirements.txt", "prebuilt/quest-converters-win64-v1.zip")
SOURCE_SUFFIXES = {".py", ".cs", ".c", ".cpp", ".h", ".json", ".md", ".txt", ".shader", ".cginc", ".lock"}
GENERATED = {"bin", "obj", "Library", "Temp", "Logs", "__pycache__", ".git"}


def require(value, message):
    if not value:
        raise BuildError("Default sky resources: " + message)


def plan(source, source_files=None):
    """Public recipe identity; immutable rows also work in a compiler snapshot."""
    source = Path(source)
    def shader_source(name):
        path = PurePosixPath(name)
        return name.startswith("tools/quest-shaders/") and path.suffix in SOURCE_SUFFIXES and not set(path.parts) & GENERATED
    shader_names = ({row["path"] for row in source_files if shader_source(row["path"])} if source_files is not None else
        {p.relative_to(source).as_posix() for p in (source / "tools/quest-shaders").rglob("*")
         if p.is_file() and shader_source(p.relative_to(source).as_posix())})
    names = sorted(set(HELPERS) | shader_names)
    if source_files is None:
        rows = [record_file(_ordinary_owned(source / name), name) for name in names]
    else:
        known = {row["path"]: row for row in source_files}
        require(len(known) == len(source_files), "duplicate immutable source identity")
        rows = [known.get(name) for name in names]
    require(all(isinstance(r, dict) and type(r.get("size")) is int and r["size"] > 0
        and re.fullmatch(r"[0-9a-f]{64}", r.get("sha256", "")) for r in rows), "missing public producer input")
    provenance = source / NATIVE_PROVENANCE
    if source_files is None or provenance.is_file():
        native = json.loads(_ordinary_owned(provenance).read_text())
        require(native.get("schema") == "native-default-sky-v1" and native.get("unity") == "2021.3.5f1"
            and all(native.get("sources", {}).get(k) == v for k, v in PINS.items()), "original sky pins changed; audit the new originals first")
    recipe = {"schema": 1, "scope": SCOPE, "target": "Android", "graphicsApi": "Vulkan", "unityVersion": "2021.3.5f1",
              "stereoRenderingPath": "MultiPass", "typeTrees": True, "sources": rows}
    return {"recipeKey": value_hash(recipe), "recipe": recipe, "sources": rows}


def _load(source, relative, extra=None):
    previous, previous_storage = list(sys.path), sys.modules.get("storage")
    try:
        sys.modules["storage"] = _storage
        if extra:
            sys.path.insert(0, str(Path(source) / extra))
        spec = importlib.util.spec_from_file_location("quest_sky_" + Path(relative).stem, Path(source) / relative)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    finally:
        sys.path[:] = previous
        if previous_storage is None:
            sys.modules.pop("storage", None)
        else:
            sys.modules["storage"] = previous_storage


def _sha(raw):
    return hashlib.sha256(raw).hexdigest()


def _member(relative):
    path = PurePosixPath(relative)
    require(path.parts and path.as_posix() == relative and not path.is_absolute() and not any(p in ("", ".", "..") for p in path.parts)
        and "\\" not in relative and ":" not in relative, "unsafe player dependency path")
    return "assets/bin/Data/" + path.as_posix()


class PlayerFile(io.RawIOBase):
    """Seek across Unity's split player files, inflating only requested chunks."""
    def __init__(self, archive, logical):
        self.archive, self.name, self.position = archive, logical, 0
        matches = [i for i in archive.infolist() if i.filename == logical]
        split = sorted((i for i in archive.infolist() if re.fullmatch(re.escape(logical) + r"\.split\d+", i.filename)),
            key=lambda i: int(i.filename.rsplit("split", 1)[1]))
        require(not (matches and split) and len(matches) <= 1, "ambiguous player file/split identity")
        self.parts = matches or split
        require(self.parts and len(self.parts) <= 512, "required converted player file is absent: " + logical)
        if split:
            require([i.filename for i in split] == [logical + ".split" + str(i) for i in range(len(split))]
                and all(i.file_size == SPLIT_SIZE for i in split[:-1]) and 0 < split[-1].file_size <= SPLIT_SIZE,
                "incomplete or noncanonical player splits")
        self.size = sum(i.file_size for i in self.parts)
        require(48 <= self.size <= MAX_SERIALIZED and all(not i.flag_bits & 1 and i.compress_type in (0, 8) for i in self.parts),
            "unsupported player file bounds or compression")
        self.cache, self.used = {}, set()

    def readable(self): return True
    def seekable(self): return True
    def tell(self): return self.position

    def seek(self, offset, whence=0):
        require(whence in (0, 1, 2), "invalid player seek")
        position = offset + (self.position if whence == 1 else self.size if whence == 2 else 0)
        require(0 <= position <= self.size, "player seek outside file")
        self.position = position
        return position

    def read(self, count=-1):
        count = min(self.size - self.position, count if count >= 0 else self.size)
        require(count <= MAX_OBJECT, "unbounded player read")
        result = []
        while count:
            index = self.position // SPLIT_SIZE if len(self.parts) > 1 else 0
            offset = self.position % SPLIT_SIZE if len(self.parts) > 1 else self.position
            if index not in self.cache:
                if len(self.cache) >= 10:
                    self.cache.pop(next(iter(self.cache)))
                require(self.parts[index].file_size <= MAX_OBJECT, "player ZIP member exceeds its bound")
                self.cache[index] = self.archive.read(self.parts[index])
                self.used.add(index)
            raw = self.cache[index]
            take = min(count, len(raw) - offset)
            require(take > 0, "truncated player ZIP member")
            result.append(raw[offset:offset + take])
            self.position += take
            count -= take
        return b"".join(result)

    def readinto(self, buffer):
        value = self.read(len(buffer)); buffer[:len(value)] = value
        return len(value)


def _player_serialized(reader):
    import UnityPy
    header = reader.read(48)
    require(len(header) == 48 and struct.unpack_from(">I", header, 8)[0] == 22 and header[16] in (0, 1)
        and header[17:20] == b"\0\0\0", "player requires Unity2021 serialized v22")
    metadata, size, offset = struct.unpack_from(">Iqq", header, 20)
    require(size == reader.size and 0 < metadata <= MAX_METADATA and 48 + metadata <= offset <= size,
        "invalid serialized player metadata extent")
    reader.seek(0)
    environment = UnityPy.load(reader)
    require(len(environment.assets) == 1, "ambiguous player serialized file")
    file = environment.assets[0]
    require(file.unity_version == "2021.3.5f1" and int(file.target_platform) == 13
        and len(file.objects) <= 65536 and len(file.externals) <= 16384, "wrong player version/target or excessive object table")
    require(all(0 <= o.byte_size <= MAX_OBJECT and offset <= o.byte_start <= o.byte_start + o.byte_size <= size
        for o in file.objects.values()), "player object exceeds its serialized file")
    previous = offset
    for start, end in sorted((o.byte_start, o.byte_start + o.byte_size) for o in file.objects.values()):
        require(start >= previous, "overlapping player object ranges")
        previous = end
    return file


def _descriptor(owner, obj, stream=b""):
    require(0 < obj.byte_size <= MAX_OBJECT, "native object size exceeds its bound")
    return {"owner": owner, "object": obj, "tree": obj.read_typetree(), "stream": stream}


def _android_assets(archive):
    files, readers = {}, {}
    def file(name):
        if name not in files:
            reader = PlayerFile(archive, name)
            readers[name] = reader
            files[name] = _player_serialized(reader)
        return files[name]
    def follow(owner, ptr, expected):
        original = file(owner)
        fid, pid = ptr["m_FileID"], ptr["m_PathID"]
        require(type(fid) is int and 0 <= fid <= len(original.externals) and type(pid) is int and pid != 0,
            "invalid native sky pointer")
        target = _member(original.externals[fid - 1].path) if fid else owner
        obj = file(target).objects.get(pid)
        require(obj is not None and obj.type.name == expected, "native sky dependency has the wrong type")
        return _descriptor(target, obj)
    manager = file(_member("globalgamemanagers"))
    settings = [o.read_typetree() for o in manager.objects.values() if o.type.name == "BuildSettings"]
    require(len(settings) == 1 and settings[0].get("m_Version") == "2021.3.5f1", "missing exact player BuildSettings")
    scenes = settings[0].get("scenes", [])
    require(isinstance(scenes, list) and len(scenes) <= 128 and scenes.count("Assets/Scenes/Release/ProcGen.unity") == 1,
        "converted player has no unambiguous original ProcGen scene")
    scene_name = _member("level" + str(scenes.index("Assets/Scenes/Release/ProcGen.unity")))
    scene = file(scene_name)
    skies = [o.path_id for o in scene.objects.values() if o.type.name == "GameObject" and o.read_typetree().get("m_Name") == "GH_SkySphere"]
    require(len(skies) == 1, "original ProcGen sky is absent or ambiguous")
    def component(kind):
        values = [o.read_typetree() for o in scene.objects.values() if o.type.name == kind]
        values = [t for t in values if t["m_GameObject"] == {"m_FileID": 0, "m_PathID": skies[0]}]
        require(len(values) == 1, "original sky lacks one exact " + kind)
        return values[0]
    renderer = component("MeshRenderer")
    require(len(renderer["m_Materials"]) == 1, "original sky material cardinality changed")
    assets = {"material": follow(scene_name, renderer["m_Materials"][0], "Material"),
              "mesh": follow(scene_name, component("MeshFilter")["m_Mesh"], "Mesh")}
    material = assets["material"]
    textures = dict(material["tree"]["m_SavedProperties"]["m_TexEnvs"])
    require(set(textures) == {"_Map00", "_Map1", "_Map2", "_texcoord"}
        and textures["_Map00"]["m_Texture"] == textures["_Map1"]["m_Texture"]
        and textures["_texcoord"]["m_Texture"]["m_PathID"] == 0, "original sky texture graph changed")
    assets["sky"] = follow(material["owner"], textures["_Map1"]["m_Texture"], "Texture2D")
    assets["noise"] = follow(material["owner"], textures["_Map2"]["m_Texture"], "Texture2D")
    assets["shader"] = follow(material["owner"], material["tree"]["m_Shader"], "Shader")
    shader = assets["shader"]
    dependencies = shader["tree"]["m_Dependencies"]
    require(len(dependencies) == 2, "original AMP fallback cardinality changed")
    for ptr in dependencies:
        dep = follow(shader["owner"], ptr, "Shader")
        name = dep["tree"]["m_ParsedForm"]["m_Name"]
        role = next((r for r, n in SHADERS.items() if n == name), None)
        require(role in ("diffuse", "vertex-lit") and role not in assets, "unexpected AMP fallback shader")
        assets[role] = dep
    require(set(assets) == set(IDS), "incomplete original seven-asset closure")
    diffuse = assets["diffuse"]
    require(len(diffuse["tree"]["m_Dependencies"]) == 1, "diffuse fallback graph changed")
    vertex = follow(diffuse["owner"], diffuse["tree"]["m_Dependencies"][0], "Shader")
    require((vertex["owner"], vertex["object"].path_id) == (assets["vertex-lit"]["owner"], assets["vertex-lit"]["object"].path_id)
        and not vertex["tree"]["m_Dependencies"], "fallback graph is not Diffuse→VertexLit")
    proof = {"scene": scene_name, "files": [{"path": name, "logicalBytes": r.size,
        "readMembers": [{"path": r.parts[i].filename, "size": r.parts[i].file_size, "crc32": r.parts[i].CRC}
                        for i in sorted(r.used)]} for name, r in sorted(readers.items())]}
    return assets, proof


def _data(asset, mesh=False):
    tree = asset["tree"]
    inline = bytes(tree["m_VertexData"]["m_DataSize"] if mesh else tree.get("image data", b""))
    stream = tree.get("m_StreamData", {})
    require(not (inline and stream.get("size")), "ambiguous native inline/streamed data")
    require(bool(inline) != bool(asset["stream"]), "missing or ambiguous native pixel/vertex payload")
    return inline or asset["stream"]


def _asset_contract(assets, source, *, android=True):
    for role, asset in assets.items():
        tree, obj = asset["tree"], asset["object"]
        name = tree["m_ParsedForm"]["m_Name"] if TYPES[role] == "Shader" else tree.get("m_Name")
        require(obj.type.name == TYPES[role] and name == NAMES[role],
            "wrong original identity for " + role)
    mesh = assets["mesh"]["tree"]
    require(mesh["m_VertexData"]["m_VertexCount"] == 1679 and _sha(_data(assets["mesh"], True)) == "44117ffd2d397d50368f269dee012786f17289e285b0b913f58f5ac7e0c9a6aa"
        and _sha(bytes(mesh["m_IndexBuffer"])) == "9df7f8d347a1be9367cd26e6f74106385b6f3a2d1a34ce80ddc5918b5fd8cfb4"
        and value_hash(mesh["m_LocalAABB"]) == "15618a12191e760e431526a2a8615b1dd600db212fe6ce37933ba0c5a85419ed"
        and value_hash(mesh["m_VertexData"]["m_Channels"]) == "5eb49a367c8bbf5c2dd2aea3a0c45a677f5e585a1e3471c2dd6450e7e5cf9cae"
        and value_hash(mesh["m_SubMeshes"]) == "5d2aa3c06b0be132b810d48817fb8d3b4f1a8bd7a29c5c5e457faa01ca2e22e2", "original sky geometry changed")
    properties = assets["material"]["tree"]["m_SavedProperties"]
    require(value_hash({k: dict(properties[k]) for k in ("m_Colors", "m_Floats", "m_Ints")})
        == "0d444a95a63e797de451637a8b16e79f290ffb78d63b7603daf41893316e3d00", "original sky material values changed")
    textures = []
    for role, width, height, count, aniso in (("sky", 4096, 2048, 13, 1), ("noise", 512, 512, 10, 16)):
        t = assets[role]["tree"]; raw = _data(assets[role]); fmt = t["m_TextureFormat"]
        require((t["m_Width"], t["m_Height"], t["m_MipCount"], t["m_ColorSpace"]) == (width, height, count, 1)
            and t["m_TextureSettings"] == {"m_FilterMode": 1, "m_Aniso": aniso, "m_MipBias": 0.0,
                "m_WrapU": 0, "m_WrapV": 0, "m_WrapW": 0}, "original texture dimensions/mips/sampling changed")
        formats = {4: 4, 34: 8, 45: 8, 47: 16, 48: 16} if android else {10: 8}
        require(fmt in formats, "unsupported Android texture encoding; retain the existing game and audit this format")
        sizes = [(max(1, width >> i) * max(1, height >> i) * 4 if fmt == 4 else
                  ((max(1, width >> i) + 3) // 4) * ((max(1, height >> i) + 3) // 4) * formats[fmt]) for i in range(count)]
        require(len(raw) == sum(sizes), "native texture mip payload is incomplete")
        offset, mips = 0, []
        for i, size in enumerate(sizes):
            mips.append({"level": i, "size": size, "sha256": _sha(raw[offset:offset + size])}); offset += size
        textures.append({"role": role, "format": fmt, "size": len(raw), "sha256": _sha(raw), "mips": mips})
    shaders = []
    if android:
        auditor = _load(source, "tools/quest-builder/campaign_native_shaders.py")
        for role, name in SHADERS.items():
            t = assets[role]["tree"]
            require(t["platforms"] == [18] and t.get("m_ShaderIsBaked") is True and t["compressedBlob"], "missing baked native Vulkan shader: " + name)
            require([len(s["m_Passes"]) for s in t["m_ParsedForm"]["m_SubShaders"]] == [{"shader": 3, "diffuse": 5, "vertex-lit": 3}[role]],
                "native shader pass closure changed: " + name)
            aliases = auditor.native_aliases(t, {"originalName": name})
            programs = auditor.program_sample(t, sorted(set(aliases.values())))
            require(aliases and programs, "native shader has no executable Vulkan programs: " + name)
            shaders.append({"role": role, "name": name, "aliases": len(aliases), "programs": programs,
                            "compiledBlobSha256": _sha(bytes(t["compressedBlob"]))})
    return {"target": "Android", "platform": 13, "graphicsApi": "Vulkan", "shaderPlatform": 18,
        "meshVertexSha256": _sha(_data(assets["mesh"], True)), "meshIndexSha256": _sha(bytes(mesh["m_IndexBuffer"])),
        "materialValuesKey": value_hash({k: dict(properties[k]) for k in ("m_Colors", "m_Floats", "m_Ints")}),
        "textures": textures, "shaders": shaders, "headsetPixelsVerified": False}


def _basis(assets, kind):
    return {"kind": kind, "originalSources": PINS, "assets": [{"role": role, "name": NAMES[role],
        "type": TYPES[role], "owner": a["owner"], "pathId": a["object"].path_id,
        "objectSha256": _sha(a["object"].get_raw_data()), "streamSha256": _sha(a["stream"])} for role, a in sorted(assets.items())]}


def _pack(assets, source, producer_key):
    """Use only an authored container; relocate exact native objects into it."""
    import UnityPy
    from UnityPy.enums import ClassIDType
    from UnityPy.files import SerializedFile
    from UnityPy.files.ObjectReader import ObjectReader
    from UnityPy.streams import EndianBinaryReader
    environment = UnityPy.load(str(_ordinary_owned(Path(source) / TEMPLATE)))
    wrapper = next(iter(environment.files.values()))
    file = next(f for f in wrapper.files.values() if isinstance(f, SerializedFile))
    require(file.unity_version == "2021.3.5f1" and int(file.target_platform) == 13 and file._enable_type_tree,
        "public container template is not exact Unity2021 Android/type-tree enabled")
    container = next(o for o in file.objects.values() if o.type.name == "AssetBundle")
    container_tree = container.read_typetree()
    roots = {role: copy.deepcopy(a["tree"]) for role, a in assets.items()}
    roots["material"]["m_Shader"] = {"m_FileID": 0, "m_PathID": IDS["shader"]}
    for key, value in roots["material"]["m_SavedProperties"]["m_TexEnvs"]:
        value["m_Texture"] = {"m_FileID": 0, "m_PathID": IDS["sky"] if key in ("_Map00", "_Map1") else IDS["noise"] if key == "_Map2" else 0}
    roots["shader"]["m_Dependencies"] = [{"m_FileID": 0, "m_PathID": IDS[r]} for r in ("diffuse", "vertex-lit")]
    roots["diffuse"]["m_Dependencies"] = [{"m_FileID": 0, "m_PathID": IDS["vertex-lit"]}]
    roots["vertex-lit"]["m_Dependencies"] = []
    for role in ("sky", "noise", "mesh"):
        t = roots[role]
        if role == "mesh": t["m_VertexData"]["m_DataSize"] = _data(assets[role], True)
        else: t["image data"] = _data(assets[role])
        t["m_StreamData"] = {"offset": 0, "size": 0, "path": ""}
    objects, types = {}, []
    def add(original, pid, tree):
        typ = copy.copy(original.serialized_type)
        if typ.node is None: typ.node = original._get_typetree_node(None)
        if typ.type_dependencies is None: typ.type_dependencies = []
        for node in typ.node.traverse():
            if node.m_TypeFlags is None: node.m_TypeFlags = 1 if node.m_Type == "Array" else 0
            if node.m_RefTypeHash is None: node.m_RefTypeHash = 0
        types.append(typ)
        raw = original.get_raw_data()
        obj = ObjectReader(file, original.reader, pid, len(types) - 1, typ, original.class_id,
            ClassIDType(original.class_id), 0, len(raw), None, None, raw)
        obj.save_typetree(tree)
        payload = obj.data
        require(payload is not None, "native object writer produced no serialized bytes")
        # get_raw_data() follows the reader, not .data. Own an exact reader for
        # the replacement bytes so roundtrip checks cannot observe a template.
        obj = ObjectReader(file, EndianBinaryReader(payload, endian=original.reader.endian), pid, len(types) - 1,
            typ, original.class_id, ClassIDType(original.class_id), 0, len(payload), None, None, payload, len(payload))
        require(obj.get_raw_data() == payload, "native replacement reader does not own its bytes")
        objects[pid] = obj
    for role in sorted(assets): add(assets[role]["object"], IDS[role], roots[role])
    container_tree["m_Name"] = container_tree["m_AssetBundleName"] = "ghvr-native-default-sky-android"
    container_tree["m_Dependencies"] = []
    container_tree["m_IsStreamedSceneAssetBundle"] = False
    container_tree["m_ExplicitDataLayout"] = 0
    container_tree["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": i} for i in sorted(objects)]
    container_tree["m_Container"] = [(name, {"preloadIndex": 0, "preloadSize": len(objects),
        "asset": {"m_FileID": 0, "m_PathID": IDS[role]}}) for name, role in sorted(ROOTS.items())]
    container_tree["m_MainAsset"] = {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}}
    add(container, 1, container_tree)
    file.objects, file.types, file.externals, file.script_types = objects, types, [], []
    file.name = "CAB-" + producer_key[:32]
    wrapper.files = {file.name: file}
    raw = wrapper.save("lz4")
    require(0 < len(raw) <= MAX_BANK, "new native sky bank exceeds its resource bound")
    actual = UnityPy.load(raw).assets[0]
    require(actual.objects[1].read_typetree() == container_tree, "runtime container changed during serialized roundtrip")
    require(all(actual.objects[IDS[role]].read_typetree() == tree for role, tree in roots.items()),
        "native render asset fields changed beyond declared pointer/data relocation")
    require(inspect_bank(raw, source) == _asset_contract(assets, source),
        "native geometry/material/mip/program bytes changed during private repack")
    return raw


def _inspect_bank(raw, source):
    import UnityPy
    require(0 < len(raw) <= MAX_BANK and raw.startswith(b"UnityFS\0"), "missing finite UnityFS sky bank")
    environment = UnityPy.load(raw)
    require(len(environment.assets) == 1, "unexpected native sky serialized files")
    file = environment.assets[0]
    require(file.unity_version == "2021.3.5f1" and int(file.target_platform) == 13 and file._enable_type_tree and not file.externals
        and len(file.objects) == 8 and set(environment.container) == set(ROOTS), "sky bank is not the closed Android seven-asset container")
    assets = {role: _descriptor(file.name, file.objects[pid]) for role, pid in IDS.items() if pid in file.objects}
    require(len(assets) == 7 and all(environment.container[k].path_id == IDS[v] for k, v in ROOTS.items()), "runtime sky roots have wrong identities")
    require(assets["material"]["tree"]["m_Shader"] == {"m_FileID": 0, "m_PathID": IDS["shader"]}
        and assets["shader"]["tree"]["m_Dependencies"] == [{"m_FileID": 0, "m_PathID": IDS[r]} for r in ("diffuse", "vertex-lit")]
        and assets["diffuse"]["tree"]["m_Dependencies"] == [{"m_FileID": 0, "m_PathID": IDS["vertex-lit"]}]
        and not assets["vertex-lit"]["tree"]["m_Dependencies"], "native sky shader/fallback pointers are open")
    for key, value in assets["material"]["tree"]["m_SavedProperties"]["m_TexEnvs"]:
        expected = IDS["sky"] if key in ("_Map00", "_Map1") else IDS["noise"] if key == "_Map2" else 0
        require(value["m_Texture"] == {"m_FileID": 0, "m_PathID": expected}, "native texture pointer is open")
    return _asset_contract(assets, source)


def inspect_bank(raw, source):
    try:
        return _inspect_bank(raw, source)
    except BuildError:
        raise
    except Exception as error:
        raise BuildError("Default sky resources: malformed native Android bank; retain the current installation: " + str(error)) from error


def _metadata(raw, expected):
    require(len(raw) <= 4 * 1048576, "sky provenance exceeds its bound")
    try: value = json.loads(raw)
    except (ValueError, UnicodeError) as error: raise BuildError("Default sky resources: invalid provenance") from error
    require(isinstance(value, dict) and value.get("schema") == 1 and value.get("scope") == SCOPE
        and value.get("recipe") == expected["recipe"] and value.get("recipeKey") == expected["recipeKey"]
        and isinstance(value.get("basis"), dict)
        and value.get("producerKey") == value_hash({"recipeKey": expected["recipeKey"], "basis": value["basis"]}), "resource source/basis identity differs")
    basis = value["basis"]
    require(set(basis) == {"kind", "originalSources", "assets"}
        and basis["kind"] in ("retained-android-player", "owned-pc-native-vulkan")
        and basis["originalSources"] == PINS and isinstance(basis["assets"], list)
        and len(basis["assets"]) == 7, "unknown native acquisition basis")
    require([r.get("role") for r in basis["assets"] if isinstance(r, dict)] == sorted(IDS), "native basis roles are incomplete or ambiguous")
    for row in basis["assets"]:
        role = row["role"]
        owner = row.get("owner")
        require(set(row) == {"role", "name", "type", "owner", "pathId", "objectSha256", "streamSha256"}
            and row["name"] == NAMES[role] and row["type"] == TYPES[role]
            and isinstance(owner, str) and len(owner) <= 512 and PurePosixPath(owner).as_posix() == owner
            and not PurePosixPath(owner).is_absolute() and ".." not in PurePosixPath(owner).parts
            and "\\" not in owner and ":" not in owner and not any(ord(c) < 32 for c in owner)
            and type(row["pathId"]) is int and 0 < row["pathId"] < 2 ** 63
            and all(isinstance(row[key], str) and re.fullmatch(r"[0-9a-f]{64}", row[key]) for key in ("objectSha256", "streamSha256")),
            "invalid original native asset identity")
        if basis["kind"] == "retained-android-player":
            require(owner.startswith("assets/bin/Data/"), "native Android asset has an unknown owner")
        else:
            require(owner == ("Resources/unity_builtin_extra" if role in ("diffuse", "vertex-lit") else "sharedassets8.assets")
                and row["pathId"] == ({"diffuse": 7, "vertex-lit": 6}.get(role, IDS[role])), "native PC asset has an unknown owner")
    if "workRelativePath" in value:
        require(value["workRelativePath"] == "work/sky-" + value["producerKey"][:12], "native workspace reference has an unknown identity")
    return value


def _result(folder, value, output, reused):
    result = {"bundle": folder / BANK, "provenance": folder / PROVENANCE, "recipeKey": value["recipeKey"],
        "producerKey": value["producerKey"], "reused": reused, "basis": value["basis"],
        "cacheRelativePath": folder.relative_to(output).as_posix()}
    if "workRelativePath" in value: result["workRelativePath"] = value["workRelativePath"]
    return result


def _validate(folder, expected, source, output):
    if not (folder / PROVENANCE).is_file() and not (folder / BANK).is_file(): return None
    require((folder / PROVENANCE).is_file() and (folder / BANK).is_file(), "incomplete private sky resource cache")
    value = _metadata(_ordinary_owned(folder / PROVENANCE).read_bytes(), expected)
    require(folder.name == value["producerKey"] and folder.parent.name == value["recipeKey"], "resource cache directory ownership differs")
    raw = _ordinary_owned(folder / BANK).read_bytes()
    require(value.get("bundle") == {"path": BANK, "size": len(raw), "sha256": _sha(raw)}
        and value.get("nativePlatformAudit") == inspect_bank(raw, source), "cached sky bank differs from its closed native proof")
    return _result(folder, value, output, True)


def _guard_pair(folder):
    _ordinary_owned(folder)
    require(not folder.exists() or folder.is_dir(), "private resource cache root is not a directory")
    for name in (BANK, PROVENANCE):
        path = _ordinary_owned(folder / name)
        require(not path.exists() or path.is_file(), "resource cache file position has unknown contents: " + name)


def _known_cache(folder, expected, source, output):
    """Only call after a real selected source closes this exact cache identity."""
    _guard_pair(folder)
    try:
        return _validate(folder, expected, source, output), None
    except BuildError as error:
        before = {}
        for name in (BANK, PROVENANCE):
            path = folder / name
            if path.is_file():
                size = path.stat().st_size
                if size <= MAX_RESOURCE:
                    before[name] = {"size": size, "sha256": digest(path)}
                else:
                    with path.open("rb") as stream: prefix = stream.read(1048576)
                    before[name] = {"size": size, "prefixBytes": len(prefix), "prefixSha256": _sha(prefix), "wholeHashRecorded": False}
        return None, {"reason": str(error)[:512], "before": before}


def _atomic_bytes(path, raw):
    temporary = path.parent / (".sky-" + uuid.uuid4().hex[:12] + ".tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _repair_report(output, value, repair, provenance_raw):
    if repair is None: return
    path = _ordinary_owned(output / "logs" / ("default-sky-repair-" + value["recipeKey"][:12] + "-" + value["producerKey"][:12] + ".json"))
    repairs = []
    if path.is_file() and path.stat().st_size <= 65536:
        try:
            old = json.loads(path.read_text())
            if old.get("recipeKey") == value["recipeKey"] and old.get("producerKey") == value["producerKey"]:
                previous = old.get("repairs", [])
                if isinstance(previous, list): repairs = previous[-15:]
        except (ValueError, AttributeError, TypeError): pass
    repairs.append({**repair, "restored": {"bundleSha256": value["bundle"]["sha256"], "provenanceSha256": _sha(provenance_raw)}})
    write_json(path, {"schema": 1, "scope": SCOPE, "recipeKey": value["recipeKey"], "producerKey": value["producerKey"], "repairs": repairs})


def _publish(raw, expected, basis, source, output, *, acquisition=None, repair=None, work_relative=None, provenance_raw=None):
    producer_key = value_hash({"recipeKey": expected["recipeKey"], "basis": basis})
    folder = _ordinary_owned(output / CACHE_ROOT / expected["recipeKey"] / producer_key)
    _guard_pair(folder)
    folder.mkdir(parents=True, exist_ok=True)
    value = {"schema": 1, "scope": SCOPE, "recipeKey": expected["recipeKey"], "producerKey": producer_key,
        "recipe": expected["recipe"], "basis": basis, "bundle": {"path": BANK, "size": len(raw), "sha256": _sha(raw)},
        "nativePlatformAudit": inspect_bank(raw, source), "acquisition": acquisition,
        "limits": "Exact native serialized Android assets and executable Vulkan banks; no headset/device pixel acceptance."}
    if work_relative is not None: value["workRelativePath"] = work_relative
    if provenance_raw is not None:
        selected = _metadata(provenance_raw, expected)
        require(selected["basis"] == basis and selected["bundle"] == value["bundle"]
            and selected.get("nativePlatformAudit") == value["nativePlatformAudit"], "selected resource pair is not this exact native basis")
        value = selected
    else:
        provenance_raw = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode() + b"\n"
    _atomic_bytes(folder / BANK, raw)
    _atomic_bytes(folder / PROVENANCE, provenance_raw)
    _repair_report(output, value, repair, provenance_raw)
    result = _validate(folder, expected, source, output)
    result["reused"] = False
    result["repaired"] = repair is not None
    return result


def _installed_pair(apk, source, expected, output):
    parser = _load(source, "tools/quest-builder/menu_depth_resources.py")
    matches = [i for i in apk.infolist() if i.filename == RESOURCE_MEMBER]
    require(len(matches) <= 1, "duplicate installed managed resource sidecar")
    if not matches: return None
    require(matches[0].file_size <= MAX_RESOURCE, "installed managed resources exceed their bound")
    raw = apk.read(matches[0]); entries = parser.resource_records(raw)
    if BUNDLE_RESOURCE not in entries and PROVENANCE_RESOURCE not in entries: return None
    require(BUNDLE_RESOURCE in entries and PROVENANCE_RESOURCE in entries, "incomplete installed sky resource pair")
    start, size = entries[PROVENANCE_RESOURCE]
    try: probe = json.loads(raw[start:start + size])
    except (ValueError, UnicodeError) as error: raise BuildError("Default sky resources: malformed installed provenance") from error
    if isinstance(probe, dict) and probe.get("recipeKey") != expected["recipeKey"]: return None
    provenance_raw = raw[start:start + size]
    value = _metadata(provenance_raw, expected)
    start, size = entries[BUNDLE_RESOURCE]; bank = raw[start:start + size]
    require(value.get("bundle") == {"path": BANK, "size": len(bank), "sha256": _sha(bank)}
        and value.get("nativePlatformAudit") == inspect_bank(bank, source), "installed sky bank changed")
    folder = output / CACHE_ROOT / expected["recipeKey"] / value["producerKey"]
    retained, repair = _known_cache(folder, expected, source, output)
    if retained: return retained
    result = _publish(bank, expected, value["basis"], source, output, repair=repair, provenance_raw=provenance_raw)
    result["reused"] = True
    return result


def _pc_assets(game):
    import UnityPy
    for name, expected in PINS.items():
        require(digest(_ordinary_owned(game / name)) == expected, "owned PC sky source changed: " + name)
    files = {name: UnityPy.load(str(game / name)).assets[0] for name in ("sharedassets8.assets", "Resources/unity_builtin_extra")}
    assets = {}
    for role, name, pid in (("material", "sharedassets8.assets", 4), ("noise", "sharedassets8.assets", 6),
            ("sky", "sharedassets8.assets", 8), ("mesh", "sharedassets8.assets", 12), ("shader", "sharedassets8.assets", 18),
            ("diffuse", "Resources/unity_builtin_extra", 7), ("vertex-lit", "Resources/unity_builtin_extra", 6)):
        obj = files[name].objects[pid]
        asset = _descriptor(name, obj)
        stream = asset["tree"].get("m_StreamData", {})
        if stream.get("size"):
            require(stream["path"] == "sharedassets8.assets.resS" and 0 < stream["size"] <= MAX_OBJECT, "unexpected PC sky stream")
            with (game / stream["path"]).open("rb") as f:
                f.seek(stream["offset"]); asset["stream"] = f.read(stream["size"])
            require(len(asset["stream"]) == stream["size"], "truncated original PC sky stream")
        assets[role] = asset
    return assets


def _seed_native(cache, seed, native, assets):
    for name in ("bound", "translated", "interfaces", "forms", "tools", "tools/quest-converters-win64-v1"):
        path = _ordinary_owned(cache / name)
        require(not path.exists() or path.is_dir(), "native binding cache directory has unknown contents")
    for name in ("progress.json", "original-shader-stages.json", "original-shader-inventory.json"):
        path = _ordinary_owned(cache / name)
        require(not path.exists() or path.is_file(), "native binding report has unknown contents")
    seed = Path(seed) if seed is not None else None
    for role in SHADERS:
        for p in native.original_programs(assets[role]["object"].read()):
            dxbc, interface = p["originalDxbcSha256"], value_hash(p["interface"])
            _ordinary_owned(cache / "interfaces" / (interface + ".json"))
            for directory, name in (("bound", dxbc + "-" + interface + ".hlsl"), ("bound", dxbc + "-" + interface + ".json"),
                    ("translated", dxbc + ".dxbc"), ("translated", dxbc + ".hlsl"), ("translated", dxbc + ".spv")):
                old, new = seed / directory / name if seed is not None else None, cache / directory / name
                _ordinary_owned(new)
                require(not new.exists() or new.is_file(), "native program cache position has unknown contents")
                if old is not None and old.is_file() and not new.exists():
                    new.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(old, new)


def _work_root(output, expected, producer_key):
    work = _ordinary_owned(output / "work" / ("sky-" + producer_key[:12]))
    owner = {"schema": 1, "scope": WORK_SCOPE, "recipeKey": expected["recipeKey"], "producerKey": producer_key}
    if work.exists():
        require(work.is_dir(), "short sky workspace is not a directory")
        marker = _ordinary_owned(work / "owner.json")
        require(marker.is_file() and marker.stat().st_size <= 4096, "short sky workspace has no finite owner")
        try: previous = json.loads(marker.read_text())
        except (ValueError, UnicodeError) as error: raise BuildError("Default sky resources: invalid short workspace owner") from error
        require(previous == owner, "short sky workspace belongs to a different full native identity")
    else:
        work.mkdir(parents=True); write_json(work / "owner.json", owner)
    cache = _ordinary_owned(work / "native-cache")
    require(not cache.exists() or cache.is_dir(), "native binding cache is not a directory")
    return work


def _windows_work_budget(output, producer_key):
    """Keep real native includes/metas below the ordinary Win32 tool budget."""
    root = PureWindowsPath(output)
    require(root.is_absolute() and ".." not in root.parts and not str(root).startswith(("\\\\?\\", "\\\\.\\")),
        "native sky Windows work requires an ordinary absolute output root")
    work = root / "work" / ("sky-" + producer_key[:12])
    unity = work / "unity-01234567"
    program = PureWindowsPath("Assets/QuestOriginalCampaign/ShaderPrograms/" + "f" * 64 + ".hlsl.meta")
    paths = [unity / "native-overlay" / program, unity / program,
        unity / "native-source/Assets/QuestDefaultSky/vertex-lit.shader.meta",
        work / "native-cache/bound" / ("f" * 64 + "-" + "e" * 64 + ".hlsl")]
    maximum = max(len(str(p).encode("utf-16-le")) // 2 for p in paths)
    require(maximum < 260, "native sky files exceed the Win32 budget; select a shorter output root such as C:\\q, without rebuilding game content")
    return {"maximumPathUtf16Units": maximum}


def _decode_bc1_mips(asset):
    """Preserve every authored mip and native row order; never regenerate mips."""
    import texture2ddecoder
    tree, raw = asset["tree"], _data(asset)
    require(tree["m_TextureFormat"] == 10, "only the pinned original BC1 sky pixels may be decoded")
    output, offset = bytearray(), 0
    for mip in range(tree["m_MipCount"]):
        w, h = max(1, tree["m_Width"] >> mip), max(1, tree["m_Height"] >> mip)
        size = ((w + 3) // 4) * ((h + 3) // 4) * 8
        require(offset + size <= len(raw), "native BC1 mip chain is truncated")
        bgra = texture2ddecoder.decode_bc1(raw[offset:offset + size], w, h)
        require(len(bgra) == w * h * 4, "native BC1 mip decode has wrong dimensions")
        rgba = bytearray(bgra)
        rgba[0::4], rgba[2::4] = bgra[2::4], bgra[0::4]
        output.extend(rgba); offset += size
    require(offset == len(raw), "native BC1 mip chain has trailing bytes")
    result = dict(asset)
    result["tree"] = copy.deepcopy(tree)
    result["tree"]["m_TextureFormat"] = 4
    result["tree"]["m_CompleteImageSize"] = len(output)
    result["tree"]["image data"] = bytes(output)
    result["tree"]["m_StreamData"] = {"offset": 0, "size": 0, "path": ""}
    result["stream"] = b""
    return result


# These are the original VertexLit directional/point/spot instruction/interface
# pairs, translated by the pinned tools. Their signed loop starts at zero,
# breaks before i >= unity_VertexLightParams.x, and increments by one. Thus
# 0 <= i <= INT_MAX-1 at every read; uint(i)+7/15/23/31 cannot wrap. Lighting
# vectors 0..6 are never addressed. The generic binder conservatively includes
# the whole dynamically indexed array and otherwise rejects these empty slots.
# Do not infer additional engine uniforms from an SDK declaration: only these
# unreachable initializer slots may receive zero. Original accesses, bounds
# lowering, declared arrays and all active uniform values remain unchanged.
VERTEX_LIT = {
    "76bac418e0c772673a48bc414c52672c1b5c1bdab50b5d4df4043855f1ea00a9": (
        "fe3f725c2c14772532b292ef8af865eff5060cc3cb72b09ba7777d0d5841e161",
        "2ef201a3d0e622c87b6d9a0ed488f6d8c1a0ceefe05d21a9b965d73798802226", 23),
    "b7f32cf6b14b01c482e80470f9e465408386dc90ed982ad14ad2728d8a617a79": (
        "bd3a89a87e40c63ab48d79bdc9ff09164f965b1db176d8090006628e74e30fa0",
        "f9cf6737dd62cd22b5f59fab5ba02497f56924e967d6ead2a8d4fe6bdf9c3a89", 31),
    "21c7c888a1cb1c1b94c78db2e2cda5dac91b43b2fff56215effad9827c0f6faa": (
        "3b54fb01c25ae844843db024f3cf3470f5aa0c920a22783729d8c3d5e7f1b56b",
        "87b3c49623d00ee4a21804277fd7b77c726e428f8210ac7d56bc152bae4d9404", 39),
}


def _vertex_lit_uniforms(native, original, hlsl, interface, signatures=(), layouts=()):
    identity = value_hash(interface)
    witness = VERTEX_LIT.get(identity)
    require(witness is not None and _sha(hlsl.encode()) == witness[1],
        "VertexLit lighting instruction/interface witness changed; audit the exact native program")
    lighting = next(b for b in interface["buffers"] if b["name"] == "UnityLighting")
    fields = [{"name": name, "type": 0, "rows": 1, "columns": 4, "matrix": False,
               "arraySize": 8, "byteOffset": offset} for name, offset in
              (("unity_LightColor", 112), ("unity_LightPosition", 240),
               ("unity_LightAtten", 368), ("unity_SpotDirection", 496))[:(witness[2] - 7) // 8]]
    require(value_hash({**lighting, "fields": sorted(lighting["fields"], key=lambda f: f["byteOffset"])}) ==
        value_hash({"name": "UnityLighting", "bytes": 768, "fields": fields, "structures": []}),
        "VertexLit lighting field type/offset/16-byte array stride changed")
    require(re.search(r"float4 cb1_0_m0\[" + str(witness[2]) + r"\] : packoffset\(c0\);", hlsl),
        "VertexLit original cbuffer extent changed")
    def components(field):
        result = native.field_components(field)
        if field == fields[0]: result.update({i: "0.0" for i in range(28)})
        return result
    scoped = types.FunctionType(original.__code__, {**original.__globals__, "field_components": components},
                                original.__name__, original.__defaults__, original.__closure__)
    bound, observed = scoped(hlsl, interface, signatures, layouts)
    for row in observed:
        if row["originalBuffer"] == "UnityLighting":
            row["skyUnreachableInitializerScalars"] = list(range(28))
            row["skyOriginalDxbcSha256"] = witness[0]
            row["skyOriginalInterfaceSha256"] = identity
            row["skyMinimumReadVector"] = 7
    return bound, observed


def _install_sky_binder(native, assets):
    require(_sha(assets["vertex-lit"]["object"].get_raw_data()) ==
        "0d0aa1a0490ea849e006ae8054d416724741b5dc804fb1c7529bf8d91a91887a",
        "VertexLit original object changed")
    pairs = {(p["originalDxbcSha256"], value_hash(p["interface"]))
             for p in native.original_programs(assets["vertex-lit"]["object"].read())}
    require(all((v[0], key) in pairs for key, v in VERTEX_LIT.items()), "VertexLit original program closure changed")
    original = native.restore_uniforms
    def restore_uniforms(hlsl, interface, input_signature=(), resource_layouts=()):
        key = value_hash(interface)
        if key in VERTEX_LIT or _sha(hlsl.encode()) in {v[1] for v in VERTEX_LIT.values()}:
            bound, observed = _vertex_lit_uniforms(native, original, hlsl, interface, input_signature, resource_layouts)
        else:
            bound, observed = original(hlsl, interface, input_signature, resource_layouts)
        # Unity's include omits these legacy engine-populated uniforms. Declare
        # exactly their original metadata types, without substituting values.
        if key in VERTEX_LIT: bound = "int4 unity_VertexLightParams;\n" + bound
        if key == "2495e6cb014dd52e45eb43f96c11b95d8f5120ef0c0f680257a508f3e51d843b":
            bound = "float4 unity_Lightmap_ST;\n" + bound
        return bound, observed
    native.restore_uniforms = restore_uniforms


def _sky_import_tags(producer):
    # Compiled native metadata uppercases LightMode. ShaderLab import requires
    # its public key/value spelling to identify SVC pass types. This recovers
    # the same known semantic enum; it does not relabel or remove any pass.
    modes = {v.upper(): v for v in ("ForwardBase", "ForwardAdd", "ShadowCaster", "Deferred",
        "PrePassBase", "PrePassFinal", "Vertex", "VertexLM", "VertexLMRGBM", "Always")}
    shader_source = producer.shader_source
    def source(form, record, cache, includes, graphics_api="Vulkan"):
        # restore_project clears its module cache first. Bind after that clear,
        # inside each finite shader emission; restore the shared function after.
        native = producer.recovery_module()
        original = native.tags
        def tags(value):
            result = copy.deepcopy(value)
            require(all(str(v).upper() in modes for k, v in value.get("tags", []) if k.upper() == "LIGHTMODE"),
                "unknown native sky LightMode cannot be imported")
            result["tags"] = [("LightMode", modes[str(v).upper()]) if k.upper() == "LIGHTMODE" else (k, v)
                              for k, v in value.get("tags", [])]
            return original(result)
        native.tags = tags
        try: return shader_source(form, record, cache, includes, graphics_api)
        finally: native.tags = original
    producer.shader_source = source


def _produce_pc(builder, source, output, expected, game, editor, native_cache, project):
    # The prepared complete project is deliberately not imported. Its presence
    # does not authorize scene recovery or whole-game shader/texture compilation.
    assets = _pc_assets(game)
    _asset_contract(assets, source, android=False)
    basis = _basis(assets, "owned-pc-native-vulkan")
    producer_key = value_hash({"recipeKey": expected["recipeKey"], "basis": basis})
    folder = _ordinary_owned(output / CACHE_ROOT / expected["recipeKey"] / producer_key)
    retained, repair = _known_cache(folder, expected, source, output)
    if retained: return retained
    folder.mkdir(parents=True, exist_ok=True)
    if os.name == "nt": _windows_work_budget(output, producer_key)
    native = _load(source, "tools/quest-builder/full_shaders.py")
    _install_sky_binder(native, assets)
    work = _work_root(output, expected, producer_key)
    unity = work / ("unity-" + uuid.uuid4().hex[:8])
    unity.mkdir()
    project_source, cache, overlay = unity / "native-source", work / "native-cache", unity / "native-overlay"
    identities = []
    for role, name in SHADERS.items():
        path = "Assets/QuestDefaultSky/" + role + ".shader"
        guid = _sha((SCOPE + ":" + role).encode())[:32]
        placeholder = project_source / path; placeholder.parent.mkdir(parents=True, exist_ok=True)
        placeholder.write_text('Shader "' + name + '" {}\n')
        placeholder.with_suffix(".shader.meta").write_text("fileFormatVersion: 2\nguid: " + guid + "\n")
        obj = assets[role]["object"]
        identities.append({"guid": guid, "path": path, "objects": [{"collection": obj.assets_file.name, "pathId": obj.path_id, "classId": 48}]})
    _seed_native(cache, native_cache, native, assets)
    converters = _load(source, "tools/quest-shaders/converters.py")
    tools = converters.ensure(cache / "tools", source / "prebuilt/quest-converters-win64-v1.zip")
    inventory = native.inventory(project_source, game, identities,
        {assets[r]["object"].assets_file.name: assets[r]["owner"] for r in SHADERS}, cache, bind_programs=True, **tools)
    require(not inventory.get("errors") and not inventory.get("blockedShaderCount") and len(inventory["shaders"]) == 3,
        "bounded three-shader native recovery failed")
    producer = _load(source, "tools/quest-shaders/produce.py", "tools/quest-shaders")
    _sky_import_tags(producer)
    restored = producer.restore_project(project_source, cache / "original-shader-inventory.json", cache, overlay)
    for name in ("Assets/Editor", "Packages", "ProjectSettings"): (unity / name).mkdir(parents=True, exist_ok=True)
    compiler_files = {p["assetPath"] for p in restored["programs"]}
    compiler_files.update(s["assetPath"] for s in restored["shaders"])
    compiler_files.update(s["assetPath"] + ".meta" for s in restored["shaders"])
    for relative in sorted(compiler_files):
        require(relative.startswith("Assets/") and ".." not in PurePosixPath(relative).parts, "native compiler source escaped its private Assets")
        target = unity / relative; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(_ordinary_owned(overlay / relative), target)
    shutil.copyfile(source / EDITOR, unity / "Assets/Editor/DefaultSkyResourceBuilder.cs")
    write_json(unity / "Packages/manifest.json", {"dependencies": {
        "com.unity.modules.assetbundle": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0"}})
    (unity / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    write_json(unity / "sky-shaders.json", restored)
    bundles = unity / "bundles"
    suffix = expected["recipeKey"][:12] + "-" + producer_key[:12]
    unity_log = output / "logs" / ("default-sky-unity-" + suffix + ".log")
    launch_log = output / "logs" / ("default-sky-launch-" + suffix + ".log")
    unity_log.parent.mkdir(parents=True, exist_ok=True)
    builder.command(builder.unity_launcher(editor, graphics=True) + ["-projectPath", str(unity), "-buildTarget", "Android",
        "-executeMethod", "DefaultSkyResourceBuilder.Build", "-skyManifest", str(unity / "sky-shaders.json"),
        "-skyOutput", str(bundles), "-logFile", str(unity_log)], launch_log)
    import UnityPy
    compiled = UnityPy.load(str(bundles / "native-sky-shaders.bundle"))
    shader_roots = {"assets/questdefaultsky/" + role + ".shader": role for role in SHADERS}
    require({p for p, o in compiled.container.items() if o.type.name == "Shader"} == set(shader_roots),
        "tiny compiler did not deliver exactly the three explicit native shader roots")
    auditor = _load(source, "tools/quest-builder/campaign_native_shaders.py")
    for path, role in shader_roots.items():
        assets[role] = _descriptor("compiled-native-vulkan", compiled.container[path].deref())
        contract = next(s for s in restored["shaders"] if s["originalName"] == SHADERS[role])
        auditor.require_closure(assets[role]["tree"], contract)
    for role in ("sky", "noise"): assets[role] = _decode_bc1_mips(assets[role])
    _asset_contract(assets, source)
    require(plan(source) == expected and all(digest(game / name) == sha for name, sha in PINS.items()), "source changed during tiny compilation")
    bank = _pack(assets, source, producer_key)
    proof = [{"name": s["originalName"], "originalObjectSha256": s["originalObjectSha256"],
        "originalParsedFormSha256": s["originalParsedFormSha256"], "originalDxbcSha256": sorted(s["originalDxbcSha256"]),
        "aliases": [{k: v[k] for k in ("subshader", "pass", "stage", "hardwareTier", "keywords", "originalDxbcSha256", "originalInterfaceSha256")}
                    for v in s["variants"]]} for s in inventory["shaders"]]
    write_json(output / "logs" / ("default-sky-native-proof-" + suffix + ".json"), {"schema": 1, "recipeKey": expected["recipeKey"],
        "producerKey": producer_key, "nativeRestoration": proof, "compilerManifest": restored,
        "nativeInventorySha256": digest(cache / "original-shader-inventory.json")})
    return _publish(bank, expected, basis, source, output, repair=repair, work_relative=work.relative_to(output).as_posix(),
        acquisition={"kind": "three-shader-only", "nativeRestorationKey": value_hash(proof),
        "originalAliases": sum(len(s["variants"]) for s in inventory["shaders"]),
        "nativeShaders": 3, "sourceMipChainPreserved": True, "pcBc1DecodedToLosslessRgba32": True})


def produce(builder, source, output, *, project=None, game=None, editor=None, base_apk=None,
            source_package=None, native_cache=None, source_files=None):
    """Prepare a resource pair. Root embeds both before every managed weave.

    Installed player extraction is preferred and needs no Editor, PC game,
    retained project, external Campaign archive or new native shader compile.
    The containing Builder operation owns process reaping and cache cleanup.
    """
    source, output = Path(source), Path(output)
    expected = plan(source, source_files)
    cache = output / CACHE_ROOT / expected["recipeKey"]
    candidates = sorted(p for p in cache.iterdir() if re.fullmatch(r"[0-9a-f]{64}", p.name)) if cache.is_dir() else []
    require(len(candidates) <= 16, "ambiguous excessive private sky bases")
    # A selected installed resource carries the strongest exact retained basis.
    if base_apk is not None:
        with zipfile.ZipFile(_ordinary_owned(Path(base_apk))) as apk:
            names = [i.filename for i in apk.infolist()]
            require(len(names) <= 200000 and len(names) == len(set(names)), "duplicate or excessive installed APK entries")
            retained = _installed_pair(apk, source, expected, output)
            if retained: return retained
            manifest = [i for i in apk.infolist() if i.filename == "assets/Quest/input-manifest.json"]
            require(len(manifest) == 1 and manifest[0].file_size <= 8 * 1048576, "installed player lacks bounded original input identities")
            original = json.loads(apk.read(manifest[0]))
            rows = original.get("game", {}).get("files", [])
            require(isinstance(rows, list) and len(rows) <= 200000 and all(isinstance(r, dict)
                and isinstance(r.get("path"), str) and re.fullmatch(r"[0-9a-f]{64}", r.get("sha256", "")) for r in rows),
                "invalid original player source roster")
            game_rows = {r["path"]: r["sha256"] for r in rows}
            require(len(rows) == len(game_rows), "duplicate original player source identities")
            require(all(game_rows.get(k) == v for k, v in PINS.items()), "installed original sky sources differ; keep this installation and audit its native data")
            try:
                assets, acquisition = _android_assets(apk)
                _asset_contract(assets, source)
            except BuildError:
                raise
            except Exception as error:
                raise BuildError("Default sky resources: unsupported installed player sky data; retain the current installation: " + str(error)) from error
            basis = _basis(assets, "retained-android-player")
            producer_key = value_hash({"recipeKey": expected["recipeKey"], "basis": basis})
            retained, repair = _known_cache(cache / producer_key, expected, source, output)
            if retained: return retained
            bank = _pack(assets, source, producer_key)
            suffix = expected["recipeKey"][:12] + "-" + producer_key[:12]
            write_json(output / "logs" / ("default-sky-player-reads-" + suffix + ".json"), acquisition)
            return _publish(bank, expected, basis, source, output, acquisition={"kind": "retained-android-player", "nativeAssets": 7,
                "sourceMipChainPreserved": True, "nativeProgramsCopiedExactly": True}, repair=repair)
    if len(candidates) == 1:
        _guard_pair(candidates[0])
        try:
            retained = _validate(candidates[0], expected, source, output)
        except BuildError:
            if game is None or editor is None or builder is None: raise
            retained = None # Actual PC identity below decides its one owned repair target.
        if retained: return retained
    require(not candidates or game is not None and editor is not None and builder is not None,
        "multiple retained sky bases; select the matching installed APK")
    require(game is not None and editor is not None and builder is not None,
        "select the installed Quest APK for bounded native sky extraction, or the owned PC game and pinned Editor for first generation; no full-game rebuild is required")
    require(plan(source) == expected, "producer snapshot bytes differ from their immutable roster")
    return _produce_pc(builder, source, output, expected, Path(game), Path(editor), native_cache, project)
