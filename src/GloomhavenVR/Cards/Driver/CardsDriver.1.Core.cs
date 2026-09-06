using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver is ONE class split across SIX files. This is part 1 — read it first: it holds the
// class doc, every shared field, the lifecycle and the board-tuning subscription.
//
//   CardsDriver.1.Core.cs          header, shared fields, character-swap exchange, lifecycle,
//                                  board tuning (Part F)
//   CardsDriver.2.Update.cs        board pose guard, handlers, Update, tick attribution guard,
//                                  fan diagnostics, card audio
//   CardsDriver.3.Laser.cs         fan laser, fan hover ownership + split, hand-contact
//                                  arbitration, laser stand-down, board/browse/item-fan/active
//                                  laser, modal input-block, slot snap preview
//   CardsDriver.4.Rebuild.cs       hand fan reorder, Rebuild, focus slot-card diagnostics,
//                                  fly-to-pile + pile arrival, MP card-FX anchors,
//                                  BurnSlab, HookCard
//   CardsDriver.5.Interactions.cs  interactions, hand-to-hand transfer, pick flows, short rest
//   CardsDriver.6.Flows.cs         overlay gate, wanted-slot hint, pick progress + confirm
//                                  routing, initiative to-do, long-rest tracing, fan interaction
//                                  mode, take-damage selection + decision surface, long-rest turn
//                                  pump, selection-follow hand switch, pile browse, active cards,
//                                  dev fake hand
//
// THE FILENAMES ARE NOT DECORATION. The csproj uses the SDK's default `**/*.cs` glob, so compile
// order follows the filename sort, and a partial class's members land in metadata in compile
// order. The digits keep the six parts concatenating back into the ORIGINAL member order, which
// is what lets refactor-guard.sh prove this split changed nothing. Renaming a part so it sorts
// differently silently reorders field initializers. Do not do it.
//
// SPLITTING A FILE DOES NOT SPLIT THE CALL ORDER. One coupling now spans a cut and is worth
// naming here because neither compiler nor guard will: TickInteractionsAndStatus (part 2) fixes
// the order `laser paths → UpdateHandContactArbitration` (part 3) — arbitration must run AFTER
// the laser paths have published their hover, and "these are all independent, let me tidy the
// tick calls" is exactly how that breaks (INVARIANTS-Cards §3). The other two order-critical
// pairs stay inside one part on purpose: Rebuild before TickBurnToPile (both part 4), and
// TickBoardPoseWatch last in Update (both part 2).

/// <summary>
/// Phase-3b orchestrator (one MonoBehaviour, created by <see cref="CardsModule"/>):
/// - pumps <see cref="CardActionQueue"/> (one blocking game call per frame max),
/// - enforces <see cref="HandSuppression"/>,
/// - reacts to the P2 bus/mode machine + Cards signals by rebuilding the card zones
///   (fan / tray / half layout) from authoritative game state,
/// - routes interactions (grab-drop, pokes, palm gate) into the verified game APIs.
///
/// Everything here runs on the main thread; handlers only set dirty flags or enqueue
/// — per-frame work happens in <see cref="Update"/> (P2 threading rules).
/// </summary>
internal sealed partial class CardsDriver : MonoBehaviour
{
    private readonly VRCardFactory _factory = new();
    private readonly CardFan _fan = new();
    private readonly PlayTray _tray = new();
    private readonly RestControls _rest = new();
    private readonly HalfSelection _half = new();
    private readonly PileViewer _piles = new();
    private readonly PileBrowser _browser = new();
    private readonly ActivePileViewer _active = new();

    // Reused buffers (no per-frame allocations).
    private readonly List<AbilityCardUI> _widgetBuffer = new(24);
    private readonly List<AbilityCardUI> _pileWidgetBuffer = new(16);
    private readonly List<AbilityCardUI> _activeWidgetBuffer = new(8);
    /// <summary>Signature scratch for <c>PollHandCards</c> — the hand-fan membership watchdog. Its
    /// own buffer rather than <see cref="_widgetBuffer"/>: that one is owned by Rebuild and is live
    /// across the whole rebuild, while this is read from the per-frame poll.</summary>
    private readonly List<AbilityCardUI> _handSigBuffer = new(24);
    private readonly List<VRCard> _fanBuffer = new(24);

    /// <summary>
    /// WHICH PILE THE FAN THIS DRIVER IS HOLDING UP IS DRAWN FROM: the DISCARD or LOST pile while
    /// the game has this player stepping through a modal card pick (a long rest's burn step, an
    /// avoid-damage burn, a card-limit discard, a recover), and <see cref="CardPileType.None"/>
    /// otherwise. Rewritten on every rebuild from the very widgets that became
    /// <see cref="_fanBuffer"/>, so it can never describe a fan that is no longer up.
    ///
    /// <para>NONE RATHER THAN HAND FOR THE ORDINARY CASE, and the two are not interchangeable here.
    /// An ordinary hand fan and NO FAN AT ALL are one answer on purpose, because the wire record
    /// this feeds is written only for a pile: making "the hand" sayable would give one picture two
    /// spellings, and the receiver would then have to tell them apart to no purpose. See
    /// <c>Net.NetProtocol.IsFanSourcePile</c>.</para>
    ///
    /// <para>IT EXISTS BECAUSE A PEER CANNOT DERIVE IT. Everything else a mirrored fan needs is
    /// host-replicated model state both clients already hold; this one is not. The pile a pick fan
    /// is drawn from is decided by <c>CardsHandUI.Show(..., selectableCardType, ...)</c> and read
    /// back off <c>AbilityCardUI.IsSelectable</c> — LOCAL UI state on the picking player's machine,
    /// which no other client's copy of the model reflects. Without it every observer resolves a
    /// peer's fan as their HAND (<c>CardsGameApi.HandFanMember</c>), which during a long rest is a
    /// list of a different length than the arc the owner is actually holding, and
    /// <c>Net.RemoteHandFan</c>'s length belt correctly refuses the whole fan and draws BACKS. That
    /// is the 2026-09-06 report item 7 in one sentence, and its evidence is one census line on the
    /// host: <c>hand fan[p2] 0 FRONT / 2 BACK — LENGTH BELT: 6 model card(s) vs 2 slab(s) on the
    /// wire</c> standing for the whole of the co-player's long rest, with
    /// <c>RevealGate.ShowRoundCardFronts(actor)=true</c> beside it — an OPEN gate and a shut
    /// arithmetic.</para>
    ///
    /// <para>NOT A SECRECY TERM AND NEVER TO BE MADE ONE. It names a POPULATION, not a permission:
    /// <c>Net.RevealGate</c> alone decides whether a front may be drawn, and it is asked with the
    /// same phase test for a pick fan as for a hand fan (see
    /// <c>RevealGate.PeerCardPopulation.PickFan</c>).</para>
    /// </summary>
    private CardPileType _fanSourcePile = CardPileType.None;

