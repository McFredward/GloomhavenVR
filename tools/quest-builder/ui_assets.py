"""Restore original EULA and promotion UI shaders in generated Quest projects.

AssetRipper exported the animated EULA backing as constant white and dropped
the promotion's native dissolve, transparency and stencil operations. Original
DXBC vertex/fragment banks and uniform tables establish the portable programs
below. Material textures, properties, keywords, GUIDs and pass states are
retained; no artwork or replacement background is introduced.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import uuid

from storage import BuildError, write_json

NAME = "Splash Screen Shader"
ASSET = "Assets/Shader/Splash Screen Shader.shader"
GUID = "36fec7f4d3bfafd409fd42ffef9eec70"
DUMMY_SHA256 = "852f87eae8f6a70e1da343325013cd8ebf3362e5bcbdd46227f77c75fdb5ee89"
SOURCE_SHA256 = "d91f4c480676b22c09da4262f99d2fb38677257117be1130c85fbab2f34f95b4"
RECIPE_SHA256 = "fd65d0a293f36a8d33578386f182d73b0aeba5ecd86a556826371db6ee1fff89"
RECIPE_FORM_SHA256 = "af7616b518effd29a17231545f7acbfe49d3006ce9732c07818aa5235ed950b3"
RECIPES = "Assets/QuestOriginalStartup/shader-recipes.json"
RECEIPT = "QuestStartupEvidence/original-ui-assets.json"
RECIPE = "original-ui-dxbc-port-v1"
DXBC_SHA256 = {
    "vertexBothKeywords": "662d78c82ec3a85be9b9dfc398ae4802b196096bd2b968b323712d72b957bbd2",
    "fragmentMultiply": "6d7cbc6a11923d9b36d1feaebe7f4a737e698d4f39daf56301e7e91b8c631122",
    "fragmentAdd": "013359136f801af9de989ba6a24ac76174737de26662fd641f918731de502ecf",
}

# Direct transcription of the original compiled register operations. In
# particular these sine inputs are material values: the original scroll uses
# _Time.y outside sin(), and original output alpha is always one. Canvas tint,
# sprite artwork and a generic UI shader would each change authored behavior.
PROGRAM = r'''    // Quest: original-splash-dxbc-port-v1 (original compiled program transcription).
    SubShader
    {
        Tags { "RenderType" = "Overlay" }
        LOD 100
        Pass
        {
            Name "Unlit"
            Cull Back
            ZTest Always
            ZWrite On
            Blend One Zero
            ColorMask RGBA
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ _ADDORMULT_ON
            #include "UnityCG.cginc"

            sampler2D _DiffuseTexture, _Map1, _Map2;
            float4 _DiffuseTexture_ST;
            float2 _Map_01_Speed, _Map02_Speed;
            float _Map_01_Sin_Value, _Map_02_Sin_Value;
            float _Map01_Tiling, _Map02_Tiling;
            float4 _Map01_Tint, _Map02Tint;
            float _TextureScrollBlend, _FinalBlend_Sine;

            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                float2 map2UV = input.uv * _Map02_Tiling +
                    _Time.y * (sin(_Map_02_Sin_Value) * _Map02_Speed);
                float2 map1UV = input.uv * _Map01_Tiling +
                    _Time.y * (sin(_Map_01_Sin_Value) * _Map_01_Speed);
                float3 map2 = tex2D(_Map2, map2UV).rgb;
                float3 map1Tinted = tex2D(_Map1, map1UV).rgb * _Map01_Tint.rgb;
                float3 maps = _TextureScrollBlend *
                    (map2 * _Map02Tint.rgb - map1Tinted) + map1Tinted;
                float3 diffuse = tex2D(_DiffuseTexture,
                    input.uv * _DiffuseTexture_ST.xy + _DiffuseTexture_ST.zw).rgb;
                float weight = sin(_FinalBlend_Sine) * 0.5;
                #if defined(_ADDORMULT_ON)
                    return float4(weight * maps + diffuse, 1.0);
                #else
                    return float4(weight * (diffuse * maps - diffuse) + diffuse, 1.0);
                #endif
            }
            ENDHLSL
        }
    }
}
'''

DISSOLVE_PROGRAM = r'''    // Quest: original-dissolve-dxbc-port-v1 (original compiled program transcription).
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent"
               "IgnoreProjector" = "true" "PreviewType" = "Plane" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask [_ColorMask]
            Stencil
            {
                Ref [_Stencil]
                Comp [_StencilComp]
                Pass [_StencilOp]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
            }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Mask;
            float4 _MainTex_ST, _Mask_ST;
            float _Cutoff, _EdgeWeight;
            struct Input
            {
                float4 position : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
            };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv.xy = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.uv.zw = input.uv * _Mask_ST.xy + _Mask_ST.zw;
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                float maskValue = tex2D(_Mask, input.uv.zw).r;
                // The original discard compares strictly less than the cutoff.
                if (maskValue < _Cutoff) discard;
                float4 color = tex2D(_MainTex, input.uv.xy) * input.color;
                color.a = (((maskValue - _Cutoff) * color.a) / maskValue) * _EdgeWeight;
                return color;
            }
            ENDHLSL
        }
    }
}
'''

SHADERS = {
    NAME: {"asset": ASSET, "guid": GUID, "dummySha256": DUMMY_SHA256,
           "sourceSha256": SOURCE_SHA256, "recipeSha256": RECIPE_SHA256,
           "formSha256": RECIPE_FORM_SHA256, "program": PROGRAM,
           "keywords": ["", "_ADDORMULT_ON"], "dxbcSha256": DXBC_SHA256,
           "colorProperties": ["_Map02Tint", "_Map01_Tint"]},
    "UI/Dissolve mask": {
        "asset": "Assets/Shader/UI_Dissolve mask.shader", "guid": "ee924f72fbf9fcd4a9ea6e1dceb11982",
        "dummySha256": "45a57f07b6464d97e012cc59c6f49ff899bcecc1c7c745acef0904d22ea32509",
        "sourceSha256": "c2b96680f6cd9fd44070e998c89576beeb468ab88ac1f27a1d651f86f2c9abb4",
        "recipeSha256": "0bbd001e5f0469c922464801d967e366451d698e98d787c3c97488f7f07008a7",
        "formSha256": "0de78653f6fe4bbdbbd2a49a117a6a2870e7a5ecb9b04e85c6be86a21f44f0fc",
        "program": DISSOLVE_PROGRAM, "keywords": [""], "colorProperties": [], "dxbcSha256": {
            "vertex": "076c53f158c731b2703f8baa965b0b668ddd6df4f2f07875ca6e081db5592fbe",
            "fragment": "6759a3905d6da9e7464b1c344227dd6e2ab902265e3c08d7b16af1b5a7fee65e"},
    },
}


def stage_startup_blur(project):
    """Stage the separately audited original framebuffer blur without changing the two-shader receipt."""
    from ui_blur import stage_startup_blur as restore
    return restore(project)


def _hash(content):
    return hashlib.sha256(content).hexdigest()


def _safe(path):
    path = Path(os.path.abspath(path))
    if any(parent.is_symlink() for parent in (path, *path.parents)):
        raise BuildError("Linked original UI asset/provenance path: " + str(path))
    return path


def _read(path, maximum):
    _safe(path)
    if not path.is_file() or path.stat().st_size > maximum:
        raise BuildError("Original UI asset/provenance is missing or oversized: " + str(path))
    return path.read_bytes()


def _original_recipes(project):
    try:
        source = json.loads(_read(project / RECIPES, 32 * 1024 * 1024))
        result = {}
        for name, spec in SHADERS.items():
            rows = [row for row in source["recipes"] if row["parsedForm"]["m_Name"] == name]
            if len(rows) != 1:
                raise ValueError("expected one original shader: " + name)
            row = rows[0]
            form = json.dumps(row["parsedForm"], sort_keys=True, separators=(",", ":")).encode("utf-8")
            if (row["recipeSha256"] != spec["recipeSha256"] or _hash(form) != spec["formSha256"] or
                    row["compiledPlatforms"] != [4]):
                raise ValueError("compiled recipe/properties/states differ: " + name)
            result[name] = row
        return result
    except (ValueError, KeyError, TypeError) as error:
        raise BuildError("Original UI shader recipe differs: " + str(error)) from error


def _transcribe(dummy, spec):
    if _hash(dummy) != spec["dummySha256"]:
        raise BuildError("Original UI shader dummy differs from the audited input")
    prefix, marker, _ = dummy.decode("utf-8").partition("\t//DummyShaderTextExporter")
    if not marker:
        raise BuildError("Original UI dummy export marker is missing")
    # The exporter also spelled the original m_Type=0 Color properties as
    # Vector. Restore their recipe-proven types while retaining descriptions,
    # defaults and all serialized material values.
    for name in spec["colorProperties"]:
        pattern = r'(^\s*' + re.escape(name) + r'\s*\(\s*"[^"]*"\s*,\s*)Vector(\s*\))'
        prefix, count = re.subn(pattern, r'\1Color\2', prefix, flags=re.M)
        if count != 1:
            raise BuildError("Original UI color property declaration differs: " + name)
    source = (prefix + spec["program"]).encode("utf-8")
    if _hash(source) != spec["sourceSha256"]:
        raise BuildError("Transcribed original UI source differs from the audited program")
    return source


def stage_startup_ui(project):
    """Replace audited EULA and DLC-promotion shaders, retaining metadata verbatim.

    Run after startup asset recovery and before Unity imports/builds. A repeated
    call verifies the exact previously generated source and receipt; unknown
    changes fail before writing. Native scene, material and DLC assets are not
    edited. No external executable, package or network request is needed.
    """
    project = _safe(project)
    if not _safe(project / "Assets/Quest").is_dir():
        raise BuildError("Original UI restoration requires a generated Quest project")
    rows = _original_recipes(project)
    receipt_path = _safe(project / RECEIPT)
    if receipt_path.exists():
        try:
            previous = json.loads(_read(receipt_path, 1024 * 1024))
        except (ValueError, TypeError) as error:
            raise BuildError("Previous original UI restoration differs: " + str(error)) from error
    else:
        previous = None
    entries, before, contents = [], {}, {}
    for name, spec in SHADERS.items():
        target = _safe(project / spec["asset"])
        metadata = _read(_safe(Path(str(target) + ".meta")), 65536)
        if (re.findall(rb"^guid:\s*([0-9a-f]{32})\s*$", metadata, re.M) != [spec["guid"].encode()] or
                b"ShaderImporter:" not in metadata):
            raise BuildError("Original UI shader GUID/importer differs: " + name)
        existing = _read(target, 65536)
        if previous is not None:
            if _hash(existing) != spec["sourceSha256"]:
                raise BuildError("Previous restored original UI source differs: " + name)
            restored = existing
        else:
            restored = _transcribe(existing, spec)
        before[target], contents[target] = existing, restored
        entries.append({"name": name, "assetPath": spec["asset"], "guid": spec["guid"],
                        "originalDummySha256": spec["dummySha256"], "sourceSha256": _hash(restored),
                        "metaSha256": _hash(metadata), "canonicalRecipeSha256": rows[name]["recipeSha256"],
                        "originalDxbcSha256": spec["dxbcSha256"], "passCount": 1,
                        "keywords": spec["keywords"], "propertiesAndDefaultsRetained": True,
                        "originalColorPropertyTypesRestored": spec["colorProperties"],
                        "originalPassStatesRetained": True, "materialAndSceneUnchanged": True})
    receipt = {"schema": 1, "target": "startup", "recipe": RECIPE,
               "origin": "original-compiled-DXBC-transcription", "shaders": entries,
               "androidShaderCompiled": False, "originalPixelParityVerified": False}
    if previous is not None:
        if previous != receipt:
            raise BuildError("Previous original UI receipt differs from the verified source")
        return receipt
    written = []
    try:
        for target, content in contents.items():
            temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".restore")
            try:
                temporary.write_bytes(content)
                temporary.replace(target)
                written.append(target)
            finally:
                temporary.unlink(missing_ok=True)
        write_json(receipt_path, receipt)
    except BaseException:
        receipt_path.unlink(missing_ok=True)
        for target in reversed(written):
            target.write_bytes(before[target])
        raise
    return receipt
