using System;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// [FigureGrab] config section (P8). Bound against a module-owned config file
/// (<c>BepInEx/config/dev.gloomhavenvr.figuregrab.cfg</c>) via
/// <see cref="ModuleConfig.Create"/> — self-contained, so this worker never has to edit
/// the shared <see cref="BoardConfig"/>. Feature toggle plus the in-hand pose tunables
/// (offset / rotation) and the held-size bounds, all of which need a hardware pass to feel right.
/// There is deliberately NO held-scale dial — see the note above <see cref="HeldOffset"/>.
///
/// PER HAND STYLE (2026-07 request A): the held mini is docked between thumb and index of
/// the hand MESH, whose geometry differs per style (Glove/Plate/Arcane) — so every held-pose
/// tunable (offset X/Y/Z, pitch, yaw, roll) is stored PER STYLE (the <c>Style*</c>
/// arrays, exactly the HandsConfig per-style seat pattern): each style is SEEDED on first
/// bind from the legacy global entry in this same file (BepInEx returns saved over default,
/// so a tuned pose carries over to all three styles instead of resetting; afterwards the
/// saved per-style value always wins). The <c>Active*</c> accessors read the ACTIVE style
/// ([Hands] HandStyle) live, and both a value edit AND a style switch re-pose the currently
/// held mini immediately (SettingChanged → <see cref="FigureGrabbable.ReapplyAll"/>). The
/// legacy global entries stay bound as harmless orphans (pre-Bind fallback + seed source).
/// <see cref="HeldUpright"/> is a MODE, not geometry — it stays global.
/// </summary>
internal static class FigureGrabConfig
{
    /// <summary>Master toggle for grabbing board figures into the hand.</summary>
    public static ConfigEntry<bool> GrabFigures = null!;

    /// <summary>
    /// How close the PINCH POINT has to come to a figure before it lights up as the grab
    /// candidate, in REAL MILLIMETRES AT THE HAND — see <see cref="PickRadiusRealMeters"/> for
    /// why the unit is the whole fix.
    /// </summary>
    public static ConfigEntry<float> PickRadiusMillimeters = null!;

    /// <summary>
    /// The figure pick radius as REAL METRES AT THE HAND — the distance your own hand travels,
    /// never a distance on the board.
    ///
    /// <para>WHY THIS EXISTS AS ITS OWN NUMBER (user report 2026-08, "Der Bereich in dem die Hand
    /// eine Figur zum grabben auswählt ist zu groß … Aktuell nehme ich so versehentlich Figuren in
    /// die Hand"). Figures used to inherit the shared palm reach of the interactor,
    /// <c>ProximityGrabber.ReachMeters</c> = 0.13 m — a HAND-SPAN, chosen for the card fan, where
    /// a card is a hand-span wide. A figure is not: you grab one by closing your fingers ON it.
    /// And because the mod's zoom scales the RIG and not the board, the board keeps its world
    /// size while the player grows: 0.13 m at the hand is <c>0.13 × rigScale</c> WORLD units, so
    /// measured in the only units the player can see next to a mini — hex widths — the volume is
    /// <c>0.13 / (TargetHexSize / zoom) = 0.867 × zoom</c> hexes across. At the shipped zoom of
    /// 2.8163 that is 2.4 HEXES of hover, most of it in mid-air above the mini, which is exactly
    /// the report. Shrinking the number does not change that proportionality (nothing can, while
    /// the reach is anchored to the hand — and the user asked for it to stay anchored there:
    /// "der Bereich nicht größer wird mit dem zoomen sondern an der Hand bleibt"), but 40 mm from
    /// the pinch point to the mini's own collider surface means you have to reach for the figure
    /// at every zoom instead of waving near it.</para>
    ///
    /// <para>Clamped rather than trusted: 5 mm is the smallest radius a tracked hand can hold
    /// steady, and 130 mm restores the old palm-reach behaviour exactly, which is what makes this
    /// dial a safe answer to "it is now too small" as well.</para>
    /// </summary>
    internal static float PickRadiusRealMeters
    {
        get
        {
            float mm = PickRadiusMillimeters != null
                ? PickRadiusMillimeters.Value
                : Defaults.PickRadiusMillimeters;
            return Mathf.Clamp(mm, PickRadiusMinMm, PickRadiusMaxMm) * 0.001f;
        }
    }

    /// <summary>Smallest radius a tracked hand can hold steady (mm).</summary>
    internal const float PickRadiusMinMm = 5f;

    /// <summary>The legacy shared palm reach (<c>ProximityGrabber.ReachMeters</c>), in mm.</summary>
    internal const float PickRadiusMaxMm = 130f;

    // ---- HELD-FIGURE STRETCH (two-hand resize gesture; see FigureStretch) -------------------

    /// <summary>Capture radius of the stretch gesture: how close the free hand's pinch point must
    /// come to the mini in the OTHER hand before trigger starts the resize, real mm at the hand.</summary>
    public static ConfigEntry<float> StretchReachMillimeters = null!;

    /// <summary>
    /// Let the hand that is NOT holding a figure push that figure's cape around by touching it.
    /// User request, ModBuild 286: "sie sollen auch auf meine andere Hand reagieren, wenn ich mit
    /// der freien VR hand diese elemente berühre." See <see cref="FigureClothHands"/> for the
    /// mechanism and for why it is measured to be free. Default ON, because he asked for it; a dial
    /// because the free hand approaching a held figure is ALSO how the stretch gesture is armed, and
    /// only his hands can say whether the two fight.
    /// </summary>
    public static ConfigEntry<bool> ClothFollowsFreeHand = null!;

