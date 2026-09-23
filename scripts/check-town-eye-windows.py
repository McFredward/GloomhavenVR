#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.10"
# dependencies = ["UnityPy==1.25.3"]
# ///
"""Inspect actual D3D11 eye fragment bindings, not Linux shader source variants.

Run: uv run scripts/check-town-eye-windows.py prebuilt/ghvr-town.bundle
Build 543 is a real negative control: fwdbase removed VERTEXLIGHT_ON code from
its D3D fragment programs while the OpenGL rendering tests passed.
"""
import argparse
import hashlib
from pathlib import Path

import UnityPy


def vectors(parameters):
    for vector in parameters.get("m_VectorParams", []):
        yield vector["m_NameIndex"]
    for buffer in parameters.get("m_ConstantBuffers", []):
        yield from vectors(buffer)


def check(path):
    expected = {"GloomhavenVR/TownEye", "GloomhavenVR/TownCornea"}
    required = {"unity_4LightPosX0", "unity_4LightPosY0", "unity_4LightPosZ0",
                "unity_4LightAtten0", "unity_LightColor"}
    found = set()
    for obj in UnityPy.load(str(path)).objects:
        if obj.type.name != "Shader":
            continue
        tree = obj.read_typetree()
        form = tree["m_ParsedForm"]
        name = form["m_Name"]
        if name not in expected:
            continue
        assert 4 in tree["platforms"], f"{name}: no D3D11 platform"
        found.add(name)
        variants = 0
        for subshader in form["m_SubShaders"]:
            for shader_pass in subshader["m_Passes"]:
                names = {index: name for name, index in shader_pass["m_NameIndices"]}
                fragment = shader_pass["progFragment"]
                common = set(vectors(fragment.get("m_CommonParameters", {})))
                for program in fragment["m_SubPrograms"]:
                    # ShaderGpuProgramType.DX11PixelSM40/SM50.
                    if program["m_GpuProgramType"] not in (17, 18):
                        continue
                    bound = {names[index] for index in common | set(vectors(program["m_Parameters"]))}
                    missing = required - bound
                    assert not missing, f"{name}: D3D fragment lacks practical lights: {sorted(missing)}"
                    variants += 1
        assert variants, f"{name}: no D3D fragment variants inspected"
        print(f"PASS {name}: {variants} D3D fragment variants bind all four-light inputs")
    assert found == expected, f"Missing eye shaders: {sorted(expected - found)}"
    print(f"PASS Windows eye lighting: {path.name} SHA256={hashlib.sha256(path.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    check(parser.parse_args().bundle.resolve())
