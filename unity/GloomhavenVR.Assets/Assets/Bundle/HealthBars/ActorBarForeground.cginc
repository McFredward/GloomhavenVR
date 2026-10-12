// MultiPass owns a fresh 2D foreground depth target for each actual eye pass.
// SV_POSITION is already in that target's pixel convention on D3D and GL; unlike
// a reconstructed centre-eye projection it has no eye, viewport or Y-flip drift.
UNITY_DECLARE_DEPTH_TEXTURE(_GHVRActorForegroundDepth);
float4 _GHVRActorForegroundSize;
float _GHVRActorForegroundReady;

void GHVRActorBarForegroundClip(float4 pixelPosition)
{
    if (_GHVRActorForegroundReady > 0.5)
    {
        float foreground = SAMPLE_DEPTH_TEXTURE(_GHVRActorForegroundDepth,
            pixelPosition.xy * _GHVRActorForegroundSize.xy);
        // LinearEyeDepth uses the actual camera's _ZBufferParams, including
        // reversed-Z. Compare fragment depth, never renderer centre distance.
        clip(LinearEyeDepth(foreground) - LinearEyeDepth(pixelPosition.z) + 0.00001);
    }
}
