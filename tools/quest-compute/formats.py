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


def native_platform_contract(shader: str) -> dict | None:
    """Record an existing original platform branch, without changing it.

    These exact managed methods were audited in the hash-pinned original DLL.
    A cooked GLSL bank does not prove that a GLES driver accepts every image
    format, nor does it override the original caller's capability decision.
    """
    common = {"assembly": "Unity.Postprocessing.Runtime.dll", "sha256": POST_PROCESSING_SHA256,
              "originalMethodUnchanged": True, "allOriginalKernelsRetained": True}
    if shader.startswith("MultiScaleVO"):
        return {**common, "method": "AmbientOcclusion.IsEnabledAndSupported",
            "mode": "MultiScaleVolumetricObscurance", "androidOpenGlesBranchReachable": False,
            "requiredComputeSupport": True, "requiredNotAndroidOpenGL": True,
            "androidOpenGLDefinitionMethod": "RuntimeUtilities.get_isAndroidOpenGL",
            "androidOpenGLDefinition": "Application.platform == Android && graphicsDeviceType != Vulkan",
            "requiredLoadStoreFormats": ["R32_SFloat", "R16_SFloat", "R8_UNorm"],
            "requiredLoadStoreFormatValues": [49, 45, 5],
            "normalCaller": "PostProcessLayer.BuildCommandBuffers checks settings.IsEnabledAndSupported before RenderAfterOpaque/RenderAmbientOnly",
            "directBakeBypass": "PostProcessLayer.BakeMSVOMap exists; no direct callers found in the original 71-assembly census"}
    if shader == "Lut3DBaker":
        return {**common, "method": "ColorGradingRenderer.Render",
            "pipeline": "HDR 3D LUT compute baker", "androidOpenGlesBranchReachable": False,
            "requiredComputeSupport": True, "required3DRenderTextureSupport": True,
            "excludedGraphicsDevices": ["OpenGLCore", "OpenGLES3"],
            "originalOpenGlesPipeline": "RenderHDRPipeline2D"}
    return None


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
    contract = dict(zip(("renderTextureFormat", "hlslElementType", "glslImageQualifier", "channelCount", "nativeCaller"), row))
    contract["gles31CoreImageFormat"] = contract["glslImageQualifier"] in ("r32f", "rgba32f", "rgba16f", "rgba8")
    # This is existing original capability behaviour, not a new feature switch.
    # GLES cannot make an RHalf UAV legal simply by omitting its qualifier:
    # glBindImageTexture still restricts image unit formats to the core table.
    contract["originalAndroidGlesCapabilityBranchExcludesShader"] = shader.startswith("MultiScaleVO")
    return contract