    /// <summary>
    /// Show the pre-grab proximity glow on figures WHILE THE PLAYER IS STANDING INSIDE THE BOARD
    /// (<see cref="Core.WallSegmentFade.WalkInsideEngaged"/> — the narrow walk-in latch, not the
    /// loose "leaning over the table" one).
    ///
    /// <para>USER REQUEST, ModBuild 286: "Wenn ich mich im Modus befinde, dass ich IN der Welt drin
    /// bin (das hast du für Wände schon gebaut) möchte ich optional das highlighting der figuren
    /// deaktivieren können wenn man mit der Hand über ihnen fährt. Grabbing soll noch ganz normal
    /// möglich sein."</para>
    ///
    /// <para>DEFAULT OFF — i.e. the glow IS suppressed in walk-in mode — and here is the argument,
    /// because a default is a behaviour change and he only literally asked for the switch. (1) He
    /// described the current behaviour as the problem; nobody asks for a switch to keep what they
    /// already have. (2) The cue's JOB is gone in that mode. The glow answers "which of these minis
    /// would my hand pluck from across the table" — standing among them at figure scale the answer
    /// is whichever one you are reaching into, and a glow that answers a question you are not asking
    /// is a light show. (3) The cost of being wrong is one toggle, and the toggle is in the same
    /// menu section as the feature it belongs to. If he wanted it kept, this is the line to flip and
    /// nothing else changes.</para>
    ///
    /// <para>GRABBING IS UNTOUCHED. This suppresses one visual only: the hover election, the haptic
    /// hover tick, the offset-anchor winner and every grab path run exactly as before.</para>
    /// </summary>
    public static ConfigEntry<bool> HighlightWhileWalkIn = null!;

    /// <summary>Pre-grab glow allowed right now? False only while the player is standing inside the
    /// board AND <see cref="HighlightWhileWalkIn"/> is off. Null-guarded; pre-Bind → default.</summary>
    internal static bool HighlightAllowedHere
        => (HighlightWhileWalkIn == null ? Defaults.HighlightWhileWalkIn : HighlightWhileWalkIn.Value)
           || !Core.WallSegmentFade.WalkInsideEngaged;

    /// <summary>
    /// How close the free hand must come to a held figure before its cape starts colliding with
    /// that hand, in REAL MILLIMETRES AT THE HAND. Outside it the collider is detached entirely and
    /// costs nothing.
    /// </summary>
    public static ConfigEntry<float> ClothHandReachMillimeters = null!;

    /// <summary>The free-hand cloth reach as REAL METRES AT THE HAND — the same unit and the same
    /// reasoning as <see cref="PickRadiusRealMeters"/>. Clamped rather than trusted; the number
    /// inside <c>Clamp</c> is the PRE-BIND fallback and the shipped default lives in
    /// <c>Defaults.ClothHandReachMillimeters</c>.</summary>
    internal static float ClothHandReachRealMeters
    {
        get
        {
            float mm = ClothHandReachMillimeters != null
                ? ClothHandReachMillimeters.Value
                : Defaults.ClothHandReachMillimeters;
            return Mathf.Clamp(mm, ClothHandReachMinMm, ClothHandReachMaxMm) * 0.001f;
        }
    }

    /// <summary>Below this the probe would attach only when the hand is already inside the cape.</summary>
    internal const float ClothHandReachMinMm = 30f;

    /// <summary>Above this the probe is attached essentially whenever a figure is held, which
    /// defeats the "costs nothing when the hand is away" half of the design.</summary>
    internal const float ClothHandReachMaxMm = 600f;

    /// <summary>Smallest TOTAL held size a figure may have in the hand, relative to its own
    /// board-home size as it appears at the DEFAULT diorama zoom (see the semantics note on
    /// <see cref="StretchLimits"/>).</summary>
    public static ConfigEntry<float> StretchScaleMin = null!;

    /// <summary>Largest TOTAL held size a figure may have in the hand (same reference as
    /// <see cref="StretchScaleMin"/>).</summary>
    public static ConfigEntry<float> StretchScaleMax = null!;

    /// <summary>
    /// Master switch for the two size bounds above — the requested off switch.
    ///
    /// <para>USER REQUEST (hardware report 2026-08-11, verbatim): "lass mich die mindestgröße und
    /// maximalgröße einer Figur im Debugmenu einstellen. Wenn ich so nah in der Welt reingezommed
    /// habe, dass dei figur größer als die maximalgröße ist und ich sie in die Hand nehme solle
    /// sie die Maximalgrößer in der Hand haben (selbes Prinzip für die Minimalgröße. Ich will
    /// Optional auch die Ober und und Untergrenzen ganz abschalten können im Debug Menu."</para>
    ///
    /// <para>SEMANTICS OF THE BOUNDS (integrator ruling, this round): Min/Max bound the figure's
    /// TOTAL held size relative to its own board-home size — the size the mini would show next to
    /// the hand at the DEFAULT diorama zoom. That total is (zoom-ratio-at-grab × stretch factor):
    /// the grab-time size latch inherits the zoom the player stood at when grabbing
    /// (<c>FigureGrabbable._heldLocalScale</c>), and the two-hand gesture multiplies on top. The
    /// user's words are "Mindestgröße/Maximalgröße einer FIGUR" and his scenario is zoom-driven,
    /// so the bound must catch the size HOWEVER it arose — a deep zoom-in at grab time exactly as
    /// much as an outward drag. Two consequences, both implemented at the only pop-free moments:
    /// the GRAB clamps the latch itself (the mini enters the hand at exactly the bound — "solle
    /// sie die Maximalgrößer in der Hand haben"), and the GESTURE's clamp is expressed in the
    /// same total (converted to per-hold factor bounds at latch time,
    /// <c>FigureGrabbable.GetStretchFactorBounds</c>). A grab at the default zoom keeps the exact
    /// pre-this-round behaviour: ratio 1, factor bounds = Min..Max verbatim.</para>
    ///
    /// <para>WHEN FALSE: no grab-time clamp and no gesture clamp — only the technical floor
    /// <see cref="StretchHardFloor"/> keeps the scale positive and finite. LIVE, but latch-scoped:
    /// flipping it mid-hold changes what the NEXT gesture frame / NEXT grab may do; the standing
    /// held size is never re-clamped in place, because re-clamping a size the player is looking at
    /// would be a pop. MULTIPLAYER: the bounds are a LOCAL presentation choice — only the gesture
    /// factor rides the wire (record 30) and <c>NetProtocol.EncodeHeldStretch</c> clamps the
    /// outgoing value into the wire's own sane envelope (0.10×..8.0×, NaN→neutral), so a
    /// limits-off factor beyond 8× reaches peers as 8× and can never be rejected by their
    /// fail-closed decode.</para>
    /// </summary>
    public static ConfigEntry<bool> StretchLimits = null!;

