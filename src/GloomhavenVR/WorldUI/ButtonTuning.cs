using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Live-tunable 3D-button geometry (user requests #8/#9, canonical <see cref="ModuleConfig.Create"/>
/// pattern — <c>dev.gloomhavenvr.buttons.cfg</c>). Two groups:
///
/// TRANSIENT round-phase buttons (user #8 — the ButtonCluster column that shows the game's
/// turn-flow buttons like "Bewegung überspringen"): position offset in the tray-root plane,
/// cap shape (round puck / square keycap) and cap size — the group the user pressed by
/// accident because it sat too close to the card slots. The DEFAULT anchor itself also moved
/// down-board (see <see cref="ButtonCluster"/> column constants) so the group clears the slot
/// row out of the box; these offsets shift it further from there.
///
/// SQUARE keycaps (user #9 — Confirm/Undo/gear/follow on the control board and the cluster's
/// caps when the transient shape is Square): independent cap WIDTH and HEIGHT (rectangular
/// caps), cap DEPTH (the extrusion toward the player) and press TRAVEL (how far the cap sinks
/// — also the distance the fingertip must push for the depth-fire press, user #6). Width /
/// height / depth / travel use 0 = "authored default" so the shipped look is bit-identical
/// until a value is set.
///
/// Consumers re-read the clamped accessors and rebuild on <see cref="Version"/> change
/// (PlayTray.TickStatus / ButtonCluster.Tick), so every entry is live — no restart. The
/// settings panel binds steppers against the public entries + <see cref="Changed"/> in a
/// later phase. All values are LOCAL visuals — nothing here syncs to multiplayer.
/// </summary>
internal static class ButtonTuning
{
    private static ConfigFile? _file;

    // ---- transient round-phase button group (user #8) ------------------------------------
    internal static ConfigEntry<float>? TransientOffsetX;
    internal static ConfigEntry<float>? TransientOffsetY;
    internal static ConfigEntry<Cards.ButtonShape>? TransientShape;
    internal static ConfigEntry<float>? TransientCapSize;

    // ---- square keycap geometry (user #9) ------------------------------------------------
    internal static ConfigEntry<float>? SquareCapWidth;
    internal static ConfigEntry<float>? SquareCapHeight;
    internal static ConfigEntry<float>? SquareCapDepth;
    internal static ConfigEntry<float>? PressTravel;

    /// <summary>Raised on every entry write (settings-panel steppers bind here later).</summary>
    internal static event System.Action? Changed;

    /// <summary>Monotonic change counter — pull-based consumers rebuild when it moves.</summary>
    internal static int Version { get; private set; }

    // ---- press-travel firing (user #6, depth-fire — constants, not config) -----------------
    /// <summary>Fraction of the full cap travel the fingertip must reach before the press fires.</summary>
    internal const float PressFireFraction = 0.90f;
    /// <summary>The cap must rise back above (1 − this) … i.e. depth must fall to ≤ this fraction before a new press can fire.</summary>
    internal const float PressRearmFraction = 0.50f;

    // ---- dissolve timing (user #7 — constants, not config) ---------------------------------
    /// <summary>Seconds the cap shrinks out while the dust burst plays (logical hide is instant).</summary>
    internal const float DissolveSeconds = 0.16f;
    /// <summary>Seconds of the quick scale-in when a transient button appears.</summary>
    internal const float AppearSeconds = 0.15f;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("buttons");

        TransientOffsetX = config.Bind("TransientButtons", "OffsetX", 0f,
            "Sideways offset (tray-ROOT-local meters, +X = toward the board's right edge / the " +
            "Undo-gear pads) of the transient round-phase button group (skip-step etc.) from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        TransientOffsetY = config.Bind("TransientButtons", "OffsetY", 0f,
            "Up-board offset (tray-ROOT-local meters, +Y = toward the card slots / far edge, " +
            "-Y = toward the bottom edge and handle) of the transient button group from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        TransientShape = config.Bind("TransientButtons", "Shape", Cards.ButtonShape.Round,
            "Cap shape of the transient round-phase buttons: Round = flattened puck (default), " +
            "Square = boxy keycap (then SquareCaps Width/Height/Depth apply to them too). Live.");
        TransientCapSize = config.Bind("TransientButtons", "CapSize", 0.042f,
            "Cap radius (cluster-local meters) of the transient buttons. The column auto-fit only " +
            "SHRINKS below this when several buttons must share the column; a single button uses " +
            "exactly this size. Live; clamped 0.015..0.09.");

        SquareCapWidth = config.Bind("SquareCaps", "Width", 0f,
            "Cap width (meters, along the board's X) of the SQUARE keycaps — Confirm/Undo/gear/" +
            "follow on the board, and the cluster caps when TransientButtons.Shape=Square. " +
            "0 = each button's authored width (per-board Confirm/Undo size; gear 0.062; follow " +
            "0.068). Set Width and Height independently for rectangular caps. Live; clamped " +
            "0.02..0.20 when set.");
        SquareCapHeight = config.Bind("SquareCaps", "Height", 0f,
            "Cap height (meters, along the board's Y) of the SQUARE keycaps. 0 = each button's " +
            "authored height. Live; clamped 0.015..0.20 when set.");
        SquareCapDepth = config.Bind("SquareCaps", "Depth", 0f,
            "Cap depth/extrusion (meters toward the player) of the SQUARE keycaps. 0 = authored " +
            "(Confirm/Undo 0.036; gear/follow 0.030; square cluster caps 0.012). Live; clamped " +
            "0.006..0.08 when set.");
        PressTravel = config.Bind("SquareCaps", "Travel", 0f,
            "Press travel (meters) — how far a cap sinks under the fingertip, and therefore how " +
            "far you must push it in before the press fires (depth-fire at 90% of travel). " +
            "Applies to ALL 3D board/cluster caps. 0 = authored (board buttons 0.004; cluster " +
            "caps 0.008). Live; clamped 0.002..0.02 when set.");

        Hook(TransientOffsetX);
        Hook(TransientOffsetY);
        Hook(TransientShape);
        Hook(TransientCapSize);
        Hook(SquareCapWidth);
        Hook(SquareCapHeight);
        Hook(SquareCapDepth);
        Hook(PressTravel);
    }

    private static void Hook<T>(ConfigEntry<T> entry) =>
        entry.SettingChanged += (_, _) =>
        {
            Version++;
            Changed?.Invoke();
        };

    // ---- clamped live accessors (safe before Bind — fall back to shipped defaults) ---------

    /// <summary>Tray-root-plane offset of the transient button group (user #8).</summary>
    internal static Vector2 TransientOffset => new(
        Clamped(TransientOffsetX, 0f, -0.30f, 0.30f),
        Clamped(TransientOffsetY, 0f, -0.30f, 0.30f));

    /// <summary>Whether the transient cluster caps are round pucks (default) or square keycaps.</summary>
    internal static bool TransientRound =>
        TransientShape == null || TransientShape.Value == Cards.ButtonShape.Round;

    /// <summary>Configured transient cap radius, cluster-local meters (auto-fit ceiling).</summary>
    internal static float TransientCapRadius => Clamped(TransientCapSize, 0.042f, 0.015f, 0.09f);

    /// <summary>Square-cap width, or <paramref name="authored"/> while the entry is 0/auto.</summary>
    internal static float WidthOr(float authored) => Override(SquareCapWidth, authored, 0.02f, 0.20f);

    /// <summary>Square-cap height, or <paramref name="authored"/> while the entry is 0/auto.</summary>
    internal static float HeightOr(float authored) => Override(SquareCapHeight, authored, 0.015f, 0.20f);

    /// <summary>Square-cap depth/extrusion, or <paramref name="authored"/> while the entry is 0/auto.</summary>
    internal static float DepthOr(float authored) => Override(SquareCapDepth, authored, 0.006f, 0.08f);

    /// <summary>Press travel, or <paramref name="authored"/> while the entry is 0/auto.</summary>
    internal static float TravelOr(float authored) => Override(PressTravel, authored, 0.002f, 0.02f);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);

    private static float Override(ConfigEntry<float>? entry, float authored, float min, float max) =>
        entry == null || entry.Value <= 0f ? authored : Mathf.Clamp(entry.Value, min, max);

    /// <summary>One-line value dump for the "geometry config applied" log (0 = authored default).</summary>
    internal static string Describe()
    {
        Vector2 off = TransientOffset;
        return $"transient offset ({off.x:F3}, {off.y:F3}) m, shape {(TransientRound ? "Round" : "Square")}, " +
               $"cap size {TransientCapRadius:F3} m; square W/H/D " +
               $"{Clamped(SquareCapWidth, 0f, 0f, 0.20f):F3}/{Clamped(SquareCapHeight, 0f, 0f, 0.20f):F3}/" +
               $"{Clamped(SquareCapDepth, 0f, 0f, 0.08f):F3} m, travel {Clamped(PressTravel, 0f, 0f, 0.02f):F3} m " +
               "(0 = authored)";
    }
}

