using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// USE-SLOT BARS DOCK: the game's four in-scenario "use bar" surfaces — the active-bonus
/// toggles (<c>UIActiveBonusBar</c>), the ability-card element-consume augments
/// (<c>UIUseAugmentationsBar</c>), the element-infusion / choose-ability pickers
/// (<c>UIUseAbilitiesBar</c>) and the usable-items bar (<c>UIUseItemsBar</c>) — live on the
/// flat 2D HUD the VR conversion hides, so EVERY decision they carry was unreachable in VR:
/// element-creating potions (element sub-picker in an item slot), passive worn items adding
/// persistent toggles, active cards augmenting future events, and — the deadlock-critical
/// one — the END-OF-ABILITY unpicked "Any" infusion (<c>CardsActionControlller.
/// PickAnyInfusionElements</c> → <c>UIUseAbilitiesBar.ShowInfusionsAction</c>: if the pick
/// is never answered the turn NEVER ends; verified decompiled Choreographer.cs:8425/8477).
///
/// This surface polls the four bar singletons per tick (they are HUD widgets, not
/// <c>UIWindow</c>s — no window events fire) and, whenever a bar actually HAS slots,
/// converts the bar's own root canvas subtree pokeable onto the control board via the
/// proven <see cref="CanvasConversion"/> path (the decision-dock mechanism — NOT the
/// occlusion-bugged TrayControlDockSurface approach, see that file's header). The
/// player then clicks the game's REAL widgets: <c>UIUseSlot.Toggle()</c> and everything
/// behind it (<c>ToggleActiveBonus</c>/<c>ToggleActionAugmentation</c>/<c>ToggleItem</c>/
/// the infuse network sends) are the game's own MP-synced click paths — the mod never
/// bypasses them, it only makes the widgets reachable. Empty bars never dock; a bar
/// releases (restoring its exact 2D home) the moment its last slot hides.
///
/// EMBEDDED SUB-PICKERS ARE PART OF THE CONVERTED SUBTREE — BY CONSTRUCTION. The element
/// picker (<c>UIElementPicker</c>) and option picker (<c>UIOptionPicker</c>) are
/// <c>[SerializeField]</c> references ON the slot prefabs themselves
/// (<c>UIUseConsumeInfuseSlot.elementPicker</c>, <c>UIUseConsumeInfuseOptionsSlot.
/// optionPicker</c>, <c>UIUseAugmentation.optionPicker</c>, plus the option widgets
/// <c>optionsUI</c>/<c>initiativeOption</c> the initiative/forgo/choose-ability
/// controllers drive), and every slot is <c>Object.Instantiate(slotPrefab, container)</c>
/// under the bar's serialized <c>container</c> (all four bars, verified decompiled).
/// A prefab's serialized component reference can only point INSIDE the prefab (Unity
/// serialization rule — prefab assets cannot reference scene objects), so the picker
/// RectTransforms are descendants of each slot instance, i.e. descendants of the
/// converted bar root: poke and laser reach them through the same host raycaster.
/// The pickers' popup <c>content</c> can extend past the bar's authored strip, so the
/// host is fitted UNCLAMPED (<see cref="ConvertedPanel.FitFrameDegenerate"/>): the
/// visible-graphics union IS the host rect, and it grows to cover an open picker —
/// RayUguiDriver intersects the host plane rect, so an uncovered popup would be
/// laser-dead (the test #14 lesson).
///
/// PLACEMENT: the bars stack as a second "drawer" in the decision-dock zone below the
/// board, pose-following <see cref="PlayTray.DecisionMount"/> (no new mount — the mount
/// seam contract says hosts pose-follow, never re-parent). While the decision dock
/// actually holds a prompt row (<see cref="DecisionDockSurface.RowDocked"/> — both ARE
/// live at once during take-damage: reduce-damage bonuses + OnAttacked items + the
/// burn choice) the bar stack hangs a small clearance below the decision row's MEASURED
/// bottom edge (<see cref="DecisionDockSurface.RowBottomUpMeters"/>, deadband-latched
/// here so the row's few-px hover breathing never wobbles the stack) — the two read as
/// ONE connected decision area; the old worst-case ±MaxHeight/2 shift (abstand.png: the
/// armor slot floating far below the burn choices) remains only as the fallback while
/// no measurement exists yet. Otherwise the bars occupy the drawer zone itself. Shared
/// tray density (× <see cref="DensityScale"/>), width-only dock fit (heights grow
/// downward — an opening picker must grow the panel, not shrink its glyphs). No
/// tray/mount → HMD-anchored fallback float (the ModalFallback pattern: a decision must
/// never be invisible).
///
/// TOP-FLUSH, ALWAYS (user ruling 2026-08-09: "Der obere Rand des Entscheidungsbereichs
/// wurde mit dem offset festgelegt. Von da sollen die Elemente immer ausnahmslos anfangen
/// und nach unten wachsen."). The lane cursor is placed against the bar's VISIBLE top edge,
/// not its host rect's: the content fit leaves slack around the measured union and centres
/// the union inside the host it produces (<see cref="ConvertedPanel.FitContentPadding"/>),
/// so pinning the host's own edge seated every bar that slack below the ceiling and
/// compounded the error down the stack — the reported "die Elemente rutschen tiefer als es
/// sein müsste".
///
/// FIT STABILITY (the "docked symbol jumps on hover/press" fix): the game's slot widgets
/// REACT to interaction — <c>ExtendedButton</c> scales its target rect by
/// <c>highlightScaleFactor</c> on pointer enter, and select/press toggles
/// <c>selectedMask</c>/<c>optionalHiglight</c>/<c>mandatoryHiglight</c>
/// (<c>UIUseSlot.Refresh</c>). Under the unclamped degenerate fit those transients
/// change the visible-graphics union (hardware log: 88x152 → 94x165 on hover → 94x217 px
/// on select, oscillating), each applied re-fit resizes the host AND re-centers the
/// target inside it, and the stack re-places from the measured rect — the symbol jumped.
/// Time-based hysteresis cannot fix this (a hover lasts seconds and would "stabilize"
/// into a re-fit), so the surface HOLDS the fit frozen (<c>ConvertedPanel.FitEnabled</c>
/// off — a surface-side policy, zero shared-machinery changes) whenever the layout truth
/// is unchanged, and re-arms it only for the states that legitimately change geometry:
/// the post-dock settle window, an actual slot-set change (the bar container's active
/// children — layout truth, hover scaling never touches it), and an OPEN element/option
/// sub-picker (<c>UIElementPicker</c>/<c>UIOptionPicker.IsOpen</c> — the degenerate-fit
/// growth that must keep working; the fit check is forced the same tick a picker opens,
/// and every tick of the short close window so the growth is handed back at once).
/// Poke presses never move the host physically (UguiPokeSurfaces writes no host
/// transforms) — the press jump was purely this fit path.
///
/// AND THE COLLAPSE IS INSTANT (user ruling 2026-08-09: "Schließt man dieses 'Ausklappen'
/// geht der button ca. 2 Sekunden verzögert wieder zu seiner Ursprungsposition. Das will
/// ich sofort." — the Flitzstiefel's two-option column). That delay was never a tween: it
/// is the SHARED fit machinery's shrink damping (<c>FitStableSeconds</c> 0.5 +
/// <c>FitRefitMinIntervalSeconds</c> 1.5, plus the ~0.4 s periodic check throttle), which
/// exists to stop OSCILLATING content re-fitting twice a second. This dock does not need a
/// clock for that — the layout-truth hold above is strictly better, and it means no hover or
/// press transient ever reaches the fit path — so the dock opts out
/// (<see cref="ConvertedPanel.FitShrinkImmediate"/>) and the panel is now SYMMETRIC: growth
/// was always immediate, shrink is too. The user's ruling explicitly overrides the project's
/// "everything moves with the animation" rule for this one direction.
///
/// STATUS: while a docked bar carries a decision the game is WAITING on (an unselected
/// abilities-bar slot — infusion/choose-ability; a pending MANDATORY active bonus), a
/// short hint runs over the existing pick-banner seam (<see cref="PlayTray.SetPickStatus"/>,
/// change-gated; keys <c>bars_waiting_element</c>/<c>bars_waiting_bonus</c>). The banner
/// is shared with the CardsDriver pick flows, but those (modal card picks) and the bar
/// flows are mutually exclusive game phases; the hint is pushed/cleared only on change so
/// it can never tick-fight another writer.
///
/// CONFIRM AFFORDANCES (audited per flow, decompiled Choreographer.cs):
/// - active-bonus / augment / item toggles commit per-click; the surrounding step
///   completes via the game ReadyButton (states CONTINUE/CONFIRMTARGETS/…), which the
///   board CONFIRM drives (CardsDriver.OnConfirmRequested → CardsGameApi.ClickReady);
/// - END-OF-ACTION toggles (Choreographer.cs:11929 ShowEndOfActionToggleBonuses) raise
///   the bar with <c>readyButton.Toggle(active: false, …)</c> and ONLY the SkipButton
///   visible ("GUI_SKIP_ABILITY") — the ButtonCluster's Skip twin mirrors exactly
///   <c>m_SkipButton.gameObject.activeInHierarchy</c> (ButtonCluster.MirrorSkip), so the
///   only out of that state is a physical board button;
/// - the end-of-ability infusion and choose-ability commit through the slot click itself
///   (<c>ShowInfusionsAction</c>'s onSelect hides the bar and completes the phase; the
///   choose-ability slot is pre-selected and the CONFIRM/ReadyButton AlternativeAction
///   finishes it) — no extra affordance needed.
///
/// ONE CHARACTER OWNS A DECISION (user ruling 2026-08-08, the same rule
/// <see cref="DecisionDockSurface"/> follows — "Die Entscheidung soll auch nur für den
/// jeweiligen Character angezeigt werden!"). Since ModBuild 80 an item is used by
/// PLACING its card in the board's item-use slot, and the follow-up element choice
/// appears here, in this drawer — so from the player's side these bars ARE "die
/// Entscheidung", and leaving them up while the player looks at a teammate is exactly
/// the complaint. Every bar carries the actor the GAME raised it for
/// (<c>UIUseItemsBar.actor</c> :40/:372/:431, <c>UIUseAugmentationsBar.actor</c>
/// :27/:252, <c>UIUseAbilitiesBar.actor</c> :172/:272/:295/:310/:330,
/// <c>UIActiveBonusBar.actors</c> :25/:236 — a LIST, the bar can be raised for several
/// at once), so ownership is read from the model, never from turn state (a bar outlives
/// a turn boundary just as a prompt does). While the focused character is not among a
/// bar's owners, that bar is RENDER-HIDDEN; it returns unchanged the moment the owner is
/// focused again. Unresolvable owner ⇒ shown (an unanswerable decision is worse than a
/// visible one — the same fail-open direction as everywhere else in this drawer).
///
/// THE HIDE CANNOT DISTURB THE PENDING CHOICE — structurally. It clears
/// <c>Canvas.enabled</c> and <c>Renderer.enabled</c> on the MOD-OWNED converted host subtree
/// (nested canvases and the mixed-reality backing plate included — that plate is a
/// MeshRenderer, and leaving it on is what produced the ModBuild 84 report of an empty dark
/// rectangle where the bar had been) and writes nothing else: no game method, no
/// <c>SetActive</c> on a game object. That
/// matters here more than anywhere: the slots ARE <c>ExtendedButton</c>s, whose
/// <c>OnDisable</c> raises <c>ActiveChanged(false)</c>, un-highlights and can clear the
/// EventSystem selection + invoke <c>onDeselected</c> (ExtendedButton.cs:300-320), and an
/// OPEN element picker mid-choice must survive untouched. It also leaves the requirement-C
/// items split alone by construction: <see cref="ItemsPopulated"/> keys on slot
/// <c>activeSelf</c>, which the hide never writes, and <see cref="EnforceItemsSplit"/> keys on
/// hierarchy containment plus each slot's own Graphics — so a hidden bar neither releases its
/// dock nor re-exposes a plain symbol, and coming back needs no second placement. The fit is FROZEN while hidden
/// (<c>ConvertedPanel.FitEnabled</c> off, the existing stability hold) so the panel returns
/// at exactly the geometry it left with. Input is impossible meanwhile: both interactors
/// skip a canvas that is not <c>isActiveAndEnabled</c> (RayUguiDriver:132/312,
/// PokeInteractor:298).
///
/// ─── THE FOUR DOCKS SHARE ONE ROOT, AND THAT IS HOW THE BRILLE CAME BACK ────────────────────
/// User report 2026-08-24 (ModBuild 242), verbatim: „Der Gegenstand der Brille wurde wieder in
/// der Entscheidungsarea angezeigt. Alle Gegenstände sollen nur über die gebaute
/// Item-Interaktion nutzbar sein. Die jeweiligen Symbole sollen daher nicht erscheinen. Dort
/// sollen NUR die Entscheidungen erscheinen, die neben dem eigentlichen Auslösen des
/// Gegenstands an Entscheidungen getroffen werden müssen. Die Brille fällt da nicht rein —
/// während einer Angriffsaktion kann die Brille als Karte in den Bereich gelegt und ausgelöst
/// werden."
///
/// <para>THE LINE, AS A TESTABLE RULE: a widget belongs in the decision area iff answering it is
/// a DECISION — a target, a number, an either/or, an element pick. A widget whose entire effect
/// is "trigger this item" does not, because the item interaction (card into the board's recess,
/// poke USE) is the only way an item may be triggered. Per dock that resolves to:
/// <c>UseBarItems</c> carries triggers (plain slots) AND decisions (sub-choice slots) and is
/// therefore SPLIT; <c>UseBarActiveBonus</c> likewise (item-backed, option-less, optional rows
/// are triggers — <see cref="EnforceActiveBonusSplit"/>); <c>UseBarAbilities</c> and
/// <c>UseBarAugments</c> carry NOTHING BUT decisions (end-of-ability infusion / choose-ability;
/// ability-card element consumes — <c>ConsumeButton.abilityConsume</c>, never an item) and are
/// not filtered at all. That is why the rule is per-dock and never a blanket suppression.</para>
///
/// <para>ROOT CAUSE, and it is not a regression of a rule that stopped firing — it is a rule that
/// was NEVER reached. All four bar singletons hang off ONE game object (every conversion in the
/// hardware log reports <c>root 'UseItemsBar'</c>, for the bonus dock as well as the augment
/// dock), so a dock converts a subtree that contains the OTHER bars' slot containers too, and
/// <see cref="ConvertedPanel"/>'s content fit measures <c>root.GetComponentsInChildren</c> —
/// the whole subtree. The reporting log proves it: the fit of
/// <c>Panel_UseBarAugments</c> measured <c>'UIUseItem(Clone)/Slot' 60x60px at (-64,-30)</c> beside
/// the augment's own <c>'UIUseConsumeAbilityElement/Slot'</c> at (4,-30) — two tiles, 128x60 px,
/// exactly the picture in brille.jpg — and the player then laser-hovered <c>UIUseItem(Clone)</c>
/// and the Eagle-Eye_Goggles art mip-baked. Meanwhile <see cref="EnforceItemsSplit"/>, the pass
/// whose whole job is "a plain item symbol never shows", was gated on the ITEMS dock being
/// converted — and the items dock only converts for a SUB-CHOICE slot, which the Brille has none
/// of. Its own log line (<c>items-bar SPLIT</c>) reads ZERO in both hardware logs of that
/// session: the rule never ran, in any build, for any plain-only bar. The gate is now the
/// MEASURED question instead of a proxy for it — is the items bar's slot container inside the
/// subtree some dock actually converted (<see cref="ItemsHostDock"/>) — which is a predicate that
/// can be observed to FAIL and says so in the log.</para>
///
/// <para>AND THE SUPPRESSION IS RENDER-LEVEL, NOT <c>SetActive(false)</c> — that change is what
/// makes widening the gate safe. Three mod flows resolve an item's slot through
/// <c>CardsGameApi.LiveItemsBarSlot</c>, which requires an ACTIVE object: the take-damage shield
/// placement (<c>ItemsPile.TickTakeDamagePick</c>/<c>HandleTakeDamageDrop</c> — without it the
/// recess never appears and a shield card is UNPLACEABLE, a deadlock, which is worse than the
/// bug), the untoggle-on-grab-back path, and the ordinary place-to-use confirm, whose fallback
/// (<c>UseItemService</c> direct) silently loses the FIXED-element consume auto-resolve that the
/// slot click performs. Disabling the slot's <see cref="Graphic"/> components removes every pixel
/// and every raycast target while leaving <c>gameObject.activeSelf</c> — and the game's own
/// pooling, and <c>ExtendedButton.OnDisable</c>'s deselect side effects — untouched. The row stays
/// fully clickable BY CODE and is only invisible, which is the same shape
/// <see cref="EnforceActiveBonusSplit"/> already relies on.</para>
///
/// <para>REJECTED, so a later round does not re-try them: (a) hiding the whole augment/ability
/// dock — those are the decisions the user explicitly asked to KEEP ("die Entscheidungen, die
/// neben dem eigentlichen Auslösen des Gegenstands getroffen werden müssen"); (b) making the
/// items split unconditional with <c>SetActive(false)</c> — that is the shield deadlock above;
/// (c) standing the split down during take-damage (what <c>ItemsPile.EnforceChoiceSlotSplit</c>
/// must do for its half) — unnecessary here, because the render hide does not break the
/// active-slot seam, so the OnAttacked item symbols are suppressed too and the user's rule holds
/// during a damage decision as well; (d) fixing it on the wire — the peer never saw this leak at
/// all (see below), it is purely what the OWNER's board draws.</para>
///
/// MP — THE BARS THEMSELVES NOW RIDE THE WIRE (record 25; the paragraph below used to read
/// "zero wire changes"). Every interaction still is a real widget click on local-player UI and
/// peers still see the RESULT via the game's own sync (proxy paths <c>ProxyUseActiveBonus</c>/
/// <c>ProxyToggleAugment</c>/<c>ProxyInfuseAbility</c> replay on remotes untouched) — but the
/// DISPLAY did not travel, and under the 2026-08-08 ruling it must: these four bars are HUD
/// singletons raised on ONE client, so a peer saw nothing below the decision row while the owner
/// looked at a whole drawer of slots. <see cref="SampleWire"/> publishes the structure and the
/// state (which bars, how many slots, offered/dimmed/chosen, sub-picker open) — never a slot's
/// identity, which for these widgets is CARD ART and has no textual form at all (see
/// <c>Net.NetProtocol.ExtIdUseBars</c>). The focus HIDE above travels with it: a render-hidden
/// bar is dropped from the published mask, so a peer's copy empties in the same frames.
/// <para>THE BRILLE LEAK NEVER RODE THIS WIRE, and that is why the fix is sender-side presentation
/// with NO wire change at all: <see cref="BarDock.SampleWireSlots"/> walks the bar's OWN
/// <c>container</c>, so the augment dock published <c>augments: 1 slot(s)</c> while its owner was
/// looking at TWO tiles. The peer's mirror was already the picture the user asked for and the
/// owner's was not; after this pass the two agree — one tile each — which is a strict improvement
/// in the mirror's fidelity and costs no bit. The plain-item render hide is additionally taught to
/// the sampler (<see cref="IsPlainRenderHidden"/>) so that when the ITEMS dock itself is up for a
/// sub-choice slot, a suppressed-but-active plain slot can never become a phantom tile on the peer's
/// board — the failure mode the empty-bar drop below was written for, one level down.</para>
/// Reversibility: pure CanvasConversion (Release restores the exact 2D home); nothing
/// destroyed, nothing re-layered permanently; every pass TickGuard-isolated by the module.
/// </summary>
internal sealed class UseBarsSurface
{
    /// <summary>Bar glyphs are read under pressure — 0.8× tray density = 1.25× bigger (the decision-dock rationale).</summary>
    private const float DensityScale = 0.8f;