    /// <summary>
    /// <see cref="_fanSourcePile"/> for the Net layer's sampler, static for the same reason
    /// <see cref="SacrificeSeat"/> is: the driver instance is a private of this class and the Net
    /// layer must not thread through it. Answers <see cref="CardPileType.None"/> with no driver,
    /// which the sampler writes as "no record" — i.e. exactly what every client before ModBuild 459
    /// sends, and what an observer already renders.
    /// </summary>
    internal static CardPileType FanSourcePile =>
        Instance != null ? Instance._fanSourcePile : CardPileType.None;

    private readonly List<VRCard> _halfBuffer = new(4);
    private readonly List<VRCard> _browseBuffer = new(16);
    private readonly List<VRCard> _activeBuffer = new(8);
    private readonly List<VRCard> _fakeCards = new(12);
    private int _activeSignature = int.MinValue; // change-gate for the active-card set (feature 6)
    private int _handSignature = int.MinValue;   // change-gate for the HAND-pile card set (item 10, burn)

    // Issue 5 (fly-to-pile): the round cards docked in the PREVIOUS rebuild (a snapshot of
    // _halfBuffer), so the park sweep can tell a just-cleared PLAYED card from any other parked
    // card and fly it into its destination pile. _flyingToPile holds the cards mid-flight so the
    // sweep leaves them untouched (no re-park, no re-launch — the fly runs exactly once per card).
    private readonly HashSet<VRCard> _lastHalfCards = new();
    // Issue 1 (user): the SELECTED cards laid in the control-board SLOTS during card selection are
    // the "cards already lying there". Switching character must DISAPPEAR (scale-down) the outgoing
    // character's slot cards and APPEAR (scale-in, in place) the incoming character's — never a
    // fly-from-below. _lastTrayCards is the previous rebuild's slot occupants (the vanish set, like
    // _lastHalfCards for the docked round cards). _lastVisibleCards is every card that was in a
    // VISIBLE zone (fan / tray / half) last rebuild: a slot card that was already visible there
    // (e.g. one the player just DROPPED in from the fan) must GLIDE, not scale-in — only a card that
    // was parked/hidden (a character switch bringing back another character's cards) appears.
    private readonly HashSet<VRCard> _lastTrayCards = new();
    private readonly HashSet<VRCard> _lastVisibleCards = new();
    private readonly HashSet<VRCard> _flyingToPile = new();
    private const float FlyToPileSeconds = 0.4f;

    // EVENT-DISCARD EXIT (user report 2026-08-24, kartenabwurf2.jpg: "gehen die zwei ersten Karten
    // der ersten Seite komisch zur Seite und clippen dann im board — stattdessen will ich das ganz
    // normal die 'verbrennen' Animation abgespielt wird und die Karten in den jeweiligen Pile
    // gehen"). The two sets below are the ONLY new bookkeeping this fix adds; both are per-pick and
    // die with the pick.
    //
    // _lastFieldCards is the pick field's counterpart to _lastHalfCards: the cards that were lying
    // in the board's pick recesses in the PREVIOUS rebuild. Without it the park sweep cannot tell a
    // just-committed pick card ("it left the field because the game finally moved it") from any
    // other parked card, and the FINAL card of an event discard popped out of existence
    // (CardsDriver.4.Rebuild.TryStartFlyToPile's pre-filter is a membership test, and there was no
    // set to be a member of).
    private readonly HashSet<VRCard> _lastFieldCards = new();

    // _pickExitFlown: pick-field cards whose exit flight has ALREADY been launched, so
    // RelayoutField stops re-homing them onto the beside-Slot2 overflow seats (that re-home IS the
    // reported bug) and no second flight can ever be launched for the same card. Membership is not
    // "the card is in the pile" — it is "this driver has already handed this card to FlyToPile";
    // the flight's own completion callback parks it. Cleared with _pickLockedCount everywhere.
    private readonly HashSet<VRCard> _pickExitFlown = new();

    // PICK RESTART RETURN FLIGHT (user report 2026-08-24: "Wird der gedrückt soll die Auswahl auf
    // der ersten Seite nochmal komplett von anfang an beginnen. Am Besten mit einer kleinen
    // Animation, weil ja bereits zwei Karten in den jeweiligen pile geflogen sind.").
    //
    // When the game's own "Wähle eine andere Karte" is pressed, the whole event discard restarts at
    // page 1 — and the pages that ALREADY flew into the discard stack (_pickExitFlown) have to come
    // back out of it. This is the hand-over list between the two halves of that restart: the UNDO
    // seam fills it the moment the cancel actually lands, and Rebuild empties it one pass later,
    // AFTER CardFan.SetCards has given each card its new home, by playing VRCard.FlyFromPile — the
    // exact reverse of the FlyToPile arc that put them there. It is deliberately NOT a claim on the
    // card (unlike _pickExitFlown): the cards are ordinary fan cards again from the game's point of
    // view the instant the cancel deselects them, and a card that cannot fly (held, parked with the
    // fan closed, no discard stack built) is simply dropped from the list with a logged reason.
    private readonly List<VRCard> _pickReturnFlight = new(4);

    // …and the ONE piece of state the return flight leaves behind: when it lands. Rebuild's zone
    // stamp SKIPS a flying card outright (CardsDriver.4.Rebuild.cs:822 — a flight owns its
    // transform), and VRCard.FlyFromPile drops Grabbable for the duration, so a returned card would
    // stay un-grabbable until something else happened to mark the driver dirty. A returned card the
    // player cannot pick up again would be the restart failing at the last step, so the landing is
    // not left to chance: an unscaled deadline (the flight is a fixed duration and FlyFromPile takes
    // no completion callback) re-arms ONE rebuild. 0 = nothing pending.
    private float _pickReturnSettleAt;

