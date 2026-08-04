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
/// [RestButtons] — the short/long rest keycaps in the tray's rest zone (2026-07 category
/// completion, user: "set button form per category for ALL buttons"): cap width/height
/// (used when the per-board Rest shape is Square — Round rest discs keep the per-board
/// authored diameter, exactly like the Confirm/Undo Round path), plus depth/extrusion and
/// press travel that apply to BOTH shapes. Consumed by <c>RestControls.EnsureBuilt</c>
/// ONLY. The Rest cap SHAPE and the round disc DIAMETER stay per-board
/// (<c>CardsConfig.RestButtonShape</c>/<c>RestButtonDiameter</c>), so nothing here leaks
/// across categories.
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
    internal const float DefaultRoundCapSize = Defaults.RoundButtons_CapSize;  // ButtonCluster column cap-radius ceiling
    internal const float DefaultRoundWidth = 0.084f;    // square cluster cap = 2 × the 42 mm radius
    internal const float DefaultRoundHeight = 0.084f;
    internal const float DefaultRoundDepth = 0.012f;    // authored square cluster-cap extrusion
    internal const float DefaultRoundTravel = Defaults.RoundButtons_Travel;   // authored 8 mm cluster cap travel
    internal const float DefaultBoardWidth = 0.073f;    // authored Confirm/Undo cap side (was [Cards] ConfirmUndoSize_Bronze until its 2026-08 retirement; also the round-cap diameter now)
    internal const float DefaultBoardHeight = 0.073f;
    internal const float DefaultBoardDepth = 0.036f;    // PlayTray.SquareCapThickness
    internal const float DefaultBoardTravel = Defaults.BoardButtons_Travel;   // BoardButton.CapTravel
    internal const float DefaultPinWidth = Defaults.PinWidth;      // authored follow/pin plate width
    internal const float DefaultDashHeight = 0.030f;    // authored gear/pin plate height
    internal const float DefaultDashDepth = 0.030f;     // authored gear/pin plate extrusion
    internal const float DefaultDashTravel = Defaults.BoardDashboard_Travel;    // BoardButton.CapTravel (same authored travel)
    internal const float DefaultRestWidth = Defaults.RestButtons_Width;     // authored RestButtonDiameter default (square rest-cap side)
    internal const float DefaultRestHeight = Defaults.RestButtons_Height;
    internal const float DefaultRestDepth = Defaults.RestButtons_Depth;     // authored CardsConfig.RoundButtonThickness (rest disc/cap thickness)
    internal const float DefaultRestTravel = Defaults.RestButtons_Travel;    // BoardButton.CapTravel (rest discs used the authored default)

    // ---- authored colour defaults ([ButtonColors] — reproduce today's look bit-exact) -------
    // Label fill = NativeButtonSkin.LabelColor bright warm parchment (#FBF3E0); outline = its
    // dark-umber engrave keyline; every cap-face tint seeds WHITE (1,1,1) = identity multiply.
    internal const float DefaultLabelR = Defaults.LabelR;
    internal const float DefaultLabelG = Defaults.LabelG;
    internal const float DefaultLabelB = Defaults.LabelB;
    internal const float DefaultLabelOutlineR = 0.09f;
    internal const float DefaultLabelOutlineG = 0.06f;
    internal const float DefaultLabelOutlineB = 0.03f;
    internal const float DefaultLabelOutlineWidth = Defaults.LabelOutlineWidth; // fraction of the SDF spread

    /// <summary>Keycap category whose [ButtonColors] cap-face TINT applies (see <see cref="CapTint"/>).
    /// Matches the geometry categories; <see cref="CapCategory.Rest"/> is the default so the
    /// RestControls call site (which does not pass one) picks up the rest tint automatically.</summary>
    internal enum CapCategory { Board, Dashboard, Cluster, Rest }

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
    internal static ConfigEntry<float>? DashPinWidth;
    internal static ConfigEntry<float>? DashHeight;
    internal static ConfigEntry<float>? DashDepth;
    internal static ConfigEntry<float>? DashTravel;

    // ---- [RestButtons] — short/long rest keycaps (RestControls.EnsureBuilt ONLY) ----------
    internal static ConfigEntry<float>? RestWidth;
    internal static ConfigEntry<float>? RestHeight;
    internal static ConfigEntry<float>? RestDepth;
    internal static ConfigEntry<float>? RestTravel;

    // ---- [ButtonColors] — user-tunable keycap LABEL text + cap-FACE colours (user: "give me
    // the TEXT COLORS and BUTTON COLORS as a debug option"). Defaults reproduce today's look
    // EXACTLY: the label binds seed the current bright-parchment fill / dark-umber outline, and
    // every cap-face tint seeds WHITE (1,1,1) = an identity multiply so caps are unchanged until
    // the user edits. R/G/B floats (0..1) so the settings panel steppers them cleanly. ----------
    internal static ConfigEntry<float>? LabelR;
    internal static ConfigEntry<float>? LabelG;
    internal static ConfigEntry<float>? LabelB;
    internal static ConfigEntry<bool>? LabelOutline;
    internal static ConfigEntry<float>? LabelOutlineR;
    internal static ConfigEntry<float>? LabelOutlineG;
    internal static ConfigEntry<float>? LabelOutlineB;
    internal static ConfigEntry<float>? LabelOutlineW;
    internal static ConfigEntry<bool>? LabelUnderlay;
    internal static ConfigEntry<float>? BoardCapTintR, BoardCapTintG, BoardCapTintB;      // Confirm/Undo
    internal static ConfigEntry<float>? DashCapTintR, DashCapTintG, DashCapTintB;         // gear/Fixiert
    internal static ConfigEntry<float>? ClusterCapTintR, ClusterCapTintG, ClusterCapTintB; // round-phase cluster
    internal static ConfigEntry<float>? RestCapTintR, RestCapTintG, RestCapTintB;         // short/long rest

    /// <summary>Raised on every entry write (settings-panel steppers bind here).</summary>
    internal static event System.Action? Changed;

    /// <summary>Monotonic change counter — pull-based consumers rebuild when it moves.</summary>
    internal static int Version { get; private set; }

    // ---- press-travel firing (user #6, depth-fire — constants, not config) -----------------
    /// <summary>Fraction of the full cap travel the fingertip must reach before the press fires.</summary>
    internal const float PressFireFraction = 0.90f;
    /// <summary>The cap must rise back above (1 − this) … i.e. depth must fall to ≤ this fraction before a new press can fire.</summary>
    internal const float PressRearmFraction = 0.50f;

    // ---- press debounce (user: keycap double-trigger — same fix as PileStack.OnPoke) --------
    /// <summary>
    /// Shared press-debounce cooldown (seconds) for the 3D keycaps (BoardButton + the
    /// ButtonCluster caps), copied 1:1 from <c>PileViewer.PokeToggleCooldownSeconds</c>: after a
    /// press commits, NO second press — from the retract/re-entry of the same physical poke, a
    /// PokeInteractor hover flicker, or a cross-path poke+laser — fires within this window. It
    /// composes with the depth-fire: the depth-fire (~90% travel) is the press event and still
    /// requires the cap to first retract past <see cref="PressRearmFraction"/> to re-arm; this
    /// cooldown additionally kills the exit+enter re-fire the hysteresis alone could not. The
    /// board laser routes through the same cooldown (cooldown-debounced, NOT dwell), so a
    /// deliberate second laser click after the window keeps working.
    /// </summary>
    internal const float PokePressCooldownSeconds = 0.4f;

    // ---- keycap appear/disappear animation ([ButtonAnim] — user: "APPEAR animation instead of
    // popping in; the disappear should apply to ALL vanishing buttons"). Small live config: an
    // enable toggle + the two durations, defaults matching the prior fast, subtle constants so the
    // shipped feel is unchanged until edited. Consumed by BoardButton (Confirm/Undo/gear/Fixiert/
    // rest) and the round-phase ButtonCluster caps. Durations clamp to a small floor (never 0 →
    // no divide-by-zero in the shrink/scale ramps); to turn the effect OFF use Enable=false, which
    // hides/shows the caps INSTANTLY (no dust, no scale-in). All LOCAL visuals — never synced. -----
    internal const float DefaultDissolveSeconds = Defaults.DisappearSeconds; // authored shrink-out duration
    internal const float DefaultAppearSeconds = Defaults.AppearSeconds;   // authored scale-in duration
    internal static ConfigEntry<bool>? AnimEnable;
    internal static ConfigEntry<bool>? AnimAppearParticles;
    internal static ConfigEntry<float>? AnimDissolveDuration;
    internal static ConfigEntry<float>? AnimAppearDuration;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("buttons");

        RoundOffsetX = config.Bind("RoundButtons", "OffsetX", Defaults.RoundButtons_OffsetX,
            "Sideways offset (tray-ROOT-local meters, +X = toward the board's right edge / the " +
            "Undo-gear pads) of the transient round-phase button group (skip-step etc.) from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        RoundOffsetY = config.Bind("RoundButtons", "OffsetY", Defaults.RoundButtons_OffsetY,
            "Up-board offset (tray-ROOT-local meters, +Y = toward the card slots / far edge, " +
            "-Y = toward the bottom edge and handle) of the transient button group from its " +
            "default anchor. Live; clamped -0.30..0.30.");
        RoundOffsetZ = config.Bind("RoundButtons", "OffsetZ", Defaults.OffsetZ,
            "Out-of-plane offset (tray-ROOT-local meters, +Z = OUT of the board toward the " +
            "player, -Z = sunk toward/behind the board face) of the transient button group " +
            "from its default proud seat. Live; clamped -0.30..0.30.");
        RoundShape = config.Bind("RoundButtons", "Shape", Defaults.RoundButtons_Shape,
            "Cap shape of the transient round-phase buttons: Round = flattened puck (default), " +
            "Square = boxy keycap (then this section's Width/Height/Depth apply). Live.");
        RoundCapSize = config.Bind("RoundButtons", "CapSize", Defaults.RoundButtons_CapSize,
            "Cap radius (cluster-local meters) of the transient buttons. The column auto-fit only " +
            "SHRINKS below this when several buttons must share the column; a single button uses " +
            "exactly this size. Live; clamped 0.015..0.09.");
        RoundWidth = config.Bind("RoundButtons", "Width", Defaults.RoundButtons_Width,
            "Cap width (meters) of the transient buttons while Shape=Square. Applies ONLY to " +
            "this group. Live; clamped 0.02..0.20.");
        RoundHeight = config.Bind("RoundButtons", "Height", Defaults.RoundButtons_Height,
            "Cap height (meters) of the transient buttons while Shape=Square. Applies ONLY to " +
            "this group. Live; clamped 0.015..0.20.");
        RoundDepth = config.Bind("RoundButtons", "Depth", Defaults.RoundButtons_Depth,
            "Cap depth/extrusion (meters toward the player) of the transient buttons while " +
            "Shape=Square. Applies ONLY to this group. Live; clamped 0.006..0.08.");
        RoundTravel = config.Bind("RoundButtons", "Travel", Defaults.RoundButtons_Travel,
            "Press travel (meters) of the transient buttons — how far a cap sinks under the " +
            "fingertip before the depth-fire press commits (fires at 90% of travel). Applies " +
            "ONLY to this group. Live; clamped 0.002..0.02.");

        BoardWidth = config.Bind("BoardButtons", "Width", Defaults.BoardButtons_Width,
            "Cap width (meters, along the board's X) of the Confirm/Undo keycaps on the control " +
            "board. Applies ONLY to Confirm/Undo (square shape). Live; clamped 0.02..0.20.");
        BoardHeight = config.Bind("BoardButtons", "Height", Defaults.BoardButtons_Height,
            "Cap height (meters, along the board's Y) of the Confirm/Undo keycaps. Applies ONLY " +
            "to Confirm/Undo (square shape). Live; clamped 0.015..0.20.");
        BoardDepth = config.Bind("BoardButtons", "Depth", Defaults.BoardButtons_Depth,
            "Cap depth/extrusion (meters toward the player) of the Confirm/Undo keycaps. " +
            "Applies ONLY to Confirm/Undo. Live; clamped 0.006..0.08.");
        BoardTravel = config.Bind("BoardButtons", "Travel", Defaults.BoardButtons_Travel,
            "Press travel (meters) of the Confirm/Undo keycaps — cap sink distance and the " +
            "depth-fire push distance. Applies ONLY to Confirm/Undo. Live; clamped 0.002..0.02.");

        DashPinWidth = config.Bind("BoardDashboard", "PinWidth", Defaults.PinWidth,
            "Cap width (meters) of the follow/pin toggle ('Fixiert') plate on the control " +
            "board. Applies ONLY to that toggle. Live; clamped 0.02..0.20.");
        DashHeight = config.Bind("BoardDashboard", "Height", Defaults.BoardDashboard_Height,
            "Cap height (meters) of the follow/pin plate. Applies ONLY to that toggle. " +
            "Live; clamped 0.015..0.20.");
        DashDepth = config.Bind("BoardDashboard", "Depth", Defaults.BoardDashboard_Depth,
            "Cap depth/extrusion (meters toward the player) of the gear + follow/pin plates. " +
            "Applies ONLY to those two. Live; clamped 0.006..0.08.");
        DashTravel = config.Bind("BoardDashboard", "Travel", Defaults.BoardDashboard_Travel,
            "Press travel (meters) of the gear + follow/pin plates. Applies ONLY to those two. " +
            "Live; clamped 0.002..0.02.");

        RestWidth = config.Bind("RestButtons", "Width", Defaults.RestButtons_Width,
            "Cap width (meters) of the short/long REST keycaps in the tray's rest zone while their " +
            "per-board shape is Square. Round rest discs keep the per-board authored diameter. " +
            "Applies ONLY to the rest buttons. Live; clamped 0.02..0.20.");
        RestHeight = config.Bind("RestButtons", "Height", Defaults.RestButtons_Height,
            "Cap height (meters) of the short/long REST keycaps while their per-board shape is " +
            "Square. Applies ONLY to the rest buttons. Live; clamped 0.015..0.20.");
        RestDepth = config.Bind("RestButtons", "Depth", Defaults.RestButtons_Depth,
            "Cap depth/extrusion (meters toward the player) of the short/long REST keycaps — for " +
            "BOTH Round discs and Square caps. Applies ONLY to the rest buttons. Live; clamped 0.006..0.08.");
        RestTravel = config.Bind("RestButtons", "Travel", Defaults.RestButtons_Travel,
            "Press travel (meters) of the short/long REST keycaps — cap sink distance and the " +
            "depth-fire push distance. Applies ONLY to the rest buttons. Live; clamped 0.002..0.02.");

        // ---- [ButtonColors] — keycap LABEL text + cap-FACE colours (user debug option) --------
        LabelR = config.Bind("ButtonColors", "LabelR", Defaults.LabelR,
            "Engraved keycap LABEL colour — RED channel (0..1). Colours the text on EVERY 3D keycap " +
            "(Confirm/Undo, gear/Fixiert, short/long rest, the round-phase cluster) AND the docked " +
            "native button captions. Default 0.984 = the bright warm parchment (#FBF3E0). Live.");
        LabelG = config.Bind("ButtonColors", "LabelG", Defaults.LabelG,
            "Engraved keycap LABEL colour — GREEN channel (0..1). Default 0.953 (#FBF3E0). Live.");
        LabelB = config.Bind("ButtonColors", "LabelB", Defaults.LabelB,
            "Engraved keycap LABEL colour — BLUE channel (0..1). Default 0.878 (#FBF3E0). Live.");
        LabelOutline = config.Bind("ButtonColors", "LabelOutline", Defaults.LabelOutline,
            "Draw the dark keyline OUTLINE around the keycap label (the carved-engraving rim that " +
            "separates bright glyphs from a light brass cap). Turn OFF for a flat label. Default true. Live.");
        LabelOutlineR = config.Bind("ButtonColors", "LabelOutlineR", Defaults.LabelOutlineR,
            "Keycap label OUTLINE colour — RED channel (0..1). Default 0.09 = dark umber. Live.");
        LabelOutlineG = config.Bind("ButtonColors", "LabelOutlineG", Defaults.LabelOutlineG,
            "Keycap label OUTLINE colour — GREEN channel (0..1). Default 0.06 = dark umber. Live.");
        LabelOutlineB = config.Bind("ButtonColors", "LabelOutlineB", Defaults.LabelOutlineB,
            "Keycap label OUTLINE colour — BLUE channel (0..1). Default 0.03 = dark umber. Live.");
        LabelOutlineW = config.Bind("ButtonColors", "LabelOutlineWidth", Defaults.LabelOutlineWidth,
            "Keycap label OUTLINE width, fraction of the SDF spread (thicker = a heavier dark rim). " +
            "Default 0.20. Live; clamped 0..1.");
        LabelUnderlay = config.Bind("ButtonColors", "LabelUnderlay", Defaults.LabelUnderlay,
            "Draw the soft dark drop-shadow UNDERLAY beneath the keycap label (a second contrast cue " +
            "on light caps). Turn OFF to drop the shadow. Default true. Live.");

        BoardCapTintR = config.Bind("ButtonColors", "BoardCapTintR", Defaults.BoardCapTintR,
            "Confirm/Undo keycap FACE colour TINT — RED channel (0..1), MULTIPLIED into the cap face " +
            "(native sprite AND procedural). 1 = unchanged; lower = darker/less red, so white text reads. Live.");
        BoardCapTintG = config.Bind("ButtonColors", "BoardCapTintG", Defaults.BoardCapTintG,
            "Confirm/Undo keycap FACE tint — GREEN channel (0..1). 1 = unchanged. Live.");
        BoardCapTintB = config.Bind("ButtonColors", "BoardCapTintB", Defaults.BoardCapTintB,
            "Confirm/Undo keycap FACE tint — BLUE channel (0..1). 1 = unchanged. Live.");
        DashCapTintR = config.Bind("ButtonColors", "DashCapTintR", Defaults.DashCapTintR,
            "Dashboard gear + Fixiert (follow/pin) plate FACE tint — RED channel (0..1). 1 = unchanged. Live.");
        DashCapTintG = config.Bind("ButtonColors", "DashCapTintG", Defaults.DashCapTintG,
            "Dashboard gear + Fixiert plate FACE tint — GREEN channel (0..1). 1 = unchanged. Live.");
        DashCapTintB = config.Bind("ButtonColors", "DashCapTintB", Defaults.DashCapTintB,
            "Dashboard gear + Fixiert plate FACE tint — BLUE channel (0..1). 1 = unchanged. Live.");
        ClusterCapTintR = config.Bind("ButtonColors", "ClusterCapTintR", Defaults.ClusterCapTintR,
            "Round-phase cluster (Ready/Undo/Skip) cap FACE tint — RED channel (0..1). 1 = unchanged. Live.");
        ClusterCapTintG = config.Bind("ButtonColors", "ClusterCapTintG", Defaults.ClusterCapTintG,
            "Round-phase cluster cap FACE tint — GREEN channel (0..1). 1 = unchanged. Live.");
        ClusterCapTintB = config.Bind("ButtonColors", "ClusterCapTintB", Defaults.ClusterCapTintB,
            "Round-phase cluster cap FACE tint — BLUE channel (0..1). 1 = unchanged. Live.");
        RestCapTintR = config.Bind("ButtonColors", "RestCapTintR", Defaults.RestCapTintR,
            "Short/long REST keycap FACE tint — RED channel (0..1). 1 = unchanged. Live.");
        RestCapTintG = config.Bind("ButtonColors", "RestCapTintG", Defaults.RestCapTintG,
            "Short/long REST keycap FACE tint — GREEN channel (0..1). 1 = unchanged. Live.");
        RestCapTintB = config.Bind("ButtonColors", "RestCapTintB", Defaults.RestCapTintB,
            "Short/long REST keycap FACE tint — BLUE channel (0..1). 1 = unchanged. Live.");

        // ---- [ButtonAnim] — keycap appear/disappear animation (user: general dissolve + APPEAR) ----
        AnimEnable = config.Bind("ButtonAnim", "Enable", Defaults.Enable,
            "Play the appear/disappear animation on the 3D keycaps (Confirm/Undo, gear/Fixiert, the " +
            "short/long rest keycaps and the transient round-phase cluster buttons). ON: a matched pair — " +
            "a vanishing button CRUMBLES TO DUST (shrinks out with a face-colored dust burst) and an " +
            "appearing one MATERIALIZES FROM DUST (converging dust motes settle onto the cap while its " +
            "surface fades up to full colour — no scale pop). OFF: buttons pop in/out instantly (no dust, " +
            "no fade). Input is live immediately either way. Default true. Live.");
        AnimAppearParticles = config.Bind("ButtonAnim", "AppearParticles", Defaults.AppearParticles,
            "Emit the converging 'assembling' dust cloud when a keycap APPEARS (the disappear crumble " +
            "burst in reverse — same pooled system, matched density). OFF = the surface fade-in alone. " +
            "Default true. Live.");
        AnimDissolveDuration = config.Bind("ButtonAnim", "DisappearSeconds", Defaults.DisappearSeconds,
            "Seconds the cap shrinks out while the dust burst plays when a keycap DISAPPEARS (the logical " +
            "hide — input off, layout reflow — is instant regardless). Default 0.16. Live; clamped 0.05..1.0.");
        AnimAppearDuration = config.Bind("ButtonAnim", "AppearSeconds", Defaults.AppearSeconds,
            "Seconds of the materialize-from-dust APPEAR (converging dust motes settle onto the cap while " +
            "its surface fades up from the dust to full colour, in place — no scale pop). Input/colliders " +
            "are live from frame one — the animation is purely visual. Default 0.15. Live; clamped 0.05..1.0.");

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
        Hook(DashPinWidth);
        Hook(DashHeight);
        Hook(DashDepth);
        Hook(DashTravel);
        Hook(RestWidth);
        Hook(RestHeight);
        Hook(RestDepth);
        Hook(RestTravel);
        Hook(LabelR);
        Hook(LabelG);
        Hook(LabelB);
        Hook(LabelOutline);
        Hook(LabelOutlineR);
        Hook(LabelOutlineG);
        Hook(LabelOutlineB);
        Hook(LabelOutlineW);
        Hook(LabelUnderlay);
        Hook(BoardCapTintR); Hook(BoardCapTintG); Hook(BoardCapTintB);
        Hook(DashCapTintR); Hook(DashCapTintG); Hook(DashCapTintB);
        Hook(ClusterCapTintR); Hook(ClusterCapTintG); Hook(ClusterCapTintB);
        Hook(RestCapTintR); Hook(RestCapTintG); Hook(RestCapTintB);
        Hook(AnimEnable); Hook(AnimAppearParticles); Hook(AnimDissolveDuration); Hook(AnimAppearDuration);
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
        ConfigEntry<float> offX = config.Bind("TransientButtons", "OffsetX", Defaults.TransientButtons_OffsetX, "legacy");
        ConfigEntry<float> offY = config.Bind("TransientButtons", "OffsetY", Defaults.TransientButtons_OffsetY, "legacy");
        ConfigEntry<Cards.ButtonShape> shape =
            config.Bind("TransientButtons", "Shape", Defaults.TransientButtons_Shape, "legacy");
        ConfigEntry<float> capSize = config.Bind("TransientButtons", "CapSize", Defaults.TransientButtons_CapSize, "legacy");
        ConfigEntry<float> width = config.Bind("SquareCaps", "Width", Defaults.SquareCaps_Width, "legacy");
        ConfigEntry<float> height = config.Bind("SquareCaps", "Height", Defaults.SquareCaps_Height, "legacy");
        ConfigEntry<float> depth = config.Bind("SquareCaps", "Depth", Defaults.SquareCaps_Depth, "legacy");
        ConfigEntry<float> travel = config.Bind("SquareCaps", "Travel", Defaults.SquareCaps_Travel, "legacy");

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

        Copy("Width", width.Value, RoundWidth, BoardWidth, DashPinWidth);
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

    /// <summary>[BoardDashboard] follow/pin ('Fixiert') plate width (that toggle ONLY).</summary>
    internal static float DashboardPinWidth => Clamped(DashPinWidth, DefaultPinWidth, 0.02f, 0.20f);

    /// <summary>[BoardDashboard] gear + follow/pin plate height.</summary>
    internal static float DashboardHeight => Clamped(DashHeight, DefaultDashHeight, 0.015f, 0.20f);

    /// <summary>[BoardDashboard] gear + follow/pin plate depth.</summary>
    internal static float DashboardDepth => Clamped(DashDepth, DefaultDashDepth, 0.006f, 0.08f);

    /// <summary>[BoardDashboard] gear + follow/pin press travel.</summary>
    internal static float DashboardTravel => Clamped(DashTravel, DefaultDashTravel, 0.002f, 0.02f);

    /// <summary>[RestButtons] Square-shape cap width (short/long rest keycaps ONLY).</summary>
    internal static float RestCapWidth => Clamped(RestWidth, DefaultRestWidth, 0.02f, 0.20f);

    /// <summary>[RestButtons] Square-shape cap height (short/long rest keycaps ONLY).</summary>
    internal static float RestCapHeight => Clamped(RestHeight, DefaultRestHeight, 0.015f, 0.20f);

    /// <summary>[RestButtons] cap depth/extrusion — both Round discs and Square caps (rest keycaps ONLY).</summary>
    internal static float RestCapDepth => Clamped(RestDepth, DefaultRestDepth, 0.006f, 0.08f);

    /// <summary>[RestButtons] press travel (short/long rest keycaps ONLY).</summary>
    internal static float RestCapTravel => Clamped(RestTravel, DefaultRestTravel, 0.002f, 0.02f);

    // ---- [ButtonColors] live accessors (safe before Bind — fall back to the authored look) ----

    /// <summary>Engraved keycap LABEL fill colour (user debug option). Default = the bright warm
    /// parchment #FBF3E0; consumed by <see cref="NativeButtonSkin.LabelColor"/>.</summary>
    internal static Color LabelColor => new(
        Clamped(LabelR, DefaultLabelR, 0f, 1f),
        Clamped(LabelG, DefaultLabelG, 0f, 1f),
        Clamped(LabelB, DefaultLabelB, 0f, 1f), 1f);

    /// <summary>Engraved keycap label OUTLINE colour. Default = dark umber (#170F08-ish).</summary>
    internal static Color LabelOutlineColor => new(
        Clamped(LabelOutlineR, DefaultLabelOutlineR, 0f, 1f),
        Clamped(LabelOutlineG, DefaultLabelOutlineG, 0f, 1f),
        Clamped(LabelOutlineB, DefaultLabelOutlineB, 0f, 1f), 1f);

    /// <summary>Keycap label outline width (fraction of the SDF spread). Default 0.20.</summary>
    internal static float LabelOutlineWidth => Clamped(LabelOutlineW, DefaultLabelOutlineWidth, 0f, 1f);

    /// <summary>Whether the dark keyline outline is drawn on keycap labels (default on).</summary>
    internal static bool LabelOutlineEnabled => LabelOutline == null || LabelOutline.Value;

    /// <summary>Whether the dark drop-shadow underlay is drawn under keycap labels (default on).</summary>
    internal static bool LabelUnderlayEnabled => LabelUnderlay == null || LabelUnderlay.Value;

    /// <summary>Confirm/Undo keycap FACE tint (multiplier, default white = unchanged).</summary>
    internal static Color BoardCapTint => Tint3(BoardCapTintR, BoardCapTintG, BoardCapTintB);

    /// <summary>Gear + Fixiert (follow/pin) plate FACE tint (multiplier, default white).</summary>
    internal static Color DashCapTint => Tint3(DashCapTintR, DashCapTintG, DashCapTintB);

    /// <summary>Round-phase cluster (Ready/Undo/Skip) cap FACE tint (multiplier, default white).</summary>
    internal static Color ClusterCapTint => Tint3(ClusterCapTintR, ClusterCapTintG, ClusterCapTintB);

    /// <summary>Short/long REST keycap FACE tint (multiplier, default white).</summary>
    internal static Color RestCapTint => Tint3(RestCapTintR, RestCapTintG, RestCapTintB);

    /// <summary>Cap-face TINT for a category (default white = identity multiply until edited).</summary>
    internal static Color CapTint(CapCategory category) => category switch
    {
        CapCategory.Board => BoardCapTint,
        CapCategory.Dashboard => DashCapTint,
        CapCategory.Cluster => ClusterCapTint,
        _ => RestCapTint,
    };

    private static Color Tint3(ConfigEntry<float>? r, ConfigEntry<float>? g, ConfigEntry<float>? b) => new(
        Clamped(r, 1f, 0f, 1f), Clamped(g, 1f, 0f, 1f), Clamped(b, 1f, 0f, 1f), 1f);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);

    // ---- [ButtonAnim] live accessors (safe before Bind — fall back to the authored feel) --------

    /// <summary>Whether the keycap appear/disappear animation plays (OFF = instant pop, no dust/scale).</summary>
    internal static bool ButtonAnimEnabled => AnimEnable == null || AnimEnable.Value;

    /// <summary>Whether the converging 'assembling' dust cloud plays on a keycap appear (needs
    /// <see cref="ButtonAnimEnabled"/> too — the surface fade-in runs regardless while animation is on).</summary>
    internal static bool AppearParticlesEnabled => ButtonAnimEnabled && (AnimAppearParticles == null || AnimAppearParticles.Value);

    /// <summary>Seconds the cap shrinks out while the dust burst plays (logical hide is instant).
    /// Floored at 0.05 so the shrink/fade ramps never divide by zero (use Enable=false for instant).</summary>
    internal static float DissolveSeconds => Clamped(AnimDissolveDuration, DefaultDissolveSeconds, 0.05f, 1.0f);

    /// <summary>Seconds of the materialize-from-dust surface fade-in when a keycap appears (floored at 0.05 — see above).</summary>
    internal static float AppearSeconds => Clamped(AnimAppearDuration, DefaultAppearSeconds, 0.05f, 1.0f);

    // Throttle so a relayout that shows/hides several caps in one frame logs once, not a storm.
    private static float _nextAnimLogTime;

    /// <summary>Throttled debug line when a keycap appear/disappear animation plays (user: "add a log
    /// line when appear/disappear plays — button name, which anim"). At most one line per 0.4 s so a
    /// multi-button relayout does not spam; the [WorldUI] debug channel.</summary>
    internal static void LogAnim(string button, string anim)
    {
        float now = Time.unscaledTime;
        if (now < _nextAnimLogTime)
            return;
        _nextAnimLogTime = now + 0.4f;
        VRLog.Debug("WorldUI", $"Keycap anim: '{button}' plays {anim}.");
    }

    /// <summary>One-line value dump for the "geometry config applied" log (all values numeric — no Auto).</summary>
    internal static string Describe()
    {
        Vector3 off = TransientOffset;
        return $"round offset ({off.x:F3}, {off.y:F3}, {off.z:F3}) m, shape {(TransientRound ? "Round" : "Square")}, " +
               $"cap size {TransientCapRadius:F3} m, W/H/D {RoundCapWidth:F3}/{RoundCapHeight:F3}/{RoundCapDepth:F3} m, " +
               $"travel {RoundCapTravel:F3} m; board W/H/D {BoardCapWidth:F3}/{BoardCapHeight:F3}/{BoardCapDepth:F3} m, " +
               $"travel {BoardCapTravel:F3} m; dashboard pin W {DashboardPinWidth:F3} m, " +
               $"H/D {DashboardHeight:F3}/{DashboardDepth:F3} m, travel {DashboardTravel:F3} m; " +
               $"rest W/H/D {RestCapWidth:F3}/{RestCapHeight:F3}/{RestCapDepth:F3} m, travel {RestCapTravel:F3} m";
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
    // Materialize = the disappear's MATCHED PAIR (user: "a matched pair — crumble away / assemble
    // from dust"), so the converging cloud is as dense as the crumble burst, not the old sparse
    // 12-mote shimmer.
    private const int MaterializeCount = BurstCount;

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

    /// <summary>
    /// MATERIALIZE-FROM-DUST for a button that APPEARS (user: "emerge from dust … a matched pair
    /// with the crumble-away") — the <see cref="Play"/> dissolve burst run in REVERSE. A dense
    /// cloud of face-colored motes (matched to the crumble's <see cref="BurstCount"/>) spawns on
    /// a ring OUT around the cap and drifts INWARD, decelerating as it converges and settling ONTO
    /// the cap face, fading as it arrives — so the cap reads as assembling out of the swept-away
    /// powder rather than popping or scaling in. Spawn spread/inward speed mirror the crumble's
    /// outward sweep so the two read as the same powder in opposite time order. Same pooled
    /// world-space system, per-particle Emit — no per-frame allocation. Same args as
    /// <see cref="Play"/>. Purely local visuals; never delays interactivity (input is already live).
    /// </summary>
    internal static void PlayMaterialize(Vector3 center, Vector3 outNormal, float worldSize, Color color)
    {
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — appear degrades to the surface fade-in alone
        color.a = 1f;
        worldSize = Mathf.Clamp(worldSize, 0.01f, 0.5f);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < MaterializeCount; i++)
        {
            Vector3 jitter = Random.insideUnitSphere;
            Vector3 inPlane = Vector3.ProjectOnPlane(jitter, outNormal);
            if (inPlane.sqrMagnitude < 1e-6f)
                inPlane = Vector3.Cross(outNormal, Vector3.up);
            inPlane.Normalize();
            // Spawn on the same ~0.45·size ring the crumble sweeps OUT to, lifted a little off the
            // face, and converge inward toward the cap center so the powder gathers back into place.
            float ring = worldSize * (0.40f + 0.20f * Random.value);
            ep.position = center + inPlane * ring + outNormal * (worldSize * (0.10f + 0.15f * Random.value));
            // Inward drift (mirrors the crumble's outward sweep magnitude) plus a slight lift toward
            // the face so the motes settle ONTO the cap rather than sinking through it.
            ep.velocity = (-inPlane * (0.8f + 0.6f * Random.value)
                           + outNormal * 0.20f) * worldSize;
            ep.startLifetime = 0.16f + 0.12f * Random.value;
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
