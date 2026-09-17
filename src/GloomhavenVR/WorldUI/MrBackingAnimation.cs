using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class MrBacking
{
    private sealed class MrBackingAnimationState
    {
        internal bool Active, Closed;
        internal float Progress;
        internal Rect Bounds, Frame, CaptureFrame;
        internal bool CaptureKnown, Captured, PendingGeometry;
        internal readonly List<Rect> Pieces = new();
        internal readonly Dictionary<CanvasRenderer, float> OriginalAlpha = new();
    }

    // Build 522: the window's elements finish before its dust. Keeping an opaque MR quad
    // until host destruction left an empty wall over newly opened windows. Snapshot geometry
    // BEFORE the first element-alpha write, then consume that runner's exact field progress.
    // There is no backing timer and this decoration never owns a native close callback.
    internal static void BeginWindowMaterialise(ConvertedPanel panel,
        IList<CanvasRenderer>? renderers = null, IList<float>? originalAlpha = null)
    {
        if (panel.MrBackingSuppressed || panel.HostRect == null)
            return;
        try
        {
            bool wanted = MixedReality.BackingsWanted;
            if (wanted)
            {
                EnsurePlateMaterial();
                _applied = true;
            }
            PanelEntry entry = GetAnimationEntry(panel);
            entry.Materialise?.Restore(entry.Plate);
            entry.Faded = false;
            if (entry.Plate != null)
                entry.Plate.GetComponent<Renderer>().sharedMaterial = _plateMat;
            entry.Animation.Active = true;
            entry.Animation.Closed = false;
            entry.Animation.Frame = panel.HostRect.rect;
            entry.Animation.Bounds = default;
            entry.Animation.Pieces.Clear();
            entry.Animation.OriginalAlpha.Clear();
            entry.Animation.CaptureKnown = false;
            entry.Animation.PendingGeometry = false;
            if (renderers != null && originalAlpha != null)
                for (int i = 0; i < renderers.Count && i < originalAlpha.Count; i++)
                    if (renderers[i] != null) entry.Animation.OriginalAlpha[renderers[i]] = originalAlpha[i];
            entry.Visibility.Root = panel.FitContentRoot ?? panel.Target;
            // Keep per-graphic pre-effect geometry while MR is off too: a single cached union
            // cannot later distinguish a wholly cropped outlier from legitimate visible overflow.
            // The reveal can start before Tick has ever built a plate. Measure the same native
            // ink now, while it is still whole; never substitute the transparent host rectangle.
            CaptureAnimationBounds(entry);
        }
        catch (Exception ex)
        {
            FailBackingAnimation(panel, ex);
        }
    }

    internal static void ApplyWindowMaterialise(ConvertedPanel panel, float elementProgress)
    {
        PanelEntry? entry = FindAnimationEntry(panel);
        if (entry == null || !entry.Animation.Active)
            return;
        entry.Animation.Progress = elementProgress;
        try
        {
            DrawWindowAnimation(entry, MixedReality.BackingsWanted && panel.HostGo != null
                && panel.HostGo.activeInHierarchy && !panel.RenderHidden && !panel.OwnerRenderHidden);
        }
        catch (Exception ex)
        {
            FailBackingAnimation(panel, ex);
        }
    }

    internal static void EndWindowMaterialise(ConvertedPanel panel, bool vanishing)
    {
        PanelEntry? entry = FindAnimationEntry(panel);
        if (entry == null)
            return;
        // A close remains closed even if Unity delays destruction until the end of the frame.
        // Reopening has to explicitly Begin again, rather than resurrecting the old backing.
        entry.Animation.Active = false;
        entry.Animation.Closed = vanishing;
        try
        {
            entry.Materialise?.Restore(entry.Plate);
            entry.Faded = false;
            if (entry.Plate != null)
            {
                entry.Plate.GetComponent<Renderer>().sharedMaterial = _plateMat;
                if (vanishing || !MixedReality.BackingsWanted)
                    entry.Plate.gameObject.SetActive(false);
            }
            if (vanishing)
            {
                entry.Materialise?.Dispose();
                entry.Materialise = null;
                if (entry.FadeMat != null)
                    UnityEngine.Object.Destroy(entry.FadeMat);
                entry.FadeMat = null;
            }
            entry.Animation.Pieces.Clear();
            entry.Animation.OriginalAlpha.Clear();
            if (!vanishing)
                entry.Layout.Settle(entry.Animation.Bounds, Time.unscaledTime);
            if (!MixedReality.BackingsWanted)
            {
                entry.Materialise?.Dispose();
                DestroyPlate(entry.Plate, entry.FadeMat);
                Panels.Remove(entry);
            }
        }
        catch (Exception ex)
        {
            FailBackingAnimation(panel, ex);
        }
    }

    private static void DrawWindowAnimation(PanelEntry entry, bool visible)
    {
        // uGUI may not have built its first mesh when the native reveal starts. Wait for that
        // real geometry, then join the current field; do not invent a layout-sized rectangle.
        if (visible && entry.Animation.Progress < 1f
            && (entry.Animation.Pieces.Count == 0 || entry.Animation.PendingGeometry))
            CaptureAnimationBounds(entry);
        RefreshAnimationCapture(entry);
        Rect rect = entry.Animation.Bounds;
        visible &= MrBackingLayout.ReadyForSample(true, rect) && entry.Animation.Progress < 1f;
        RectTransform? host = entry.Panel.HostRect;
        if (host == null)
            return;
        if (visible)
        {
            // MR may have been enabled after Begin and before its regular Update driver.
            EnsurePlateMaterial();
            _applied = true;
        }
        if (entry.Plate == null && visible)
        {
            entry.Plate = CreatePlate(host);
            CanvasConversion.RegisterOrderFollower(entry.Panel, entry.Plate.GetComponent<MeshRenderer>(), 0);
        }
        if (entry.Plate == null)
            return;
        if (entry.Plate.gameObject.activeSelf != visible)
            entry.Plate.gameObject.SetActive(visible);
        if (!visible)
            return;
        Fit(entry.Plate, host, rect.size, rect.center);
        entry.Shown = rect;
        entry.Materialise ??= new MrBackingMaterialise();
        entry.FadeMat ??= CreateFadeMaterial();
        entry.FadeMat.color = _plateColor;
        entry.Materialise.Apply(entry.Plate, rect, entry.Animation.Frame,
            entry.Animation.Progress, entry.FadeMat);
    }

    private static void CaptureAnimationBounds(PanelEntry entry)
    {
        ConvertedPanel panel = entry.Panel;
        if (panel.HostRect == null)
            return;
        bool measured = PanelInkBounds.TryMeasure(panel, out PanelInkBounds.Ink ink,
            contentRoot: panel.FitContentRoot, visibleWitnesses: entry.Visibility.Witnesses,
            backingGeometry: true, backingPieces: entry.Animation.Pieces,
            backingOriginalAlpha: entry.Animation.OriginalAlpha);
        entry.Animation.PendingGeometry = ink.PendingPaint > 0;
        entry.Animation.CaptureKnown = false;
        if (measured && ink.Valid)
        {
            entry.Animation.Bounds = MrBackingLayout.WindowRect(panel.HostRect.rect,
                ink.Rect, ink.Plates > 0, ink.PlateBottom, fitScoped: true);
            LogPaintedExtent(entry, panel.HostRect, panel.HostRect.rect, entry.Animation.Bounds);
        }
    }

    private static void RefreshAnimationCapture(PanelEntry entry)
    {
        MrBackingAnimationState state = entry.Animation;
        bool captured = PanelSupersample.TryGetBackingCaptureRect(entry.Panel, out Rect frame);
        if (state.CaptureKnown && state.Captured == captured && (!captured
            || (Mathf.Abs(state.CaptureFrame.xMin - frame.xMin) <= 0.001f
                && Mathf.Abs(state.CaptureFrame.yMin - frame.yMin) <= 0.001f
                && Mathf.Abs(state.CaptureFrame.width - frame.width) <= 0.001f
                && Mathf.Abs(state.CaptureFrame.height - frame.height) <= 0.001f))) return;
        state.CaptureKnown = true;
        state.Captured = captured;
        state.CaptureFrame = frame;
        state.Bounds = MrBackingCaptureBounds.Union(state.Pieces, captured, frame, out Rect ink)
            ? MrBackingLayout.WindowRect(state.Frame, ink, false, ink.yMin, fitScoped: true) : default;
    }

    private static PanelEntry? FindAnimationEntry(ConvertedPanel panel)
    {
        for (int i = 0; i < Panels.Count; i++)
            if (ReferenceEquals(Panels[i].Panel, panel))
                return Panels[i];
        return null;
    }

    private static PanelEntry GetAnimationEntry(ConvertedPanel panel)
    {
        PanelEntry? entry = FindAnimationEntry(panel);
        if (entry != null)
            return entry;
        entry = new PanelEntry { Panel = panel };
        Panels.Add(entry);
        return entry;
    }

    private static void FailBackingAnimation(ConvertedPanel panel, Exception ex)
    {
        // This also runs from Finish after its exactly-once latch has been set. Neither a
        // destroyed decoration nor a failing diagnostic may stop the native close callback.
        try
        {
            PanelEntry? entry = FindAnimationEntry(panel);
            if (entry != null)
            {
                entry.Animation.Active = false;
                entry.Animation.Closed = true;
                if (entry.Plate != null)
                    entry.Plate.gameObject.SetActive(false);
            }
        }
        catch { /* Best-effort cleanup of an already failed, mod-owned decoration. */ }
        try
        {
            VRLog.Warn("WorldUI", "MR BACKING ANIMATION: decoration disabled after "
                + ex.GetType().Name + "; native window continuation remains independent.");
        }
        catch { /* A diagnostic must never prevent the game's pending continuation. */ }
    }
}
