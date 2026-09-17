using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class MrBacking
{
    private sealed class MrBackingAnimationState
    {
        internal bool Active, Closed;
        internal float Progress;
        internal Rect Bounds, Frame;
    }

    // Build 522: the window's elements finish before its dust. Keeping an opaque MR quad
    // until host destruction left an empty wall over newly opened windows. Snapshot geometry
    // BEFORE the first element-alpha write, then consume that runner's exact field progress.
    // There is no backing timer and this decoration never owns a native close callback.
    internal static void BeginWindowMaterialise(ConvertedPanel panel)
    {
        if (!MixedReality.BackingsWanted || panel.MrBackingSuppressed || panel.HostRect == null)
            return;
        try
        {
            EnsurePlateMaterial();
            _applied = true;
            PanelEntry entry = GetAnimationEntry(panel);
            entry.Materialise?.Restore(entry.Plate);
            entry.Faded = false;
            if (entry.Plate != null)
                entry.Plate.GetComponent<Renderer>().sharedMaterial = _plateMat;
            entry.Animation.Active = true;
            entry.Animation.Closed = false;
            entry.Animation.Frame = panel.HostRect.rect;
            entry.Animation.Bounds = entry.Shown;
            entry.Visibility.Root = panel.FitContentRoot ?? panel.Target;
            // The reveal can start before Tick has ever built a plate. Measure the same native
            // ink now, while it is still whole; never substitute the transparent host rectangle.
            if (PanelInkBounds.TryMeasure(panel, out PanelInkBounds.Ink ink,
                    contentRoot: panel.FitContentRoot, visibleWitnesses: entry.Visibility.Witnesses)
                && ink.Valid)
                entry.Animation.Bounds = GlyphTrueRect(panel, panel.HostRect,
                    MrBackingLayout.WindowRect(panel.HostRect.rect, ink.Rect, ink.Plates > 0,
                        ink.PlateBottom, fitScoped: panel.FitContentRoot != null), out _,
                    contentRoot: panel.FitContentRoot);
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
            if (!vanishing)
                entry.Layout.Settle(entry.Animation.Bounds, Time.unscaledTime);
        }
        catch (Exception ex)
        {
            FailBackingAnimation(panel, ex);
        }
    }

    private static void DrawWindowAnimation(PanelEntry entry, bool visible)
    {
        Rect rect = entry.Animation.Bounds;
        visible &= MrBackingLayout.ReadyForSample(true, rect) && entry.Animation.Progress < 1f;
        RectTransform? host = entry.Panel.HostRect;
        if (host == null)
            return;
        if (entry.Plate == null && visible)
        {
            entry.Plate = CreatePlate(host);
            CanvasConversion.RegisterOrderFollower(entry.Panel, entry.Plate.GetComponent<MeshRenderer>(), 0);
        }
        if (entry.Plate == null)
            return;
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
        PanelEntry? entry = FindAnimationEntry(panel);
        if (entry != null)
        {
            entry.Animation.Active = false;
            entry.Animation.Closed = true;
            if (entry.Plate != null)
                entry.Plate.gameObject.SetActive(false);
        }
        VRLog.Warn("WorldUI", "MR BACKING ANIMATION: decoration disabled after "
            + ex.GetType().Name + "; native window continuation remains independent.");
    }
}