    // FLIGHT TRIGGER IS THE MODEL, NOT THE DOCK (user report 2026-08-08: "Wenn ich in der
    // Aktionsphase von dem aktiven Character zu einem anderen Character wechsel ... wird die
    // Animation abgespielt dass beide Karten des aktiven Characters in den 'abgeworfen' pile gehen
    // - das ist definitiv falsch"). _lastHalfCards only says the dock's CONTENT changed, and a
    // focus switch changes the content without moving a single card. The verdict a docked card that
    // left every zone gets is therefore read from the OWNER's authoritative CCharacterClass lists
    // (CardsDriver.RoundCardExitOf) and this dictionary remembers the last REFUSAL that was logged
    // per widget, so a repeated switch stays one line per switch and a stuck state stays one line
    // in total. The entry is dropped the moment the card docks again (CollectRoundCards), so the
    // table can never hold more than the character's own cards.
    private readonly Dictionary<AbilityCardUI, RoundCardExit> _loggedFlightRefusal = new(8);

    // Issue 1 (fly-to-pile "suddenly somewhere else" glitch): the last-known WORLD pose of every
    // adopted card while it was still visible in a zone, keyed by its game widget. When a card is
    // burned via damage its live VR card is often already parked (position lost) or recycled by the
    // time TickBurnToPile sees it in the burnt pile — the old fallback then flew a slab FROM THE
    // DISCARD PILE, i.e. it teleported to a different place first (the glitch). Now the fallback
    // slab starts from this recorded true position/rotation instead, holding that orientation for
    // the whole flight; with no recorded pose the animation is skipped (never a teleport).
    private readonly Dictionary<AbilityCardUI, Vector3> _lastCardWorldPos = new(16);
    private readonly Dictionary<AbilityCardUI, Quaternion> _lastCardWorldRot = new(16);

    // Issue 2 (character/turn switch board cards must not pop): suppress the docked-card appear/
    // disappear animation for exactly the first Rebuild after a fresh board build / teardown (the
    // scenario-load "no storm" guard, same philosophy as the buttons' _everShown). Set true on
    // enable / board switch / hand teardown; cleared at the end of each Rebuild so every LATER
    // change (the actual character/turn switches) animates.
    private bool _dockAnimSuppressed = true;

    // Issue B (user): a card burned via the TAKE-DAMAGE decision ("burn available/discarded
    // card") is a different flow from the turn-clear round-card sweep above — the game moves it
    // straight into the character's Lost pile and recycles the widget, so it just vanished with no
    // VR animation. TickBurnToPile watches the burnt pile's widget set per hand; any card newly
    // added there that the turn-clear path did NOT already claim flies to the BURNT stack with the
    // same over-the-board arc. _knownBurntWidgets is the previous-tick baseline (re-seeded on a
    // hand change so a hand's pre-existing burnt cards never animate retroactively).
    private readonly List<AbilityCardUI> _burntWidgetBuffer = new(8);
    private readonly HashSet<AbilityCardUI> _knownBurntWidgets = new();
    private CardsHandUI? _burnWatchHand;

    private bool _dirty;
    private bool _modalInputBlocked; // menu-open gate: while set, cards are inert + card/board laser picks are off
    // Item 4: previous VRMode transition, to detect the laundered ModalUI→TableIdle→HalfSelection
    // reveal-confirm round-trip (see OnModeChanged) and NOT re-seat the board on it.
    private VRMode _prevFrom = VRMode.Menu2D;
    private VRMode _prevTo = VRMode.Menu2D;
    private bool _boardChanged; // [Cards] Board switched — tear down + rebuild the tray next frame
    private bool _reassertTray;  // item 3: presence regained (HMD re-donned) — re-assert board placement next frame
    // PART D: the outgoing board's world pose, captured on a SWITCH so the new board re-appears
    // in the EXACT same place instead of re-anchoring to the head.
    private bool _hasSwitchPose;
    private Vector3 _switchPos;
    private Quaternion _switchRot = Quaternion.identity;
    private Vector3 _switchScale = Vector3.one;
    private bool _fakeActive;
    private VRHand? _gateHand;

    // Task #9 (empty-fan feedback): ghost placard shown when the palm gate opens on an
    // empty hand; _gateWasRevealed edge-detects the gesture so the hint fires once per
    // roll, never per frame.
    private readonly EmptyFanHint _emptyFanHint = new();
    private bool _gateWasRevealed;
    private CardsHandUI? _boundHand;

    /// <summary>
    /// The hand whose live <c>AbilityCardUI</c> faces are currently ADOPTED for a READ-ONLY focus
    /// view (null = none). The highest-uncertainty part of the character-focus feature: a focused
    /// view reparents ANOTHER character's real widgets into VR cards, exactly as the local fan has
    /// always done for a hand switch, and hands them back on release. Tracking the hand here is
    /// what lets <c>ReleaseStaleFocusHand</c> give a character's faces back the moment the focus
    /// leaves it — and log what was adopted and whether the restore actually landed, so a
    /// "somebody's hand came back wrong" report is answerable from the log rather than from a
    /// screenshot.
    /// </summary>
    private CardsHandUI? _focusAdoptedHand;

    // ------------------------------------------- the hand fan's CHARACTER-SWAP EXCHANGE --
    //
    // User report 2026-08-09 ("mach auch hier eine neue coolere Tauschanimation rein die den Fächer
    // austauscht"). The animation itself lives in CardFan's exchange region; what the DRIVER owns is
    // the two facts the fan cannot know:
    //
    //   (a) WHETHER this rebuild is an exchange at all. The fan only sees its list change, and a
    //       list change alone cannot tell "the whole hand was swapped for another character's" from
    //       "a card was drawn / played / plucked". CharacterFocus.PresentedActorId is the exact
    //       predicate — it is re-derived by ResolveHand at the top of every Rebuild and names the
    //       character the board is CURRENTLY presenting, focus override included — so a change in it
    //       with a fan already up is the swap edge, and nothing else is.
    //
    //   (b) WHEN the outgoing character's borrowed card FACES may go back to the game. This used to
    //       be "immediately, at the top of Rebuild" (ReleaseStaleFocusHand), which is what made an
    //       out-animation impossible in the first place: CardFace.Restore reparents the live
    //       fullAbilityCard back into the game's own UI and VRCardFactory.ReleaseWidget then
    //       destroys the VR card, so by the time SetCards ran the outgoing hand was already faceless
    //       and about to be deleted. Flying a card-BACK away would be a content pop of its own. The
    //       restore is therefore DEFERRED for exactly as long as the exchange is in the air, and no
    //       longer: the fan reports when its outgoing wave has drained, and only the FAN-OPEN swap
    //       path defers at all — with the fan closed nothing animates and the release is immediate,
    //       byte for byte the previous behaviour.
    //
    // The deferral is bounded three ways, because a stranded adoption is a visible GAME bug (the
    // character's 2D hand stays borrowed): the fan lands its exchange on close / re-open / destroy,
    // the drain releases as soon as no card is still leaving, and DeadlineSeconds below is a hard
    // backstop for any path that manages to do neither.

