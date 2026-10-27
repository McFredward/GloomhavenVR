using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    private static long _menuDepthVramTotal;
    private static readonly Dictionary<Entry, long> HeadReservations = new();
    private static long MenuDepthReservation(Entry entry) => HeadReservations.TryGetValue(entry, out long bytes) ? bytes : 0;
    private static readonly Dictionary<Entry, MenuDepthCaptureFrame> MenuDepthFrames = new();

    private sealed class MenuDepthCaptureFrame
    {
        internal readonly Rect Original;
        internal Rect Frame;
        private float _shrinkSince = -1f;
        internal MenuDepthCaptureFrame(Rect original) { Original = Frame = original; }
        internal bool Update(Rect wanted)
        {
            Vector2 size = Vector2.Max(Frame.size, wanted.size);
            if (wanted.width < size.x * .75f || wanted.height < size.y * .75f)
            {
                if (_shrinkSince < 0f) _shrinkSince = Time.unscaledTime;
                if (Time.unscaledTime - _shrinkSince >= 1.5f) { size = wanted.size; _shrinkSince = -1f; }
            }
            else _shrinkSince = -1f;
            bool resize = size != Frame.size;
            Frame = new Rect(wanted.center - size * .5f, size);
            return resize;
        }
    }

    // The native frame remains the layout/hit/shared-geometry authority. Only
    // its owned capture allocation and transparent photograph include overspill.
    private static Rect MenuDepthAllocationFrame(Entry entry, Rect nativeFrame)
        => MenuDepthFrames.TryGetValue(entry, out MenuDepthCaptureFrame state) && state.Original == nativeFrame
            ? state.Frame : nativeFrame;

    private static Rect PrepareMenuDepthFrame(Entry entry, MenuDepthEye left, MenuDepthEye right)
    {
        if (!MenuDepthFrames.TryGetValue(entry, out MenuDepthCaptureFrame state) || state.Original != entry.Frame)
            MenuDepthFrames[entry] = state = new MenuDepthCaptureFrame(entry.Frame);
        Rect wanted = MenuDepthDisplay.ForegroundFrame(entry.Panel.HostRect, entry.Frame, left, right);
        if (state.Update(wanted)) Reallocate(entry, entry.Frame, ReallocTrigger.EyeGrid);
        return state.Frame;
    }

    // Ordinary windows keep their exact mono capture. Deliberately spatial content
    // opts its own containing window in; text and controls at z=0 project to identical
    // pixels in both eye textures. The native UI hierarchy keeps every stencil/mask.
    private static void CaptureMenuDepth(Camera head)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (i >= Entries.Count) continue;
            Entry e = Entries[i];
            if (e.MenuDepthFrame == Time.frameCount) { e.MenuDepthDisplay?.PrepareHead(head); continue; }
            e.MenuDepthFrame = Time.frameCount;
            if (e.Panel.HostRect == null || e.Cam == null || e.Rt == null || e.DisplayImage == null
                || !e.DisplayImage.isActiveAndEnabled)
            { ReleaseMenuDepth(e); continue; }
            if (!MenuDepthSources.HasContent(e.Panel.HostRect))
            {
                bool skippedMono = e.MenuDepthDisplay != null && !e.Cam.enabled;
                ReleaseMenuDepth(e);
                if (skippedMono) RestoreMenuDepthMono(e);
                continue;
            }
            try
            {
                if (!MenuDepthDisplay.TryGetEyePositions(head, out Vector3 left, out Vector3 right))
                {
                    bool skippedMono = e.MenuDepthDisplay != null && !e.Cam.enabled;
                    ReleaseMenuDepth(e);
                    if (skippedMono) RestoreMenuDepthMono(e);
                    continue;
                }
                var leftEye = new MenuDepthEye(false, left, head);
                var rightEye = new MenuDepthEye(true, right, head);
                // No eye-dependent expansion of the native photograph. A complete
                // original UI draw now has its actual head-pixel raster instead.
                long headBytes = MenuDepthDisplay.HeadCost(head, e.Rt);
                if (e.MenuDepthDisplay != null && !e.MenuDepthDisplay.Matches(e.Rt, DisplayTexture(e)))
                    ReleaseMenuDepth(e, preserveFrame: true);
                if (!HeadReservations.ContainsKey(e) && HeadReservations.Count >= MenuDepthHeadRaster.WindowSlots)
                    throw new InvalidOperationException("The fixed head-raster window slots are fully reserved.");
                // During an actual XR/format change old admitted pairs may still
                // be retiring. Charge their real bytes until release, using the
                // largest live pair for this finite-slot ceiling. Head movement
                // neither changes the ceiling nor asks mono ResolveRate to shrink.
                long largest = headBytes;
                foreach (long held in HeadReservations.Values) largest = Math.Max(largest, held);
                long budget = MenuDepthHeadRaster.Budget(largest, MenuDepthHeadRaster.WindowSlots, 448L * 1024 * 1024);
                if (headBytes > budget - _menuDepthVramTotal + e.MenuDepthBytes)
                    throw new InvalidOperationException("The fixed head-raster window memory budget is fully reserved.");
                HeadReservations[e] = headBytes;
                if (e.MenuDepthDisplay == null)
                    e.MenuDepthDisplay = new MenuDepthDisplay(e.DisplayImage, e.Rt, DisplayTexture(e), headRaster: true);
                SyncDisplayPose(e);
                e.MenuDepthDisplay.RenderHead(e.Cam, e.Panel.HostRect, e.Frame, e.Rt, leftEye, rightEye);
                _menuDepthVramTotal += e.MenuDepthDisplay.HeadBytes - e.MenuDepthBytes;
                e.MenuDepthBytes = e.MenuDepthDisplay.HeadBytes;
                NoteCaptureCompleted(e);
            }
            catch (Exception error)
            {
                ReleaseMenuDepth(e);
                if (!Entries.Contains(e)) continue; // A native close is ordinary owner loss.
                int count = ++e.MenuDepthFailures;
                if (count <= 2 || (count & (count - 1)) == 0)
                    VRLog.Warn(Scope, $"MENU DEPTH WINDOW FALLBACK: window='{e.Window}', error={error.GetType().Name}: {error.Message}; count={count}; original input and continuation remain native.");
                // The left pair may already have been borrowed before a right-eye
                // failure. Refill it from the restored original mono camera now.
                RestoreMenuDepthMono(e, count);
            }
        }
    }

    private static void RestoreMenuDepthMono(Entry e, int previousFailure = 0)
    {
        if (e.Cam == null || e.Rt == null || !e.Rt.IsCreated() || !Entries.Contains(e)
            || e.DisplayImage == null || !e.DisplayImage.isActiveAndEnabled) return;
        try { SyncProjection(e); Canvas.ForceUpdateCanvases(); e.Cam.Render(); }
        catch (Exception fallback)
        {
            int count = previousFailure != 0 ? previousFailure : ++e.MenuDepthFailures;
            if (count <= 2 || (count & (count - 1)) == 0)
                VRLog.Warn(Scope, "MENU DEPTH MONO RESTORE: " + fallback.GetType().Name + "; count=" + count);
        }
    }

    private static void ReleaseMenuDepth(Entry e, bool preserveFrame = false)
    {
        if (!preserveFrame) { MenuDepthFrames.Remove(e); HeadReservations.Remove(e); }
        if (e.MenuDepthDisplay == null) return;
        e.MenuDepthDisplay.Dispose();
        e.MenuDepthDisplay = null;
        _menuDepthVramTotal = Math.Max(0, _menuDepthVramTotal - e.MenuDepthBytes);
        e.MenuDepthBytes = 0;
    }
}
