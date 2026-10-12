// MultiPass owns a fresh 2D foreground depth target for each actual eye pass.
// The unmodified native clip position travels through Unity's ComputeScreenPos:
// normalized coordinates map into the actual captured eye viewport. Native UI
// SV_POSITION/_ScreenParams units can differ from the allocated render target,
// so do not use those units for texture XY or reconstruct a centre-eye projection.
UNITY_DECLARE_DEPTH_TEXTURE(_GHVRActorForegroundDepth);
float4 _GHVRActorForegroundSize;
float4 _GHVRActorForegroundViewport;
float _GHVRActorForegroundReady;

void GHVRActorBarForegroundClip(float4 pixelPosition, float4 screenPosition)
{
    if (_GHVRActorForegroundReady > 0.5)
    {
        float2 capturePixel = _GHVRActorForegroundViewport.xy
            + (screenPosition.xy / screenPosition.w) * _GHVRActorForegroundViewport.zw;
        float foreground = SAMPLE_DEPTH_TEXTURE(_GHVRActorForegroundDepth,
            capturePixel * _GHVRActorForegroundSize.xy);
        // LinearEyeDepth uses the actual camera's _ZBufferParams, including
        // reversed-Z. Compare fragment depth, never renderer centre distance.
        clip(LinearEyeDepth(foreground) - LinearEyeDepth(pixelPosition.z) + 0.00001);
    }
}
