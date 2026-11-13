"""Fail-closed identity/coverage checks for native Campaign shader validation.

These checks consume identities captured from the original Unity objects.  They
never choose a replacement shader by display name and never equate a successful
host compilation with headset fidelity.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re


class ValidationError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    with path.open("rb") as source:
        result = hashlib.file_digest(source, "sha256")
    return result.hexdigest()


def _hash(value, label):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value):
        raise ValidationError(f"Invalid {label} SHA-256.")
    return value


def _guid(value, label):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{32}", value):
        raise ValidationError(f"Invalid {label} original/recovered GUID.")
    return value


def asset_path(value):
    if not isinstance(value, str) or "\\" in value:
        raise ValidationError("Invalid shader validation asset path.")
    parts = value.split("/")
    if parts[0] != "Assets" or any(part in ("", ".", "..") for part in parts):
        raise ValidationError("Shader validation path escapes the asset tree.")
    return value


def _rows(value, label):
    if not isinstance(value, list) or not value:
        raise ValidationError(f"Missing {label} coverage.")
    if any(not isinstance(row, dict) for row in value):
        raise ValidationError(f"Invalid {label} coverage row.")
    return value


def load(path: Path, project: Path | None = None):
    raw = path.read_bytes()
    if len(raw) > 64 * 1024 * 1024:
        raise ValidationError("Shader identity manifest is oversized.")
    manifest = json.loads(raw)
    if manifest.get("schema") != 1 or manifest.get("scope") not in ("campaign", "campaign-compiler", "campaign-pixels"):
        raise ValidationError("Expected original Campaign shader identity manifest schema 1.")
    shaders = _rows(manifest.get("shaders"), "shader")
    materials = [] if manifest.get("scope") == "campaign-compiler" and manifest.get("materials") == [] else _rows(manifest.get("materials"), "material")
    compiler_only = manifest.get("scope") == "campaign-compiler"
    cases = [] if compiler_only else _rows(manifest.get("renderCases"), "native render")
    if compiler_only and manifest.get("requiredShaderCount") != len(shaders):
        raise ValidationError("Original Campaign shader coverage is incomplete.")
    if manifest.get("requiredMaterialCount") != len(materials):
        raise ValidationError("Original Campaign material coverage is incomplete.")
    shader_index = {}
    variant_ids = set()
    for shader in shaders:
        guid = _guid(shader.get("guid"), "shader")
        if guid in shader_index:
            raise ValidationError("Duplicate original shader identity.")
        shader_index[guid] = shader
        path_value = asset_path(shader.get("assetPath"))
        _hash(shader.get("sourceSha256"), "translated shader source")
        if not shader.get("originalSerializedFile") or not isinstance(shader.get("originalPathId"), int):
            raise ValidationError("Missing original shader CAB/path-ID provenance.")
        if not shader.get("originalName") or shader["originalName"] == "Hidden/InternalErrorShader":
            raise ValidationError("An error shader cannot provide Campaign material coverage.")
        for variant in _rows(shader.get("variants"), "original shader variant"):
            _hash(variant.get("vertexOriginalDxbcSha256"), "original vertex DXBC")
            _hash(variant.get("fragmentOriginalDxbcSha256"), "original fragment DXBC")
            for field in ("subshader", "pass"):
                if type(variant.get(field)) is not int or variant[field] < 0:
                    raise ValidationError("Invalid original shader pass identity.")
            keys = variant.get("keywords")
            if not isinstance(keys, list) or any(not isinstance(k, str) or not re.fullmatch(r"[A-Za-z0-9_]+", k) for k in keys) or len(set(keys)) != len(keys):
                raise ValidationError("Invalid original shader keyword bank.")
            stereo = variant.get("stereo")
            if variant.get("fragmentOutput", "color") not in ("color", "depth", "none"):
                raise ValidationError("Original fragment output contract is unknown.")
            if stereo not in ("mono", "instancing", "multiview"):
                raise ValidationError("Missing explicit native shader eye mode.")
            if stereo == "multiview" and "STEREO_MULTIVIEW_ON" not in keys:
                raise ValidationError("Multiview evidence requires its actual keyword bank.")
            variant_id = (guid, variant["subshader"], variant["pass"], variant.get("hardwareTier", 0), tuple(sorted(keys)))
            if variant_id in variant_ids:
                raise ValidationError("Duplicate declared original shader variant.")
            variant_ids.add(variant_id)
        if project is not None:
            candidate = project / path_value
            from retained import source_matches
            if not candidate.is_file() or not source_matches(shader, sha256(candidate), project):
                raise ValidationError("Actual translated shader bytes differ from provenance: " + guid)
            meta = candidate.with_name(candidate.name + ".meta")
            if not meta.is_file() or re.search(r"(?m)^guid: " + guid + r"$", meta.read_text()) is None:
                raise ValidationError("Actual translated shader GUID differs: " + guid)
    material_index = {}
    for material in materials:
        guid = _guid(material.get("guid"), "material")
        builtin = material.get("originalEngineBuiltinShader") is True and material.get("shaderGuid") in ("0000000000000000e0000000000000000", "0000000000000000f0000000000000000") and type(material.get("shaderFileId")) is int
        native_null = material.get("originalShaderNull") is True and material.get("shaderGuid") is None
        if guid in material_index or material.get("shaderGuid") not in shader_index and not (compiler_only and (builtin or native_null)):
            raise ValidationError("Duplicate material or unresolved exact original shader reference.")
        material_index[guid] = material
        asset_path(material.get("assetPath"))
    if compiler_only:
        return manifest
    covered = set()
    case_ids = set()
    features = set()
    for case in cases:
        if not isinstance(case.get("id"), str) or not re.fullmatch(r"[A-Za-z0-9_.-]+", case["id"]) or case["id"] in case_ids:
            raise ValidationError("Native render case identity is absent or duplicated.")
        case_ids.add(case["id"])
        material = material_index.get(case.get("materialGuid"))
        if material is None or case.get("shaderGuid") != material["shaderGuid"]:
            raise ValidationError("Native render case changes its original material/shader association.")
        _guid(case.get("meshGuid"), "render mesh")
        for key in ("originalMaterial", "originalMesh"):
            original = case.get(key)
            if not isinstance(original, dict) or not isinstance(original.get("assetName"), str) or not original["assetName"]:
                raise ValidationError("Native reference case lacks its original asset-bundle address.")
            if not original.get("bundlePaths") or not isinstance(original["bundlePaths"], list):
                raise ValidationError("Native reference case lacks its original bundle dependency closure.")
            for bundle in original["bundlePaths"]:
                _hash(bundle.get("sha256"), "original reference bundle")
                if not bundle.get("path"):
                    raise ValidationError("Original reference bundle path is missing.")
        case_features = case.get("features")
        if not isinstance(case_features, list) or not case_features or not set(case_features) <= {"geometry", "lighting", "texture", "alpha", "uv", "animation", "stereo"}:
            raise ValidationError("Native rendering assertions must declare bounded observed features.")
        features.update(case_features)
        covered.add(case["shaderGuid"])
    if set(shader_index) != covered:
        raise ValidationError("At least one original Campaign shader lacks native reference rendering.")
    required_features = set(manifest.get("requiredFeatures", []))
    if required_features != {"geometry", "lighting", "texture", "alpha", "uv", "animation", "stereo"} or not required_features <= features:
        raise ValidationError("Campaign native rendering omits a required picture hypothesis.")
    return manifest


def validate_receipt(manifest, manifest_sha, receipt):
    """A compiler receipt is only accepted for the exact original identity set."""
    if receipt.get("schema") != 1 or receipt.get("sourceManifestSha256") != manifest_sha:
        raise ValidationError("Native shader receipt refers to different source identities.")
    if receipt.get("unityVersion") != "2021.3.5f1" or receipt.get("compilerPlatform") != manifest.get("compilerPlatform", "GLES3x"):
        raise ValidationError("Native shader receipt uses a different compiler contract.")
    expected = {(s["guid"], v["subshader"], v["pass"], v.get("hardwareTier", 0), tuple(sorted(v["keywords"])))
                for s in manifest["shaders"] for v in s["variants"]}
    expected_variants = {(s["guid"], v["subshader"], v["pass"], v.get("hardwareTier", 0), tuple(sorted(v["keywords"]))): v
                         for s in manifest["shaders"] for v in s["variants"]}
    actual = set()
    for row in receipt.get("programs", []):
        key = (row.get("guid"), row.get("subshader"), row.get("pass"), row.get("hardwareTier", 0), tuple(sorted(row.get("keywords", []))))
        if key in actual or not row.get("vertexCompiled") or not row.get("fragmentCompiled"):
            raise ValidationError("Native GLES bank failed or is duplicated.")
        actual.add(key)
        if manifest.get("graphicsApi") == "Vulkan":
            _hash(row.get("bankSha256"), "actual native Vulkan bank")
            _hash(row.get("vertexSha256"), "actual native Vulkan vertex")
            _hash(row.get("fragmentSha256"), "actual native Vulkan fragment")
        else:
            _hash(row.get("glesSha256"), "actual native GLES bank")
        original = expected_variants.get(key)
        if original is None or row.get("stereo") != original["stereo"]:
            raise ValidationError("Native GLES bank changes its declared stereo contract.")
        if original["stereo"] == "multiview":
            if not row.get("vertexEyeRoutingObserved") and not original.get("viewInvariant"):
                raise ValidationError("Actual multiview vertex bank lacks observed eye routing.")
            if original.get("requiresFragmentEyeRouting") and not row.get("fragmentEyeRoutingObserved"):
                raise ValidationError("Actual multiview fragment bank loses its required eye.")
    if actual != expected or receipt.get("materialCount") != len(manifest["materials"]):
        raise ValidationError("Native shader receipt omits original material/variant coverage.")
    if receipt.get("originalPixelParityVerified") is not False or receipt.get("headsetPictureVerified") is not False:
        raise ValidationError("Compilation alone cannot assert original pixels or a headset picture.")
    return receipt
