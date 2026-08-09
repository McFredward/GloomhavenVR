using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Registers a <see cref="FigureGrabbable"/> against every live board figure and drives
/// the far laser point-and-grab (P8). Enumeration mirrors <c>WorldUI/ActorBars.Tick</c>:
/// it walks the game's own <c>WorldspaceUITools.Instance._panelUIControllers</c> registry,
/// takes each controller's tracked figure GameObject (<c>m_ObjectToTrack</c>), resolves the
/// figure's <c>CInteractableActor</c> collider + <c>ActorBehaviour</c>, and adopts/prunes
/// one grabbable per actor.
///
/// Near reach-and-close grabs are handled entirely by <see cref="ProximityGrabber"/>
/// (the TRIGGER, since the grabbable is no longer <c>GrabWithGrip</c> — same button +
/// <c>Ray.HasFreshUiHit</c> arbitration as the hand cards). This driver only adds the FAR
/// path: while the hand's ray points at a grabbable figure it clamps the beam to the mini
/// (<c>Ray.UiHitOverride</c>) so the trigger over a figure grabs it instead of doubling as
/// a board far-click, and on TriggerDown <c>ForceGrab</c> plucks it (released on
/// TriggerUp) — the exact trigger pluck the card fan uses. A fan card / world-UI panel that
/// clamped the beam closer this frame keeps the trigger (we defer on a FOREIGN
/// <c>HasFreshUiHit</c>, ignoring our own figure clamp).
/// </summary>
internal sealed class FigureGrabDriver : MonoBehaviour
{
    private sealed class Adopted
    {
        public FigureGrabbable Grabbable = null!;
        public Collider Collider = null!;
    }

    // Keyed by the figure's interactable collider component (Unity-nullable key, like
    // ActorBars' controller map — a destroyed key stays a valid CLR dictionary key).
    private readonly Dictionary<CInteractableActor, Adopted> _adoptions = new();
    private readonly List<CInteractableActor> _scratch = new(32);

    /// <summary>
    /// [Optimize] FigureScanCache: figure GameObject instance id → its CInteractableActor, so the
    /// per-frame registry sweep does not re-run a deep includeInactive hierarchy walk for figures
    /// it already knows. Keyed by INSTANCE ID (not the object) so a destroyed figure can never keep
    /// a Unity-null key alive in a way that hides a rebuilt one; a stale/destroyed value simply
    /// falls back to the full walk. Cleared with the adoptions.
    /// </summary>
    private readonly Dictionary<int, CInteractableActor> _figureInteractables = new(64);

    // Last frame THIS driver clamped the beam to a figure, per hand — so the far-grab
    // arbitration can tell our own fresh figure clamp apart from a FOREIGN UI/card clamp.
    private int _leftClampFrame = int.MinValue;
    private int _rightClampFrame = int.MinValue;

    // Item 3 — proximity reach for the offset-anchor selection. Mirrors ProximityGrabber's
    // private ReachMeters (0.13 m at scale 1) so the candidate set matches the palm reach the
    // grabber itself would consider before we narrow it to the offset-anchor-nearest figure.
    private const float ReachMeters = 0.13f;

    // Log dedupe (see LogPickVolume / LogElection): the last pick-volume line printed, and the
    // figure each hand last elected as its pinch candidate. Diagnostics only — nothing reads these
    // to decide anything.
    private string _lastPickVolumeLog = string.Empty;
    private FigureGrabbable? _lastElectedLeft;
    private FigureGrabbable? _lastElectedRight;

    // Cached per-frame tick delegates ([Optimize] CacheTickDelegates — see Update).
    private System.Action? _tickRegistry;
    private System.Action? _tickAutoRelease;
    private System.Action? _tickAnchorSelect;
    private System.Action? _tickLaserGrab;

    private void OnDestroy()
    {
        ReleaseAll();
        FigureGrabbable.FinishAllGlides(); // land any in-flight release glide (no stale suppression)
        FigureGhosts.Clear();
        FigureRingSuppressor.Clear();
    }

