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

    /// <summary>
    /// How far past <see cref="FigureGrabConfig.PickRadiusRealMeters"/> the figure that ALREADY
    /// holds the election keeps it — the release ring of the Schmitt trigger in
    /// <see cref="TickOffsetAnchorSelect"/>. A factor and not a second dial on purpose: a player
    /// tunes "how close must I get", never "how much slack does letting go get", and two dials that
    /// can be set to cross each other would need a third rule to sort them out.
    ///
    /// <para>1.25 = a quarter of the reach again. At the shipped 40 mm that is a 10 mm dead band,
    /// which is wider than the boundary drift the hardware log shows (39 → 38 → 37 mm on one
    /// figure) and far narrower than the distance between two minis on neighbouring hexes, so it
    /// cannot make a NEIGHBOUR sticky.</para>
    /// </summary>
    private const float PickExitFactor = 1.25f;

    /// <summary>
    /// Consecutive frames a NEW nearest figure must stay nearest before it is allowed to light up
    /// (the dwell in <see cref="TickOffsetAnchorSelect"/>). Six frames is ~67 ms at 90 Hz — under
    /// the ~100 ms at which a delay starts being felt as lag, and far longer than the one or two
    /// frames a figure owns the nearest slot while a hand sweeps past it.
    /// </summary>
    private const int PickDwellFrames = 6;

    /// <summary>The figure each hand's election currently rests on — the hysteresis state, indexed
    /// by <see cref="HandSide"/>. Distinct from <see cref="_lastElectedLeft"/>/<see cref="_lastElectedRight"/>,
    /// which are the LOG's dedupe and are written by <see cref="LogElection"/> itself: sharing them
    /// would make every election look unchanged to the log and silence it.</summary>
    private readonly FigureGrabbable?[] _electedBySide = new FigureGrabbable?[2];

    /// <summary>The candidate currently serving its dwell, per hand, and how many consecutive
    /// frames it has served. Reset by any change of candidate, so two figures alternating can never
    /// accumulate a dwell between them.</summary>
    private readonly FigureGrabbable?[] _pendingBySide = new FigureGrabbable?[2];

    private readonly int[] _dwellBySide = new int[2];

    // TURN-DEADLOCK GATE refusal feedback (see NoteBusyRefusal): one "no" per second per hand, and
    // the game's own negative ping as the last resort behind its invalid-option item.
    private const float BusyRefusalIntervalSeconds = 1f;
    private const string NegativePingFallback = "PlaySound_UIPingRewardNegative";
    private static readonly string[] BusyRefusalFallbacks = new string[1];
    private float _nextBusyRefusalLeft;
    private float _nextBusyRefusalRight;

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
        FigureStallWatchdog.Reset();
    }

    private void Update()
    {
        // FRAME-ORDER FigureGrabDriver.Update [FigureGrab.StallWatchdog, FigureGrab.Ghosts, FigureGrab.Glide, FigureGrab.HeldSize, GATE:FigureGrabConfig.GrabFigures, FigureGrab.Registry, FigureGrab.AutoRelease, FigureGrab.OffsetAnchorSelect, FigureGrab.LaserGrab]
        //   The GATE token is load-bearing, not decoration: StallWatchdog, Ghosts and Glide must run
        //   BEFORE the config gate's early-out. Ghosts so REMOTE-held ghosts still appear and clear
        //   while local figure-grab is off, Glide so a release glide already in flight still lands
        //   when the toggle is flipped mid-air, StallWatchdog so a turn already stalled before the
        //   toggle was flipped off still gets repaired (turning figure-grab off must not strand the
        //   session in a dead turn machine). Moving any of them below the gate strands a mini in the
        //   air on a remote peer — a bug that is invisible in single-player and invisible to
        //   refactor-guard.sh (it is an ordinary in-type diff). Locked; reordering is Tier 3.
        // TURN-DEADLOCK BACKSTOP (user, 2026-08-11) — repair a choreographer wait that a killed
        // AttackModBar coroutine has left blocked forever. Strict no-op unless a figure was held
        // during the stalled wait; see FigureStallWatchdog and FigureBusy for the whole account.
        TickGuard.Run("FigureGrab.StallWatchdog", FigureStallWatchdog.Tick);

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

            // The collider rides along for the highlight diagnostic only (FigureGrabbable.DescribeReach) —
            // the same one the election measures against, so the two readings are commensurable.
            var grabbable = new FigureGrabbable(actor, collider);
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
            if (!grabbable.IsHeld)
                continue;
            if (grabbable.AuthoritativeCellChanged())
            {
                grabbable.Restore();
                continue;
            }

            // TURN-DEADLOCK GATE, second belt. FigureGrabbable.AllowsHand already refuses a busy
            // figure, which makes ProximityGrabber.HealDeadHeld force-release it through the normal
            // path — that is the primary route and it also cleans up the hand's grab state. This is
            // here because the suppression the game hangs on lives in HeldFigures, not in the
            // grabber: if the grabber ever fails to tick (mode policy, interactor disabled, a hand
            // going untracked in the same frame) the actor must STILL leave HeldFigures, or
            // ActorBars keeps its bar host deactivated and the choreographer's untimed wait keeps
            // waiting. Restore() is idempotent, so the two paths cannot fight.
            if (FigureBusy.IsBusy(grabbable.Actor, out string why))
            {
                grabbable.Restore();
                VRLog.Info("FigureGrab",
                    $"AUTO-RELEASE (turn-deadlock gate): handed a held figure back to the game — {why}. "
                    + "Nothing of the mod's is holding its bar down any more.");
            }
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
            // pluck the player aimed at. Safe to clear for exactly one frame: on the next one the
            // holding branch of SelectByOffsetAnchor elects THIS figure (it is the one in the hand)
            // and vetoes every other, so the clear cannot widen into a second grabbable figure.
            // — that branch used to CLEAR the veto for the whole hand instead, which is the leak
            // ApplySuppression was written to end; this sentence was corrected with it.
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
    /// can only highlight/grab the winner. Runs every frame per hand, and the veto covers EVERY
    /// adopted figure, near or far — see <see cref="ApplySuppression"/> for the flashing-highlight
    /// defect that a distance-gated veto caused. The laser far-grab is untouched because it clears
    /// its own target's veto at the moment of the pluck (<see cref="TryLaserGrab"/>).
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
        if (_adoptions.Count == 0 || hand == null)
            return;

        // No usable hand (untracked) or already holding → there is no election to run, but the
        // veto still has to be WRITTEN this frame (see ApplySuppression: a frame in which a hand's
        // flags are not written is a frame in which the ProximityGrabber may highlight on stale
        // ones). So instead of clearing the veto — which would leave EVERY figure grabbable for a
        // hand that is not even tracked — we elect the figure this hand is HOLDING, if any, and
        // suppress the rest.
        //
        // Why the held figure has to stay allowed: ProximityGrabber.HealDeadHeld force-releases a
        // hold whose target starts refusing its holder (`AllowsHand`), so vetoing the mini in your
        // own hand would drop it with a warning. A held CARD (not one of ours) elects nobody, which
        // is also right — a hand with a card in it is not hovering a mini.
        if (!hand.HasPose || hand.Grabber.Held != null)
        {
            ApplySuppression(hand, hand.Grabber.Held as FigureGrabbable);
            LogElection(hand, null, 0f); // forget the candidate so re-entering it logs again
            // …and drop the hysteresis with it. A stale holder would otherwise keep the WIDE
            // exit ring across a grab or a tracking dropout, so the first figure met after it
            // would be admitted on the release radius instead of the reach radius.
            int idle = (int)hand.Side;
            _electedBySide[idle] = null;
            _pendingBySide[idle] = null;
            _dwellBySide[idle] = 0;
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
        // ---- NO FLASHING (user, ModBuild 105) -------------------------------------------------
        //
        // "das highlighting blitzt immer mal wieder auf bei verschiedenen Figuren, obwohl ich nach
        // deinem letzten fix zu weit weg sein sollte. Wenn ich mit der Hand richtig zu den Figuren
        // gehe ist es auch wie ich es will - verhindere dieses 'Aufblitzen'."
        //
        // The hardware log says he was NOT too far away — it says he was exactly ON the line:
        // "'Actor(Clone)' at 39 mm real from the pinch point (radius 40 mm)", then 38, then 37.
        // A bare radius is a step function, so a hand drifting along the boundary crosses it many
        // times a second, and each crossing is one highlight. Two mechanisms, because the report
        // describes two different flashes and one lever cannot answer both:
        //
        //   ENTER/EXIT (a Schmitt trigger) kills the chatter of ONE figure at the boundary: the
        //   winner has to come inside the configured radius, but it only LOSES the election past a
        //   wider exit radius. Between the two the answer is whatever it already was, so drift
        //   cannot toggle it. The exit ring is a factor rather than a second dial: a player tunes
        //   "how close do I have to get", not "how much slack does the release get".
        //
        //   DWELL kills the sweep across SEVERAL figures ("bei verschiedenen Figuren"): a NEW
        //   candidate must hold the election for a few consecutive frames before it is allowed to
        //   light up. Reaching for a mini clears that in well under the time it takes to notice;
        //   sweeping a hand across the board never does, because each figure owns the nearest slot
        //   for only a frame or two on the way past.
        //
        // Neither weakens the deliberate grab the user says already works: he ends up INSIDE the
        // radius and STAYS there, which is precisely the case both mechanisms are built to pass.
        float exitWorld = pickWorld * PickExitFactor;
        int side = (int)hand.Side;
        FigureGrabbable? held = _electedBySide[side];
        FigureGrabbable? winner = null;
        float bestAnchorDist = float.MaxValue;

        // TURN-DEADLOCK GATE, the LEGIBLE half. A busy figure is simply not a candidate (CanGrab is
        // false, like the MP grab-lock), so nothing lights up and the pinch does nothing — which on
        // its own reads as "the grab is broken". So on a TRIGGER EDGE ONLY we note the busy figure
        // the player was actually reaching for and answer it the way this project already answers a
        // refused click: the game's own invalid-option item through GameAudio, plus one log line.
        // Zero per-frame cost — the extra work happens on the frames the trigger goes down and on
        // no others, and only until the first busy figure inside the pick volume is found.
        bool scanRefusal = hand.TriggerDown;
        FigureGrabbable? refusedBusy = null;
        string refusedWhy = string.Empty;

        foreach (Adopted adopted in _adoptions.Values)
        {
            Collider collider = adopted.Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;
            if (!adopted.Grabbable.CanGrab)
            {
                if (scanRefusal && refusedBusy == null
                    && FigureBusy.IsBusy(adopted.Grabbable.Actor, out string busyWhy)
                    && Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor)) <= pickWorld)
                {
                    refusedBusy = adopted.Grabbable;
                    refusedWhy = busyWhy;
                }
                continue;
            }
            if (Vector3.Distance(palm, collider.ClosestPoint(palm)) > reach)
                continue; // not a proximity candidate this frame
            float anchorDist = Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor));
            // The figure that already holds the election keeps it out to the EXIT ring; anything
            // else has to come inside the configured radius to take it.
            float admit = ReferenceEquals(adopted.Grabbable, held) ? exitWorld : pickWorld;
            if (anchorDist > admit)
                continue; // in the palm's reach, but not in the PINCH — you have to reach for a mini
            if (anchorDist < bestAnchorDist)
            {
                bestAnchorDist = anchorDist;
                winner = adopted.Grabbable;
            }
        }

        // DWELL. A candidate that is not the one already lit has to be asked for
        // PickDwellFrames consecutive frames before it becomes the winner; until then the
        // PREVIOUS winner stands (or nothing does). Counted per hand, reset by any change of
        // candidate, so the count can never be accumulated by two figures alternating.
        if (!ReferenceEquals(winner, held))
        {
            if (!ReferenceEquals(winner, _pendingBySide[side]))
            {
                _pendingBySide[side] = winner;
                _dwellBySide[side] = 1;
            }
            else
            {
                _dwellBySide[side]++;
            }
            // Letting GO is immediate — only ARRIVING has to be earned. A hand that has genuinely
            // left every figure must stop highlighting in the same frame, or the release would
            // itself become a lag the player feels as stickiness.
            if (winner != null && _dwellBySide[side] < PickDwellFrames)
                winner = held;
        }
        else
        {
            _pendingBySide[side] = winner;
            _dwellBySide[side] = PickDwellFrames;
        }
        _electedBySide[side] = winner;
        LogElection(hand, winner, bestAnchorDist);

        ApplySuppression(hand, winner);

        if (refusedBusy != null && winner == null)
            NoteBusyRefusal(hand, refusedBusy, refusedWhy);
    }

    /// <summary>
    /// Say NO out loud when a pinch lands on a figure the turn machine is currently depending on
    /// (<see cref="FigureBusy"/>). Uses the project's EXISTING refusal recipe rather than a new one:
    /// the game's own invalid-option audio item
    /// (<c>UIInfoTools.generalAudioButtonProfile.nonInteractableMouseDownAudioItem</c>) played
    /// listener-anchored through <see cref="Core.GameAudio"/>, falling back to the game's negative
    /// ping — exactly the chain <c>WorldUI/Surfaces/InitiativePortraitClickSound</c> resolved for the
    /// initiative-track refusal (see that file for why the positional overload is silent in VR).
    ///
    /// <para>Throttled to once per second per hand, like <c>ProximityGrabber.LogRefusal</c>, so a
    /// player mashing the trigger at a resolving enemy hears one clear "no" and not a rattle.
    /// Strict no-op offline and online alike: it plays a local sound and writes a local line.</para>
    /// </summary>
    private void NoteBusyRefusal(VRHand hand, FigureGrabbable figure, string why)
    {
        bool left = hand.Side == HandSide.Left;
        float next = left ? _nextBusyRefusalLeft : _nextBusyRefusalRight;
        if (Time.unscaledTime < next)
            return;
        if (left)
            _nextBusyRefusalLeft = Time.unscaledTime + BusyRefusalIntervalSeconds;
        else
            _nextBusyRefusalRight = Time.unscaledTime + BusyRefusalIntervalSeconds;

        // Read field-by-field, never through UIInfoTools.InvalidOptionAudioItem: that property is an
        // unchecked `generalAudioButtonProfile.nonInteractableMouseDownAudioItem` and NREs before a
        // scene has assigned the profile (UIInfoTools.cs:467).
        UIInfoTools tools = UIInfoTools.Instance;
        AudioButtonProfile? general = tools != null ? tools.generalAudioButtonProfile : null;
        string preferred = general != null ? general.nonInteractableMouseDownAudioItem ?? string.Empty : string.Empty;
        BusyRefusalFallbacks[0] = NegativePingFallback;
        bool played = Core.GameAudio.PlayListenerAnchored(preferred, BusyRefusalFallbacks,
                                                          out string item, out bool valid, out string note);

        VRLog.Info("FigureGrab",
            $"{hand.Side} grab REFUSED on {figure.Label} — {why}. This is the turn-deadlock gate "
            + "(user report 2026-08-11): a figure the game is waiting on may not be picked up, "
            + "because hiding its actor bar under the hand kills the very coroutine the "
            + "choreographer's untimed wait needs. Refusal sound: item "
            + $"'{item}' valid={valid} played={played}{note}.");
    }

    /// <summary>
    /// Pass 2 — publish this frame's election as a per-hand veto: EVERY adopted figure except
    /// <paramref name="winner"/> refuses <paramref name="hand"/> (<see cref="FigureGrabbable.AllowsHand"/>),
    /// so <see cref="ProximityGrabber"/> can only ever highlight and grab the one figure this driver
    /// elected.
    ///
    /// ---- WHY THIS IS UNCONDITIONAL (user, ModBuild 106) -------------------------------------
    ///
    /// "Wenn ich meine Hand über die Spielfiguren halte werden sie zwar nicht mehr dauerhaft
    /// ausgewählt sondern das highlighting blitzt immer mal wieder auf bei verschiedenen Figuren,
    /// obwohl ich nach deinem letzten fix zu weit weg sein sollte. Wenn ich mit der hand richtug zu
    /// den figuren gehe ist es auch wie ich es will - verhindere dieses 'Aufblitzen'."
    ///
    /// ROOT CAUSE, and it is NOT the election. This driver does not raise the highlight at all —
    /// <see cref="ProximityGrabber.UpdateHighlight"/> does, on the nearest registered grabbable
    /// inside its CARD-sized 13 cm palm reach that still allows the hand. The election is only a
    /// VETO, and it used to be published for palm-reach figures ONLY:
    ///
    ///     bool inReach = … ≤ reach;
    ///     SetProximitySuppressed(side, inReach &amp;&amp; !winner);   // ← everything else: NOT suppressed
    ///
    /// A figure outside the palm sphere was therefore actively written back to "allowed", and the
    /// grabber ticks in its own driver (Hands) — one frame's flags in arrears. So on the frame a
    /// figure CROSSED INTO the 13 cm sphere it was still un-vetoed, while every figure that had
    /// been in the sphere longer was already vetoed; the newcomer was thus the only ALLOWED
    /// candidate and won the grabber's "nearest" by default. One frame of amber glow on the figure
    /// at the far EDGE of the palm reach — repeating on figure after figure as a hovering hand
    /// drifts. That is "blitzt auf bei VERSCHIEDENEN Figuren", and it is why the hand felt "zu weit
    /// weg": the leak fires at the 130 mm palm reach, not at the 40 mm pick radius.
    ///
    /// The hardware log of ModBuild 106 (5f3e3807f) is unambiguous about the mechanism: 137
    /// "pre-grab highlight ENGAGED" lines against 9 "pinch candidate" elections. The hysteresis and
    /// the dwell added in 76daf29 stabilise the ELECTION, and the election was never what lit those
    /// 128 other figures.
    ///
    /// The veto is now written for every adopted figure on every frame a hand is ticked, so it can
    /// never be stale and it FAILS CLOSED: a figure is grabbable only because this driver said so
    /// THIS frame. That also makes the fix independent of the Update order between HandsDriver and
    /// this driver, which Unity does not define.
    ///
    /// REJECTED — shrinking [FigureGrab] PickRadiusMillimeters. The leak fires on the palm reach,
    /// so the dial the player would be told to turn is not the one in the causal chain; it would
    /// have made deliberate grabs harder and left the flashing exactly where it was.
    /// REJECTED — narrowing ProximityGrabber's 13 cm reach. That reach is a CARD's reach; the hand
    /// fan is measured in it (see FigureGrabConfig.PickRadiusRealMeters), and figures may not drag
    /// the card fan's ergonomics along behind them.
    /// REJECTED — pinning the two drivers with [DefaultExecutionOrder]. It would hide this bug
    /// behind an ordering the project explicitly refuses to freeze (see the note in TryLaserGrab),
    /// and the veto would still be one frame old the moment anything else moved.
    ///
    /// The far LASER grab is untouched: it clears its own target's veto immediately before
    /// <c>ForceGrab</c> (see <see cref="TryLaserGrab"/>, which already had to, and says why).
    /// </summary>
    private void ApplySuppression(VRHand hand, FigureGrabbable? winner)
    {
        foreach (Adopted adopted in _adoptions.Values)
            adopted.Grabbable.SetProximitySuppressed(hand.Side, !ReferenceEquals(adopted.Grabbable, winner));
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

    // NOTE — there is deliberately no ClearSuppression(side) any more. It existed for the
    // "untracked hand / already holding" branch of SelectByOffsetAnchor, and clearing the veto for
    // a whole hand is precisely the state this round's defect was made of: the next frame the
    // ProximityGrabber ticks (it ticks first) it would see every figure allowed and light up the
    // nearest one inside its 13 cm palm reach. That branch now publishes a veto like any other —
    // see ApplySuppression, which also explains why the held figure is the winner there.

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
