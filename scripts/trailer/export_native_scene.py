#!/usr/bin/env python3
"""Export two original actors for private trailer staging, without modifying game data.

UnityPy 1.25.2 is required. The exported OBJ is Blender Z-up, right handed:
Unity (x, y, z) -> (-x, -z, y), with reversed triangle winding. Native generic
animation curves at t=0 and the original skin matrices provide a real idle pose.
This produces a geometry reconstruction, never a gameplay capture.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import zlib

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


ACTORS = {
    "brute": {
        "bank": "hero_brute_assets_all.bundle",
        "root": "HE_Brute_PR",
        "clip": "HE_Brute_Idle_v001",
        "meshes": ("HE_Brute_Mesh", "HE_Brute_Skirt", "HE_Brute_Cloak", "HE_Brute_Cloak_Top"),
        "weapons": {"WP_Brute_Sword_01": "R_weaponSkel_JNT",
                    "WP_Brute_Shield_01": "L_weaponSkel_JNT"},
        "companions": (),
    },
    "livingbones": {
        "bank": "npc_livingbones_assets_all.bundle",
        "root": "HM_MO_LivingBones",
        "clip": "LivingBones_idle",
        "meshes": ("MO_LivingBones", "MO_LivingBones_Cloth1"),
        "weapons": {"WP_LIvingBones_Sword_01": "R_weaponSkel01_JNT"},
        "companions": ("pcg_materials_assets_hm_mo_livingbones_mat.bundle",),
    },
}


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def safe(name):
    return re.sub(r"[^A-Za-z0-9_.-]+", "_", name)


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def trs(tree):
    q = tree["m_LocalRotation"]
    x, y, z, w = (q[key] for key in "xyzw")
    length = math.sqrt(x*x + y*y + z*z + w*w)
    x, y, z, w = (v / length for v in (x, y, z, w))
    result = [
        [1 - 2*y*y - 2*z*z, 2*x*y - 2*z*w, 2*x*z + 2*y*w, 0],
        [2*x*y + 2*z*w, 1 - 2*x*x - 2*z*z, 2*y*z - 2*x*w, 0],
        [2*x*z - 2*y*w, 2*y*z + 2*x*w, 1 - 2*x*x - 2*y*y, 0],
        [0, 0, 0, 1],
    ]
    for i in range(3):
        result[i][3] = tree["m_LocalPosition"]["xyz"[i]]
        for j in range(3):
            result[i][j] *= tree["m_LocalScale"]["xyz"[j]]
    return result


def transform(matrix, vector, position=True):
    return tuple(sum(matrix[i][j] * vector[j] for j in range(3))
                 + (matrix[i][3] if position else 0) for i in range(3))


def blender(vector):
    return (-vector[0], -vector[2], vector[1])


def bounds(vertices):
    return [[min(v[i] for v in vertices) for i in range(3)],
            [max(v[i] for v in vertices) for i in range(3)]]


def first_frame(clip):
    """Read the t=0 streamed, dense and constant values of a generic native clip.

    Each streamed key is index + four cubic coefficients. The final coefficient
    is the value exactly at that key. We deliberately require an actual t=0
    frame instead of implementing approximate interpolation.
    """
    data = clip["m_MuscleClip"]["m_Clip"]["data"]
    stream = data["m_StreamedClip"]
    raw = struct.pack("<" + "I" * len(stream["data"]), *stream["data"])
    offset = 0
    streamed = {}
    found = False
    while offset < len(raw):
        time, count = struct.unpack_from("<fi", raw, offset)
        offset += 8
        for unused in range(count):
            index, a, b, c, value = struct.unpack_from("<iffff", raw, offset)
            offset += 20
            if time <= 0:
                streamed[index] = value
        if time == 0:
            found = True
            break
    if stream["curveCount"] and not found:
        raise ValueError("Native clip has no t=0 key frame; refusing an approximate pose")
    if set(streamed) != set(range(stream["curveCount"])):
        raise ValueError("Incomplete native t=0 streamed curves")
    dense = data["m_DenseClip"]
    if dense["m_CurveCount"] and dense["m_BeginTime"] != 0:
        raise ValueError("Native dense curves do not start at t=0")
    values = [streamed[i] for i in range(stream["curveCount"])]
    values.extend(dense["m_SampleArray"][:dense["m_CurveCount"]])
    values.extend(data["m_ConstantClip"]["data"])
    result = {}
    cursor = 0
    for binding in clip["m_ClipBindingConstant"]["genericBindings"]:
        attribute = binding["attribute"]
        dimensions = {1: 3, 2: 4, 3: 3, 4: 3}.get(attribute, 1) if binding["typeID"] == 4 else 1
        value = values[cursor:cursor + dimensions]
        if len(value) != dimensions:
            raise ValueError("Native curve count does not agree with binding dimensions")
        cursor += dimensions
        if binding["typeID"] == 4:
            if attribute not in (1, 2, 3):
                raise ValueError("Euler generic curves require a separate native evaluation")
            result[binding["path"], attribute] = value
    if cursor != len(values):
        raise ValueError("Unbound native animation values")
    return result


class Actor:
    def __init__(self, base, output, name):
        self.base, self.output, self.name = base, output / name, name
        self.config = ACTORS[name]
        self.output.mkdir(parents=True, exist_ok=True)
        self.banks = []
        self.sources = []
        for filename in (self.config["bank"],) + self.config["companions"]:
            source = base / filename
            environment = UnityPy.load(str(source))
            objects = {o.path_id: o for o in environment.objects}
            self.banks.append((source, objects))
            self.sources.append({"path": str(source), "sha256": digest(source),
                                 "bytes": source.stat().st_size})
        self.objects = self.banks[0][1]
        self.transforms = {o.path_id: o.read_typetree() for o in self.objects.values()
                           if o.type.name == "Transform"}
        self.names = {i: self.objects[t["m_GameObject"]["m_PathID"]].read().m_Name
                      for i, t in self.transforms.items()}
        self.paths = {}
        for index in self.transforms:
            parts, current = [], index
            while current:
                parts.append(self.names[current])
                current = self.transforms[current]["m_Father"]["m_PathID"]
            self.paths[index] = "/".join(reversed(parts))
        self.clip_object = next(o for o in self.objects.values()
                                if o.type.name == "AnimationClip" and o.read().m_Name == self.config["clip"])
        curves = first_frame(self.clip_object.read_typetree())
        self.sampled = 0
        for index, tree in self.transforms.items():
            if not self.paths[index].startswith(self.config["root"] + "/"):
                continue
            names = self.paths[index].split("/")
            for begin in range(1, len(names)):
                hashed = zlib.crc32("/".join(names[begin:]).encode())
                for attribute, field in ((1, "m_LocalPosition"), (2, "m_LocalRotation"), (3, "m_LocalScale")):
                    value = curves.get((hashed, attribute))
                    if value is not None:
                        tree[field] = dict(zip("xyzw", value))
                        self.sampled += 1
        if self.sampled < 30:
            raise ValueError("Too few native transforms matched the original idle clip")
        self.worlds, self.materials, self.textures, self.parts = {}, {}, {}, []

    def world(self, index):
        if index not in self.worlds:
            tree = self.transforms[index]
            parent = tree["m_Father"]["m_PathID"]
            self.worlds[index] = multiply(self.world(parent), trs(tree)) if parent else trs(tree)
        return self.worlds[index]

    def resolve(self, pointer, current_bank=0):
        if not pointer["m_PathID"]:
            return None
        if pointer["m_FileID"] == 0:
            return current_bank, self.banks[current_bank][1][pointer["m_PathID"]]
        candidates = [(i, objs[pointer["m_PathID"]]) for i, (unused, objs) in enumerate(self.banks)
                      if i != current_bank and pointer["m_PathID"] in objs]
        if len(candidates) != 1:
            raise ValueError("Unresolved or ambiguous selected native dependency: " + str(pointer))
        return candidates[0]

    def material(self, pointer, current_bank=0):
        bank, obj = self.resolve(pointer, current_bank)
        key = (bank, obj.path_id)
        if key in self.materials:
            return self.materials[key]["obj_name"]
        tree = obj.read_typetree()
        name = safe(tree["m_Name"])
        record = {"obj_name": name, "source_name": tree["m_Name"], "path_id": obj.path_id,
                  "source_bank": str(self.banks[bank][0]), "textures": {},
                  "original_properties": tree["m_SavedProperties"]}
        for field, texture in tree["m_SavedProperties"]["m_TexEnvs"]:
            ref = texture["m_Texture"]
            if not ref["m_PathID"]:
                continue
            tex_bank, native = self.resolve(ref, bank)
            tex_key = (tex_bank, native.path_id)
            if tex_key not in self.textures:
                tex = native.read()
                image_path = self.output / (safe(tex.m_Name) + ".png")
                tex.image.save(image_path)
                self.textures[tex_key] = {"source_name": tex.m_Name, "path_id": native.path_id,
                    "source_bank": str(self.banks[tex_bank][0]), "output": str(image_path),
                    "width": tex.m_Width, "height": tex.m_Height, "sha256": digest(image_path)}
            record["textures"][field] = self.textures[tex_key]
        self.materials[key] = record
        return name

    def renderer(self, obj):
        tree = obj.read_typetree()
        mesh = self.objects[tree["m_Mesh"]["m_PathID"]].read()
        handler = MeshHandler(mesh)
        handler.process()
        transform_id = next(c["component"]["m_PathID"]
                            for c in self.objects[tree["m_GameObject"]["m_PathID"]].read_typetree()["m_Component"]
                            if self.objects[c["component"]["m_PathID"]].type.name == "Transform")
        attachment = self.config["weapons"].get(mesh.m_Name)
        extra = identity()
        if attachment:
            target = next(i for i, name in self.names.items() if name == attachment
                          and self.paths[i].startswith(self.config["root"] + "/"))
            extra = self.world(target)
        matrices = []
        for pointer, bind in zip(tree["m_Bones"], mesh.m_BindPose):
            bind_matrix = [[getattr(bind, "e" + str(i) + str(j)) for j in range(4)] for i in range(4)]
            matrices.append(multiply(extra, multiply(self.world(pointer["m_PathID"]), bind_matrix)))
        vertices, normals = [], []
        if len(matrices) != len(tree["m_Bones"]):
            raise ValueError("Original bone and bind matrix counts disagree")
        bone_weights = handler.m_BoneWeights
        if bone_weights is None:
            # Unity omits the weights channel for a single influence per vertex.
            if not handler.m_BoneIndices or any(len(indices) != 1 for indices in handler.m_BoneIndices):
                raise ValueError("Missing native weights for a multiple-influence mesh")
            bone_weights = [(1.0,)] * len(handler.m_Vertices)
        for index, vertex in enumerate(handler.m_Vertices):
            weights, indices = bone_weights[index], handler.m_BoneIndices[index]
            position = [0.0] * 3
            normal = [0.0] * 3
            for weight, bone in zip(weights, indices):
                if weight <= 0:
                    continue
                p = transform(matrices[bone], vertex)
                n = transform(matrices[bone], handler.m_Normals[index], False)
                for axis in range(3):
                    position[axis] += weight * p[axis]
                    normal[axis] += weight * n[axis]
            length = math.sqrt(sum(v*v for v in normal))
            vertices.append(blender(position))
            normals.append(blender([v / length for v in normal]))
        if not all(math.isfinite(v) for vector in vertices for v in vector):
            raise ValueError("Non-finite native skinned vertices")
        materials = [self.material(pointer) for pointer in tree["m_Materials"]]
        submeshes = handler.get_triangles()
        if len(materials) != len(submeshes):
            raise ValueError("Original submesh and material slots disagree")
        record = {"mesh_name": mesh.m_Name, "mesh_path_id": mesh.object_reader.path_id,
            "renderer_path_id": obj.path_id, "hierarchy": self.paths[transform_id],
            "vertices": len(vertices), "triangles": sum(len(f) for f in submeshes),
            "blender_bounds": bounds(vertices), "materials": materials,
            "attachment_bone": attachment, "bone_path_ids": [p["m_PathID"] for p in tree["m_Bones"]],
            "bind_matrices_unity": [[[getattr(b, "e" + str(i) + str(j)) for j in range(4)]
                                     for i in range(4)] for b in mesh.m_BindPose],
            "weights": bone_weights, "bone_indices": handler.m_BoneIndices}
        self.parts.append(record)
        return vertices, normals, handler.m_UV0, submeshes, materials

    def export(self):
        obj_path = self.output / (self.name + "_idle.obj")
        mtl_path = obj_path.with_suffix(".mtl")
        lines = ["# Original native geometry, sampled idle t=0; reconstructed trailer staging.\n",
                 "# Blender Z-up: Unity (x,y,z) -> (-x,-z,y); original units preserved.\n",
                 "mtllib " + mtl_path.name + "\n"]
        offset, all_vertices = 0, []
        for obj in self.objects.values():
            if obj.type.name != "SkinnedMeshRenderer":
                continue
            tree = obj.read_typetree()
            mesh_name = self.objects[tree["m_Mesh"]["m_PathID"]].read().m_Name
            if mesh_name not in self.config["meshes"] and mesh_name not in self.config["weapons"]:
                continue
            # Only the original high-detail renderer, never sibling LOD copies.
            if mesh_name in self.config["meshes"]:
                go = self.objects[tree["m_GameObject"]["m_PathID"]].read()
                tr = next(c.component.path_id for c in go.m_Component if c.component.type.name == "Transform")
                if not self.paths[tr].startswith(self.config["root"] + "/"):
                    continue
            vertices, normals, uvs, submeshes, materials = self.renderer(obj)
            lines.append("o " + safe(mesh_name) + "\n")
            lines.extend("v %.9g %.9g %.9g\n" % v for v in vertices)
            lines.extend("vt %.9g %.9g\n" % tuple(uv[:2]) for uv in uvs)
            lines.extend("vn %.9g %.9g %.9g\n" % v for v in normals)
            for slot, triangles in enumerate(submeshes):
                lines.append("g " + safe(mesh_name) + "_" + str(slot) + "\n")
                lines.append("usemtl " + materials[slot] + "\n")
                for a, b, c in triangles:
                    face = (c + offset + 1, b + offset + 1, a + offset + 1)
                    lines.append("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n".format(*face))
            offset += len(vertices)
            all_vertices.extend(vertices)
        if len(self.parts) < len(self.config["meshes"]):
            raise ValueError("Original requested actor parts missing")
        obj_path.write_text("".join(lines))
        mtl = []
        for record in self.materials.values():
            mtl.extend(["newmtl " + record["obj_name"] + "\n", "Kd 1 1 1\n", "Ks 0.12 0.12 0.12\n",
                        "Ns 32\n", "illum 2\n", "d 1\n"])
            textures = record["textures"]
            diffuse = textures.get("_Diffuse") or textures.get("_MainTex")
            if diffuse:
                mtl.append("map_Kd " + Path(diffuse["output"]).name + "\n")
            normal = textures.get("_NormalMap") or textures.get("_BumpMap")
            if normal:
                mtl.append("map_Bump " + Path(normal["output"]).name + "\n")
        mtl_path.write_text("".join(mtl))
        rig_path = self.output / "native_rig.json"
        retained = {i for i in self.transforms
                    if self.paths[i].startswith(self.config["root"] + "/")}
        for part in self.parts:
            for bone in part["bone_path_ids"]:
                while bone:
                    retained.add(bone)
                    bone = self.transforms[bone]["m_Father"]["m_PathID"]
        rig = {"coordinate_space": "original Unity left-handed Y-up", "sample_seconds": 0,
               "transforms": [{"path_id": i, "name": self.names[i], "path": self.paths[i],
                    "local": tree, "world_matrix_unity": self.world(i)}
                   for i, tree in self.transforms.items()
                   if i in retained], "parts": self.parts}
        rig_path.write_text(json.dumps(rig, indent=2) + "\n")
        manifest = {"actor": self.name, "representation": "geometry reconstruction, not gameplay footage",
            "coordinate_conversion": "Unity (x,y,z) -> Blender (-x,-z,y), reversed triangle winding",
            "original_units_preserved": True, "blender_up_axis": "Z",
            "converted_unity_positive_z_axis": "-Y", "actor_anatomical_forward_axis": "-Y",
            "sources": self.sources, "unitypy_version": UnityPy.__version__,
            "native_pose": {"clip": self.config["clip"], "clip_path_id": self.clip_object.path_id,
                            "seconds": 0, "sampled_transform_properties": self.sampled,
                            "evaluation": "t=0 generic keys, original hierarchy and linear skinning"},
            "bounds_blender": bounds(all_vertices), "obj": str(obj_path), "mtl": str(mtl_path),
            "rig": str(rig_path), "textures": list(self.textures.values()),
            "materials": list(self.materials.values()),
            "parts": [{key: value for key, value in part.items()
                       if key not in ("weights", "bone_indices", "bind_matrices_unity")} for part in self.parts],
            "outputs": [{"path": str(path), "sha256": digest(path)} for path in (obj_path, mtl_path, rig_path)]}
        manifest_path = self.output / "manifest.json"
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
        return {"actor": self.name, "obj": str(obj_path), "manifest": str(manifest_path),
                "vertices": offset, "bounds": manifest["bounds_blender"]}


def export_dungeon(base, output):
    """Export two static original PCG meshes and their matching original material banks.

    PCG template renderers have empty material slots: the native procedural
    builder supplies them later. This is an explicit cinematic pairing of the
    corresponding native stone wall/floor banks, not an evaluated room capture.
    """
    directory = output / "dungeon"
    directory.mkdir(parents=True, exist_ok=True)
    geometry_source = base / "pcg_databases_assets_assets/pcg/pcg_dungeon.asset.bundle"
    environment = UnityPy.load(str(geometry_source))
    objects = {o.path_id: o for o in environment.objects}
    sources = [{"path": str(geometry_source), "sha256": digest(geometry_source)}]
    props = []
    for mesh_name, material_bank in (
        ("CR_Dungeon_Wall_Stone_01", "pcg_materials_assets_cr_dungeon_stonewall_01_mat.bundle"),
        ("CR_Dungeon_Floor_Seg_P", "pcg_materials_assets_cr_dungeon_stonefloor_01_mat.bundle"),
    ):
        source = base / material_bank
        material_environment = UnityPy.load(str(source))
        material_objects = {o.path_id: o for o in material_environment.objects}
        sources.append({"path": str(source), "sha256": digest(source)})
        material_obj = next(o for o in material_objects.values() if o.type.name == "Material")
        # Reuse the actor's read-only texture/material serializer without loading
        # any unrelated actor bank or constructing a native scene.
        context = Actor.__new__(Actor)
        context.output = directory
        context.banks = [(source, material_objects)]
        context.materials, context.textures = {}, {}
        material_name = context.material({"m_FileID": 0, "m_PathID": material_obj.path_id})
        mesh_obj = next(o for o in objects.values()
                        if o.type.name == "Mesh" and o.read().m_Name == mesh_name)
        handler = MeshHandler(mesh_obj.read())
        handler.process()
        vertices = [blender(v) for v in handler.m_Vertices]
        normals = [blender(v) for v in handler.m_Normals]
        obj_path = directory / (safe(mesh_name) + ".obj")
        mtl_path = obj_path.with_suffix(".mtl")
        lines = ["# Original native PCG geometry, cinematic source-material pairing.\n",
                 "mtllib " + mtl_path.name + "\n", "o " + safe(mesh_name) + "\n"]
        lines.extend("v %.9g %.9g %.9g\n" % tuple(v) for v in vertices)
        lines.extend("vt %.9g %.9g\n" % tuple(v[:2]) for v in handler.m_UV0)
        lines.extend("vn %.9g %.9g %.9g\n" % tuple(v) for v in normals)
        lines.append("usemtl " + material_name + "\n")
        triangles = handler.get_triangles()
        for faces in triangles:
            for a, b, c in faces:
                lines.append("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n".format(c+1, b+1, a+1))
        obj_path.write_text("".join(lines))
        material = next(iter(context.materials.values()))
        diffuse = material["textures"]["_MainTex"]
        mtl_path.write_text("newmtl " + material_name + "\nKd 1 1 1\nillum 2\nNs 20\nmap_Kd "
                            + Path(diffuse["output"]).name + "\n")
        props.append({"mesh": mesh_name, "mesh_path_id": mesh_obj.path_id, "obj": str(obj_path),
                      "sha256": digest(obj_path), "vertices": len(vertices),
                      "triangles": sum(len(f) for f in triangles), "blender_bounds": bounds(vertices),
                      "original_units_preserved": True, "material": material,
                      "textures": list(context.textures.values())})
    manifest_path = directory / "manifest.json"
    manifest_path.write_text(json.dumps({"representation": "geometry reconstruction, not gameplay footage",
        "coordinate_conversion": "Unity (x,y,z) -> Blender (-x,-z,y), reversed triangle winding",
        "material_pairing": "Matching original PCG stone-wall/floor material banks; original template slots empty",
        "sources": sources, "props": props}, indent=2) + "\n")
    print(json.dumps({"dungeon_manifest": str(manifest_path),
                      "props": [p["obj"] for p in props]}), flush=True)
    return {"manifest": str(manifest_path), "props": [p["obj"] for p in props]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--actors", default="brute,livingbones")
    parser.add_argument("--dungeon", action="store_true", help="Also export one native wall and floor segment")
    args = parser.parse_args()
    selected = args.actors.split(",") if args.actors else []
    if any(name not in ACTORS for name in selected):
        parser.error("Only the deliberately selected brute,livingbones actors are supported")
    base = args.game_data.resolve() / "StreamingAssets/aa/StandaloneWindows64"
    output = args.output.resolve()
    if base == output or base in output.parents:
        parser.error("Output must be outside the read-only game data")
    output.mkdir(parents=True, exist_ok=True)
    for name in selected:
        result = Actor(base, output, name).export()
        print(json.dumps(result), flush=True)
    dungeon = export_dungeon(base, output) if args.dungeon else None
    # Partial exports retain links to current actor deliverables already present.
    results = []
    for name in ACTORS:
        path = output / name / "manifest.json"
        if path.is_file():
            manifest = json.loads(path.read_text())
            results.append({"actor": name, "obj": manifest["obj"], "manifest": str(path),
                            "vertices": sum(p["vertices"] for p in manifest["parts"]),
                            "bounds": manifest["bounds_blender"]})
    dungeon_path = output / "dungeon/manifest.json"
    if dungeon is None and dungeon_path.is_file():
        manifest = json.loads(dungeon_path.read_text())
        dungeon = {"manifest": str(dungeon_path), "props": [p["obj"] for p in manifest["props"]]}
    (output / "scene_manifest.json").write_text(json.dumps({"actors": results, "dungeon": dungeon,
        "representation": "geometry reconstruction, not gameplay footage",
        "native_action_rule": "Return a held actor to its original cell before any non-idle native action."}, indent=2) + "\n")


if __name__ == "__main__":
    main()
