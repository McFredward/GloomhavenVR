using System.Collections.Generic;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel
    {
        internal RectTransform HostRect = null!, Target = null!;
        internal GameObject HostGo = null!;
        internal bool IsAlive = true, ModLayerEnabled = true;
        internal bool RenderHidden, OwnerRenderHidden;
    }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels = new();
    }
    // Explicit already-engaged panel resources. The production .6 coordinator,
    // registry, source renders and head presentation execute unchanged.
    internal static partial class PanelSupersample
    {
        internal sealed partial class Entry
        {
            internal ConvertedPanel Panel = new(); internal Camera Cam = null!;
            internal RenderTexture Rt = null!; internal RawImage DisplayImage = null!;
            internal MenuDepthDisplay? MenuDepthDisplay; internal int MenuDepthFrame = -1, MenuDepthFailures;
            internal long MenuDepthBytes, VramBytes; internal int RtW, RtH, Msaa = 1;
            internal string Window = "actual test window"; internal Rect Frame;
            internal int Layer;
        }
        private sealed class TransformIdentityComparer : IEqualityComparer<Transform>
        {
            internal static readonly TransformIdentityComparer Instance = new();
            public bool Equals(Transform a, Transform b) => ReferenceEquals(a,b);
            public int GetHashCode(Transform value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        private sealed class LayerRecord { internal Transform Transform = null!; internal int OriginalLayer; }
        private static readonly List<Entry> Entries = new();
        private static readonly HashSet<int> Refused = new();
        private const int EffectiveMaxPanels = 8;
        private static int AdmissionRequests;
        private static bool Eligible(ConvertedPanel panel) => panel.IsAlive && panel.ModLayerEnabled;
        private static bool FailedOut(string window) => false;
        private static string WindowNameOf(ConvertedPanel panel) => panel.Target.name;
        private static void Engage(ConvertedPanel panel) { AdmissionRequests++; Refused.Add(panel.HostGo.GetInstanceID()); }
        internal static int ConsiderAdmissions()
        { int before = AdmissionRequests; Consider(); return AdmissionRequests - before; }
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
            var panel = new ConvertedPanel { HostRect = root, Target = root, HostGo = root.gameObject };
            CanvasConversion.ActivePanels.Add(panel);
            var e = new Entry { Panel = panel, Cam = capture, Rt = target, Layer = root.gameObject.layer,
                DisplayImage = image, Frame = root.rect, RtW = target.width, RtH = target.height,
                VramBytes = target.width * (long)target.height * 8 };
            Entries.Add(e); _vramTotal += e.VramBytes;
            if (Entries.Count == 1) Camera.onPreCull += ConvertedHeadPreCull745;
            MenuDepthSources.BeforeHeadCapture += CaptureMenuDepth;
            return e;
        }
        internal static void EndHead(Entry e)
        {
            MenuDepthSources.BeforeHeadCapture -= CaptureMenuDepth;
            ForgetOwnedPanel(e.Panel);
            ReleaseMenuDepth(e); Entries.Remove(e); _vramTotal -= e.VramBytes;
            if (Entries.Count == 0) Camera.onPreCull -= ConvertedHeadPreCull745;
            CanvasConversion.ActivePanels.Remove(e.Panel);
        }
        // Original admission/resource disposal is a declared boundary of this
        // head fixture. The explicit member and full .6 coordinator execute.
        private static bool IsForeignPoolLayer(Entry mine, int layer)
        {
            for(int i=0;i<Entries.Count;i++)
                if(!ReferenceEquals(Entries[i],mine)&&Entries[i].Layer==layer)return true;
            return false;
        }
        private static void StandDown(Entry e,int index,string reason) => EndHead(e);
        internal static ConvertedPanel MemberPanel(RectTransform root)
        {
            var panel=new ConvertedPanel{HostRect=root,Target=root,HostGo=root.gameObject};
            CanvasConversion.ActivePanels.Add(panel);return panel;
        }
        internal static void ForgetMember(ConvertedPanel panel)
        { NoticeRelease(panel);CanvasConversion.ActivePanels.Remove(panel); }
        internal static bool MemberCaptured(ConvertedPanel panel) => CapturedOwnedMember(panel);
        internal static void MonoBudget(long bytes) => _vramTotal=bytes;
        internal static int OwnedRelationCount => OwnedMembers.Count;
        internal static void ResetOwnedRelations() => ResetOwnedMembers();
    }
}
