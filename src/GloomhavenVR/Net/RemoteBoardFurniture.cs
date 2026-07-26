using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The INTERACTIVE FURNITURE of a peer's control board, reproduced on their remote board as
/// PURELY INERT VISUALS.
///
/// WHY THIS EXISTS AT ALL. An earlier pass rendered only the informational surfaces (objectives,
/// elements, round, initiative badge, rest badge, active cards, pile counters) and DELIBERATELY
/// left the furniture out, on the argument that "a button you cannot press is not information".
/// The user rejected that outright ("Das will ich NICHT, ALLES von den aufgeführten Elementen soll
/// dargestellt werden. Aber wichtig: Nichts davon soll man interagieren können auf dem fremden
/// Board, es ist eine reine Darstellung."). A peer's board must LOOK like a control board — the
/// keycaps, the gear, the pin toggle, the handle bar, the turn-flow cap, the item-use recess and
/// the decision drawer are what makes it read as one — while remaining completely untouchable.
///
/// NON-INTERACTIVE IS A HARD REQUIREMENT, and it is enforced three ways, not one:
///   1. CONSTRUCTION — every piece is built through <see cref="BoardVisual.Quad"/> (which destroys
///      the primitive's collider on creation) or is a bare <c>GameObject</c> + <c>TextMeshPro</c>.
///      Nothing here ever creates a <c>Collider</c>, a <c>Rigidbody</c> or a
///      <c>GrabbableBehaviour</c>.
///   2. REGISTRATION — this file never calls <c>PlayTray.RegisterLaserTarget</c>, never implements
///      <c>IPokeable</c>/<c>IGrabbable</c>, never touches <c>VRInteractables</c>,
///      <c>UguiPokeSurfaces</c> or <c>LaserTargets</c>. It has no click callbacks at all: the caps
///      are quads with a label, full stop. (Compare the LOCAL board, where every one of these
///      widgets is a <c>BoardButton</c>/<c>PhysicalButton</c> that registers a collider.)
///   3. BELT AND BRACES — <see cref="StripColliders"/> walks the finished hierarchy and destroys
///      anything that still carries a collider, logging a warning if it ever finds one. That
///      converts "I reviewed the code" into a runtime guarantee that survives future edits.
///
/// STATE FIDELITY. The remote board reproduces a widget's state wherever that state is knowable
/// from the SAME sources the rest of the remote board already uses — the host-replicated
/// <c>CPlayerActor</c> model and the already-synced VR extras (<see cref="RemoteAvatar"/>). No new
/// wire field was added and none was needed: the extras flag byte is exhausted (see the layout
/// contract in <see cref="PresenceSerializer"/>), and nothing here is worth spending the pile-browse
/// block's reserved bits on. Everything that is genuinely LOCAL-ONLY on the peer's client (their
/// own uGUI button interactability, their own VR preferences, their own hand hovering) is drawn in
/// a NEUTRAL / default look; each such case is called out on the member that draws it.
///
/// ANTI-CHEAT is unchanged: nothing here reads a card identity, and the two pieces that DO depend
/// on the peer's card state (the wanted-slot pulse and the half-card divider) derive strictly from
/// information the remote board already draws — see their notes.
///
/// COST. Built once, torn down with the board root, and refreshed on the shared
/// <see cref="RemoteBoardContent.RefreshSeconds"/> (4 Hz) cadence with change-gated writes, so a
/// four-peer table stays inside the existing budget. The only per-frame work is the single
/// <see cref="RemoteGlowPulse"/> component, and only while a pulse is actually visible.
/// </summary>
internal sealed class RemoteBoardFurniture
{
    // ---------------------------------------------------------------- layout (board-local) --
    // Every constant below is the LOCAL board's own authored BASE offset, copied from PlayTray so
    // a peer's furniture sits where that player's own furniture sits. The per-board debug-menu
    // offsets (CardsConfig.ConfirmUndoOffset / VRSettingsOffset / PinOffset / ClusterOffset /
    // ItemUseSlotOffset / DecisionOffset) are deliberately NOT applied: they are the LOCAL
    // player's tuning of their OWN board and say nothing about the peer's. Same reasoning the
    // existing remote surfaces already follow (RemoteObjectivesPanel / RemoteElementStrip hardcode
    // their mount bases too).

    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>PlayTray.ButtonZoneX — the right-hand control column.</summary>
    private const float ButtonZoneX = 0.235f;

    /// <summary>PlayTray "ContinueMount" (0.235, 0.045, −0.006) — the CONFIRM keycap / native
    /// Continue dock.</summary>
    private static readonly Vector3 ConfirmMount = new(ButtonZoneX, 0.045f, -0.006f);

