using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Actor/monster stat panels as world panels near the inspected miniature
/// (ARCHITECTURE §7 — Phase 3a triggers the open by poking a miniature; this class
/// owns only the presentation).
///
/// Verified via ilspycmd (GH.Runtime.dll):
/// <code>
///   [RequireComponent(typeof(UIWindow))] public class ActorStatPanel : Singleton&lt;ActorStatPanel&gt;
///     public void Show(CActor actor)            // sets private CActor m_ActorShown (publicized)
///   [RequireComponent(typeof(UIWindow))] public class EnemyCurrentTurnStatPanel : Singleton&lt;...&gt;
///     public void Show(CEnemyActor enemyActor)  // sets private CEnemyActor _currentShownEnemy
///   Choreographer: public GameObject FindClientActorGameObject(CActor actor,
///       bool shouldReturnDummyActorsProp = false)
/// </code>
/// Both windows announce visibility through their UIWindow onShown/onHidden events.
/// The panel is anchored BESIDE the shown actor's miniature when its client
/// GameObject resolves, else it falls back to the StatPanel layout slot.
///
/// TEST #16 — the stat panel must never intercept the pointer:
/// - Converted NON-pokeable: both panels are purely informational (verified in the
///   decompiled classes: no Button/Selectable members, only viewers), so the host is
///   never registered in UguiPokeSurfaces — the far ray (RayUguiDriver) and fingertip
///   poke ignore it, and our UIManager.IsPointerOverUI patch (which reports
///   Ray.HasFreshUiHit / Poke.HoveredUi — registered surfaces only) can never go
///   true because of it. The host GraphicRaycaster is kept disabled too, so the
///   vanilla EventSystem answer ignores it as well.
/// - Anchored to the SIDE of the miniature, not straight above it: at +0.22 up the
///   panel sat exactly in the hand ray that was inspecting the miniature → the ray
///   hit the panel → IsPointerOverUI true → the game hid the panel → our release put
///   the ray back on the board → the game re-showed it — a 45 Hz show/hide loop that
///   also broke placement hover-arming.
/// - Release HYSTERESIS: a hide only releases the conversion after
///   <see cref="ReleaseDelaySeconds"/>; a re-show within the window simply cancels
///   the pending release (no re-conversion). If a window still churns
///   (&gt;<see cref="ChurnWarnCount"/> conversions in <see cref="ChurnWindowSeconds"/>)
///   one warning names it.
///
/// CRITICAL SINGLETON SAFETY (game-breaking-bug fix): the game's <c>Singleton&lt;T&gt;</c> base
/// nulls its static <c>_instance</c> UNCONDITIONALLY in <c>OnDestroy</c> and steals it in
/// <c>Awake</c>. An earlier dual-panel build Instantiated a live SECOND <c>ActorStatPanel</c>
/// (clone) — when that clone was destroyed at runtime its base OnDestroy nulled the game's
/// <c>Singleton&lt;ActorStatPanel&gt;.Instance</c>, and the next
/// <c>WorldspaceStarHexDisplay.HideActorStatPanel()</c> (invoked from
/// <c>Choreographer.ProcessMessage(StartTurn)</c> via the ENEMY_TURN banner's OnShown) threw an
/// NRE inside the message pump → the enemy turn never ran → soft-lock. The rework:
/// - the second panel is a DUMB VISUAL SNAPSHOT of the real panel's UI hierarchy: Instantiated
///   under an INACTIVE holder (so Awake — and therefore the Singleton steal — can never run),
///   with the <c>ActorStatPanel</c>/<c>UIWindow</c>/every Singleton-derived component
///   DestroyImmediate'd while still never-activated (Unity skips OnDestroy for components whose
///   Awake never ran, so the game's <c>_instance</c> is untouched at both ends of the copy's
///   life), THEN handed to the regular convert/place machinery;
/// - <see cref="HealSingleton"/> runs EVERY Tick regardless of state and re-asserts the last
///   known live real panel whenever <c>Instance</c> is observed null while that panel object
///   still exists — a belt-and-braces guard for the game loop.
/// No code path in this mod ever writes null (or anything but the REAL panel) into
/// <c>Singleton&lt;ActorStatPanel&gt;._instance</c>.
/// </summary>
internal sealed class StatPanelSurface
{
    /// <summary>
    /// Host-canvas sortingOrder for the converted stat panels — deliberately THE SAME tier as
    /// the floated modal.
    ///
    /// <para>Unity sorts transparent UI sortingLayer → SORTINGORDER first and only then by
    /// distance, and neither side writes depth. At the old order 10 the options menu (1000)
    /// therefore drew straight THROUGH an info panel held between it and the player: order said
    /// "menu last" and depth never got a vote. Matching the tier removed that.</para>
    ///
    /// <para>TRANSPARENCY ROUND: this is no longer what decides the draw. Every converted panel's
    /// live order is rewritten each LateUpdate from its eye distance
    /// (CanvasConversion.8.Order.cs), so an info panel held between the player and the menu
    /// occludes the menu because it IS nearer, not because of a tier and not because either side
    /// stamped depth into the gaps of the other. The constant is kept, and kept EQUAL to the modal
    /// tier, for what it still decides: the raycast tie-break, and the ladder's own tie-break
    /// between two panels the player cannot tell apart in depth — where an info panel and the menu
    /// it belongs to must not be separated by an arbitrary rule. Being above the board-button
    /// labels/keycap faces (≤3) is now a property of the whole ladder (base 100), not of this
    /// value. Real depth-writing geometry keeps occluding the panel regardless.</para>
    /// </summary>
    private const int StatPanelSortingOrder = ModalFallback.ModalHostSortingOrder;

    /// <summary>Hide→release hysteresis (unscaled seconds) — absorbs show/hide flicker.</summary>
    private const float ReleaseDelaySeconds = 0.3f;

