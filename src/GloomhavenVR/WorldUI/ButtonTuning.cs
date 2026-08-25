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
/// [RoundButtons] IS GONE (2026-08-25) and there are three geometry categories now, not four. It
/// described the TRANSIENT round-phase group — the separate column that drew the game's turn-flow
/// "Bewegung überspringen" cap — and the user retired that group: the skip is a generic board keycap
/// on the board's third recess, built from [BoardButtons] with Confirm and Undo. A category exists
/// to keep one family of caps from resizing another; a category for a cap that is now a MEMBER of
/// another family would do the opposite.
///
/// [BoardButtons] — the Confirm/Undo ("Fortfahren"/"Rückgängig machen") keycaps on the
/// control board: cap width/height/depth and press travel. Consumed by
/// <c>PlayTray.BuildButtons</c> ONLY.
///
/// [BoardDashboard] — the follow/pin toggle ("Fixiert") plate on the control board: width,
/// height, depth and press travel. Consumed by <c>PlayTray.CreateDashboardButtons</c> ONLY.
/// The set is named in the plural and its bind descriptions still mention a settings gear
/// because it used to drive that plate too; the gear went with the free-floating settings
/// panel (the VR settings are an options-window tab now, <see cref="VROptionsTab"/>). The
/// KEYS stay as they are — they are user data.
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
/// reproduce the authored look bit-identically and every options-tab stepper always
/// shows a real number. Legacy files ([TransientButtons] offsets/shape/cap size and the
/// shared [SquareCaps] 0=Auto geometry that leaked across categories) are migrated once on
/// <see cref="Bind"/>: nonzero values are copied into every category they used to affect
/// (preserving the user's current look), 0-sentinels resolve to the new numeric defaults,
/// and the legacy entries are removed from the cfg (logged).
///
/// Consumers re-read the clamped accessors and rebuild on <see cref="Version"/> change
/// (PlayTray.TickStatus / ButtonCluster.Tick), so every entry is live — no restart. The
/// the VR options tab binds steppers against the public entries + <see cref="Changed"/>.
/// All values are LOCAL visuals — nothing here syncs to multiplayer.
/// </summary>
internal static class ButtonTuning
{
    private static ConfigFile? _file;

    // ---- authored defaults (the exact values each bind replaced — see the consumers) ------
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
    // …and the outline triple NAMES THE SHIPPED ENTRY rather than restating a literal. It used to
    // read 0.09/0.06/0.03 — the authored dark umber — while Defaults.LabelOutlineR/G/B had been
    // REBASED to 0.5 (scripts/rebase-defaults.py, which bakes a tuned player's cfg into the shipped
    // set). Nothing broke, because these three are only the PRE-BIND fallback and every consumer
    // calls Bind() first; but the file then held two different answers to "what colour is a keycap
    // keyline by default", and a reader could not tell which one shipped. Exactly the drift
    // scripts/check-remote-defaults.py exists to catch on the Net/ side, one file earlier.
    internal const float DefaultLabelOutlineR = Defaults.LabelOutlineR;
    internal const float DefaultLabelOutlineG = Defaults.LabelOutlineG;
    internal const float DefaultLabelOutlineB = Defaults.LabelOutlineB;
    internal const float DefaultLabelOutlineWidth = Defaults.LabelOutlineWidth; // fraction of the SDF spread

    /// <summary>Keycap category whose [ButtonColors] cap-face TINT applies (see <see cref="CapTint"/>).
    /// Matches the geometry categories; <see cref="CapCategory.Rest"/> is the default so the
    /// RestControls call site (which does not pass one) picks up the rest tint automatically.</summary>
    internal enum CapCategory { Board, Dashboard, Rest }

    // THE [RoundButtons] SECTION IS GONE (2026-08-25) — nine entries that sized, seated, shaped and
    // tuned the press of ONE cap: the turn-flow SKIP, drawn by the retired WorldUI/ButtonCluster.cs
    // in its own column. That cap is a generic board keycap on the board's third recess now and is
    // built from [BoardButtons] Width/Height/Depth/Travel together with Confirm and Undo, which is
    // what the user's "so dass all diese buttons gleich aussehen" requires. Keeping a second
    // geometry family for one of three identical caps could only make them differ again.

