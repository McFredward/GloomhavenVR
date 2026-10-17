using UnityEngine;
namespace GloomhavenVR.Core
{
    // Only the production compile-time renderer selection is outside this isolated fixture.
    internal static class StereoModeConfig
    {
        internal enum Mode { MultiPass, SinglePassInstanced }
        internal const Mode Current = Mode.MultiPass;
    }
}
public static class StereoEyeFixture
{
    // GPU fixtures cannot create a headset provider. Substitute only Unity's eye observation;
    // Camera.Render still invokes the production shared onPreRender event handler.
    public static Camera.MonoOrStereoscopicEye ActiveEye = Camera.MonoOrStereoscopicEye.Mono;
}