    /// <summary>PlayTray "UndoDockMount" (0.235, −0.06, −0.006) — the UNDO keycap / native Undo dock.</summary>
    private static readonly Vector3 UndoMount = new(ButtonZoneX, -0.06f, -0.006f);

    /// <summary>PlayTray.GearBase (0.235, −0.125, −FixedProudZ) — the VR-settings gear.</summary>
    private static readonly Vector3 GearMount = new(ButtonZoneX, -0.125f, -0.005f);

    /// <summary>PlayTray.PinBase (BoardW/2 − 0.045, −BoardH/2 − 0.030, −FixedProudZ) — FOLLOW/PIN.</summary>
    private static readonly Vector3 PinMount = new(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.005f);

    /// <summary>PlayTray.BuildHandle's bar (0, −BoardH/2 − 0.030, +0.004) and its 0.55·BoardW × 24 mm
    /// brass bar. NOTE the local handle also carries a 62 %-wide trigger BoxCollider — the remote
    /// copy is the BAR ONLY, no zone, no <c>PanelGrabHandle</c>.</summary>
    private static readonly Vector3 HandleMount = new(0f, -BoardH * 0.5f - 0.030f, 0.004f);

    /// <summary>ButtonCluster's docked right-column anchor (ColumnCenterX/Y/RootZ).</summary>
    private static readonly Vector3 ClusterMount = new(0.148f, -0.124f, -0.006f);

    /// <summary>PlayTray.ItemUseSlotBase (ButtonZoneX, −BoardH/2 − 0.095, −0.020).</summary>
    private static readonly Vector3 ItemUseMount = new(ButtonZoneX, -BoardH * 0.5f - 0.095f, -0.020f);

    /// <summary>PlayTray.DecisionMountBase (0, −0.29, −0.020) — the shared decision drawer.</summary>
    private static readonly Vector3 DecisionMount = new(0f, -0.29f, -0.020f);

    /// <summary>PlayTray.BuildPickField's anchor (0, 0.015) — the modal pick drop field.</summary>
    private static readonly Vector3 PickFieldMount = new(0f, 0.015f, RemoteControlBoard.ProudZLocal - 0.001f);

    // ---- authored widget sizes (WorldUI.ButtonTuning defaults / PlayTray literals) -------------
    // The live ButtonTuning entries are the LOCAL player's own config; a peer's caps are drawn at
    // the AUTHORED defaults so every remote board looks the same regardless of local tuning.
    private const float BoardCapW = 0.073f;   // ButtonTuning.DefaultBoardWidth
    private const float BoardCapH = 0.073f;   // ButtonTuning.DefaultBoardHeight
    private const float GearCapW = 0.062f;    // ButtonTuning.DefaultGearWidth
    private const float PinCapW = 0.068f;     // ButtonTuning.DefaultPinWidth
    private const float DashCapH = 0.030f;    // ButtonTuning.DefaultDashHeight
    private const float TransientCapR = 0.042f; // ButtonTuning.DefaultRoundCapSize (cap RADIUS)

    /// <summary>Authored card size (CardsConfig CardWidth default 0.0635 and its fixed 88/63.5
    /// aspect) — the item-use recess is a card-sized recess.</summary>
    private const float CardW = 0.0635f;
    private const float CardH = CardW * (88f / 63.5f);

    // ---- palette (verbatim from the local widgets so the boards match) -------------------------
    private static readonly Color ConfirmColor = new(0.35f, 0.46f, 0.28f); // muted sage "go"
    private static readonly Color UndoColor = new(0.44f, 0.31f, 0.20f);    // worn leather
    private static readonly Color GearColor = new(0.37f, 0.36f, 0.38f);    // aged pewter
    private static readonly Color PinColor = new(0.58f, 0.46f, 0.26f);     // aged brass
    private static readonly Color SkipColor = new(0.37f, 0.44f, 0.56f);    // slate
    private static readonly Color HandleColor = new(0.62f, 0.50f, 0.28f);  // brass bar

    // ---------------------------------------------------------------- built pieces --

    private readonly Transform _root;

    private readonly InertCap _confirm;
    private readonly InertCap _undo;
    private readonly InertCap _use;
    private readonly InertCap _gear;
    private readonly InertCap _pin;
    private readonly InertCap _skip;

    private readonly Transform _itemUse;
    private readonly Material _itemUseGlowMat;
    private readonly Transform _decision;
    private readonly Transform _pickField;
    private readonly GameObject?[] _wanted = new GameObject?[2];
    private readonly GameObject?[] _snap = new GameObject?[2];
    private readonly GameObject?[] _halves = new GameObject?[2];