    /// <summary>The character the board presented on the previous rebuild (0 = none yet). The swap
    /// edge is a change in this while the fan is open — see (a) above.</summary>
    private int _lastPresentedActorId;

    /// <summary>Focus hands whose adopted card faces are waiting on an exchange to finish. A LIST
    /// rather than a single slot because scrubbing the initiative row queues one per switch, and
    /// releasing an earlier one early would destroy VR cards that are still visibly flying (the
    /// "no card is destroyed out from under something" rule). Bounded by the party size.</summary>
    private readonly List<CardsHandUI> _pendingFaceRestore = new(4);

    /// <summary>Unscaled time by which <see cref="_pendingFaceRestore"/> must be drained whatever the
    /// fan says — the backstop against a stranded adoption.</summary>
    private float _faceRestoreDeadline;

    /// <summary>Extra seconds on top of the exchange's own length before the deadline bites. Long
    /// enough that a hitching frame or a re-switch never trips it, short enough that a genuinely
    /// stuck exchange gives the faces back within a breath.</summary>
    private const float FaceRestoreDeadlineSlack = 1.5f;

    /// <summary>Reused buffer for the cards the fan hands back as their flights land (no per-frame
    /// allocation on the drain path).</summary>
    private readonly List<VRCard> _swapLanded = new(12);

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>One-shot guard for the BoardTargeting Grab/PalmGate-policy grant (survives driver
    /// rebuilds).</summary>
    private static bool s_grabPolicyGranted;

    /// <summary>The live driver, for the static request entry points (<see cref="RequestBoardRecall"/>).
    /// Null while no cards driver exists (outside VR / before the module builds it).</summary>
    private static CardsDriver? Instance;

    /// <summary>
    /// Ask for a card REBUILD on the next update (feature "free character focus").
    ///
    /// <para>WHY IT EXISTS: <c>Rebuild</c> runs on an EDGE — <c>_dirty</c>, set by the game-driven
    /// events the driver subscribes to (mode change, card-selection change, hand shown, …). A
    /// character focus is a MOD-side change the game emits no event for, so without this the board
    /// would keep showing the previous character until the game happened to raise an unrelated
    /// event. It is also how a live read-only focus view stays current: the watched character's
    /// hand changes as THEY play, and none of those changes raise a local event either.</para>
    ///
    /// <para>No-op when no driver exists (flat / pre-build). Setting a flag is the whole body —
    /// the rebuild itself happens on the driver's own tick, on the main thread, in frame order.</para>
    /// </summary>
    internal static void RequestRebuild()
    {
        if (Instance != null)
            Instance._dirty = true;
    }

    private void OnEnable()
    {
        Instance = this;
        // USER BUG A (hardware log 4125-4193, "during movement destination selection I
        // could not grab the control board / options / VR settings bars — vibrates but
        // won't grab; laser grab dead too"): movement-destination selection is
        // Choreographer state WaitingForPlayerWaypointSelection → VRMode.BoardTargeting,
        // and the Phase-2 BoardTargeting policy carried NO Grab for EITHER hand — a
        // pre-panel-grab-era decision. Every grab primitive dies with it: proximity grip
        // on the tray/options/settings bars, PanelGrabHandle laser-carry
        // (ProximityGrabber.ForceGrab refuses on Enabled=false — the log's eleven
        // "LASER-CARRY armed" lines with no engage) and figure plucks
        // (FigureGrabDriver also grabs through ProximityGrabber). Meanwhile
        // RayGrabDriver's hover tint + haptic never consult Grabber.Enabled — the
        // "flickers and vibrates but won't grab" symptom. Desktop parity: the mouse can
        // always drag windows during targeting; per-object gates (VRCard.CanGrab,
        // GrabVisible) keep deciding WHAT is grabbable. Granted through the mode
        // machine's documented extension API (never a patch on the frozen class).
        //
        // …AND THE PALM GATE, for exactly the same reason one build later (user report: "Während
        // dessen eine Bewegung oder ein Angriff bestätigt werden muss … die Handkarten werden nicht
        // angezeigt — auch während hier auf Bestätigung gewartet wird, soll man beliebig wechseln
        // können und von jedem die Handkarten ansehen INKLUSIVE dem Character der gerade die
        // Entscheidung treffen muss").
        //
        // ROOT CAUSE, and why it looked like a card-pipeline bug when it is an INPUT-POLICY one:
        // a move destination / attack focus / push / pull / tile pick parks the Choreographer in
        // one of VRModeStateMachine.TargetingStates, i.e. VRMode.BoardTargeting — and the Phase-2
        // BoardTargeting rows carried Ray|Poke only. PalmGate is the interactor that MEASURES the
        // wrist roll (VRHand.SetInteractorPolicy → PalmGate.Enabled), and CardsDriver.UpdatePalmGate
        // gates the whole reveal on `gate.Enabled` (`revealed = gate.Enabled && …`). So the fan
        // could not be opened AT ALL for the entire confirmation wait, for EVERY character —
        // including the one that owes the confirmation. The hardware log states it plainly:
        // "fan state: mode=ActionSelection, widgets=24, fanBuffer=6, gateEnabled=False, open=False
        //  (vrMode=BoardTargeting)" — the mod had BUILT six hand cards and had no way to show them.
        //
        // That is a straight violation of the standing 2026-08-08 ruling this codebase already
        // states at Board.CharacterFocus.HandInspectable: a hand card may ALWAYS be picked up and
        // inspected, in every phase. A phase in which the hand cannot even be REVEALED is the
        // strongest possible form of that block, so the grant is not a new permission — it is the
        // removal of one more place where the rule was silently not in force.
        //
        // SAFE BY THE SAME ARGUMENT AS THE GRAB GRANT: the palm gate only decides whether the fan
        // is SHOWN. What may be done with a card in it is decided per card, unchanged, by
        // CardsDriver.Rebuild's grabbable/inspect funnel (`commitGrab` is still false outside the
        // real selection window, so the fan comes up as CardFan.FanMode.Inspect: readable, never
        // committable) and by VRCard.CanGrab. The dominant hand keeps its ray for the board pick —
        // the fan hangs off the NON-dominant palm and the far pick exclusively consumes
        // VRHands.PrimaryPick — so nothing is taken away from targeting itself. Desktop parity: the
        // 2D client can open any hand at any time from the initiative track.
        if (!s_grabPolicyGranted)
        {
            s_grabPolicyGranted = true;
            VRModeStateMachine.SetHandInteractorPolicy(VRMode.BoardTargeting, HandRole.Dominant,
                Core.Events.Interactors.Ray | Core.Events.Interactors.Poke | Core.Events.Interactors.Grab
                | Core.Events.Interactors.PalmGate);
            VRModeStateMachine.SetHandInteractorPolicy(VRMode.BoardTargeting, HandRole.NonDominant,
                Core.Events.Interactors.Poke | Core.Events.Interactors.Grab
                | Core.Events.Interactors.PalmGate);
            VRLog.Info("Cards", "BoardTargeting interactor policy now includes Grab AND PalmGate " +
                                "(both hands) — control-board/panel bars and figure grabs stay " +
                                "usable during move/target selection (user bug A), and the HAND FAN " +
                                "can be revealed while the game waits for a move/attack confirmation " +
                                "(the hand is inspectable in EVERY phase, by standing ruling — see " +
                                "Board.CharacterFocus.HandInspectable). The fan comes up read-only " +
                                "there: nothing about what a card may DO changed.");
        }

        VRModeStateMachine.ModeChanged += OnModeChanged;
        VREvents.CardSelectionChanged += OnCardSelectionChanged;
        VREvents.HandShown += OnHandShown;
        VREvents.SessionResumed += OnSessionResumed; // item 3: re-assert the board after an HMD doff/don
        CardsSignals.HandDestroying += OnHandDestroying;
        CardsSignals.CardRecycling += OnCardRecycling;
        VRHands.HandsChanged += OnHandsChanged;
        CardsConfig.Board.SettingChanged += OnBoardChanged;
        SubscribeBoardTuning(true); // PART F: per-board tuning entries live-apply (menu + hand-edited cfg)

        // GLOBAL hand-fan geometry ("Fan" debug category) — not per-board, so subscribed once.
        CardsConfig.FanPerCardStepDegrees.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanArcSweepDegrees.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanEffectiveRadius.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanHoverSplitScale.SettingChanged += OnFanTuningChanged;
        // Card presentation (edge-read fix): per-card toe-in + gaze-following bow apex. CardFan's
        // per-frame param signature would re-lay the OPEN fan anyway; subscribing keeps them on the
        // established live-apply path so a hand-edited cfg / menu step also logs the change.
        CardsConfig.FanFaceViewer.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanGazeApexFollow.SettingChanged += OnFanTuningChanged;
        // Item 12 (global): the movement scheme re-applies the board orientation live —
        // switching Frei → Begrenzt re-levels a freely rotated board. The pitch-window edges
        // are per-board (BoardPitchMin/Max_<board>) and subscribe in SubscribeBoardTuning, so a
        // debug-menu clamp edit still immediately re-clamps an applied TrayPitch.
        _lastMoveMode = CardsConfig.BoardMoveMode.Value;
        CardsConfig.BoardMoveMode.SettingChanged += OnMoveModeChanged;
        CardsConfig.BoardMoveMode.SettingChanged += OnOrientationChanged;

        _tray.SwapRequested += OnSwapRequested;
        _tray.ConfirmRequested += OnConfirmRequested;
        _tray.UndoRequested += OnUndoRequested;
        _tray.SkipRequested += OnSkipRequested;
        _rest.ShortRestRequested += OnShortRestRequested;
        _rest.LongRestRequested += OnLongRestRequested;
        _half.PlayRequested += OnPlayRequested;
        // Poke/laser toggle is the ONLY stack seam left: the GrabOpened/GrabReleased pair
        // (pinch-to-browse-while-held) died with the stack grab (PileStack.CanGrab, 2026-08-06).
        _piles.PokeToggled += OnPileTogglePoked;
        _piles.ItemsOpening += () => CloseBrowser("items browse opened"); // one pile fan at a time (#6)

        _dirty = true;
    }

    private void OnDisable()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null; // static request entry points go no-op again
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VREvents.HandShown -= OnHandShown;
        VREvents.SessionResumed -= OnSessionResumed;
        CardsSignals.HandDestroying -= OnHandDestroying;
        CardsSignals.CardRecycling -= OnCardRecycling;
        VRHands.HandsChanged -= OnHandsChanged;
        CardsConfig.Board.SettingChanged -= OnBoardChanged;
        SubscribeBoardTuning(false);

        CardsConfig.FanPerCardStepDegrees.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanArcSweepDegrees.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanEffectiveRadius.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanHoverSplitScale.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanFaceViewer.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanGazeApexFollow.SettingChanged -= OnFanTuningChanged;
        CardsConfig.BoardMoveMode.SettingChanged -= OnMoveModeChanged;
        CardsConfig.BoardMoveMode.SettingChanged -= OnOrientationChanged;
    }

