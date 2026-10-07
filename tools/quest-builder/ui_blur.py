"""Restore the attached original UI blur in private generated Quest projects.

The five programs are transcribed from sharedassets1.assets Shader pathID 872,
not inferred from a generic blur example. The original renderer accepts no COLOR
attribute and uses opaque replacement of three successive framebuffer grabs.
Neither native visibility nor CanvasRenderer culling is changed here.
"""
import json
from pathlib import Path
import re
import uuid

from storage import BuildError, write_json
from ui_assets import _hash, _read, _safe, _transcribe, RECIPES

NAME = "Custom/SimpleGrabPassBlur"
ASSET = "Assets/Shader/Custom_SimpleGrabPassBlur.shader"
GUID = "a0b681ed431356a47b5287e6441c1c11"
RECEIPT = "QuestStartupEvidence/original-ui-blur.json"
RECIPE = "original-ui-blur-dxbc-port-v1"
RECIPE_SHA256 = "8c0b49510c15a60b71b6da8638bdca047d39fbc6317b8cbdd1d9d956413461ed"
FORM_SHA256 = "fa7b20ad60256357747bf0b160e9ef3d9a411391199f8b2d788a48f32fe04282"
DUMMY_SHA256 = "2becde7e00713952ce9151750bf0ef7970cfd8ff23fc4a970696f768cb440df7"
SOURCE_SHA256 = "408ffb291c0a071cd0eef920f87e0867c8007d0b01a8775d6e011811a8832bfc"
DXBC_SHA256 = {
    "grabVertex": "090e42733308dd506b0161f21410399f44c352fcb9a3bc19b3617314d310f8ef",
    "horizontalFragment": "faaf888f8d94c3464870932a40a683ef85256c7dcc45c62d92a15ac622296d4f",
    "verticalFragment": "922df212d277454ad870cba807774e3219a479d3b20bdad73265f3a69a598435",
    "distortionVertex": "cc824340e88532a35b4958ce8dd5fce9d7b976dbd3725ce3b93ef9396cb22ba7",
    "distortionFragment": "594977e3a883942b6b74e461b0953955919efe4c837903f4672a69334a35df5c",
}

PROGRAM = r'''    // Quest: original-ui-blur-dxbc-port-v1, original five-program transcription.
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "true" "RenderType" = "Opaque" }
        Cull Back
        ZTest LEqual
        ZWrite On
        Blend One Zero
        ColorMask RGBA
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _GrabTexture, _MainTex, _BumpMap;
        float4 _GrabTexture_TexelSize, _MainTex_ST, _BumpMap_ST, _Color;
        float _Size, _BumpAmt;
        struct Input { float3 position : POSITION; float2 uv : TEXCOORD0; };
        struct BlurOutput { float4 position : SV_POSITION; float4 grab : TEXCOORD0; };
        struct DistortionOutput
        {
            float4 position : SV_POSITION;
            float4 grab : TEXCOORD0;
            float4 uv : TEXCOORD1;
        };
        BlurOutput grabVertex(Input input)
        {
            BlurOutput output;
            output.position = UnityObjectToClipPos(float4(input.position, 1));
            // The DXBC Y inversion is the D3D framebuffer convention. Unity's
            // helper supplies the corresponding GLES convention without changing
            // homogeneous Z/W or applying _ProjectionParams to the grab again.
            output.grab = ComputeGrabScreenPos(output.position);
            return output;
        }
        float4 horizontal(BlurOutput input) : SV_Target
        {
            float step = _GrabTexture_TexelSize.x * _Size;
            float4 sum = tex2D(_GrabTexture, (input.grab.xy + float2(-3 * step, 0)) / input.grab.w) * 0.09;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(-4 * step, 0)) / input.grab.w) * 0.05 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(-2 * step, 0)) / input.grab.w) * 0.12 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(-step, 0)) / input.grab.w) * 0.15 + sum;
            sum = tex2D(_GrabTexture, input.grab.xy / input.grab.w) * 0.18 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(step, 0)) / input.grab.w) * 0.15 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(2 * step, 0)) / input.grab.w) * 0.12 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(3 * step, 0)) / input.grab.w) * 0.09 + sum;
            return tex2D(_GrabTexture, (input.grab.xy + float2(4 * step, 0)) / input.grab.w) * 0.05 + sum;
        }
        float4 vertical(BlurOutput input) : SV_Target
        {
            float step = _GrabTexture_TexelSize.y * _Size;
            float4 sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, -3 * step)) / input.grab.w) * 0.09;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, -4 * step)) / input.grab.w) * 0.05 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, -2 * step)) / input.grab.w) * 0.12 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, -step)) / input.grab.w) * 0.15 + sum;
            sum = tex2D(_GrabTexture, input.grab.xy / input.grab.w) * 0.18 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, step)) / input.grab.w) * 0.15 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, 2 * step)) / input.grab.w) * 0.12 + sum;
            sum = tex2D(_GrabTexture, (input.grab.xy + float2(0, 3 * step)) / input.grab.w) * 0.09 + sum;
            return tex2D(_GrabTexture, (input.grab.xy + float2(0, 4 * step)) / input.grab.w) * 0.05 + sum;
        }
        DistortionOutput distortionVertex(Input input)
        {
            DistortionOutput output;
            output.position = UnityObjectToClipPos(float4(input.position, 1));
            output.grab = ComputeGrabScreenPos(output.position);
            output.uv.xy = input.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
            output.uv.zw = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
            return output;
        }
        float4 distortion(DistortionOutput input) : SV_Target
        {
            float4 encodedNormal = tex2D(_BumpMap, input.uv.xy);
            // The native D3D bank is Unity's RGB-or-AG R*A unpack. Android
            // ASTC normal import can duplicate X into R and A, so its genuine
            // Unity 2021.3.5 encoding uses AG; plain RGB uses RG. These are
            // the exact XY branches of UnityCG.UnpackNormal, without Z work.
            #if defined(UNITY_NO_DXT5nm)
            float2 normal = encodedNormal.rg * 2 - 1;
            #elif defined(UNITY_ASTC_NORMALMAP_ENCODING)
            float2 normal = encodedNormal.ag * 2 - 1;
            #else
            float2 normal = float2(encodedNormal.r * encodedNormal.a, encodedNormal.g) * 2 - 1;
            #endif
            float2 offset = normal * _BumpAmt * _GrabTexture_TexelSize.xy;
            float2 distorted = (offset * input.grab.z + input.grab.xy) / input.grab.w;
            return tex2D(_GrabTexture, distorted) * tex2D(_MainTex, input.uv.zw) * _Color;
        }
        ENDCG
        GrabPass { }
        Pass
        {
            Tags { "LightMode" = "Always" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex grabVertex
            #pragma fragment horizontal
            ENDCG
        }
        GrabPass { }
        Pass
        {
            Tags { "LightMode" = "Always" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex grabVertex
            #pragma fragment vertical
            ENDCG
        }
        GrabPass { }
        Pass
        {
            Tags { "LightMode" = "Always" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex distortionVertex
            #pragma fragment distortion
            #pragma multi_compile __ UNITY_ASTC_NORMALMAP_ENCODING
            ENDCG
        }
    }
}
'''


