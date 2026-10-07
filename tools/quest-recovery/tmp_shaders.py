"""Restore source-only official TMP shaders, retaining owned material identities.

This is a verified compatible source restoration, not proof that the original
desktop bytecode and Android pixels are identical. No TMP runtime is installed.
"""
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile

from recover import RecoveryError, sha256

TMP_URL = "https://packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz"
TMP_SHA256 = "1ce172027b906a30be33cefe7b2ee46e1c8d35f729359b8b9785fc120d57b637"
SHADERS = {"TextMeshPro/Distance Field": "TMP_SDF.shader",
           "TextMeshPro/Mobile/Distance Field": "TMP_SDF-Mobile.shader",
           "TextMeshPro/Sprite": "TMP_Sprite.shader"}


def official_sources(archive):
    if sha256(archive) != TMP_SHA256:
        raise RecoveryError("Official TMP archive does not match the pinned SHA256.")
    sources = {}
    with tarfile.open(archive, "r:gz") as package:
        essential = package.extractfile("package/Package Resources/TMP Essential Resources.unitypackage").read()
    with tarfile.open(fileobj=io.BytesIO(essential), mode="r:gz") as package:
        members = {member.name: member for member in package.getmembers() if member.isfile()}
        for name, member in members.items():
            if not name.endswith("/pathname"):
                continue
            path = package.extractfile(member).read().decode().strip()
            if not path.startswith("Assets/TextMesh Pro/Shaders/") or Path(path).suffix not in (".shader", ".cginc"):
                continue
            filename = Path(path).name
            asset = package.extractfile(members[name[:-len("pathname")] + "asset"]).read()
            if filename in sources:
                raise RecoveryError("Duplicate official TMP shader/include name.")
            sources[filename] = asset
    if any(name not in sources for name in SHADERS.values()):
        raise RecoveryError("Official package is missing a required TMP shader.")
    return sources