    // ------------------------------------------------------------------ board tuning (Part F) --

    // Debug-menu / hand-edited per-board offsets live-apply through a dirty-flag consumed in
    // Update (the P2 threading rule: handlers only set flags). Position changes apply in place;
    // size/diameter changes rebuild just the affected buttons.
    private bool _applyControlOffsets;   // rest + confirm/undo X/Y/Z offset + group spacing (instant)
    private bool _applyPickBannerOffset; // pick-status placard offset (instant, plain transform write)
    private bool _applyControlRebuild;   // rest diameter / confirm-undo size / button SHAPE (rebuild the buttons)
    private int _restTuningVersion;      // last-seen ButtonTuning.Version — rest [RestButtons] W/H/D/Travel live-rebuild
    private bool _applyOverlayOffset;    // slot/wanted glow offset
    private bool _applyInitiativeOffset;
    private bool _applyAssetPose; // initiative-track mount position
    private bool _applyOrientation;      // board tilt / yaw / scale / pos-offset
    private bool _applyActive;           // active-cards mount offset / card scale / grid spacing
    private bool _applyPiles;            // discard/burn pile mount offset / scale / inter-pile spacing
    private bool _applyObjectives;       // items 4/6: objectives ('Aufgaben') dock offset / scale
    private bool _applyElements;         // items 4/6: element infusion ('Elemente') dock offset / scale
    private bool _applyHudWidgets;       // items 4/6: gear / follow-pin / round-readout offsets (in place)
    private bool _applyDecision;         // item C: shared decision-dock offset / scale
    private bool _applyFan;              // GLOBAL hand-fan geometry (step / arc / radius / hover-split)

