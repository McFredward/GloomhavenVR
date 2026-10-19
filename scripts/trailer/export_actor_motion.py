#!/usr/bin/env python3
"""Sample original generic Unity animation and skin it for private trailer staging.

Requires UnityPy 1.25.2 and NumPy. Game banks are only read. Output is a native
geometry/animation reconstruction, not a runtime capture or ability simulation.
"""
import argparse
import bisect
import copy
import json
import math
from pathlib import Path
import struct
import zlib

import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

from export_native_scene import ACTORS, Actor, digest, first_frame, safe


DEFAULT_CLIPS = {
    "brute": ("HE_Brute_Idle_v001", "HE_Brute_Melee_Attack_v001",
              "HE_Brute_Heavy_Melee_Attack_v001"),
    "livingbones": ("LivingBones_idle", "LivingBones_OnHit_v001",
                    "LivingBones_Melee_Attack_v002"),
}


class Clip:
    """Evaluate the original streamed polynomial and constant generic curves."""

    def __init__(self, tree):
        self.tree = tree
        self.duration = tree["m_MuscleClip"]["m_StopTime"]
        data = tree["m_MuscleClip"]["m_Clip"]["data"]
        stream = data["m_StreamedClip"]
        self.streams = [[] for _ in range(stream["curveCount"])]
        raw = struct.pack("<" + "I" * len(stream["data"]), *stream["data"])
        offset = 0
        while offset < len(raw):
            time, count = struct.unpack_from("<fi", raw, offset)
            offset += 8
            for _ in range(count):
                index, a, b, c, value = struct.unpack_from("<iffff", raw, offset)
                offset += 20
                if not 0 <= index < len(self.streams):
                    raise ValueError("Native streamed key has an invalid curve index")
                self.streams[index].append((time, a, b, c, value))
        if offset != len(raw) or any(not keys for keys in self.streams):
            raise ValueError("Native streamed curves are incomplete")
        self.stream_times = [[key[0] for key in keys] for keys in self.streams]
        self.dense = data["m_DenseClip"]
        self.constant = data["m_ConstantClip"]["data"]
        self.bindings = tree["m_ClipBindingConstant"]["genericBindings"]
        self.continuity_error = self.check_continuity()
        expected = first_frame(tree)
        actual = self.evaluate(0)
        self.t0_error = max(abs(value - actual[key][i])
                            for key, values in expected.items()
                            for i, value in enumerate(values))
        if self.t0_error > 1e-7:
            raise ValueError("Sampled t=0 differs from the native key export")

    def check_continuity(self):
        errors = []
        for keys in self.streams:
            for left, right in zip(keys, keys[1:]):
                if left[0] < 0 or right[0] > self.duration + 1e-4:
                    continue
                dt = right[0] - left[0]
                if dt < 0:
                    raise ValueError("Native streamed times are not monotonic")
                predicted = ((left[1] * dt + left[2]) * dt + left[3]) * dt + left[4]
                errors.append(abs(predicted - right[4]))
        maximum = max(errors, default=0)
        if maximum > 0.005:
            raise ValueError("Native streamed polynomial does not reach its next key")
        return maximum

    def evaluate(self, seconds):
        if not math.isfinite(seconds) or seconds < 0 or seconds > self.duration + 1e-5:
            raise ValueError("Requested sample lies outside the original clip")
        values = []
        for times, keys in zip(self.stream_times, self.streams):
            index = max(0, bisect.bisect_right(times, seconds) - 1)
            time, a, b, c, value = keys[index]
            dt = seconds - time
            values.append(((a * dt + b) * dt + c) * dt + value)
        dense = self.dense
        if dense["m_CurveCount"]:
            count = dense["m_CurveCount"]
            position = max(0, (seconds - dense["m_BeginTime"]) * dense["m_SampleRate"])
            frame = min(math.floor(position), dense["m_FrameCount"] - 1)
            following = min(frame + 1, dense["m_FrameCount"] - 1)
            fraction = min(1, position - frame)
            source = dense["m_SampleArray"]
            values.extend(source[frame * count + i] * (1 - fraction)
                          + source[following * count + i] * fraction for i in range(count))
        values.extend(self.constant)
        curves, cursor = {}, 0
        for binding in self.bindings:
            attribute = binding["attribute"]
            dimensions = {1: 3, 2: 4, 3: 3, 4: 3}.get(attribute, 1) if binding["typeID"] == 4 else 1
            value = values[cursor:cursor + dimensions]
            cursor += dimensions
            if len(value) != dimensions:
                raise ValueError("Native curve count disagrees with its bindings")
            if binding["typeID"] == 4:
                if attribute not in (1, 2, 3):
                    raise ValueError("Euler curves require separate native evaluation")
                curves[binding["path"], attribute] = value
        if cursor != len(values) or not all(math.isfinite(v) for v in values):
            raise ValueError("Unbound or non-finite native curve values")
        return curves