/// <summary>
/// Dust-dissolve burst for vanishing buttons (user #7): a couple dozen tiny face-colored
/// quads that drift sideways and fade over ~0.3 s while the cap itself shrinks out. ONE
/// pooled world-space <see cref="ParticleSystem"/> serves every button on every tray/cluster
/// (Emit with per-particle position/color — no per-button systems, no per-frame allocations;
/// the only allocations happen once at pool build). Purely local visuals (never synced), and
/// it never delays the logical hide — callers disable input/colliders before playing this.
/// Lazily rebuilt if a scene unload destroyed the pool object.
/// </summary>
internal static class ButtonDissolveFx
{
    private const int BurstCount = 22;

    private static ParticleSystem? _ps;

    /// <summary>
    /// Emit one dust burst. <paramref name="center"/> = cap center (world), <paramref name="outNormal"/> =
    /// the cap's outward face normal (world), <paramref name="worldSize"/> = cap footprint diameter in
    /// world meters (scales spread/speed/particle size), <paramref name="color"/> = the button's face color.
    /// </summary>
    internal static void Play(Vector3 center, Vector3 outNormal, float worldSize, Color color)
    {
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — dissolve degrades to the cap shrink alone
        color.a = 1f;
        worldSize = Mathf.Clamp(worldSize, 0.01f, 0.5f);
        // A common sideways sweep direction so the powder reads as "swept away", plus
        // per-particle jitter. Any tangent of the face normal works.
        Vector3 sweep = Vector3.Cross(outNormal, Vector3.up);
        if (sweep.sqrMagnitude < 1e-4f)
            sweep = Vector3.Cross(outNormal, Vector3.right);
        sweep.Normalize();
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < BurstCount; i++)
        {
            Vector3 jitter = Random.insideUnitSphere;
            Vector3 inPlane = Vector3.ProjectOnPlane(jitter, outNormal);
            ep.position = center + inPlane * (worldSize * 0.45f) + outNormal * (worldSize * 0.05f * Random.value);
            Vector3 drift = inPlane.sqrMagnitude > 1e-6f ? inPlane.normalized : sweep;
            ep.velocity = (sweep * (0.8f + 0.6f * Random.value)
                           + drift * (0.5f * Random.value)
                           + outNormal * 0.25f) * worldSize;
            ep.startLifetime = 0.22f + 0.16f * Random.value;
            ep.startSize = worldSize * (0.045f + 0.05f * Random.value);
            ep.startColor = color;
            _ps.Emit(ep, 1);
        }
    }

    private static void BuildPool()
    {
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;
        var go = new GameObject("GloomhavenVR.ButtonDustFx");
        _ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = _ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true; // keeps the system simulating; idle cost ~zero with no live particles
        main.maxParticles = 256;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = _ps.emission;
        emission.enabled = false; // burst-only via Emit
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        ParticleSystem.SizeOverLifetimeModule size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = new Material(shader);
        renderer.sortingOrder = 2; // over the cap face sprites (≤1), under panel canvases (10)
        Core.VRLayers.Apply(go);
        _ps.Play(); // armed — particles only exist after Emit
    }
}