    // ---- change gates (a TMP/material write per tick is exactly the churn the 4 Hz cadence is
    //      there to avoid; every setter below no-ops until the value really moves) --------------
    private bool _shownArmed;
    private int _shownWantedMask = -1;
    private int _shownSnapMask = -1;
    /// <summary>Per-slot half-divider visibility as a bit mask (−1 = nothing written yet). A plain
    /// bool would miss the case where the dividers stay shown but the OCCUPANCY moves from one slot
    /// to the other.</summary>
    private int _shownHalfMask = -1;
    private bool _shownPickField;
    private string _langShown = string.Empty;

    /// <summary>Per-slot unscaled time the snap glow was lit (a card just landed there). Negative
    /// infinity = never.</summary>
    private readonly float[] _snapLitAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>How long a snap glow stays lit after a card lands in a slot. Matches the "the card
    /// will zap here" telegraph duration the local board's gold glow is visible for around a drop.</summary>
    private const float SnapGlowSeconds = 0.6f;

    /// <summary>Round-card occupancy at the previous refresh — the edge detector that lights the
    /// snap glow (see <see cref="Refresh"/>).</summary>
    private readonly bool[] _wasFilled = new bool[2];

    /// <summary>One-line state summary for the change-gated diagnostic in
    /// <see cref="RemoteControlBoard.LogContent"/> (grep: "Remote board furniture").</summary>
    public string StateLine { get; private set; } = string.Empty;

    // ---------------------------------------------------------------- construction --

    public RemoteBoardFurniture(Transform boardRoot)
    {
        _root = new GameObject("Furniture").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);

        // ---- right-hand control column: CONFIRM / [USE] / UNDO on their native dock mounts -----
        // The local board parks the mod CONFIRM keycap on "ContinueMount" and the mod UNDO keycap
        // on "UndoDockMount" — the very anchors the REAL Continue/Undo game widgets dock onto when
        // the WorldUI TrayControlDockSurface takes over. Whichever of the two is showing on the
        // peer's client, it occupies THIS spot, so one inert cap per mount is the faithful picture.
        _confirm = new InertCap(_root, "Confirm", ConfirmMount, new Vector2(BoardCapW, BoardCapH),
            ConfirmColor);
        _undo = new InertCap(_root, "Undo", UndoMount, new Vector2(BoardCapW, BoardCapH), UndoColor);

        // The item "USE" confirm is a DYNAMIC member of that same generic cluster (PlayTray
        // requirement 9a): while a usable item card is clipped into the use recess it joins as
        // member 1, i.e. the slot between CONFIRM (top) and UNDO (bottom). Its exact Y depends on
        // the LOCAL player's tuned [Cards] GenericButtonSpacing, which is theirs and not the
        // peer's — so it is drawn at the geometric midpoint of the two mounts, which is where a
        // sanely tuned three-stack puts it.
        _use = new InertCap(_root, "ItemUse", (ConfirmMount + UndoMount) * 0.5f,
            new Vector2(BoardCapW, BoardCapH), ConfirmColor);
        // Starts hidden and in step with the _shownArmed seed below: the local cluster only holds
        // this member while an item decision is pending, and Refresh() early-outs while nothing
        // changed — so a board that never sees an item fan must not be left showing a USE cap.
        _use.SetShown(false);

        _gear = new InertCap(_root, "SettingsGear", GearMount, new Vector2(GearCapW, DashCapH), GearColor);
        _pin = new InertCap(_root, "FollowToggle", PinMount, new Vector2(PinCapW, DashCapH), PinColor);

        // ---- grab-handle bar -------------------------------------------------------------------
        // The local handle is a brass Cube PLUS a 62 %-wide trigger BoxCollider and a
        // WorldUI.PanelGrabHandle that carries/rotates/resizes the board. Only the BAR is
        // reproduced: no collider, no grab zone, no handle component. A peer's board can never be
        // picked up — it follows the pose THEY broadcast and nothing else.
        var handle = new GameObject("HandleBar").transform;
        handle.SetParent(_root, worldPositionStays: false);
        handle.localPosition = HandleMount;
        BoardVisual.Quad(handle, "Bar", new Vector2(BoardW * 0.55f, 0.024f),
            BoardVisual.Unlit(HandleColor));
        // A darker strip along the lower edge stands in for the bar's shaded underside, so the flat
        // quad still reads as the round brass bar it copies.
        BoardVisual.Quad(handle, "BarShade", new Vector2(BoardW * 0.55f, 0.007f),
            BoardVisual.Unlit(new Color(HandleColor.r * 0.45f, HandleColor.g * 0.45f, HandleColor.b * 0.45f, 1f)))
            .transform.localPosition = new Vector3(0f, -0.0105f, -0.0005f);

