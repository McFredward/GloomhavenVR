"""Build a tiny immutable-source compute overlay for the complete Quest game."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

if __package__:
    from .adapter import restore
    from .native import ComputeRecoveryError, parse
    from .formats import POST_PROCESSING_SHA256
else:
    from adapter import restore
    from native import ComputeRecoveryError, parse
    from formats import POST_PROCESSING_SHA256

NAMES = {"EyeHistogram", "Lut3DBaker", "MultiScaleVODownsample2", "Vectorscope", "Histogram",
    "AutoExposure", "MultiScaleVOUpsample", "Waveform", "ExposureHistogram", "MultiScaleVODownsample1",
    "Texture3DLerp", "GaussianDownsample", "MultiScaleVORender"}
MANIFEST = "QuestCampaignEvidence/compute-recovery.json"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_graphics(path: Path):
    spec = importlib.util.spec_from_file_location("quest_compute_graphics", path)
    if spec is None or spec.loader is None:
        raise ComputeRecoveryError("Original instruction converter module is unavailable.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def inventory(project: Path) -> list[tuple[Path, dict, str]]:
    result = []
    for path in sorted((project / "Assets").rglob("*.asset")):
        if path.is_symlink():
            raise ComputeRecoveryError("Recovered compute source must not be symlinked.")
        with path.open("rb") as stream:
            prefix = stream.read(512)
        if not re.search(rb"^--- !u!72 ", prefix, re.M):
            continue
        value = parse(path.read_text(encoding="utf-8"))
        meta = path.with_suffix(path.suffix + ".meta")
        if not meta.is_file() or meta.is_symlink():
            raise ComputeRecoveryError("Original compute metadata is missing.")
        text = meta.read_text(encoding="utf-8")
        guid = re.findall(r"^guid: ([0-9a-f]{32})$", text, re.M)
        if len(guid) != 1 or "NativeFormatImporter:" not in text or "mainObjectFileID: 7200000" not in text:
            raise ComputeRecoveryError("Original compute GUID/importer/localID is unproven.")
        result.append((path, value, guid[0]))
    if len(result) != 13 or {value["name"] for _, value, _ in result} != NAMES:
        raise ComputeRecoveryError("Complete Campaign requires the exact 13 audited original compute objects.")
    if sum(len(value["kernels"]) for _, value, _ in result) != 36:
        raise ComputeRecoveryError("Original complete compute kernel census changed; re-audit it.")
    return result


def stage(source_project: Path, overlay_directory: Path, *, graphics_module: Path,
          vkd3d="vkd3d-compiler", spirv_cross="spirv-cross") -> dict:
    """Return original->.compute pathMap, removals and a checksummed overlay manifest.

    The caller applies only the declared files/removals in its disposable generated
    project, then rewrites catalog/binding paths. Original assets stay read-only.
    """
    source, overlay = Path(source_project).resolve(), Path(overlay_directory).resolve()
    if source == overlay or source in overlay.parents or overlay in source.parents:
        raise ComputeRecoveryError("Compute overlay and immutable recovery project must be separate.")
    if overlay.exists() and any(overlay.iterdir()):
        raise ComputeRecoveryError("Compute overlay destination must be empty.")
    originals = inventory(source)
    if digest(source / "Assets/Plugins/Unity.Postprocessing.Runtime.dll") != POST_PROCESSING_SHA256:
        raise ComputeRecoveryError("Native post-processing image allocations changed; re-audit typed UAV storage.")
    inputs = {path.relative_to(source).as_posix(): digest(path) for path, _, _ in originals}
    inputs.update({path.relative_to(source).as_posix() + ".meta": digest(path.with_suffix(path.suffix + ".meta"))
                   for path, _, _ in originals})
    graphics = load_graphics(Path(graphics_module).resolve())
    cache = overlay / "QuestCampaignEvidence/ComputeInstructionProof"
    output_files, path_map, rows = {}, {}, []
    for path, shader, guid in originals:
        relative = path.relative_to(source).as_posix()
        target = Path(relative).with_suffix(".compute").as_posix()
        pieces, kernels = [], []
        for index, kernel in enumerate(shader["kernels"]):
            kernel["shaderName"] = shader["name"]
            translated = graphics.translate(kernel["code"], cache, vkd3d=vkd3d, spirv_cross=spirv_cross)
            hlsl, proof = restore(Path(translated["hlslPath"]).read_text(encoding="utf-8"), kernel, graphics)
            pieces.append("#if defined(QUEST_ORIGINAL_KERNEL_" + str(index) + ")\n" + hlsl + "\n#endif\n")
            kernels.append({"name": kernel["name"], "threadGroups": kernel["threadGroups"],
                "requirements": kernel["requirements"], "interface": kernel["interface"], **proof,
                "originalDxbcSha256": translated["originalDxbcSha256"],
                "spirvSha256": translated["spirvSha256"],
                "translatedHlslSha256": translated["translatedHlslSha256"],
                "restoredHlslSha256": hashlib.sha256(hlsl.encode()).hexdigest()})
        pragmas = "\n".join("#pragma kernel " + kernel["name"] + " QUEST_ORIGINAL_KERNEL_" + str(index)
                            for index, kernel in enumerate(shader["kernels"]))
        text = "// Generated from the owner's exact original DXBC instruction bank.\n" + pragmas + "\n\n" + "\n".join(pieces)
        destination = overlay / target
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(text, encoding="utf-8")
        meta = overlay / (target + ".meta")
        meta.write_text("fileFormatVersion: 2\nguid: " + guid + "\nComputeShaderImporter:\n"
                        "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
        for file in (destination, meta):
            output_files[file.relative_to(overlay).as_posix()] = {"sha256": digest(file), "bytes": file.stat().st_size}
        path_map[relative] = target
        rows.append({"name": shader["name"], "originalPath": relative, "assetPath": target, "guid": guid,
            "classId": 72, "localFileId": 7200000, "sourceSha256": digest(destination), "metaSha256": digest(meta),
            "kernelCount": len(kernels), "kernels": kernels})
    for path, expected in inputs.items():
        if digest(source / path) != expected:
            raise ComputeRecoveryError("Original compute input changed during conversion: " + path)
    manifest = {"schema": 1, "scope": "complete-original-campaign-compute", "shaderCount": 13, "kernelCount": 36,
        "recovery": "exact-original-DXBC-to-SPIRV-to-HLSL-with-native-interface-restoration",
        "originalPostProcessingRuntimeSha256": digest(source / "Assets/Plugins/Unity.Postprocessing.Runtime.dll"),
        "graphicsConverterModuleSha256": digest(Path(graphics_module)), "originalInputs": inputs,
        "files": output_files, "pathMap": path_map,
        "removePaths": [path for original in path_map for path in (original, original + ".meta")],
        "shaders": rows, "originalInputsUnchanged": True, "androidCompiled": False,
        "originalPixelParityVerified": False, "hardwareVerified": False}
    receipt = overlay / MANIFEST
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-project", required=True, type=Path)
    parser.add_argument("--overlay", required=True, type=Path)
    parser.add_argument("--graphics-module", required=True, type=Path)
    parser.add_argument("--vkd3d", default="vkd3d-compiler")
    parser.add_argument("--spirv-cross", default="spirv-cross")
    args = parser.parse_args()
    manifest = stage(args.source_project, args.overlay, graphics_module=args.graphics_module,
                     vkd3d=args.vkd3d, spirv_cross=args.spirv_cross)
    print(json.dumps({key: manifest[key] for key in ("shaderCount", "kernelCount", "androidCompiled", "hardwareVerified")}, indent=2))


if __name__ == "__main__":
    main()
