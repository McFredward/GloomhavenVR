using System.Collections.Generic;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    // Explicit already-engaged panel resources. The production .6 coordinator,
    // registry, source renders and head presentation execute unchanged.
    internal static partial class PanelSupersample
    {
        internal sealed class PanelInput { internal RectTransform HostRect = null!; }
        internal sealed class Entry
        {
            internal PanelInput Panel = new(); internal Camera Cam = null!;
            internal RenderTexture Rt = null!; internal RawImage DisplayImage = null!;
            internal MenuDepthDisplay? MenuDepthDisplay; internal int MenuDepthFrame = -1, MenuDepthFailures;
            internal long MenuDepthBytes, VramBytes; internal int RtW, RtH, Msaa = 1;
            internal string Window = "actual test window"; internal Rect Frame;
        }
        private static readonly List<Entry> Entries = new();
        private static long _vramTotal;
        private const long MaxPanelVramBytes = 160L * 1024 * 1024, MaxTotalVramBytes = 448L * 1024 * 1024;
        private const string Scope = "WorldUI";
        private enum ReallocTrigger { EyeGrid }
        private static void Reallocate(Entry entry, Rect frame, ReallocTrigger trigger)
            => throw new System.Exception("Unexpected fixture mono allocation; original fixed target fits both actual budgets.");
        private static RenderTexture DisplayTexture(Entry entry) => entry.Rt;
        private static void SyncDisplayPose(Entry entry) { }
        private static void NoteCaptureCompleted(Entry entry) { }
        private static void SyncProjection(Entry entry) { }
        internal static Entry BindHead(RectTransform root, Camera capture, RenderTexture target, RawImage image)
        {
            var e = new Entry { Panel = new PanelInput { HostRect = root }, Cam = capture, Rt = target,
                DisplayImage = image, Frame = root.rect, RtW = target.width, RtH = target.height,
                VramBytes = target.width * (long)target.height * 8 };
            Entries.Add(e); _vramTotal += e.VramBytes;
            MenuDepthSources.BeforeHeadCapture += CaptureMenuDepth;
            return e;
        }
        internal static void EndHead(Entry e)
        {
            MenuDepthSources.BeforeHeadCapture -= CaptureMenuDepth;
            ReleaseMenuDepth(e); Entries.Remove(e); _vramTotal -= e.VramBytes;
        }
    }
}