    /// <summary>Churn telemetry: rolling window for the conversion counter (unscaled seconds).</summary>
    private const float ChurnWindowSeconds = 2f;

    /// <summary>Conversions inside one window above which the single churn warning fires.</summary>
    private const int ChurnWarnCount = 5;

    /// <summary>How long the REAL panel shows the second hand's figure before the static snapshot
    /// copy is taken (lets the game's async portrait/addressable loads land first).</summary>
    private const float CopySnapshotDelaySeconds = 0.35f;

    /// <summary>Give up on a pending snapshot after this long past the delay (e.g. the game's
    /// <c>CanShow()</c> gate refused the Show); the real panel then re-binds to the primary.</summary>
    private const float CopySnapshotTimeoutSeconds = 2f;

    private sealed class Watch
    {
        public Component? Attached;
        public UIWindow? Window;
        public ConvertedPanel? Panel;
        public bool PendingShow;

        /// <summary>Unscaled time at which a scheduled release fires; 0 = none pending.</summary>
        public float ReleaseAt;

        // Churn telemetry (test #16): conversions inside the rolling window.
        public float CycleWindowStart;
        public int CycleCount;
        public bool ChurnWarned;

        public UnityEngine.Events.UnityAction OnShown = null!;
        public UnityEngine.Events.UnityAction OnHidden = null!;
    }

    private readonly Watch _actorPanel = new();
    private readonly Watch _enemyTurnPanel = new();

    // ---- held-figure anchor override (P8, figure-grab) — PER HAND ---------------------
    // TASK #4 — a figure can be held in EACH hand, and BOTH get an info panel docked at their
    // own hand. We keep PER-HAND registrations (set by the Board-side FigureGrabbable, which does
    // not own a StatPanelSurface instance — hence static) and drive TWO panels from them:
    //   • the game's SINGLE ActorStatPanel Singleton shows the PRIMARY held figure (the first hand
    //     to grab — so a single hold behaves EXACTLY as before), docked at that hand;
    //   • the SECONDARY held figure gets a STATIC VISUAL SNAPSHOT of the panel (see the class doc's
    //     singleton-safety note — never a live second ActorStatPanel): the real panel briefly shows
    //     the second figure, a component-stripped copy of its hierarchy is taken, and the copy is
    //     parked at the second hand while the real panel re-binds to the first hand's figure.
    // If only one hand holds, no copy is created and behaviour is identical to the old single panel.
    private readonly struct HeldReg
    {
        public HeldReg(Transform anchor, ScenarioRuleLibrary.CActor actor, GloomhavenVR.Hands.HandSide side, long seq)
        { Anchor = anchor; Actor = actor; Side = side; Seq = seq; }
        public readonly Transform Anchor;
        public readonly ScenarioRuleLibrary.CActor Actor;
        public readonly GloomhavenVR.Hands.HandSide Side;
        public readonly long Seq; // grab order — lowest is PRIMARY (real panel)
    }

    // Index by HandSide (Left=0, Right=1); null = that hand holds nothing.
    private static readonly HeldReg?[] _reg = new HeldReg?[2];
    private static long _seqCounter;

    // Derived each reconcile: PRIMARY (real panel) + SECONDARY (snapshot copy) docking state.
    private static Transform? _heldAnchor;
    private static ScenarioRuleLibrary.CActor? _heldActor;
    private static float _heldSideSign = 1f;
    private static Transform? _held2Anchor;
    private static ScenarioRuleLibrary.CActor? _held2Actor;
    private static float _held2SideSign = 1f;

    // ---- second-panel STATIC SNAPSHOT state (singleton-safe; see class doc) ------------
    private static GameObject? _copyHolder;   // inactive parent — the copy Awakes only once converted
    private static RectTransform? _copyRect;  // the stripped copy's root RectTransform
    private static ConvertedPanel? _copyPanel;
    private static ScenarioRuleLibrary.CActor? _copyActor;    // the actor the current copy portrays
    private static ScenarioRuleLibrary.CActor? _pendingCopyActor; // snapshot requested for this actor
    private static float _pendingCopyAt;
    private static float _pendingCopyDeadline;

    /// <summary>Last-known REAL game panel — the ONLY value <see cref="HealSingleton"/> may ever
    /// write back into <c>Singleton&lt;ActorStatPanel&gt;._instance</c>. Never a copy (copies carry
    /// no ActorStatPanel component at all), never null.</summary>
    private static ActorStatPanel? _realPanel;

    /// <summary>Viewer-relative dock side for a hand: RIGHT hand → viewer-LEFT (-1), LEFT hand →
    /// viewer-RIGHT (+1), so the holding hand never occludes its own panel (item 5).</summary>
    private static float SignFor(GloomhavenVR.Hands.HandSide side)
        => side == GloomhavenVR.Hands.HandSide.Right ? -1f : 1f;

    /// <summary>Held-figure offset (side / up, real meters × world scale) — snug so it clears the hand.</summary>
    private const float HeldSideOffset = 0.15f;
    private const float HeldUpOffset = 0.05f;

    public string Name => "StatPanel";

    public StatPanelSurface()
    {
        _actorPanel.OnShown = () => _actorPanel.PendingShow = true;
        _actorPanel.OnHidden = () => ScheduleRelease(_actorPanel);
        _enemyTurnPanel.OnShown = () => _enemyTurnPanel.PendingShow = true;
        _enemyTurnPanel.OnHidden = () => ScheduleRelease(_enemyTurnPanel);

        // P5 (MISSION A.8): Board announces miniature pokes on the bus; opening the
        // game's own ActorStatPanel window here triggers the conversion above via its
        // UIWindow onShown — the presentation pipeline is unchanged.
        VREvents.MiniaturePoked += OnMiniaturePoked;
    }