    /// <summary>
    /// Show the docked actor info panel when a figure is picked up (default: yes — the behaviour
    /// every build so far shipped). USER REQUEST (2026-08-11, verbatim): "Beim Figur aufnehmen
    /// kommt ja die Gegnerinfo (was gewollt ist) mach diese aber auch optional in dem VR
    /// Einstellungen deaktivierbar." Consumed by <c>WorldUI.Surfaces.StatPanelSurface.ShowHeldFigure</c>
    /// (the single registration seam every pickup goes through); the SettingChanged hook in
    /// <see cref="Bind"/> closes an already-open held panel when the dial is flipped OFF
    /// mid-hold. Flipping it ON mid-hold shows nothing until the next pickup — registration
    /// happens only at the grab.
    /// </summary>
    public static ConfigEntry<bool> HeldFigureInfo = null!;

    /// <summary>May chests, gold piles, traps and obstacles be picked up like a figure?
    /// See <c>Board.FigureGrab.PropGrab</c> (the m_ClientObjects pass it replaced was retired in ModBuild 338).</summary>
    public static ConfigEntry<bool> GrabProps = null!;

    /// <summary>True unless the player switched prop pickup off; true before the config is
    /// bound, matching HeldFigureInfoEnabled's shape next door.</summary>
    internal static bool GrabPropsEnabled => GrabProps == null || GrabProps.Value;

    /// <summary>Technical floor on the stretch factor while <see cref="StretchLimits"/> is OFF —
    /// not a size opinion, only "the scale must stay positive and finite" (a zero or negative
    /// scale breaks renderer bounds and the release glide's lerp).</summary>
    internal const float StretchHardFloor = 0.01f;

    /// <summary>Bounds enabled? Null-guarded like every accessor here (pre-Bind → default).</summary>
    internal static bool StretchLimitsEnabled => StretchLimits == null || StretchLimits.Value;

    /// <summary>Info panel on pickup enabled? (null-guarded; pre-Bind → default).</summary>
    internal static bool HeldFigureInfoEnabled => HeldFigureInfo == null || HeldFigureInfo.Value;

    /// <summary>Bind-range floor/ceiling of the two stretch clamps. Deliberately INSIDE the wire's
    /// sane envelope (<c>NetProtocol.HeldStretchCodeMin/Max</c>, 0.10×..8.0×), so no legitimately
    /// tuned factor can ever be rejected by a peer's fail-closed decode. Since the bounds went
    /// TOTAL-based (and got an off switch, <see cref="StretchLimits"/>) the per-hold FACTOR can
    /// legitimately leave that envelope; the guarantee is now carried by the encoder instead —
    /// <c>NetProtocol.EncodeHeldStretch</c> clamps every outgoing factor into the envelope before
    /// quantizing, so the wire never carries a rejectable code either way.</summary>
    internal const float StretchScaleFloor = 0.1f;
    internal const float StretchScaleCeiling = 8f;

    /// <summary>The stretch capture radius as REAL METRES AT THE HAND — same unit story as
    /// <see cref="PickRadiusRealMeters"/>: the distance the player's own hand travels, never a
    /// distance on the board, so zooming the diorama never changes the gesture's feel.</summary>
    internal static float StretchReachRealMeters
    {
        get
        {
            float mm = StretchReachMillimeters != null
                ? StretchReachMillimeters.Value
                : Defaults.StretchReachMillimeters;
            return Mathf.Clamp(mm, PickRadiusMinMm, 300f) * 0.001f;
        }
    }

    /// <summary>The lower stretch clamp, cross-clamped so Min can never exceed Max: two dials that
    /// crossed would otherwise need a third rule to sort out mid-gesture (the PickExitFactor
    /// argument, applied to a pair we DO ship as two dials because both ends are things a player
    /// legitimately tunes).</summary>
    internal static float StretchScaleMinValue
    {
        get
        {
            float min = StretchScaleMin != null ? StretchScaleMin.Value : Defaults.StretchScaleMin;
            return Mathf.Clamp(Mathf.Min(min, StretchScaleMaxValue), StretchScaleFloor, StretchScaleCeiling);
        }
    }