    private void Update()
    {
        // FRAME-ORDER FigureGrabDriver.Update [FigureGrab.Ghosts, FigureGrab.Glide, FigureGrab.HeldSize, GATE:FigureGrabConfig.GrabFigures, FigureGrab.Registry, FigureGrab.AutoRelease, FigureGrab.OffsetAnchorSelect, FigureGrab.LaserGrab]
        //   The GATE token is load-bearing, not decoration: Ghosts and Glide must run BEFORE the
        //   config gate's early-out. Ghosts so REMOTE-held ghosts still appear and clear while
        //   local figure-grab is off, Glide so a release glide already in flight still lands when
        //   the toggle is flipped mid-air. Moving either below the gate strands a mini in the air
        //   on a remote peer — a bug that is invisible in single-player and invisible to
        //   refactor-guard.sh (it is an ordinary in-type diff). Locked; reordering is Tier 3.
        // TASK #3 — reconcile home-spot ghosts against the local + remote held-sets (spawn is done at
        // grab time; this only tears down ghosts whose figure was released, incl. remote releases).
        // Runs even when local figure-grab is disabled so REMOTE-held ghosts still appear/clear.
        TickGuard.Run("FigureGrab.Ghosts", FigureGhosts.Tick);

        // GLIDE-BACK — advance every in-flight release glide (released mini easing home). Before
        // the config gate so a glide started just before GrabFigures was toggled off still lands
        // (the toggle's ReleaseAll → Restore also finishes glides instantly as a backstop).
        TickGuard.Run("FigureGrab.Glide", FigureGrabbable.TickGlides);

        // SIZE PARITY — re-assert every held mini's BOARD world size. Above the config gate for the
        // same reason as Glide: a mini still in the hand when GrabFigures is toggled off is released
        // by the gate's ReleaseAll on THIS frame, and it must not be rendered at a zoom-drifted size
        // for the frame in between. Cheap and a strict no-op when nothing is held.
        TickGuard.Run("FigureGrab.HeldSize", FigureGrabbable.TickHeldScale);

        if (!FigureGrabConfig.GrabFigures.Value)
        {
            if (_adoptions.Count > 0)
                ReleaseAll();
            return;
        }

        // [Optimize] CacheTickDelegates (2026-07 perf pass): these four used to allocate a fresh
        // Action from an instance method group EVERY FRAME — four of the mod's seven such sites,
        // ~256 B/frame from this driver alone, all of it gen0 garbage whose collection pauses show
        // up as exactly the head-turn judder being investigated. Cached now; the toggle re-creates
        // them per frame so the cost can be A/B'd against the [Perf] gc/alloc counters.
        bool cache = PerfConfig.CacheDelegates;
        TickGuard.Run("FigureGrab.Registry", cache ? _tickRegistry ??= RefreshRegistry : RefreshRegistry);
        TickGuard.Run("FigureGrab.AutoRelease", cache ? _tickAutoRelease ??= AutoReleaseMovedFigures : AutoReleaseMovedFigures);
        TickGuard.Run("FigureGrab.OffsetAnchorSelect", cache ? _tickAnchorSelect ??= TickOffsetAnchorSelect : TickOffsetAnchorSelect);
        TickGuard.Run("FigureGrab.LaserGrab", cache ? _tickLaserGrab ??= TickLaserGrab : TickLaserGrab);

        // (Issue A) The held rotation is a FIXED CONSTANT anchor-LOCAL rotation
        // (FigureGrabConfig.HeldUprightRotation) applied at grab — no world-up / head derivation and
        // no per-frame re-derivation — so it RIDES THE HAND (turning the hand turns the mini) while
        // the grab approach angle never changes the resting hold and it never clips into the palm.

        // Keep the held figure's stat panel locked to that figure even if the laser sweeps
        // another figure on the board (risk #5 — the game's hover would otherwise re-target).
        if (HeldFigures.Count > 0)
            StatPanelSurface.ReassertHeld();
    }

