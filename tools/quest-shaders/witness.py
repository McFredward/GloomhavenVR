#!/usr/bin/env python3
"""Capture a private material/mesh's exact route from an original public prefab.

This uses native serialized PPtrs and child/component ordinals, never matching a
material or mesh by its display name. UnityPy is the same read-only parser used
by the asset recovery lane and is an optional host-tool dependency.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys


def _component(pair):
    return pair.component if hasattr(pair, "component") else pair[1]


def _identity(reader):
    return {"serializedFile": reader.assets_file.name, "pathId": reader.path_id, "class": reader.type.name}


def witness(bundle, asset_name, shader_serialized_file, shader_path_id):
    import UnityPy
    environment = UnityPy.load(str(bundle))
    containers = []
    for reader in environment.objects:
        if reader.type.name == "AssetBundle":
            for key, value in reader.read().m_Container:
                if key == asset_name:
                    containers.append(value.asset.deref())
    if len(containers) != 1 or containers[0].type.name != "GameObject":
        raise ValueError("Original public prefab address is absent or ambiguous.")
    root = containers[0]
    rows = []
    visited = set()

    def traverse(reader, children):
        key = (reader.assets_file.name, reader.path_id)
        if key in visited:
            raise ValueError("Original prefab child hierarchy repeats an object.")
        visited.add(key)
        data = reader.read()
        counters = {}
        transform = None
        for component in data.m_Component:
            native = _component(component).deref()
            kind = native.type.name
            component_index = counters.get(kind, 0)
            counters[kind] = component_index + 1
            value = native.read()
            if kind == "Transform":
                transform = value
            if kind not in ("SkinnedMeshRenderer", "MeshRenderer"):
                continue
            for material_index, pointer in enumerate(value.m_Materials):
                if pointer.m_PathID == 0:
                    continue
                material = pointer.deref()
                shader = material.read().m_Shader
                if shader.m_FileID == 0:
                    shader_file = material.assets_file.name
                else:
                    shader_file = material.assets_file.externals[shader.m_FileID - 1].name
                if shader_file != shader_serialized_file or shader.m_PathID != shader_path_id:
                    continue
                route = {"assetName": asset_name, "rootType": "GameObject", "childIndices": list(children),
                         "componentType": kind, "componentIndex": component_index, "materialIndex": material_index}
                mesh_reader = None
                mesh_route = dict(route)
                if kind == "SkinnedMeshRenderer":
                    mesh_reader = value.m_Mesh.deref()
                else:
                    filters = [_component(item).deref() for item in data.m_Component if _component(item).type.name == "MeshFilter"]
                    if len(filters) != 1:
                        raise ValueError("Original material owner lacks a unique typed mesh filter.")
                    mesh_reader = filters[0].read().m_Mesh.deref()
                    mesh_route.update(componentType="MeshFilter", componentIndex=0)
                rows.append({"publicRoot": _identity(root), "owner": _identity(native), "material": _identity(material),
                             "mesh": _identity(mesh_reader), "shader": {"serializedFile": shader_file, "pathId": shader_path_id},
                             "originalMaterial": route, "originalMesh": mesh_route})
        if transform is None:
            raise ValueError("Original prefab hierarchy lacks its native Transform.")
        for index, child in enumerate(transform.m_Children):
            traverse(child.read().m_GameObject.deref(), children + [index])

    traverse(root, [])
    if not rows:
        raise ValueError("The selected original prefab never references the exact selected shader object.")
    return {"schema": 1, "bundle": str(bundle.resolve()), "bundleSha256": hashlib.sha256(bundle.read_bytes()).hexdigest(), "routes": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--asset-name", required=True)
    parser.add_argument("--shader-serialized-file", required=True)
    parser.add_argument("--shader-path-id", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = witness(args.bundle, args.asset_name, args.shader_serialized_file, args.shader_path_id)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print("Exact original prefab material/mesh routes: " + str(len(result["routes"])))


if __name__ == "__main__":
    main()
