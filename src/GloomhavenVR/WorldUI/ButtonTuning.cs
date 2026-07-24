using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Live-tunable 3D-button geometry (user requests #8/#9 + the 2026-07 category split,
/// canonical <see cref="ModuleConfig.Create"/> pattern — <c>dev.gloomhavenvr.buttons.cfg</c>).
///
/// PER-CATEGORY bind sets (user: "every value must apply ONLY to its own category"):
///
/// [RoundButtons] — the TRANSIENT round-phase button group (the ButtonCluster column that
/// shows the game's turn-flow buttons like "Bewegung überspringen"): position offset in the
/// tray-root frame (X sideways, Y up-board, Z out of the board toward the player), cap shape
/// (round puck / square keycap), cap size, and — for its Square shape — its OWN cap
/// width/height/depth and press travel. Consumed by <see cref="ButtonCluster"/> ONLY.
///
/// [BoardButtons] — the Confirm/Undo ("Fortfahren"/"Rückgängig machen") keycaps on the
/// control board: cap width/height/depth and press travel. Consumed by
/// <c>PlayTray.BuildButtons</c> ONLY.
///
/// [BoardDashboard] — the settings gear ("Einstellungen") and the follow/pin toggle
/// ("Fixiert") flat plates on the control board: per-button width (they are authored at
/// different widths), shared height/depth and press travel. Consumed by
/// <c>PlayTray.CreateDashboardButtons</c> ONLY.
///
/// NO "Auto" SENTINEL any more (user: "give me a fixed numeric value everywhere"): every
/// entry's DEFAULT is the exact authored value it replaced, so the shipped defaults
/// reproduce the authored look bit-identically and every settings-panel stepper always
/// shows a real number. Legacy files ([TransientButtons] offsets/shape/cap size and the
/// shared [SquareCaps] 0=Auto geometry that leaked across categories) are migrated once on
/// <see cref="Bind"/>: nonzero values are copied into every category they used to affect
/// (preserving the user's current look), 0-sentinels resolve to the new numeric defaults,
/// and the legacy entries are removed from the cfg (logged).
///
/// Consumers re-read the clamped accessors and rebuild on <see cref="Version"/> change
/// (PlayTray.TickStatus / ButtonCluster.Tick), so every entry is live — no restart. The
/// settings panel binds steppers against the public entries + <see cref="Changed"/>.
/// All values are LOCAL visuals — nothing here syncs to multiplayer.
/// </summary>
internal static class ButtonTuning
{
    private static ConfigFile? _file;

    // ---- authored defaults (the exact values each bind replaced — see the consumers) ------
    internal const float DefaultRoundCapSize = 0.042f;  // ButtonCluster column cap-radius ceiling
    internal const float DefaultRoundWidth = 0.084f;    // square cluster cap = 2 × the 42 mm radius
    internal const float DefaultRoundHeight = 0.084f;
    internal const float DefaultRoundDepth = 0.012f;    // authored square cluster-cap extrusion
    internal const float DefaultRoundTravel = 0.008f;   // authored 8 mm cluster cap travel
    internal const float DefaultBoardWidth = 0.073f;    // authored ConfirmUndoSize default (CardsConfig)
    internal const float DefaultBoardHeight = 0.073f;
    internal const float DefaultBoardDepth = 0.036f;    // PlayTray.SquareCapThickness
    internal const float DefaultBoardTravel = 0.004f;   // BoardButton.CapTravel
    internal const float DefaultGearWidth = 0.062f;     // authored gear plate width
    internal const float DefaultPinWidth = 0.068f;      // authored follow/pin plate width
    internal const float DefaultDashHeight = 0.030f;    // authored gear/pin plate height
    internal const float DefaultDashDepth = 0.030f;     // authored gear/pin plate extrusion
    internal const float DefaultDashTravel = 0.004f;    // BoardButton.CapTravel (same authored travel)

    // ---- [RoundButtons] — transient round-phase button group (ButtonCluster ONLY) ---------
    internal static ConfigEntry<float>? RoundOffsetX;
    internal static ConfigEntry<float>? RoundOffsetY;
    internal static ConfigEntry<float>? RoundOffsetZ;
    internal static ConfigEntry<Cards.ButtonShape>? RoundShape;
    internal static ConfigEntry<float>? RoundCapSize;
    internal static ConfigEntry<float>? RoundWidth;
    internal static ConfigEntry<float>? RoundHeight;
    internal static ConfigEntry<float>? RoundDepth;
    internal static ConfigEntry<float>? RoundTravel;

    // ---- [BoardButtons] — Confirm/Undo keycaps (PlayTray.BuildButtons ONLY) ---------------
    internal static ConfigEntry<float>? BoardWidth;
    internal static ConfigEntry<float>? BoardHeight;
    internal static ConfigEntry<float>? BoardDepth;
    internal static ConfigEntry<float>? BoardTravel;

    // ---- [BoardDashboard] — gear + Fixiert plates (PlayTray.CreateDashboardButtons ONLY) --
    internal static ConfigEntry<float>? DashGearWidth;
    internal static ConfigEntry<float>? DashPinWidth;
    internal static ConfigEntry<float>? DashHeight;
    internal static ConfigEntry<float>? DashDepth;
    internal static ConfigEntry<float>? DashTravel;

    /// <summary>Raised on every entry write (settings-panel steppers bind here).</summary>
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

        RoundOffsetX = config.Bind("RoundButtons", "OffsetX", 0f,
            "Sideways offset (tray-ROOT-local meters, +X = toward the board's right edge / the " +
            "Undo-gear pads) of the transient round-phase button group (skip-step etc.) from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        RoundOffsetY = config.Bind("RoundButtons", "OffsetY", 0f,
            "Up-board offset (tray-ROOT-local meters, +Y = toward the card slots / far edge, " +
            "-Y = toward the bottom edge and handle) of the transient button group from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        RoundOffsetZ = config.Bind("RoundButtons", "OffsetZ", 0f,
            "Out-of-plane offset (tray-ROOT-local meters, +Z = OUT of the board toward the " +
            "player, -Z = sunk toward/behind the board face) of the transient button group " +
            "from its default proud seat. Live; clamped -0.30..0.30.");
        RoundShape = config.Bind("RoundButtons", "Shape", Cards.ButtonShape.Round,
            "Cap shape of the transient round-phase buttons: Round = flattened puck (default), " +
            "Square = boxy keycap (then this section's Width/Height/Depth apply). Live.");
        RoundCapSize = config.Bind("RoundButtons", "CapSize", DefaultRoundCapSize,
            "Cap radius (cluster-local meters) of the transient buttons. The column auto-fit only " +
            "SHRINKS below this when several buttons must share the column; a single button uses " +
            "exactly this size. Live; clamped 0.015..0.09.");
        RoundWidth = config.Bind("RoundButtons", "Width", DefaultRoundWidth,
            "Cap width (meters) of the transient buttons while Shape=Square. Applies ONLY to " +
            "this group. Live; clamped 0.02..0.20.");
        RoundHeight = config.Bind("RoundButtons", "Height", DefaultRoundHeight,
            "Cap height (meters) of the transient buttons while Shape=Square. Applies ONLY to " +
            "this group. Live; clamped 0.015..0.20.");
        RoundDepth = config.Bind("RoundButtons", "Depth", DefaultRoundDepth,
            "Cap depth/extrusion (meters toward the player) of the transient buttons while " +
            "Shape=Square. Applies ONLY to this group. Live; clamped 0.006..0.08.");
        RoundTravel = config.Bind("RoundButtons", "Travel", DefaultRoundTravel,
            "Press travel (meters) of the transient buttons — how far a cap sinks under the " +
            "fingertip before the depth-fire press commits (fires at 90% of travel). Applies " +
            "ONLY to this group. Live; clamped 0.002..0.02.");

        BoardWidth = config.Bind("BoardButtons", "Width", DefaultBoardWidth,
            "Cap width (meters, along the board's X) of the Confirm/Undo keycaps on the control " +
            "board. Applies ONLY to Confirm/Undo (square shape). Live; clamped 0.02..0.20.");
        BoardHeight = config.Bind("BoardButtons", "Height", DefaultBoardHeight,
            "Cap height (meters, along the board's Y) of the Confirm/Undo keycaps. Applies ONLY " +
            "to Confirm/Undo (square shape). Live; clamped 0.015..0.20.");
        BoardDepth = config.Bind("BoardButtons", "Depth", DefaultBoardDepth,
            "Cap depth/extrusion (meters toward the player) of the Confirm/Undo keycaps. " +
            "Applies ONLY to Confirm/Undo. Live; clamped 0.006..0.08.");
        BoardTravel = config.Bind("BoardButtons", "Travel", DefaultBoardTravel,
            "Press travel (meters) of the Confirm/Undo keycaps — cap sink distance and the " +
            "depth-fire push distance. Applies ONLY to Confirm/Undo. Live; clamped 0.002..0.02.");

        DashGearWidth = config.Bind("BoardDashboard", "GearWidth", DefaultGearWidth,
            "Cap width (meters) of the settings-gear plate on the control board. Applies ONLY " +
            "to the gear. Live; clamped 0.02..0.20.");
        DashPinWidth = config.Bind("BoardDashboard", "PinWidth", DefaultPinWidth,
            "Cap width (meters) of the follow/pin toggle ('Fixiert') plate on the control " +
            "board. Applies ONLY to that toggle. Live; clamped 0.02..0.20.");
        DashHeight = config.Bind("BoardDashboard", "Height", DefaultDashHeight,
            "Cap height (meters) of the gear + follow/pin plates. Applies ONLY to those two. " +
            "Live; clamped 0.015..0.20.");
        DashDepth = config.Bind("BoardDashboard", "Depth", DefaultDashDepth,
            "Cap depth/extrusion (meters toward the player) of the gear + follow/pin plates. " +
            "Applies ONLY to those two. Live; clamped 0.006..0.08.");
        DashTravel = config.Bind("BoardDashboard", "Travel", DefaultDashTravel,
            "Press travel (meters) of the gear + follow/pin plates. Applies ONLY to those two. " +
            "Live; clamped 0.002..0.02.");

        MigrateLegacy(config);

        Hook(RoundOffsetX);
        Hook(RoundOffsetY);
        Hook(RoundOffsetZ);
        Hook(RoundShape);
        Hook(RoundCapSize);
        Hook(RoundWidth);
        Hook(RoundHeight);
        Hook(RoundDepth);
        Hook(RoundTravel);
        Hook(BoardWidth);
        Hook(BoardHeight);
        Hook(BoardDepth);
        Hook(BoardTravel);
        Hook(DashGearWidth);
        Hook(DashPinWidth);
        Hook(DashHeight);
        Hook(DashDepth);
        Hook(DashTravel);
    }

    /// <summary>
    /// ONE-TIME legacy migration (pre-split cfg → per-category sections). The old file had
    /// [TransientButtons] OffsetX/OffsetY/Shape/CapSize plus a SHARED [SquareCaps]
    /// Width/Height/Depth/Travel where 0 = "authored default" ("Auto") — and the shared
    /// entries leaked across button categories. Rules:
    /// - [TransientButtons] values move 1:1 into [RoundButtons] (same semantics).
    /// - A NONZERO [SquareCaps] value is copied into EVERY category it used to affect
    ///   (RoundButtons + BoardButtons + BoardDashboard) so the user's current look is
    ///   preserved exactly; the categories are then independently adjustable.
    /// - A saved 0 was the old "Auto" sentinel: it is NOT copied (a literal 0 would build
    ///   0-sized caps) — the new numeric per-category defaults, which ARE the authored
    ///   values 0 used to mean, take over. Logged.
    /// The legacy entries are then removed from the cfg and the file saved, so this runs
    /// exactly once per install.
    /// </summary>
    private static void MigrateLegacy(ConfigFile config)
    {
        bool hasLegacy;
        try
        {
            hasLegacy = System.IO.File.Exists(config.ConfigFilePath)
                        && System.IO.File.ReadAllText(config.ConfigFilePath) is string text
                        && (text.Contains("[TransientButtons]") || text.Contains("[SquareCaps]"));
        }
        catch (System.Exception)
        {
            hasLegacy = false; // unreadable file — nothing to migrate from
        }
        if (!hasLegacy)
            return;

        // Bind the legacy entries (this consumes whatever the user saved; absent = default).
        ConfigEntry<float> offX = config.Bind("TransientButtons", "OffsetX", 0f, "legacy");
        ConfigEntry<float> offY = config.Bind("TransientButtons", "OffsetY", 0f, "legacy");
        ConfigEntry<Cards.ButtonShape> shape =
            config.Bind("TransientButtons", "Shape", Cards.ButtonShape.Round, "legacy");
        ConfigEntry<float> capSize = config.Bind("TransientButtons", "CapSize", DefaultRoundCapSize, "legacy");
        ConfigEntry<float> width = config.Bind("SquareCaps", "Width", 0f, "legacy");
        ConfigEntry<float> height = config.Bind("SquareCaps", "Height", 0f, "legacy");
        ConfigEntry<float> depth = config.Bind("SquareCaps", "Depth", 0f, "legacy");
        ConfigEntry<float> travel = config.Bind("SquareCaps", "Travel", 0f, "legacy");

        var moved = new System.Text.StringBuilder();
        var zeros = new System.Text.StringBuilder();

        void Copy(string name, float value, params ConfigEntry<float>?[] targets)
        {
            if (value > 0f)
            {
                foreach (ConfigEntry<float>? t in targets)
                {
                    if (t != null)
                        t.Value = value;
                }
                moved.Append(moved.Length > 0 ? ", " : "").Append($"{name}={value:F3}");
            }
            else
            {
                // Old 0 = "Auto" sentinel → the new numeric per-category defaults apply.
                zeros.Append(zeros.Length > 0 ? ", " : "").Append(name);
            }
        }

        if (RoundOffsetX != null && offX.Value != 0f)
            RoundOffsetX.Value = offX.Value;
        if (RoundOffsetY != null && offY.Value != 0f)
            RoundOffsetY.Value = offY.Value;
        if (RoundShape != null && shape.Value != Cards.ButtonShape.Round)
            RoundShape.Value = shape.Value;
        if (RoundCapSize != null && capSize.Value > 0f)
            RoundCapSize.Value = capSize.Value;

        Copy("Width", width.Value, RoundWidth, BoardWidth, DashGearWidth, DashPinWidth);
        Copy("Height", height.Value, RoundHeight, BoardHeight, DashHeight);
        Copy("Depth", depth.Value, RoundDepth, BoardDepth, DashDepth);
        Copy("Travel", travel.Value, RoundTravel, BoardTravel, DashTravel);

        config.Remove(offX.Definition);
        config.Remove(offY.Definition);
        config.Remove(shape.Definition);
        config.Remove(capSize.Definition);
        config.Remove(width.Definition);
        config.Remove(height.Definition);
        config.Remove(depth.Definition);
        config.Remove(travel.Definition);
        config.Save();

        VRLog.Info("WorldUI", "ButtonTuning: migrated legacy [TransientButtons]/[SquareCaps] entries to the " +
                              "per-category sections (RoundButtons/BoardButtons/BoardDashboard) — " +
                              (moved.Length > 0
                                  ? $"copied {moved} into every category the shared entry used to affect; "
                                  : "no nonzero shared geometry to copy; ") +
                              (zeros.Length > 0
                                  ? $"{zeros} were 0 ('Auto') → rewritten to the per-category numeric authored defaults; "
                                  : "") +
                              "legacy entries removed (one-time).");
    }

    private static void Hook<T>(ConfigEntry<T> entry) =>
        entry.SettingChanged += (_, _) =>
        {
            Version++;
            Changed?.Invoke();
        };

    // ---- clamped live accessors (safe before Bind — fall back to authored defaults) --------

    /// <summary>Tray-root-frame offset of the transient button group (user #8): X sideways, Y up-board, Z toward the player.</summary>
    internal static Vector3 TransientOffset => new(
        Clamped(RoundOffsetX, 0f, -0.30f, 0.30f),
        Clamped(RoundOffsetY, 0f, -0.30f, 0.30f),
        Clamped(RoundOffsetZ, 0f, -0.30f, 0.30f));

    /// <summary>Whether the transient cluster caps are round pucks (default) or square keycaps.</summary>
    internal static bool TransientRound =>
        RoundShape == null || RoundShape.Value == Cards.ButtonShape.Round;

    /// <summary>Configured transient cap radius, cluster-local meters (auto-fit ceiling).</summary>
    internal static float TransientCapRadius => Clamped(RoundCapSize, DefaultRoundCapSize, 0.015f, 0.09f);

    /// <summary>[RoundButtons] square-shape cap width (transient cluster ONLY).</summary>
    internal static float RoundCapWidth => Clamped(RoundWidth, DefaultRoundWidth, 0.02f, 0.20f);

    /// <summary>[RoundButtons] square-shape cap height (transient cluster ONLY).</summary>
    internal static float RoundCapHeight => Clamped(RoundHeight, DefaultRoundHeight, 0.015f, 0.20f);

    /// <summary>[RoundButtons] square-shape cap depth (transient cluster ONLY).</summary>
    internal static float RoundCapDepth => Clamped(RoundDepth, DefaultRoundDepth, 0.006f, 0.08f);

    /// <summary>[RoundButtons] press travel (transient cluster ONLY).</summary>
    internal static float RoundCapTravel => Clamped(RoundTravel, DefaultRoundTravel, 0.002f, 0.02f);

    /// <summary>[BoardButtons] cap width (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapWidth => Clamped(BoardWidth, DefaultBoardWidth, 0.02f, 0.20f);

    /// <summary>[BoardButtons] cap height (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapHeight => Clamped(BoardHeight, DefaultBoardHeight, 0.015f, 0.20f);

    /// <summary>[BoardButtons] cap depth (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapDepth => Clamped(BoardDepth, DefaultBoardDepth, 0.006f, 0.08f);

    /// <summary>[BoardButtons] press travel (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapTravel => Clamped(BoardTravel, DefaultBoardTravel, 0.002f, 0.02f);

    /// <summary>[BoardDashboard] settings-gear plate width (gear ONLY).</summary>
    internal static float DashboardGearWidth => Clamped(DashGearWidth, DefaultGearWidth, 0.02f, 0.20f);

    /// <summary>[BoardDashboard] follow/pin ('Fixiert') plate width (that toggle ONLY).</summary>
    internal static float DashboardPinWidth => Clamped(DashPinWidth, DefaultPinWidth, 0.02f, 0.20f);

    /// <summary>[BoardDashboard] gear + follow/pin plate height.</summary>
    internal static float DashboardHeight => Clamped(DashHeight, DefaultDashHeight, 0.015f, 0.20f);

    /// <summary>[BoardDashboard] gear + follow/pin plate depth.</summary>
    internal static float DashboardDepth => Clamped(DashDepth, DefaultDashDepth, 0.006f, 0.08f);

    /// <summary>[BoardDashboard] gear + follow/pin press travel.</summary>
    internal static float DashboardTravel => Clamped(DashTravel, DefaultDashTravel, 0.002f, 0.02f);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);

    /// <summary>One-line value dump for the "geometry config applied" log (all values numeric — no Auto).</summary>
    internal static string Describe()
    {
        Vector3 off = TransientOffset;
        return $"round offset ({off.x:F3}, {off.y:F3}, {off.z:F3}) m, shape {(TransientRound ? "Round" : "Square")}, " +
               $"cap size {TransientCapRadius:F3} m, W/H/D {RoundCapWidth:F3}/{RoundCapHeight:F3}/{RoundCapDepth:F3} m, " +
               $"travel {RoundCapTravel:F3} m; board W/H/D {BoardCapWidth:F3}/{BoardCapHeight:F3}/{BoardCapDepth:F3} m, " +
               $"travel {BoardCapTravel:F3} m; dashboard gear/pin W {DashboardGearWidth:F3}/{DashboardPinWidth:F3} m, " +
               $"H/D {DashboardHeight:F3}/{DashboardDepth:F3} m, travel {DashboardTravel:F3} m";
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
