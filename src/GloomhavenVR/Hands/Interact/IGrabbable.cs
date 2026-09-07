using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Something a hand can grab (FROZEN Phase-2 API).
///
/// Implement on a MonoBehaviour and register it together with a Collider via
/// <see cref="VRInteractables.RegisterGrabbable"/> — or derive from
/// <see cref="GrabbableBehaviour"/> which also handles snap-to-hand parenting.
///
/// Flow: when the palm comes within reach and <see cref="CanGrab"/> is true the object
/// is highlighted (see <see cref="IGrabHighlight"/>); a grip press calls
/// <see cref="OnGrab"/>; releasing the grip calls <see cref="OnRelease"/> with the
/// measured palm velocity (world units/s — already diorama-scaled).
/// The grabber does NOT reparent anything itself; the grabbable decides what
/// "being held" means (typically: snap to <c>hand.Rig.GrabAnchor</c>).
/// </summary>
internal interface IGrabbable
{
    /// <summary>Gate: return false to be ignored (e.g. card not selectable right now).</summary>
    bool CanGrab { get; }

    /// <summary>
    /// True: this grabbable is ALWAYS taken with the GRIP button, ignoring the
    /// <c>[Cards] GrabButton</c> config (hardware test #27: boards/world panels grip-
    /// grab, more intuitive than the Demeo trigger). False: obey the config — Trigger
    /// (Demeo default) or Grip — UNLESS the target is trigger-only: cards (typed into
    /// <c>ProximityGrabber.IsTriggerOnly</c>, user 2026-08-11) and
    /// <see cref="ITriggerOnlyGrabbable"/> markers take the trigger whatever the config
    /// says. net472 has no default-interface-method support, so every
    /// <see cref="IGrabbable"/> implementer states this explicitly (the
    /// <see cref="GrabbableBehaviour"/> base defaults it to false for cards).
    /// </summary>
    bool GrabWithGrip { get; }

    /// <summary>The hand closed on the object.</summary>
    void OnGrab(VRHand hand);