    /// <summary>
    /// Issue C — freeze the animation-driven position of every held figure (local + remote) AFTER
    /// the Animator has run this frame. A figure grabbed mid-walk (or in any animation) would
    /// otherwise translate straight out of the hand: the suppressed <c>ActorBehaviour.ApplyMotion</c>
    /// is what normally re-zeros the animated mesh each LateUpdate. Re-pinning it here reinstates
    /// exactly that one write so the mesh rides the hand while the clip keeps playing. Runs in
    /// LateUpdate precisely so it lands after the animation update; a strict no-op when nothing is
    /// held (offline or otherwise).
    /// </summary>
    private void LateUpdate()
    {
        // FRAME-ORDER FigureGrabDriver.LateUpdate LateUpdate-required [HeldFigures.PinAnimatedRoots, NetHeldFigures.PinAnimatedRoots, FigureRingSuppressor.Tick]
        //   These three MUST run in LateUpdate, after the Animator. The doc comment above says
        //   why; this line is the machine-checked form of it. The change it exists to stop is
        //   "merge Update and LateUpdate for symmetry" — which would put the pin BEFORE the
        //   animation update and translate every held figure straight out of the hand.
        if (HeldFigures.Count > 0)
            TickGuard.Run("FigureGrab.PinHeld", HeldFigures.PinAnimatedRoots);
        if (NetHeldFigures.Count > 0)
            TickGuard.Run("FigureGrab.PinNetHeld", NetHeldFigures.PinAnimatedRoots);

        // TASK #2 — keep the game's selection ring OFF under every in-hand figure (local AND
        // remote-held), restoring the game's intent on release; the ghost's own ring copy at the
        // home cell is the only ring the player sees while a figure is held. Runs in LateUpdate so
        // it lands after the game's Update writes and before render; unconditional (not gated on
        // GrabFigures) so remote-held figures stay covered, and a strict no-op when nothing is held.
        TickGuard.Run("FigureGrab.RingSuppress", FigureRingSuppressor.Tick);
    }

    private void RefreshRegistry()
    {
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null || Choreographer.s_Choreographer == null)
            return;

        // Adopt new figures.
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController controller = controllers[i];
            if (controller == null)
                continue;
            GameObject figure = controller.m_ObjectToTrack;
            if (figure == null)
                continue;

            // [Optimize] FigureScanCache (2026-07 perf pass): the GetComponentInChildren below is a
            // DEEP hierarchy walk (includeInactive, so it visits every disabled child of a rigged
            // character mesh), and it used to run for EVERY registered figure on EVERY frame —
            // including the ones already adopted, whose result is thrown away one line later by the
            // ContainsKey check. A figure never changes its CInteractableActor, so remembering the
            // resolution per figure turns the steady state (all figures adopted) into a dictionary
            // lookup per figure. Behaviour is unchanged: a destroyed/cleared entry falls back to the
            // full walk, so a figure that is rebuilt is picked up exactly as before.
            bool lean = Core.PerfConfig.FigureScanCacheOn;
            CInteractableActor? interactable = null;
            int figureId = 0;
            if (lean)
            {
                figureId = figure.GetInstanceID();
                if (_figureInteractables.TryGetValue(figureId, out CInteractableActor cached))
                {
                    if (cached != null && _adoptions.ContainsKey(cached))
                        continue;      // already adopted — nothing left to resolve this frame
                    interactable = cached; // may be Unity-null (destroyed) → re-resolved below
                }
            }
            if (interactable == null)
                interactable = figure.GetComponentInChildren<CInteractableActor>(includeInactive: true);
            if (interactable == null || _adoptions.ContainsKey(interactable))
                continue;
            if (lean)
                _figureInteractables[figureId] = interactable;

            Collider? collider = interactable.GetComponent<Collider>();
            if (collider == null)
                collider = interactable.GetComponentInChildren<Collider>();
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(figure);
            if (collider == null || actor == null)
                continue;