    /// <summary>The upper stretch clamp (see <see cref="StretchScaleMinValue"/>).</summary>
    internal static float StretchScaleMaxValue
    {
        get
        {
            float max = StretchScaleMax != null ? StretchScaleMax.Value : Defaults.StretchScaleMax;
            return Mathf.Clamp(max, StretchScaleFloor, StretchScaleCeiling);
        }
    }

    /// <summary>Held offset toward the fingertips (GrabAnchor-local Z).</summary>
    public static ConfigEntry<float> HeldOffsetForward = null!;

    /// <summary>Held offset out of the palm (GrabAnchor-local Y).</summary>
    public static ConfigEntry<float> HeldOffsetUp = null!;

    /// <summary>Held lateral offset toward the thumb–index pinch (GrabAnchor-local X).</summary>
    public static ConfigEntry<float> HeldOffsetSide = null!;

    /// <summary>
    /// Hold the mini UPRIGHT (feet→head along world up), pinched between thumb and index and
    /// facing the player — like inspecting a chess piece. When false, the legacy palm pose is
    /// used (<see cref="HeldPalmRotation"/> relative to the hand, lays it flat).
    /// </summary>
    public static ConfigEntry<bool> HeldUpright = null!;

    /// <summary>Stand the mini head-up IN THE WORLD at the moment of the grab, whatever angle you
    /// reached from. One-shot: it still turns freely with the hand afterwards.</summary>
    public static ConfigEntry<bool> HeldUprightAtGrab = null!;

    /// <summary>Held tilt (degrees) — tip the mini toward the face for inspection (both modes).</summary>
    public static ConfigEntry<float> HeldTiltDegrees = null!;

    /// <summary>
    /// Upright mode only: extra yaw (degrees) spinning the mini's readable front toward the
    /// player. Auto-facing assumes the model's local +Z is its front; set 180 if it shows its
    /// back. Tune on hardware.
    /// </summary>
    public static ConfigEntry<float> HeldFaceYawDegrees = null!;

    // ---- per-STYLE held pose (request A; indexed by (int)HandStyle: Glove/Plate/Arcane) ----

    /// <summary>Per-style held lateral offset (GrabAnchor-local X, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetSide;

    /// <summary>Per-style held offset out of the palm (GrabAnchor-local Y, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetUp;

    /// <summary>Per-style held offset toward the fingertips (GrabAnchor-local Z, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetForward;

    /// <summary>Per-style held tilt (degrees).</summary>
    public static ConfigEntry<float>[]? StyleHeldTiltDegrees;

    /// <summary>Per-style upright-mode face yaw (degrees).</summary>
    public static ConfigEntry<float>[]? StyleHeldFaceYawDegrees;

    /// <summary>Per-style held ROLL (degrees) — superseded by <see cref="StyleHeldRotRoll"/>.</summary>
    public static ConfigEntry<float>[]? StyleHeldRollDegrees;

    // The rotation trio, renamed so the three sit TOGETHER in an alphabetically sorted menu
    // (HeldRotPitch / HeldRotRoll / HeldRotYaw) instead of scattered between the offsets, the
    // scale and the upright switch — "Tilt" and "FaceYaw" also never said which axis they were.
    // Each is seeded from the value its predecessor held, so nothing anyone tuned is lost.

    /// <summary>Per-style PITCH (degrees): tips the mini forward/back. Was HeldTiltDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotPitch;

    /// <summary>Per-style YAW (degrees): spins the mini about its OWN up axis. Was HeldFaceYawDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotYaw;

    /// <summary>Per-style ROLL (degrees): spins the mini about its OWN forward axis. Was HeldRollDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotRoll;

    /// <summary>The ACTIVE style's per-style value, else the legacy global, else the shipped default.</summary>
    private static float StyleOr(ConfigEntry<float>[]? entries, ConfigEntry<float>? legacy, float shipped)
    {
        try
        {
            if (entries != null)
                return entries[Hands.HandsConfig.ActiveStyleIndex].Value;
            return legacy != null ? legacy.Value : shipped;
        }
        catch
        {
            return shipped;
        }
    }

    internal static float ActiveHeldSide => StyleOr(StyleHeldOffsetSide, HeldOffsetSide, 0f);
    internal static float ActiveHeldUp => StyleOr(StyleHeldOffsetUp, HeldOffsetUp, 0.03f);
    internal static float ActiveHeldForward => StyleOr(StyleHeldOffsetForward, HeldOffsetForward, 0.03f);
    internal static float ActiveHeldTilt => StyleOr(StyleHeldRotPitch, null, StyleOr(StyleHeldTiltDegrees, HeldTiltDegrees, 0f));
    internal static float ActiveHeldFaceYaw => StyleOr(StyleHeldRotYaw, null, StyleOr(StyleHeldFaceYawDegrees, HeldFaceYawDegrees, 0f));
    internal static float ActiveHeldRoll => StyleOr(StyleHeldRotRoll, null, StyleOr(StyleHeldRollDegrees, null, 0f));

