"""Audit real cooked Android compute bytes; never infer compilation from source.

The import of UnityPy is confined to the CLI/bundle adapter; validate_objects is
standard-library-only and receives already decoded class72 type trees.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

if __package__:
    from .native import ComputeRecoveryError
else:
    from native import ComputeRecoveryError


def validate_objects(manifest: dict, objects: list[dict]) -> dict:
    if manifest.get("schema") != 1 or manifest.get("shaderCount") != 13 or manifest.get("kernelCount") != 36 \
            or len(manifest.get("shaders", [])) != 13 or sum(len(row["kernels"]) for row in manifest["shaders"]) != 36:
        raise ComputeRecoveryError("Cooked gate requires the complete 13-shader / 36-kernel original contract.")
    contracts = {shader["name"]: shader for shader in manifest["shaders"]}
    observed = {shader.get("m_Name"): shader for shader in objects}
    if len(observed) != len(objects) or observed.keys() != contracts.keys():
        raise ComputeRecoveryError("Cooked compute object census/identity differs from source contract.")
    rows = []
    for name, contract in contracts.items():
        variants = observed[name]["variants"]
        if len(variants) != 1 or variants[0]["targetRenderer"] != 11 or variants[0]["targetLevel"] != 3:
            raise ComputeRecoveryError("Cooked compute is not the exact GLES3.1 native bank: " + name)
        kernels = variants[0]["kernels"]
        if [kernel["name"] for kernel in kernels] != [kernel["name"] for kernel in contract["kernels"]]:
            raise ComputeRecoveryError("Cooked compute kernel order/identity changed: " + name)
        kernel_rows = []
        for actual, expected in zip(kernels, contract["kernels"]):
            programs = actual["variantMap"]
            if len(programs) != 1 or programs[0][0] != "":
                raise ComputeRecoveryError("Cooked original compute variant was stripped or changed.")
            program = programs[0][1]
            if program["threadGroupSize"] != expected["threadGroups"]:
                raise ComputeRecoveryError("Cooked native compute dispatch dimensions differ.")
            code = bytes(program["code"])
            text = code.rstrip(b"\0").decode("utf-8")
            if not text.startswith("#version 310 es\n") or not re.search(r"\bvoid main\s*\(", text):
                raise ComputeRecoveryError("Cooked compute has no actual GLES3.1 executable kernel.")
            local = re.search(r"layout\s*\(\s*local_size_x\s*=\s*(\d+)\s*,\s*local_size_y\s*=\s*(\d+)\s*,\s*local_size_z\s*=\s*(\d+)\s*\)\s*in\s*;", text)
            if local is None or list(map(int, local.groups())) != expected["threadGroups"]:
                raise ComputeRecoveryError("Actual GLSL local sizes do not match original dispatch metadata.")
            outputs = {binding["name"]: binding for binding in program["outBuffers"]}
            if outputs.keys() != {binding["name"] for binding in expected["outputBindings"]}:
                raise ComputeRecoveryError("Actual GLES compute output property closure differs.")
            images = []
            for binding in expected["outputBindings"]:
                storage = binding.get("imageStorage")
                actual_output = outputs[binding["name"]]
                if actual_output["texDimension"] != binding["dimension"]:
                    raise ComputeRecoveryError("Actual GLES output texture dimension differs from native binding.")
                if storage is None:
                    # Original ComputeBuffers remain std430 structured storage;
                    # a typed samplerBuffer substitution loses native stride.
                    if actual_output["texDimension"] != -1 or not re.search(r"\bbuffer\s+" + re.escape(binding["name"]) + r"\b", text):
                        raise ComputeRecoveryError("Native structured output was not cooked as a GLES storage buffer.")
                    structure = re.search(r"\bbuffer\s+" + re.escape(binding["name"])
                        + r"\s*\{\s*(\w+)\s+\w+\[\]\s*;\s*\}", text)
                    fields = None if structure is None else re.search(r"\bstruct\s+" + re.escape(structure[1])
                        + r"\s*\{\s*uint\[(\d+)\]\s+\w+\s*;\s*\}", text)
                    if fields is None or int(fields[1]) * 4 != binding["strideBytes"]:
                        raise ComputeRecoveryError("Actual GLES structured output stride differs from native ComputeBuffer.")
                    continue
                dimension = {2: "image2D", 3: "image3D", 5: "image2DArray"}.get(binding["dimension"])
                if dimension is None: raise ComputeRecoveryError("Unknown original typed image dimension.")
                match = re.search(r"(?m)^.*layout\([^\n]*\b" + re.escape(storage["glslImageQualifier"])
                    + r"\b[^\n]*\)[^\n]*\b" + dimension + r"\s+" + re.escape(binding["name"]) + r"\s*;", text)
                if match is None:
                    raise ComputeRecoveryError("Actual GLES image qualifier differs from native RenderTexture: " + name + "/" + binding["name"])
                images.append({"name": binding["name"], "renderTextureFormat": storage["renderTextureFormat"],
                    "glslImageQualifier": storage["glslImageQualifier"], "actualDeclaration": match[0].strip()})
            names = {binding["name"] for binding in program["textures"] + program["inBuffers"]}
            expected_inputs = {binding["name"] for binding in expected["interface"]["bindings"] if binding["kind"] in ("texture", "buffer")}
            if not expected_inputs <= names:
                raise ComputeRecoveryError("Actual GLES input property closure differs.")
            kernel_rows.append({"name": expected["name"], "threadGroups": expected["threadGroups"],
                "actualGlslSha256": hashlib.sha256(code).hexdigest(), "actualCodeBytes": len(code), "images": images})
        rows.append({"name": name, "assetPath": contract["assetPath"], "guid": contract["guid"],
            "originalLocalFileId": contract["localFileId"], "nativeRenderer": 11, "nativeTargetLevel": 3,
            "kernels": kernel_rows})
    return {"schema": 1, "shaderCount": len(rows), "kernelCount": sum(len(row["kernels"]) for row in rows),
        "androidCompiled": True, "actualGles31BytesVerified": True, "originalKernelIdentitiesRetained": True,
        "originalThreadGroupsRetained": True, "nativeImageAllocationsMatched": True,
        "originalPixelParityVerified": False, "hardwareVerified": False, "shaders": rows}


def validate_bundle(manifest_path: Path, bundle_path: Path) -> dict:
    import UnityPy
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    objects = [obj.read_typetree() for obj in UnityPy.load(str(bundle_path)).objects if obj.type.name == "ComputeShader"]
    receipt = validate_objects(manifest, objects)
    receipt.update({"recoveryManifestSha256": hashlib.sha256(Path(manifest_path).read_bytes()).hexdigest(),
                    "actualBundleSha256": hashlib.sha256(Path(bundle_path).read_bytes()).hexdigest()})
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    receipt = validate_bundle(args.manifest, args.bundle)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({key: receipt[key] for key in ("shaderCount", "kernelCount", "androidCompiled", "nativeImageAllocationsMatched", "hardwareVerified")}))