def stage_startup_blur(project):
    """Restore only the original UI_Blur shader GUID actually attached at startup."""
    project = _safe(project)
    if not _safe(project / "Assets/Quest").is_dir():
        raise BuildError("Original UI blur restoration requires a generated Quest project")
    try:
        rows = json.loads(_read(project / RECIPES, 32 * 1024 * 1024))["recipes"]
        matches = [row for row in rows if row.get("recipeSha256") == RECIPE_SHA256
                   and row.get("parsedForm", {}).get("m_Name") == NAME]
        if len(matches) != 1:
            raise ValueError("expected exact attached original blur recipe")
        original = matches[0]
        form = json.dumps(original["parsedForm"], sort_keys=True, separators=(",", ":")).encode()
        if _hash(form) != FORM_SHA256 or original["compiledPlatforms"] != [4]:
            raise ValueError("original blur properties, states or program bank differs")
    except (ValueError, KeyError, TypeError, AttributeError) as error:
        raise BuildError("Original UI blur source contract differs: " + str(error)) from error
    target = _safe(project / ASSET)
    metadata = _read(Path(str(target) + ".meta"), 65536)
    if re.findall(rb"^guid:\s*([0-9a-f]{32})\s*$", metadata, re.M) != [GUID.encode()] or b"ShaderImporter:" not in metadata:
        raise BuildError("Original attached UI blur GUID/importer differs")
    receipt_path = _safe(project / RECEIPT)
    previous = None
    if receipt_path.exists():
        try:
            previous = json.loads(_read(receipt_path, 1024 * 1024))
        except (ValueError, TypeError) as error:
            raise BuildError("Previous original blur receipt differs") from error
    existing = _read(target, 65536)
    spec = {"dummySha256": DUMMY_SHA256, "sourceSha256": SOURCE_SHA256,
            "colorProperties": ["_Color"], "program": PROGRAM}
    if previous is None:
        restored = _transcribe(existing, spec)
    else:
        if _hash(existing) != SOURCE_SHA256:
            raise BuildError("Previous restored original blur source differs")
        restored = existing
    receipt = {"schema": 1, "target": "startup", "recipe": RECIPE,
               "origin": "original-compiled-DXBC-transcription", "name": NAME,
               "assetPath": ASSET, "guid": GUID, "sourceSha256": _hash(restored),
               "metaSha256": _hash(metadata), "canonicalRecipeSha256": RECIPE_SHA256,
               "canonicalFormSha256": FORM_SHA256, "originalDxbcSha256": DXBC_SHA256,
               "originalDummySha256": DUMMY_SHA256, "drawPassCount": 3, "grabPassCount": 3,
               "normalEncodingPolicy": "Unity-2021.3.5-UnpackNormal-XY",
               "originalVertexColorAbsent": True, "originalPassStatesRetained": True,
               "propertiesAndDefaultsRetained": True, "materialAndSceneUnchanged": True,
               "androidShaderCompiled": False, "originalPixelParityVerified": False}
    if previous is not None:
        if previous != receipt:
            raise BuildError("Previous original blur receipt differs from the verified source")
        return receipt
    temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".restore")
    try:
        temporary.write_bytes(restored)
        temporary.replace(target)
        write_json(receipt_path, receipt)
    except BaseException:
        receipt_path.unlink(missing_ok=True)
        target.write_bytes(existing)
        raise
    finally:
        temporary.unlink(missing_ok=True)
    return receipt
