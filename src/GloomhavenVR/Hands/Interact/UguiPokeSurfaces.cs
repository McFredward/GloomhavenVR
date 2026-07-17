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

    // Per-canvas tuning; canvases without an entry use PokeSurfaceTuning.Default.
    private static readonly Dictionary<Canvas, PokeSurfaceTuning> Tunings = new(8);

    // Nested canvases extending a HOST's hit-testing (test #20): uGUI Graphics under
    // an ENABLED nested canvas register with THAT canvas (GraphicRegistry), so the
    // host's GraphicRaycaster cannot see them — UguiPointer.TryRaycast queries these
    // alongside the host. Only hosts live in Surfaces: the laser/poke plane math
    // (RayUguiDriver, PokeInteractor) keeps intersecting the HOST rect alone.
    private static readonly Dictionary<Canvas, List<Canvas>> NestedByHost = new(8);

    public static void Register(Canvas canvas) => Register(canvas, null);

    /// <summary>
    /// P5 (MISSION A.10): register with per-canvas press-depth/hover tuning — small
    /// dialogs want a tighter hover halo and shallower press-through than big panels.
    /// Passing null keeps the Phase-2 defaults (behavior identical to Register(canvas)).
    /// </summary>
    public static void Register(Canvas canvas, PokeSurfaceTuning? tuning)
    {
        if (canvas == null)
        {
            VRLog.Warn("Interact", "UguiPokeSurfaces.Register called with null canvas — ignored.");
            return;
        }
        if (!Surfaces.Contains(canvas))
            Surfaces.Add(canvas);
        if (tuning.HasValue)
            Tunings[canvas] = tuning.Value;
        else
            Tunings.Remove(canvas);
    }

    public static void Unregister(Canvas canvas)
    {
        Surfaces.Remove(canvas);
        if (canvas != null)
        {
            Tunings.Remove(canvas);
            NestedByHost.Remove(canvas);
        }
    }

    /// <summary>
    /// Test #20: attach a nested canvas to a registered host so
    /// <c>UguiPointer.TryRaycast</c> merges its GraphicRaycaster hits with the
    /// host's. The nested canvas is NOT added to <see cref="Surfaces"/> — beam
    /// clamp and poke plane stay on the host rect.
    /// </summary>
    public static void RegisterNested(Canvas host, Canvas nested)
    {
        if (host == null || nested == null)
        {
            VRLog.Warn("Interact", "UguiPokeSurfaces.RegisterNested called with null canvas — ignored.");
            return;
        }
        if (!NestedByHost.TryGetValue(host, out List<Canvas>? list))
            NestedByHost[host] = list = new List<Canvas>(2);
        if (!list.Contains(nested))
            list.Add(nested);
    }

    /// <summary>Nested canvases registered for a host (null when none; do not mutate).</summary>
    internal static List<Canvas>? NestedOf(Canvas? host) =>
        host != null && NestedByHost.TryGetValue(host, out List<Canvas>? list) ? list : null;

    /// <summary>Effective tuning for a registered canvas (Default when none was supplied).</summary>
    internal static PokeSurfaceTuning TuningFor(Canvas canvas) =>
        Tunings.TryGetValue(canvas, out PokeSurfaceTuning t) ? t : PokeSurfaceTuning.Default;

    internal static void Prune()
    {
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (Surfaces[i] == null)
                Surfaces.RemoveAt(i);
        }
        // Drop tunings/nested lists whose canvases died (rare; allocation acceptable here).
        PruneDeadKeys(Tunings);
        PruneDeadKeys(NestedByHost);
    }

    private static void PruneDeadKeys<TValue>(Dictionary<Canvas, TValue> map)
    {
        if (map.Count == 0)
            return;
        List<Canvas>? dead = null;
        foreach (KeyValuePair<Canvas, TValue> pair in map)
        {
            if (pair.Key == null)
                (dead ??= new List<Canvas>()).Add(pair.Key!); // Unity-null: reference still hashes
        }
        if (dead != null)
        {
            for (int i = 0; i < dead.Count; i++)
                map.Remove(dead[i]);
        }
    }

    internal static void Clear()
    {
        Surfaces.Clear();
        Tunings.Clear();
        NestedByHost.Clear();
    }
}

/// <summary>
/// Per-canvas poke feel (P5, MISSION A.10). All distances are meters at diorama
/// scale 1 (the interactor multiplies the hand's world scale). The defaults are the
/// exact Phase-2 constants, so untuned canvases behave identically.
/// </summary>
internal readonly struct PokeSurfaceTuning
{
    /// <summary>Fingertip-to-plane distance below which hover events run (default 0.06).</summary>
    public readonly float HoverRange;

    /// <summary>How far the fingertip must retract behind the plane to release a press (default 0.012).</summary>
    public readonly float ReleaseDepth;

    /// <summary>How far the fingertip may sink THROUGH the plane before the press cancels (default 0.05).</summary>
    public readonly float PressThrough;

    public PokeSurfaceTuning(float hoverRange, float releaseDepth, float pressThrough)
    {
        HoverRange = hoverRange;
        ReleaseDepth = releaseDepth;
        PressThrough = pressThrough;
    }

    /// <summary>The Phase-2 behavior (0.06 / 0.012 / 0.05 m).</summary>
    public static PokeSurfaceTuning Default { get; } = new(0.06f, 0.012f, 0.05f);

    /// <summary>Preset for small close-range dialogs: tighter halo, shallower travel.</summary>
    public static PokeSurfaceTuning SmallDialog { get; } = new(0.04f, 0.008f, 0.03f);

    /// <summary>Preset for large table panels: generous halo and press-through.</summary>
    public static PokeSurfaceTuning LargePanel { get; } = new(0.08f, 0.015f, 0.07f);
}