            var grabbable = new FigureGrabbable(actor);
            VRInteractables.RegisterGrabbable(grabbable, collider);
            _adoptions[interactable] = new Adopted { Grabbable = grabbable, Collider = collider };
        }

        // Prune figures whose collider/actor died (actor removed / scene unloading).
        if (_adoptions.Count == 0)
            return;
        _scratch.Clear();
        foreach (KeyValuePair<CInteractableActor, Adopted> pair in _adoptions)
        {
            if (pair.Key == null || pair.Value.Collider == null)
                _scratch.Add(pair.Key!);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Drop(_scratch[i]);
    }

    /// <summary>
    /// R2 hardening: if the game moves a HELD figure to a new authoritative board cell (a networked
    /// move on a remote/enemy turn, or the actor is destroyed under us), restore it immediately so
    /// it never rides the hand at a stale board position and jumps on release. Restore is idempotent
    /// and leaves the grabber's logical hold to end normally on trigger-up (a no-op re-Restore).
    /// </summary>
    private void AutoReleaseMovedFigures()
    {
        foreach (Adopted adopted in _adoptions.Values)
        {
            FigureGrabbable grabbable = adopted.Grabbable;
            if (grabbable.IsHeld && grabbable.AuthoritativeCellChanged())
                grabbable.Restore();
        }
    }

    private void TickLaserGrab()
    {
        TryLaserGrab(VRHands.Left);
        TryLaserGrab(VRHands.Right);
    }

    private void TryLaserGrab(VRHand? hand)
    {
        if (hand == null || !hand.HasPose || !hand.Ray.Enabled)
            return;
        // Near reach-grab (a highlighted figure in the palm) belongs to the ProximityGrabber;
        // the far pluck only runs when the grabber is idle this frame.
        if (hand.Grabber.Held != null || hand.Grabber.Highlighted != null)
            return;
        if (!hand.Ray.TryGetPick(out PickPose pick) || !pick.HasHit || pick.HitCollider == null)
            return;

        CInteractableActor interactable = pick.HitCollider.GetComponentInParent<CInteractableActor>();
        if (interactable == null
            || !_adoptions.TryGetValue(interactable, out Adopted adopted)
            || !adopted.Grabbable.CanGrab)
            return;

        // ARBITRATION: a fan card / world-UI panel that clamped the beam this frame owns the
        // trigger. HasFreshUiHit is their signal; ignore our OWN figure clamp from last frame
        // (recorded below) so we never defer to ourselves.
        int myClampFrame = hand.Side == HandSide.Left ? _leftClampFrame : _rightClampFrame;
        bool foreignUi = hand.Ray.HasFreshUiHit && Time.frameCount - myClampFrame > 1;
        if (foreignUi)
            return;

        // Clamp the beam to the mini (reticle on the figure) AND suppress the board far-click /
        // game actor-select for this trigger press — the same UiHitOverride the fan laser uses.
        //
        // NO FRAME-ORDER MARKER, DELIBERATELY. This is the producer; consumers read it through
        // RayInteractor.HasFreshUiHit, on a different GameObject, so Unity's relative Update
        // order is undefined. That is by design: HasFreshUiHit's window is TWO frames
        // (`frameCount - _uiHitOverrideFrame <= 1`) PRECISELY so producer and consumer may sit
        // in either phase. Adding [DefaultExecutionOrder] to pin the phase would freeze an order
        // nothing relies on, and would quietly narrow that window's job to nothing.
        // See .planning/refactor/REVIEW-Hands-Board-Core.md §P2.
        hand.Ray.UiHitOverride = pick.HitPoint;
        if (hand.Side == HandSide.Left)
            _leftClampFrame = Time.frameCount;
        else
            _rightClampFrame = Time.frameCount;

        if (hand.TriggerDown)
        {
            // THE LASER IS A DELIBERATE AIM, so it is not subject to the proximity arbitration —
            // clear this hand's suppression on the target before plucking. Before the pinch-radius
            // gate this could not matter: SelectByOffsetAnchor always elected SOME winner among the
            // palm-reach figures, and TryLaserGrab early-outs while the grabber has a highlight, so
            // a suppressed figure was never a laser target either. Now that a figure can be inside
            // the palm reach with NO winner elected, the laser can reach one that is suppressed —
            // and ForceGrab consults the same AllowsHand filter, so without this it would refuse a
            // pluck the player aimed at. Safe to clear: the next frame either sees the hold (which
            // clears suppression for the whole hand anyway) or re-derives it from scratch.
            adopted.Grabbable.SetProximitySuppressed(hand.Side, false);
            hand.Grabber.ForceGrab(adopted.Grabbable, releaseOnTriggerUp: true);
        }
    }

    /// <summary>
    /// Item 3 — when a hand hovers over MULTIPLE figures, grab the one nearest the OFFSET ANCHOR
    /// (the point where the held mini appears, <c>GrabAnchor.TransformPoint(HeldOffsetFor(side))</c>)
    /// rather than nearest to the palm. We can't change <see cref="ProximityGrabber"/>'s palm-based
    /// metric, so instead we mark every figure EXCEPT the offset-anchor winner as suppressed for
    /// that hand (per-hand <see cref="IGrabbableHandFilter"/>): the grabber then skips the losers and
    /// can only highlight/grab the winner. Runs every frame per hand; uncontested figures (single
    /// figure in reach, or a far laser target out of proximity reach) are never suppressed, so the
    /// laser far-grab (which keeps using the ray pick) is untouched.
    ///
    /// <para>IT IS ALSO THE PICK VOLUME (user report 2026-08, accidental grabs). The same offset
    /// anchor that decides WHICH figure wins now decides WHETHER any figure wins at all: a
    /// candidate has to be within <see cref="FigureGrabConfig.PickRadiusRealMeters"/> — real metres
    /// at the hand, converted to world units by the rig's own scale — of the pinch point, or every
    /// figure is suppressed and nothing lights up. Doing it here rather than in
    /// <see cref="ProximityGrabber"/> is deliberate: that reach is 13 cm because a CARD is a
    /// hand-span wide, and narrowing it there would narrow the card fan with it.</para>
    /// </summary>
    private void TickOffsetAnchorSelect()
    {
        SelectByOffsetAnchor(VRHands.Left);
        SelectByOffsetAnchor(VRHands.Right);
    }

    private void SelectByOffsetAnchor(VRHand? hand)
    {
        if (_adoptions.Count == 0)
            return;

        // No usable hand (untracked) or already holding → nothing to arbitrate; clear this hand's
        // suppression on every figure so none is left stuck non-grabbable.
        if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
        {
            if (hand != null)
            {
                ClearSuppression(hand.Side);
                LogElection(hand, null, 0f); // forget the candidate so re-entering it logs again
            }
            return;
        }

        Vector3 palm = hand.Rig.PalmCenter.position;
        Vector3 offsetAnchor = hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));
        float reach = ReachMeters * hand.WorldScale;
        // THE PICK VOLUME, in REAL METRES AT THE HAND, converted to world units with the rig's own
        // lossyScale — the mod's zoom is a scale on the RIG, so "one real metre at the hand" is
        // `1 × hand.WorldScale` world units and nothing else. See FigureGrabConfig.PickRadiusRealMeters
        // for why a figure may not keep the interactor's card-sized 0.13 m palm reach.
        float pickWorld = FigureGrabConfig.PickRadiusRealMeters * hand.WorldScale;
        LogPickVolume(hand, pickWorld);

        // Pass 1: among figures within PALM reach (the grabber's own candidate set) AND inside the
        // pinch-radius volume, find the one nearest the OFFSET ANCHOR — the figure the user is
        // aiming the pinch at. The palm reach is the interactor's own gate and stays as the outer
        // filter (a figure it never considers can never be highlighted anyway); the pinch radius is
        // the tight one that decides what the player can actually pick up.
        FigureGrabbable? winner = null;
        float bestAnchorDist = float.MaxValue;
        foreach (Adopted adopted in _adoptions.Values)
        {
            Collider collider = adopted.Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;
            if (!adopted.Grabbable.CanGrab)
                continue;
            if (Vector3.Distance(palm, collider.ClosestPoint(palm)) > reach)
                continue; // not a proximity candidate this frame
            float anchorDist = Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor));
            if (anchorDist > pickWorld)
                continue; // in the palm's reach, but not in the PINCH — you have to reach for a mini
            if (anchorDist < bestAnchorDist)
            {
                bestAnchorDist = anchorDist;
                winner = adopted.Grabbable;
            }
        }
        LogElection(hand, winner, bestAnchorDist);

        // Pass 2: suppress every palm-reach candidate except the winner; clear everyone else. With
        // no winner (nothing inside the pinch radius) that suppresses ALL of them, which is the
        // whole point: a hand hovering a hand-span above the board highlights nothing at all.
        foreach (Adopted adopted in _adoptions.Values)
        {
            Collider collider = adopted.Collider;
            bool inReach = collider != null && collider.enabled && collider.gameObject.activeInHierarchy
                && adopted.Grabbable.CanGrab
                && Vector3.Distance(palm, collider.ClosestPoint(palm)) <= reach;
            bool suppressed = inReach && !ReferenceEquals(adopted.Grabbable, winner);
            adopted.Grabbable.SetProximitySuppressed(hand.Side, suppressed);
        }
    }

    /// <summary>
    /// THE NUMBERS BEHIND THE PICK VOLUME, printed whenever they change — the resolved radius in
    /// REAL millimetres at the hand, the world units that comes to at the current zoom, the zoom
    /// itself, and the width of one hex in the same real millimetres.
    ///
    /// <para>Written because "der Bereich ist zu groß" is unanswerable without them. The radius is
    /// a constant at the HAND and a variable on the BOARD (the mod zooms by scaling the rig, so the
    /// board keeps its world size while the player grows), and those two readings of the same
    /// number are what the report and the code disagreed about. The hex width is the third column
    /// for that reason: radius-in-hexes is the ratio the player actually sees next to a mini.</para>
    ///
    /// <para>Deduped on the formatted line, so it costs one line per zoom change or config edit —
    /// both of which are human acts — and nothing at all while the player just plays.</para>
    /// </summary>
    private void LogPickVolume(VRHand hand, float pickWorld)
    {
        float mm = FigureGrabConfig.PickRadiusRealMeters * 1000f;
        float scale = hand.WorldScale;
        float hexMm = scale > 1e-4f
            ? UnityGameEditorRuntime.s_TileSize.x / scale * 1000f
            : 0f;
        string line = $"PICK VOLUME: {mm:F0} mm real at the hand = {pickWorld:F2} world units "
                      + $"(rig world scale {scale:F2}); one hex is {hexMm:F0} mm real at this zoom, "
                      + $"so the volume spans {(hexMm > 1e-3f ? mm / hexMm : 0f):F2} hexes. Fixed at "
                      + "the hand — zooming the table changes the hex column, never the first.";
        if (line == _lastPickVolumeLog)
            return;
        _lastPickVolumeLog = line;
        VRLog.Info("FigureGrab", line);
    }

    /// <summary>
    /// One line per NEWLY elected pinch winner (per hand): which figure lit up and how far its own
    /// surface was from the pinch point, in the same real millimetres the dial is set in. This is
    /// the half that answers "it is STILL too big" — if a figure lights up at 38 mm the dial is
    /// simply set too wide, and if one lights up at 300 mm something else is wrong.
    /// </summary>
    private void LogElection(VRHand hand, FigureGrabbable? winner, float anchorDistWorld)
    {
        bool left = hand.Side == HandSide.Left;
        FigureGrabbable? last = left ? _lastElectedLeft : _lastElectedRight;
        if (ReferenceEquals(last, winner))
            return;
        if (left)
            _lastElectedLeft = winner;
        else
            _lastElectedRight = winner;
        if (winner == null)
            return; // losing the candidate is not news; only a new one carries a distance

        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        ActorBehaviour actor = winner.Actor;
        VRLog.Info("FigureGrab",
            $"{hand.Side} pinch candidate '{(actor != null ? actor.name : "?")}' at "
            + $"{anchorDistWorld / scale * 1000f:F0} mm real from the pinch point "
            + $"(radius {FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm).");
    }

    private void ClearSuppression(HandSide side)
    {
        foreach (Adopted adopted in _adoptions.Values)
            adopted.Grabbable.SetProximitySuppressed(side, false);
    }

    private void Drop(CInteractableActor key)
    {
        if (_adoptions.TryGetValue(key, out Adopted adopted))
        {
            adopted.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(adopted.Grabbable);
        }
        _adoptions.Remove(key);
    }

    private void ReleaseAll()
    {
        foreach (KeyValuePair<CInteractableActor, Adopted> pair in _adoptions)
        {
            pair.Value.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(pair.Value.Grabbable);
        }
        _adoptions.Clear();
        // [Optimize] FigureScanCache: the resolution cache is only ever a shortcut to the walk it
        // replaces, so dropping it with the adoptions keeps it bounded per scenario and guarantees
        // the next sweep re-resolves everything from scratch.
        _figureInteractables.Clear();
        HeldFigures.Clear();
    }
}