        // ---- turn-flow ButtonCluster ----------------------------------------------------------
        // FIDELITY NOTE (this is why only ONE cap is drawn, not three): on a DOCKED board the
        // cluster's Ready and Undo twins are forced permanently OFF — ButtonCluster.Tick calls
        // MirrorReady(null, …) / MirrorUndo(null, …) precisely so the board never shows a duplicate
        // "Fortfahren"/Undo next to the right-hand pads. ONLY Skip is mirrored there. Drawing an
        // Undo|Ready|Skip trio here would therefore NOT match what the peer sees on their own
        // board; a single Skip cap at the column anchor does. The cluster auto-fits its cap size
        // from the live visible count, and at count 1 that is exactly the configured cap size.
        _skip = new InertCap(_root, "TurnFlowSkip", ClusterMount,
            new Vector2(TransientCapR * 2f, TransientCapR * 2f), SkipColor, plate: TransientCapR * 2.4f);

        // ---- item-USE clip-in recess ----------------------------------------------------------
        _itemUse = BuildItemUseRecess(out _itemUseGlowMat);

        // ---- shared decision drawer -----------------------------------------------------------
        _decision = BuildDecisionDrawer();

        // ---- modal pick drop field ------------------------------------------------------------
        _pickField = BuildPickField();

        // ---- slot overlays: wanted pulse, snap glow, half-poke divider ------------------------
        for (int i = 0; i < 2; i++)
        {
            Vector3 slot = RemoteControlBoard.SlotLocal(i);
            _wanted[i] = BuildSlotGlow($"WantedGlow{i}", slot, 1.30f, -0.003f,
                new Color(0.25f, 0.85f, 0.60f, 0.70f), pulse: true);
            _snap[i] = BuildSlotGlow($"SnapGlow{i}", slot, 1.18f, -0.005f,
                new Color(1f, 0.85f, 0.30f, 0.95f), pulse: false);
            _halves[i] = BuildHalfDivider($"HalfDivider{i}", slot);
        }

