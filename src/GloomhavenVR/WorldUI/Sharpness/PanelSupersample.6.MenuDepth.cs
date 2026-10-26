using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    private static long _menuDepthVramTotal;

    // Ordinary windows keep their exact mono capture. Deliberately spatial content
    // opts its own containing window in; text and controls at z=0 project to identical
    // pixels in both eye textures. The native UI hierarchy keeps every stencil/mask.
    private static void CaptureMenuDepth(Camera head)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (i >= Entries.Count) continue;
            Entry e = Entries[i];
            if (e.MenuDepthFrame == Time.frameCount) continue;
            e.MenuDepthFrame = Time.frameCount;
            if (e.Panel.HostRect == null || e.Cam == null || e.Rt == null || e.DisplayImage == null
                || !e.DisplayImage.enabled || !MenuDepthSources.HasContent(e.Panel.HostRect))
            { ReleaseMenuDepth(e); continue; }
            try
            {
                if (!MenuDepthDisplay.TryGetEyePositions(head, out Vector3 left, out Vector3 right))
                { ReleaseMenuDepth(e); continue; }
                if (e.MenuDepthDisplay != null && !e.MenuDepthDisplay.Matches(e.Rt, DisplayTexture(e)))
                    ReleaseMenuDepth(e);
                if (e.MenuDepthDisplay == null)
                {
                    long extra = VramBytesFor(e.RtW, e.RtH, e.Msaa);
                    if (e.VramBytes + extra > MaxPanelVramBytes || _vramTotal + _menuDepthVramTotal + extra > MaxTotalVramBytes)
                    {
                        // A source can appear after the ordinary mono engage. Reuse
                        // the existing symmetric rate/MSAA step-down for BOTH eyes.
                        Reallocate(e, e.Frame, ReallocTrigger.EyeGrid);
                        extra = VramBytesFor(e.RtW, e.RtH, e.Msaa);
                    }
                    if (e.VramBytes + extra > MaxPanelVramBytes || _vramTotal + _menuDepthVramTotal + extra > MaxTotalVramBytes)
                        throw new InvalidOperationException("The symmetric menu-depth eye pair exceeds the existing window memory budget.");
                    e.MenuDepthDisplay = new MenuDepthDisplay(e.DisplayImage, e.Rt, DisplayTexture(e));
                    e.MenuDepthBytes = extra;
                    _menuDepthVramTotal += extra;
                }
                SyncDisplayPose(e);
                e.MenuDepthDisplay.Render(e.Cam, e.Panel.HostRect, e.Frame, e.Rt, DisplayTexture(e),
                    new MenuDepthEye(false, left, head), new MenuDepthEye(true, right, head));
                NoteCaptureCompleted(e);
            }
            catch (Exception error)
            {
                ReleaseMenuDepth(e);
                int count = ++e.MenuDepthFailures;
                if (count <= 2 || (count & (count - 1)) == 0)
                    VRLog.Warn(Scope, $"MENU DEPTH WINDOW FALLBACK: window='{e.Window}', error={error.GetType().Name}: {error.Message}; count={count}; original input and continuation remain native.");
                // The left pair may already have been borrowed before a right-eye
                // failure. Refill it from the restored original mono camera now.
                if (e.Cam != null && e.Rt != null && e.Rt.IsCreated() && Entries.Contains(e))
                {
                    try { SyncProjection(e); e.Cam.Render(); }
                    catch (Exception fallback)
                    {
                        if (count <= 2) VRLog.Warn(Scope, "MENU DEPTH MONO RESTORE: " + fallback.GetType().Name);
                    }
                }
            }
        }
    }

    private static void ReleaseMenuDepth(Entry e)
    {
        if (e.MenuDepthDisplay == null) return;
        e.MenuDepthDisplay.Dispose();
        e.MenuDepthDisplay = null;
        _menuDepthVramTotal = Math.Max(0, _menuDepthVramTotal - e.MenuDepthBytes);
        e.MenuDepthBytes = 0;
    }
}
