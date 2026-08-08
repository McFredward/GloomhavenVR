using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

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
/// and stays live briefly after it closes so the damped shrink can hand the panel back).
/// Poke presses never move the host physically (UguiPokeSurfaces writes no host
/// transforms) — the press jump was purely this fit path.
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
/// items split alone by construction: <see cref="ItemsPopulated"/> and
/// <see cref="EnforceItemsSplit"/> both key on slot <c>activeSelf</c>, which the hide never
/// writes — so a hidden bar neither releases its dock nor re-exposes a plain symbol, and
/// coming back needs no second placement. The fit is FROZEN while hidden
/// (<c>ConvertedPanel.FitEnabled</c> off, the existing stability hold) so the panel returns
/// at exactly the geometry it left with. Input is impossible meanwhile: both interactors
/// skip a canvas that is not <c>isActiveAndEnabled</c> (RayUguiDriver:132/312,
/// PokeInteractor:298).
///
/// MP: zero wire changes — every interaction is a real widget click on local-player UI;
/// peers see state via the game's own sync (proxy paths <c>ProxyUseActiveBonus</c>/
/// <c>ProxyToggleAugment</c>/<c>ProxyInfuseAbility</c> replay on remotes untouched).
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
    private const float DecisionClearance = 0.015f;

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
    private const float StackGap = 0.012f;

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
        _itemsDock = new BarDock("UseBarItems", ItemsRoot, ItemsPopulated, ItemsContainer, ItemsOwners, this);
        _docks = new[]
        {
            new BarDock("UseBarActiveBonus", ActiveBonusRoot, ActiveBonusPopulated, ActiveBonusContainer,
                ActiveBonusOwners, this),
            new BarDock("UseBarAbilities", AbilitiesRoot, AbilitiesPopulated, AbilitiesContainer,
                AbilitiesOwners, this),
            new BarDock("UseBarAugments", AugmentsRoot, AugmentsPopulated, AugmentsContainer,
                AugmentsOwners, this),
            _itemsDock,
        };
    }

    // ---- per-tick drive (registered in WorldUIModule, TickGuard-isolated) -----------------

    internal void Tick()
    {
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Tick(); // convert / release, level-triggered on the polled slot state

        EnforceItemsSplit();      // req C: plain item slots never show in a docked (mixed) items bar
        UpdateFocusVisibility();  // one character owns a decision — hide bars whose owner is not in view
        StackDocked();            // …so the stack closes up over a hidden bar in the SAME tick
        UpdateWaitingHint();
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
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Shutdown();
        ClearHint();
        _floatPlaced = false;
        _floatPlacedCount = -1;
        _rowBottomValid = false;
    }

    // ---- bar detection (polled — the bars raise no window events) -------------------------

    private static RectTransform? ActiveBonusRoot() =>
        Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance.transform as RectTransform : null;

    /// <summary>Any live active-bonus slot showing (pooled slots are SetActive(false) on remove).</summary>
    private static bool ActiveBonusPopulated()
    {
        UIActiveBonusBar? bar = Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> kv in bar.activeBonusSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf)
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
    /// all. Bonus/augment/ability bars are deliberately unaffected: their slots carry the
    /// choice UIs (initiative ±, forgo, infusion picks) the split keeps in the bars.
    /// A slot this surface itself suppressed (see <see cref="EnforceItemsSplit"/>) is
    /// inactive and naturally does not count.
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

    /// <summary>Owner scratch — one resolve per dock per tick, reused (no steady-state garbage).</summary>
    private static readonly List<CPlayerActor> OwnerScratch = new(4);

    /// <summary>
    /// ONE CHARACTER OWNS A DECISION, bar edition (see the class doc). For every DOCKED bar:
    /// resolve the owners from the game's model and render-hide the bar while the player has
    /// FOCUSED somebody who is not among them.
    ///
    /// <para>Both clauses are required, and each on purpose. <c>Focused</c> non-null means the
    /// player has taken an explicit focus override — merely following the game is never "looking
    /// elsewhere", so a player who never touches the feature can never lose sight of a bar. A
    /// non-empty owner set means the bar's attribution is KNOWN; an empty one (bar raised for an
    /// enemy/object, or not raised at all yet) fails open and stays visible.</para>
    ///
    /// <para>Interaction with the requirement-C items split, verified: the split keys on
    /// <c>_itemsDock.Docked != null</c> and on slot <c>activeSelf</c>, and this hide writes
    /// neither — so a hidden items bar keeps its dock, keeps its plain slots suppressed, and
    /// keeps its choice slots active. <see cref="ItemsPopulated"/> reads the same
    /// <c>activeSelf</c> flags, so <c>WantConverted</c> does not flip either: no release, no
    /// re-convert, and returning to the owner needs no second card placement.</para>
    /// </summary>
    private void UpdateFocusVisibility()
    {
        CPlayerActor? focused = Board.CharacterFocus.Focused;
        for (int i = 0; i < _docks.Length; i++)
        {
            BarDock dock = _docks[i];
            if (dock.Docked == null)
                continue; // nothing converted — BarDock.Tick already restored any hide

            OwnerScratch.Clear();
            dock.ResolveOwners(OwnerScratch);
            bool owned = false;
            for (int o = 0; o < OwnerScratch.Count && !owned; o++)
                owned = ReferenceEquals(OwnerScratch[o], focused);
            bool hide = focused != null && OwnerScratch.Count > 0 && !owned;

            if (hide)
                dock.ApplyFocusHide(OwnerScratch, focused);
            else
                dock.NoteFocusVisible(OwnerScratch, focused);
            OwnerScratch.Clear();
        }
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
        bool docked = _itemsDock.Docked != null;
        UIUseItemsBar? bar = Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null;

        if (!docked || bar == null)
        {
            RestorePlainHidden(bar);
            return;
        }

        foreach (KeyValuePair<CItem, UIUseItemScenario> kv in bar.ItemSlots)
        {
            UIUseItemScenario slot = kv.Value;
            if (slot == null || !slot.gameObject.activeSelf)
                continue;
            if (CardsGameApi.SlotNeedsSubChoice(slot))
                continue; // choice slot — the reason the bar is docked; keep it
            slot.gameObject.SetActive(false);
            bool known = false;
            for (int i = 0; i < _plainHidden.Count; i++)
                if (ReferenceEquals(_plainHidden[i].Value, slot))
                {
                    known = true;
                    break;
                }
            if (!known)
                _plainHidden.Add(kv);
        }

        if (_plainHidden.Count != _lastPlainHiddenCount)
        {
            _lastPlainHiddenCount = _plainHidden.Count;
            if (_plainHidden.Count > 0)
                VRLog.Info("WorldUI", $"USE BARS: items-bar SPLIT — {_plainHidden.Count} plain-use slot(s) hidden " +
                                      "from the docked bar (plain items activate by placing the card into the " +
                                      "board's item slot); choice slots (element sub-picks) remain docked.");
        }
    }

    /// <summary>Restore every slot this surface hid, but only where the bar still maps the same
    /// item to the same slot AND the bar is still shown — otherwise the game has already taken
    /// the slot back (hidden/pooled) and re-activating would corrupt its pooling.</summary>
    private void RestorePlainHidden(UIUseItemsBar? bar)
    {
        if (_plainHidden.Count == 0)
        {
            _lastPlainHiddenCount = -1;
            return;
        }
        for (int i = 0; i < _plainHidden.Count; i++)
        {
            UIUseItemScenario slot = _plainHidden[i].Value;
            CItem item = _plainHidden[i].Key;
            if (slot == null || bar == null || !bar.IsShown)
                continue;
            if (bar.ItemSlots.TryGetValue(item, out UIUseItemScenario live)
                && ReferenceEquals(live, slot) && !slot.gameObject.activeSelf)
                slot.gameObject.SetActive(true);
        }
        _plainHidden.Clear();
        _lastPlainHiddenCount = -1;
        VRLog.Info("WorldUI", "USE BARS: items-bar split released — hidden plain-use slots restored to the bar's own state.");
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
            _rowBottomValid = false;
            cursor = PlayTray.DecisionMountMaxHeight * 0.5f * trayScale;
        }

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
            Transform host = panel.HostTransform;
            host.rotation = mount.rotation;
            host.localScale = Vector3.one * scale;
            host.position = mount.position
                            + up * (cursor - h * 0.5f)
                            + toViewer * (ProudStep * (index + 1) * trayScale);
            cursor -= h + StackGap * trayScale;
            index++;

            _docks[i].LogDockedRect(mount);
        }
    }

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
        /// Fit window after a picker CLOSES — must exceed the fit machinery's shrink damping
        /// (FitStableSeconds 0.5 + FitRefitMinIntervalSeconds 1.5) so the panel actually
        /// hands its picker growth back before the hold re-freezes it.
        /// </summary>
        private const float PickerSettleSeconds = 2.5f;

        private readonly System.Func<RectTransform?> _root;
        private readonly System.Func<bool> _populated;
        private readonly System.Func<RectTransform?> _container;
        private readonly System.Action<List<CPlayerActor>> _owners;
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

        // Docked-rect log dedup (the TrayMountedPanelSurface diagnostic, simplified).
        private static readonly Vector3[] CornerScratch = new Vector3[4];
        private static readonly List<UIElementPicker> ElementPickerScratch = new(4);
        private static readonly List<UIOptionPicker> OptionPickerScratch = new(4);
        private int _loggedMountId;
        private Vector2 _loggedWorldSize;

        internal BarDock(string name, System.Func<RectTransform?> root,
            System.Func<bool> populated, System.Func<RectTransform?> container,
            System.Action<List<CPlayerActor>> owners, UseBarsSurface owner)
        {
            Name = name;
            _root = root;
            _populated = populated;
            _container = container;
            _owners = owners;
            _owner = owner;
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

        protected override bool ConfigEnabled => WorldUIConfig.UseBars.Value;

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
                    _fitLiveUntil = Mathf.Max(_fitLiveUntil, now + PickerSettleSeconds);
            }

            bool wantFit = !panel.FitMeasuredOnce || pickerOpen || now < _fitLiveUntil;
            if (panel.FitEnabled != wantFit)
            {
                panel.FitEnabled = wantFit;
                if (wantFit)
                    panel.FitNextCheckFrame = 0; // react THIS tick (CanvasConversion ticks after us)
            }
        }

        /// <summary>Any embedded element/option sub-picker open in the docked subtree (their
        /// popup content is the legitimate degenerate-fit growth case).</summary>
        private static bool AnyPickerOpen(RectTransform? target)
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
            if (open)
                return true;
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
            // Fresh dock: fit fully live through the settle window, then the stability
            // hold freezes it (TickFitStability). Hash 0 forces one slot-set snapshot on
            // the first tick (inside the settle window, so no extra fit churn).
            _fitLiveUntil = Time.unscaledTime + DockSettleSeconds;
            _slotChildrenHash = 0;
            _pickerWasOpen = false;
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