    /// <summary>
    /// TASK #4: register a figure held in <paramref name="holdingHand"/> so its info panel docks at
    /// that hand. The FIRST-held hand takes the game's real ActorStatPanel; a SECOND-held hand takes
    /// a static snapshot copy. <see cref="Reconcile"/> then (re)binds both panels. Replaces the old
    /// single static held anchor.
    /// </summary>
    internal static void ShowHeldFigure(Transform anchor, ScenarioRuleLibrary.CActor? actor,
        GloomhavenVR.Hands.HandSide holdingHand)
    {
        if (actor == null || anchor == null)
            return;
        _reg[(int)holdingHand] = new HeldReg(anchor, actor, holdingHand, ++_seqCounter);
        Reconcile();
    }

    /// <summary>
    /// TASK #4: clear the held registration for <paramref name="holdingHand"/> (only if
    /// <paramref name="actor"/> still owns that slot) and re-bind the panels. If the OTHER hand still
    /// holds, its figure is promoted to the real panel and the snapshot copy torn down; if neither
    /// hand holds, both panels go away.
    /// </summary>
    internal static void ClearHeldFigure(GloomhavenVR.Hands.HandSide holdingHand,
        ScenarioRuleLibrary.CActor? actor)
    {
        int i = (int)holdingHand;
        HeldReg? cur = _reg[i];
        if (cur.HasValue && actor != null && !ReferenceEquals(cur.Value.Actor, actor))
            return; // that hand already re-registered a different figure
        ScenarioRuleLibrary.CActor? released = cur?.Actor ?? actor;
        _reg[i] = null;
        Reconcile(released);
    }

    /// <summary>
    /// Re-open the held figures' stat cards if the game's board hover re-targeted the shared real
    /// panel to another actor (risk #5). Cheap; called every frame while any figure is held.
    /// </summary>
    internal static void ReassertHeld()
    {
        if (CanvasConversion.IsLockedNow)
            return;
        Reconcile();
    }

    /// <summary>
    /// Derive PRIMARY (real panel) and SECONDARY (snapshot copy) from the per-hand registrations and
    /// bind each panel to its figure.
    ///
    /// <para>PRIMARY IS THE LATEST-GRABBED HAND, and that ordering is the whole trick. It used to be
    /// the earliest, which meant that picking up a second figure had to BORROW the real panel — show
    /// the new actor on it for a third of a second so the snapshot could capture populated stats,
    /// then hand it back. ForceShowOn does that with HideForActor() + Show(), so the FIRST figure's
    /// panel visibly blanked and came back every time the other hand grabbed something. That is the
    /// flicker.</para>
    ///
    /// <para>Turned around, there is nothing to borrow: at the instant the second figure is grabbed
    /// the panel is ALREADY showing the first one, fully populated, so the snapshot can be taken
    /// immediately, from what is on screen, and the live panel simply moves on to the newer figure
    /// and stays there. One panel is frozen either way; freezing the older one is also the better
    /// half of the trade, since the figure you just picked up is the one you are looking at.</para>
    ///
    /// <para>When the panel is NOT already showing the figure to freeze — both hands grabbing
    /// within one frame, or the panel closed at grab time — the same realTarget rule LENDS the
    /// panel to that figure for CopySnapshotDelaySeconds first (the game's async portrait loads
    /// need the time), so the NEWER figure's panel appears late instead of the older one blinking.
    /// Rare, and a late panel beats a snapshot of the wrong actor.</para>
    /// </summary>
    private static void Reconcile(ScenarioRuleLibrary.CActor? releasing = null)
    {
        HeldReg? primary = null, secondary = null;
        for (int i = 0; i < _reg.Length; i++)
        {
            HeldReg? r = _reg[i];
            if (!r.HasValue)
                continue;
            if (!primary.HasValue || r.Value.Seq > primary.Value.Seq)
            {
                secondary = primary;
                primary = r;
            }
            else
            {
                secondary = r;
            }
        }

        _heldActor = primary?.Actor;
        _heldAnchor = primary?.Anchor;
        _heldSideSign = primary.HasValue ? SignFor(primary.Value.Side) : 1f;
        _held2Actor = secondary?.Actor;
        _held2Anchor = secondary?.Anchor;
        _held2SideSign = secondary.HasValue ? SignFor(secondary.Value.Side) : 1f;

        bool canShow = WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive
                       && !CanvasConversion.IsLockedNow && Singleton<ActorStatPanel>.IsInitialized;

        // --- secondary (second hand) → request/keep a static snapshot copy ---
        if (!canShow || _held2Actor == null)
        {
            _pendingCopyActor = null; // copy teardown (if one exists) happens in TickCopy
        }
        else if (!ReferenceEquals(_copyActor, _held2Actor) && !ReferenceEquals(_pendingCopyActor, _held2Actor))
        {
            _pendingCopyActor = _held2Actor;
            // Shown already (the normal case — it WAS the primary until this very grab)? Snapshot
            // it as it stands, this tick: the panel has been populated for as long as the figure
            // was held. Not shown (rapid double-grab, panel was closed)? Then the panel must be
            // lent to it first, and the delay exists for the game's async portrait loads.
            bool alreadyShown = Singleton<ActorStatPanel>.IsInitialized
                                && ActorStatPanel.Instance != null
                                && ReferenceEquals(ActorStatPanel.Instance.m_ActorShown, _held2Actor);
            _pendingCopyAt = alreadyShown
                ? Time.unscaledTime
                : Time.unscaledTime + CopySnapshotDelaySeconds;
            _pendingCopyDeadline = _pendingCopyAt + CopySnapshotTimeoutSeconds;
        }

        // --- real panel: the pending-snapshot figure UNTIL ITS COPY EXISTS, else the primary.
        // This line is the no-flicker invariant: the live panel is never retargeted away from a
        // figure before that figure's snapshot is on screen. In the normal case the panel already
        // shows the pending figure, so ForceShowOn below no-ops, TickCopy snapshots it this same
        // tick, converts the copy, and only its own Reconcile call moves the live panel on — one
        // tick, no gap. Retargeting here immediately instead (tried in 886a9dc) left TickCopy
        // waiting for a panel state that could never come: it timed out after two seconds, and for
        // those two seconds the first figure had NO panel at all. The "snapshot timed out (game
        // refused Show)" log line was this bug, not the game refusing anything. ---
        ScenarioRuleLibrary.CActor? realTarget = _pendingCopyActor ?? _heldActor;
        if (canShow && realTarget != null)
            ForceShowOn(ActorStatPanel.Instance, realTarget);
        else if (_heldActor == null && Singleton<ActorStatPanel>.IsInitialized)
            HideHeldOn(ActorStatPanel.Instance, releasing); // no primary hold → drop our card if still ours
    }

