using UnityEngine;

// The editor executes GL pixels. This explicit backend input models only the
// D3D render-target projection's Y orientation while retaining GL depth encoding;
// it is not execution of a D3D driver or an XR provider.
internal static class HeadPitchBackend736
{
    internal static bool TopOrigin=SystemInfo.graphicsUVStartsAtTop;
    internal static Matrix4x4 TopOriginGpuProjection(Matrix4x4 projection,bool renderToTexture)
    {
        Matrix4x4 gpu=GL.GetGPUProjectionMatrix(projection,renderToTexture);
        gpu.SetRow(1,-gpu.GetRow(1));
        return gpu;
    }
}
