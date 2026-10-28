using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform
    {
        internal static bool Enabled => false;
        internal static bool SharedStereoEyeRouting => false;
        internal static string ResourceDirectory => string.Empty;
        internal static void SetFlatScreenEyes(Material material, Texture? left, Texture? right) { }
        internal static void SetFlatScreenMono(Material material, Texture? texture) { }
    }
    internal static class QuestMovieAssets
    {
        internal static Shader Shader(string name) => throw new System.InvalidOperationException("Android shader loading is exercised separately.");
    }

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