    /// <summary>
    /// Show <paramref name="actor"/> in <paramref name="panel"/>, RETARGETING if needed. The game's
    /// <c>ActorStatPanel.Show</c> is gated by <c>CanShow()</c> (refuses while another actor is shown),
    /// so we clear the latch first. No-op when the right actor is already shown.
    /// </summary>
    private static void ForceShowOn(ActorStatPanel panel, ScenarioRuleLibrary.CActor actor)
    {
        if (panel == null || ReferenceEquals(panel.m_ActorShown, actor))
            return;
        if (panel.m_ActorShown != null)
            panel.HideForActor(); // clear m_ActorShown so CanShow() passes and Show() retargets
        panel.Show(actor);
    }

    /// <summary>Hide the held card on <paramref name="panel"/> if it still shows a held figure the
    /// game did not re-target (mirrors the old ClearHeldFigure nuance).</summary>
    private static void HideHeldOn(ActorStatPanel panel, ScenarioRuleLibrary.CActor? onlyIf)
    {
        if (panel == null)
            return;
        ScenarioRuleLibrary.CActor? shown = panel.m_ActorShown;
        if (shown == null)
            return;
        // Only hide OUR held figures — never a panel the game re-targeted to a board-hover actor.
        bool ours = ReferenceEquals(shown, _heldActor) || ReferenceEquals(shown, _held2Actor)
                    || (onlyIf != null && ReferenceEquals(shown, onlyIf));
        if (ours)
            panel.HideForActor(shown);
    }

    /// <summary>
    /// GAME-LOOP GUARD (mandatory, unconditional): the game reads
    /// <c>Singleton&lt;ActorStatPanel&gt;.Instance</c> without null checks in hot paths
    /// (<c>WorldspaceStarHexDisplay.HideActorStatPanel</c>/<c>ShowActorStatPanelForTile</c>, the
    /// former INSIDE <c>Choreographer.ProcessMessage(StartTurn)</c> — a null Instance there
    /// soft-locked the enemy turn). If Instance is ever observed null while the last known REAL
    /// panel object still exists, re-assert it. The only value ever written is the live real panel.
    /// </summary>
    private static void HealSingleton()
    {
        ActorStatPanel? live = Singleton<ActorStatPanel>.IsInitialized ? ActorStatPanel.Instance : null;
        if (live != null)
        {
            _realPanel = live; // cache the genuine game instance while it is alive
            return;
        }
        // Singleton observed null. If the real panel object was legitimately destroyed (scene
        // teardown) the Unity-null check leaves it alone; otherwise restore it.
        if (_realPanel != null)
        {
            _realPanel.SetInstance(_realPanel);
            VRLog.Warn("WorldUI", "ActorStatPanel singleton was null while the real panel still " +
                                  "exists — restored (game-loop guard).");
        }
    }

    private static void OnMiniaturePoked(MiniaturePokedEvent e)
    {
        if (!WorldUIConfig.StatPanels.Value || !WorldUIConfig.ConversionActive)
            return;
        if (CanvasConversion.IsLockedNow)
            return; // modality: no popups while the game locked its UI
        if (!Singleton<ActorStatPanel>.IsInitialized || e.Actor == null)
            return;
        // Verified via ilspycmd (GH.Runtime.dll): public void Show(CActor actor).
        ActorStatPanel.Instance.Show(e.Actor);
    }

    public void Tick()
    {
        HealSingleton(); // keep the game's ActorStatPanel singleton pointing at the real panel

        TickWatch(_actorPanel,
            Singleton<ActorStatPanel>.IsInitialized ? Singleton<ActorStatPanel>.Instance : null,
            "ActorStatPanel");
        TickWatch(_enemyTurnPanel,
            Singleton<EnemyCurrentTurnStatPanel>.IsInitialized ? Singleton<EnemyCurrentTurnStatPanel>.Instance : null,
            "EnemyCurrentTurnStatPanel");

        // The second hand's STATIC SNAPSHOT panel (never a live ActorStatPanel — class doc).
        TickCopy();
    }