    /// <summary>Dock-fit guards (test #16 semantics, width axis only).</summary>
    private const float MaxFitScale = 1f;
    private const float MinFitScale = 0.5f;

    /// <summary>Clearance between the decision row's measured bottom edge and the bar stack top (tray-local m).</summary>
    /// <remarks>INTERNAL since the 1:1 remote mirror of this drawer (wire record 25):
    /// <c>Net.RemoteBoardFurniture</c> hangs its mirrored bar stack the same clearance below the
    /// mirrored decision row and ALIASES this constant rather than keeping a hand-copied duplicate
    /// — the fix <c>scripts/check-mirrors.sh</c> exists to provoke (see
    /// <see cref="DecisionDockSurface.BarClearanceMeters"/>, which went the same way).</remarks>
    internal const float DecisionClearance = 0.015f;

    /// <summary>
    /// Deadband (uGUI px at row density) on the decision row's live bottom edge before the
    /// bar stack follows it: the row's own widgets hover-scale a few px
    /// (<c>ExtendedButton.highlightScaleFactor</c>), and without the latch that breathing
    /// would wobble the whole bar stack. Real changes (prompt switch, live gap re-tune)
    /// move the edge by tens of px and pass; the latch also resets whenever the row
    /// undocks (RowBottomUpMeters goes null).
    /// </summary>
    private const float RowBottomDeadbandPx = 8f;

    /// <summary>Vertical gap between stacked bars (tray-local m).</summary>
    /// <remarks>INTERNAL for the same reason as <see cref="DecisionClearance"/>: the mirrored bar
    /// stack on a peer's board stacks its rows with THIS gap, by alias, not by copy.</remarks>
    internal const float StackGap = 0.012f;

    /// <summary>Per-bar proud step toward the viewer — stops equal-order world canvases depth-tying (flicker lesson).</summary>
    private const float ProudStep = 0.004f;

    /// <summary>No-tray fallback float distance / shrink (the DecisionDockSurface values).</summary>
    private const float FloatDistanceMeters = 1.1f;
    private const float FloatScaleFactor = 0.7f;

    /// <summary>Vertical spacing of the HMD-float stack (real m before world scale).</summary>
    private const float FloatStackStep = 0.26f;

    private readonly BarDock[] _docks;
    private bool _floatPlaced;
    private int _floatPlacedCount = -1;
    private string? _lastHint;
    private bool _hintPushed;

    // Decision-row bottom latch (see the FIT STABILITY / PLACEMENT class doc).
    private float _rowBottomLatched;
    private bool _rowBottomValid;

    /// <summary>The items dock, held separately: the place-to-use split (EnforceItemsSplit)
    /// suppresses plain-use slots only on THIS dock while it is converted.</summary>
    private readonly BarDock _itemsDock;

    internal UseBarsSurface()
    {
        // Fixed stack order, top to bottom: bonuses (turn-defining toggles first), the
        // ability/infusion pickers (the deadlock-critical answers), augments, items.
        // The container accessor feeds the fit-stability hold: the bar's serialized slot
        // container is the LAYOUT truth (slots are Instantiate(prefab, container)), so its
        // active-children set changes exactly when the slot set does — never on hover.
        //
        // THE STACK ORDER IS ALSO THE WIRE ORDER: the four record-25 mask bits are assigned in
        // exactly this sequence (NetProtocol.UseBarActiveBonusBit … UseBarItemsBit), so a peer's
        // mirrored drawer stacks the bars the way the owner's own drawer does without anything
        // having to describe the order.
        _itemsDock = new BarDock("UseBarItems", ItemsRoot, ItemsPopulated, ItemsContainer, ItemsOwners,
            ItemSlotChosen, this);
        _docks = new[]
        {
            new BarDock("UseBarActiveBonus", ActiveBonusRoot, ActiveBonusPopulated, ActiveBonusContainer,
                ActiveBonusOwners, ActiveBonusSlotChosen, this),
            new BarDock("UseBarAbilities", AbilitiesRoot, AbilitiesPopulated, AbilitiesContainer,
                AbilitiesOwners, AbilitySlotChosen, this),
            new BarDock("UseBarAugments", AugmentsRoot, AugmentsPopulated, AugmentsContainer,
                AugmentsOwners, AugmentSlotChosen, this),
            _itemsDock,
        };
    }

    // ---- per-tick drive (registered in WorldUIModule, TickGuard-isolated) -----------------

    internal void Tick()
    {
        // FIRST, before the docks poll their populated state: an item-backed bonus row must never be
        // seen at all, not even for the one frame a "dock, then suppress, then release" ordering
        // would cost. ActiveBonusPopulated() filters the same rows on its own as well (belt and
        // braces — the game re-Shows pooled slots at will), so the dock gate agrees with what is
        // actually visible in the same tick.
        EnforceActiveBonusSplit();

        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Tick(); // convert / release, level-triggered on the polled slot state

        EnforceItemsSplit();      // req C: plain item slots never show in a docked (mixed) items bar
        UpdateFocusVisibility();  // one character owns a decision — hide bars whose owner is not in view
        StackDocked();            // …so the stack closes up over a hidden bar in the SAME tick
        UpdateWaitingHint();
        // …and deliberately LAST, after the hide has been applied: what rides the wire is what this
        // board SHOWS, so the sampler reads the same FocusHidden flags the stack just honoured.
        SampleWire();
    }

    /// <summary>
    /// Re-stack at the END of the frame. The bar hosts pose-follow <c>PlayTray.DecisionMount</c>,
    /// and the board's carry writer (<c>PanelGrabHandle.Update</c>) has no execution-order relation
    /// to this Update tick — so an Update-only copy of the mount pose renders a frame behind a
    /// board that is being carried (see <c>WorldSurface.LateTick</c> for the full derivation; the
    /// user asked for the piles' rigidity "allgemein bei allen Elementen die an dem Controllboard
    /// dran sind"). <see cref="StackDocked"/> derives every pose from the mount and the fitted
    /// rects and latches nothing that a second call could double-apply, so re-running it is
    /// idempotent; conversion/release and the waiting hint stay on the Update tick.
    /// </summary>
    internal void LateTick() => StackDocked();

    internal void Shutdown()
    {
        RestorePlainHidden(Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null); // req C: leave the 2D bar exactly as authored
        RestoreBonusHidden();     // …and the same for the item-backed bonus rows (pure, reversible)
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Shutdown();
        ClearHint();
        _floatPlaced = false;
        _floatPlacedCount = -1;
        _rowBottomValid = false;
        // The wire seam is static and outlives this instance: a module teardown must withdraw
        // record 25 explicitly, or peers would keep the last drawer standing on a board that no
        // longer has one (the same contract DecisionDockSurface's undock publish honours).
        _nextWireAt = 0f;
        SampleWire();
    }

    // ---- bar detection (polled — the bars raise no window events) -------------------------

    private static RectTransform? ActiveBonusRoot() =>
        Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance.transform as RectTransform : null;