class MotionActor:
    def __init__(self, base, native, output, name):
        self.actor = Actor(base, output, name)
        self.original = {i: obj.read_typetree() for i, obj in self.actor.objects.items()
                         if obj.type.name == "Transform"}
        self.native = native / name
        self.reference = json.loads((self.native / "manifest.json").read_text())
        self.parts, self.material_names = [], []
        self.binding_paths = {}
        for index, path in self.actor.paths.items():
            if path.startswith(self.actor.config["root"] + "/"):
                names = path.split("/")
                self.binding_paths[index] = [zlib.crc32("/".join(names[start:]).encode())
                                             for start in range(1, len(names))]
        for obj in self.actor.objects.values():
            if obj.type.name != "SkinnedMeshRenderer":
                continue
            tree = obj.read_typetree()
            mesh = self.actor.objects[tree["m_Mesh"]["m_PathID"]].read()
            if mesh.m_Name not in self.actor.config["meshes"] and mesh.m_Name not in self.actor.config["weapons"]:
                continue
            if mesh.m_Name in self.actor.config["meshes"]:
                go = self.actor.objects[tree["m_GameObject"]["m_PathID"]].read()
                tr = next(c.component.path_id for c in go.m_Component if c.component.type.name == "Transform")
                if not self.actor.paths[tr].startswith(self.actor.config["root"] + "/"):
                    continue
            handler = MeshHandler(mesh)
            handler.process()
            weights = handler.m_BoneWeights
            if weights is None:
                if any(len(indices) != 1 for indices in handler.m_BoneIndices):
                    raise ValueError("Missing original multiple-influence weights")
                weights = [(1.0,)] * len(handler.m_Vertices)
            weight = np.asarray(weights, dtype=np.float64)
            indices = np.asarray(handler.m_BoneIndices, dtype=np.int32)
            if np.max(np.abs(np.sum(weight, axis=1) - 1)) > 1e-5:
                raise ValueError("Original skin weights do not sum to one")
            materials = [self.actor.material(p) for p in tree["m_Materials"]]
            for material in materials:
                if material not in self.material_names:
                    self.material_names.append(material)
            material_ids = []
            faces = []
            for material, triangles in zip(materials, handler.get_triangles()):
                faces.extend([(c, b, a) for a, b, c in triangles])
                material_ids.extend([self.material_names.index(material)] * len(triangles))
            bind = np.asarray([[[getattr(b, "e" + str(i) + str(j)) for j in range(4)]
                                 for i in range(4)] for b in mesh.m_BindPose], dtype=np.float64)
            vertices = np.asarray(handler.m_Vertices, dtype=np.float64)[:, :3]
            attachment = self.actor.config["weapons"].get(mesh.m_Name)
            target = next((i for i, n in self.actor.names.items() if n == attachment
                           and self.actor.paths[i].startswith(self.actor.config["root"] + "/")), None)
            self.parts.append({"name": mesh.m_Name, "renderer_path_id": obj.path_id,
                "mesh_path_id": mesh.object_reader.path_id, "bind": bind,
                "bones": [p["m_PathID"] for p in tree["m_Bones"]], "weight": weight,
                "indices": indices, "homogeneous": np.column_stack((vertices, np.ones(len(vertices)))),
                "uv": np.asarray(handler.m_UV0, dtype=np.float32)[:, :2],
                "triangles": np.asarray(faces, dtype=np.int32),
                "triangle_materials": np.asarray(material_ids, dtype=np.int16),
                "attachment_bone": attachment, "attachment_target": target})
        if [(p["name"], len(p["homogeneous"])) for p in self.parts] != [
                (p["mesh_name"], p["vertices"]) for p in self.reference["parts"]]:
            raise ValueError("Native base OBJ and sampled part ordering disagree")

    def pose(self, curves):
        self.actor.transforms = copy.deepcopy(self.original)
        self.actor.worlds = {}
        matched = 0
        for index, hashed in self.binding_paths.items():
            tree = self.actor.transforms[index]
            for path in hashed:
                for attribute, field in ((1, "m_LocalPosition"), (2, "m_LocalRotation"), (3, "m_LocalScale")):
                    value = curves.get((path, attribute))
                    if value is not None:
                        tree[field] = dict(zip("xyzw", value))
                        matched += 1
        if matched < 30:
            raise ValueError("Too few native animation transform properties matched")
        result, attachments = [], []
        for part in self.parts:
            world = np.asarray([self.actor.world(bone) for bone in part["bones"]], dtype=np.float64)
            matrices = np.matmul(world, part["bind"])
            extra = np.eye(4) if part["attachment_target"] is None else np.asarray(
                self.actor.world(part["attachment_target"]), dtype=np.float64)
            matrices = np.matmul(extra, matrices)
            skinned = np.zeros((len(part["homogeneous"]), 4), dtype=np.float64)
            for influence in range(part["indices"].shape[1]):
                selected = matrices[part["indices"][:, influence]]
                moved = np.einsum("nij,nj->ni", selected, part["homogeneous"])
                skinned += moved * part["weight"][:, influence, None]
            positions = np.column_stack((-skinned[:, 0], -skinned[:, 2], skinned[:, 1]))
            result.append(positions)
            if part["attachment_target"] is not None:
                attachments.append(extra)
        merged = np.concatenate(result)
        if not np.isfinite(merged).all():
            raise ValueError("Native skinning produced non-finite vertices")
        return merged, np.asarray(attachments), matched

    def reference_error(self):
        clip = self.actor.clip_object.read_typetree()
        positions, _, _ = self.pose(first_frame(clip))
        vertices = []
        for line in Path(self.reference["obj"]).read_text().splitlines():
            if line.startswith("v "):
                vertices.append([float(v) for v in line.split()[1:]])
        error = np.max(np.abs(positions - np.asarray(vertices)))
        if error > 1e-6:
            raise ValueError("Sampled original idle does not reproduce the proven native OBJ")
        return float(error)

    def export(self, clip_obj, output, fps):
        clip = Clip(clip_obj.read_typetree())
        times = np.arange(math.floor(clip.duration * fps + 1e-5) + 1, dtype=np.float64) / fps
        frames, attachments, matched = [], [], []
        for time in times:
            frame, attached, count = self.pose(clip.evaluate(float(time)))
            frames.append(frame.astype(np.float32))
            attachments.append(attached.astype(np.float32))
            matched.append(count)
        positions = np.asarray(frames)
        offsets = np.cumsum([0] + [len(p["homogeneous"]) for p in self.parts])
        triangles = np.concatenate([p["triangles"] + offsets[i] for i, p in enumerate(self.parts)])
        uv = np.concatenate([p["uv"] for p in self.parts])
        path = output / (safe(clip.tree["m_Name"]) + ".npz")
        np.savez_compressed(path, positions=positions, times=times,
            mesh_offsets=offsets.astype(np.int32), mesh_names=np.asarray([p["name"] for p in self.parts]),
            triangles=triangles, triangle_materials=np.concatenate([p["triangle_materials"] for p in self.parts]),
            uv=uv, material_names=np.asarray(self.material_names),
            attachment_world_unity=np.asarray(attachments), fps=np.int32(fps))
        displacements = np.linalg.norm(positions - positions[0], axis=2)
        steps = np.linalg.norm(np.diff(positions, axis=0), axis=2)
        bounds = np.stack((positions.min(axis=1), positions.max(axis=1)), axis=1)
        manifest = {"actor": self.actor.name, "clip": clip.tree["m_Name"],
            "clip_path_id": clip_obj.path_id, "original_duration_seconds": clip.duration,
            "fps": fps, "frames": len(times), "sample_times_seconds": times.tolist(),
            "classification": "native geometry and animation reconstruction; not runtime gameplay footage",
            "coordinate_conversion": "Unity (x,y,z) -> Blender (-x,-z,y); original units; anatomical forward -Y",
            "evaluation": "original streamed cubic polynomial, constants, hierarchy and normalized quaternion TRS; original linear skinning and weapon joint attachments",
            "sources": self.actor.sources, "base_manifest": str(self.native / "manifest.json"),
            "base_manifest_sha256": digest(self.native / "manifest.json"),
            "base_obj": self.reference["obj"], "base_obj_sha256": digest(Path(self.reference["obj"])),
            "idle_reference_max_abs_error": self.reference_error(),
            "t0_curve_max_abs_error": clip.t0_error,
            "stream_polynomial_max_boundary_error": clip.continuity_error,
            "matched_transform_properties_min": min(matched),
            "vertices": positions.shape[1], "triangles": len(triangles),
            "max_vertex_displacement": float(displacements.max()),
            "max_frame_vertex_step": float(steps.max()),
            "frame_bounds_blender": bounds.tolist(), "material_names": self.material_names,
            "parts": [{"name": p["name"], "renderer_path_id": p["renderer_path_id"],
                "mesh_path_id": p["mesh_path_id"], "vertex_start": int(offsets[i]),
                "vertex_count": int(offsets[i + 1] - offsets[i]),
                "triangles": len(p["triangles"]), "attachment_bone": p["attachment_bone"]}
                for i, p in enumerate(self.parts)],
            "output": str(path), "output_sha256": digest(path),
            "numpy_version": np.__version__, "unitypy_version": UnityPy.__version__}
        path.with_suffix(".json").write_text(json.dumps(manifest, indent=2) + "\n")
        return {"actor": self.actor.name, "clip": clip.tree["m_Name"], "output": str(path),
                "frames": len(times), "vertices": positions.shape[1],
                "reference_error": manifest["idle_reference_max_abs_error"],
                "continuity_error": clip.continuity_error}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-base", type=Path, required=True)
    parser.add_argument("--native-scene", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--actor", choices=ACTORS, action="append")
    parser.add_argument("--clip", action="append", help="Exact clip name; selects these instead of the defaults")
    parser.add_argument("--fps", type=int, default=24)
    args = parser.parse_args()
    if args.fps <= 0 or args.fps > 120:
        parser.error("fps must be between 1 and 120")
    output = args.output.resolve()
    if (output == args.game_base.resolve() or args.game_base.resolve() in output.parents
            or any(part in ("ressources", "libs", "decompiled") for part in output.parts)):
        parser.error("Output must be outside read-only game data")
    output.mkdir(parents=True, exist_ok=True)
    results = []
    for name in args.actor or ACTORS:
        actor = MotionActor(args.game_base, args.native_scene, output, name)
        selected = args.clip or DEFAULT_CLIPS[name]
        available = {o.read().m_Name: o for o in actor.actor.objects.values()
                     if o.type.name == "AnimationClip"}
        for requested in selected:
            if requested not in available:
                parser.error(f"Clip {requested!r} does not exist for {name}")
            result = actor.export(available[requested], actor.actor.output, args.fps)
            results.append(result)
            print(json.dumps(result), flush=True)
    (output / "export-results.json").write_text(json.dumps(results, indent=2) + "\n")


if __name__ == "__main__":
    main()