    private void TickWatch(Watch watch, Component? live, string name)
    {
        if (watch.Panel != null && !watch.Panel.IsAlive)
            watch.Panel = null;

        if (!ReferenceEquals(live, watch.Attached))
        {
            DetachWatch(watch);
            watch.Attached = live;
            watch.Window = live != null ? live.GetComponent<UIWindow>() : null;
            if (watch.Window != null)
            {
                watch.Window.onShown.AddListener(watch.OnShown);
                watch.Window.onHidden.AddListener(watch.OnHidden);
                if (watch.Window.IsOpen)
                    watch.PendingShow = true;
            }
        }

        if (watch.PendingShow)
        {
            watch.PendingShow = false;
            if (watch.Panel != null)
            {
                // Re-shown inside the hysteresis window — keep the live conversion.
                watch.ReleaseAt = 0f;
            }
            else if (WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive
                && Choreographer.s_Choreographer != null && watch.Attached != null)
            {
                // Informational panel (no buttons, verified) — NOT pokeable: never in
                // UguiPokeSurfaces, so neither ray nor poke nor IsPointerOverUI see it.
                // flatten2D (test #21): the actor/enemy stat card carries the game's baked
                // local-z / local rotation (subtle perspective styling under the game's UI
                // camera) that becomes literal 3D geometry on a world-space host — text/icons
                // protrude out of the panel plane. Flatten zeros every descendant's local-z +
                // local rotation, and CanvasConversion.LateTick RE-runs the flatten EVERY frame
                // while shown (FlattenEnabled) so a NEW ENEMY TYPE repopulating the card with
                // fresh tilted stat rows / ability text can NEVER re-acquire the 3D tilt.
                watch.Panel = CanvasConversion.Convert(watch.Attached.transform as RectTransform, name,
                    pokeable: false, sortingOrder: StatPanelSortingOrder, flatten2D: true);
                if (watch.Panel != null)
                {
                    // MR backing opt-out (user ruling 2026-08-04, reported on the figure-grab
                    // info panel): the stat card carries the game's own parchment card art as
                    // backing, so MrBacking's host-rect plate only added a dark rectangle proud
                    // of the card. It is the SAME window whether opened by holding a figure or
                    // by poking a miniature, so the exclusion follows the surface, not the
                    // trigger. Per conversion — the show/hide release hysteresis re-converts
                    // this window constantly, and every fresh ConvertedPanel needs the flag.
                    watch.Panel.MrBackingSuppressed = true;
                    CountConversion(watch, name);
                    PlaceWatch(watch);
                }
            }
        }

        // Deferred release (hysteresis): the window stayed hidden past the delay.
        if (watch.Panel != null && watch.ReleaseAt > 0f && Time.unscaledTime >= watch.ReleaseAt)
            Release(watch);

        // Keep facing the player (billboard yaw only; cheap, allocation-free).
        if (watch.Panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (watch.Panel.HostRaycaster != null && watch.Panel.HostRaycaster.enabled)
                watch.Panel.HostRaycaster.enabled = false;
            PlaceWatch(watch);
        }
    }

    // ---- second-panel snapshot pipeline -------------------------------------------------

    /// <summary>
    /// Drive the second hand's snapshot copy: tear it down when the second hold ended (or its
    /// figure changed), take the pending snapshot once the real panel has shown the second figure
    /// long enough for its async content to land, and keep the copy converted + docked at the
    /// second hand. The copy is pure imagery — no ActorStatPanel, no UIWindow, no Singleton
    /// anything (see <see cref="BuildStaticCopy"/>).
    /// </summary>
    private static void TickCopy()
    {
        bool active = WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive;

        // Teardown: second hold gone, feature off, or the copy portrays the wrong figure.
        if (_copyHolder != null && (!active || _held2Actor == null || !ReferenceEquals(_copyActor, _held2Actor)))
            DestroyCopy();
        if (!active)
        {
            _pendingCopyActor = null;
            return;
        }

        // Pending snapshot: wait out the delay, then copy while the REAL panel shows the figure.
        if (_pendingCopyActor != null)
        {
            if (!ReferenceEquals(_pendingCopyActor, _held2Actor))
            {
                _pendingCopyActor = null; // stale request (hand released / figure swapped)
                Reconcile();
            }
            else if (Singleton<ActorStatPanel>.IsInitialized
                     && ActorStatPanel.Instance != null
                     && ReferenceEquals(ActorStatPanel.Instance.m_ActorShown, _pendingCopyActor)
                     && Time.unscaledTime >= _pendingCopyAt)
            {
                BuildStaticCopy(ActorStatPanel.Instance, _pendingCopyActor);
                _pendingCopyActor = null;
                Reconcile(); // re-bind the real panel to the FIRST hand's figure immediately
            }
            else if (Time.unscaledTime >= _pendingCopyDeadline)
            {
                // The game's CanShow() gate refused the Show (transition/results screen…) —
                // fall back to the single real panel on the primary figure rather than waiting.
                _pendingCopyActor = null;
                Reconcile();
                VRLog.Info("WorldUI", "second held-figure snapshot timed out (game refused Show) — " +
                                      "keeping the single real panel.");
            }
        }

        // Convert + dock the copy at the second hand (same machinery as the real panels).
        // flatten2D: the snapshot copy is a clone of the same tilted stat-card hierarchy, so it
        // needs the identical per-frame flatten (FlattenEnabled → LateTick) or the second hand's
        // enemy card protrudes in 3D exactly like the real one did.
        if (_copyPanel != null && !_copyPanel.IsAlive)
            _copyPanel = null;
        if (_copyHolder != null && _copyRect != null && _copyPanel == null)
        {
            _copyPanel = CanvasConversion.Convert(_copyRect, "ActorStatPanelCopy", pokeable: false,
                sortingOrder: StatPanelSortingOrder, flatten2D: true);
            // MR backing opt-out (user ruling 2026-08-04): the snapshot is a clone of the same
            // parchment-backed stat card — the second hand's copy must match the real panel's
            // no-plate treatment (see TickWatch) or only one of two held figures grows a dark
            // rectangle behind its card in see-through mode.
            if (_copyPanel != null)
                _copyPanel.MrBackingSuppressed = true;
        }
        if (_copyPanel != null)
        {
            if (_copyPanel.HostRaycaster != null && _copyPanel.HostRaycaster.enabled)
                _copyPanel.HostRaycaster.enabled = false;
            if (_held2Anchor != null
                && TryComputeHeldPose(_held2Anchor.position, _held2SideSign, out Vector3 pos, out Quaternion rot))
                CanvasConversion.PlaceHost(_copyPanel, pos, rot, PanelLayout.WorldScale * 0.6f);
        }
    }