    /// <summary>
    /// Any live active-bonus slot showing (pooled slots are SetActive(false) on remove) — MINUS the
    /// rows that are answered by placing an item card (<see cref="EnforceActiveBonusSplit"/>).
    ///
    /// <para>The predicate is applied here as well as in the split, for the same reason
    /// <see cref="ItemsPopulated"/> applies the sub-choice predicate rather than trusting the split
    /// to have run: the game re-activates pooled slots from its own callbacks
    /// (<c>CreateBonus</c>/<c>Show</c> on an element unreserve, a proxy replay), and a bar that
    /// docked for one frame on a row nobody may click is a visible flash of exactly the button this
    /// pass removes. With both in place the dock gate and the visible rows agree every tick.</para>
    /// </summary>
    private static bool ActiveBonusPopulated()
    {
        UIActiveBonusBar? bar = Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> kv in bar.activeBonusSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf
                && !CardsGameApi.BonusIsPlaceable(kv.Key))
                return true;
        }
        return false;
    }

    private static RectTransform? AbilitiesRoot() =>
        Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance.transform as RectTransform : null;

    private static bool AbilitiesPopulated()
    {
        UIUseAbilitiesBar? bar = Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<string, UIUseAbility> kv in bar.slots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf)
                return true;
        }
        return false;
    }

    private static RectTransform? AugmentsRoot() =>
        Singleton<UIUseAugmentationsBar>.IsInitialized
            ? Singleton<UIUseAugmentationsBar>.Instance.transform as RectTransform : null;

    private static bool AugmentsPopulated()
    {
        UIUseAugmentationsBar? bar = Singleton<UIUseAugmentationsBar>.IsInitialized
            ? Singleton<UIUseAugmentationsBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<ConsumeButton, UIUseAugmentation> kv in bar.augmentSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf)
                return true;
        }
        foreach (KeyValuePair<string, UIUseAugmentation> kv in bar.augmentGroupSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf)
                return true;
        }
        return false;
    }

    private static RectTransform? ItemsRoot() =>
        Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance.transform as RectTransform : null;

    // ---- slot containers (layout truth for the fit-stability hold; publicized fields) ----

    private static RectTransform? ActiveBonusContainer() =>
        Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance.container : null;

    private static RectTransform? AbilitiesContainer() =>
        Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance.container : null;

    private static RectTransform? AugmentsContainer() =>
        Singleton<UIUseAugmentationsBar>.IsInitialized
            ? Singleton<UIUseAugmentationsBar>.Instance.container : null;

    private static RectTransform? ItemsContainer() =>
        Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance.container : null;

    /// <summary>
    /// Requirement C (activation split): the items bar wants dock ONLY for slots whose
    /// activation opens a further SUB-CHOICE at the slot (element consume/infuse "Any" —
    /// <see cref="CardsGameApi.SlotNeedsSubChoice"/>, decompiled basis on that predicate).
    /// PLAIN use/toggle items are activated exclusively by physically placing the item card
    /// into the board's item-use slot (the ItemsPile clip-in flow), so their symbols never
    /// count toward docking — and if ALL visible slots are plain, the bar does not dock at
    /// all. The augment/ability bars are deliberately unaffected: their slots carry the
    /// choice UIs (infusion picks, choose-ability) the split keeps in the bars. The BONUS bar
    /// used to be unaffected too; since 2026-08-09 it has its own, narrower split
    /// (<see cref="EnforceActiveBonusSplit"/>) which removes only the item-backed rows that
    /// need no further option — the initiative ± / forgo / choose-ability / element-consume
    /// rows it still keeps, for exactly the reason stated here.
    /// A slot this surface itself suppressed (see <see cref="EnforceItemsSplit"/>) is a PLAIN slot
    /// and is excluded by the sub-choice test here anyway — note that since ModBuild 243 that
    /// suppression is a render hide, so such a slot is still <c>activeSelf</c> and the exclusion is
    /// carried by the predicate, not by the active flag.
    /// </summary>
    private static bool ItemsPopulated()
    {
        UIUseItemsBar? bar = Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<CItem, UIUseItemScenario> kv in bar.ItemSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf
                && CardsGameApi.SlotNeedsSubChoice(kv.Value))
                return true;
        }
        return false;
    }

    // ---- one character owns a decision: WHOSE bar is this? (read from the game's model) ----

    /// <summary>
    /// Resolve a game actor to the CHARACTER a focus can be on, and add it to
    /// <paramref name="into"/> without duplicates. A summon maps to its <c>Summoner</c> — the
    /// selectable hero, <c>CHeroSummonActor.Summoner</c>, the same mapping
    /// <c>CardsGameApi.TakeDamageSubject</c> and the initiative track use, so a bar raised for a
    /// summon belongs to the player who owns it. Anything that is not a player character (an
    /// enemy, an object) contributes NOTHING, which — with the fail-open rule in
    /// <see cref="UpdateFocusVisibility"/> — means such a bar is always shown.
    /// </summary>
    private static void AddOwner(List<CPlayerActor> into, CActor? actor)
    {
        CPlayerActor? owner = actor switch
        {
            CPlayerActor player => player,
            CHeroSummonActor summon => summon.Summoner,
            _ => null,
        };
        if (owner == null)
            return;
        for (int i = 0; i < into.Count; i++)
        {
            if (ReferenceEquals(into[i], owner))
                return;
        }
        into.Add(owner);
    }

    /// <summary>The items bar's own actor — <c>UIUseItemsBar.actor</c> (private, publicized;
    /// UIUseItemsBar.cs:40), written by every entry point that raises the bar
    /// (<c>ShowItems</c> :372, <c>ShowUsableItems</c> :431, and <c>TakeDamagePanel.Show</c>'s
    /// OnAttacked repopulation, which is why <c>CardsGameApi.TakeDamagePlaceContext</c> already
    /// compares against it). THIS is "the character whose item slot raised the element
    /// picker".</summary>
    private static void ItemsOwners(List<CPlayerActor> into)
    {
        UIUseItemsBar? bar = Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null;
        if (bar != null)
            AddOwner(into, bar.actor);
    }

    /// <summary>The abilities bar's actor — <c>UIUseAbilitiesBar.actor</c> (CPlayerActor,
    /// UIUseAbilitiesBar.cs:172), written by all four raise paths: <c>ShowInfuseAbilities</c>
    /// :272, <c>ShowInfusionsAction</c> :295 (the end-of-ability "Any" infusion that BLOCKS the
    /// turn), <c>ShowGenericInfusion</c> :310 and <c>ShowChooseAbility</c> :330.</summary>
    private static void AbilitiesOwners(List<CPlayerActor> into)
    {
        UIUseAbilitiesBar? bar = Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance : null;
        if (bar != null)
            AddOwner(into, bar.actor);
    }

    /// <summary>The augment bar's actor — <c>UIUseAugmentationsBar.actor</c>
    /// (UIUseAugmentationsBar.cs:27, written by <c>Show</c> :252 and handed to every slot's
    /// <c>Init</c> :45/:62, so the slots and this agree by construction).</summary>
    private static void AugmentsOwners(List<CPlayerActor> into)
    {
        UIUseAugmentationsBar? bar = Singleton<UIUseAugmentationsBar>.IsInitialized
            ? Singleton<UIUseAugmentationsBar>.Instance : null;
        if (bar != null)
            AddOwner(into, bar.actor);
    }

    /// <summary>
    /// The active-bonus bar's actors — <c>UIActiveBonusBar.actors</c> (a LIST,
    /// UIActiveBonusBar.cs:25, written by <c>Init</c> :236; the single-actor
    /// <c>ShowActiveBonus</c> overload :248 wraps one actor in a one-element list :250). This is
    /// the one bar the game can legitimately raise for SEVERAL characters at once, so ownership
    /// here is a SET and the rule generalizes without a special case: the bar is shown while the
    /// focused character is one of them.
    /// </summary>
    private static void ActiveBonusOwners(List<CPlayerActor> into)
    {
        UIActiveBonusBar? bar = Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance : null;
        List<CActor>? actors = bar != null ? bar.actors : null;
        if (actors == null)
            return;
        for (int i = 0; i < actors.Count; i++)
            AddOwner(into, actors[i]);
    }

    // ---- MULTIPLAYER READ SEAM (wire record 25 — NetProtocol.ExtIdUseBars) ------------------

    /// <summary>
    /// Which bars are DOCKED AND VISIBLE on this board right now, as the record-25 mask
    /// (<c>NetProtocol.UseBarActiveBonusBit</c> … <c>UseBarItemsBit</c>, in this surface's own
    /// stack order). 0 = nothing to publish, which is also what a peer predating the record
    /// renders (no second drawer at all).
    ///
    /// <para>A bar that is docked but RENDER-HIDDEN for another character's focus is deliberately
    /// NOT in this mask: since the 2026-08-08 ruling a remote board is a picture of ITS OWNER'S
    /// board, and the owner's board shows nothing at that lane while the hide holds — the same
    /// rule <c>DecisionDockSurface.WireButtonLines</c> follows. The hide predicate is not
    /// re-derived here; this reads <c>BarDock.FocusHidden</c>, i.e. the flag
    /// <see cref="UpdateFocusVisibility"/> set when it actually applied the hide.</para>
    /// </summary>
    internal static byte WireBarMask { get; private set; }

    /// <summary>Per-bar flags (element/option sub-picker open), indexed by BAR INDEX — the same
    /// 0..3 order as the mask bits. Meaningful only for bars present in
    /// <see cref="WireBarMask"/>.</summary>
    private static readonly byte[] WireBarFlagsBuffer = new byte[Net.NetProtocol.UseBarsCount];

    /// <summary>Per-bar visible slot counts, indexed by bar index (≤
    /// <c>NetProtocol.UseBarsMaxSlots</c>).</summary>
    private static readonly byte[] WireBarSlotCountBuffer = new byte[Net.NetProtocol.UseBarsCount];

    /// <summary>Per-slot state bytes, bar <c>b</c> occupying
    /// <c>[b * NetProtocol.UseBarsMaxSlots .. +count)</c>. Flat and fixed-size so the whole sample
    /// is allocation-free in the steady state.</summary>
    private static readonly byte[] WireSlotStateBuffer =
        new byte[Net.NetProtocol.UseBarsCount * Net.NetProtocol.UseBarsMaxSlots];

    /// <summary>
    /// Copy the published per-bar flags / slot counts / slot states into the caller's buffers (each
    /// may be shorter; nothing is written past its length). The three describe the bars named by
    /// <see cref="WireBarMask"/>, indexed by BAR INDEX, so the caller never has to know which bars
    /// were present to address them.
    /// </summary>
    internal static void CopyWireBars(byte[]? flags, byte[]? counts, byte[]? states)
    {
        Copy(WireBarFlagsBuffer, flags);
        Copy(WireBarSlotCountBuffer, counts);
        Copy(WireSlotStateBuffer, states);

        static void Copy(byte[] from, byte[]? into)
        {
            if (into == null)
                return;
            int n = from.Length < into.Length ? from.Length : into.Length;
            for (int i = 0; i < n; i++)
                into[i] = from[i];
        }
    }

    /// <summary>Next unscaled time the wire sample runs while bars are up (the shared content
    /// cadence the decision dock uses — the states move on human-paced clicks, not per frame).</summary>
    private float _nextWireAt;

    /// <summary>The mask/flags/counts/states last PUBLISHED — the change gate's memory, so a
    /// steady drawer costs one comparison and no log line.</summary>
    private static byte _publishedMask;
    private static readonly byte[] PublishedFlags = new byte[Net.NetProtocol.UseBarsCount];
    private static readonly byte[] PublishedCounts = new byte[Net.NetProtocol.UseBarsCount];
    private static readonly byte[] PublishedStates =
        new byte[Net.NetProtocol.UseBarsCount * Net.NetProtocol.UseBarsMaxSlots];

    /// <summary>The record-25 mask bit of bar index <paramref name="i"/> — the stack order this
    /// surface builds its docks in IS the bit order (see the constructor).</summary>
    private static byte BarBit(int i) => (byte)(1 << i);

    /// <summary>
    /// Sample the docked bars for the multiplayer wire (record 25): the mask of bars that are up
    /// AND visible, each bar's open sub-picker flags, and one state byte per visible slot.
    ///
    /// <para>WITHDRAWAL BYPASSES THE CADENCE. When nothing is up any more — the last bar released,
    /// or every bar went render-hidden for another character's focus — the empty mask is published
    /// on the very tick it becomes true, so a peer's drawer empties in the same frames the owner's
    /// does instead of up to a quarter second later. Only the non-empty sample is throttled.</para>
    ///
    /// <para>NO IDENTITY IS READ. The walk visits the bar's slot CONTAINER children (visual order,
    /// the same layout truth the fit hold uses) and asks three questions of each: can the owner
    /// click it, is it dimmed, is it toggled on. It never touches a slot's sprite, its model
    /// element or its tooltip — see <c>NetProtocol.ExtIdUseBars</c> for why an item slot's only
    /// "label" is its card art and therefore may not travel in any form.</para>
    ///
    /// <para>Never throws its way out of Tick: a half-torn bar degrades to "that bar is not
    /// published", which peers render as no bar at all.</para>
    /// </summary>
    private void SampleWire()
    {
        byte mask = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            if (_docks[i].Docked != null && !_docks[i].FocusHidden)
                mask |= BarBit(i);
        }

        if (mask == 0)
        {
            _nextWireAt = 0f; // withdraw NOW, not on the next cadence tick
            for (int i = 0; i < WireBarFlagsBuffer.Length; i++)
            {
                WireBarFlagsBuffer[i] = 0;
                WireBarSlotCountBuffer[i] = 0;
            }
            for (int i = 0; i < WireSlotStateBuffer.Length; i++)
                WireSlotStateBuffer[i] = 0;
            Publish(0);
            return;
        }

        if (Time.unscaledTime < _nextWireAt)
            return;
        _nextWireAt = Time.unscaledTime + 0.25f;

        for (int i = 0; i < _docks.Length; i++)
        {
            int at = i * Net.NetProtocol.UseBarsMaxSlots;
            byte flags = 0;
            int count = 0;
            if ((mask & BarBit(i)) != 0)
            {
                try
                {
                    count = _docks[i].SampleWireSlots(WireSlotStateBuffer, at, out flags);
                }
                catch (System.Exception e)
                {
                    // A bar that cannot be read is a bar peers do not get: dropping it from the
                    // mask is the honest (and safe) degradation, never a guessed row of tiles.
                    mask &= (byte)~BarBit(i);
                    count = 0;
                    flags = 0;
                    VRLog.Warn("WorldUI", $"USE BARS: wire sample of '{_docks[i].Name}' failed " +
                                          $"({e.Message}) — that bar is dropped from record 25 this " +
                                          "cadence; peers simply do not draw it.");
                }
            }
            // A BAR WITH NO ROWS IS NOT A BAR — DROP IT FROM THE MASK.
            //
            // ROOT CAUSE (found auditing the item use-bar removal, user report 2026-08-09: "Den
            // Button will ich hier also nicht sehen"). The mask above is set from "this bar is
            // DOCKED and not focus-hidden", but the rows a peer draws come from the SLOT WALK, and
            // the two can legitimately disagree: every slot in a docked bar may be suppressed by
            // the place-to-use split (<c>ItemsPile.EnforceChoiceSlotSplit</c>'s choice half
            // SetActive(false)s them; <see cref="EnforceItemsSplit"/>'s plain-item half disables their
            // Graphics — <see cref="BarDock.SampleWireSlots"/> skips both), while the DOCK itself only
            // releases on the next level-triggered tick — and during take-damage/surrender the two
            // halves stand down and re-arm on different ticks again. So "docked, zero visible
            // slots" is a reachable steady state, not a one-frame race.
            //
            // What that published on the peer: <c>RemoteBoardFurniture.SetUseBars</c> builds a row
            // for EVERY masked bar before it ever looks at the count — plate, MR-opacified backing
            // and caption first, then `new Material[n]` tiles — so n = 0 produced a full-width
            // empty caption plate hanging in the mirrored drawer under a decision row, describing a
            // bar the owner is not showing a single symbol of. That is precisely the "empty drawer /
            // stale plate on the peer's side" this pass had to rule out, and it is the mirror image
            // of the local rule the whole surface is built on ("empty bars never dock").
            //
            // Fixed on the SENDER, deliberately: record 25's format is untouched (no new field, no
            // new id), every peer — including one running an older receiver — simply stops being
            // told about a bar that has nothing in it, and the receiver's existing mask == 0 path
            // then hides the whole drawer exactly as it already does when no bar is docked at all.
            if (count == 0)
            {
                mask &= (byte)~BarBit(i);
                flags = 0;
            }
            WireBarFlagsBuffer[i] = flags;
            WireBarSlotCountBuffer[i] = (byte)count;
            for (int s = count; s < Net.NetProtocol.UseBarsMaxSlots; s++)
                WireSlotStateBuffer[at + s] = 0; // stale tail must never reach the wire
        }
        Publish(mask);
    }

    /// <summary>Publish (change-gated) what record 25 carries, and log the change once. Mask,
    /// flags, counts and states move together — they describe one drawer — so they share one gate
    /// and one line.</summary>
    private static void Publish(byte mask)
    {
        bool same = mask == _publishedMask;
        for (int i = 0; same && i < Net.NetProtocol.UseBarsCount; i++)
        {
            if (PublishedFlags[i] != WireBarFlagsBuffer[i] || PublishedCounts[i] != WireBarSlotCountBuffer[i])
                same = false;
        }
        for (int i = 0; same && i < WireSlotStateBuffer.Length; i++)
        {
            if (PublishedStates[i] != WireSlotStateBuffer[i])
                same = false;
        }
        if (same)
            return;

        _publishedMask = mask;
        WireBarMask = mask;
        for (int i = 0; i < Net.NetProtocol.UseBarsCount; i++)
        {
            PublishedFlags[i] = WireBarFlagsBuffer[i];
            PublishedCounts[i] = WireBarSlotCountBuffer[i];
        }
        for (int i = 0; i < WireSlotStateBuffer.Length; i++)
            PublishedStates[i] = WireSlotStateBuffer[i];

        if (mask == 0)
        {
            VRLog.Info("WorldUI", "USE BARS: wire drawer cleared (no bar docked and visible) — record " +
                                  "25 stops riding, so every peer's mirrored bar drawer empties too, " +
                                  "including when the bars are still docked but render-hidden because " +
                                  "the player is looking at another character.");
            return;
        }
        VRLog.Info("WorldUI", $"USE BARS: wire drawer published — mask 0x{mask:X2} " +
                              $"[{DescribeWire(mask)}] (record 25: which bars, how many slots, each " +
                              "slot offered/dimmed/chosen, sub-picker open. NO slot identity — the " +
                              "game's use slots carry no label at all, only card ART, which never " +
                              "rides this wire).");
    }

    /// <summary>Human-readable summary of the published drawer — built ONLY when a line is really
    /// emitted (the gate above compares bytes first), so the steady state allocates nothing.</summary>
    private static string DescribeWire(byte mask)
    {
        var sb = new System.Text.StringBuilder(96);
        for (int i = 0; i < Net.NetProtocol.UseBarsCount; i++)
        {
            if ((mask & BarBit(i)) == 0)
                continue;
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append(WireBarName(i)).Append(": ").Append(WireBarSlotCountBuffer[i]).Append(" slot(s)");
            byte f = WireBarFlagsBuffer[i];
            if ((f & Net.NetProtocol.UseBarElementPickerBit) != 0)
                sb.Append(" +ELEMENT PICKER OPEN");
            if ((f & Net.NetProtocol.UseBarOptionPickerBit) != 0)
                sb.Append(" +OPTION PICKER OPEN");
            int at = i * Net.NetProtocol.UseBarsMaxSlots;
            for (int s = 0; s < WireBarSlotCountBuffer[i]; s++)
            {
                byte st = WireSlotStateBuffer[at + s];
                sb.Append(" #").Append(s).Append('=')
                  .Append((st & Net.NetProtocol.UseSlotOfferedBit) != 0 ? "OFFERED" : "greyed");
                if ((st & Net.NetProtocol.UseSlotDimmedBit) != 0)
                    sb.Append("+dim");
                if ((st & Net.NetProtocol.UseSlotChosenBit) != 0)
                    sb.Append("+CHOSEN");
            }
        }
        return sb.ToString();
    }

    /// <summary>Bar name for the diagnostic line (the wire carries the BAR BIT, never a name).</summary>
    private static string WireBarName(int index) => index switch
    {
        0 => "activeBonus",
        1 => "abilities",
        2 => "augments",
        _ => "items",
    };

    // ---- per-bar "is this slot toggled on?" resolvers ---------------------------------------
    // Each bar instantiates ONE concrete slot type under its container (UIUseSlot<T>.IsSelected()
    // is inherited public), so the chosen state is read off the widget the owner clicked rather
    // than re-derived from the model. A child that is not that type is not a slot and contributes
    // nothing (null) — pooled decoration under the container can never become a mirrored tile.

    private static bool? ActiveBonusSlotChosen(Transform child)
    {
        var slot = child.GetComponent<UIUseActiveBonus>();
        return slot != null ? slot.IsSelected() : null;
    }

    private static bool? AbilitySlotChosen(Transform child)
    {
        var slot = child.GetComponent<UIUseAbility>();
        return slot != null ? slot.IsSelected() : null;
    }

    private static bool? AugmentSlotChosen(Transform child)
    {
        var slot = child.GetComponent<UIUseAugmentation>();
        return slot != null ? slot.IsSelected() : null;
    }

    private static bool? ItemSlotChosen(Transform child)
    {
        var slot = child.GetComponent<UIUseItemScenario>();
        return slot != null ? slot.IsSelected() : null;
    }

    /// <summary>Owner scratch — one resolve per dock per tick, reused (no steady-state garbage).</summary>
    private static readonly List<CPlayerActor> OwnerScratch = new(4);

    /// <summary>
    /// ONE CHARACTER OWNS A DECISION, bar edition (see the class doc). For every DOCKED bar:
    /// resolve the owners from the game's model and render-hide the bar unless one of them is the
    /// character THIS BOARD IS PRESENTING.
    ///
    /// ─── THE MULTIPLAYER HALF (user, hardware ModBuild 137) ───────────────────────────────────
    /// Verbatim: „Die Stiefel-Entscheidungen waren im Test nur bei einem Character zu tun, aber mein
    /// Mitspieler hat die die selbe Entscheidung bei einem anderen Character angezeigt, obwohl er
    /// der character sie nicht hat und diese Entscheidung auch nicht treffen muss. Warum wurde sie
    /// fälschlicherweise auch noch bei einem anderen Character angezeigt der das item gar nicht
    /// hatte?"
    ///
    /// <para>ROOT CAUSE. The boots prompt is not a decision-dock prompt at all — it is the ACTIVE-
    /// BONUS bar. <c>Choreographer.CheckForInitiativeAdjustments</c> calls
    /// <c>UIActiveBonusBar.ShowActiveBonus(…)</c> on EVERY client and gates only the ready BUTTON on
    /// <c>IsUnderMyControl</c>; the bar itself is not gated at all. So a teammate's client raises
    /// Cryonaris's bar as a local HUD singleton and this surface docked it on THAT machine's own
    /// board, which was presenting Hilde Die 2Te — both lines a second apart in his peer log:
    /// <c>USE BARS: 'UseBarActiveBonus' VISIBLE — owner 'Cryonaris' is the character in view</c> and
    /// <c>Board: CONFIRM/UNDO keycaps … owner 'Hilde Die 2Te' is in view</c>. The rule that is meant
    /// to stop exactly this compared the owners against <see cref="Board.CharacterFocus.Focused"/> —
    /// the EXPLICIT focus override, which is <b>null whenever the player simply follows the game</b>,
    /// i.e. almost always — so it never fired. The wire half shipped with the same report
    /// (<c>Net.NetAvatarDriver</c> withholds record 25 when every visible bar is foreign); this is
    /// the local half, and it is the one that decides what the OWNER of the machine sees.
    ///
    /// ─── THE PREDICATE, CLAUSE BY CLAUSE ──────────────────────────────────────────────────────
    /// <list type="bullet">
    /// <item><b>Attributable</b> — a non-empty owner set means the bar's attribution is KNOWN. An
    ///   empty one (raised for an enemy/object, or a half-torn bar whose resolver threw) FAILS OPEN
    ///   and stays visible: an unanswerable decision nobody can see is the worse failure, and it is
    ///   the failure this whole surface exists to prevent.</item>
    /// <item><b>Not the character in view</b> — <see cref="Board.CharacterFocus.PresentedActor"/>,
    ///   with the explicit override as fallback while the card pipeline has not resolved a hand yet.
    ///   PRESENTED, not FOCUSED, is the fix: it is the character the board is actually showing,
    ///   whether the player picked it or the game did, and since the selection floor landed
    ///   (CharacterFocus.LocalFloorHand, same round) "presenting nobody" is no longer a state a
    ///   local client can sit in. Null (spectator / whole party exhausted) disarms the OVERRIDE
    ///   clause — there is nothing to compare against — but not the foreign one, which does not need
    ///   a character in view to know that nobody here can answer.</item>
    /// <item><b>…AND one of two reasons to look away.</b> Either the player took an explicit focus
    ///   override (<c>Focused != null</c> — the 2026-08-08 ruling, unchanged: merely following the
    ///   game is never "looking elsewhere"), OR <b>every owner of the bar is a character under
    ///   ANOTHER player's control</b> (<see cref="Board.CharacterFocus.IsForeign"/>), in which case
    ///   nobody on this machine can answer it and it has no business being docked here. That second
    ///   clause is term for term the one <c>NetAvatarDriver.AllVisibleUseBarsAreForeign</c> applies
    ///   to the wire, which is what makes the two halves agree by construction instead of by
    ///   inspection.</item>
    /// </list>
    ///
    /// <para>WHAT MUST STILL SHOW, and does. (1) A bar for a character the local client controls,
    /// while the board presents a DIFFERENT one of his characters and he has taken no override:
    /// <c>IsForeign</c> is false, <c>Focused</c> is null ⇒ VISIBLE. The player must be able to
    /// answer his own decision without first clicking a portrait — that case is why the foreign
    /// clause is a disjunct and not a replacement. (2) The take-damage prompt: not a use bar at all
    /// (<see cref="DecisionDockSurface"/>), and its own owner is always local while the panel is
    /// open — the game routes a remote player's damage through <c>TakeDamagePanel.ShowOtherPlayer</c>,
    /// which ends in <c>myWindow.Hide(instant: true)</c> (TakeDamagePanel.cs:1133). (3) A bar the
    /// player is deliberately watching on a teammate (portrait-focused): the presented actor IS that
    /// teammate, so the owner test matches and it stays visible, read-only, exactly as the focus
    /// feature intends.</para>
    ///
    /// <para>SINGLE PLAYER IS UNCHANGED. <c>IsForeign</c> is <c>FFSNetwork.IsOnline &amp;&amp;
    /// !IsUnderMyControl</c> — false offline for every actor — so the new clause is inert and the
    /// surviving rule is the 2026-08-08 one. The only offline difference is a strict improvement:
    /// while a focus override is live but its hand widget is not built yet, the comparison now runs
    /// against the character the board is REALLY drawing instead of the one it is about to draw.</para>
    ///
    /// <para>REJECTED: (a) hiding whenever the owners do not include the presented actor, without
    /// the two-reason gate — that hides a local character's own live decision the moment the game
    /// points the board at somebody else, and the player has no way of knowing to go looking for it;
    /// (b) hiding on <c>IsForeign</c> alone, without the owner/presented test — that would blank the
    /// bar a player is deliberately watching on a focused teammate, which is the focus feature's
    /// whole point; (c) doing this on the wire only — the machine that raised the bogus bar would
    /// still show it to its own player, which is half of what he reported.</para>
    ///
    /// <para>Interaction with the requirement-C items split, verified: the split keys on hierarchy
    /// containment (<see cref="ItemsHostDock"/>) and on each plain slot's own Graphics, and this hide
    /// writes neither — so a hidden items bar keeps its dock, keeps its plain slots suppressed, and
    /// keeps its choice slots active. <see cref="ItemsPopulated"/> reads the same
    /// <c>activeSelf</c> flags, so <c>WantConverted</c> does not flip either: no release, no
    /// re-convert, and returning to the owner needs no second card placement.</para>
    /// </para></summary>
    private void UpdateFocusVisibility()
    {
        CPlayerActor? focused = Board.CharacterFocus.Focused;
        // THE CHARACTER THIS BOARD PRESENTS. The override is only the fallback: it is what the view
        // will become while ResolveHand's hand widget is still being built (CharacterFocus latches
        // PresentedActor from the RESOLVED hand, so it lags a focus click by at most one rebuild).
        CPlayerActor? inView = Board.CharacterFocus.PresentedActor ?? focused;
        for (int i = 0; i < _docks.Length; i++)
        {
            BarDock dock = _docks[i];
            if (dock.Docked == null)
                continue; // nothing converted — BarDock.Tick already restored any hide

            OwnerScratch.Clear();
            dock.ResolveOwners(OwnerScratch);
            bool owned = false;      // one of the owners IS the character on this board
            bool answerable = false; // …and at least one owner can be driven from this machine
            for (int o = 0; o < OwnerScratch.Count; o++)
            {
                CPlayerActor owner = OwnerScratch[o];
                owned |= ReferenceEquals(owner, inView);
                answerable |= !Board.CharacterFocus.IsForeign(owner);
            }
            // The override clause needs a character in view to compare against (null ⇒ fail open);
            // the foreign clause does not — a bar every one of whose owners belongs to another
            // player cannot be answered on this machine whatever the board happens to present,
            // which is exactly the term NetAvatarDriver.AllVisibleUseBarsAreForeign applies to the
            // wire. Keeping the two halves textually identical is what stops them drifting apart.
            bool lookingElsewhere = focused != null && inView != null; // the 2026-08-08 rule
            bool foreignOnly = OwnerScratch.Count > 0 && !answerable;  // MP only; inert offline
            bool hide = OwnerScratch.Count > 0 && !owned && (lookingElsewhere || foreignOnly);

            if (hide)
                dock.ApplyFocusHide(OwnerScratch, inView);
            else
                dock.NoteFocusVisible(OwnerScratch, inView);
            // Contribute to the cross-surface roll-up so ONE grep names every piece of the decision
            // display and what each switched off (DecisionDockSurface.PromptFocus).
            dock.ReportFocus();
            // BAR FOCUS bookkeeping: integers only, no string (see FlushBarFocus).
            _barFocusHash = _barFocusHash * 31 + BarFocusKey(i, OwnerScratch, hide, foreignOnly);
            _barFocusDocked++;
            OwnerScratch.Clear();
        }
        FlushBarFocus(inView, focused);
    }

    /// <summary>This tick's <c>BAR FOCUS</c> state, folded to one integer while the docks are
    /// walked — the whole point is that the steady state costs no string and no allocation, the
    /// same discipline <see cref="BarDock.FocusStateChanged"/> already follows.</summary>
    private int _barFocusHash;

    /// <summary>How many bars contributed to <see cref="_barFocusHash"/> this tick.</summary>
    private int _barFocusDocked;

    /// <summary>Change gate for the <c>BAR FOCUS</c> line — the last hash actually logged.</summary>
    private int _loggedBarFocusHash;

    /// <summary>One bar's contribution to the roll-up key. <c>CActor.ID</c> is the game's own actor
    /// identity, so this needs no string and no reference to survive the tick.</summary>
    private static int BarFocusKey(int index, List<CPlayerActor> owners, bool hide, bool foreignOnly)
    {
        int key = (index * 4) + (hide ? 2 : 0) + (foreignOnly ? 1 : 0);
        for (int o = 0; o < owners.Count; o++)
            key = key * 31 + owners[o].ID;
        return key;
    }

    /// <summary>
    /// THE ONE LINE that proves which character each docked bar was measured against — grep
    /// <c>BAR FOCUS</c>. It exists because the boots defect was invisible in the old logs: they
    /// stated the bar's owner and the (null) focus override, but never the character the board was
    /// actually presenting, so "owner 'Cryonaris' is the character in view" read as a verdict when
    /// it was only the absence of an override.
    ///
    /// <para>Change-gated on the folded integer key, so a steady state costs nothing at all and a
    /// flip costs exactly one line. The owners are re-resolved HERE, on the change tick only, which
    /// is what keeps the per-tick path free of strings (the same reason
    /// <see cref="DescribeOwners"/> is only ever called from an emitting branch).</para>
    /// </summary>
    private void FlushBarFocus(CPlayerActor? inView, CPlayerActor? focused)
    {
        int hash = _barFocusDocked == 0 ? 0 : _barFocusHash;
        hash = hash * 31 + (inView != null ? inView.ID : 0);
        hash = hash * 31 + (focused != null ? focused.ID : 0);
        int docked = _barFocusDocked;
        _barFocusHash = 0;
        _barFocusDocked = 0;
        if (docked == 0)
        {
            _loggedBarFocusHash = 0; // no bar docked — the next one announces itself
            return;
        }
        if (_loggedBarFocusHash == hash)
            return;
        _loggedBarFocusHash = hash;

        var sb = new System.Text.StringBuilder(200);
        for (int i = 0; i < _docks.Length; i++)
        {
            BarDock dock = _docks[i];
            if (dock.Docked == null)
                continue;
            OwnerScratch.Clear();
            dock.ResolveOwners(OwnerScratch);
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(dock.Name).Append("' owners '").Append(DescribeOwners(OwnerScratch))
              .Append("' ⇒ ").Append(dock.FocusHidden ? "HIDDEN" : "shown");
            if (!dock.FocusHidden && OwnerScratch.Count == 0)
                sb.Append(" (not attributable — fails open)");
            OwnerScratch.Clear();
        }

        VRLog.Info("WorldUI", $"BAR FOCUS: this board presents '{Board.CharacterFocus.Describe(inView)}' " +
                              "(explicit focus override: " +
                              (focused != null
                                  ? $"'{Board.CharacterFocus.Describe(focused)}'"
                                  : "none — following the game") + "). " + sb +
                              ". A bar is hidden only when it is attributable AND its owners do not " +
                              "include the presented character AND either the player took an explicit " +
                              "override or every owner is under ANOTHER player's control — the use " +
                              "bars are per-client HUD singletons the game raises on EVERY machine " +
                              "(Choreographer.CheckForInitiativeAdjustments → " +
                              "UIActiveBonusBar.ShowActiveBonus, which gates only the ready button), " +
                              "which is how a teammate's boots decision ended up docked on a board " +
                              "showing somebody else (user report 2026-08-13). PRESENTED, not focused: " +
                              "the old rule compared against the explicit override alone and therefore " +
                              "never fired while the player simply followed the game.");
    }

    /// <summary>Log-safe owner list — built ONLY when a line is actually emitted (the callers
    /// change-dedup on cheap instance ids first), so a per-tick resolve allocates nothing.</summary>
    private static string DescribeOwners(List<CPlayerActor> owners)
    {
        if (owners.Count == 0)
            return "nobody resolvable";
        if (owners.Count == 1)
            return Board.CharacterFocus.Describe(owners[0]);
        var sb = new System.Text.StringBuilder(48);
        for (int i = 0; i < owners.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(Board.CharacterFocus.Describe(owners[i]));
        }
        return sb.ToString();
    }

    // ---- requirement C: hide PLAIN item slots while the (mixed) items bar is docked ---------

    // The plain-use slots this surface hid while the items bar is docked (a mixed bar: choice
    // slots keep it docked, plain symbols must not appear). Tracked as (item, slot) pairs so
    // the restore only ever re-activates a slot that still belongs to that item in the bar's
    // live registry — never a slot the game itself has since hidden/pooled for other reasons.
    private readonly List<KeyValuePair<CItem, UIUseItemScenario>> _plainHidden = new(4);
    private int _lastPlainHiddenCount = -1;

    /// <summary>The Graphics this surface disabled to render-hide those slots, held by reference so
    /// the restore lands even after the game pooled the slot away — the same discipline
    /// <c>BarDock._focusHiddenCanvases</c> follows. Only components that were ENABLED at hide time
    /// are recorded, so a restore can never switch on something the game itself had off.</summary>
    private readonly List<Graphic> _plainHiddenGraphics = new(16);

    /// <summary>Reused walk buffer for <see cref="HidePlainSlotGraphics"/> — the split re-asserts
    /// every tick while a dock is up and must not allocate doing it.</summary>
    private static readonly List<Graphic> PlainGraphicScratch = new(16);

    /// <summary>
    /// Requirement C, mixed-bar case: while the items bar IS docked (because at least one
    /// visible slot carries a sub-choice), the PLAIN slots ride along in the converted subtree
    /// — their symbols would appear and stay clickable, violating "place the card is THE way".
    /// Suppress them (SetActive(false)) level-triggered every tick (the game's AddItem/
    /// RefreshItem may re-activate a slot at any time — e.g. an element unreserve re-adding an
    /// item, UIUseItemsBar.cs:59/[OnUnreservedElement]), and restore the exact pooled 2D state
    /// the moment the dock releases (or on shutdown / the manual rescue screen, which releases
    /// the conversion first). Purely a visibility split of WHICH slots dock; the bar's own
    /// logic, data and click seams are untouched — reversible by construction.
    /// </summary>
    private void EnforceItemsSplit()
    {
        UIUseItemsBar? bar = Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null;
        BarDock? host = ItemsHostDock(bar);
        ReportItemsHost(host, bar);

        if (host == null || bar == null)
        {
            RestorePlainHidden(bar);
            return;
        }

        int before = _plainHidden.Count;
        foreach (KeyValuePair<CItem, UIUseItemScenario> kv in bar.ItemSlots)
        {
            UIUseItemScenario slot = kv.Value;
            if (slot == null || !slot.gameObject.activeSelf)
                continue;
            if (CardsGameApi.SlotNeedsSubChoice(slot))
                continue; // choice slot — a DECISION; ItemsPile.EnforceChoiceSlotSplit owns that half
            bool known = false;
            for (int i = 0; i < _plainHidden.Count; i++)
                if (ReferenceEquals(_plainHidden[i].Value, slot))
                {
                    known = true;
                    break;
                }
            // Level-triggered, on the LIVE graphics rather than on the ledger: the game re-Shows
            // pooled slots and flips masks/highlights inside them at will (UIUseSlot.Refresh), so a
            // once-only pass would let a re-enabled Image back onto the board.
            int hid = HidePlainSlotGraphics(slot);
            if (!known)
            {
                _plainHidden.Add(kv);
                ReportPlainHidden(kv.Key, slot, host, hid);
            }
        }

        if (_plainHidden.Count != _lastPlainHiddenCount)
        {
            bool grew = _plainHidden.Count > before;
            _lastPlainHiddenCount = _plainHidden.Count;
            if (_plainHidden.Count > 0)
                VRLog.Info("WorldUI", $"USE BARS: items-bar SPLIT — {_plainHidden.Count} plain-use slot(s) " +
                                      $"RENDER-hidden inside the docked '{host.Name}' subtree (plain items are " +
                                      "activated by placing the card into the board's item slot; the slot object " +
                                      "stays ACTIVE so LiveItemsBarSlot — and with it the take-damage shield " +
                                      "placement and the fixed-element consume auto-resolve — keeps working). " +
                                      "Choice slots (element sub-picks) are untouched here.");
            if (grew)
                ReArmDockedFits();
        }
    }

    /// <summary>
    /// THE FALSIFIER, and the gate. Which converted dock — if any — actually DRAWS the items bar's
    /// slot container? A <see cref="ConvertedPanel"/> hosts everything under the root it converted
    /// and the content fit measures that whole subtree, so this is the literal question "are the
    /// item symbols on the board right now", asked of the hierarchy instead of inferred from which
    /// dock the surface believes is up. It can answer NO while the user is looking at an item
    /// symbol — in which case the model behind this pass is wrong and
    /// <see cref="ReportItemsHost"/>'s line says so in as many words, rather than the pass silently
    /// doing nothing (the ModBuild 242 shape: <c>items-bar SPLIT</c> read zero and nobody could tell
    /// "the predicate said no" from "the gate was never true").
    /// </summary>
    private BarDock? ItemsHostDock(UIUseItemsBar? bar)
    {
        RectTransform? container = bar != null ? ItemsContainer() : null;
        if (container == null)
            return null;
        for (int i = 0; i < _docks.Length; i++)
        {
            RectTransform? target = _docks[i].Docked?.Target;
            if (target != null && container.IsChildOf(target))
                return _docks[i];
        }
        return null;
    }

    /// <summary>Name of the dock last reported as hosting the item symbols (null = none), so the
    /// containment verdict costs one line per change and nothing per tick.</summary>
    private string? _loggedItemsHost;
    private bool _loggedItemsHostNone;

    /// <summary>Say — once per change — WHERE the item symbols are being drawn, or that nothing
    /// draws them. Both directions are logged: "no dock hosts them" is the falsifying answer and is
    /// worth exactly as much as the positive one.</summary>
    private void ReportItemsHost(BarDock? host, UIUseItemsBar? bar)
    {
        if (host == null)
        {
            if (bar == null)
            {
                // No bar singleton yet — there is nothing to be contained anywhere, and saying so
                // before the game has built the HUD would be noise, not evidence. The verdict is
                // deliberately NOT latched here, so the first line after the bar exists still prints.
                _loggedItemsHost = null;
                return;
            }
            if (_loggedItemsHostNone)
                return;
            _loggedItemsHost = null;
            _loggedItemsHostNone = true;
            VRLog.Info("WorldUI", "USE BARS: item-symbol containment — NO converted dock holds the items " +
                                  "bar's slot container, so no plain item symbol can be on the board and the " +
                                  "split stands down. If an item symbol IS visible while this line is the last " +
                                  "one, the containment model is wrong (the four bars were believed to share " +
                                  "one root object) and THAT is the bug, not the predicate.");
            return;
        }
        _loggedItemsHostNone = false;
        if (_loggedItemsHost == host.Name)
            return;
        _loggedItemsHost = host.Name;
        VRLog.Info("WorldUI", $"USE BARS: item-symbol containment — the items bar's slot container is INSIDE " +
                              $"the converted '{host.Name}' subtree, i.e. every active item slot is being drawn " +
                              "in the decision area whether or not the items dock itself is up. All four bar " +
                              "singletons hang off one game object, so this is the normal case, not an anomaly " +
                              "— it is how the Brille's symbol reached brille.jpg. The plain-use symbols are " +
                              "suppressed below; sub-choice slots are decisions and stay.");
    }

    /// <summary>
    /// Name what was suppressed and — the half a hide must never leave unsaid — whether the thing
    /// suppressed is still reachable through the VR item interaction. Reachability is read from the
    /// item fan itself (<c>ItemsPile.Current.Chips</c>: a chip exists for this CItem), because that
    /// chip IS the alternative route; no chip means this pass just removed the only affordance for
    /// an item the game is offering, which is a deadlock and is logged as a WARNING, loudly, rather
    /// than passing for a successful hide.
    /// </summary>
    private static void ReportPlainHidden(CItem item, UIUseItemScenario slot, BarDock host, int graphics)
    {
        string name = item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : "<unnamed item>";
        bool reachable = false;
        try
        {
            IReadOnlyList<ItemsPile.ItemChip>? chips = ItemsPile.Current?.Chips;
            for (int i = 0; chips != null && i < chips.Count && !reachable; i++)
            {
                ItemsPile.ItemChip chip = chips[i];
                reachable = chip != null && chip.Item != null && ReferenceEquals(chip.Item, item);
            }
        }
        catch (System.Exception)
        {
            reachable = false; // an unreadable fan is not evidence of reachability
        }

        if (reachable)
        {
            VRLog.Info("WorldUI", $"USE BARS: items-bar split hid the plain-use symbol of '{name}' " +
                                  $"({graphics} graphic(s) disabled) from the '{host.Name}' dock — that item has " +
                                  "a card in the local item fan, so the VR item interaction (lay the card in the " +
                                  "board's recess, poke USE) reaches it; the slot object stays active and the " +
                                  "confirm drives this very slot's own click.");
            return;
        }
        VRLog.Warn("WorldUI", $"USE BARS: items-bar split hid the plain-use symbol of '{name}' from the " +
                              $"'{host.Name}' dock, but NO card for it is in the local item fan " +
                              $"(ItemsPile.Current {(ItemsPile.Current == null ? "does not exist yet" : "holds no chip for this item")}). " +
                              "If the fan is merely not built yet this is harmless and self-corrects; if it " +
                              "persists, this hide removed the ONLY way to answer a live offer — which is worse " +
                              "than the symbol was, and the split must be narrowed rather than the report closed.");
    }

    /// <summary>
    /// Disable every enabled <see cref="Graphic"/> under <paramref name="slot"/> and record it, so
    /// the row draws nothing and catches no ray while its GameObject stays ACTIVE (see the class
    /// doc for why the active flag is load-bearing here). Returns how many were newly disabled —
    /// zero on the steady state, which is what makes the per-tick re-assertion cheap.
    /// </summary>
    private int HidePlainSlotGraphics(UIUseItemScenario slot)
    {
        PlainGraphicScratch.Clear();
        slot.GetComponentsInChildren(includeInactive: false, PlainGraphicScratch);
        int hid = 0;
        for (int i = 0; i < PlainGraphicScratch.Count; i++)
        {
            Graphic g = PlainGraphicScratch[i];
            if (g == null || !g.enabled)
                continue;
            g.enabled = false;
            _plainHiddenGraphics.Add(g);
            hid++;
        }
        PlainGraphicScratch.Clear();
        return hid;
    }

    /// <summary>Is <paramref name="child"/> a slot this surface render-hid? The wire sampler asks,
    /// because a render-hidden slot is still <c>activeSelf</c> and would otherwise be published to
    /// peers as a tile the owner is not drawing.</summary>
    internal bool IsPlainRenderHidden(Transform child)
    {
        for (int i = 0; i < _plainHidden.Count; i++)
        {
            UIUseItemScenario slot = _plainHidden[i].Value;
            if (slot != null && ReferenceEquals(slot.transform, child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Re-arm the content fit of every converted dock. The fit is frozen unless the OWNING bar's
    /// slot set changed (<c>TickFitStability</c>), and this suppression changes what a FOREIGN bar
    /// contributes to the host's visible-graphics union — so without this the augment dock would keep
    /// the 152 px width it measured while the item tile was still drawing and leave half a panel
    /// blank. Called only on a change, never per tick.
    /// </summary>
    private void ReArmDockedFits()
    {
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].ReArmFit();
    }

    /// <summary>
    /// Restore every slot this surface render-hid. The restore re-enables EXACTLY the Graphics it
    /// disabled, held by reference — it is not conditioned on the bar still mapping the item to the
    /// slot, and deliberately so: a component the mod switched off belongs switched back on wherever
    /// the game has since put that widget (a pooled slot is re-Shown for a different item and would
    /// otherwise come back permanently blank). Only components that were enabled at hide time are in
    /// the list, so this can never switch on something the game had off. The GameObject's active
    /// state was never written, so there is nothing there to undo.
    /// </summary>
    private void RestorePlainHidden(UIUseItemsBar? bar)
    {
        if (_plainHidden.Count == 0 && _plainHiddenGraphics.Count == 0)
        {
            _lastPlainHiddenCount = -1;
            return;
        }
        int restored = 0;
        for (int i = 0; i < _plainHiddenGraphics.Count; i++)
        {
            Graphic g = _plainHiddenGraphics[i];
            if (g == null || g.enabled)
                continue;
            g.enabled = true;
            restored++;
        }
        _plainHiddenGraphics.Clear();
        _plainHidden.Clear();
        _lastPlainHiddenCount = -1;
        ReArmDockedFits();
        VRLog.Info("WorldUI", $"USE BARS: items-bar split released — {restored} graphic(s) re-enabled on the " +
                              "plain-use slots (the bar is " +
                              (bar == null ? "gone" : bar.IsShown ? "still shown" : "hidden") +
                              "); no GameObject active state was ever written, so the bar is exactly as the game " +
                              "left it.");
    }

    // ---- the ITEM-BACKED ACTIVE BONUS rows leave the decision area (user ruling 2026-08-09) ----

    // The bonus rows this surface deactivated, held as (bonus, slot) pairs so the restore can only
    // ever re-activate a slot the bar still maps to that same bonus — never one the game itself has
    // since pooled. Exactly the ledger shape EnforceItemsSplit uses, for exactly its reason.
    private readonly List<KeyValuePair<CActiveBonus, UIUseActiveBonus>> _bonusHidden = new(4);

    /// <summary>Bonuses whose "why was this row kept" line has already been printed — one per
    /// offered bonus, not one per tick (see <see cref="EnforceActiveBonusSplit"/>). A HashSet on
    /// the bonus instance: the game pools the SLOT widgets but hands out a fresh bonus object per
    /// offer, so an entry here can never suppress the line for a genuinely new offer.</summary>
    private readonly HashSet<CActiveBonus> _placeableRefused = new();
    private readonly List<KeyValuePair<CActiveBonus, UIUseActiveBonus>> _bonusScratch = new(8);

    /// <summary>
    /// "So wie ich das verstehe kannst du damit alle Gegenstandsknöpfe entfernen und wirklich nur
    /// noch die Entscheidungen im Entscheidungsbereich belassen die als Konsequenz von
    /// Gegenstandsnutzung oder passiven Effekten auftritt" (user, 2026-08-09).
    ///
    /// <para>Since ModBuild 94 no ITEM is used by pressing a symbol; since this build no
    /// item-backed ACTIVE BONUS is either — the "Brille" class is answered by placing its card in
    /// the board's recess and poking USE (<c>ItemsPile</c>'s ACTIVE-BONUS placement, whose seam and
    /// full rationale live in <c>CardsGameApi</c>'s "ITEM-BACKED ACTIVE BONUSES" block). So the row
    /// has no reason to be drawn as a decision-area button, and it is deactivated here — the same
    /// mechanism <see cref="EnforceItemsSplit"/> and <c>ItemsPile.EnforceChoiceSlotSplit</c> use, and
    /// deliberately not a second one.</para>
    ///
    /// <para>WHAT SURVIVES, and this is the boundary the user drew: everything that carries a
    /// FURTHER OPTION (<c>CardsGameApi.BonusNeedsFurtherOption</c> — the initiative-boots ± picker,
    /// forgo-which-ability, choose-ability, the element consume), everything MANDATORY (the game
    /// refuses to continue without it, so an invisible row would be a deadlock — fail open), and
    /// every bonus with NO card to place at all: auras, character abilities and summons, which the
    /// game itself distinguishes in <c>ActiveBonus.GetIcon</c>'s fallbacks. Those keep their rows by
    /// necessity, not by exception.</para>
    ///
    /// <para>UNCONDITIONAL, not gated on the bar being docked — the <c>ItemsPile.EnforceChoiceSlotSplit</c>
    /// form rather than the <see cref="EnforceItemsSplit"/> one. The dock gate reads
    /// <c>activeSelf</c> (<see cref="ActiveBonusPopulated"/>), so suppressing only while docked
    /// would be circular: the bar would have to dock the row once to learn it should not.</para>
    ///
    /// <para>NO TAKE-DAMAGE / DEMAND STAND-DOWN, decided deliberately (the open question
    /// <see cref="EnforceItemsSplit"/> left behind — there, hiding a plain OnAttacked slot can make
    /// the shield placement fail silently, because that flow resolves slots through
    /// <c>CardsGameApi.LiveItemsBarSlot</c>, which requires an ACTIVE object). Nothing in this flow
    /// asks for an active bonus slot: the placement resolves through
    /// <c>UIActiveBonusBar.GetSlotForActiveBonus</c>, a plain dictionary lookup, and so do the two
    /// other mod readers of this bar (<c>TakeDamagePanelSafety.AutoUseMandatoryActiveBonuses</c>,
    /// <c>DamageTooltipSurface.MandatoryActiveBonusPending</c>) and the game's own MP replay
    /// (<c>ProxyUseActiveBonus</c>). A deactivated row therefore stays fully clickable BY CODE while
    /// being invisible to the player, which is exactly what this split needs and what the items-bar
    /// split could not have. The mandatory carve-out above additionally means the auto-use path
    /// never even meets a hidden row.</para>
    /// </summary>
    private void EnforceActiveBonusSplit()
    {
        CardsGameApi.ActiveBonusSlotsSnapshot(_bonusScratch);
        if (_bonusScratch.Count == 0)
        {
            RestoreBonusHidden();
            return;
        }

        for (int i = 0; i < _bonusScratch.Count; i++)
        {
            CActiveBonus bonus = _bonusScratch[i].Key;
            UIUseActiveBonus slot = _bonusScratch[i].Value;
            if (slot == null || !slot.gameObject.activeSelf)
                continue;
            if (!CardsGameApi.BonusIsPlaceable(bonus))
            {
                // WHY THIS ROW SURVIVED (user, ModBuild 103: the peer saw the Brille among the
                // symbols, and the mod's own log could not say why). The split is silent by design
                // when it hides a row, so a row that is NOT hidden left no trace at all — the two
                // hardware logs of the reporting session contain zero "bonus-bar SPLIT" lines, and
                // that is equally consistent with "the predicate said no" and with "the pass never
                // ran". Neither could be told from the other, which is why this is here.
                //
                // BonusIsPlaceable is a conjunction of four conditions and FAILS OPEN — any null
                // anywhere keeps the button. That is the right default (an unanswerable demand is
                // worse than a spare button) and it is also exactly how a MULTIPLAYER divergence
                // would present: a field the host has resolved and a client has not yet, on the
                // same bonus, in the same build. So the line names WHICH condition said no rather
                // than that one did, and both sides' logs can then be compared directly.
                //
                // Deduped on the bonus instance, so it costs one line per offered bonus.
                if (_placeableRefused.Add(bonus))
                {
                    bool baseIsItem = bonus.BaseCard is CItem;
                    bool needsOption = CardsGameApi.BonusNeedsFurtherOption(bonus);
                    bool hasData = bonus.Ability != null && bonus.Ability.ActiveBonusData != null;
                    VRLog.Info("WorldUI", "USE BARS: bonus-bar split KEPT the decision-area row for " +
                        $"'{CardsGameApi.BonusCardName(bonus)}' ({bonus.GetType().Name}) — " +
                        $"BaseCard is CItem: {baseIsItem} (false ⇒ aura/ability/summon, nothing to place); " +
                        $"needs a further option: {needsOption} (true ⇒ picker/consume, keeps its row); " +
                        $"Ability.ActiveBonusData present: {hasData}" +
                        (hasData ? $"; ToggleIsOptional: {bonus.Ability!.ActiveBonusData!.ToggleIsOptional} " +
                                   "(false ⇒ MANDATORY, kept deliberately — fail open)" : " (NULL ⇒ the " +
                                   "predicate fails open and the row is kept; on a CLIENT this is the " +
                                   "shape a host/client divergence takes, so compare this line against " +
                                   "the other machine's for the same bonus)") + ".");
                }
                continue; // options / mandatory / no card to place — it keeps its row
            }
            slot.gameObject.SetActive(false);
            bool known = false;
            for (int j = 0; j < _bonusHidden.Count; j++)
                if (ReferenceEquals(_bonusHidden[j].Value, slot))
                {
                    known = true;
                    break;
                }
            if (!known)
            {
                _bonusHidden.Add(_bonusScratch[i]);
                VRLog.Info("WorldUI", "USE BARS: bonus-bar SPLIT — hid the decision-area row for the " +
                                      $"item-backed active bonus '{CardsGameApi.BonusCardName(bonus)}' " +
                                      $"({bonus.GetType().Name}). It is answered by PLACING that item's card " +
                                      "in the board's recess and poking USE, which drives this very row's own " +
                                      "click; the row stays reachable by code (GetSlotForActiveBonus is a " +
                                      "dictionary lookup) and is only invisible.");
            }
        }
        _bonusScratch.Clear();

        // Prune rows the game has since withdrawn: once the bar stops mapping the bonus to this
        // slot the entry can never be legitimately restored, and keeping it would risk re-activating
        // a pooled widget if the bar ever handed the same object back for the same bonus.
        for (int i = _bonusHidden.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(CardsGameApi.ActiveBonusSlot(_bonusHidden[i].Key), _bonusHidden[i].Value))
                _bonusHidden.RemoveAt(i);
        }
    }

    /// <summary>Re-activate every bonus row this surface hid, but only where the bar still maps the
    /// same bonus to the same slot — otherwise the game has pooled it and re-activating would
    /// corrupt its pooling (the <see cref="RestorePlainHidden"/> rule, verbatim).</summary>
    private void RestoreBonusHidden()
    {
        if (_bonusHidden.Count == 0)
            return;
        for (int i = 0; i < _bonusHidden.Count; i++)
        {
            UIUseActiveBonus slot = _bonusHidden[i].Value;
            if (slot == null)
                continue;
            UIUseActiveBonus? live = CardsGameApi.ActiveBonusSlot(_bonusHidden[i].Key);
            if (ReferenceEquals(live, slot) && !slot.gameObject.activeSelf)
                slot.gameObject.SetActive(true);
        }
        _bonusHidden.Clear();
        VRLog.Info("WorldUI", "USE BARS: bonus-bar split released — hidden item-backed bonus rows restored to " +
                              "the bar's own state.");
    }

    /// <summary>
    /// Hierarchy-overlap guard: if (unexpectedly — the bars are authored as siblings) one
    /// bar's root were an ancestor/descendant of an already-converted bar, converting both
    /// would tear the first host's subtree apart. The nested bar then simply rides along
    /// inside the outer conversion instead of docking separately.
    /// </summary>
    private bool ConflictsWithDocked(BarDock self, RectTransform target)
    {
        for (int i = 0; i < _docks.Length; i++)
        {
            BarDock other = _docks[i];
            if (ReferenceEquals(other, self))
                continue;
            RectTransform? docked = other.Docked?.Target;
            if (docked == null)
                continue;
            if (target.IsChildOf(docked) || docked.IsChildOf(target))
            {
                other.WarnConflictOnce(self.Name);
                return true;
            }
        }
        return false;
    }

    // ---- placement: the second drawer below the board -------------------------------------

    /// <summary>
    /// Pose every docked bar host onto the <see cref="PlayTray.DecisionMount"/> zone,
    /// stacked top-to-bottom: stack top = the drawer zone's top edge, pushed below the
    /// decision row's worst-case extent while a decision row is actually docked (the two
    /// co-occur during take-damage and must not collide). Width-only dock fit at shared
    /// tray density: an opening element/option picker GROWS the host downward/outward at
    /// unchanged glyph scale instead of shrinking the whole bar (the FitWidthToMount
    /// lesson, inverted axis). Every transform is pose-follow only — hosts are never
    /// re-parented under the tray (mount seam reversibility contract).
    /// </summary>
    private void StackDocked()
    {
        // Count only the bars that are actually SHOWN: a bar render-hidden because its owner is
        // not in view must not consume a stack lane, or the visible ones would sit under a gap.
        // Its host keeps its last pose (invisible), and this method re-derives every lane from
        // the mount + fitted rect each tick, so a returning bar takes its place the same frame
        // it is shown — nothing has to be remembered across the hide.
        int docked = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            if (_docks[i].Docked != null && !_docks[i].FocusHidden)
                docked++;
        }
        if (docked == 0)
        {
            _floatPlaced = false;
            _floatPlacedCount = -1;
            _rowBottomValid = false; // no stale latch across a later re-dock
            return;
        }

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            for (int i = 0; i < _docks.Length; i++)
            {
                ConvertedPanel? p = _docks[i].Docked;
                if (p != null && !_docks[i].FocusHidden)
                    p.OrderCluster = null; // floating stack — not part of the board's draw cluster
            }
            PlaceFloatingStack(docked); // a pending decision must never be invisible
            return;
        }
        _floatPlaced = false;

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        Vector3 up = mount.up;
        Vector3 toViewer = -mount.forward; // DecisionMount convention: -Z is proud of the board lip

        // Stack top: while a prompt row is docked, hang the stack a small clearance below
        // the row's MEASURED bottom edge (DecisionDockSurface.RowBottomUpMeters — the
        // abstand.png fix: the worst-case ±MaxHeight/2 shift left a huge dead gap that
        // visually severed the bonus slots from the burn choices); the worst-case shift
        // remains only as the fallback until the row has measured. The live edge is
        // deadband-latched: the row's widgets hover-scale a few px and the stack must not
        // breathe with them (same jump family as the fit hold, see the class doc). No
        // feedback loop: the row measures only its own subtree, never the bar hosts.
        float cursor;
        string ceilNote = "hung below the docked decision row";
        bool atCeiling = false;
        if (DecisionDockSurface.RowDocked)
        {
            float? rowBottom = DecisionDockSurface.RowBottomUpMeters;
            if (rowBottom.HasValue)
            {
                float deadband = RowBottomDeadbandPx / (PlayTray.TrayPixelsPerMeter * DensityScale) * trayScale;
                if (!_rowBottomValid || Mathf.Abs(rowBottom.Value - _rowBottomLatched) > deadband)
                {
                    _rowBottomLatched = rowBottom.Value;
                    _rowBottomValid = true;
                }
                cursor = _rowBottomLatched - DecisionClearance * trayScale;
            }
            else
            {
                _rowBottomValid = false;
                cursor = -(PlayTray.DecisionMountMaxHeight * 0.5f + DecisionClearance) * trayScale;
            }
        }
        else
        {
            // NO DECISION ROW ⇒ THE BARS *ARE* THE TOP OF THE DECISION AREA, so they take its
            // CEILING (ModBuild 91). That is still the drawer zone's own top edge — the height the
            // bars have always started at here — but it is now read from the ONE place all three
            // decision surfaces read it (DecisionDockSurface.AreaCeilingUp), so the prompt text and
            // the widget row start at exactly the same height when they exist. This is the case the
            // user tuned: the initiative-boots ± bar is use bars ALONE, and its top is the topmost
            // pixel of the whole display for that prompt.
            _rowBottomValid = false;
            cursor = DecisionDockSurface.AreaCeilingUp(mount, up, trayScale, out ceilNote);
            atCeiling = true;
        }

        LogStackSeat(mount, up, trayScale, cursor, ceilNote, atCeiling, docked);

        int index = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            ConvertedPanel? panel = _docks[i].Docked;
            if (panel == null || _docks[i].FocusHidden)
                continue; // hidden for another character's focus — no lane, no cursor advance
            // Docked on the control board: the board's furniture stays structurally below the
            // bars at every viewing angle — see ConvertedPanel.OrderCluster.
            panel.OrderCluster = PlayTray.Current;
            if (!panel.HostGo.activeSelf)
                panel.HostGo.SetActive(true);

            Rect rect = panel.HostRect.rect; // content-fitted (unclamped union incl. open pickers)
            if (rect.width < 1f || rect.height < 1f)
                continue; // not fitted yet — next tick

            float fit = Mathf.Clamp(PlayTray.DecisionMountWidth * density / rect.width,
                MinFitScale, MaxFitScale);
            float scale = fit / density * trayScale; // world m per uGUI px

            float h = rect.height * scale;
            // THE LANE IS THE BAR'S OWN TOP EDGE, NOT ITS HOST'S (user, ModBuild 102: "Weiterhin
            // rutschen die Elemente immer direkt so beginn tiefer als es sein müsste … sie sollten
            // sich immer am oberen Rand orientieren"). The content fit leaves slack around the
            // visible union and CENTERS the union in the host it produces
            // (ConvertedPanel.FitContentPadding — 12 px per side here, unclipped because this dock
            // fits degenerate), so pinning the HOST's top edge at the cursor seated every visible
            // bar that far below it, and the error compounded down the stack. The pad is a fitted
            // constant, not a live measurement: the stack stays as immune to hover/press breathing
            // as the FitEnabled hold makes it.
            float pad = panel.FitContentPadding.y * scale;
            float visibleH = Mathf.Max(0f, h - 2f * pad);
            Transform host = panel.HostTransform;
            host.rotation = mount.rotation;
            host.localScale = Vector3.one * scale;
            host.position = mount.position
                            + up * (cursor + pad - h * 0.5f)
                            + toViewer * (ProudStep * (index + 1) * trayScale);
            cursor -= visibleH + StackGap * trayScale;
            index++;

            _docks[i].LogDockedRect(mount);
        }
    }

    /// <summary>
    /// ONE line, change-gated on the rounded millimetres, stating where this stack's TOP sits and
    /// what put it there — the decision area's CEILING when no prompt row is up (the
    /// initiative-boots case: the bars are then the topmost element of the whole decision display),
    /// or the docked row's measured bottom edge when one is. It shares the <c>DECISION DOCK SEAT:</c>
    /// prefix with the row's and the prompt text's lines ON PURPOSE: one grep over a hardware log
    /// then shows the ceiling reported by every prompt that came up, and they must all be equal
    /// (user, ModBuild 90: "der höchste Punkt bei den Initiativ-Schuhen [soll] auch der höchste
    /// Punkt [sein], an dem der Text angezeigt wird" — "the highest point of the initiative shoes
    /// [should] also be the highest point at which the text is shown").
    /// </summary>
    private void LogStackSeat(Transform mount, Vector3 up, float trayScale, float cursor,
                              string note, bool atCeiling, int docked)
    {
        float ceilingUp = DecisionDockSurface.AreaCeilingUp(mount, up, trayScale);
        string key = $"{ceilingUp * 1000f:F0}|{cursor * 1000f:F0}|{atCeiling}|{docked}";
        if (_loggedStackSeat == key)
            return;
        _loggedStackSeat = key;
        VRLog.Info("WorldUI", $"DECISION DOCK SEAT: the decision AREA's CEILING is " +
                              $"{ceilingUp * 1000f:F0} mm above the decision mount; the use-bar drawer " +
                              $"({docked} visible bar(s)) starts at {cursor * 1000f:F0} mm — " +
                              (atCeiling
                                  ? "AT the ceiling, because no prompt row is docked, so these bars ARE the " +
                                    "topmost element of the decision display (this is the initiative-boots " +
                                    "± case)"
                                  : note + ", i.e. below the prompt text and the buttons that sit between " +
                                    "them and the ceiling") +
                              ". The ceiling is prompt-independent: it must read the same here as in the " +
                              "row's and the prompt text's own DECISION DOCK SEAT lines.");
    }

    /// <summary>Change-dedup for <see cref="LogStackSeat"/> (never per frame — a settled stack logs once).</summary>
    private string? _loggedStackSeat;

    /// <summary>
    /// No usable mount (Cards module off, tray hidden/destroyed): float the whole stack
    /// HMD-anchored at reading distance (the ModalFallback pattern), placed once per
    /// docked-set change so it does not chase the head.
    /// </summary>
    private void PlaceFloatingStack(int dockedCount)
    {
        if (_floatPlaced && _floatPlacedCount == dockedCount)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float worldScale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        Vector3 basePos = h.position + fwd * (FloatDistanceMeters * worldScale);
        Vector3 down = rot * Vector3.down;

        int index = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            ConvertedPanel? panel = _docks[i].Docked;
            if (panel == null || _docks[i].FocusHidden)
                continue; // hidden for another character's focus — no lane in the float stack either
            if (!panel.HostGo.activeSelf)
                panel.HostGo.SetActive(true);
            CanvasConversion.PlaceHost(panel,
                basePos + down * (FloatStackStep * worldScale * index), rot,
                worldScale * FloatScaleFactor);
            index++;
        }
        _floatPlaced = true;
        _floatPlacedCount = dockedCount;
    }

    // ---- pending-decision hint over the existing pick-banner seam ---------------------------

    /// <summary>
    /// While a docked bar carries a decision the game is WAITING on, run a short hint on
    /// the board's pick banner so the player knows why the flow is not advancing:
    /// - any active, UNSELECTED abilities-bar slot (element infusion / choose ability —
    ///   the end-of-ability variant blocks the turn outright) → <c>bars_waiting_element</c>;
    /// - a pending MANDATORY active bonus (the game's own <c>isMandatoryChecker</c>, the
    ///   exact gate <c>CanTakeDamage</c>/the confirm refuses on) → <c>bars_waiting_bonus</c>.
    /// Pushed/cleared strictly on CHANGE: the banner seam is shared with the CardsDriver
    /// card-pick flows (mutually exclusive game phases), so this writer never tick-fights.
    ///
    /// <para>DELIBERATELY KEYED ON <c>Docked</c>, NOT ON VISIBILITY: the hint keeps running while
    /// a bar is render-hidden for another character's focus. That is the point — the banner is
    /// then the only thing telling the player why the flow is not advancing, i.e. the cue to look
    /// back at the character who owes the answer. Hiding the hint with the bar would turn a
    /// visible wait into a silent one, which is the failure mode this whole drawer exists to
    /// prevent.</para>
    /// </summary>
    private void UpdateWaitingHint()
    {
        string? hint = null;
        if (_docks[1].Docked != null && AnyAbilitySlotUnselected())
            hint = Loc.Mod("bars_waiting_element");
        else if (_docks[0].Docked != null && AnyMandatoryBonusPending())
            hint = Loc.Mod("bars_waiting_bonus");

        if (hint == _lastHint)
            return;
        _lastHint = hint;

        PlayTray? tray = PlayTray.Current;
        if (tray == null)
            return;
        if (hint != null)
        {
            tray.SetPickStatus(hint, null, null);
            _hintPushed = true;
            VRLog.Info("WorldUI", $"USE BARS: waiting hint shown — \"{hint}\".");
        }
        else if (_hintPushed)
        {
            _hintPushed = false;
            tray.SetPickStatus(null, null, null);
        }
    }

    private void ClearHint()
    {
        _lastHint = null;
        if (!_hintPushed)
            return;
        _hintPushed = false;
        PlayTray.Current?.SetPickStatus(null, null, null);
    }

    private static bool AnyAbilitySlotUnselected()
    {
        UIUseAbilitiesBar? bar = Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<string, UIUseAbility> kv in bar.slots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf && !kv.Value.IsSelected())
                return true;
        }
        return false;
    }

    private static bool AnyMandatoryBonusPending()
    {
        UIActiveBonusBar? bar = Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance : null;
        if (bar == null || bar.isMandatoryChecker == null)
            return false;
        foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> kv in bar.activeBonusSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf && !kv.Value.IsSelected()
                && bar.isMandatoryChecker(kv.Key))
                return true;
        }
        return false;
    }

    // ---- one bar's conversion lifecycle -----------------------------------------------------

    /// <summary>
    /// One bar's convert/release lifecycle on the standard <see cref="WorldSurface"/>
    /// machinery: level-triggered on the polled populated state, converted pokeable via
    /// <see cref="CanvasConversion"/> (poke + laser click the real widgets), released —
    /// restoring the exact 2D home — when the last slot hides. Placement is centralized
    /// in <see cref="UseBarsSurface.StackDocked"/> (the bars stack against each other, so
    /// no per-dock Place can know its own lane), hence the empty <see cref="Place"/>.
    /// </summary>
    private sealed class BarDock : WorldSurface
    {
        /// <summary>Post-dock window the fit stays live (show animation + late slot pop-in settle).</summary>
        private const float DockSettleSeconds = 1.5f;

        /// <summary>Fit window after a slot-set change (new slot must be measured into the host/laser plane).</summary>
        private const float SlotsSettleSeconds = 1.0f;

        /// <summary>
        /// Fit window after a picker CLOSES. It used to be 2.5 s because it had to OUTLAST the fit
        /// machinery's shrink damping (FitStableSeconds 0.5 + FitRefitMinIntervalSeconds 1.5) —
        /// which is exactly the delay the user reported ("Schließt man dieses 'Ausklappen' geht der
        /// button ca. 2 Sekunden verzögert wieder zu seiner Ursprungsposition. Das will ich
        /// sofort.", 2026-08-09). The damping is now off for this dock
        /// (<see cref="ConvertedPanel.FitShrinkImmediate"/>, set in <see cref="OnConverted"/>), so
        /// the window only has to cover the game's own picker-close animation — and every tick
        /// inside it forces a check (<see cref="TickFitStability"/>), so the panel follows the
        /// collapse frame by frame instead of stepping at the ~0.4 s periodic throttle.
        /// </summary>
        private const float PickerSettleSeconds = 0.5f;

        private readonly System.Func<RectTransform?> _root;
        private readonly System.Func<bool> _populated;
        private readonly System.Func<RectTransform?> _container;
        private readonly System.Action<List<CPlayerActor>> _owners;

        /// <summary>"Is this container child a slot, and is it toggled ON?" for THIS bar's concrete
        /// slot type — null when the child is not a slot at all. Feeds the record-25 sample only.</summary>
        private readonly System.Func<Transform, bool?> _chosen;

        private readonly UseBarsSurface _owner;
        private bool _conflictWarned;

        // One character owns a decision (see the class doc): canvases and renderers WE disabled to
        // render-hide this bar, held by reference so the restore lands even after the conversion
        // released. The RENDERER list is what killed the ModBuild 84 bug — the mixed-reality
        // backing plate is a MeshRenderer under the host, not a Canvas, so a canvas-only hide left
        // an empty dark rectangle floating where the bar had been.
        private readonly List<Canvas> _focusHiddenCanvases = new(4);
        private readonly List<Renderer> _focusHiddenRenderers = new(4);
        private int _loggedFocusHash;

        // Totals switched off by the CURRENT hide (summed over its re-asserting ticks) + whether
        // the MR plate was among them, for the log line the next hardware log is read against.
        private int _focusHiddenCanvasCount;
        private int _focusHiddenRendererCount;
        private bool _focusHiddenPlate;

        // Fit-stability hold state (see the FIT STABILITY class doc).
        private float _fitLiveUntil;
        private int _slotChildrenHash;
        private bool _pickerWasOpen;

        /// <summary>Unscaled time the every-tick forced fit check after a picker CLOSE ends — the
        /// "collapse immediately" window (see <see cref="PickerSettleSeconds"/>).</summary>
        private float _pickerClosingUntil;

        // Docked-rect log dedup (the TrayMountedPanelSurface diagnostic, simplified).
        private static readonly Vector3[] CornerScratch = new Vector3[4];
        private static readonly List<UIElementPicker> ElementPickerScratch = new(4);
        private static readonly List<UIOptionPicker> OptionPickerScratch = new(4);
        private int _loggedMountId;
        private Vector2 _loggedWorldSize;

        internal BarDock(string name, System.Func<RectTransform?> root,
            System.Func<bool> populated, System.Func<RectTransform?> container,
            System.Action<List<CPlayerActor>> owners, System.Func<Transform, bool?> chosen,
            UseBarsSurface owner)
        {
            Name = name;
            _root = root;
            _populated = populated;
            _container = container;
            _owners = owners;
            _chosen = chosen;
            _owner = owner;
        }

        /// <summary>
        /// Sample this bar for wire record 25: one state byte per VISIBLE slot into
        /// <paramref name="into"/> at <paramref name="at"/> (never past
        /// <c>NetProtocol.UseBarsMaxSlots</c> entries or the buffer), plus the bar's open-sub-picker
        /// <paramref name="flags"/>. Returns the slot count written.
        ///
        /// <para>The walk is the bar's slot CONTAINER children in hierarchy order — the same layout
        /// truth <see cref="TickFitStability"/> hashes, i.e. the order the owner sees — and the
        /// facts read per slot are exactly the ones the receiver paints: OFFERED (the game's
        /// <c>Selectable.IsInteractable</c> and no dim), DIMMED (the lowest <c>CanvasGroup</c> alpha
        /// between the slot and the bar root, which for these widgets is
        /// <c>UIUseSlot.SetInteractable</c> writing its serialized <c>disabledAlpha</c>), CHOSEN
        /// (<c>UIUseSlot.IsSelected()</c> through this bar's concrete slot type) and, since
        /// ModBuild 308, the owner's POINTER on a slot they can actually use — see
        /// <see cref="SampleSlotState"/> for why that last one is gated on OFFERED. A slot the mod
        /// itself suppressed is absent here too — the choice half by being inactive, the
        /// requirement-C plain-item half by <see cref="UseBarsSurface.IsPlainRenderHidden"/>, which
        /// the active flag no longer answers since that half became a render hide. The peer sees the
        /// same slots the owner does.</para>
        /// </summary>
        internal int SampleWireSlots(byte[] into, int at, out byte flags)
        {
            flags = 0;
            RectTransform? target = Panel?.Target;
            if (target != null)
            {
                if (AnyElementPickerOpen(target))
                    flags |= Net.NetProtocol.UseBarElementPickerBit;
                if (AnyOptionPickerOpen(target))
                    flags |= Net.NetProtocol.UseBarOptionPickerBit;
            }

            RectTransform? container = _container();
            if (container == null)
                return 0;

            int count = 0;
            for (int i = 0; i < container.childCount && count < Net.NetProtocol.UseBarsMaxSlots; i++)
            {
                Transform child = container.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;
                // …and a slot the plain-item split RENDER-hid is active but draws nothing. Before
                // that split became a render hide the active flag alone answered this; it no longer
                // does, and a tile the owner is not drawing must never appear on a peer's board.
                if (_owner.IsPlainRenderHidden(child))
                    continue;
                bool? chosen = _chosen(child);
                if (chosen == null)
                    continue; // not a slot widget — pooled decoration, never a mirrored tile
                if (at + count >= into.Length)
                    break;
                into[at + count] = SampleSlotState(child, container, chosen.Value);
                count++;
            }
            return count;
        }

        /// <summary>One slot's record-25 state byte. Identical axes (and identical bit positions) to
        /// <c>DecisionDockSurface.SampleOptionState</c>, so a receiver paints a bar tile and a
        /// decision plate through one code path; OFFERED additionally requires "not dimmed", because
        /// for these widgets the alpha IS the game's own interactable readout
        /// (<c>UIUseSlot.SetInteractable</c>) while the button's own <c>interactable</c> flag is
        /// never written.
        ///
        /// <para>Since ModBuild 308 that includes the two POINTER axes, sampled through the shared
        /// <see cref="DecisionDockSurface.SamplePointerBits"/> rather than a second copy of the
        /// hover/press table lookups. They were missed when ModBuild 300 gave the decision options
        /// theirs — the two samplers live in different files and only the bit POSITIONS were ever
        /// kept in step — so the owner's beam lit a use-bar slot on their own board and nowhere
        /// else while the decision row one drawer above mirrored correctly. The user reported
        /// exactly that gap.</para></summary>
        private static byte SampleSlotState(Transform slot, Transform root, bool chosen)
        {
            float alpha = 1f;
            Transform? t = slot;
            while (t != null)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group != null && group.alpha < alpha)
                    alpha = group.alpha;
                if (ReferenceEquals(t, root))
                    break;
                t = t.parent;
            }
            bool dimmed = alpha < 0.999f;

            var sel = slot.GetComponentInChildren<Selectable>(includeInactive: false);
            bool offered = !dimmed && sel != null && sel.IsInteractable();

            byte state = 0;
            if (offered)
                state |= Net.NetProtocol.UseSlotOfferedBit;
            if (dimmed)
                state |= Net.NetProtocol.UseSlotDimmedBit;
            if (chosen)
                state |= Net.NetProtocol.UseSlotChosenBit;

            // The owner's pointer, asked through the ONE shared sampler instead of a second copy of
            // the tracker lookups. Two things about this line are load-bearing and neither of them
            // is visible in it, so both are written down.
            //
            // FIRST, the result is folded in RAW. SamplePointerBits answers in record 24's bits
            // (DecisionOptionHoveredBit / DecisionOptionPressedBit) and they land unchanged in a
            // record 25 byte, which is legal ONLY because the two bit sets share positions 3 and 4
            // by design — the alignment this record was born with on 2026-08-08 and which
            // NetProtocol restates at every one of these constants. That is not an accident to lean
            // on silently: if the positions ever diverge, this line goes on compiling and starts
            // writing the wrong axes, and the remedy then is a translation HERE, not a
            // re-numbering there.
            //
            // SECOND, the pointer bits hang off this method's own `offered` and not merely off a
            // non-null Selectable. SamplePointerBits does already refuse a non-interactable widget
            // — but for use slots that refusal can never fire, because the game greys a slot by
            // writing UIUseSlot.disabledAlpha and never touches the button's own `interactable`
            // flag, so sel.IsInteractable() stays TRUE on a slot the owner is looking at greyed.
            // That is the same asymmetry the summary above records as the reason OFFERED demands
            // "not dimmed" here, and its consequence is that SamplePointerBits cannot see the
            // greying at all and would report a hover for a dimmed slot. Publishing that would make
            // every peer paint a highlight the owner never sees, since uGUI gives the disabled tint
            // priority over highlighted and pressed alike. `offered` is the only predicate in this
            // file that knows about the alpha, so it is the one to gate on. (The null test beside
            // it looks redundant — `offered` already implies it — and it is kept because it is what
            // proves non-null to the nullable analysis, rather than suppressing the question with
            // a `!`; the lookup above returns null for a slot with no Selectable in it.)
            if (offered && sel != null)
                state |= DecisionDockSurface.SamplePointerBits(sel);

            return state;
        }

        /// <summary>Fill <paramref name="into"/> with the characters this bar was raised FOR
        /// (empty = not attributable — see <see cref="UseBarsSurface.UpdateFocusVisibility"/>).</summary>
        internal void ResolveOwners(List<CPlayerActor> into)
        {
            try
            {
                _owners(into);
            }
            catch (System.Exception)
            {
                // Attribution is a PRESENTATION question: a half-torn bar must never make a live
                // decision disappear. An empty list fails open (bar stays visible).
                into.Clear();
            }
        }

        public override string Name { get; }

        // Always on — user ruling 2026-08-11: essential (off left mid-scenario decisions,
        // incl. the end-of-ability infusion pick that never lets the turn end, reachable
        // only via the rescue chord).
        protected override bool ConfigEnabled => true;

        /// <summary>
        /// Docked only while the bar actually HAS visible slots (empty bars never dock).
        /// The manual A/X screen chord releases the conversion (the DecisionDock rule):
        /// the bar must be back in the 2D composite the rescue screen mirrors.
        /// </summary>
        protected override bool WantConverted =>
            base.WantConverted && !FlatScreen.ManualScreenActive && _populated();

        internal ConvertedPanel? Docked => Panel;

        public override void Tick()
        {
            base.Tick(); // convert / release (level-triggered)
            if (Panel == null)
                RestoreFocusHide("the bar released (its last slot hid)");
            TickFitStability();
        }

        public override void Shutdown()
        {
            RestoreFocusHide("the use-bars surface is shutting down"); // BEFORE the release
            base.Shutdown();
            _loggedFocusHash = 0;
        }

        // ---- one character owns a decision (user ruling 2026-08-08) ----------------------

        /// <summary>True while this bar is render-hidden because its owner is not the focused
        /// character. Read by the stack so a hidden bar consumes no lane.</summary>
        internal bool FocusHidden { get; private set; }

        /// <summary>Contribute this bar to the cross-surface focus roll-up — the single change-gated
        /// line that names EVERY piece of a decision display and the components each one switched
        /// off (see <see cref="DecisionDockSurface.PromptFocus"/>).</summary>
        internal void ReportFocus() =>
            DecisionDockSurface.PromptFocus.Report("UseBars/" + Name, FocusHidden,
                _focusHiddenCanvasCount, _focusHiddenRendererCount, _focusHiddenPlate);

        /// <summary>
        /// RENDER-HIDE this bar — and NOTHING ELSE. The only things written are
        /// <c>Canvas.enabled = false</c> and <c>Renderer.enabled = false</c> on the mod's own
        /// converted host subtree and on the panel's registered extra render roots. No game method
        /// is called and no GameObject the game owns is deactivated, which is the whole point here:
        /// these slots ARE <c>ExtendedButton</c>s, and <c>ExtendedButton.OnDisable</c> raises
        /// <c>ActiveChanged(false)</c>, un-highlights and can clear the EventSystem selection +
        /// invoke <c>onDeselected</c> (ExtendedButton.cs:300-320) — a mid-choice element picker
        /// must not be poked like that. Idempotent and re-asserted every tick, so a canvas the game
        /// adds under an opening picker is caught on the next frame.
        ///
        /// <para>WHY RENDERERS TOO (hardware report ModBuild 84, the empty MR rectangle): the
        /// mixed-reality backing plate <see cref="MrBacking"/> parents under the host rect is a
        /// <c>MeshRenderer</c>, not a Canvas, so the canvas-only hide left it drawing behind
        /// nothing. <see cref="CanvasConversion.ApplyOwnerRenderHide"/> switches every renderer off
        /// as well and sets <c>ConvertedPanel.OwnerRenderHidden</c>, which additionally makes the
        /// plate sweep refuse to BUILD one for a hidden panel — and this surface ticks earlier in
        /// the same Update than that sweep, so there is not even a one-frame plate.</para>
        /// </summary>
        internal void ApplyFocusHide(List<CPlayerActor> owners, CPlayerActor? focused)
        {
            ConvertedPanel? panel = Panel;
            if (panel == null)
                return;
            if (!FocusHidden)
            {
                _focusHiddenCanvasCount = 0;
                _focusHiddenRendererCount = 0;
                _focusHiddenPlate = false;
            }
            FocusHidden = true;

            int before = _focusHiddenRenderers.Count;
            CanvasConversion.ApplyOwnerRenderHide(panel, _focusHiddenCanvases, _focusHiddenRenderers,
                out int canvases, out int renderers);
            _focusHiddenCanvasCount += canvases;
            _focusHiddenRendererCount += renderers;
            // Diagnostic only (MrBacking.PlateObjectName): the hide is name-blind, the LOG is not.
            for (int i = before; i < _focusHiddenRenderers.Count && !_focusHiddenPlate; i++)
            {
                Renderer r = _focusHiddenRenderers[i];
                if (r != null && r.gameObject.name == MrBacking.PlateObjectName)
                    _focusHiddenPlate = true;
            }

            if (!FocusStateChanged(hidden: true, owners, focused))
                return;
            string ownerNote = DescribeOwners(owners);
            VRLog.Info("WorldUI", $"USE BARS: '{Name}' belongs to '{ownerNote}' and the player is " +
                                  $"looking at '{Board.CharacterFocus.Describe(focused)}' — the bar is " +
                                  $"RENDER-HIDDEN ({_focusHiddenCanvasCount} canvas(es) and " +
                                  $"{_focusHiddenRendererCount} renderer(s) disabled on the mod-owned host " +
                                  "subtree + extra render roots, MR backing plate " +
                                  $"{(_focusHiddenPlate ? "INCLUDED (the empty dark rectangle is gone)" : "not present (no plate on this panel yet)")}) " +
                                  "and takes no stack " +
                                  "lane. The decision itself is untouched: no slot was deactivated, an " +
                                  "open element/option picker keeps its state, the items split still " +
                                  "holds, and it reappears unchanged the moment the owner is focused " +
                                  "again — no second card placement needed.");
        }

        /// <summary>The bar is (or becomes) visible: lift any hide and log the transition once.</summary>
        internal void NoteFocusVisible(List<CPlayerActor> owners, CPlayerActor? focused)
        {
            RestoreFocusHide(null);
            if (!FocusStateChanged(hidden: false, owners, focused))
                return;
            VRLog.Info("WorldUI", $"USE BARS: '{Name}' VISIBLE — " +
                                  (owners.Count == 0
                                      ? "the bar is not attributable to a character, so it is shown to " +
                                        "whoever is looking (an unanswerable decision is the worse failure)."
                                      : $"owner '{DescribeOwners(owners)}' is the character in view" +
                                        (focused == null ? " (no focus override — following the game)." : ".")));
        }

        /// <summary>
        /// Change-dedup on (hidden, owner set, focused) using instance ids only — no string is
        /// built unless the state genuinely moved, so the per-tick resolve stays allocation-free.
        /// </summary>
        private bool FocusStateChanged(bool hidden, List<CPlayerActor> owners, CPlayerActor? focused)
        {
            // CActor.ID is the game's own actor identity (the id its own network paths send —
            // UIUseItemsBar.cs:198/215), so this key is stable and needs no string.
            int hash = hidden ? 1 : 2;
            for (int i = 0; i < owners.Count; i++)
                hash = hash * 31 + owners[i].ID;
            hash = hash * 31 + (focused != null ? focused.ID : 0);
            // The hidden component tally is part of the key: the hide is re-asserted every tick, so
            // a canvas/renderer that only appears LATER (an opening picker's canvas, an MR plate
            // built a frame after the bar docked) genuinely changes what is hidden and earns one
            // more line. Both sets are finite, so this can never turn into a per-frame log.
            hash = hash * 31 + _focusHiddenCanvasCount;
            hash = hash * 31 + _focusHiddenRendererCount;
            if (_loggedFocusHash == hash)
                return false;
            _loggedFocusHash = hash;
            return true;
        }

        /// <summary>Undo <see cref="ApplyFocusHide"/>: re-enable exactly the canvases AND renderers
        /// it disabled and clear <c>ConvertedPanel.OwnerRenderHidden</c>. Idempotent, and safe after
        /// the conversion was released — the components are held by reference and belong enabled
        /// wherever they now live (their restored 2D home has them enabled too).</summary>
        internal void RestoreFocusHide(string? reason)
        {
            if (_focusHiddenCanvases.Count == 0 && _focusHiddenRenderers.Count == 0 && !FocusHidden)
                return;
            CanvasConversion.LiftOwnerRenderHide(Panel, _focusHiddenCanvases, _focusHiddenRenderers);
            bool was = FocusHidden;
            FocusHidden = false;
            int shownCanvases = _focusHiddenCanvasCount;
            int shownRenderers = _focusHiddenRendererCount;
            _focusHiddenCanvasCount = 0;
            _focusHiddenRendererCount = 0;
            _focusHiddenPlate = false;
            // Re-arm the fit briefly: it was frozen for the whole hidden period (see
            // TickFitStability), so give it a window to pick up anything that changed meanwhile.
            if (was)
                _fitLiveUntil = Mathf.Max(_fitLiveUntil, Time.unscaledTime + SlotsSettleSeconds);
            if (reason != null)
            {
                _loggedFocusHash = 0;
                VRLog.Info("WorldUI", $"USE BARS: '{Name}' focus hide lifted ({reason}) — all " +
                                      $"{shownCanvases} canvas(es) and {shownRenderers} renderer(s) the " +
                                      "mod disabled (the MR backing plate among them) are enabled again; " +
                                      "the bar was never touched.");
            }
        }

        /// <summary>
        /// FIT STABILITY (the hover/press jump fix, see the class doc): freeze the content
        /// fit (<see cref="ConvertedPanel.FitEnabled"/> = false — a pure surface-side hold,
        /// the shared machinery is untouched and Release restores everything as before)
        /// whenever the docked bar's LAYOUT is unchanged, so hover scale-ups and
        /// select-highlight toggles can no longer re-measure the union and move the panel.
        /// The fit runs live only while geometry can legitimately change:
        /// - until the first fit landed + a short post-dock settle (show animation),
        /// - for a window after the slot-set changed (the container's active children —
        ///   layout truth; a new slot must grow the host/laser plane or it would be
        ///   laser-dead),
        /// - while an element/option sub-picker is open (+ a close window long enough for
        ///   the damped shrink to hand the growth back). The fit check is FORCED the tick a
        ///   picker opens: this surface ticks before CanvasConversion.Tick, so the popup is
        ///   covered the same frame it appears.
        /// Held panels may keep a few px of transient size latched from the settle windows —
        /// cosmetic only; the panel simply stops moving.
        /// </summary>
        private void TickFitStability()
        {
            ConvertedPanel? panel = Panel;
            if (panel == null)
                return;

            // RENDER-HIDDEN for another character's focus: freeze the fit outright. The measure
            // rejects graphics whose CanvasRenderer is culled/disabled, and a hidden bar has no
            // business re-measuring anyway — freezing means the panel comes back at EXACTLY the
            // geometry it left with, and RestoreFocusHide re-arms a settle window so anything that
            // legitimately changed meanwhile is picked up on return.
            if (FocusHidden)
            {
                if (panel.FitEnabled)
                    panel.FitEnabled = false;
                return;
            }

            float now = Time.unscaledTime;

            // Layout truth: the active-children set of the bar's slot container. Hover
            // scaling animates transforms INSIDE the slots and never flips container
            // children, so this hash moves exactly when the slot set does.
            RectTransform? container = _container();
            int hash = 17;
            if (container != null)
            {
                for (int i = 0; i < container.childCount; i++)
                {
                    Transform child = container.GetChild(i);
                    if (child.gameObject.activeSelf)
                        hash = hash * 31 + child.GetInstanceID();
                }
            }
            if (hash != _slotChildrenHash)
            {
                _slotChildrenHash = hash;
                _fitLiveUntil = Mathf.Max(_fitLiveUntil, now + SlotsSettleSeconds);
            }

            bool pickerOpen = AnyPickerOpen(panel.Target);
            if (pickerOpen != _pickerWasOpen)
            {
                _pickerWasOpen = pickerOpen;
                if (pickerOpen)
                    panel.FitNextCheckFrame = 0; // skip the ~0.4 s periodic throttle: the popup
                                                 // is measured/covered the same frame it opens
                else
                {
                    // CLOSED: the collapse must land NOW, not at the next periodic check (user
                    // 2026-08-09: "Das will ich sofort"). The shrink damping is already off for
                    // this dock; _pickerClosingUntil additionally forces a check EVERY tick of the
                    // close window, so the host follows the popup's own close animation down and
                    // the stack re-seats against the shrunk rect in the same frames.
                    _pickerClosingUntil = now + PickerSettleSeconds;
                    _fitLiveUntil = Mathf.Max(_fitLiveUntil, _pickerClosingUntil);
                }
            }
            if (now < _pickerClosingUntil)
                panel.FitNextCheckFrame = 0;

            bool wantFit = !panel.FitMeasuredOnce || pickerOpen || now < _fitLiveUntil;
            if (panel.FitEnabled != wantFit)
            {
                panel.FitEnabled = wantFit;
                if (wantFit)
                    panel.FitNextCheckFrame = 0; // react THIS tick (CanvasConversion ticks after us)
            }
        }

        /// <summary>Any embedded element/option sub-picker open in the docked subtree (their
        /// popup content is the legitimate degenerate-fit growth case). Split into the two
        /// per-kind helpers below because wire record 25 reports them as SEPARATE bits — the
        /// owner's element pick and their option pick are different pictures.</summary>
        private static bool AnyPickerOpen(RectTransform? target) =>
            AnyElementPickerOpen(target) || AnyOptionPickerOpen(target);

        /// <summary>An <c>UIElementPicker</c> popup stands open in the docked subtree.</summary>
        private static bool AnyElementPickerOpen(RectTransform? target)
        {
            if (target == null)
                return false;
            bool open = false;
            ElementPickerScratch.Clear();
            target.GetComponentsInChildren(includeInactive: false, ElementPickerScratch);
            for (int i = 0; i < ElementPickerScratch.Count && !open; i++)
            {
                UIElementPicker p = ElementPickerScratch[i];
                open = p != null && p.IsOpen;
            }
            ElementPickerScratch.Clear();
            return open;
        }

        /// <summary>An <c>UIOptionPicker</c> popup stands open in the docked subtree.</summary>
        private static bool AnyOptionPickerOpen(RectTransform? target)
        {
            if (target == null)
                return false;
            bool open = false;
            OptionPickerScratch.Clear();
            target.GetComponentsInChildren(includeInactive: false, OptionPickerScratch);
            for (int i = 0; i < OptionPickerScratch.Count && !open; i++)
            {
                UIOptionPicker p = OptionPickerScratch[i];
                open = p != null && p.IsOpen;
            }
            OptionPickerScratch.Clear();
            return open;
        }

        protected override RectTransform? FindTarget()
        {
            RectTransform? target = _root();
            if (target == null)
                return null;
            if (_owner.ConflictsWithDocked(this, target))
                return null; // nested inside another docked bar — rides along there
            return target;
        }

        /// <summary>Give the frozen content fit a live window again (see
        /// <see cref="UseBarsSurface.ReArmDockedFits"/>): the layout-truth hold only watches THIS
        /// bar's own slot container, and a foreign bar's slots inside the same converted subtree can
        /// change the visible-graphics union without ever touching it.</summary>
        internal void ReArmFit()
        {
            if (Panel == null)
                return;
            _fitLiveUntil = Mathf.Max(_fitLiveUntil, Time.unscaledTime + SlotsSettleSeconds);
            Panel.FitNextCheckFrame = 0;
        }

        internal void WarnConflictOnce(string otherName)
        {
            if (_conflictWarned)
                return;
            _conflictWarned = true;
            VRLog.Warn("WorldUI", $"USE BARS: '{otherName}' bar root overlaps the already-docked " +
                                  $"'{Name}' subtree — it rides along inside that conversion " +
                                  "instead of docking separately.");
        }

        protected override void Place()
        {
            // Centralized in UseBarsSurface.StackDocked (runs after all docks ticked).
        }

        protected override void OnConverted()
        {
            if (Panel == null)
                return;
            // The pickers' popup content may open PAST the bar's authored strip rect; an
            // unclamped fit lets the visible-graphics union (and with it the poke/laser
            // plane) grow to cover it — see the class doc. The bar root is typically a
            // fullscreen stretch rect anyway, so the union is the only honest frame.
            Panel.FitFrameDegenerate = true;
            // COLLAPSING GIVES THE GROWTH BACK IN THE SAME FRAME (user ruling 2026-08-09: "sobald
            // es wieder eingeklappt wird soll es sofort reagieren"). Growth was always immediate;
            // the shared machinery damped the SHRINK by FitStableSeconds + FitRefitMinIntervalSeconds
            // — the reported "ca. 2 Sekunden verzögert" when the Flitzstiefel's option column
            // closed. This dock does not need that clock: TickFitStability already holds the fit
            // frozen unless the bar's LAYOUT TRUTH changed, so no hover/press transient can reach
            // the fit path at all (see ConvertedPanel.FitShrinkImmediate).
            Panel.FitShrinkImmediate = true;
            // Fresh dock: fit fully live through the settle window, then the stability
            // hold freezes it (TickFitStability). Hash 0 forces one slot-set snapshot on
            // the first tick (inside the settle window, so no extra fit churn).
            _fitLiveUntil = Time.unscaledTime + DockSettleSeconds;
            _slotChildrenHash = 0;
            _pickerWasOpen = false;
            _pickerClosingUntil = 0f;
            _loggedMountId = 0;
            VRLog.Info("WorldUI", $"USE BARS: '{Name}' docked on the board drawer — the game's real " +
                                  "use-slot widgets (incl. their embedded element/option sub-pickers) " +
                                  "are now poke/laser reachable; released to 2D when the bar empties.");
        }

        /// <summary>Log the docked world rect once per (re-)dock / material size change (Info: survives BepInEx's Debug drop).</summary>
        internal void LogDockedRect(Transform mount)
        {
            if (Panel == null)
                return;
            Panel.HostRect.GetWorldCorners(CornerScratch);
            float w = (CornerScratch[3] - CornerScratch[0]).magnitude;
            float h = (CornerScratch[1] - CornerScratch[0]).magnitude;
            int mountId = mount.GetInstanceID();
            if (mountId == _loggedMountId
                && Mathf.Abs(w - _loggedWorldSize.x) < _loggedWorldSize.x * 0.02f + 0.001f
                && Mathf.Abs(h - _loggedWorldSize.y) < _loggedWorldSize.y * 0.02f + 0.001f)
                return;
            _loggedMountId = mountId;
            _loggedWorldSize = new Vector2(w, h);
            Rect px = Panel.HostRect.rect;
            VRLog.Info("WorldUI", $"USE BARS: docked '{Panel.HostGo.name}' on '{mount.name}': " +
                                  $"world rect {w:F3}x{h:F3} m ({px.width:F0}x{px.height:F0} px).");
        }
    }
}
