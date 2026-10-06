using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Core;

// Conversion staging/grab registration are explicit boundaries. The actual production
// Surface, full-cover background filter and restoration block are bound verbatim.
namespace UnityEngine.UI { public sealed class UIWindow : MonoBehaviour { } }
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceQuietController { internal static bool IsSourceBoundary(Transform _) => false; }
    internal static partial class TownServicePresentation { internal static bool IsQuietTemple(UIWindow _) => false; }
    internal sealed class CounterSetting634 { internal float Value=1f; }
    internal static partial class WorldUIConfig { internal static readonly CounterSetting634 CanvasScaleMm=new(); }
    internal sealed class GrabbableModal
    { internal void Build(ConvertedPanel _,float scale,string name){} internal void SetExtraScale(float _){}
      internal void SnapFrameTo(Vector3 _,Quaternion yaw){} internal void Tick(){} internal void LateSyncHost(){} internal void Destroy(){} }
    internal sealed class ConvertedPanel
    {
        internal GameObject HostGo=null!;internal RectTransform HostRect=null!,Target=null!;
        internal Transform OriginalParent=null!;internal int OriginalSibling;
        internal Vector2 OriginalMin,OriginalMax,OriginalSize,OriginalPosition;
        internal Vector3 OriginalScale;
        internal bool IsAlive=>Target!=null;
        internal bool MrBackingSuppressed,HideBackground,KeepBackgroundHidden;
        internal int BackgroundSweepNextFrame,FitOneShotStableCount,FitSettleStillCount,FitOneShotStableGraphics;
        internal readonly List<Graphic> HiddenBackgrounds=new();
    }
    internal static partial class CanvasConversion
    {
        internal static ConvertedPanel Convert(RectTransform target,string name,bool fitContent,bool useModLayer,bool transparentBackground)
        {
            var size=target.rect.size;var host=new GameObject(name,typeof(RectTransform),typeof(Canvas));
            var rect=host.GetComponent<RectTransform>();rect.SetParent(target.parent,false);rect.sizeDelta=size;rect.pivot=target.pivot;
            var panel=new ConvertedPanel {HostGo=host,HostRect=rect,Target=target,OriginalParent=target.parent,
                OriginalSibling=target.GetSiblingIndex(),OriginalMin=target.anchorMin,OriginalMax=target.anchorMax,
                OriginalSize=target.sizeDelta,OriginalPosition=target.anchoredPosition,OriginalScale=target.localScale};
            target.SetParent(rect,false);target.anchorMin=target.anchorMax=rect.pivot;target.sizeDelta=size;target.anchoredPosition=Vector2.zero;
            host.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            bool keepBackgroundHidden=false;
            /* ORIGINAL_BACKGROUND_CONVERT */
            return panel;
        }
        internal static void PlaceHost(ConvertedPanel panel,Vector3 position,Quaternion rotation,float scale)
        {panel.HostRect.SetPositionAndRotation(position,rotation);panel.HostRect.localScale=Vector3.one*(.001f*scale);}
        internal static void Release(ConvertedPanel panel)
        {
            /* ORIGINAL_BACKGROUND_RESTORE */
            panel.Target.SetParent(panel.OriginalParent,false);panel.Target.SetSiblingIndex(panel.OriginalSibling);
            panel.Target.anchorMin=panel.OriginalMin;panel.Target.anchorMax=panel.OriginalMax;panel.Target.sizeDelta=panel.OriginalSize;
            panel.Target.anchoredPosition=panel.OriginalPosition;panel.Target.localScale=panel.OriginalScale;
            Object.DestroyImmediate(panel.HostGo);
        }
    }
}