    /// <summary>
    /// Build the DUMB VISUAL COPY of the real panel's UI hierarchy for <paramref name="actor"/>.
    /// Singleton safety (the load-bearing part):
    /// 1. the copy is Instantiated under an INACTIVE holder, so Unity NEVER calls Awake on any of
    ///    its components — the copied ActorStatPanel cannot steal <c>Singleton._instance</c>;
    /// 2. the <c>ActorStatPanel</c> (Singleton-derived; destroyed FIRST — it RequireComponent's the
    ///    UIWindow), every other Singleton-derived component, and the <c>UIWindow</c> are
    ///    DestroyImmediate'd while still never-activated — Unity does not invoke OnDestroy for
    ///    components whose Awake never ran, so <c>Singleton._instance</c> is untouched here too;
    /// 3. only then does <see cref="TickCopy"/> hand the bare imagery to CanvasConversion, whose
    ///    re-parent into the active host finally activates it — at that point no component with any
    ///    Singleton/window lifecycle exists anywhere in the copy.
    /// </summary>
    private static void BuildStaticCopy(ActorStatPanel real, ScenarioRuleLibrary.CActor actor)
    {
        DestroyCopy();

        var holder = new GameObject("GloomhavenVR.StatPanelCopyHolder");
        holder.SetActive(false); // MUST precede the Instantiate — keeps every Awake from running

        GameObject copy = Object.Instantiate(real.gameObject, holder.transform, false);
        copy.name = "GloomhavenVR.ActorStatPanelCopy";

        int stripped = StripLogicComponents(copy);

        // The window may have been copied mid fade-in — force the snapshot fully opaque and inert.
        var group = copy.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        int detached = DetachSharedImagery(real, copy);
        string diff = StructuralDiff(real.gameObject, copy);

        _copyHolder = holder;
        _copyRect = copy.transform as RectTransform;
        _copyActor = actor;

        VRLog.Info("WorldUI", $"second held-figure info panel snapshot created ({stripped} logic " +
                              $"component(s) stripped, {detached} portrait(s) detached; game " +
                              $"singleton untouched) — both hands' figures now show a panel each. {diff}");
    }

