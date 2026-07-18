using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Which controller button grabs a card (Demeo grabs with the index/trigger; test-#22 Demeo-parity pass).</summary>
internal enum CardGrabButton
{
    Grip,
    Trigger,
}

/// <summary>
/// Which control-board (PlayTray) prefab is loaded from the asset bundle. Switchable
/// live from the VR settings panel; the bundle asset paths are mapped in
/// <see cref="VRCardFactory.GetTrayPrefab"/>. Oak is the original bundled board (default);
/// a selected prefab that is not yet in the bundle falls back to Oak, then to the
/// procedural board, so this compiles and runs before the new bundle ships.
/// </summary>
internal enum ControlBoard
{
    Oak,
    Steel,
    Bronze,
}

/// <summary>
/// Phase-3b config. Plugin.cs is frozen shared surface, so the Cards module binds its
/// own ConfigFile (<c>BepInEx/config/dev.gloomhavenvr.cards.cfg</c>) instead of adding
/// entries to the main plugin config.
/// </summary>
internal static class CardsConfig
{
    private static ConfigFile? _file;

    /// <summary>Spawn N dummy VR cards (procedural face) without a scenario — exercises fan/tray/grab under [Dev] SimulateHands.</summary>
    internal static ConfigEntry<int> DevFakeHand = null!;

    /// <summary>How the palm fan reveals: "tilt" (Demeo palm-flip gate) or "always" (open whenever cards exist).</summary>
    internal static ConfigEntry<string> RevealMode = null!;

    /// <summary>Supination (roll-axis) enter value for RevealMode=tilt (higher = more deliberate roll).</summary>
    internal static ConfigEntry<float> SupinationThreshold = null!;

    /// <summary>Fan arc radius in real meters (diorama scale applied automatically).</summary>
    internal static ConfigEntry<float> FanRadius = null!;

    /// <summary>Max total fan arc in degrees.</summary>
    internal static ConfigEntry<float> FanArcDegrees = null!;

    /// <summary>Height of the fan pivot above the palm, real meters.</summary>
    internal static ConfigEntry<float> FanPalmOffset = null!;

    /// <summary>Card width in real meters (poker card = 0.0635); height follows 63.5:88 aspect.</summary>
    internal static ConfigEntry<float> CardWidth = null!;

    /// <summary>Scale factor applied to a card while grabbed/inspected.</summary>
    internal static ConfigEntry<float> InspectScale = null!;

    /// <summary>LEGACY (superseded by <see cref="HeldFaceBias"/>): old palm-aligned face tilt, unused.</summary>
    internal static ConfigEntry<float> HeldTiltDegrees = null!;

    /// <summary>Held card: face-normal lean from "out of the palm" back toward the wrist/eyes, degrees.</summary>
    internal static ConfigEntry<float> HeldFaceBias = null!;

    /// <summary>Held card: FALLBACK pinch-point offset along the fingers, real meters (no finger joints).</summary>
    internal static ConfigEntry<float> HeldForward = null!;

    /// <summary>Held card: FALLBACK pinch-point offset off the palm surface, real meters (no finger joints).</summary>
    internal static ConfigEntry<float> HeldOffPalm = null!;

    /// <summary>Held card: fine-tune offset added to the thumb/index pinch point, GrabAnchor-local meters.</summary>
    internal static ConfigEntry<Vector3> HeldPinchOffset = null!;

    /// <summary>Tray placement offset from the head, real meters: forward distance.</summary>
    internal static ConfigEntry<float> TrayForward = null!;

    /// <summary>Tray placement offset from the head, real meters: drop below eye height.</summary>
    internal static ConfigEntry<float> TrayDown = null!;

    /// <summary>Tray placement offset, real meters: sideways (+right).</summary>
    internal static ConfigEntry<float> TrayRight = null!;

    /// <summary>Tray tilt toward the player in degrees (0 = flat).</summary>
    internal static ConfigEntry<float> TrayTilt = null!;

    /// <summary>Tray yaw relative to the head's flat forward at placement, degrees (written by the tray grab).</summary>
    internal static ConfigEntry<float> TrayYaw = null!;

    /// <summary>Tray size multiplier (two-handed tray grab), clamped 0.5–2.</summary>
    internal static ConfigEntry<float> TrayScale = null!;

    /// <summary>Tray anchor mode (test #15): true = follows the player (rig-anchored), false = static in world.</summary>
    internal static ConfigEntry<bool> TrayFollow = null!;

    /// <summary>Card seating depth in the physical slot recesses, real meters toward the viewer (−z). Test #28.</summary>
    internal static ConfigEntry<float> SlotCardInset = null!;

    /// <summary>Steady "wanted slot" hint glow on the slot(s) the game is waiting to be filled (test #28).</summary>
    internal static ConfigEntry<bool> WantedSlotHint = null!;

    /// <summary>Item 3: multiplier that scales a slotted card UP to (nearly) fill the physical slot recess.</summary>
    internal static ConfigEntry<float> SlotCardFill = null!;

    /// <summary>Feature 6a: diameter (real meters) of the ROUND rest-notch buttons (short/long rest discs).</summary>
    internal static ConfigEntry<float> RoundButtonDiameter = null!;

    /// <summary>Feature 6a: thickness (real meters) of the round rest-button pressable puck.</summary>
    internal static ConfigEntry<float> RoundButtonThickness = null!;

    /// <summary>Item 2 (Oak-tuned): inward local-X nudge (real meters) centering the round rest discs in the notches.</summary>
    internal static ConfigEntry<float> RestButtonInsetX = null!;

    /// <summary>Item 3 (Oak-tuned): side length (real meters) of the square Confirm/Undo buttons fitting the metal pads.</summary>
    internal static ConfigEntry<float> ConfirmUndoSize = null!;

    /// <summary>Item 3 (Oak-tuned): inward local-X nudge (real meters) centering Confirm/Undo on the metal pads.</summary>
    internal static ConfigEntry<float> ConfirmUndoInsetX = null!;

    /// <summary>Animation speed for cards flying between fan/tray/half layout (1/s, exponential smoothing).</summary>
    internal static ConfigEntry<float> CardLerpSpeed = null!;

    /// <summary>Discard/burnt pile stacks on the control board + the browse fan (hardware test #21 wish).</summary>
    internal static ConfigEntry<bool> PileViewer = null!;

    /// <summary>ACTIVE CARDS area on the control board's right edge (feature 6): the currently-active ability cards, permanently shown + grabbable.</summary>
    internal static ConfigEntry<bool> ActivePile = null!;

    /// <summary>Which control-board prefab is loaded (Oak = the original bundled board; Steel/Bronze are new). Switchable live.</summary>
    internal static ConfigEntry<ControlBoard> Board = null!;

    // ---- Demeo-parity fan/grab tuning (test #22 blueprint DEMEO-HANDS-CARDS.md) ----

    /// <summary>G1: scale fan arch + per-card tilt by how full the hand is (Demeo CardHandView.cs:814).</summary>
    internal static ConfigEntry<bool> FanCurveByFill = null!;

    /// <summary>G1: hand size at which the fan reaches its full curvature (fill = n / this, clamped 0..1).</summary>
    internal static ConfigEntry<int> FanMaxHandForCurve = null!;

    /// <summary>G1: full-curvature vertical-arch factor (the pre-Demeo constant curvature, now the fill=1 target).</summary>
    internal static ConfigEntry<float> FanFlatCurvatureFactor = null!;

    /// <summary>G1: full-curvature per-card Z-tilt factor (fill=1 target).</summary>
    internal static ConfigEntry<float> FanTiltFactor = null!;

    /// <summary>G2: sideways slide (real meters) applied to the fan's cards to split apart around the hovered one.</summary>
    internal static ConfigEntry<float> FanSplitMultiplier = null!;

    /// <summary>G2: how fast the neighbor split decays with index distance from the hovered card (higher = only nearest neighbors move). Coded-curve substitute for Demeo's serialized AnimationCurve.</summary>
    internal static ConfigEntry<float> FanSplitFalloff = null!;

    /// <summary>G2: forward pop (real meters, along -face-normal toward the viewer) of the hovered/selected card.</summary>
    internal static ConfigEntry<float> FanSelectedPopForward = null!;

    /// <summary>G3: which controller button grabs a card. Demeo = Trigger (index); our legacy = Grip.</summary>
    internal static ConfigEntry<CardGrabButton> GrabButton = null!;

    /// <summary>G4: eased dead-zoned fan follow rate (1/s exponential). 0 = rigidly parented to the palm (pre-Demeo). >0 = Demeo-style eased follow.</summary>
    internal static ConfigEntry<float> FanFollowSmoothing = null!;

    /// <summary>G4: fan-follow dead zone in real meters — the fan only chases the palm once it drifts past this (Demeo ViewHelper minDistanceToMove).</summary>
    internal static ConfigEntry<float> FanFollowDeadzone = null!;

    /// <summary>G5: reveal preset — "generous" (our deliberate P6 default, test #10) or "demeo" (tighter 53° supination cone).</summary>
    internal static ConfigEntry<string> RevealPreset = null!;

    /// <summary>G5: suppress the reveal gate on the hand that is currently grabbing something (Demeo CardHandController.cs:475).</summary>
    internal static ConfigEntry<bool> RevealIgnoreWhenGrabbing = null!;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = new ConfigFile(Path.Combine(Paths.ConfigPath, "dev.gloomhavenvr.cards.cfg"), true);

        DevFakeHand = _file.Bind("Cards", "DevFakeHand", 0,
            "Spawn this many dummy VR cards (procedural placeholder faces) so the fan/tray/grab " +
            "mechanics are exercisable without a scenario. Requires [Dev] Enabled (+ SimulateHands " +
            "or a real HMD). 0 = off.");
        RevealMode = _file.Bind("Cards", "RevealMode", "tilt",
            "How the palm fan reveals. 'tilt' = Demeo-style wrist SUPINATION on the non-dominant " +
            "hand: turning the palm up / toward you, measured on the ROLL axis alone (pitching or " +
            "pointing the arm has no effect — hardware test #10). 'always' = the fan is out " +
            "whenever a card phase has cards, no gesture at all.");
        SupinationThreshold = _file.Bind("Cards", "SupinationThreshold", 0.2f,
            "RevealMode=tilt: supination amount above which the fan opens. Scale: -1 palm fully " +
            "down, 0 palm vertical (thumb up), +1 palm fully up/toward the face. 0.2 = roll just " +
            "past vertical. Closes again below (SupinationThreshold - 0.35) — wide hysteresis, " +
            "the fan never flickers at the boundary.");
        FanRadius = _file.Bind("Cards", "FanRadius", 0.16f,
            "Palm fan arc radius in real-world meters (diorama scale is applied automatically).");
        FanArcDegrees = _file.Bind("Cards", "FanArcDegrees", 70f,
            "Maximum total fan arc in degrees (cards overlap more as the hand grows).");
        FanPalmOffset = _file.Bind("Cards", "FanPalmOffset", 0.09f,
            "Height of the fan pivot above the palm center, real-world meters.");
        CardWidth = _file.Bind("Cards", "CardWidth", 0.0635f,
            "Physical card width in meters (real poker card = 0.0635). Height keeps the 63.5:88 aspect.");
        InspectScale = _file.Bind("Cards", "InspectScale", 1.6f,
            "Scale multiplier applied to a card while it is held (natural-size inspection).");
        HeldTiltDegrees = _file.Bind("Cards", "HeldTiltDegrees", 20f,
            "LEGACY — no longer used (hardware test #13). The old palm-aligned held pose " +
            "required a hard supination to read the card; the pose is now controlled by " +
            "HeldFaceBias instead. Kept only so existing config files load cleanly.");
        HeldFaceBias = _file.Bind("Cards", "HeldFaceBias", 65f,
            "Held card readability (test #13): degrees the card FACE leans from 'flat on the " +
            "palm' (0 = old pose, face along the palm normal — readable only by twisting the " +
            "wrist) back toward the wrist/forearm. In a relaxed controller grip (grip pose " +
            "pitched, see [Hands] GripPitchOffsetDegrees) the fingers point forward " +
            "and slightly down, so at ~65 the face points up/back at your eyes — like really " +
            "holding a playing card. The card top points to the thumb side (which is world-up " +
            "in a relaxed grip; mirrored automatically for the left hand). The card still " +
            "follows the wrist 1:1 — this is a fixed bias, NOT per-frame auto-facing.");
        HeldForward = _file.Bind("Cards", "HeldForward", 0.02f,
            "Held card FALLBACK (only used when the hand rig has no finger joints): " +
            "pinch-point offset from the grab anchor along the fingers, meters.");
        HeldOffPalm = _file.Bind("Cards", "HeldOffPalm", 0.015f,
            "Held card FALLBACK (only used when the hand rig has no finger joints): " +
            "pinch-point offset off the palm surface, meters.");
        HeldPinchOffset = _file.Bind("Cards", "HeldPinchOffset", Vector3.zero,
            "Held card fine-tune: offset (meters) ADDED to the computed pinch point — " +
            "the midpoint between the thumb tip and index tip at grab time — in " +
            "GrabAnchor-local axes: +Y out of the palm, +Z along the fingers, +X " +
            "sideways (anatomically mirrored between hands). Example {x:0, y:0.01, " +
            "z:0.02} lifts the card 1 cm off the palm and shifts it 2 cm toward the " +
            "fingertips.");
        TrayForward = _file.Bind("Cards", "TrayForward", 0.45f,
            "Control board placement: forward distance from the head at placement time, meters.");
        TrayDown = _file.Bind("Cards", "TrayDown", 0.35f,
            "Control board placement: drop below eye height, meters (0.35 ~ chest height).");
        TrayRight = _file.Bind("Cards", "TrayRight", 0.0f,
            "Control board placement: sideways offset (+right), meters.");
        TrayTilt = _file.Bind("Cards", "TrayTilt", 30f,
            "Control board tilt, degrees FROM HORIZONTAL toward the player: 0 = flat like a " +
            "desk, 90 = upright panel. 30 reads like a card-table edge / lectern.");
        TrayYaw = _file.Bind("Cards", "TrayYaw", 0f,
            "Control board yaw relative to the head's flat forward at placement time, degrees. " +
            "Written automatically when you grip-move the tray by its handle bar; edit only to reset.");
        TrayScale = _file.Bind("Cards", "TrayScale", 1f,
            "Control board size multiplier (0.5–2). Written automatically by the two-handed " +
            "tray grab (grip the handle bar with both hands and spread/pinch); edit only to reset.");
        TrayFollow = _file.Bind("Cards", "TrayFollow", true,
            "Tray anchor mode (test #15, toggled by the pin button on the tray frame). " +
            "true = the tray is rig-anchored: it moves with you (world grab, snap turn, " +
            "recenter) and re-places itself at the TrayForward/Down/Right offsets on mode " +
            "entry. false = the tray is PINNED where you left it, world-anchored — it " +
            "stays put while you move around and never re-places itself. Switching back " +
            "to follow re-anchors it at the configured offsets.");
        CardLerpSpeed = _file.Bind("Cards", "CardLerpSpeed", 14f,
            "Card fly animation speed (exponential smoothing constant, 1/s).");
        SlotCardInset = _file.Bind("Cards", "SlotCardInset", 0.024f,
            "How far a card is lifted OUT of a physical slot recess toward the viewer, real meters " +
            "(the board's -Z face). BuildBoard now projects the slot anchors onto the recess FLOOR, so " +
            "a card at 0 sits deep inside the recess and reads as 'poking through' — barely visible from " +
            "the top. This lifts it up to (roughly) the recess rim so it rests visibly ON the board's top " +
            "surface facing the player. Raise it if cards still look sunken, lower it if they float. Applies to played " +
            "cards, docked action cards and the single-card pick/short-rest layouts alike. Does NOT " +
            "change the card width/height (a separate pass aligns the recess to the card).");
        SlotCardFill = _file.Bind("Cards", "SlotCardFill", 1.45f,
            "Item 3: how much a card laid in a board slot scales UP to fill the physical slot " +
            "recess. Multiplies the card's in-slot size (on top of the slot frame's own 1.3x " +
            "SlotScale). 1.0 = the pre-fix size (visibly smaller than the recess). PER-BOARD: " +
            "raise toward the recess/card ratio of the ACTIVE control-board asset until the card " +
            "nearly fills the recess without overflowing the rim; the default is tuned for the " +
            "current bundled PlayTray. Applies to played cards, single-card pick candidates and " +
            "docked action cards alike. Does NOT change the recess or the card's slot seating depth " +
            "(that is SlotCardInset).");
        RoundButtonDiameter = _file.Bind("Cards", "RoundButtonDiameter", 0.105f,
            "Feature 6a / item 2: diameter (real meters) of the ROUND short-rest / long-rest " +
            "buttons that seat in the control board's two round rest-notches. Raised to 0.105 " +
            "(from 0.096) — the user wanted the rest discs BIGGER so they fill the round notches. " +
            "Dial this in from a hardware test until the discs drop cleanly into the notches without " +
            "overhanging the rim — NO baked notch dimension exists in code, so this is the fit knob. " +
            "PER-BOARD: the two upcoming control boards have differently sized notches; this default " +
            "is Oak-tuned and a future per-board descriptor will override it (see RestControls.EnsureBuilt).");
        RoundButtonThickness = _file.Bind("Cards", "RoundButtonThickness", 0.012f,
            "Feature 6a: thickness (real meters) of the round rest-button pressable puck " +
            "along the press axis. Higher = a chunkier disc that stands prouder of the notch " +
            "floor; the puck still travels the same fixed 4 mm on press. May differ per " +
            "control board (see RoundButtonDiameter).");
        RestButtonInsetX = _file.Bind("Cards", "RestButtonInsetX", 0.024f,
            "LIVE FIT KNOB (dial in dev.gloomhavenvr.cards.cfg without a rebuild): sideways nudge of " +
            "the round short/long-rest discs along the rest anchor's LOCAL X, real meters. DIRECTION: " +
            "POSITIVE = toward the board CENTER (the anchor local +X == the slot0->slot1 long axis; the " +
            "rest zone sits on the LEFT, so +X moves the discs RIGHT/inward). NEGATIVE = toward the " +
            "board EDGE, i.e. 'nach links' (further out from center) — this value MAY be negative. The " +
            "correct sign depends on the board frame, which is uncertain per board, so tune it live: if " +
            "the discs sit too far right, lower the value (into the negatives) until they drop into the " +
            "notches; too far left, raise it. Default 0.024 (Oak: bundle anchors sit ~0.02 m too far " +
            "toward the edge). PER-BOARD: a future per-board descriptor overrides this (see RestControls.EnsureBuilt).");
        ConfirmUndoSize = _file.Bind("Cards", "ConfirmUndoSize", 0.073f,
            "Item 3 (Oak-tuned): side length (real meters) of the SQUARE Confirm/Undo buttons so they " +
            "sit on the Oak board's two ~0.066 m metal button pads (were 0.115x0.06 / 0.09x0.042). " +
            "PER-BOARD: differs per control board.");
        ConfirmUndoInsetX = _file.Bind("Cards", "ConfirmUndoInsetX", 0.014f,
            "Item 3 (Oak-tuned): inward nudge in local X (real meters, toward board center) applied to " +
            "Confirm/Undo so they center on the Oak metal pads (the ButtonZone anchor X ~+0.235 sits " +
            "~0.015 m too far toward the board edge; pad center X ~+0.235... nudged in). PER-BOARD.");
        WantedSlotHint = _file.Bind("Cards", "WantedSlotHint", true,
            "Steady, softly pulsing accent glow on the slot(s) the game is currently waiting to be " +
            "filled (test #28) — distinct from the transient gold snap glow that previews where a " +
            "HELD card will drop. During normal card selection it marks the still-empty play slot(s) " +
            "the round expects a card in; during single-card pick flows (long rest lose-a-card, " +
            "avoid-damage, recover/discard) it marks the left slot. Clears once the requirement is " +
            "met or the flow ends. false = no wanted-slot hint.");
        PileViewer = _file.Bind("Cards", "PileViewer", true,
            "Discard/burnt pile stacks on the control board's right edge (hardware test #21 " +
            "wish): each pile shows as a small physical card stack with a count; poking or " +
            "pinch-grabbing a stack raises a readable browse fan of that pile's cards " +
            "(informational — release/poke again to dismiss). false = no pile furniture at all.");
        ActivePile = _file.Bind("Cards", "ActivePile", true,
            "ACTIVE CARDS area (feature 6): the character's currently-active ability cards " +
            "(round-long or persistent) shown PERMANENTLY as a small column just to the RIGHT " +
            "of the discard/burnt pile stacks. The cards read slightly smaller than the hand " +
            "fan and each stays grabbable so you can pluck one out to read it (it returns to " +
            "the column on release); the active HALF of each card is highlighted. Purely " +
            "informational — grabbing an active card never selects or commits it. Empty when " +
            "no card is active. false = no active-cards area at all.");
        Board = _file.Bind("Cards", "Board", ControlBoard.Oak,
            "Which control-board (PlayTray) model to load from the asset bundle — switchable " +
            "live from the VR settings panel. Oak = the original bundled board (default); Steel " +
            "and Bronze are the two new boards. The enum→bundle-path map lives in " +
            "VRCardFactory.GetTrayPrefab. If the selected prefab is not in the bundle yet the " +
            "board falls back to Oak, and if Oak is also missing the procedural board is used, " +
            "so any selection is safe. Changing this tears down and rebuilds the tray live " +
            "(CardsDriver), re-seating the cards on the newly loaded board.");

        // ---- Demeo-parity fan/grab tuning (test #22 blueprint) ----
        FanCurveByFill = _file.Bind("Cards", "FanCurveByFill", true,
            "Demeo parity (G1): scale the fan's vertical arch and per-card tilt by how full the " +
            "hand is — nearly flat with a few cards, arched/tilted when the hand is full (Demeo " +
            "CardHandView). false = the old constant curvature at every hand size.");
        FanMaxHandForCurve = _file.Bind("Cards", "FanMaxHandForCurve", 10,
            "Demeo parity (G1): hand size at which the fan reaches full curvature. fill = " +
            "cardCount / this (clamped 0..1) scales the arch + tilt.");
        FanFlatCurvatureFactor = _file.Bind("Cards", "FanFlatCurvatureFactor", 0.55f,
            "Demeo parity (G1): the vertical-arch factor at a FULL hand (fill = 1). This is the " +
            "pre-Demeo constant value; with FanCurveByFill it is now the fill=1 target and the " +
            "arch scales down toward flat as the hand shrinks.");
        FanTiltFactor = _file.Bind("Cards", "FanTiltFactor", 0.85f,
            "Demeo parity (G1): the per-card Z-tilt factor at a FULL hand (fill = 1), scaled by fill.");
        FanSplitMultiplier = _file.Bind("Cards", "FanSplitMultiplier", 0.02f,
            "Demeo parity (G2): how far (real meters) the fan's cards slide sideways to open a gap " +
            "around the hovered card (Demeo splits the whole fan apart, not just the hovered card). " +
            "0 = no split.");
        FanSplitFalloff = _file.Bind("Cards", "FanSplitFalloff", 1.6f,
            "Demeo parity (G2): how quickly the neighbor split decays with distance (in card slots) " +
            "from the hovered card. Higher = only the nearest neighbors move; lower = the whole fan " +
            "spreads. Coded-curve substitute for Demeo's serialized falloff curve.");
        FanSelectedPopForward = _file.Bind("Cards", "FanSelectedPopForward", 0.035f,
            "Demeo parity (G2): how far (real meters) the hovered/selected card pops toward the " +
            "viewer (along -face normal). Demeo uses ~0.25 scene units; 0.035 m matches our scale.");
        GrabButton = _file.Bind("Cards", "GrabButton", CardGrabButton.Grip,
            "Demeo parity (G3): which controller button grabs a card by proximity. Trigger = " +
            "Demeo (index-finger pinch, matches our laser pluck so a card grabbed either way " +
            "releases on trigger-up). Grip = the pre-Demeo behavior.");
        FanFollowSmoothing = _file.Bind("Cards", "FanFollowSmoothing", 16f,
            "Demeo parity (G4): eased fan-follow rate (1/s exponential smoothing). The fan chases " +
            "the palm with a soft ease instead of being rigidly welded to it (Demeo ViewHelper). " +
            "0 = rigidly parented (the pre-Demeo behavior). Higher = snappier.");
        FanFollowDeadzone = _file.Bind("Cards", "FanFollowDeadzone", 0.004f,
            "Demeo parity (G4): fan-follow dead zone (real meters). The fan holds still until the " +
            "palm drifts past this, then eases to it — kills micro-jitter (Demeo minDistanceToMove). " +
            "Only used when FanFollowSmoothing > 0.");
        RevealPreset = _file.Bind("Cards", "RevealPreset", "generous",
            "Demeo parity (G5): 'generous' = our deliberate wide supination gate (hardware test " +
            "#10 — a small wrist roll reveals; the default). 'demeo' = Demeo's tighter ~53-degree " +
            "palm-up cone. Overrides the enter/exit supination thresholds when set to 'demeo'.");
        RevealIgnoreWhenGrabbing = _file.Bind("Cards", "RevealIgnoreWhenGrabbing", true,
            "Demeo parity (G5): don't open the fan on the hand that is currently grabbing " +
            "something (Demeo suppresses the reveal on the busy hand). false = the old behavior.");
    }

    /// <summary>True when the Demeo reveal preset is selected ([Cards] RevealPreset = demeo).</summary>
    internal static bool RevealDemeo =>
        string.Equals(RevealPreset.Value, "demeo", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Tray scale multiplier clamp (matches the two-handed grab clamp).</summary>
    internal static float ClampedTrayScale => Mathf.Clamp(TrayScale.Value, 0.5f, 2f);

    /// <summary>Card height derived from width (63.5 x 88 mm poker aspect).</summary>
    internal static float CardHeight => CardWidth.Value * (88f / 63.5f);

    /// <summary>True when the fan should be out without a gesture ([Cards] RevealMode = always).</summary>
    internal static bool RevealAlways =>
        string.Equals(RevealMode.Value, "always", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gate exit value derived from the enter value (wide hysteresis). May go negative:
    /// on the supination scale, 0 is palm-vertical, so closing can require rolling
    /// back BELOW vertical — that is intended (the fan never flickers).
    /// </summary>
    internal static float SupinationExitThreshold => Mathf.Max(-0.6f, SupinationThreshold.Value - 0.35f);

    internal static Vector3 TrayOffset => new(TrayRight.Value, -TrayDown.Value, TrayForward.Value);
}
