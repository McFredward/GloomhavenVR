#!/usr/bin/env python3
"""Expose exact native texture objects twice without reimporting their pixels."""
import copy
import hashlib
import json
import sys
from pathlib import Path
import UnityPy
from UnityPy.enums import ClassIDType
from UnityPy.files import SerializedFile
from UnityPy.files.ObjectReader import ObjectReader
from UnityPy.streams import EndianBinaryReader

base, out = map(Path, sys.argv[1:])
out.mkdir(parents=True, exist_ok=True)
targets = {"Default-Particle": ("resources.assets", 207, 64, 64, 4, 7),
           "T_sphere_norm": ("sharedassets1.assets", 364, 512, 512, 4, 10)}
found = {name: [] for name in targets}
sources = {}
for path in sorted(base.glob("*.assets")):
    sources[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    for obj in UnityPy.load(str(path)).objects:
        if obj.type.name != "Texture2D":
            continue
        tree = obj.read_typetree()
        name = tree["m_Name"]
        if name in found:
            found[name].append([path.name, obj.path_id, tree["m_Width"],
                                tree["m_Height"], tree["m_TextureFormat"], tree["m_MipCount"]])
for name, expected in targets.items():
    assert found[name] == [list(expected)], (name, found[name])

wrapper_path = base / "StreamingAssets/aa/StandaloneWindows64/itemconfigs_backgrounds_assets_poisondagger.bundle"
receipts = []
for name, (source, path_id, *_) in targets.items():
    for variant in ("a", "b"):
        native = next(iter(UnityPy.load(str(base / source)).files.values()))
        texture = native.objects[path_id]
        raw_sha = hashlib.sha256(texture.get_raw_data()).hexdigest()
        wrapper = next(iter(UnityPy.load(str(wrapper_path)).files.values()))
        wfile = next(f for f in wrapper.files.values() if isinstance(f, SerializedFile))
        container = next(o for o in wfile.objects.values() if o.type.name == "AssetBundle")
        tree = container.read_typetree()
        tree["m_Name"] = f"npc660-{name}-{variant}"
        tree["m_AssetBundleName"] = tree["m_Name"]
        tree["m_Dependencies"] = []
        tree["m_ExplicitDataLayout"] = 0
        tree["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": path_id}]
        tree["m_Container"] = [("native/texture", {"preloadIndex": 0, "preloadSize": 1,
                                                  "asset": {"m_FileID": 0, "m_PathID": path_id}})]
        tree["m_MainAsset"] = {"preloadIndex": 0, "preloadSize": 0,
                              "asset": {"m_FileID": 0, "m_PathID": 0}}
        container.save_typetree(tree)
        asset_type = copy.copy(container.serialized_type)
        native.types.append(asset_type)
        wrapped = ObjectReader(native, container.reader, 1, len(native.types) - 1,
                               asset_type, container.class_id, ClassIDType.AssetBundle,
                               0, len(container.data), None, None, container.data)
        native.objects = {path_id: texture, 1: wrapped}
        native.script_types = []
        native.externals = []
        used = sorted({o.type_id for o in native.objects.values()})
        remap = {old: new for new, old in enumerate(used)}
        native.types = [native.types[i] for i in used]
        for obj in native.objects.values():
            obj.type_id = remap[obj.type_id]
            if native.types[obj.type_id].node is None:
                native.types[obj.type_id].node = obj._get_typetree_node(None)
        for typ in native.types:
            if typ.type_dependencies is None:
                typ.type_dependencies = []
            for node in typ.node.traverse():
                if node.m_TypeFlags is None:
                    node.m_TypeFlags = 1 if node.m_Type == "Array" else 0
                if node.m_RefTypeHash is None:
                    node.m_RefTypeHash = 0
        native._enable_type_tree = True
        # Distinct containers reproduce separate loaded native wrappers. Only
        # container/type metadata differs; the original Texture2D bytes do not.
        native.name = "CAB-" + hashlib.sha256((source + variant).encode()).hexdigest()[:32]
        native.flags = wfile.flags
        wrapper.files = {native.name: native}
        stream = texture.read().m_StreamData
        if stream.path:
            with (base / stream.path).open("rb") as f:
                data = f.read(stream.offset + stream.size)
            companion = out / stream.path
            if companion.exists():
                assert companion.read_bytes() == data
            else:
                companion.write_bytes(data)
            reader = EndianBinaryReader(data)
            reader.flags = 0
            wrapper.files[stream.path] = reader
        assert hashlib.sha256(texture.get_raw_data()).hexdigest() == raw_sha
        payload = wrapper.save("lz4")
        bank = out / f"{name}-{variant}.bundle"
        bank.write_bytes(payload)
        actual = next(o for o in UnityPy.load(str(bank)).objects if o.type.name == "Texture2D")
        assert hashlib.sha256(actual.get_raw_data()).hexdigest() == raw_sha
        receipts.append({"name": name, "source": source, "path_id": path_id,
                         "object_sha256": raw_sha, "bank": bank.name,
                         "bank_sha256": hashlib.sha256(payload).hexdigest()})
(out / "provenance.json").write_text(json.dumps({"original_assets_census": found,
    "source_sha256": sources, "banks": receipts,
    "boundary": "Only generated outer containers change; original texture object/pixel streams remain exact."}, indent=2) + "\n")
print("Verified two exact native textures, two distinct wrappers each.", flush=True)