        ApplyLabels();
        StripColliders(_root.gameObject, "RemoteBoardFurniture");
    }

    // ---------------------------------------------------------------- refresh --

    /// <summary>
    /// Re-read everything knowable and repaint what changed. Called on the shared 4 Hz content
    /// cadence from <see cref="RemoteControlBoard"/>.
    ///
    /// <paramref name="showFronts"/> is the shared <see cref="RevealGate"/> answer for this actor;
    /// <paramref name="slot0"/>/<paramref name="slot1"/> say whether that peer's two round-card
    /// slots currently hold a card (the SAME occupancy the board already renders as a card back or
    /// a face — so nothing derived from it can leak anything the board does not already show).
    /// </summary>
    public void Refresh(CPlayerActor actor, RemoteAvatar owner, bool showFronts, bool slot0, bool slot1)
    {
        // A language switch invalidates every cached label (the local board self-heals the same way).
        string lang = Loc.CurrentLanguage;
        if (lang != _langShown)
        {
            _langShown = lang;
            ApplyLabels();
        }

        // ---- item-use recess + USE cap --------------------------------------------------------
        // KNOWABLE PROXY: the local recess appears only while its owner physically holds a usable
        // item card on their own turn — a purely local hand state with no wire field. What IS
        // already on the wire (and already drives RemoteItemFan) is whether that player's ITEM FAN
        // is open, which is the gesture that precedes every item use. So the recess is ALWAYS drawn
        // (the user wants every element represented) and merely switches between an IDLE look and
        // an ARMED look while the peer is actually handling items. The USE cap follows the same
        // gate — on the local board it exists only while a card is clipped in.
        bool armed = owner.ItemCardCount > 0;
        if (armed != _shownArmed)
        {
            _shownArmed = armed;
            // Additive/alpha glow rim: bright while armed, a dim outline while idle.
            _itemUseGlowMat.color = armed
                ? new Color(1f, 0.82f, 0.35f, 0.80f)
                : new Color(0.30f, 0.25f, 0.12f, 0.35f);
            _use.SetShown(armed);
        }

        // ---- wanted-slot pulse ----------------------------------------------------------------
        // The local board pulses a teal rim behind every slot the game is still WAITING for a card
        // in. For a peer that is exactly "their round-card slot is empty during the secret
        // selection phase".
        //
        // ANTI-CHEAT: this reveals nothing, on two independent grounds. (a) It is the strict
        // COMPLEMENT of what this very board already draws — an occupied slot already shows a card
        // BACK during selection, so "empty" was already visible. (b) VANILLA BROADCASTS THE SAME
        // FACT ANYWAY, twice over: the multiplayer ready tracker shows a per-character ready marker
        // for the whole selection phase (UIScenarioMultiplayerController.ShowReadyTracker →
        // UIReadyTrackerBar.RefreshReady → UIReadyTracker.ShowReady), and the hand tabs print every
        // player's live "selected/2" count with no IsUnderMyControl gate
        // (CardsHandManager.OnSelectedCardsNumberChanged ← the bolt StartRoundCards replication).
        // No card IDENTITY is involved here and nothing rides the wire.
        bool selecting = RevealGate.InScenario && RevealGate.IsSecretSelectionPhase;
        int wantedMask = 0;
        if (selecting)
        {
            if (!slot0) wantedMask |= 1;
            if (!slot1) wantedMask |= 2;
        }
        SetWanted(wantedMask);

        // ---- snap glow ------------------------------------------------------------------------
        // The local gold snap glow is a HOVER telegraph ("the held card lands here on release") and
        // the hovering hand is local-only. The moment it is actually FOR, though — a card arriving
        // in a slot — is perfectly knowable: the model transition empty→occupied. So the remote
        // glow lights on that edge and fades after SnapGlowSeconds, which reproduces the flash the
        // peer saw at the instant of their own drop. A remote hover is not reproduced (and cannot be).
        int snapMask = 0;
        for (int i = 0; i < 2; i++)
        {
            bool filled = i == 0 ? slot0 : slot1;
            if (filled && !_wasFilled[i])
                _snapLitAt[i] = Time.unscaledTime;
            _wasFilled[i] = filled;
            if (Time.unscaledTime - _snapLitAt[i] < SnapGlowSeconds)
                snapMask |= 1 << i;
        }
        SetSnap(snapMask);

        // ---- HalfSelection half-poke zones ----------------------------------------------------
        // The LOCAL half zones are deliberately INVISIBLE (HalfSelection: "the mod zones ... stay
        // INVISIBLE — the game's own on-card highlight is the only hover/selection feedback"), so a
        // literally faithful copy would draw nothing at all and the element would be missing from
        // the peer's board. The compromise: a hairline divider across the middle of each face-up
        // round card, marking where the two action halves split. Inert, one quad per slot, shown
        // exactly when the local zones are armed — i.e. while the cards are face-up in the action
        // phase, never during the secret selection phase.
        int halfMask = 0;
        if (showFronts)
        {
            if (slot0) halfMask |= 1;
            if (slot1) halfMask |= 2;
        }
        SetHalves(halfMask);

        // ---- modal pick drop field ------------------------------------------------------------
        // LOCAL-ONLY STATE: a pick flow (recover / lose / choose a card) is a prompt the game raises
        // on ONE client; there is no replicated model field that says a peer is inside one. Drawn in
        // its neutral look and shown only when both round slots are empty — the same invariant the
        // local board relies on ("the slots are guaranteed empty in pick modes"), which also
        // guarantees the field can never occlude a card the board is showing.
        SetPickField(!slot0 && !slot1 && !selecting);

        StateLine = $"use={(armed ? "armed" : "idle")}, wanted={wantedMask}, snap={snapMask}, " +
                    $"halves={halfMask}, pick={_shownPickField}";
        _ = actor; // reserved: no per-actor furniture state is knowable beyond the slots (see notes)
    }

    // ---------------------------------------------------------------- labels --

    /// <summary>
    /// (Re)write every cap label in the current language. All literals go through the game's own
    /// loc keys where one exists, so a peer's board reads in the local player's language exactly
    /// like their own board does.
    ///
    /// NEUTRAL LOOKS declared here, once, because none of these states cross the wire and none is
    /// worth a wire field:
    ///   • CONFIRM / UNDO / SKIP enabled-vs-disabled — the local caps mirror the peer's OWN uGUI
    ///     <c>readyButton.interactable</c> / <c>m_UndoButton.interactable</c> /
    ///     <c>skipButton.interactable</c>, which are recomputed per frame on THEIR client only.
    ///     Drawn ENABLED (the authored base colour), never dimmed.
    ///   • CONFIRM's live label — the local cap re-reads the game widget's own text every tick
    ///     (16 <c>ReadyButton.EButtonState</c> values: "Continue", "Perform long rest", …). Drawn
    ///     with the neutral GUI_CONFIRM wording.
    ///   • The FOLLOW/PIN toggle — the peer's <c>[Cards] TrayFollow</c> is a private VR preference
    ///     of theirs. Drawn in the DEFAULT (FOLLOW, un-accented) look.
    ///   • The gear — stateless on the local board too, so it is faithful by construction.
    /// </summary>
    private void ApplyLabels()
    {
        _confirm.SetLabel(Loc.Game("GUI_CONFIRM", "Confirm"));
        _undo.SetLabel(Loc.Game("GUI_UNDO", "Undo"));
        _use.SetLabel(Loc.Game("GUI_USE", "USE").ToUpperInvariant());
        _gear.SetLabel(Loc.Mod("set"));
        _pin.SetLabel(Loc.Mod("follow"));
        // GUI_SKIP_MOVEMENT is the key SkipButton.Start() seeds its own label from; the live button
        // swaps in GUI_SKIP_ABILITY / GUI_SKIP_ATTACK per situation, which is peer-local state.
        _skip.SetLabel(Loc.Game("GUI_SKIP_MOVEMENT", "Skip"));
    }

    // ---------------------------------------------------------------- sub-builders --

    /// <summary>
    /// The item-USE clip-in recess: gold frame + dark inner + glow rim + the localized "USE"
    /// caption BELOW the recess — the exact composition (and the exact 1.12 / 1.04 / 1.28 card-size
    /// multipliers, and the caption's one-half-card drop) of <c>PlayTray.BuildItemUseSlot</c>, minus
    /// the pulse driver and minus anything droppable.
    /// </summary>
    private Transform BuildItemUseRecess(out Material glowMat)
    {
        var root = new GameObject("ItemUseRecess").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = ItemUseMount;

        glowMat = BoardVisual.Unlit(new Color(0.30f, 0.25f, 0.12f, 0.35f));
        BoardVisual.Quad(root, "Glow", new Vector2(CardW * 1.28f, CardH * 1.28f), glowMat)
            .transform.localPosition = new Vector3(0f, 0f, 0.0005f);
        BoardVisual.Quad(root, "Frame", new Vector2(CardW * 1.12f, CardH * 1.12f),
            BoardVisual.Unlit(new Color(0.55f, 0.45f, 0.22f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        BoardVisual.Quad(root, "FrameInner", new Vector2(CardW * 1.04f, CardH * 1.04f),
            BoardVisual.Unlit(new Color(0.12f, 0.10f, 0.08f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.0005f);

        RemoteBoardContent.Label(root, "Label",
            new Vector3(0f, -(CardH * 0.5f + 0.026f), -0.001f),
            new Vector2(CardW * 1.1f, 0.030f), 0.05f,
            new Color(1f, 0.92f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold)
            .text = Loc.Game("GUI_USE", "USE").ToUpperInvariant();
        return root;
    }

    /// <summary>
    /// The SHARED DECISION DRAWER: the reserved strip below the board where the take-damage burn
    /// choice, the burn-confirm dialog and every other in-scenario prompt dock their REAL widgets
    /// on the local board (<c>PlayTray.DecisionMount</c>, 0.42 × 0.12 max).
    ///
    /// LOCAL-ONLY STATE: whether a peer currently has such a prompt open lives entirely in THEIR
    /// client's UI (<c>TakeDamagePanel</c>, <c>UIManager.dialogPopup</c>) — there is no replicated
    /// model flag for it and it is not worth a wire field. So the drawer is drawn permanently in
    /// its IDLE look: the slim empty drawer with its caption, at the mount's own offset, rather
    /// than a fake button row.
    /// </summary>
    private Transform BuildDecisionDrawer()
    {
        var root = new GameObject("DecisionDrawer").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = DecisionMount;

        BoardVisual.Quad(root, "Plate", new Vector2(0.42f, 0.055f),
            BoardVisual.Unlit(new Color(0.10f, 0.09f, 0.08f, 0.80f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        // A thin lip along the top edge so the empty drawer reads as a drawer and not as a shadow.
        BoardVisual.Quad(root, "Lip", new Vector2(0.42f, 0.004f),
            BoardVisual.Unlit(new Color(0.36f, 0.31f, 0.20f, 1f)))
            .transform.localPosition = new Vector3(0f, 0.0275f, 0f);

        RemoteBoardContent.Label(root, "Caption", new Vector3(0f, 0f, -0.001f),
            new Vector2(0.36f, 0.026f), 0.05f,
            new Color(0.72f, 0.68f, 0.58f), TextAlignmentOptions.Center)
            .text = Loc.Mod("decision_dock").ToUpperInvariant();
        return root;
    }

    /// <summary>
    /// The modal pick DROP FIELD (<c>PlayTray.BuildPickField</c>): warm accent frame + dark inner +
    /// the localized "SELECT" caption, at the slot-zone centre. Same composition, no drop mechanics,
    /// no capture radius, no highlight driver — see <see cref="Refresh"/> for when it is shown.
    /// </summary>
    private Transform BuildPickField()
    {
        var root = new GameObject("PickField").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = PickFieldMount;
        // The local field inherits the slots' 1.3× SlotScale; the remote board's own round-card
        // slots are drawn at 0.15 m wide, so match THOSE rather than the local card metrics.
        const float w = 0.15f;
        const float h = w * (88f / 63.5f);

        BoardVisual.Quad(root, "Frame", new Vector2(w * 1.12f, h * 1.12f),
            BoardVisual.Unlit(new Color(0.62f, 0.42f, 0.18f, 0.85f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        BoardVisual.Quad(root, "FrameInner", new Vector2(w * 1.04f, h * 1.04f),
            BoardVisual.Unlit(new Color(0.12f, 0.10f, 0.08f, 0.85f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.0005f);
        RemoteBoardContent.Label(root, "Caption", new Vector3(0f, -h * 0.62f, -0.001f),
            new Vector2(0.14f, 0.024f), 0.05f,
            new Color(1f, 0.85f, 0.55f), TextAlignmentOptions.Center, FontStyles.Bold)
            .text = Loc.Game("GUI_SELECT", "SELECT").ToUpperInvariant();

        root.gameObject.SetActive(false);
        return root;
    }

    /// <summary>A collider-free glow rim behind a round-card slot (the teal "wanted" pulse and the
    /// gold snap flash share this shape, exactly as on the local board — different hue, different
    /// rim size, the teal a hair less proud so the gold always draws in front of it).</summary>
    private GameObject BuildSlotGlow(string name, Vector3 slotLocal, float scale, float proud,
        Color color, bool pulse)
    {
        var mat = BoardVisual.Unlit(color);
        MeshRenderer mr = BoardVisual.Quad(_root, name, new Vector2(0.15f * scale, 0.15f * (88f / 63.5f) * scale), mat);
        mr.transform.localPosition = new Vector3(slotLocal.x, slotLocal.y, slotLocal.z + proud);
        if (pulse)
            mr.gameObject.AddComponent<RemoteGlowPulse>().Init(mat, color);
        mr.gameObject.SetActive(false);
        return mr.gameObject;
    }

    /// <summary>The hairline that marks the top/bottom action-half split on a face-up round card —
    /// the only way to depict the deliberately invisible <c>HalfSelection</c> poke zones at all
    /// (see <see cref="Refresh"/>). A single inert quad; nothing to poke.</summary>
    private GameObject BuildHalfDivider(string name, Vector3 slotLocal)
    {
        MeshRenderer mr = BoardVisual.Quad(_root, name, new Vector2(0.15f * 0.90f, 0.0012f),
            BoardVisual.Unlit(new Color(0.20f, 0.16f, 0.12f, 0.75f)));
        mr.transform.localPosition = new Vector3(slotLocal.x, slotLocal.y, slotLocal.z - 0.002f);
        mr.gameObject.SetActive(false);
        return mr.gameObject;
    }

    // ---------------------------------------------------------------- change-gated setters --

    private void SetWanted(int mask)
    {
        if (mask == _shownWantedMask)
            return;
        _shownWantedMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_wanted[i] != null && _wanted[i]!.activeSelf != on)
                _wanted[i]!.SetActive(on);
        }
    }

    private void SetSnap(int mask)
    {
        if (mask == _shownSnapMask)
            return;
        _shownSnapMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_snap[i] != null && _snap[i]!.activeSelf != on)
                _snap[i]!.SetActive(on);
        }
    }

    private void SetHalves(int mask)
    {
        if (mask == _shownHalfMask)
            return;
        _shownHalfMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_halves[i] != null && _halves[i]!.activeSelf != on)
                _halves[i]!.SetActive(on);
        }
    }

    private void SetPickField(bool show)
    {
        if (show == _shownPickField)
            return;
        _shownPickField = show;
        if (_pickField != null && _pickField.gameObject.activeSelf != show)
            _pickField.gameObject.SetActive(show);
    }

    // ---------------------------------------------------------------- inert cap --

    /// <summary>
    /// One INERT keycap: a lit bevel plate, the state-coloured cap face on top of it and an
    /// engraved label — the flat, unlit reading of the local board's beveled 3D keycaps
    /// (<c>BoardButton</c> / <c>ButtonCluster.PhysicalButton</c>), drawn with the same palette so
    /// the two boards match at a glance.
    ///
    /// It has NO collider, NO <c>IPokeable</c>, NO click action and is registered NOWHERE. That is
    /// the entire point of the class: it is a picture of a button.
    /// </summary>
    private sealed class InertCap
    {
        private readonly GameObject _go;
        private readonly TextMeshPro _label;
        private string _shown = string.Empty;

        public InertCap(Transform parent, string name, Vector3 localPos, Vector2 size, Color color,
            float plate = 0f)
        {
            _go = new GameObject(name);
            _go.transform.SetParent(parent, worldPositionStays: false);
            _go.transform.localPosition = localPos;

            // Optional dark base plate (the round turn-flow caps sit on one; the square board caps
            // do not) — drawn first so everything else layers proud of it.
            if (plate > 0f)
            {
                BoardVisual.Quad(_go.transform, "Base", new Vector2(plate, plate),
                    BoardVisual.Unlit(new Color(0.10f, 0.09f, 0.08f, 1f)))
                    .transform.localPosition = new Vector3(0f, 0f, 0.0015f);
            }

            // Bevel ring: the bright 45° chamfer that makes the local cap read as raised.
            BoardVisual.Quad(_go.transform, "Bevel", size + new Vector2(0.008f, 0.008f),
                BoardVisual.Unlit(new Color(
                    Mathf.Clamp01(color.r * 1.45f), Mathf.Clamp01(color.g * 1.45f),
                    Mathf.Clamp01(color.b * 1.45f), 1f)))
                .transform.localPosition = new Vector3(0f, 0f, 0.001f);

            BoardVisual.Quad(_go.transform, "Cap", size, BoardVisual.Unlit(color))
                .transform.localPosition = new Vector3(0f, 0f, 0.0005f);

            _label = RemoteBoardContent.Label(_go.transform, "Label", new Vector3(0f, 0f, -0.001f),
                new Vector2(size.x * 0.90f, size.y * 0.55f), 0.05f,
                new Color(0.96f, 0.93f, 0.84f), TextAlignmentOptions.Center, FontStyles.Bold,
                wrap: true);
        }

        /// <summary>Change-gated label write (a per-tick TMP assignment re-triggers auto-size).</summary>
        public void SetLabel(string text)
        {
            if (text == _shown)
                return;
            _shown = text;
            _label.text = text;
        }

        public void SetShown(bool shown)
        {
            if (_go.activeSelf != shown)
                _go.SetActive(shown);
        }
    }

    // ---------------------------------------------------------------- inertness guarantee --

    /// <summary>
    /// BELT AND BRACES for the "nothing on a peer's board may be interactable" rule: walk the
    /// finished hierarchy and destroy any <c>Collider</c> that made it in. Today none can — every
    /// builder above goes through <see cref="BoardVisual.Quad"/>, which already strips the
    /// primitive's collider — but a future edit that adds a plain <c>CreatePrimitive</c> would
    /// silently make a peer's board pokeable, and that is exactly the regression this catches. The
    /// warning it logs is deliberately loud (grep: "remote board furniture carried").
    /// </summary>
    internal static void StripColliders(GameObject root, string who)
    {
        Collider[] found = root.GetComponentsInChildren<Collider>(includeInactive: true);
        if (found == null || found.Length == 0)
            return;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                Object.Destroy(found[i]);
        }
        VRLog.Warn("Net", $"INERT-GUARD: {who} carried {found.Length} collider(s) — destroyed. " +
                          "A remote player's control board is a pure display: nothing on it may be " +
                          "pokeable, laser-targetable or grabbable.");
    }
}

/// <summary>
/// Self-animated soft pulse for the remote board's "wanted slot" hint — the mirror of
/// <c>PlayTray.SlotPulse</c> (same 3.2 rad/s breath between 0.30 and 0.85), kept local to the Net
/// module so the remote board never reaches into the local board's private visuals. Runs only while
/// its quad is active, i.e. only while a peer really has an unfilled slot during selection.
///
/// It is a RENDERING component: it writes a material colour and nothing else. No input, no
/// collider, no registry.
/// </summary>
internal sealed class RemoteGlowPulse : MonoBehaviour
{
    private Material? _material;
    private Color _base;

    internal void Init(Material material, Color baseColor)
    {
        _material = material;
        _base = baseColor;
    }

    private void Update()
    {
        if (_material == null)
            return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
        float k = Mathf.Lerp(0.30f, 0.85f, t);
        _material.color = new Color(_base.r * k, _base.g * k, _base.b * k, k);
    }
}