    /// <summary>Subscribe/unsubscribe every per-board tuning entry's SettingChanged (both boards' menu AND cfg edits live-apply).</summary>
    private void SubscribeBoardTuning(bool subscribe)
    {
        // THE KEYCAP SEAT FAMILY IS SUBSCRIBED ONCE, NOT ONCE PER BOARD. These five used to be
        // per-board entries and were hooked inside the loop below; they are single shared entries
        // now (the per-board part of a keycap seat comes off the board's own anchors — see
        // CardsConfig's seat-family note), and hooking a shared entry three times would fire its
        // handler three times per edit.
        if (subscribe)
        {
            CardsConfig.RestButtonOffset.SettingChanged += OnControlOffsetChanged;
            CardsConfig.ConfirmUndoOffset.SettingChanged += OnControlOffsetChanged;
            CardsConfig.RestStackSpacing.SettingChanged += OnControlOffsetChanged;   // spacing = in-place move
            CardsConfig.ButtonStackSpacing.SettingChanged += OnControlOffsetChanged;
            CardsConfig.RestButtonDiameter.SettingChanged += OnControlSizeChanged;
        }
        else
        {
            CardsConfig.RestButtonOffset.SettingChanged -= OnControlOffsetChanged;
            CardsConfig.ConfirmUndoOffset.SettingChanged -= OnControlOffsetChanged;
            CardsConfig.RestStackSpacing.SettingChanged -= OnControlOffsetChanged;
            CardsConfig.ButtonStackSpacing.SettingChanged -= OnControlOffsetChanged;
            CardsConfig.RestButtonDiameter.SettingChanged -= OnControlSizeChanged;
        }
        foreach (ControlBoard b in System.Enum.GetValues(typeof(ControlBoard)))
        {
            if (subscribe)
            {
                CardsConfig.ItemUseSlotOffset(b).SettingChanged += OnControlOffsetChanged;   // item-use slot = in-place move
                CardsConfig.ItemCardOffset(b).SettingChanged += OnControlOffsetChanged;      // item fan/held pose (read live by ItemsPile)
                // The two ENGRAVED rest captions. They are re-seated by RestControls.SetOffset, the
                // same call the rest discs' own offset lands in, so they ride this handler rather
                // than needing one of their own. WITHOUT THESE TWO LINES the dials would exist,
                // persist and read back correctly and move nothing until the next board rebuild —
                // "der Regler hat keinen Einfluss", which this project has now shipped four times.
                CardsConfig.ShortRestCaptionOffset(b).SettingChanged += OnControlOffsetChanged;
                CardsConfig.LongRestCaptionOffset(b).SettingChanged += OnControlOffsetChanged;
                // [Cards] ConfirmUndoSize_{board} is GONE (retired 2026-08): the Confirm/Undo cap
                // size lives in [BoardButtons] for BOTH shapes now, and those edits already reach
                // the caps via the ButtonTuning.Version watch (PlayTray.ApplyButtonTuningIfChanged).
                CardsConfig.RestButtonShape(b).SettingChanged += OnControlSizeChanged;        // shape = rebuild the caps
                CardsConfig.GenericButtonShape(b).SettingChanged += OnControlSizeChanged;
                CardsConfig.SlotOverlayOffset(b).SettingChanged += OnOverlayOffsetChanged;
                CardsConfig.SlotOverlaySpacing(b).SettingChanged += OnOverlayOffsetChanged; // item 1: overlay pair spacing
                // 2026-08-11: the overlay SIZE is the same live-apply as its offset — one call
                // resizes both glows AND re-homes the resting card, which is the coupling itself.
                CardsConfig.SlotOverlayScale(b).SettingChanged += OnOverlayOffsetChanged;
                CardsConfig.InitiativeOffset(b).SettingChanged += OnInitiativeOffsetChanged;
                CardsConfig.PickBannerOffset(b).SettingChanged += OnPickBannerOffsetChanged;
                CardsConfig.AssetOffset(b).SettingChanged += OnAssetPoseChanged;
                CardsConfig.AssetPitch(b).SettingChanged += OnAssetPoseChanged;
                CardsConfig.AssetYaw(b).SettingChanged += OnAssetPoseChanged;
                CardsConfig.AssetRoll(b).SettingChanged += OnAssetPoseChanged;
                CardsConfig.BoardTilt(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardPitchMin(b).SettingChanged += OnOrientationChanged; // item 12: pitch-window edge re-clamps TrayPitch
                CardsConfig.BoardPitchMax(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardYaw(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardScale(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardPosOffset(b).SettingChanged += OnOrientationChanged;
                CardsConfig.ActiveOffset(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.ActiveCardScale(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.ActiveGridSpacing(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.PileOffset(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.PileScale(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.PileSpacing(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.ObjectivesOffset(b).SettingChanged += OnObjectivesTuningChanged;
                CardsConfig.ObjectivesScale(b).SettingChanged += OnObjectivesTuningChanged;
                CardsConfig.ElementsOffset(b).SettingChanged += OnElementsTuningChanged;
                CardsConfig.ElementsScale(b).SettingChanged += OnElementsTuningChanged;
                CardsConfig.PinOffset(b).SettingChanged += OnHudWidgetTuningChanged;
                CardsConfig.ReadoutOffset(b).SettingChanged += OnHudWidgetTuningChanged;
                CardsConfig.DecisionOffset(b).SettingChanged += OnDecisionTuningChanged;
                CardsConfig.DecisionScale(b).SettingChanged += OnDecisionTuningChanged;
            }
            else
            {
                CardsConfig.ItemUseSlotOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.ItemCardOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.ShortRestCaptionOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.LongRestCaptionOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.RestButtonShape(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.GenericButtonShape(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.SlotOverlayOffset(b).SettingChanged -= OnOverlayOffsetChanged;
                CardsConfig.SlotOverlaySpacing(b).SettingChanged -= OnOverlayOffsetChanged; // item 1: overlay pair spacing
                CardsConfig.InitiativeOffset(b).SettingChanged -= OnInitiativeOffsetChanged;
                CardsConfig.PickBannerOffset(b).SettingChanged -= OnPickBannerOffsetChanged;
                CardsConfig.AssetOffset(b).SettingChanged -= OnAssetPoseChanged;
                CardsConfig.AssetPitch(b).SettingChanged -= OnAssetPoseChanged;
                CardsConfig.AssetYaw(b).SettingChanged -= OnAssetPoseChanged;
                CardsConfig.AssetRoll(b).SettingChanged -= OnAssetPoseChanged;
                CardsConfig.BoardTilt(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardPitchMin(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardPitchMax(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardYaw(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardScale(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardPosOffset(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.ActiveOffset(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.ActiveCardScale(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.ActiveGridSpacing(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.PileOffset(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.PileScale(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.PileSpacing(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.ObjectivesOffset(b).SettingChanged -= OnObjectivesTuningChanged;
                CardsConfig.ObjectivesScale(b).SettingChanged -= OnObjectivesTuningChanged;
                CardsConfig.ElementsOffset(b).SettingChanged -= OnElementsTuningChanged;
                CardsConfig.ElementsScale(b).SettingChanged -= OnElementsTuningChanged;
                CardsConfig.PinOffset(b).SettingChanged -= OnHudWidgetTuningChanged;
                CardsConfig.ReadoutOffset(b).SettingChanged -= OnHudWidgetTuningChanged;
                CardsConfig.DecisionOffset(b).SettingChanged -= OnDecisionTuningChanged;
                CardsConfig.DecisionScale(b).SettingChanged -= OnDecisionTuningChanged;
            }
        }
    }

    private void OnControlOffsetChanged(object sender, System.EventArgs e) => _applyControlOffsets = true;
    private void OnControlSizeChanged(object sender, System.EventArgs e) => _applyControlRebuild = true;
    private void OnOverlayOffsetChanged(object sender, System.EventArgs e) => _applyOverlayOffset = true;
    private void OnInitiativeOffsetChanged(object sender, System.EventArgs e) => _applyInitiativeOffset = true;
    private void OnPickBannerOffsetChanged(object sender, System.EventArgs e) => _applyPickBannerOffset = true;
    private void OnAssetPoseChanged(object sender, System.EventArgs e) => _applyAssetPose = true;
    private void OnOrientationChanged(object sender, System.EventArgs e) => _applyOrientation = true;

    /// <summary>Movement scheme active before the latest [Cards] BoardMoveMode edit.</summary>
    private BoardMoveMode _lastMoveMode = BoardMoveMode.Limited;

    /// <summary>
    /// User addendum to item 12: switching INTO a more restrictive movement scheme RESETS the
    /// axes that scheme locks, instead of freezing (or clamping) them where they happen to be —
    /// an upside-down or steeply pitched Frei pose must not survive into Begrenzt, and a
    /// Frei pitch beyond the window must not enter "Begrenzt mit Neigung" pinned at the clamp.
    /// Restrictiveness order: Frei (nothing locked) &lt; Begrenzt mit Neigung (roll locked)
    /// &lt; Begrenzt (roll + pitch locked). Roll is already dropped structurally by the level
    /// carry; the one axis with persisted state is the pitch, so the reset clears TrayPitch.
    /// Loosening the mode (Begrenzt → Frei) resets nothing. Subscribed BEFORE
    /// <see cref="OnOrientationChanged"/>, so the re-apply that follows sees the cleared pitch.
    /// </summary>
    private void OnMoveModeChanged(object sender, System.EventArgs e)
    {
        BoardMoveMode previous = _lastMoveMode;
        BoardMoveMode now = CardsConfig.BoardMoveMode.Value;
        _lastMoveMode = now;
        if (now == previous)
            return;
        static int Restrictiveness(BoardMoveMode m) => m switch
        {
            BoardMoveMode.Free => 0,
            BoardMoveMode.LimitedPitch => 1,
            _ => 2, // Limited
        };
        if (Restrictiveness(now) <= Restrictiveness(previous))
            return;
        if (Mathf.Abs(CardsConfig.TrayPitch.Value) > 0.01f)
        {
            VRLog.Info("Cards", $"Board move mode {previous} → {now}: locked-axis RESET — " +
                                $"TrayPitch {CardsConfig.TrayPitch.Value:F0}° → 0° (a restrictive " +
                                "mode starts level, it never freezes or clamps the old pose).");
            CardsConfig.TrayPitch.Value = 0f;
        }
    }
    private void OnActiveTuningChanged(object sender, System.EventArgs e) => _applyActive = true;
    private void OnPilesTuningChanged(object sender, System.EventArgs e) => _applyPiles = true;
    private void OnObjectivesTuningChanged(object sender, System.EventArgs e) => _applyObjectives = true;
    private void OnElementsTuningChanged(object sender, System.EventArgs e) => _applyElements = true;
    private void OnHudWidgetTuningChanged(object sender, System.EventArgs e) => _applyHudWidgets = true;
    private void OnDecisionTuningChanged(object sender, System.EventArgs e) => _applyDecision = true;
    private void OnFanTuningChanged(object sender, System.EventArgs e) => _applyFan = true;

    /// <summary>
    /// PART F: consume the per-board tuning dirty flags on the main thread and re-apply the
    /// matching element to the live board (offsets in place, sizes by rebuild, orientation via
    /// ReapplyOrientation). Every apply logs the element + the new value. No-op with no board.
    /// </summary>
    private void ApplyBoardTuning()
    {
        if (_tray.Root == null)
            return;
        ControlBoard b = CardsConfig.CurrentBoard;

        if (_applyControlRebuild)
        {
            _applyControlRebuild = false;
            _applyControlOffsets = false; // the rebuild re-reads the offsets from config
            _rest.Destroy();
            _tray.RebuildAttachedControls(); // rebuilds Confirm/Undo AND purges the dead rest laser targets
            _rest.EnsureBuilt(_tray);
            _restTuningVersion = WorldUI.ButtonTuning.Version; // this rebuild already reflects current [RestButtons] geometry
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rebuilt Generic Confirm/Undo ({CardsConfig.GenericButtonShape(b).Value}, " +
                                $"[BoardButtons] width {WorldUI.ButtonTuning.BoardCapWidth:F3} m) + Rest buttons ({CardsConfig.RestButtonShape(b).Value}, " +
                                $"diameter {CardsConfig.RestButtonDiameter.Value:F3} m).");
        }
        // [RestButtons] geometry live-apply (W/H/D/Travel): the rest keycaps read ButtonTuning in
        // EnsureBuilt, so a settings-panel stepper edit (ButtonTuning.Version bump) rebuilds JUST the
        // rest caps on their existing anchors — no restart. This is a REST-ONLY rebuild on purpose:
        // it must NOT route through PlayTray.RebuildAttachedControls (which would bump the tray's own
        // _tuningVersion and starve PlayTray.ApplyButtonTuningIfChanged of the gear/follow dashboard
        // rebuild). PurgeDeadLaserTargets drops the destroyed caps' stale laser entries; EnsureBuilt
        // re-registers the fresh ones and logs the applied W/H/D/Travel.
        else if (_restTuningVersion != WorldUI.ButtonTuning.Version)
        {
            _restTuningVersion = WorldUI.ButtonTuning.Version;
            _rest.Destroy();
            _tray.PurgeDeadLaserTargets();
            _rest.EnsureBuilt(_tray);
        }
        if (_applyControlOffsets)
        {
            _applyControlOffsets = false;
            _rest.SetOffset(CardsConfig.RestButtonOffset.Value, CardsConfig.RestStackSpacing.Value);
            _tray.SetConfirmUndoOffset(CardsConfig.ConfirmUndoOffset.Value, CardsConfig.ButtonStackSpacing.Value);
            _tray.SetItemUseSlotOffset(CardsConfig.ItemUseSlotOffset(b).Value); // items rework: move the use slot in place
            // ItemCardOffset (req #2) is read LIVE by ItemsPile every Tick, so an open item fan moves
            // immediately with no push needed here — logged for parity with the other control offsets.
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rest offset {CardsConfig.RestButtonOffset.Value} " +
                                $"(spacing ×{CardsConfig.RestStackSpacing.Value:F3} of the board's own pad pitch), " +
                                $"generic keycap offset {CardsConfig.ConfirmUndoOffset.Value} " +
                                $"(spacing ×{CardsConfig.ButtonStackSpacing.Value:F3} of the board's own " +
                                $"{_tray.SeatPitch * 1000f:F1} mm recess pitch), " +
                                $"item-use slot offset {CardsConfig.ItemUseSlotOffset(b).Value}, " +
                                $"item-card offset {CardsConfig.ItemCardOffset(b).Value}.");
        }
        if (_applyOverlayOffset)
        {
            _applyOverlayOffset = false;
            _tray.SetOverlayOffset(CardsConfig.SlotOverlayOffset(b).Value, CardsConfig.SlotOverlaySpacing(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: slot overlay offset {CardsConfig.SlotOverlayOffset(b).Value} " +
                                $"(spacing {CardsConfig.SlotOverlaySpacing(b).Value:F3} m, " +
                                $"size {CardsConfig.SlotOverlayScale(b).Value:F3}× — glow AND resting card).");
        }
        if (_applyInitiativeOffset)
        {
            _applyInitiativeOffset = false;
            _tray.SetInitiativeOffset(CardsConfig.InitiativeOffset(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: initiative offset {CardsConfig.InitiativeOffset(b).Value}.");
        }
        if (_applyPickBannerOffset)
        {
            _applyPickBannerOffset = false;
            _tray.SetPickBannerOffset();
            VRLog.Info("Cards", $"Debug live-apply [{b}]: pick-status placard offset " +
                                $"{CardsConfig.PickBannerOffset(b).Value} (the \"Wähle N Karte(n)\" line; " +
                                "a peer's remote board mirrors the same offset).");
        }
        if (_applyAssetPose)
        {
            _applyAssetPose = false;
            var assetEuler = new Vector3(CardsConfig.AssetPitch(b).Value,
                                         CardsConfig.AssetYaw(b).Value,
                                         CardsConfig.AssetRoll(b).Value);
            _tray.SetAssetPose(CardsConfig.AssetOffset(b).Value, assetEuler);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: asset-only pose — offset " +
                                $"{CardsConfig.AssetOffset(b).Value}, pitch/yaw/roll {assetEuler} °" +
                                " (anchors pinned; the mesh moved underneath them).");
        }
        if (_applyOrientation)
        {
            _applyOrientation = false;
            _expectedPoseChange = "user-settings (orientation tuning)"; // sanctioned move (issue C watchdog)
            _tray.ReapplyOrientation();
            VRLog.Info("Cards", $"Debug live-apply [{b}]: orientation tilt {CardsConfig.BoardTilt(b).Value:F0}°, " +
                                $"yaw {CardsConfig.BoardYaw(b).Value:F0}°, scale {CardsConfig.BoardScale(b).Value:F2}×, " +
                                $"posOffset {CardsConfig.BoardPosOffset(b).Value}.");
        }
        if (_applyActive)
        {
            _applyActive = false;
            _tray.SetActiveOffset(CardsConfig.ActiveOffset(b).Value); // move the mount in place
            _active.ApplyLayout();                                    // re-lay from the per-board scale + grid step
            VRLog.Info("Cards", $"Debug live-apply [{b}]: active offset {CardsConfig.ActiveOffset(b).Value}, " +
                                $"card scale {CardsConfig.ActiveCardScale(b).Value:F2}×, grid step {CardsConfig.ActiveGridSpacing(b).Value}.");
        }
        if (_applyPiles)
        {
            _applyPiles = false;
            _tray.SetPileOffset(CardsConfig.PileOffset(b).Value); // move the mount in place
            _piles.ApplyLayout();                                 // re-seat both stacks (scale + inter-pile spacing)
            VRLog.Info("Cards", $"Debug live-apply [{b}]: pile offset {CardsConfig.PileOffset(b).Value}, " +
                                $"scale {CardsConfig.PileScale(b).Value:F2}×, spacing {CardsConfig.PileSpacing(b).Value:F3} m.");
        }
        if (_applyObjectives)
        {
            _applyObjectives = false;
            // Move + resize the docked panel in place: the surface pose-follows the mount's
            // position AND lossyScale each tick, so no panel reconvert is needed.
            _tray.SetObjectivesLayout(CardsConfig.ObjectivesOffset(b).Value, CardsConfig.ObjectivesScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: objectives offset {CardsConfig.ObjectivesOffset(b).Value}, " +
                                $"scale {CardsConfig.ObjectivesScale(b).Value:F2}×.");
        }
        if (_applyElements)
        {
            _applyElements = false;
            _tray.SetElementsLayout(CardsConfig.ElementsOffset(b).Value, CardsConfig.ElementsScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: elements offset {CardsConfig.ElementsOffset(b).Value}, " +
                                $"scale {CardsConfig.ElementsScale(b).Value:F2}×.");
        }
        if (_applyHudWidgets)
        {
            _applyHudWidgets = false;
            _tray.SetPinOffset(CardsConfig.PinOffset(b).Value);
            _tray.SetReadoutOffset(CardsConfig.ReadoutOffset(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: pin offset {CardsConfig.PinOffset(b).Value}, " +
                                $"readout offset {CardsConfig.ReadoutOffset(b).Value}.");
        }
        if (_applyDecision)
        {
            _applyDecision = false;
            _tray.SetDecisionLayout(CardsConfig.DecisionOffset(b).Value, CardsConfig.DecisionScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: decision-dock offset {CardsConfig.DecisionOffset(b).Value}, " +
                                $"scale {CardsConfig.DecisionScale(b).Value:F2}×.");
        }
    }
}