    // WHY THERE IS NO HELD-ZOOM DIAL (the old [FigureGrab] HeldScale + {Style}HeldScale family
    // is DELETED — 2026-08 dead-settings sweep): a held mini enters the hand at exactly its
    // BOARD size and then KEEPS that size (FigureGrabbable.HeldLocalScale, latched at the grab)
    // — user ruling after the 2026-08 MP hardware test, "Die Figuren-Größen ändern sich wenn man
    // sie in die Hand nimmt. Das soll nicht sein." Any multiplier is also a MULTIPLAYER defect
    // and not merely a taste one: the held figure's wire record carries pose only, so a peer
    // renders the mini at its own board scale — a zoom applied on the holder's side alone (and
    // bound PER HAND STYLE, so not even the same number on two machines) is exactly the "ich
    // sehe beim Remote-Spieler eine andere Größe als er selbst" half of the report. Putting a
    // held zoom back therefore needs a scale on the wire, not a dial here.
    //
    // 2026-08-11 follow-up, so this note is not read as forbidding the current behaviour: the
    // hold FREEZES the size at the grab instead of re-deriving the board size every frame ("die
    // Größe soll nur abhängig sein wann sie greift und dann fix in der Hand sein - auch wenn man
    // dabei zoomed"). That is not a multiplier — the size still comes from the board and from
    // nowhere else. The peer-side reconstruction for that is described in
    // FigureGrabbable._heldLocalScale and needs no dial here.

    /// <summary>
    /// GrabAnchor-local held position for the RIGHT hand — the canonical pose the debug
    /// steppers tune (ACTIVE hand style). The LEFT hand derives from it by mirroring
    /// (see <see cref="HeldOffsetFor"/>).
    /// </summary>
    internal static Vector3 HeldOffset => new(ActiveHeldSide, ActiveHeldUp, ActiveHeldForward);

    /// <summary>
    /// The GrabAnchor-local held offset for a given hand. The tuned values are canonical for
    /// the RIGHT hand; the LEFT hand is the MIRROR IMAGE across the hand-frame's left-right (X)
    /// axis, so the mini sits in the left hand exactly as it does in the right. Only the lateral
    /// component (<see cref="HeldOffsetSide"/>, local X) flips sign; forward (Z) and up (Y) are
    /// unchanged. The hand rig frame is NOT mirrored between hands (mesh mirrored, frame shared —
    /// see HandRig), so without this flip the same local X put the mini on the same frame-side of
    /// both hands (anatomically opposite) — this negation restores the true mirror.
    /// </summary>
    internal static Vector3 HeldOffsetFor(HandSide side)
    {
        float sideSign = side == HandSide.Left ? -1f : 1f;
        return new Vector3(sideSign * ActiveHeldSide, ActiveHeldUp, ActiveHeldForward);
    }

    /// <summary>
    /// The inspection yaw for a given hand. Mirroring a rotation across the hand-frame's
    /// left-right (X) plane negates the YAW (and any ROLL) while leaving the TILT (pitch about X)
    /// untouched — so the LEFT hand yaws the mini's readable front the opposite way, the mirror of
    /// the tuned RIGHT-hand yaw. (Roll is always 0 here, so only the yaw needs the flip.)
    /// </summary>
    internal static float HeldFaceYawFor(HandSide side)
        => side == HandSide.Left ? -ActiveHeldFaceYaw : ActiveHeldFaceYaw;

    /// <summary>
    /// Held ROLL for one hand — the third rotation axis, so a mini has the same freedom in the
    /// hand as the hand itself: three offsets and three angles.
    ///
    /// <para>Mirrored exactly like the yaw, and for the same reason: roll is a rotation about the
    /// forward axis, so the mirror image of a right-hand roll is the opposite roll. Tune once on
    /// the right hand and the left is correct. (Tilt stays mirror-invariant — it is a rotation
    /// about the mirror axis itself.)</para>
    /// </summary>
    internal static float HeldRollFor(HandSide side)
        => side == HandSide.Left ? -ActiveHeldRoll : ActiveHeldRoll;

    /// <summary>
    /// Issue A — the upright held orientation as a FIXED CONSTANT rotation RELATIVE TO THE
    /// GRABANCHOR (NOT derived from world up, the player's head, the figure's board rotation,
    /// or the grab-moment anchor orientation), so the mini sits the SAME way in the palm no
    /// matter the approach angle and RIDES THE HAND (turn the hand → it turns with it).
    ///
    /// The mod's GrabAnchor is authored so its +Y points OUT OF THE PALM (see
    /// <c>HandVisuals</c>: the palm anchor's +Y is the palm normal, GrabAnchor inherits it), and
    /// the board mini's model local +Y is its up axis — so an IDENTITY base already stands the
    /// mini upright out of the palm, exactly the good "perfect horizontal grab" hold (palm up →
    /// mini up). Because it is anchor-LOCAL, grabbing from directly above (palm down) makes the
    /// mini extend OUT of the palm (harmlessly downward) instead of standing world-upright and
    /// clipping INTO the hand — the whole bug. <see cref="HeldTiltDegrees"/> then tips it toward
    /// the face and the MIRROR-CORRECT <see cref="HeldFaceYawFor"/> spins the readable front
    /// toward the player (negated for the left hand, tilt mirror-invariant), both live-tunable.
    /// </summary>
    /// <remarks>
    /// ORDER MATTERS, and a single Quaternion.Euler(tilt, yaw, roll) gets it wrong. Unity composes
    /// that as Ry * Rx * Rz, so the YAW is applied LAST and therefore about the HAND's up axis.
    /// Once the mini is tilted, spinning about the hand's up axis no longer spins the mini — it
    /// TIPS it, which is exactly the complaint: yaw on a figure you have just stood upright should
    /// turn it and nothing else. So the yaw goes FIRST, in the mini's own frame, and tilt/roll are
    /// applied on top: R = tiltRoll * yaw. Upright (tilt 0) that is an ordinary spin about the
    /// mini's axis; tilted, it is still a pure spin, just seen tipped.
    /// </remarks>
    internal static Quaternion HeldUprightRotation(HandSide side)
        => Quaternion.Euler(ActiveHeldTilt, 0f, HeldRollFor(side))
           * Quaternion.Euler(0f, HeldFaceYawFor(side), 0f);