    // ---- [BoardButtons] — Confirm/Undo keycaps (PlayTray.BuildButtons ONLY) ---------------
    internal static ConfigEntry<float>? BoardWidth;
    internal static ConfigEntry<float>? BoardHeight;
    internal static ConfigEntry<float>? BoardDepth;
    internal static ConfigEntry<float>? BoardTravel;

    // ---- [BoardDashboard] — the Fixiert plate (PlayTray.CreateDashboardButtons ONLY) -------
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
    // the user edits. R/G/B floats (0..1) so the options tab steppers them cleanly. -----------
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
    internal static ConfigEntry<float>? RestCapTintR, RestCapTintG, RestCapTintB;         // short/long rest

    /// <summary>Raised on every entry write (the options tab's steppers bind here).</summary>
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

    // ---- THE PRESS STROKE ---------------------------------------------------------------------
    // It LIVES IN WorldUI/ButtonStroke.cs, not here, and the move is not tidying. The stroke is a
    // pure function of one float that both the owner's cap and every peer's mirror of it call, and
    // it had to be free of BepInEx's ConfigEntry and of everything else in this file so that
    // tests/GloomhavenVR.WireTests could LINK it and drive the real curve. The first version of its
    // release leg was arithmetically incapable of the overshoot its own comment claimed, and the
    // vectors exist because of that. See that file's header for what the stroke replaced (an
    // instantaneous drop with a linear 6/s decay), why the mirrored decay CONSTANT is gone with it,
    // and why the wire contract does not move.
    //
    // Nothing about it is config: it is authored feel, like the bevel width and the assembly ramp.

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
        // [ButtonColors] ClusterCapTintR/G/B went with the group they tinted (2026-08-25): they
        // multiplied the round-phase cluster caps' faces, and the only one of those ever drawn was
        // the turn-flow SKIP. It is a generic board keycap now and takes BoardCapTint with its two
        // siblings, so a surviving cluster tint would have been a debug-menu dial that changes
        // nothing — the failure mode this file's own category split exists to prevent.
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
        Hook(RestCapTintR); Hook(RestCapTintG); Hook(RestCapTintB);
        Hook(AnimEnable); Hook(AnimAppearParticles); Hook(AnimDissolveDuration); Hook(AnimAppearDuration);
    }

    /// <summary>
    /// ONE-TIME legacy migration (pre-split cfg → per-category sections). The old file had
    /// [TransientButtons] OffsetX/OffsetY/Shape/CapSize plus a SHARED [SquareCaps]
    /// Width/Height/Depth/Travel where 0 = "authored default" ("Auto") — and the shared
    /// entries leaked across button categories. Rules:
    /// - [TransientButtons] values are CONSUMED AND DROPPED: their only destination was
    ///   [RoundButtons], and that section was retired with the cap group it sized (2026-08-25).
    /// - A NONZERO [SquareCaps] value is copied into EVERY surviving category it used to affect
    ///   (BoardButtons + BoardDashboard) so the user's current look is preserved exactly; the
    ///   categories are then independently adjustable.
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

        // The four [TransientButtons] entries (OffsetX/OffsetY/Shape/CapSize) had exactly ONE
        // destination — [RoundButtons] — and that section is retired, so they are consumed and
        // dropped rather than copied anywhere. Consuming them is still the point of this pass: it is
        // what removes them from the cfg. The SHARED [SquareCaps] geometry is unaffected; it always
        // fanned out to several categories and still reaches the two that survive.
        Copy("Width", width.Value, BoardWidth, DashPinWidth);
        Copy("Height", height.Value, BoardHeight, DashHeight);
        Copy("Depth", depth.Value, BoardDepth, DashDepth);
        Copy("Travel", travel.Value, BoardTravel, DashTravel);

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
                              "per-category sections (BoardButtons/BoardDashboard; the old " +
                              "[TransientButtons] group was retired with its cap and is dropped) — " +
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

    /// <summary>[BoardButtons] cap width (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapWidth => Clamped(BoardWidth, DefaultBoardWidth, 0.02f, 0.20f);

    /// <summary>[BoardButtons] cap height (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapHeight => Clamped(BoardHeight, DefaultBoardHeight, 0.015f, 0.20f);

    /// <summary>[BoardButtons] cap depth (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapDepth => Clamped(BoardDepth, DefaultBoardDepth, 0.006f, 0.08f);

    /// <summary>[BoardButtons] press travel (Confirm/Undo keycaps ONLY).</summary>
    internal static float BoardCapTravel => Clamped(BoardTravel, DefaultBoardTravel, 0.002f, 0.02f);

    /// <summary>[BoardDashboard] follow/pin ('Fixiert') plate width (that toggle ONLY).</summary>
    internal static float DashboardPinWidth => Clamped(DashPinWidth, DefaultPinWidth, 0.02f, 0.20f);

    /// <summary>[BoardDashboard] follow/pin plate height.</summary>
    internal static float DashboardHeight => Clamped(DashHeight, DefaultDashHeight, 0.015f, 0.20f);

    /// <summary>[BoardDashboard] follow/pin plate depth.</summary>
    internal static float DashboardDepth => Clamped(DashDepth, DefaultDashDepth, 0.006f, 0.08f);

    /// <summary>[BoardDashboard] follow/pin press travel.</summary>
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

    // THE PRE-BIND FALLBACK OF ALL FOUR IS THE SHIPPED VALUE, NOT WHITE. It used to be white (an
    // identity multiply), on the reasonable-sounding note "default white = unchanged" — but the
    // shipped [ButtonColors] *CapTint entries were REBASED to 0.5 (scripts/rebase-defaults.py), so
    // the accessor answered 0.5 after Bind and 1.0 before it: two different pictures for the same
    // untuned player, differing by a factor of two in brightness.
    //
    // Locally that was hidden — every cap builder calls Bind() first — but it stopped being
    // harmless the moment these accessors became WIRE INPUT (extension record 28 ids 50..53).
    // BoardTuningSampler emits a field whenever the live value differs from the shipped one, and a
    // packet can go out during scene load: a completely untuned player would have started
    // broadcasting "my cap tint is white" for as long as Bind had not run, which is exactly the
    // "an untuned player emits the bytes the previous build emitted" guarantee the whole record is
    // built on. The fallback and the bind default now name the same entries, so pre-Bind and
    // post-Bind are the same colour and the sampler stays silent.

    /// <summary>Confirm/Undo keycap FACE tint (multiplier over the state palette; shipped 0.5).</summary>
    internal static Color BoardCapTint =>
        Tint3(BoardCapTintR, BoardCapTintG, BoardCapTintB,
              Defaults.BoardCapTintR, Defaults.BoardCapTintG, Defaults.BoardCapTintB);

    /// <summary>Fixiert (follow/pin) plate FACE tint.</summary>
    internal static Color DashCapTint =>
        Tint3(DashCapTintR, DashCapTintG, DashCapTintB,
              Defaults.DashCapTintR, Defaults.DashCapTintG, Defaults.DashCapTintB);

    /// <summary>Short/long REST keycap FACE tint.</summary>
    internal static Color RestCapTint =>
        Tint3(RestCapTintR, RestCapTintG, RestCapTintB,
              Defaults.RestCapTintR, Defaults.RestCapTintG, Defaults.RestCapTintB);

    /// <summary>Cap-face TINT for a category (default white = identity multiply until edited).</summary>
    internal static Color CapTint(CapCategory category) => category switch
    {
        CapCategory.Board => BoardCapTint,
        CapCategory.Dashboard => DashCapTint,
        _ => RestCapTint,
    };

    /// <summary>Three [ButtonColors] channels as one opaque tint, each falling back to its own
    /// SHIPPED default before <c>Bind</c> — see the block above for why the fallback may not be a
    /// blanket white. ALPHA IS ALWAYS 1: no [ButtonColors] entry has an A channel, which is what
    /// lets the wire carry these as 3 bytes (NetProtocol.TuneColorIdMin).</summary>
    private static Color Tint3(ConfigEntry<float>? r, ConfigEntry<float>? g, ConfigEntry<float>? b,
                               float defR, float defG, float defB) => new(
        Clamped(r, defR, 0f, 1f), Clamped(g, defG, 0f, 1f), Clamped(b, defB, 0f, 1f), 1f);

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

    // ---- THE AUTHORED "ASSEMBLES OUT OF DUST" SURFACE RAMP --------------------------------
    //
    // USER REPORT 2026-08-09, second round: "Auch kam beim Test in diesem Zustand der Fall dass
    // die Buttons die wegen dem Gegenstand noch kommen: 'Ziele bestätigen' und 'Rückgängig machen'
    // teilweise unsichtbar waren und nur der Text lesbar war." (He had reported the same shape once
    // before: "die Buttons … waren unsichtbar - nur der Text auf den Buttons war noch zu sehen".)
    //
    // WHAT THE OLD RAMP DID, AND WHY IT COULD NOT BE MADE TO WORK. The appear multiplied the cap's
    // state colour by `Mathf.SmoothStep(0.15f, 1f, k)` — it faded the cap toward BLACK and back.
    // ModBuild 94 shipped diagnostics for the two competing explanations and the fresh hardware log
    // answered both, in the negative: "KEYCAP FADE HEALED" 0 hits (no stranded/stalled fade) and
    // "KEYCAP MATERIAL" 0 hits (no unresolved material) — while the per-cap diag reports a perfectly
    // healthy cap ("shader 'GloomhavenVR/BoardLit', renderQueue 2000. OPAQUE: YES"). What is left is
    // the ramp itself, doing exactly what it was written to do. This player's [ButtonColors]
    // BoardCapTint is 0.5, so the sage CONFIRM top (0.35,0.46,0.28) starts every appear at
    // 0.5 × 0.15 = (0.026,0.035,0.021) — black on a black board — while the TMP label is a SEPARATE
    // renderer the ramp never touches and keeps drawing at full parchment. "Button invisible, only
    // the text readable" is the literal rendered result of a cap in the first frames of that ramp.
    // And it is not rare: the same session's log carries 33 keycap builds, each re-running it.
    //
    // WHY NOT SIMPLY RAISE THE 0.15 FLOOR. Because a floor is a number that has to be re-guessed for
    // every cap colour AND every user tint — and the tint is a config the player owns. 0.15 was
    // "clearly dim but visible" at the shipped tint and is invisible at 0.5; any replacement fails at
    // some other tint, and we would be back here. The AXIS was wrong, not the number: multiplying
    // toward black is the one direction that can pass through "not there" on a dark board, and mixed
    // reality makes it worse still — behind a passthrough-lit cap the background is the player's real
    // room, so a black cap reads as a hole rather than as a dim button.
    //
    // WHAT IT DOES INSTEAD. The cap ASSEMBLES. It starts as the warm parchment/brass DUST it is being
    // built out of (the same powder ButtonDissolveFx sprays) and COOLS INTO its own state colour,
    // with the BEVEL RING leading: the bright 45° catch-light chamfer that is the whole "this key is
    // raised" cue (BoardButton.BevelHighlight, and the log's own "BRIGHT lit bevel ring") lands
    // first, the top plateau fills in behind it, the side walls settle last. The dissolve runs the
    // identical curve backwards — walls crumble first, the brass frame is the last thing to go — so
    // the pair still reads as one matched crumble/assemble, which is what the user asked for
    // originally ("emerge from dust, matched to the crumble").
    //
    // THE INVARIANT THAT MAKES IT TINT-PROOF. <see cref="AssemblyColor"/> never renders a cap DARKER
    // THAN ITS OWN REST COLOUR in any channel: the hot end is a per-channel MAX of the rest colour and
    // the dust pull. So whatever the player dials BoardCapTint / RestCapTint / DashCapTint to, an
    // assembling cap is at least as visible as the settled cap they configured — and the settled cap
    // is by definition the look they chose. The ANIMATION is the dust; the BUTTON is theirs again the
    // moment it lands. There is no floor left to re-guess, at any tint, in either lighting.
    //
    // ONE HOME, TWO CONSUMERS. <c>Cards.PlayTray.BoardButton</c> (every keycap on the board, the
    // turn-flow SKIP included since it joined that family) and <c>Net.RemoteCapFx</c>
    // (a peer's mirrored board) both call these two methods, so the MULTIPLAYER 1:1 rule holds by
    // construction rather than by a lint: one recipe, nothing to drift. This deliberately RETIRES the
    // AppearFadeFloor mirror pair that scripts/check-mirrors.sh described in prose — the same
    // resolution DecisionDockSurface.BarClearanceMeters got, and the one that file itself recommends:
    // delete the second copy instead of linting it.

    /// <summary>Which face of a beveled keycap the assembly ramp is painting. The three are
    /// STAGGERED so the cap builds itself edge-first; a single-material cap (round puck, native
    /// sprite face, cluster cap) uses <see cref="CapPart.Top"/>.</summary>
    internal enum CapPart
    {
        /// <summary>The lit 45° chamfer ring — leads the assembly (it is the "raised" cue).</summary>
        Bevel,

        /// <summary>The state-coloured top plateau — follows the bevel.</summary>
        Top,

        /// <summary>The dark warm side walls — settle last.</summary>
        Wall,
    }

    /// <summary>Fraction of the animation over which the BEVEL ring completes. Under 1 because it
    /// LEADS: the frame is already solid brass while the faces are still dust.</summary>
    private const float AssemblyBevelSpan = 0.55f;

    /// <summary>Fraction of the animation the TOP plateau waits before it starts to solidify.</summary>
    private const float AssemblyTopDelay = 0.12f;

    /// <summary>Fraction of the animation the side WALLS wait before they start (they settle last,
    /// so the cap's silhouette fills in from its lit edge inward).</summary>
    private const float AssemblyWallDelay = 0.30f;

    /// <summary>
    /// The dust a keycap assembles out of and crumbles into: the board's own warm parchment-brass
    /// powder (in key with <c>BoardButton.BevelHighlight</c> and the engraved parchment labels), and
    /// bright enough to read against BOTH the near-black board and mixed-reality passthrough, where
    /// the backdrop is whatever room the player is sitting in.
    /// </summary>
    internal static readonly Color AssemblyDust = new(0.88f, 0.76f, 0.52f);

    /// <summary>How far a cap's colour is pulled toward <see cref="AssemblyDust"/> at the very start
    /// of the assembly (1 = anonymous flash of pure dust). Held under 1 on purpose so a strongly
    /// tinted or accented cap still carries its own hue through the dust — the player can tell WHICH
    /// button is arriving before it has finished arriving.</summary>
    private const float AssemblyDustLerp = 0.82f;

    /// <summary>
    /// This face's own 0..1 progress at overall animation progress <paramref name="k"/> (0 = pure
    /// dust, 1 = solid material), including the bevel-leads-walls-trail stagger and the eased
    /// in/out. Feed the result to <see cref="AssemblyColor"/>.
    /// </summary>
    internal static float AssemblyPhase(float k, CapPart part)
    {
        k = Mathf.Clamp01(k);
        float phase = part switch
        {
            CapPart.Bevel => k / AssemblyBevelSpan,
            CapPart.Wall => (k - AssemblyWallDelay) / (1f - AssemblyWallDelay),
            _ => (k - AssemblyTopDelay) / (1f - AssemblyTopDelay),
        };
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase));
    }

    /// <summary>
    /// The colour a face wears at <paramref name="phase"/> (from <see cref="AssemblyPhase"/>) on its
    /// way to / from its settled <paramref name="rest"/> colour.
    ///
    /// <para>THE HOT END IS A PER-CHANNEL MAX — that is the whole tint-proofing (see the block
    /// comment above). Pulling toward the dust can only ever ADD light to a channel, never remove it,
    /// so no user tint, no accent and no disabled state can put an assembling cap below the
    /// brightness of the cap the player already accepted as visible. Alpha is passed through
    /// untouched: every keycap material is OPAQUE (BoardLit, queue 2000) and this ramp must not be
    /// the thing that starts pretending otherwise.</para>
    /// </summary>
    // ---- THE TWIN INVARIANT: A CAP'S *REST* COLOUR IS SEATED TOO -----------------------------
    //
    // USER REPORT 2026-08-09, THIRD OCCURRENCE: "Ich hatte in Tests wieder die Situation, dass die
    // buttons unsichtbar waren und nur der text darauf sichtbar."
    //
    // The two rounds before this one both fixed the ANIMATION, and both fixed it correctly: round 1
    // gave the fade an unscaled clock and a wall-clock deadline, round 2 replaced the
    // multiply-toward-black ramp with <see cref="AssemblyColor"/>'s per-channel MAX, whose invariant
    // is "an assembling cap is never darker than its own REST colour". ModBuild 102's hardware log
    // proves neither mechanism was in play when the user hit it again: ZERO "KEYCAP FADE HEALED"
    // lines and ZERO "KEYCAP MATERIAL MISSING/HEALED" lines in the whole session.
    //
    // The hole both rounds left is one word wide. They made the ANIMATION safe RELATIVE TO the rest
    // colour, and never asked whether the REST COLOUR ITSELF is visible. It is not, and the same log
    // states the numbers outright — the build-time cap diag of the very session the user reported on:
    //
    //     ITEMA cap diag — Confirm: … top RGBA(0.105, 0.080, 0.055), wall RGBA(0.102, 0.069, 0.041)
    //
    // against a WELL (the recessed base plate this same widget paints two millimetres behind the cap,
    // <see cref="CapWellColor"/>) of (0.15, 0.12, 0.08). The cap face is DARKER THAN THE HOLE IT SITS
    // IN, in every channel. There is nothing left to see: no silhouette, no value step, no edge — a
    // hole in the board with a bright parchment label floating over it. That is the report, verbatim,
    // and it is a STEADY STATE, not a race, which is why it has survived three builds and why the
    // player sees it "come back after a while" (the state flips to enabled and the cap lights up).
    //
    // WHY IT NEEDS NO ANIMATION TO HAPPEN. The cap palette is authored bright enough
    // (BoardButton.DisabledColor 0.21/0.16/0.11, the cluster's dark-wood lerp 0.17/0.13/0.09) — and
    // then MULTIPLIED by the player's own [ButtonColors] face tint, which the well is NOT. At this
    // user's tint of 0.5 the disabled face lands at 0.105 against a 0.15 well. The threshold is pure
    // arithmetic: 0.21 × t < 0.15 for any t < 0.714. Every tint the config invites the player to dial
    // ("lower = darker/less red, so white text reads") crosses it long before the slider runs out.
    //
    // THE FIX IS THE SAME SHAPE AS AssemblyColor's, ONE LEVEL DOWN. A cap face is never rendered
    // darker than the well it is seated in, times a single contrast step. The reference is not a
    // guessed brightness — it is the colour of the surface the cap is physically sitting in, which
    // this mod paints itself and which is the same in all four builders. That makes the floor
    // tint-proof (it does not move when the player moves the tint), MR-proof (a cap that beats its
    // own well beats passthrough for the same reason the well does) and, unlike the AppearFadeFloor
    // that round 2 rightly deleted, there is nothing here to re-guess per colour or per user.
    //
    // ONE HOME, THREE CONSUMERS — the same rule <see cref="AssemblyColor"/> already lives by, so the
    // MULTIPLAYER 1:1 requirement holds by construction: Cards.PlayTray.BoardButton.StateColor,
    // ButtonCluster.PhysicalButton.SetState and Net.RemoteBoardFurniture.InertCap.SetTint all end
    // their colour derivation with this call.

    /// <summary>
    /// The recessed WELL / base plate every mod keycap is seated in — a solid warm dark wood, so
    /// the recess around the cap reads as part of the physical panel rather than a hole under a
    /// glassy key. Deliberately NOT multiplied by any [ButtonColors] face tint: the tint is a
    /// property of the cap FACE (it exists so white label text reads over it), and the well is the
    /// fixed surface that face has to stand out from.
    ///
    /// <para>This used to be the literal <c>new Color(0.15f, 0.12f, 0.08f)</c> in four separate
    /// builders (the local board keycap, the local cluster cap and both mirrored copies). It is one
    /// constant now because <see cref="SeatedCapColor"/> measures against it — a floor and its
    /// reference drifting apart is exactly the failure this whole block exists to end.</para>
    /// </summary>
    internal static readonly Color CapWellColor = new(0.15f, 0.12f, 0.08f);

    /// <summary>
    /// How much brighter than its own <see cref="CapWellColor"/> a cap FACE must render before it
    /// stops reading as a hole and starts reading as a raised key.
    ///
    /// <para>NOT A GUESS — it is READ OFF THE SHIPPED PALETTE. The darkest colour this mod ever
    /// authored for a cap face is <c>BoardButton.DisabledColor</c> (0.21, 0.16, 0.11), and against
    /// the well (0.15, 0.12, 0.08) that is a per-channel ratio of 1.40 / 1.33 / 1.38 — mean 1.37.
    /// So the author of the palette already decided how much a cap has to beat its recess by; this
    /// constant just states it, one hair under the mean so the shipped disabled look at the default
    /// tint comes back unchanged to within 0.002 of one channel. It is a ratio against a real
    /// surface rather than a fraction of a state colour, which is precisely why it does not have to
    /// be re-guessed for another palette entry, another accent or another user tint — the failure
    /// mode that got the AppearFadeFloor deleted in round 2.</para>
    /// </summary>
    private const float CapSeatContrast = 1.35f;

    /// <summary>
    /// Floor a cap FACE colour so it can never render darker than the well it is seated in (see the
    /// block comment above). Per-channel <c>Max</c>, exactly like <see cref="AssemblyColor"/>'s hot
    /// end, so it can only ever ADD light — no state, no accent and no user tint can push a cap
    /// below the surface it sits on. Alpha is passed through untouched: every keycap material is
    /// opaque (BoardLit, queue 2000) and this must not be the thing that starts pretending otherwise.
    ///
    /// <para>Above the floor this is the identity, so every look the player actually configured —
    /// idle, accent, confirmed, the whole enabled palette — is returned bit-for-bit unchanged. Only
    /// the sunk-below-the-board case moves, which is the case that has no pixels.</para>
    /// </summary>
    internal static Color SeatedCapColor(Color face) => new(
        Mathf.Max(face.r, CapWellColor.r * CapSeatContrast),
        Mathf.Max(face.g, CapWellColor.g * CapSeatContrast),
        Mathf.Max(face.b, CapWellColor.b * CapSeatContrast),
        face.a);

    /// <summary>True when <see cref="SeatedCapColor"/> would actually lift <paramref name="face"/>,
    /// i.e. the cap WOULD have rendered darker than its own well. Diagnostics only — it is what lets
    /// the keycap surface log say "this cap was about to be invisible" instead of printing three
    /// colour triples and leaving the reader to do the comparison.</summary>
    internal static bool CapSeatFloorEngages(Color face) =>
        face.r < CapWellColor.r * CapSeatContrast
        || face.g < CapWellColor.g * CapSeatContrast
        || face.b < CapWellColor.b * CapSeatContrast;

    internal static Color AssemblyColor(Color rest, float phase)
    {
        if (phase >= 1f)
            return rest;
        var hot = new Color(
            Mathf.Max(rest.r, Mathf.Lerp(rest.r, AssemblyDust.r, AssemblyDustLerp)),
            Mathf.Max(rest.g, Mathf.Lerp(rest.g, AssemblyDust.g, AssemblyDustLerp)),
            Mathf.Max(rest.b, Mathf.Lerp(rest.b, AssemblyDust.b, AssemblyDustLerp)),
            rest.a);
        Color c = Color.Lerp(hot, rest, Mathf.Clamp01(phase));
        c.a = rest.a;
        return c;
    }

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
        return $"board W/H/D {BoardCapWidth:F3}/{BoardCapHeight:F3}/{BoardCapDepth:F3} m, " +
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
