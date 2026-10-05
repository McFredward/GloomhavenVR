"""Recover typed UAV storage from the unchanged native allocation/binding code.

DXBC declares four return components even for an RFloat UAV, and therefore does
not retain the source image format. GLES image qualifiers must match the actual
RenderTexture. These contracts are audited against this exact original managed
assembly; a different game revision requires a new allocation audit.
"""
if __package__:
    from .native import ComputeRecoveryError
else:
    from native import ComputeRecoveryError

POST_PROCESSING_SHA256 = "ce62c0bc9c421f45fea6b81a0869761b749365dce5a9dffd5d639b2f170c788c"


def image_contract(shader: str, kernel: str, resource: str) -> dict:
    msaa = "_MSAA" in kernel
    if shader == "AutoExposure" and resource == "_Destination":
        row = ("RFloat", "float", "r32f", 1, "AutoExposureRenderer.CheckTexture/Render")
    elif shader in ("MultiScaleVODownsample1", "MultiScaleVODownsample2"):
        if resource == "LinearZ" or resource.endswith("Atlas"):
            row = ("RGHalf", "half2", "rg16f", 2, "MultiScaleVO.PushAllocCommands/PushDownsampleCommands") if msaa else (
                "RHalf", "half", "r16f", 1, "MultiScaleVO.PushAllocCommands/PushDownsampleCommands")
        elif resource in ("DS2x", "DS4x", "DS8x", "DS16x"):
            row = ("RGFloat", "float2", "rg32f", 2, "MultiScaleVO.PushAllocCommands/PushDownsampleCommands") if msaa else (
                "RFloat", "float", "r32f", 1, "MultiScaleVO.PushAllocCommands/PushDownsampleCommands")
        else:
            raise ComputeRecoveryError("Unknown native MSVO depth output allocation: " + resource)
    elif shader in ("MultiScaleVORender", "MultiScaleVOUpsample") and resource in ("Occlusion", "AoResult"):
        row = ("RG16", "unorm float2", "rg8", 2, "MultiScaleVO.PushAllocCommands/PushRenderCommands/PushUpsampleCommands") if msaa else (
            "R8", "unorm float", "r8", 1, "MultiScaleVO.PushAllocCommands/CheckAOTexture/PushUpsampleCommands")
    elif shader in ("Lut3DBaker", "Texture3DLerp") and resource == "_Output":
        row = ("ARGBHalf", "half4", "rgba16f", 4, "ColorGradingRenderer.CheckInternalLogLut/GetLutFormat" if shader == "Lut3DBaker"
               else "TextureLerper.Lerp/Get")
    elif shader == "GaussianDownsample" and resource == "_Result":
        # No native runtime caller was found in the original 71-assembly census.
        # Preserve the original four-component DXBC declaration; do not invent
        # an allocation format for this unused original resource.
        row = ("ARGBFloat", "float4", "rgba32f", 4, "original-DXBC-four-component-UAV; no-live-native-caller")
    else:
        raise ComputeRecoveryError("Original typed compute output has no audited storage contract: " + shader + "/" + resource)
    return dict(zip(("renderTextureFormat", "hlslElementType", "glslImageQualifier", "channelCount", "nativeCaller"), row))
