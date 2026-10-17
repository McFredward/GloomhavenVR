using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
namespace GloomhavenVR.Core
{
    internal static class StereoModeConfig
    {
        internal enum Mode { MultiPass, SinglePassInstanced }
        internal const Mode Current = Mode.MultiPass;
    }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Note(string scope, string text) => Messages.Add(text);
        internal static void Warn(string scope, string text) => Messages.Add(text);
    }
}
public static class StereoEyeFixture
{
    public static Camera.MonoOrStereoscopicEye ActiveEye;
}
public static class MovieDecoderFixture
{
    public static Texture? Texture;
    public static bool Prepared = true;
}
namespace GloomhavenVR.WorldUI
{
    // Decoder pixel transport is outside the raster fixture. Real VideoPlayer properties
    // remain in use for the camera-plane alpha lease; the source census alone is supplied.
    internal static class NativeVideoMovies
    {
        internal static VideoPlayer? CameraPlaneSource;
        internal static string NativePlaybackKey(VideoPlayer player) => "Embedded/studio.sbs-v1.mov";
    }
    internal sealed partial class FlatScreenStereo
    {
        private bool _active = true;
        private bool _introGuard = false;
        private RenderTexture? _rtRight, _rtLeftShifted, _leftRt;
        private int _shiftBlitFrame = -1;
        private bool EnsureShiftRt() => _rtLeftShifted != null;
        internal void Initialize(RenderTexture left, RenderTexture right, RenderTexture? background = null) { _rtLeftShifted = left; _rtRight = right; _leftRt = background; }
        internal void Draw(Camera camera, Material material) => ShowStereoMovie(camera, material);
        internal static void Pixels(Texture texture, RenderTexture target, int eye) => DrawMovieEye(texture, target, eye);
        internal void SourceChanged() => SyncStereoMovie();
    }
}
