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
/// burn choice) the bar stack shifts BELOW the decision row's worst-case extent, so the
/// two can never collide; otherwise the bars occupy the drawer zone itself. Shared tray
/// density (× <see cref="DensityScale"/>), width-only dock fit (heights grow downward —
/// an opening picker must grow the panel, not shrink its glyphs). No tray/mount →
/// HMD-anchored fallback float (the ModalFallback pattern: a decision must never be
/// invisible).
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

    /// <summary>Clearance between the decision row's worst-case bottom edge and the bar stack top (tray-local m).</summary>
    private const float DecisionClearance = 0.015f;

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

    internal UseBarsSurface()
    {
        // Fixed stack order, top to bottom: bonuses (turn-defining toggles first), the
        // ability/infusion pickers (the deadlock-critical answers), augments, items.
        _docks = new[]
        {
            new BarDock("UseBarActiveBonus", ActiveBonusRoot, ActiveBonusPopulated, this),
            new BarDock("UseBarAbilities", AbilitiesRoot, AbilitiesPopulated, this),
            new BarDock("UseBarAugments", AugmentsRoot, AugmentsPopulated, this),
            new BarDock("UseBarItems", ItemsRoot, ItemsPopulated, this),
        };
    }

    // ---- per-tick drive (registered in WorldUIModule, TickGuard-isolated) -----------------

    internal void Tick()
    {
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Tick(); // convert / release, level-triggered on the polled slot state

        StackDocked();
        UpdateWaitingHint();
    }

    internal void Shutdown()
    {
        for (int i = 0; i < _docks.Length; i++)
            _docks[i].Shutdown();
        ClearHint();
        _floatPlaced = false;
        _floatPlacedCount = -1;
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

    private static bool ItemsPopulated()
    {
        UIUseItemsBar? bar = Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance : null;
        if (bar == null)
            return false;
        foreach (KeyValuePair<CItem, UIUseItemScenario> kv in bar.ItemSlots)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf)
                return true;
        }
        return false;
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
        int docked = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            if (_docks[i].Docked != null)
                docked++;
        }
        if (docked == 0)
        {
            _floatPlaced = false;
            _floatPlacedCount = -1;
            return;
        }

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            PlaceFloatingStack(docked); // a pending decision must never be invisible
            return;
        }
        _floatPlaced = false;

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        Vector3 up = mount.up;
        Vector3 toViewer = -mount.forward; // DecisionMount convention: -Z is proud of the board lip

        // Stack top: below the decision row's worst-case bottom edge while a prompt row is
        // docked (mount ±MaxHeight/2, see the PlayTray.BuildMounts collision math), else the
        // drawer zone's own top edge.
        float cursor = DecisionDockSurface.RowDocked
            ? -(PlayTray.DecisionMountMaxHeight * 0.5f + DecisionClearance) * trayScale
            : PlayTray.DecisionMountMaxHeight * 0.5f * trayScale;

        int index = 0;
        for (int i = 0; i < _docks.Length; i++)
        {
            ConvertedPanel? panel = _docks[i].Docked;
            if (panel == null)
                continue;
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
            if (panel == null)
                continue;
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
        private readonly System.Func<RectTransform?> _root;
        private readonly System.Func<bool> _populated;
        private readonly UseBarsSurface _owner;
        private bool _conflictWarned;

        // Docked-rect log dedup (the TrayMountedPanelSurface diagnostic, simplified).
        private static readonly Vector3[] CornerScratch = new Vector3[4];
        private int _loggedMountId;
        private Vector2 _loggedWorldSize;

        internal BarDock(string name, System.Func<RectTransform?> root,
            System.Func<bool> populated, UseBarsSurface owner)
        {
            Name = name;
            _root = root;
            _populated = populated;
            _owner = owner;
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
