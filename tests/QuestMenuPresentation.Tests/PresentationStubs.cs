using System.Collections.Generic;

namespace UnityEngine
{
    internal class RenderTexture
    {
        internal string name = "";
        internal void Release() { }
    }
    internal class Camera
    {
        internal static Camera? main;
        internal string tag = "Untagged";
        internal bool enabled = true;
        internal float depth;
        internal RenderTexture? targetTexture;
        internal bool CompareTag(string value) => tag == value;
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal static class VRCameraPolicy
    {
        internal static UnityEngine.Camera[] Cameras = System.Array.Empty<UnityEngine.Camera>();
        internal static int GetAllCamerasNonAlloc(out UnityEngine.Camera[] cameras)
        { cameras = Cameras; return cameras.Length; }
    }
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver
    {
        internal static UnityEngine.Camera? HeadCamera;
        internal static UnityEngine.Camera? Resolve() => ResolveMenuCamera();
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed partial class FlatScreen
    {
        private sealed class CapturedCamera
        {
            internal FlatScreen Owner = null!;
            internal bool IsUi;
        }
        private static readonly Dictionary<UnityEngine.Camera, CapturedCamera> CapturedSet = new();
        private UnityEngine.RenderTexture? _rt, _uiRt, _scrubRt;
        private bool _splitRouting;
        internal static void ResetClaims() { CapturedSet.Clear(); DesktopScrubTarget = null; }
        internal void Capture(UnityEngine.Camera camera, UnityEngine.RenderTexture rt, bool isUi = false,
            bool split = false, UnityEngine.RenderTexture? uiRt = null)
        {
            _rt = rt; _uiRt = uiRt; _splitRouting = split;
            CapturedSet[camera] = new CapturedCamera { Owner = this, IsUi = isUi };
        }
        internal void BeginScrub(UnityEngine.RenderTexture target)
        { _scrubRt = target; PublishScrub(); }
        internal void EndScrub() { DropScrub(); _scrubRt = null; }
        internal void Route(bool split) { _splitRouting = split; }
    }
}
