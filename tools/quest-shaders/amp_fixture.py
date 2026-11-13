#!/usr/bin/env python3
"""Explicit GLES picture inputs for the first actual original Amp forward bank.

Defaults are read from the original serialized ShaderLab property table. Native
camera/light/object inputs below are controlled fixture values, not port defaults.
"""
import argparse
import json
from pathlib import Path
import re


def fixture(recipe, bank):
    parsed = recipe["parsedForm"]
    if parsed["m_Name"] != "Amp_Char_Shader":
        raise ValueError("This probe only covers the actual original Amp character forward shader.")
    properties = {row["m_Name"]: row for row in parsed["m_PropInfo"]["m_Props"]}
    uniforms = {}
    identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    builtin = {
        "_Time": [0.1, 2, 4, 6], "_SinTime": [0, 0, 0, 0], "_WorldSpaceCameraPos": [0, 0, -3],
        "_WorldSpaceLightPos0": [-0.3, 0.4, -1, 0], "_LightColor0": [0.83, 0.74, 0.61, 1],
        "_ProjectionParams": [1, 0.1, 10, 0.1], "unity_WorldTransformParams": [0, 0, 0, 1],
        "unity_OcclusionMaskSelector": [1, 0, 0, 0], "unity_SpecCube0_HDR": [1, 1, 0, 0],
        "unity_SpecCube1_HDR": [1, 1, 0, 0], "unity_ProbeVolumeParams": [0, 0, 0, 0],
    }
    for kind, name, count in re.findall(r"uniform\s+(?:(?:highp|mediump|lowp)\s+)?(float|vec[234])\s+(\w+)(?:\[(\d+)\])?\s*;", bank):
        width = 1 if kind == "float" else int(kind[-1])
        if name.startswith("hlslcc_mtx4x4"):
            value = {"width": 4, "values": identity}
        elif name.endswith("_ST"):
            value = [1, 1, 0, 0]
        elif name in builtin:
            value = builtin[name][:width]
        elif name in properties:
            value = [properties[name][f"m_DefValue_{index}_"] for index in range(width)]
        elif name.startswith("unity_"):
            value = [0] * width
        else:
            raise ValueError("Original probe cannot explain a used fixture uniform: " + name)
        if count and not isinstance(value, dict):
            value = {"width": width, "values": value * int(count)}
        uniforms[name] = value
    # This forward pass originally has native dither/death and height controls;
    # use valid character-height/world-position inputs to isolate the hypotheses.
    for name, value in {"_CharHeight": [2], "_ObjPos": [0, 0, 0], "_DisableVerticalGradient": [1],
                        "_DisableFakeDirLight": [1], "_DeathDissolvePos": [0], "_DeathDissolveTop": [1],
                        "_DeathDissolveBottom": [-1], "_Opacity": [1], "_Toggle_LocalAnimOpacity": [0],
                        "_VertexAnim_NoiseSize": [1], "_NoiseSpeed": [0.3], "_VertexAnim_Intensity": [0.2]}.items():
        if name in uniforms:
            uniforms[name] = value
    textures = {}
    for kind, name in re.findall(r"uniform\s+(?:(?:highp|mediump|lowp)\s+)?(sampler\w+)\s+(\w+)\s*;", bank):
        if name == "_NormalMap":
            pixel = [128, 128, 255, 255] * 16
        elif name == "_Diffuse":
            pixel = [channel for y in range(4) for x in range(4) for channel in ([211, 73, 29, 255] if (x + y) % 2 == 0 else [31, 181, 67, 255])]
        elif name == "_MRAO":
            pixel = [0, 179, 255, 255] * 16
        elif name in ("_VertexAnimMask", "_OpacityTexture"):
            pixel = [255] * 64
        elif name.startswith("unity_") or name in ("_EmissiveMap", "_SheenMask"):
            pixel = [0, 0, 0, 255] * 16
        else:
            raise ValueError("Original probe cannot explain a used native sampler: " + name)
        textures[name] = pixel
    attributes = {
        "in_POSITION0": [[-0.7, -0.7, 0, 1], [0.7, -0.7, 0, 1], [0, 0.7, 0, 1]],
        # A unit normal with a screen-space component makes vertex noise visible
        # through the orthographic fixture instead of moving only along depth.
        "in_NORMAL0": [[0, 0.6, -0.8]] * 3,
        "in_TANGENT0": [[1, 0, 0, 1]] * 3,
        "in_TEXCOORD0": [[0, 0, 0, 0], [1, 0, 0, 0], [0.5, 1, 0, 0]],
    }
    moved = dict(attributes, in_POSITION0=[[-0.5, -0.7, 0, 1], [0.9, -0.7, 0, 1], [0.2, 0.7, 0, 1]])
    uv = dict(attributes, in_TEXCOORD0=[[0.173, 0.297, 0, 0], [0.883, 0.297, 0, 0], [0.528, 1.007, 0, 0]])
    return {"schema": 1, "recipeOriginalName": parsed["m_Name"], "uniforms": uniforms, "textures": textures, "attributes": attributes,
            "cases": [
                {"id": "geometry", "attributes": moved},
                {"id": "lighting", "uniforms": {"_LightColor0": [0.14, 0.37, 0.91, 1]}},
                {"id": "texture", "textures": {"_Diffuse": [83, 17, 219, 255] * 16}},
                {"id": "uv", "attributes": uv},
                {"id": "alpha", "uniforms": {"_Opacity": [0]}},
                {"id": "vertex-animation", "uniforms": {"_AddVertexAnim": [1]}},
            ], "originalPixelParityVerified": False, "headsetPictureVerified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--bank", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(fixture(json.loads(args.recipe.read_text()), args.bank.read_text()), indent=2) + "\n")


if __name__ == "__main__":
    main()
