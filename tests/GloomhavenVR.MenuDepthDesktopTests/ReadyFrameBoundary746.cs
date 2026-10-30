using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Native subject identity is an explicit port; no native game callback runs in a render test.
public sealed class UIReadyToggle : MonoBehaviour { }
namespace GloomhavenVR.WorldUI
{
    internal static class RewardHeadingBounds { internal static Rect Expand(RectTransform host,Graphic? graphic,Rect bounds)=>bounds; }
    internal static class TownServiceNativeEnhancementCardMask
    {internal static bool TryAuraCaptureBounds(RectTransform host,out Rect bounds){bounds=default;return false;}}
    internal static partial class PanelSupersample
    {
        private static readonly Rect Unbounded=Rect.MinMaxRect(-1e6f,-1e6f,1e6f,1e6f);
        private static readonly Vector3[] ContentCorners=new Vector3[4];
        private static readonly List<ClipFrame> ContentStack=new();
        internal sealed partial class Entry
        {
            internal GameObject? CamGo;
            internal int FrameMeasuresSkipped,FrameGrows,ShrinkRun,FrameOutlierGrowths,FrameShrinks,BandLimitShrinks,
                FrameOutlierReleases,FrameShrinksRefused,ContentMeasures,LastMeasureFrame;
            internal float HoldLeft,HoldRight,HoldDown,HoldUp,RunLeft,RunRight,RunDown,RunUp,
                CropLeft,CropRight,CropDown,CropUp,ExpandX,ExpandY;
            internal bool FrameOutlierPending,ExpandClamped,ScaleAtMeasureValid,ExpandClampWarned;
            internal Rect HostRectAtMeasure;
            internal Vector3 ScaleAtMeasure;
            internal double ContentMs;
        }
        // Every Ready case measures a fresh stationary original host. These scheduling and
        // diagnostic ports are never substitutes for the actual walk, clip, clamp or pixels.
        private static bool SkipMeasureWhileTranslating(Entry e,RectTransform host)=>false;
        private static bool IsMoving(Entry e)=>false;
        private static bool ShrinkRecoversBandLimit(Entry e,Rect host,float l,float r,float d,float u)=>false;
        private static void ResetExtremes(Rect host) { }
        private static void NoteExtremes(string name,Rect ink) { }
        private static void CommitExtremes(Entry e) { }
        private static void RecordNeed(Entry e,float w,float h) { }
        private static void NoteBandLimitClamp(Entry e,Rect host,Rect frame) { }
        private static void NoteFrameChange(Entry e,Rect frame) { }
        private static void RecordContentScale(Entry e,float a,float b,string c,int d,int f,float g) { }
        internal static Rect MeasureReadyFrame(Entry e){MeasureFrame(e);return e.Frame;}
    }
}