    /// <summary>The hand released the object. <paramref name="velocity"/> is the palm velocity (world units/s).</summary>
    void OnRelease(VRHand hand, Vector3 velocity);
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/>: hover highlight hook
/// (emissive pulse etc.). Called with true when this becomes the grab candidate of a
/// hand, false when it stops being one.
/// </summary>
internal interface IGrabHighlight
{
    void OnGrabHighlight(VRHand hand, bool highlighted);
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/> (P7 additive, hardware test #10):
/// per-hand gate. The <see cref="ProximityGrabber"/> skips this target entirely
/// (no highlight, no grab, no haptic) for hands the filter rejects. The card fan uses
/// this to exclude the fan-owning hand — its palm sits INSIDE the fan, and its own
/// proximity hover made two cards flip-flop highlights forever without user input.
/// </summary>
internal interface IGrabbableHandFilter
{
    /// <summary>Return false to make this target invisible to <paramref name="hand"/>.</summary>
    bool AllowsHand(VRHand hand);
}

/// <summary>
/// Optional MARKER companion to <see cref="IGrabbable"/> (additive, hardware MP test 2026-08,
/// user requirement (b): "Figuren sollen — wie die Karten — nur mit dem Trigger aufgenommen
/// werden können"). A target carrying this marker is picked up by the TRIGGER edge ONLY:
/// <see cref="ProximityGrabber"/> withholds its universal "a closing fist (grip) always grabs
/// the highlighted candidate" fallback for it, so a grip squeeze near the target — the most
/// common accidental gesture over a crowded board — never starts a hold. Board figures
/// (<c>Board.FigureGrab.FigureGrabbable</c>) carry it. CARDS are trigger-only too (user
/// 2026-08-11: "Die Karten sollen nur mit dem trigger nehmbar sein") but do not carry the
/// marker — they live in Cards/ and are named by TYPE in
/// <c>ProximityGrabber.IsTriggerOnly</c> instead (the grip fallback they once had — the
/// un-grabbable placed pick card of 2026-08-04 — became obsolete when the pick take-back was
/// fixed structurally; see that method's doc). Meaningless on a
/// <see cref="IGrabbable.GrabWithGrip"/> target (that branch never consults it).
/// </summary>
internal interface ITriggerOnlyGrabbable
{
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/> (additive, ModBuild 473): THIS TARGET MEASURES
/// ITS OWN REACH, and the election must ask it instead of its collider.
///
/// <para><b>Why the registered collider is not always enough.</b> Every reach test in this mod is
/// <c>Vector3.Distance(point, collider.ClosestPoint(point))</c>, and
/// <see cref="VRInteractables.RegisterGrabbable"/> allows exactly ONE collider per target (its
/// first statement is <c>UnregisterGrabbable(target)</c>, and the election loops assume one entry
/// per target throughout). A target whose true volume is a UNION of shapes therefore cannot be
/// described by its entry: a board obstacle standing on three hexes had to register the bounding
/// box over all of them, which in the ModBuild 472 host log measured 3.6 x 3.4 HEXES across, read
/// 0 mm to a hand anywhere inside it, and so ate the figure on the neighbouring hex outright. See
/// <c>Board.FigureGrab.PropFootprint</c> for that account and its numbers.</para>
///
/// <para><b>It is a distance, not a policy.</b> The grabber still asks
/// <see cref="VRInteractables.IsUsablePickShape"/> about the registered collider first, still
/// applies <see cref="IGrabbable.CanGrab"/> and <see cref="IGrabbableHandFilter"/>, and still
/// elects the nearest candidate — only the number changes. A target that does not implement this
/// keeps the exact <c>ClosestPoint</c> expression it always had, so this can never move the reach
/// of anything that did not opt in.</para>
///
/// <para>Must be pure, allocation-free and free of scene queries: it is called once per registered
/// entry per hand per frame.</para>
/// </summary>
internal interface IGrabReachVolume
{
    /// <summary>Distance from <paramref name="point"/> to this target's reach volume in WORLD
    /// units — zero inside it, the true Euclidean gap outside.</summary>
    float ReachDistance(Vector3 point);
}

/// <summary>Which class of board object a grabbable is — see <see cref="IBoardGrabTarget"/>.</summary>
internal enum BoardGrabKind
{
    /// <summary>A board miniature (<c>Board.FigureGrab.FigureGrabbable</c>).</summary>
    Figure,

    /// <summary>Board scenery: an obstacle, chest, trap, gold pile
    /// (<c>Board.FigureGrab.GrabbableProp</c>).</summary>
    Prop,
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/> (additive, ModBuild 473): a target that is a
/// BOARD object, and which kind of one.
///
/// <para><b>What it decides.</b> Exactly one rule, in exactly one place — see
/// <c>ProximityGrabber.UpdateHighlight</c>: when a FIGURE and a PROP are both admissible for the
/// same hand in the same frame, the FIGURE wins regardless of which is the nearer. It answers the
/// half of the 2026-09-07 report that a correct footprint alone cannot: a figure standing on the
/// hex NEXT TO an obstacle is close to that obstacle's boundary either way, and if the election is
/// a bare distance comparison then two centimetres of hand tremor decide it. A figure is the
/// finer-grained and far more often wanted target, and a prop beside it can always be reached by
/// stepping off the figure — the reverse is not true.</para>
///
/// <para><b>It is deliberately not a general precedence ladder.</b> Only these two kinds carry it.
/// Cards, tray bars and world panels implement nothing here and are compared on distance exactly
/// as before, so nothing outside the board can win or lose an election because of this
/// interface.</para>
/// </summary>
internal interface IBoardGrabTarget
{
    /// <summary>Figure or prop. Constant for the life of the target.</summary>
    BoardGrabKind BoardKind { get; }

    /// <summary>The target's own short name — the vocabulary the rest of its subsystem's log lines
    /// use, so a hover line and a grab line read as one story. Log only.</summary>
    string BoardLabel { get; }

    /// <summary>
    /// One clause describing the volume this target just answered over, or an empty string when it
    /// has nothing to add. Log only, and it is how the hover line says WHICH of a multi-hex prop's
    /// hexes replied — the field the 2026-09-07 report needs and which only the target itself can
    /// know. Hands deliberately does not name Board types; this member is why it does not have to.
    /// </summary>
    /// <param name="point">The point the election measured from.</param>
    /// <param name="handWorldScale">Rig world scale, so lengths can be quoted in real millimetres
    /// at the hand — the unit every reach number in this project is stated in.</param>
    string DescribeBoardReach(Vector3 point, float handWorldScale);
}