    /// <summary>
    /// The palm pose: tilt only, as it always was.
    ///
    /// <para>It deliberately does NOT take the yaw and the roll. Giving it all three (which I did
    /// once) made the two poses byte-for-byte identical and quietly turned the "hold upright"
    /// switch into a setting that does nothing — the yaw was the only thing it ever selected.
    /// The switch keeps its meaning; the extra axes live in the upright pose, which is the one
    /// you inspect a mini in.</para>
    ///
    /// </summary>
    internal static Quaternion HeldPalmRotation() => Quaternion.Euler(ActiveHeldTilt, 0f, 0f);

    private static ConfigFile? _file;

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("figuregrab");

        GrabFigures = config.Bind(
            "FigureGrab", "GrabFigures", Defaults.GrabFigures,
            "Grab a board figure (hero OR monster) into your hand with the TRIGGER to " +
            "inspect it up close — pure immersion, no gameplay effect. Release to snap it " +
            "back to its board cell.");
        PickRadiusMillimeters = config.Bind(
            "FigureGrab", "PickRadiusMillimeters", Defaults.PickRadiusMillimeters,
            new ConfigDescription(
                "How close your PINCH POINT (where a held mini sits, between thumb and index) has " +
                "to come to a figure before it lights up as the one you would grab, in REAL " +
                "MILLIMETRES AT YOUR HAND — the distance your own hand moves, not a distance on " +
                "the board. It does NOT grow when you zoom the table out: the same 40 mm of reach " +
                "simply covers fewer board tiles once the minis are small. Measured to the " +
                "figure's own surface, so a big mini is still easy to catch. Lower it if you " +
                "still pick figures up by accident; 130 is the old palm-wide reach that made " +
                "hovering anywhere above a mini enough.",
                new AcceptableValueRange<float>(PickRadiusMinMm, PickRadiusMaxMm)));
        StretchReachMillimeters = config.Bind(
            "FigureGrab", "StretchReachMillimeters", Defaults.StretchReachMillimeters,
            new ConfigDescription(
                "While one hand HOLDS a figure: how close your OTHER hand's pinch point must come " +
                "to that mini before holding TRIGGER and dragging resizes it (outward = larger, " +
                "inward = smaller), in REAL MILLIMETRES AT YOUR HAND — the same unit as the pick " +
                "radius, so zooming the table never changes the feel. Wider than the pick radius " +
                "on purpose: the mini is in your own hand, there is no neighbouring figure to " +
                "disambiguate from. Inside this zone the trigger belongs to the gesture; a hovered " +
                "card still wins its own grab.",
                new AcceptableValueRange<float>(PickRadiusMinMm, 300f)));
        HighlightWhileWalkIn = config.Bind(
            "FigureGrab", "HighlightWhileWalkIn", Defaults.HighlightWhileWalkIn,
            "Keep the pre-grab glow on figures while you are STANDING INSIDE the board (the same " +
            "walk-in mode that holds the walls solid). Off = no glow down there; across the table " +
            "it still lights up exactly as before. Grabbing is unaffected either way — this is the " +
            "visual cue only, not the interaction. Off by default because the cue answers 'which " +
            "of those minis would I pluck from here', and standing among them at their own scale " +
            "the answer is whichever one you are reaching into.");
        ClothFollowsFreeHand = config.Bind(
            "FigureGrab", "ClothFollowsFreeHand", Defaults.ClothFollowsFreeHand,
            "While one hand HOLDS a figure, let your OTHER hand push that figure's cloth around — " +
            "capes, cloaks and tabards bend away from your fingers when you reach into them. The " +
            "holding hand already swings them by moving the mini; this adds the free hand as " +
            "something they can actually be touched by. Costs nothing while no figure is held or " +
            "while the free hand is away from the one you are holding. Turn it off if it fights " +
            "the two-hand resize gesture, which arms in the same place.");
        ClothHandReachMillimeters = config.Bind(
            "FigureGrab", "ClothHandReachMillimeters", Defaults.ClothHandReachMillimeters,
            new ConfigDescription(
                "How close your free hand has to come to the figure you are holding before its " +
                "cloth starts reacting to that hand, in REAL MILLIMETRES AT YOUR HAND — the same " +
                "unit as the pick radius, so zooming the table never changes the feel. Beyond it " +
                "the hand is detached from the simulation entirely and costs nothing. Ignored " +
                "while ClothFollowsFreeHand is off.",
                new AcceptableValueRange<float>(ClothHandReachMinMm, ClothHandReachMaxMm)));
        StretchScaleMin = config.Bind(
            "FigureGrab", "StretchScaleMin", Defaults.StretchScaleMin,
            new ConfigDescription(
                "Smallest TOTAL size a figure may have in your hand, as a factor of the size it " +
                "shows at the DEFAULT table zoom (0.5 = half). It bounds the size however it " +
                "arose: a figure grabbed while zoomed far out enters the hand at exactly this " +
                "size instead of tinier, and the two-hand stretch gesture cannot shrink it below " +
                "it either. The gesture is a ratio — slide back out and the figure returns " +
                "through every size — so this is a clamp, not a step. Ignored while " +
                "StretchLimits is off.",
                new AcceptableValueRange<float>(StretchScaleFloor, 1f)));
        StretchScaleMax = config.Bind(
            "FigureGrab", "StretchScaleMax", Defaults.StretchScaleMax,
            new ConfigDescription(
                "Largest TOTAL size a figure may have in your hand, as a factor of the size it " +
                "shows at the DEFAULT table zoom (3 = three times). It bounds the size however " +
                "it arose: a figure grabbed while zoomed in so deep that it would be bigger than " +
                "this enters the hand at exactly this size, and the two-hand stretch gesture " +
                "cannot grow it past it either. Applies to the hold only: releasing always " +
                "glides the figure back to its true board size. Ignored while StretchLimits is " +
                "off.",
                new AcceptableValueRange<float>(1f, StretchScaleCeiling)));
        StretchLimits = config.Bind(
            "FigureGrab", "StretchLimits", Defaults.StretchLimits,
            "Enforce the min/max held-figure size (StretchScaleMin/Max) at all. Off = a figure " +
            "in the hand may take any size the grab zoom and the stretch gesture produce, with " +
            "only a tiny technical floor keeping the scale positive. Live: the next grab and " +
            "the next gesture frame honour the new setting; a figure already in the hand keeps " +
            "its current size until you act on it (re-clamping it in place would make it pop).");
        HeldFigureInfo = config.Bind(
            "FigureGrab", "HeldFigureInfo", Defaults.HeldFigureInfo,
            "Show the actor info panel docked next to a figure when you pick it up (the same " +
            "stat card the game shows on mouse-over). Off = picking a figure up shows no panel. " +
            "Live: turning it off closes an open held-figure panel immediately; turning it on " +
            "takes effect on the next pickup.");
        GrabProps = config.Bind(
            "FigureGrab", "GrabProps", Defaults.GrabProps,
            "Pick up PROPS as well as figures - treasure chests, gold piles, traps, " +
            "obstacles, quest items and loose resources. They behave exactly like a " +
            "miniature in the hand: the same reach, the same hover highlight and haptic, " +
            "the same info panel docked beside them (a trap's panel is where you read what " +
            "it does), one in each hand at once, and the same purely-cosmetic hold - the " +
            "game snaps everything back to its cell when you let go. Terrain you merely " +
            "walk through more slowly is deliberately NOT included: it is not a thing you " +
            "could lift. Off = only figures, as before.");
        // The five entries below are LEGACY (see the per-STYLE block further down, which
        // superseded them): each is read exactly once, as the bind DEFAULT that seeds its
        // three per-style successors the first time this cfg file is written, and never
        // again — StyleOr() prefers the per-style array whenever it exists, which is always
        // after Bind() has run. They stay bound because unbinding drops the keys from every
        // existing dev.gloomhavenvr.figuregrab.cfg (CHARTER §5). HeldUpright is NOT legacy:
        // it is a MODE, not geometry, and stayed deliberately global.
        const string legacyTail =
            "This entry is read once, as the seed for those per-style keys the first time they " +
            "are created, and never again. Kept bound so existing config files keep loading. " +
            "Historical meaning: ";
        HeldOffsetForward = config.Bind(
            "FigureGrab", "HeldOffsetForward", Defaults.HeldOffsetForward,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetForward. " +
            legacyTail +
            "held position offset toward the fingertips (grab-anchor local Z) — moves the mini " +
            "out to the thumb–index pinch point.");
        HeldOffsetUp = config.Bind(
            "FigureGrab", "HeldOffsetUp", Defaults.HeldOffsetUp,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetUp. " +
            legacyTail +
            "held position offset out of the palm (grab-anchor local Y).");
        HeldOffsetSide = config.Bind(
            "FigureGrab", "HeldOffsetSide", Defaults.HeldOffsetSide,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetSide. " +
            legacyTail +
            "held lateral position offset (grab-anchor local X) toward the thumb–index pinch.");
        HeldUprightAtGrab = config.Bind(
            "FigureGrab", "HeldUprightAtGrab", Defaults.HeldUprightAtGrab,
            "Stand the mini HEAD UP IN THE WORLD at the moment you grab it, no matter which angle " +
            "you reached from — palm down, from the side, upside down. It is captured ONCE, at the " +
            "grab: afterwards the mini rides the hand as it always did, so turning your wrist still " +
            "turns it through every angle. It is not a constraint that keeps re-righting the mini " +
            "while you are trying to look at its base. The tuned angles below stay offsets — with " +
            "this on they are offsets from 'standing up' rather than from the hand, so expect to " +
            "re-tune them once.");
        HeldUpright = config.Bind(
            "FigureGrab", "HeldUpright", Defaults.HeldUpright,
            "Hold the mini UPRIGHT (standing, pointing up) pinched between thumb and index and " +
            "facing you, like inspecting a chess piece. False = legacy flat-on-palm pose. " +
            "LIVE (unlike the other Held* entries in this section): this is a mode, not " +
            "geometry, so it stayed global instead of going per hand style.");
        HeldTiltDegrees = config.Bind(
            "FigureGrab", "HeldTiltDegrees", Defaults.FigureGrab_HeldTiltDegrees,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldTiltDegrees. " +
            legacyTail +
            "held tilt (degrees) — tip the mini toward your face for inspection.");
        HeldFaceYawDegrees = config.Bind(
            "FigureGrab", "HeldFaceYawDegrees", Defaults.HeldFaceYawDegrees,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldFaceYawDegrees. " +
            legacyTail +
            "upright mode only: extra yaw (degrees) to spin the mini's front toward you. Set 180 " +
            "if it faces away.");

