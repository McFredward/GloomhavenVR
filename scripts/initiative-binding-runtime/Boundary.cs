#nullable enable
#pragma warning disable CS0649, CS0414
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Note(string category, string text) { } internal static void Info(string category, string text) { } internal static void Warn(string category, string text) { } }
    internal static class PerfConfig { internal static float InitiativeDepthInterval => 1; }
    internal static class PerfMonitor { internal readonly struct Lease: IDisposable { public void Dispose() { } } internal static Lease Scope(string name) => new Lease(); }
}
namespace GloomhavenVR.Cards
{
    internal sealed class PlayTray
    {
        internal static PlayTray? Current;
        internal Transform? InitiativeMount;
        internal const float TrayPixelsPerMeter = 2400, InitiativeMountWidth = .56f, InitiativeMountMaxHeight = .14f;
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IDepthPortraitPicker { bool TryPickPortrait(Vector3 origin, Vector3 direction, out GameObject target, out Vector3 hit); }
    internal static class DepthPortraitPicks
    {
        internal static readonly Dictionary<Canvas, IDepthPortraitPicker> Entries = new();
        internal static void Register(Canvas canvas, IDepthPortraitPicker picker) => Entries[canvas] = picker;
        internal static void Unregister(Canvas canvas) => Entries.Remove(canvas);
    }
}
public sealed class InitiativeTrack: MonoBehaviour
{
    public static InitiativeTrack? Instance;
    public Transform? initiativeTrackHolder;
    public bool isAnimating, animationDelayed;
    public int RestoredMips, RescannedMips;
}
public sealed class InitiativeTrackActorBehaviour: MonoBehaviour
{
    public Button? avatarButton;
    public int Clicks;
}
public sealed class Choreographer { public static Choreographer? s_Choreographer; }
namespace GloomhavenVR.WorldUI
{
    internal enum PanelSlot { InitiativeTrack }
    internal static class PanelLayout
    {
        internal static float WorldScale => 1;
        internal static bool TryGetPose(PanelSlot slot, out Vector3 position, out Quaternion rotation)
        { position = new Vector3(0, 1, 2); rotation = Quaternion.identity; return true; }
    }
    internal sealed class FloatValue { internal float Value = 10; }
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive = true;
        internal static FloatValue InitiativeDepthMaxSpreadPx = new FloatValue();
    }
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target = null!, HostRect = null!;
        internal GameObject HostGo = null!;
        internal Canvas HostCanvas = null!;
        internal GraphicRaycaster HostRaycaster = null!;
        internal Transform HostTransform => HostGo.transform;
        internal bool IsAlive => Target != null;
        internal object? OrderCluster;
        internal RectTransform? FitContentRoot;
        internal bool FitEnabled = true;
        internal int FitAppliedGeneration;
        internal Transform? OriginalParent;
        internal Vector2 OriginalMin, OriginalMax, OriginalPivot, OriginalPosition, OriginalSize;
        internal Vector3 OriginalScale, OriginalLocal;
        internal Quaternion OriginalRotation;
    }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> Active = new();
        internal static IReadOnlyList<ConvertedPanel> ActivePanels => Active;
        internal static ConvertedPanel Convert(RectTransform target, string name, bool flatten2D)
        {
            var p = new ConvertedPanel { Target = target, OriginalParent = target.parent,
                OriginalMin = target.anchorMin, OriginalMax = target.anchorMax, OriginalPivot = target.pivot,
                OriginalPosition = target.anchoredPosition, OriginalSize = target.sizeDelta,
                OriginalScale = target.localScale, OriginalLocal = target.localPosition, OriginalRotation = target.localRotation };
            p.HostGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            p.HostRect = (RectTransform)p.HostGo.transform;
            p.HostRect.sizeDelta = new Vector2(800, 180);
            p.HostCanvas = p.HostGo.GetComponent<Canvas>(); p.HostCanvas.renderMode = RenderMode.WorldSpace;
            p.HostRaycaster = p.HostGo.GetComponent<GraphicRaycaster>();
            target.SetParent(p.HostRect, false); target.anchorMin = target.anchorMax = target.pivot = new Vector2(.5f,.5f);
            target.anchoredPosition = Vector2.zero; target.localPosition = Vector3.zero; target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
            Active.Add(p); return p;
        }
        internal static void Release(ConvertedPanel p)
        {
            Active.Remove(p);
            if (p.Target != null)
            {
                var t = p.Target; t.SetParent(p.OriginalParent, false);
                t.anchorMin = p.OriginalMin; t.anchorMax = p.OriginalMax; t.pivot = p.OriginalPivot;
                t.sizeDelta = p.OriginalSize; t.anchoredPosition = p.OriginalPosition;
                t.localScale = p.OriginalScale; t.localRotation = p.OriginalRotation; t.localPosition = p.OriginalLocal;
            }
            if (p.HostGo != null) UnityEngine.Object.DestroyImmediate(p.HostGo);
        }
        internal static void PlaceHost(ConvertedPanel p, Vector3 position, Quaternion rotation, float scale)
        { p.HostTransform.SetPositionAndRotation(position, rotation); p.HostTransform.localScale = Vector3.one * scale; }
    }
    internal static class SurfaceMaterialise
    {
        internal static void NoteMembership(string name, bool dissolves) { }
        internal static void FinishNow(ConvertedPanel? panel, string reason) { }
        internal static bool HandOffRelease(ConvertedPanel panel, Action callback) => false;
        internal static bool VanishAtRelease(ConvertedPanel panel, string name, Action callback) => false;
    }
    internal static class PanelMipBake
    {
        internal static void Rescan(InitiativeTrack? track, string name) { if (track != null) track.RescannedMips++; }
        internal static void Restore(InitiativeTrack? track) { if (track != null) track.RestoredMips++; }
    }
}
namespace GloomhavenVR.WorldUI.Surfaces
{
    internal sealed class InventoryBoundary
    {
        internal bool IsDirty;
        internal Transform? Root;
        internal int Revision;
    }
    internal sealed class SlideBoundary
    {
        internal bool Active;
        internal InitiativeTrack? Last;
        internal void Tick(InitiativeTrack? track) => Last = track;
        internal void Abort(string reason) { Active = false; Last = null; }
    }
    internal sealed class PinBoundary
    {
        internal InitiativeTrack? Last;
        internal void Tick(InitiativeTrack? track, bool active) => Last = track;
        internal void Release(string reason) => Last = null;
    }
    internal sealed partial class InitiativeTrackSurface
    {
        internal ConvertedPanel? Current => Panel;
        internal InitiativeTrack? Animated => _slide.Last;
        private InitiativeTrack? _convertedTrack;
        private readonly SlideBoundary _slide = new();
        private readonly PinBoundary _pin = new();
        private bool _reorderActive;
        private InventoryBoundary? _depthInventory;
        private int _depthRevision = -1, _fitSignature = -1, _fitArmGeneration = -1;
        private float _depthCap = 10, _nextDepthEval, _nextMipRescan, _fitArmedUntil;
        private Canvas? _depthPickHost;
        private const float MipRescanInterval = 1, FitSettleSeconds = 2;
        private static readonly Vector3[] PickCorners = new Vector3[4];
        private void UpdateFitHold() { if (Panel != null) Panel.FitEnabled = false; }
        private void NormalizeDepth() { }
        private void RestoreDepth() { }
        private void FlattenEnemyInfo() { }
        private void RestoreEnemyInfoFlatten() { }
        private void LogTrackTextureDiag() { }
    }
}