    /// <summary>
    /// Give the snapshot its own copy of the TWO images the game pulls out from under it — and
    /// nothing else.
    ///
    /// <para>WHY IT IS NEEDED: the portrait is not a plain sprite on a prefab. ActorStatPanel
    /// loads it through an addressable sprite loader — <c>_imageSpriteLoader.LoadAsync(this,
    /// enemyPortrait, ...)</c> — and later calls <c>_imageSpriteLoader.Unload(enemyPortrait)</c>
    /// (both verified in the real GH.Runtime.dll). That Unload destroys the texture, and a plain
    /// Instantiate snapshot still pointing at it goes BLANK the moment the real panel moves to the
    /// other actor or closes.</para>
    ///
    /// <para>WHY IT IS NOW TWO IMAGES AND NOT ALL OF THEM. The first version of this copied every
    /// texture in the panel and rebuilt every sprite on the copies. It fixed the portrait and broke
    /// the panel: a rebuilt Sprite is a NEW sprite, and a NINE-SLICED frame rebuilt that way stops
    /// slicing correctly — which is exactly the "the second box is square and missing pieces" that
    /// came back. The frames, icons and dividers were never in danger; only what the loader
    /// unloads is. So the two portrait Images are located by REFLECTION on the real panel
    /// (<c>characterPortrait</c> and <c>enemyPortrait</c>, both private <c>Image</c> fields),
    /// translated to their transform paths, and only those two are detached in the copy.</para>
    ///
    /// <para>Failures are per-image and swallowed: an image that cannot be copied keeps the shared
    /// original, which is no worse than before this existed.</para>
    /// </summary>
    private static int DetachSharedImagery(ActorStatPanel real, GameObject copy)
    {
        var paths = new List<string>(2);
        System.Reflection.FieldInfo[] fields = typeof(ActorStatPanel).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].FieldType != typeof(Image) ||
                fields[i].Name.IndexOf("ortrait", System.StringComparison.Ordinal) < 0)
                continue;
            if (fields[i].GetValue(real) is not Image img || img == null)
                continue;
            string? path = PathUnder(real.transform, img.transform);
            if (path != null)
                paths.Add(path);
        }
        if (paths.Count == 0)
        {
            VRLog.Warn("WorldUI", "stat-panel snapshot: no portrait Image found on ActorStatPanel " +
                                  "— the copy will blank when the game unloads the portrait.");
            return 0;
        }

        int detached = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            Transform? t = copy.transform.Find(paths[i]);
            Image? img = t != null ? t.GetComponent<Image>() : null;
            Sprite? sprite = img != null ? img.sprite : null;
            if (sprite == null || sprite.texture is not Texture2D tex)
                continue;
            try
            {
                var dst = new Texture2D(tex.width, tex.height, tex.format, tex.mipmapCount > 1)
                {
                    name = tex.name + " (VR copy)",
                    filterMode = tex.filterMode,
                    wrapMode = tex.wrapMode,
                    anisoLevel = tex.anisoLevel,
                };
                Graphics.CopyTexture(tex, dst);
                var rebuilt = Sprite.Create(dst, sprite.rect, NormalizedPivot(sprite),
                                            sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                                            sprite.border);
                rebuilt.name = sprite.name + " (VR copy)";
                _copyOwnedTextures.Add(dst);
                _copyOwnedSprites.Add(rebuilt);
                img!.sprite = rebuilt;
                detached++;
            }
            catch (System.Exception e)
            {
                VRLog.Warn("WorldUI", $"stat-panel snapshot: could not copy portrait texture " +
                                      $"'{tex.name}' ({e.GetType().Name}) — it keeps the shared one " +
                                      "and may blank when the game unloads it.");
            }
        }
        return detached;
    }

    /// <summary>
    /// How the snapshot differs STRUCTURALLY from the panel it was taken from, in one line.
    ///
    /// <para>Instantiate copies the whole hierarchy including every child's active state, so the
    /// two should be identical and this should read "identical". When it does not, the difference
    /// names itself — a section that is off in the copy is a section the game switched on AFTER
    /// the snapshot, which is a timing problem and needs a longer delay, not a different copy.
    /// Written because "the second box looks different" cannot be acted on and "3 object(s) differ:
    /// ConditionsContainer, ..." can.</para>
    /// </summary>
    private static string StructuralDiff(GameObject real, GameObject copy)
    {
        Transform[] a = real.GetComponentsInChildren<Transform>(true);
        Transform[] b = copy.GetComponentsInChildren<Transform>(true);
        if (a.Length != b.Length)
            return $"STRUCTURE DIFFERS: {a.Length} object(s) in the panel vs {b.Length} in the copy.";

        var names = new List<string>(4);
        int differing = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].gameObject.activeSelf == b[i].gameObject.activeSelf)
                continue;
            differing++;
            if (names.Count < 4)
                names.Add($"{a[i].name}({(a[i].gameObject.activeSelf ? "on->off" : "off->on")})");
        }
        return differing == 0
            ? "structure identical to the live panel."
            : $"STRUCTURE DIFFERS: {differing} object(s) toggled — {string.Join(", ", names)}.";
    }

    /// <summary>Slash-separated path of <paramref name="child"/> under <paramref name="root"/>, or
    /// null when it is not a descendant. Used to carry a reference from the real panel to the
    /// identically-shaped copy.</summary>
    private static string? PathUnder(Transform root, Transform child)
    {
        var parts = new List<string>(8);
        for (Transform? t = child; t != null; t = t.parent)
        {
            if (t == root)
            {
                parts.Reverse();
                return string.Join("/", parts);
            }
            parts.Add(t.name);
        }
        return null;
    }

    /// <summary>Sprite.Create wants the pivot as a 0..1 fraction of the RECT, not in pixels.</summary>
    private static Vector2 NormalizedPivot(Sprite sprite)
    {
        Rect r = sprite.rect;
        return r.width <= 0f || r.height <= 0f
            ? new Vector2(0.5f, 0.5f)
            : new Vector2(sprite.pivot.x / r.width, sprite.pivot.y / r.height);
    }

    private static readonly List<Texture2D> _copyOwnedTextures = new();
    private static readonly List<Sprite> _copyOwnedSprites = new();

    /// <summary>
    /// DestroyImmediate every Singleton-derived component (by base-type name <c>Singleton`1</c>, so
    /// any singleton flavor is caught) and every <see cref="UIWindow"/> in <paramref name="copy"/>.
    /// Order matters: ActorStatPanel [RequireComponent(typeof(UIWindow))] must go before its window.
    /// Plain viewer components (Image/TMP_Text/layout/scrollers) are kept — they are the imagery.
    /// </summary>
    private static int StripLogicComponents(GameObject copy)
    {
        int stripped = 0;
        MonoBehaviour[] behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null || !IsSingletonDerived(mb.GetType()))
                continue;
            Object.DestroyImmediate(mb);
            stripped++;
        }
        UIWindow[] windows = copy.GetComponentsInChildren<UIWindow>(true);
        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i] == null)
                continue;
            Object.DestroyImmediate(windows[i]);
            stripped++;
        }
        return stripped;
    }

    private static bool IsSingletonDerived(System.Type type)
    {
        for (System.Type? t = type; t != null; t = t.BaseType)
        {
            if (t.IsGenericType && t.Name == "Singleton`1")
                return true;
        }
        return false;
    }

    /// <summary>Release the copy's conversion (returns it under the inactive holder) and destroy
    /// the whole snapshot. Destroying the copy touches NO singleton: it carries no ActorStatPanel /
    /// UIWindow component, and its stripped components never ran Awake anyway.</summary>
    private static void DestroyCopy()
    {
        if (_copyPanel != null)
        {
            CanvasConversion.Release(_copyPanel);
            _copyPanel = null;
        }
        if (_copyHolder != null)
        {
            Object.Destroy(_copyHolder);
            _copyHolder = null;
        }
        _copyRect = null;
        _copyActor = null;

        // Ours, so we free them. Sprites first: a Sprite holding a destroyed texture logs errors.
        for (int i = 0; i < _copyOwnedSprites.Count; i++)
            if (_copyOwnedSprites[i] != null) Object.Destroy(_copyOwnedSprites[i]);
        _copyOwnedSprites.Clear();
        for (int i = 0; i < _copyOwnedTextures.Count; i++)
            if (_copyOwnedTextures[i] != null) Object.Destroy(_copyOwnedTextures[i]);
        _copyOwnedTextures.Clear();
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Hide → deferred release (test #16). The game's hover logic hides/re-shows the
    /// panel liberally; releasing instantly re-parented the whole uGUI subtree at
    /// frame rate. A re-show within the window cancels the pending release.
    /// </summary>
    private static void ScheduleRelease(Watch watch)
    {
        watch.PendingShow = false;
        if (watch.Panel != null)
            watch.ReleaseAt = Time.unscaledTime + ReleaseDelaySeconds;
    }

    /// <summary>One warning if a panel still churns through conversions (test #16).</summary>
    private static void CountConversion(Watch watch, string name)
    {
        float now = Time.unscaledTime;
        if (now - watch.CycleWindowStart > ChurnWindowSeconds)
        {
            watch.CycleWindowStart = now;
            watch.CycleCount = 0;
        }
        watch.CycleCount++;
        if (watch.CycleCount > ChurnWarnCount && !watch.ChurnWarned)
        {
            watch.ChurnWarned = true;
            VRLog.Warn("WorldUI", $"{name} convert/release churn: >{ChurnWarnCount} conversions in " +
                                  $"{ChurnWindowSeconds:F0}s despite the {ReleaseDelaySeconds:F1}s release " +
                                  "hysteresis — something still occludes/toggles the window per frame.");
        }
    }

    /// <summary>Held-dock pose beside an anchor: docked on the viewer side <paramref name="sideSign"/>
    /// of the hand, billboarded to the player. Shared by the real panel and the snapshot copy.</summary>
    private static bool TryComputeHeldPose(Vector3 anchorPos, float sideSign, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return false;
        float scale = PanelLayout.WorldScale;
        Vector3 fromHead = anchorPos - head.transform.position;
        fromHead.y = 0f;
        if (fromHead.sqrMagnitude < 1e-4f)
            fromHead = Vector3.forward;
        fromHead.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fromHead); // unit: screen-right of the view axis
        pos = anchorPos + (side * (HeldSideOffset * sideSign) + Vector3.up * HeldUpOffset) * scale;
        Vector3 facing = pos - head.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f)
            facing = fromHead;
        rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
        return true;
    }

    private void PlaceWatch(Watch watch)
    {
        if (watch.Panel == null)
            return;

        float scale = PanelLayout.WorldScale;
        Camera? head = CanvasConversion.WorldCamera;

        // Held-figure override (P8, per hand): if THIS watch shows a figure currently held in EITHER
        // hand, anchor beside that held mini (tighter offset so it clears the hand) on the side
        // opposite that hand — instead of its board cell. Billboarded to the player either way.
        bool held = TryGetHeldFor(WatchActor(watch), out Transform? heldAnchor, out float heldSign);
        if (held && heldAnchor != null)
        {
            if (TryComputeHeldPose(heldAnchor.position, heldSign, out Vector3 heldPos, out Quaternion heldRot))
                CanvasConversion.PlaceHost(watch.Panel, heldPos, heldRot, scale * 0.6f);
            return;
        }

        Vector3? anchor = ResolveActorAnchor(watch);
        if (anchor.HasValue && head != null)
        {
            // Board case: fixed viewer-right side, a little higher than the held dock.
            const float sideOffset = 0.30f;
            const float upOffset = 0.10f;

            Vector3 fromHead = anchor.Value - head.transform.position;
            fromHead.y = 0f;
            if (fromHead.sqrMagnitude < 1e-4f)
                fromHead = Vector3.forward;
            fromHead.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fromHead); // unit: screen-right of the view axis
            Vector3 pos = anchor.Value + (side * sideOffset + Vector3.up * upOffset) * scale;
            Vector3 facing = pos - head.transform.position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f)
                facing = fromHead;
            Quaternion rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
            CanvasConversion.PlaceHost(watch.Panel, pos, rot, scale * 0.6f);
            return;
        }

        // Fallback: fixed layout slot.
        if (PanelLayout.TryGetPose(PanelSlot.StatPanel, out Vector3 slotPos, out Quaternion slotRot))
            CanvasConversion.PlaceHost(watch.Panel, slotPos, slotRot, scale * 0.6f);
    }

    /// <summary>The CActor currently shown by a watch's window (actor panel or enemy-turn panel).</summary>
    private static ScenarioRuleLibrary.CActor? WatchActor(Watch watch) => watch.Attached switch
    {
        ActorStatPanel statPanel => statPanel.m_ActorShown,
        EnemyCurrentTurnStatPanel enemyPanel => enemyPanel._currentShownEnemy,
        _ => null,
    };

    /// <summary>Per-hand held-dock lookup: if <paramref name="actor"/> is a figure currently held in
    /// either hand, return that hand's anchor + viewer-side sign. Matches both the PRIMARY (real
    /// panel) and SECONDARY (snapshot copy) held figures.</summary>
    private static bool TryGetHeldFor(ScenarioRuleLibrary.CActor? actor, out Transform? anchor, out float sign)
    {
        if (actor != null && _heldAnchor != null && ReferenceEquals(actor, _heldActor))
        {
            anchor = _heldAnchor;
            sign = _heldSideSign;
            return true;
        }
        if (actor != null && _held2Anchor != null && ReferenceEquals(actor, _held2Actor))
        {
            anchor = _held2Anchor;
            sign = _held2SideSign;
            return true;
        }
        anchor = null;
        sign = 1f;
        return false;
    }

    private static Vector3? ResolveActorAnchor(Watch watch)
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

        ScenarioRuleLibrary.CActor? actor = WatchActor(watch);
        if (actor == null)
            return null;

        GameObject clientGo = choreographer.FindClientActorGameObject(actor);
        return clientGo != null ? clientGo.transform.position : null;
    }

    private static void Release(Watch watch)
    {
        watch.PendingShow = false;
        watch.ReleaseAt = 0f;
        if (watch.Panel != null)
        {
            CanvasConversion.Release(watch.Panel);
            watch.Panel = null;
        }
    }

    private void DetachWatch(Watch watch)
    {
        if (watch.Window != null)
        {
            watch.Window.onShown.RemoveListener(watch.OnShown);
            watch.Window.onHidden.RemoveListener(watch.OnHidden);
        }
        watch.Window = null;
        watch.Attached = null;
        Release(watch);
    }

    public void Shutdown()
    {
        VREvents.MiniaturePoked -= OnMiniaturePoked;
        DetachWatch(_actorPanel);
        DetachWatch(_enemyTurnPanel);
        DestroyCopy();
        _pendingCopyActor = null;
        for (int i = 0; i < _reg.Length; i++)
            _reg[i] = null;
        _heldActor = null; _heldAnchor = null; _held2Actor = null; _held2Anchor = null;
    }
}
