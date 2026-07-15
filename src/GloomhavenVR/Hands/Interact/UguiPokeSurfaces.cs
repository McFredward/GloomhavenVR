using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Registry of uGUI canvases the fingertip may poke (FROZEN Phase-2 API).
///
/// Phase-3c registers its converted world-space canvases here; the
/// <see cref="PokeInteractor"/> then synthesizes real pointer events when a fingertip
/// crosses the canvas plane. Canvases are NOT auto-discovered: physical poke on the
/// game's screen-space canvases makes no spatial sense (they live on the UICamera's
/// image plane, not on the table) — those remain the virtual mouse's job
/// (<c>GloomhavenVR.WorldUI.VirtualMouse</c>).
///
/// Requirements for registered canvases:
/// - RenderMode.WorldSpace and a meaningful world size/pose.
/// - <c>Canvas.worldCamera</c> set (used to compute the synthetic screen point for
///   GraphicRaycaster; the VR head camera is the right choice).
/// - A GraphicRaycaster component — the poke respects its enabled state, which is
///   exactly how the game locks UI modally (UIManager.ToggleLockUI, UI-ARCH §4.3).
/// </summary>
internal static class UguiPokeSurfaces
{
    // Iterated by PokeInteractor every frame (for-loop, no allocation).
    internal static readonly List<Canvas> Surfaces = new(8);

    public static void Register(Canvas canvas)
    {
        if (canvas == null)
        {
            VRLog.Warn("Interact", "UguiPokeSurfaces.Register called with null canvas — ignored.");
            return;
        }
        if (!Surfaces.Contains(canvas))
            Surfaces.Add(canvas);
    }

    public static void Unregister(Canvas canvas) => Surfaces.Remove(canvas);

    internal static void Prune()
    {
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (Surfaces[i] == null)
                Surfaces.RemoveAt(i);
        }
    }

    internal static void Clear() => Surfaces.Clear();
}