        // PER-STYLE held pose (request A): one absolute offset/tilt/yaw/scale set per hand
        // style. The BIND DEFAULT of each entry is the legacy global entry's CURRENT value
        // (bound just above in this same file — saved wins over shipped default), so a first
        // run SEEDS every style with the user's tuned pose; any later run keeps the saved
        // per-style value. No marker entry needed: the seed is only consulted while the key
        // is absent from the cfg. HeldUpright stays global (a mode, not geometry).
        string[] styleNames = { "Glove", "Plate", "Arcane" }; // index == (int)HandStyle
        StyleHeldOffsetSide = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldOffsetUp = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldOffsetForward = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldTiltDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldFaceYawDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRollDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotPitch = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotYaw = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotRoll = new ConfigEntry<float>[HandStyles.Count];
        for (int i = 0; i < HandStyles.Count; i++)
        {
            string s = styleNames[i];
            string per = $"PER-STYLE absolute value while the {s} hand style is worn " +
                "(supersedes the shared legacy entry it was seeded from on first run). " +
                "Live-tunable — a held mini re-poses immediately.";
            StyleHeldOffsetSide[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetSide", HeldOffsetSide.Value,
                $"Held lateral position offset (grab-anchor local X) toward the thumb-index pinch. {per}");
            StyleHeldOffsetUp[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetUp", HeldOffsetUp.Value,
                $"Held position offset out of the palm (grab-anchor local Y). {per}");
            StyleHeldOffsetForward[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetForward", HeldOffsetForward.Value,
                $"Held position offset toward the fingertips (grab-anchor local Z). {per}");
            StyleHeldTiltDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldTiltDegrees", HeldTiltDegrees.Value,
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotPitch. Read once, " +
                "as the seed for its successor.");
            StyleHeldFaceYawDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldFaceYawDegrees", HeldFaceYawDegrees.Value,
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotYaw. Read once, as " +
                "the seed for its successor.");
            StyleHeldRollDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldRollDegrees", Defaults.HeldRollDegrees_ByStyle[i],
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotRoll. Read once, as " +
                "the seed for its successor.");

            // The trio, named so it sorts together and says which axis it is. Seeded from the
            // predecessor's CURRENT value, so an existing tuning carries over untouched.
            StyleHeldRotPitch[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotPitch", StyleHeldTiltDegrees[i].Value,
                "PITCH (degrees): tips the mini forward and back, about the axis running across " +
                $"your palm. This is the old HeldTiltDegrees under a name that says which axis it is. {per}");
            StyleHeldRotYaw[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotYaw", StyleHeldFaceYawDegrees[i].Value,
                "YAW (degrees): turns the mini about ITS OWN up axis — a spin, never a tip, so it " +
                "brings the readable front toward you. Applied to the mini before pitch and roll, " +
                "which is what keeps it a spin no matter how the other two are set. MIRRORED " +
                $"between the hands, so tune the right hand and the left follows. {per}");
            StyleHeldRotRoll[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotRoll", StyleHeldRollDegrees[i].Value,
                "ROLL (degrees): turns the mini about ITS OWN forward axis. MIRRORED between the " +
                $"hands like the yaw. {per}");
        }

        // Live-tune hook: any held-pose tunable change re-poses the currently-held mini in-hand
        // (the in-headset debug-menu steppers), so tuning is interactive. BepInEx still persists
        // every write to dev.gloomhavenvr.figuregrab.cfg. The legacy globals keep their hooks
        // (harmless — unread once the per-style entries exist).
        void Reapply(object sender, EventArgs e) => FigureGrabbable.ReapplyAll();
        HeldOffsetForward.SettingChanged += Reapply;
        HeldOffsetUp.SettingChanged += Reapply;
        HeldOffsetSide.SettingChanged += Reapply;
        HeldUpright.SettingChanged += Reapply;
        HeldUprightAtGrab.SettingChanged += Reapply;
        HeldTiltDegrees.SettingChanged += Reapply;
        HeldFaceYawDegrees.SettingChanged += Reapply;
        for (int i = 0; i < HandStyles.Count; i++)
        {
            StyleHeldOffsetSide[i].SettingChanged += Reapply;
            StyleHeldOffsetUp[i].SettingChanged += Reapply;
            StyleHeldOffsetForward[i].SettingChanged += Reapply;
            StyleHeldTiltDegrees[i].SettingChanged += Reapply;
            StyleHeldFaceYawDegrees[i].SettingChanged += Reapply;
            StyleHeldRollDegrees[i].SettingChanged += Reapply;
            StyleHeldRotPitch[i].SettingChanged += Reapply;
            StyleHeldRotYaw[i].SettingChanged += Reapply;
            StyleHeldRotRoll[i].SettingChanged += Reapply;
        }
        // A hand-style SWITCH changes which per-style set is active — re-pose a held mini
        // right away (the Active* accessors read the new style on the next call).
        if (Plugin.HandStyle != null)
            Plugin.HandStyle.SettingChanged += Reapply;

        // HeldFigureInfo flipped OFF while a figure is held: the open panel must close NOW, not
        // linger until release ("mach diese aber auch optional … deaktivierbar" — an off switch
        // that leaves the panel standing is not off). ClearHeldFigure with a null actor clears
        // whatever that hand registered and is a no-op for an empty hand, so this is safe to fire
        // regardless of hold state. Flipping ON registers nothing retroactively — the seam is the
        // pickup (StatPanelSurface.ShowHeldFigure), so the panel returns on the next grab.
        HeldFigureInfo.SettingChanged += (_, _) =>
        {
            if (HeldFigureInfo.Value)
                return;
            WorldUI.Surfaces.StatPanelSurface.ClearHeldFigure(HandSide.Left, null);
            WorldUI.Surfaces.StatPanelSurface.ClearHeldFigure(HandSide.Right, null);
        };
    }
}