def validate_recipe(recipe, shader_source):
    parsed = recipe["parsedForm"]
    name = parsed["m_Name"]
    text = shader_source.decode("utf-8-sig")
    if name not in SHADERS or not re.search(r'Shader\s+"' + re.escape(name) + r'"', text):
        raise RecoveryError("TMP shader source identity differs from original recipe.")
    # Match public ShaderLab property declarations rather than dummy HasProperty.
    declarations = dict(re.findall(r'\b(_\w+)\s*\(\s*"[^"\r\n]*"\s*,\s*(Color|Vector|Float|Range\s*\([^)]*\)|2D|Cube)\s*\)', text, re.I))
    differences = []
    for prop in parsed["m_PropInfo"]["m_Props"]:
        prop_name = prop["m_Name"]
        declaration = declarations.get(prop_name)
        if declaration is None:
            raise RecoveryError("Official TMP source lacks original property: " + prop_name)
        expected = {0: "float4", 1: "float4", 2: "float", 3: "float", 4: "texture"}.get(prop["m_Type"])
        actual = declaration.lower()
        actual = "float4" if actual in ("color", "vector") else "texture" if actual in ("2d", "cube") else "float"
        if expected != actual:
            raise RecoveryError("Official TMP property type differs: " + prop_name)
        if declaration.lower().startswith("range"):
            limits = [float(x) for x in re.findall(r'-?(?:\d+(?:\.\d*)?|\.\d+)', declaration)]
            original_limits = [prop["m_DefValue_1_"], prop["m_DefValue_2_"]]
            if limits != original_limits:
                differences.append({"property": prop_name, "kind": "inspector-range",
                                    "original": original_limits, "official": limits})
    additions = sorted(set(declarations) - {p["m_Name"] for p in parsed["m_PropInfo"]["m_Props"]})
    if set(additions) - {"_Sharpness", "_CullMode"}:
        raise RecoveryError("Unexpected additive official TMP shader properties: " + str(additions))
    if "_Sharpness" in additions and not re.search(r'_Sharpness\s*\([^\n]*\)\s*=\s*0\b', text):
        raise RecoveryError("Additive TMP _Sharpness is not neutral by default.")
    if "_CullMode" in additions and not re.search(r'_CullMode\s*\([^\n]*\)\s*=\s*0\b', text):
        raise RecoveryError("Added TMP culling property is not disabled by default.")
    subshaders = parsed["m_SubShaders"]
    if len(subshaders) != 1 or len(subshaders[0]["m_Passes"]) != 1:
        raise RecoveryError("Unsupported original TMP pass structure.")
    state = subshaders[0]["m_Passes"][0]["m_State"]
    def named(field, expected):
        if field.get("m_Name") != expected:
            raise RecoveryError("Original TMP render state differs: " + expected)
    if state["m_Culling"].get("m_Name") != "_CullMode":
        if state["m_Culling"].get("m_Name") != "<noninit>" or state["m_Culling"]["m_Value"] != 0:
            raise RecoveryError("Original TMP culling is neither dynamic nor disabled.")
    named(state["m_ZTest"], "unity_GUIZTestMode")
    named(state["m_StencilRef"], "_Stencil")
    named(state["m_StencilReadMask"], "_StencilReadMask")
    named(state["m_StencilWriteMask"], "_StencilWriteMask")
    named(state["m_StencilOp"]["m_Comp"], "_StencilComp")
    named(state["m_StencilOp"]["m_Pass"], "_StencilOp")
    named(state["m_RtBlend0"]["m_ColMask"], "_ColorMask")
    source_blend = 5 if name == "TextMeshPro/Sprite" else 1
    blend = state["m_RtBlend0"]
    if (state["m_ZWrite"]["m_Value"] != 0 or state["m_Lighting"] or state["m_RtSeparateBlend"]
            or blend["m_SourceBlend"]["m_Value"] != source_blend
            or blend["m_SourceBlendAlpha"]["m_Value"] != source_blend
            or blend["m_DestinationBlend"]["m_Value"] != 10
            or blend["m_DestinationBlendAlpha"]["m_Value"] != 10
            or blend["m_BlendOp"]["m_Value"] != 0 or blend["m_BlendOpAlpha"]["m_Value"] != 0
            or state["m_StencilOp"]["m_Fail"]["m_Value"] != 0
            or state["m_StencilOp"]["m_ZFail"]["m_Value"] != 0):
        raise RecoveryError("Original TMP blend/depth/lighting/stencil state is incompatible.")
    required = [r'Cull\s+\[_CullMode\]', r'ZWrite\s+Off', r'Lighting\s+Off',
                r'ZTest\s+\[unity_GUIZTestMode\]', r'ColorMask\s+\[_ColorMask\]',
                r'Ref\s+\[_Stencil\]', r'Comp\s+\[_StencilComp\]', r'Pass\s+\[_StencilOp\]',
                r'ReadMask\s+\[_StencilReadMask\]', r'WriteMask\s+\[_StencilWriteMask\]',
                r'Blend\s+' + ("SrcAlpha" if source_blend == 5 else "One") + r'\s+OneMinusSrcAlpha']
    if any(not re.search(expression, text, re.I) for expression in required):
        raise RecoveryError("Official TMP source does not preserve verified render states.")
    return {"originalPropertyCount": len(parsed["m_PropInfo"]["m_Props"]),
            "additiveNeutralProperties": additions, "propertyDifferences": differences,
            "verifiedRenderState": "single-pass-blend-depth-cull-stencil-color-mask",
            "pixelParityVerified": False}


def restore(project, source_project, asset_paths, archive):
    sources = official_sources(archive)
    recipes = {}
    for path in (source_project / "QuestRecovery/ShaderRecipes").glob("*.json"):
        recipe = json.loads(path.read_text())
        if recipe.get("parsedForm", {}).get("m_Name") in SHADERS:
            recipes.setdefault(recipe["parsedForm"]["m_Name"], []).append((path, recipe))
    records = []
    for relative in sorted(asset_paths):
        if Path(relative).suffix != ".shader":
            continue
        target = project / relative
        match = re.search(r'Shader\s+"([^"]+)"', target.read_text(encoding="utf-8-sig"))
        if not match or match[1] not in recipes:
            continue
        name = match[1]
        source = sources[SHADERS[name]]
        validations = [validate_recipe(recipe, source) for _, recipe in recipes[name]]
        if any(result != validations[0] for result in validations):
            raise RecoveryError("Original TMP shader variants disagree: " + name)
        verified = validations[0]
        before = sha256(target)
        metadata_hash = sha256(target.with_name(target.name + ".meta"))
        target.write_bytes(source)
        for filename, include in sources.items():
            if filename.endswith(".cginc"):
                (target.parent / filename).write_bytes(include)
        records.append({"assetPath": relative, "shaderName": name, "originalDummySha256": before,
                        "originalMetaSha256": metadata_hash, "restoredSha256": sha256(target),
                        "recipeSha256s": sorted(sha256(path) for path, _ in recipes[name]), "officialSourceFile": SHADERS[name],
                        "officialArchiveSha256": TMP_SHA256, "officialSourceUrl": TMP_URL, **verified})
    return records
