using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physical Ready/Undo/Skip button cluster at the table edge (ROADMAP P3c #1).
///
/// CLICK ROUTING (decided per button from the decompiled code, all signatures
/// verified against the real GH.Runtime.dll with ilspycmd 8.2, 2026-07-15):
///
/// All three buttons are clicked via <c>ExecuteEvents.pointerClickHandler</c> on the
/// REAL uGUI button GameObject — exactly the way the game's own hotkey bridge does it
/// (<c>BaseButtons.clickButton(GameObject)</c>: <c>if (Objbutton.GetComponent&lt;Selectable&gt;()
/// .IsInteractable()) ExecuteEvents.Execute(Objbutton, new PointerEventData(EventSystem.current),
/// ExecuteEvents.pointerClickHandler);</c> — IL 36 B). This runs the FULL guard chain:
///
/// - Ready — target <c>ReadyButton.ButtonComponent.gameObject</c>
///   (<c>public Button ButtonComponent =&gt; readyButton;</c>, an <c>ExtendedButton</c>).
///   <c>ExtendedButton.OnPointerClick</c> gates on
///   <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c> (FTUE/tutorial),
///   then the prefab's onClick reaches <c>public void OnClick(bool networkActionIfOnline
///   = true)</c> which re-checks <c>ButtonComponent.enabled</c>, the warning mask and
///   <c>readyButton.interactable</c>, and (mouse mode — guaranteed by
///   <see cref="InputModeGuard"/>) commits synchronously via <c>public void
///   OnClickInternal(bool networkActionIfOnline = true)</c> (IL 1099 B). Calling
///   OnClickInternal directly would SKIP those guards and the MP synchronizer path —
///   deliberately not done (UI-ARCH §9.8).
/// - Undo — target <c>UndoButton.m_UndoButton.gameObject</c>. Prefab onClick →
///   <c>public void OnClick(bool networkActionIfOnline = true)</c> (checks
///   <c>m_UndoButton.interactable</c> + InteractabilityManager + plays the undo sound)
///   → <c>public bool OnClickInternal(bool networkActionIfOnline = true, GameAction
///   action = null)</c> (IL 670 B).
/// - Skip — target <c>SkipButton.skipButton.gameObject</c> (plain <c>Button</c>).
///   Prefab onClick → <c>public void OnClickFromButton()</c> which in mouse mode goes
///   straight to <c>public void OnClick(bool networkActionIfOnline = true)</c>.
///
/// MODALITY: ExecuteEvents bypasses GraphicRaycasters, so the game's raycaster-based
/// UI lock would NOT stop it (UI-ARCH §9.3). Pokes are therefore additionally gated on
/// <see cref="CanvasConversion.IsLockedNow"/> (mirrors <c>UIManager.ToggleLockUI</c>
/// via VREvents plus the phase-banner soft lock) and on the game's own
/// <c>Selectable.IsInteractable()</c>.
///
/// STATE MIRRORING (per frame, allocation-free): label text mirrors the live
/// <c>buttonText.text</c> TMP field of each real button — that string is already
/// localized by the game itself for the current <c>ReadyButton.EButtonState</c>
/// (16-member enum, verified) / undo / skip flows, so every state shows exactly the
/// words the 2D UI would show. The enum drives the accent color (confirm-ish states
/// glow warmer). Interactability mirrors <c>readyButton.interactable</c> /
/// <c>m_UndoButton.interactable</c> / <c>skipButton.interactable</c> — recomputed
/// every frame by the game's own <c>CheckButtonInteractability()</c> (UI-ARCH §9.4).
/// Disabled or hidden buttons are dimmed/hidden and their poke collider is disabled,
/// so they produce no poke events and no haptics.
///
/// Visuals: procedural base+cap meshes now; bundle prefabs
/// (<c>Assets/Bundle/Table/Button_Ready.prefab</c> etc., see the Table README asset
/// wishlist) are probed first and used when present. The cluster is hidden in
/// <see cref="VRMode.Menu2D"/>.
///
/// DOCKED ON THE CONTROL BOARD (test #19, relaid user #8; RIGID since the lag fix): while
/// a <see cref="PlayTray"/> exists the cluster is parented DIRECTLY under the tray-root
/// transform at a FIXED local pose in the RIGHT-side column beside the Confirm/Undo pads
/// and the gear (see the COLUMN constants + <see cref="RelayoutColumn"/>). Rigid parenting
/// is the lag fix (user: "the Rundenknöpfe lag behind when I move the board fast"): the
/// old pose-follow copied the mount's world pose once per WorldUIDriver.Update, while the
/// tray's grab-follow writes its pose in a DIFFERENT component's update pass — so during a
/// fast board drag the cluster rendered one frame behind and visibly trailed. As a child
/// of the tray root it now rides the same transform pass as Confirm/Undo — zero latency,
/// no per-frame world-pose writes; the local pose is recomputed only when config/layout
/// changes (tray/board switch, per-board cluster scale, per-board [Cards] ClusterOffset seat,
/// [RoundButtons] group offset — the last two live, without a rebuild). A
/// tray teardown destroys the parented cluster with it — Tick detects the dead root,
/// releases the poke registrations and lazily rebuilds against the next tray.
/// Labels flip flat onto the caps there (the table-edge label sign would lie
/// across the slot captions), the buttons register as tray laser targets (poke AND
/// laser on every board element — the board contract), and the cluster hides with
/// the tray. The floating table-edge slot remains the no-tray fallback.
///
/// <para>ONE CHARACTER OWNS THE SKIP (user, hardware ModBuild 96). The docked cluster's only live
/// member — the SKIP cap — obeys the same per-character ownership rule the board's CONFIRM/UNDO
/// keycaps and the decision dock already do, including the "nobody is at turn ⇒ it belongs to
/// everybody" nuance the user ruled on in ModBuild 85. See <see cref="SkipCapForeignView"/> for the
/// predicate, why it is the established one term for term, and why the multiplayer mirror follows
/// with no wire change.</para>
///
/// <para>GEOMETRY IS CONFIG, AND SYNCED. The cap's seat, shape, size and press travel are the
/// <c>[RoundButtons]</c> family (<see cref="ButtonTuning"/>) — surfaced in the debug menu's
/// control-board page under Tasten ▸ "Überspringen- &amp; Fixier-Taste" beside their per-board
/// siblings, and carried to every peer on extension record 28 (ids 81..88 + the shape at 228) so a
/// player who reshapes their skip cap is seen reshaping it.</para>
/// </summary>
internal sealed class ButtonCluster
{
    // ---- RIGHT-SIDE COLUMN (user #8, repositioned for relaid user #8/point 8) ------------
    // The cluster no longer sits bottom-CENTER under the card slots: every turn-flow
    // button — including the transient round ones ("Bewegung überspringen" etc.) — now
    // stacks in a column on the RIGHT side of the board, directly beside the
    // Confirm/Undo ("Rückgängig machen") pads and the gear. Constants are tray-ROOT-local
    // meters, taken from PlayTray's collision map: the free bottom-right zone LEFT of the
    // Undo/gear column (pad left edge ≈ 0.185, gear left edge ≈ 0.19) and BELOW the card
    // slots (captions bottom edge ≈ -0.068), inside the board (bottom edge -0.16).
    // The column ANCHOR is fixed — transient buttons appear/disappear in place and only
    // the per-button size/slots reflow, never the cluster's world position.
    //
    // DEFAULT MOVED DOWN-BOARD (point 8, "zu nah an den Karten"): the old anchor
    // (y -0.115, height budget 0.084) put the column's TOP edge at y ≈ -0.073 — only
    // ~8 mm root-local below the slot captions/card bottom edge (≈ -0.065..-0.068), so a
    // hand reaching for a slotted card brushed the top transient button. New default:
    // center y -0.124 with a 0.070 budget → top edge ≈ -0.089 (~24 mm clearance to the
    // card bottom, 3× the old gap), bottom edge ≈ -0.159 still inside the board edge
    // (-0.16), x span 0.110..0.186 still clear of the pad column (left edge ≈ 0.185).
    // The default cap ceiling shrank 45 → 42 mm (ButtonTuning [RoundButtons] CapSize) so a
    // single cap's footprint fits the tighter budget. On top of the spatial gap, the
    // depth-fire press (point 6) means a brush can no longer fire at all. The
    // ButtonTuning [RoundButtons] OffsetX/Y/Z binds shift the whole group from this anchor.
    private const float ColumnCenterX = 0.148f;
    private const float ColumnCenterY = -0.124f;
    private const float ColumnRootZ = -0.006f;   // same board-face seat as the pads/mounts
    private const float ColumnRootHeight = 0.070f; // root-local vertical budget
    private const float ColumnRootWidth = 0.076f;  // root-local lateral budget

    // Auto-scale (user #8: "always choose the size so ALL buttons fit"): per-button slot
    // math in CLUSTER-local units (the docked mount scale converts to root units). The
    // size CEILING is config now (ButtonTuning.TransientCapRadius, default 42 mm): the
    // auto-fit only shrinks BELOW it when several buttons share the column; a single
    // button uses exactly the configured size.
    private const float ColumnGap = 0.012f;      // inter-slot gap, cluster-local
    private const float MinCapRadius = 0.024f;   // readability floor — below this, go 2 columns
    private const float BaseFootprint = 2.4f;    // base plate side = BaseFootprint × cap radius

    // DEPTH-CORRECT proud seat (replaces the old ZTest-Always shine-through). When docked
    // the cluster is lifted this far toward the player along the board-face normal so its
    // base plate clears the RAISED board rim instead of z-fighting/sinking into the slab.
    // A CONSTANT (not per-board): CardsConfig has no cluster-proud tunable of its own — the
    // per-board ClusterOffset is already consumed by PlayTray to place ButtonClusterMount, so
    // reading CardsConfig.ClusterOffset here would double-apply it. (That prohibition still
    // stands and is why AttachDocked takes the seat as a DELTA off the mount's own transform —
    // PlayTray.ButtonClusterOffset — rather than re-reading the config.) 10 mm matches PlayTray's proud magnitude
    // (FixedProudZ = 5 mm) with generous headroom over the mount's own 6 mm; the caps already
    // stand ~24 mm proud so this never reads as "floating". Scaled by the mount's world scale
    // (rig × grab × 0.7 dock) so it tracks the cluster's rendered size on every board variant.
    private const float ClusterProudOffset = 0.010f;

    private GameObject? _root;
    private PhysicalButton? _ready;
    private PhysicalButton? _undo;
    private PhysicalButton? _skip;
    private bool _visible;

    /// <summary>
    /// Multiplayer board-UI read seam: true while the SKIP disc is visible on the DOCKED cluster
    /// (i.e. on the control board — the only cluster member a remote board mirrors). Published as
    /// a static because the cluster instance is a private of WorldUIModule; written every Tick,
    /// cleared whenever the cluster is hidden/undocked/shut down.
    /// </summary>
    internal static bool BoardSkipShown { get; private set; }

    /// <summary>
    /// Multiplayer cap-label read seam (wire record <c>NetProtocol.ExtIdCapLabels</c> bit 1): the
    /// text the docked SKIP disc currently displays — the game's own live
    /// <c>SkipButton.buttonText</c> wording ("Bewegen überspringen" / "Angriff überspringen" /
    /// "Fähigkeit überspringen"), which the cluster already mirrors onto the cap. Null while the
    /// skip is not shown on the board, so the record is omitted exactly then. Peers render this
    /// string on their copy's skip cap verbatim (their board, their language — the pick-banner
    /// rule); the neutral GUI_SKIP_MOVEMENT re-localization is only their no-record fallback.
    /// </summary>
    internal static string? BoardSkipLabel { get; private set; }

    /// <summary>
    /// Multiplayer cap-STATE read seam (board-UI record byte 2, <c>BoardUiCapSkipEnabledBit</c>):
    /// true while the docked SKIP disc is INTERACTABLE. The cluster's disabled look is not a
    /// palette swap but a 0.75 lerp of the cap accent toward dark wood plus a 0.35-alpha label
    /// (<see cref="PhysicalButton"/>'s state apply), and a peer's mirrored cap used to render the
    /// enabled look unconditionally — a dead skip and a live one were the same picture. Written on
    /// the same tick as <see cref="BoardSkipShown"/>, off the same mirror.
    /// </summary>
    internal static bool BoardSkipEnabled { get; private set; }

    // Right-column layout state (user #8).
    private bool _dockedNow;
    private float _rootToLocal = 1f / 0.7f; // root-local → cluster-local unit factor (mount carries the 0.7 dock scale)
    private float _dockLocalFactor = -1f;   // tray-root-local dock scale the rigid attach was computed at
    private int _tuningVersion;             // ButtonTuning pull-based live-apply (see Tick)

    // The four values BuildProcedural bakes into the cap mesh, recorded at Build. A ButtonTuning
    // change that leaves all four alone needs no rebuild — see the block in Tick.
    private bool _builtRoundShape;
    private float _builtCapW, _builtCapH, _builtCapD;

    /// <summary>True when a [RoundButtons] change actually altered the cap MESH (shape, or the
    /// square cap's W/H/D) and the buttons must therefore be rebuilt. Everything else in
    /// <see cref="ButtonTuning"/> is re-read by a live path and must NOT cost a teardown.</summary>
    private bool BuiltCapGeometryChanged()
    {
        bool round = ButtonTuning.TransientRound;
        if (round != _builtRoundShape)
            return true;
        if (round)
            return false; // a round puck's size comes from RelayoutColumn's radius, not from W/H/D
        return !Mathf.Approximately(_builtCapW, ButtonTuning.RoundCapWidth)
               || !Mathf.Approximately(_builtCapH, ButtonTuning.RoundCapHeight)
               || !Mathf.Approximately(_builtCapD, ButtonTuning.RoundCapDepth);
    }

    private int _lastLayoutCount = -1;
    private float _lastLayoutRadius;
    private readonly System.Collections.Generic.List<PhysicalButton> _layoutScratch = new(3);

    public void Init()
    {
        // Built lazily in Tick when a scenario + rig exist.
    }

    public void Tick()
    {
        Choreographer? choreographer = Choreographer.s_Choreographer;
        bool wantCluster = WorldUIConfig.ButtonCluster.Value
                           && WorldUIConfig.ConversionActive
                           && VRModeStateMachine.CurrentMode != VRMode.Menu2D
                           && choreographer != null;

        if (!wantCluster)
        {
            SetVisible(false);
            BoardSkipShown = false;
            BoardSkipEnabled = false;
            BoardSkipLabel = null;
            return;
        }

        // Rigid-parenting teardown seam: the cluster root lives UNDER the tray root while
        // docked, so a tray/board teardown destroyed it externally. Release the stale
        // PhysicalButtons (poke registrations, laser bookkeeping) before rebuilding.
        if (_root == null && _ready != null)
            Shutdown();

        // ButtonTuning live-apply (user #8/#9) — REBUILD ONLY WHAT NEEDS REBUILDING.
        //
        // This used to tear the cluster down and rebuild it on ANY ButtonTuning.Version move, on
        // the reasoning that it is cheap (three buttons) and covers every knob with one path. It
        // does cover every knob — but a teardown is Object.Destroy + a fresh Build, i.e. the cap
        // VANISHES AND REAPPEARS in one frame with no crumble and no assemble. That is the exact
        // shape of pop the standing rule forbids ("nothing pops; keycaps move/appear through the
        // authored animation"), and it fired for dials that change nothing about the cap's mesh —
        // the offsets (a pose), the cap size (RelayoutColumn re-reads it every frame), the press
        // travel (TravelLocal is a per-frame property), every [ButtonColors] tint (applied in the
        // state pass) and every [ButtonAnim] entry.
        //
        // So the rebuild is now gated on the four values BuildProcedural actually BAKES into the
        // mesh — the cap shape and, for the square shape, its W/H/D. Everything else re-reads
        // itself: the seat through AttachDocked's pose gate (which now compares the solved pose),
        // the size through RelayoutColumn, the rest per frame. Moving an offset stepper therefore
        // SLIDES the group to its new seat, exactly like the per-board mounts do.
        ButtonTuning.Bind();
        if (_root != null && _tuningVersion != ButtonTuning.Version)
        {
            _tuningVersion = ButtonTuning.Version;
            if (BuiltCapGeometryChanged())
                Shutdown();
        }

        if (_root == null)
            Build();
        if (_root == null)
            return;

        bool placed = PlaceCluster();
        SetVisible(placed);
        if (!placed)
        {
            BoardSkipShown = false;
            BoardSkipEnabled = false;
            BoardSkipLabel = null;
            return;
        }

        bool locked = CanvasConversion.IsLockedNow;
        // Button layout policy: confirmations/undo live ONLY on the right-hand board pads now
        // (the mod Confirm/Undo on the control board). The center cluster's Ready + Undo twins
        // are forced permanently OFF (null → hidden) so there is never a duplicate "Fortfahren"/
        // Undo here. ONLY Skip stays mirrored (it has no right-pad equivalent and only appears
        // when the game marks the step skippable). The no-tray floating fallback is unaffected —
        // it still shows this cluster, but Ready/Undo are intentionally hidden there too.
        _ready!.MirrorReady(null, locked);
        _undo!.MirrorUndo(null, locked);
        // ONE CHARACTER OWNS THE SKIP, TOO (user, hardware ModBuild 96): "Wenn man mit einem
        // Character in der Phase ist eine Attacke oder Bewegung zu bestätigen oder zu überspringen,
        // bleibt trotzdem der 'Angriff überspringen' bzw. 'Bewegung überspringen' Knopf noch am Board
        // sichtbar, obwohl ein anderer Character ausgewählt wurde — er soll wie die anderen Buttons
        // auch nur bei dem Character angezeigt werden, der diese Wahl aktuell treffen muss."
        //
        // Handing MirrorSkip a NULL button is the whole fix, and it is deliberately the same lever
        // the two dead twins above already pull: MirrorSkip(null) runs SetState(visible: false),
        // which is the cap's ordinary logical hide — collider off, layout reflowed, and the AUTHORED
        // crumble-to-dust played on the way out (and the matched materialize-from-dust on the way
        // back). Nothing here reaches for SetActive, which is precisely the class of pop ModBuild 96
        // fixed for the item-flow caps.
        _skip!.MirrorSkip(SkipCapForeignView() ? null : choreographer!.m_SkipButton, locked);

        // Publish the docked Skip's live visibility for the multiplayer board-UI record — the
        // one cluster member a peer's copy of this board draws.
        BoardSkipShown = _dockedNow && _skip.VisibleNow;
        // …and whether it is actually PRESSABLE, for the cap-state byte of the same record: the
        // owner's dead skip cap is dimmed toward dark wood with a faded label, and that is what a
        // peer must see too.
        BoardSkipEnabled = BoardSkipShown && _skip.InteractableNow;
        // The label the shown disc wears, for the cap-labels wire record: the game's own live
        // SkipButton text (the exact string MirrorSkip just applied). Null while not shown, so
        // the record's presence tracks the control's.
        BoardSkipLabel = BoardSkipShown && choreographer.m_SkipButton != null
                         && choreographer.m_SkipButton.buttonText != null
            ? choreographer.m_SkipButton.buttonText.text
            : null;

        // User #8: pack whatever is visible into the fixed right-side column, auto-sized
        // from the live count (transient buttons reflow slots only, never the anchor).
        RelayoutColumn();

        _ready.Animate();
        _undo.Animate();
        _skip.Animate();
    }

    /// <summary>
    /// Right-column auto-layout (user #8): stack every VISIBLE button top-to-bottom in
    /// the column beside the Undo/gear pads, cap size computed from the count so the
    /// full set always fits the column budget — shrink when transient buttons appear,
    /// grow back when they vanish. Readability floor first (<see cref="MinCapRadius"/>),
    /// then a second column when the floor would overflow the stack. In the no-tray
    /// floating fallback the classic horizontal row (authored sizes) is restored.
    /// Logged once per change (count → chosen size).
    /// </summary>
    private void RelayoutColumn()
    {
        if (_ready == null || _undo == null || _skip == null)
            return;

        if (!_dockedNow)
        {
            // Floating table-edge fallback: the authored Undo | Ready | Skip row.
            _undo.SetSlot(new Vector3(-0.11f, 0f, 0f), _undo.BaseRadius);
            _ready.SetSlot(Vector3.zero, _ready.BaseRadius);
            _skip.SetSlot(new Vector3(0.11f, 0f, 0f), _skip.BaseRadius);
            _lastLayoutCount = -1;
            return;
        }

        _layoutScratch.Clear();
        if (_ready.VisibleNow) _layoutScratch.Add(_ready);
        if (_undo.VisibleNow) _layoutScratch.Add(_undo);
        if (_skip.VisibleNow) _layoutScratch.Add(_skip);
        int n = _layoutScratch.Count;
        if (n == 0)
        {
            _lastLayoutCount = 0;
            return;
        }

        float columnH = ColumnRootHeight * _rootToLocal;
        float columnW = ColumnRootWidth * _rootToLocal;

        // Slot math: base plate side = BaseFootprint × radius. Single column first; if
        // that pushes the cap under the readability floor, split into two columns.
        // The cap-size CEILING is the ButtonTuning [RoundButtons] CapSize bind (user #8): a
        // single transient button honors the configured size EXACTLY (the user owns the
        // trade-off if a big cap spills the zone); multiple buttons still auto-shrink so
        // they never overlap each other or the neighboring pads.
        float capCfg = ButtonTuning.TransientCapRadius;
        int cols = 1;
        int rows = n;
        float radius = SlotRadius(columnH, columnW, rows, cols);
        if (radius < MinCapRadius && n > 1)
        {
            int rows2 = (n + 1) / 2;
            float r2 = SlotRadius(columnH, columnW, rows2, 2);
            if (r2 > radius)
            {
                cols = 2;
                rows = rows2;
                radius = r2;
            }
        }
        radius = n == 1 ? capCfg : Mathf.Clamp(radius, 0.015f, capCfg);

        float pitch = BaseFootprint * radius + ColumnGap;
        float extentZ = rows * BaseFootprint * radius + (rows - 1) * ColumnGap;
        float extentX = cols * BaseFootprint * radius + (cols - 1) * ColumnGap;
        for (int i = 0; i < n; i++)
        {
            int row = i / cols;
            int col = i % cols;
            bool loneLastRow = cols == 2 && row == rows - 1 && (n % 2) == 1;
            // Cluster-local frame while docked: +Z = down the board (toward the player),
            // +X lateral. Row 0 sits at the TOP of the column.
            float z = -extentZ * 0.5f + BaseFootprint * radius * 0.5f + row * pitch;
            float x = loneLastRow || cols == 1
                ? 0f
                : -extentX * 0.5f + BaseFootprint * radius * 0.5f + col * pitch;
            _layoutScratch[i].SetSlot(new Vector3(x, 0f, z), radius);
        }

        if (n != _lastLayoutCount || Mathf.Abs(radius - _lastLayoutRadius) > 0.0005f)
        {
            _lastLayoutCount = n;
            _lastLayoutRadius = radius;
            VRLog.Info("WorldUI", $"ButtonCluster relayout: {n} visible button(s) → cap radius " +
                                  $"{radius * 1000f:F0} mm in {cols} column(s) (right-side column beside " +
                                  "Undo/gear; anchor fixed, transient buttons reflow in place).");
        }
    }

    /// <summary>Largest cap radius whose <paramref name="rows"/>×<paramref name="cols"/> grid of
    /// <see cref="BaseFootprint"/>-sized plates fits the column budget.</summary>
    private static float SlotRadius(float columnH, float columnW, int rows, int cols)
    {
        float slotH = (columnH - (rows - 1) * ColumnGap) / rows;
        float slotW = (columnW - (cols - 1) * ColumnGap) / cols;
        return Mathf.Min(slotH, slotW) / BaseFootprint;
    }

    /// <summary>Change-gate for the skip-ownership line below: the last (foreign, owner, focused,
    /// recoverable, attributable) tuple we logged, folded into one int from the game's own actor ids
    /// so the per-frame resolve builds no string at all. 0 = nothing logged yet.</summary>
    private int _skipOwnerLogKey;

    /// <summary>
    /// ONE CHARACTER OWNS THE SKIP — the cluster edition of the rule
    /// <c>Cards.PlayTray.ConfirmCapsForeignView</c> applies to the board's CONFIRM/UNDO keycaps,
    /// <c>WorldUI.Surfaces.DecisionDockSurface.UpdateFocusVisibility</c> to the decision row and
    /// <c>WorldUI.Surfaces.UseBarsSurface.UpdateFocusVisibility</c> to the use bars. True while the
    /// docked SKIP cap must be HIDDEN because the choice it commits does not belong to the character
    /// the player is currently looking at.
    ///
    /// <para>THE BUG (user, hardware ModBuild 96): "…bleibt trotzdem der 'Angriff überspringen' bzw.
    /// 'Bewegung überspringen' Knopf noch am Board sichtbar, obwohl ein anderer Character ausgewählt
    /// wurde — er soll wie die anderen Buttons auch nur bei dem Character angezeigt werden, der diese
    /// Wahl aktuell treffen muss." The per-character ownership machinery ModBuild 84–91 built was
    /// applied to the PlayTray caps and to the decision dock, and the Skip simply never joined it:
    /// it is not a <c>PlayTray.BoardButton</c> at all but a <see cref="PhysicalButton"/> of this
    /// cluster, mirrored straight off the ONE global <c>Choreographer.m_SkipButton</c>
    /// (<see cref="PhysicalButton.MirrorSkip"/>) — a single scenario-wide widget with no character
    /// in it, exactly like the <c>ReadyButton</c> that produced the original ModBuild 84 report.</para>
    ///
    /// <para>THE PREDICATE IS THE ESTABLISHED ONE, TERM FOR TERM, AND THAT IS THE POINT. It is not
    /// "a rule of the same spirit": it computes the identical four facts from the identical sources
    /// as <c>ConfirmCapsForeignView</c> —
    /// <list type="bullet">
    /// <item>OWNER = the hand the GAME presents, <c>DecidingHand() ?? ActiveHand()</c> gated on
    ///   <c>IsLocalHand</c>. That expression IS <c>CardsDriver.CurrentHand()</c> (CardsDriver.2.
    ///   Update.cs:941-942), which is private and per-instance; it is re-derived here rather than
    ///   plumbed because both halves are static <c>CardsGameApi</c> members and the chain itself was
    ///   extracted into <see cref="Cards.CardsGameApi.DecidingHand"/> for precisely this reason —
    ///   "so a SECOND caller can ask the question that method only answers implicitly".</item>
    /// <item>FOCUSED = <c>CharacterFocus.Focused</c>, the plain field, NOT <c>ReadOnlyView</c>:
    ///   ReadOnlyView is latched by the edge-driven rebuild while this runs every frame.</item>
    /// <item>RECOVERABLE = <c>CharacterFocus.CanFocus(owner)</c> — the deadlock interlock. The cap
    ///   is only ever taken away while the one portrait click that brings it back is available.</item>
    /// <item>ATTRIBUTABLE = <c>CharacterFocus.TurnActor != null || DecidingHand() != null</c> — the
    ///   ModBuild 85 nuance the user ruled on and the reason this is not a stricter rule than its
    ///   sibling. In the phase where nobody is at turn ("in dieser Phase macht eine Differenzierung
    ///   weniger Sinn … soll jeder Character den Fortfahren Knopf haben") the control belongs to
    ///   EVERY character, so the gate falls open and the cap shows on every board.</item>
    /// </list>
    /// Same inputs ⇒ same answer, so the Skip cap and the Confirm/Undo caps standing beside it can
    /// never disagree about who owns the step — which is the property that actually matters, and
    /// which a differently-shaped rule would not have.</para>
    ///
    /// <para>WHY THE SHARED HALF LIVES HERE AND NOT IN ONE PLACE WITH ITS SIBLING. <c>PlayTray.5.
    /// Status.cs</c> is owned by another worker in this round and may not be edited from here, so
    /// the predicate is written where this round owns the code. The two are a MIRROR PAIR in the
    /// sense <c>scripts/check-mirrors.sh</c> uses, and the honest resolution is the one that file
    /// itself recommends — fold the second copy into the first — which is a one-line change the
    /// moment PlayTray is editable again: make this method <c>internal static</c> (it already is)
    /// and have <c>ConfirmCapsForeignView</c> call it with its own <c>hand</c>. Nothing about the
    /// rule is duplicated in a form that can drift silently in the meantime: every fact above is
    /// read from the same static, and a divergence would show up as the two caps disagreeing on
    /// screen, which is exactly the symptom this whole family of fixes is about.</para>
    ///
    /// <para>THE GAME'S OWN GATE IS UNTOUCHED, and it is worth saying because Skip has one that
    /// CONFIRM does not: <see cref="PhysicalButton.MirrorSkip"/> already requires the game's own
    /// visibility (<c>canvasGroup.alpha &gt; 0.5</c>, which needs
    /// <c>Choreographer.ThisPlayerHasTurnControl</c>) — the 2026-08-04 MP-leak fix. So this gate can
    /// only ever hide a cap the game was already showing to THIS client, never reveal one; it
    /// narrows "my party may skip" to "the character I am looking at may skip".</para>
    ///
    /// <para>MULTIPLAYER — NO WIRE CHANGE, and none is possible: the hide runs through
    /// <c>SetState(visible: false)</c>, which clears <c>_logicalVisible</c>, and
    /// <see cref="BoardSkipShown"/> is DEFINED as that flag (<c>_dockedNow &amp;&amp;
    /// _skip.VisibleNow</c>). <c>NetAvatarDriver</c> reads it for <c>BoardUiSkipBit</c>, gates
    /// <see cref="BoardSkipEnabled"/> on it and drops the <c>ExtIdCapLabels</c> skip string when it
    /// is false, so a peer's <c>RemoteBoardFurniture</c> crumbles its mirrored cap away through the
    /// records that already exist — the standing rule that a remote board shows what THAT PLAYER
    /// sees holds by construction here, because the mirror is sampled from the very flag the local
    /// renderer obeys.</para>
    ///
    /// <para>Cheap and silent: two static reads and a reference compare per tick, and the log line
    /// is change-gated on an id tuple so no string is built unless the state actually moves.</para>
    /// </summary>
    internal bool SkipCapForeignView()
    {
        CardsHandUI? deciding;
        ScenarioRuleLibrary.CPlayerActor? owner;
        ScenarioRuleLibrary.CPlayerActor? focused;
        bool recoverable;
        bool attributable;
        try
        {
            deciding = Cards.CardsGameApi.DecidingHand();
            CardsHandUI? presented = deciding ?? Cards.CardsGameApi.ActiveHand();
            if (presented != null && !Cards.CardsGameApi.IsLocalHand(presented))
                presented = null;
            owner = presented != null ? presented.PlayerActor : null;
            focused = Board.CharacterFocus.Focused;
            recoverable = owner != null && Board.CharacterFocus.CanFocus(owner);
            attributable = Board.CharacterFocus.TurnActor != null || deciding != null;
        }
        catch (System.Exception)
        {
            // Presentation question, asked every frame off a half-torn game model during scene
            // changes. FAIL OPEN — a thrown resolve must never take a reachable control away.
            return false;
        }

        bool foreign = focused != null && owner != null
                       && !ReferenceEquals(focused, owner)
                       && recoverable
                       && attributable;

        int key = (foreign ? 1 : 2) * 31 + (owner != null ? owner.ID : 0);
        key = key * 31 + (focused != null ? focused.ID : 0);
        key = key * 31 + (recoverable ? 1 : 0);
        key = key * 31 + (attributable ? 1 : 0);
        if (key == _skipOwnerLogKey)
            return foreign;
        _skipOwnerLogKey = key;

        string ownerName = Board.CharacterFocus.Describe(owner);
        string focusName = Board.CharacterFocus.Describe(focused);
        if (foreign)
            VRLog.Info("WorldUI", "ButtonCluster: SKIP cap HIDDEN — the skip belongs to " +
                                  $"'{ownerName}' (the hand the game presents; the press routes to " +
                                  "the ONE global SkipButton, which the game only arms for the " +
                                  $"character it is acting on) and the player is looking at " +
                                  $"'{focusName}' (focus override). Same rule the CONFIRM/UNDO " +
                                  "keycaps obey, same authored crumble on the way out; one portrait " +
                                  $"click on '{ownerName}' brings it straight back, and nothing in " +
                                  "the game was touched.");
        else
            VRLog.Info("WorldUI", "ButtonCluster: SKIP cap NOT hidden by the owner gate — " +
                                  (owner == null
                                      ? "no presented hand this tick, so the game's own visibility " +
                                        "decides (MirrorSkip still requires the SkipButton's own " +
                                        "canvas alpha, i.e. this client's turn control)."
                                      : focused == null
                                          ? $"owner '{ownerName}' is in view (no focus override — " +
                                            "following the game)."
                                          : !recoverable
                                              ? $"owner '{ownerName}' cannot be focused right now " +
                                                "(exhausted / no live scenario / card selection), so " +
                                                "the cap stays reachable rather than strand the " +
                                                $"player while looking at '{focusName}'."
                                              : !attributable
                                                  ? "the skip is NOT ATTRIBUTABLE to any character " +
                                                    "— nobody is at turn and no decision flow claims " +
                                                    "the presented hand — so it belongs to EVERY " +
                                                    "character and shows on every board (presented " +
                                                    $"hand '{ownerName}', looking at '{focusName}')."
                                                  : $"owner '{ownerName}' is the character in view."));
        return foreign;
    }

    public void Shutdown()
    {
        _ready?.Destroy();
        _undo?.Destroy();
        _skip?.Destroy();
        _ready = _undo = _skip = null;
        _laserTray = null; // a rebuilt cluster must re-register its laser targets
        _dockedNow = false;
        BoardSkipShown = false;
        BoardSkipLabel = null;
        // …and the cap-STATE seam with them. It was missing here while its two siblings were reset,
        // so a teardown between two Ticks left BoardSkipEnabled true with BoardSkipShown false — a
        // peer read "enabled" for a cap the record says is not on the board. Harmless today (the
        // receiver reads the bit only while the shown bit is set) and wrong regardless.
        BoardSkipEnabled = false;
        _skipOwnerLogKey = 0;   // the ownership line must fire again for the rebuilt cluster
        _dockLocalFactor = -1f;
        _lastLayoutCount = -1;
        _lastLayoutRadius = 0f;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ---- construction --------------------------------------------------------------------

    private void Build()
    {
        if (!PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out _, out _))
            return; // no anchor yet

        _root = new GameObject("GloomhavenVR.ButtonCluster");

        // Left-to-right: Undo | Ready (center, larger) | Skip. Offsets in real meters.
        // T4 antique palette: accents desaturated toward worn-material tones (leather /
        // sage / slate) so the cluster reads as carved board furniture, not plastic keys.
        _undo = PhysicalButton.Create(_root.transform, "Undo", new Vector3(-0.11f, 0f, 0f), 0.038f,
            new Color(0.45f, 0.33f, 0.22f), ClickUndo);
        _ready = PhysicalButton.Create(_root.transform, "Ready", Vector3.zero, 0.05f,
            new Color(0.35f, 0.46f, 0.28f), ClickReady);
        _skip = PhysicalButton.Create(_root.transform, "Skip", new Vector3(0.11f, 0f, 0f), 0.038f,
            new Color(0.37f, 0.44f, 0.56f), ClickSkip);
        _skip.IsBoardSkip = true; // the one cluster member a peer's remote board mirrors

        // Mod layer (render-only — pokes go through the VRInteractables registry).
        VRLayers.Apply(_root);
        _tuningVersion = ButtonTuning.Version; // fresh build reflects current config
        // …and record what the caps were BAKED from, so the next Version move can tell a mesh
        // change (rebuild) from a pose/size/colour change (live, no pop) — see Tick.
        _builtRoundShape = ButtonTuning.TransientRound;
        _builtCapW = ButtonTuning.RoundCapWidth;
        _builtCapH = ButtonTuning.RoundCapHeight;
        _builtCapD = ButtonTuning.RoundCapDepth;
        VRLog.Info("WorldUI", $"ButtonCluster geometry config applied — {ButtonTuning.Describe()}.");
        VRLog.Info("WorldUI", "ButtonCluster built (Undo | Ready | Skip) — DEPTH-CORRECT: lit opaque " +
                              $"BoardLit caps at natural ZTest LEqual, seated {ClusterProudOffset * 1000f:0} mm " +
                              "proud of the board face; labels depth-honest (per-label font-material instance, " +
                              "queue 3000 + ZTest LEqual). ANTIQUE caps (user #5): wood-grain keycap + engraved " +
                              "parchment label, the VR-settings-gear style (game-default sprite face removed). " +
                              $"Round-label seated {PhysicalButton.DockedLabelProud * 1000f:0.0} mm proud of the cap TOP face " +
                              "(user #4: down from 10 mm so it no longer floats; anti-flicker from render-queue + ZTest " +
                              "ordering, not the offset). Docked layout (user #8): RIGHT-side auto-fit column beside the Undo/gear pads.");
    }

    /// <summary>
    /// Pose the cluster; returns whether it should be visible this frame. Docked on the
    /// control board while a <see cref="PlayTray"/> mount exists (test #19): RIGIDLY
    /// parented under the tray-root transform at a fixed local pose (the lag fix — board
    /// motion carries the cluster in the same transform pass as Confirm/Undo, zero
    /// latency; see the class doc), hidden with the tray. <see cref="AttachDocked"/> only
    /// writes the local pose when the attachment parameters actually changed. Floating
    /// table-edge slot as the no-tray fallback (detached, PanelLayout pose).
    /// </summary>
    private bool PlaceCluster()
    {
        if (_root == null)
            return false;
        Transform t = _root.transform;

        PlayTray? tray = PlayTray.Current;
        Transform? mount = tray?.ButtonClusterMount;
        Transform? trayRoot = tray?.Root;
        if (mount != null && trayRoot != null)
        {
            SetDockedLabels(true);
            RegisterTrayLaserTargets();
            if (!mount.gameObject.activeInHierarchy)
                return false; // tray hidden (deferred placement) → cluster hides with it
            AttachDocked(t, trayRoot, mount, tray!.ButtonClusterOffset);
            _dockedNow = true;
            return true;
        }

        _dockedNow = false;
        _dockLocalFactor = -1f;
        if (t.parent != null)
            t.SetParent(null, worldPositionStays: false); // leave a (dying) tray hierarchy
        SetDockedLabels(false);
        if (PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out Vector3 pos, out Quaternion rot))
        {
            float scale = PanelLayout.WorldScale;
            t.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 180f, 0f)); // +Z toward player
            t.localScale = Vector3.one * scale;
        }
        return true;
    }

    /// <summary>
    /// Rigid dock (the lag fix): parent the cluster under the tray ROOT and give it a
    /// FIXED local pose — the right-column anchor (constants above) plus the PER-BOARD seat
    /// (<paramref name="boardOff"/>) plus the configurable [RoundButtons] group offset
    /// (X sideways, Y up-board, Z toward the player) and the
    /// depth-correct proud seat along the board-face normal. The tray's own transform pass
    /// then carries the cluster with ZERO latency, exactly like the Confirm/Undo keycaps —
    /// no per-frame world-pose writes. Recomputed only when the attachment parameters
    /// change: fresh build (geometry change → rebuild → re-attach), tray/board switch
    /// (parent differs), a per-board cluster-scale change (the localFactor epsilon below),
    /// a per-board seat change (<paramref name="boardOff"/>) or a [RoundButtons] offset
    /// change — the last two WITHOUT a rebuild, so the group slides to its new seat instead
    /// of blinking out and back. The frame matches the old pose-follow EXACTLY:
    /// world rotation = mount.rotation × 180° yaw (+Z toward the player, +Y out of the
    /// board), world scale = mount lossy scale — both re-expressed as constants local to
    /// the tray root, so the docked look is bit-identical, just rigid.
    ///
    /// <para>THE PER-BOARD SEAT WAS THE MISSING TERM (user, hardware ModBuild 97: "Die Offsets bei
    /// den Überspringen-Tasten haben keinen Einfluss. Alles andere scheint zu funktionieren, aber
    /// die Offsets verändern nichts."). Before the rigid dock the cluster was a CHILD of
    /// ButtonClusterMount, so <c>[Cards] ClusterOffset_{board}</c> moved it for free by moving the
    /// mount. The lag fix reparented it to the tray root and carried over the mount's rotation and
    /// scale — but not its translation, which is the one thing that dial writes. The fresh log
    /// shows the consequence directly: ~25 "[Cards] Debug live-apply [Oak]: cluster offset …"
    /// lines (all three axes, walked out and back to zero) with no answering movement, while
    /// ClusterScale — which DOES survive, through <c>localFactor</c> — worked in the same minute.
    /// It arrives here as a delta off the mount's own transform (<c>PlayTray.ButtonClusterOffset</c>),
    /// never as a second read of <c>CardsConfig.ClusterOffset</c>, so it cannot be double-applied:
    /// PlayTray converts the config to meters exactly once, when it seats the mount.</para>
    /// </summary>
    private void AttachDocked(Transform t, Transform trayRoot, Transform mount, Vector3 boardOff)
    {
        float trayLossy = trayRoot.lossyScale.x;
        float mountLossy = mount.lossyScale.x;
        if (trayLossy < 1e-6f || mountLossy < 1e-6f)
            return; // degenerate mid-teardown scales — keep the last good attachment
        float localFactor = mountLossy / trayLossy; // 0.7 dock shrink × per-board ClusterScale
        // DEPTH-CORRECT proud seat: lift the cluster ClusterProudOffset along the board-face
        // normal toward the player (mount.up in world = tray-root-local -Z) so the lit opaque
        // caps and base plate stand clear of the raised board rim. The user's OffsetZ rides
        // the same outward axis (+ = further toward the player). The PER-BOARD seat adds RAW,
        // exactly as it does for every other [Cards] *Offset_{board} dial (PlayTray.SetPinOffset,
        // SetElementsLayout, SetDecisionLayout all write `Base + offset` straight into the mount's
        // localPosition) — same frame, same sign convention, so a player who has learned what
        // "Position Z +0.01" does to their rest plate gets the same motion here. All terms are
        // tray-root-local meters; only the proud offset scales with the dock factor, so it tracks
        // the rendered size.
        Vector3 cfgOff = ButtonTuning.TransientOffset;
        Vector3 outward = Quaternion.Inverse(trayRoot.rotation) * mount.up; // ≈ (0, 0, -1)
        Vector3 localPos = new Vector3(ColumnCenterX + cfgOff.x, ColumnCenterY + cfgOff.y, ColumnRootZ)
                           + boardOff
                           + outward * (ClusterProudOffset * localFactor + cfgOff.z);
        // THE GATE IS OVER THE WHOLE SOLVE, not just the scale. It used to compare the parent and
        // localFactor only, which meant a re-pose could only ever reach the transform by way of a
        // full teardown+rebuild (the [RoundButtons] path) — and for the per-board seat, which has
        // no rebuild trigger of its own, not at all. Comparing the RESULT keeps this a no-op on the
        // overwhelming majority of frames while making every seat dial live by construction: any
        // input that changes where the group belongs changes localPos, and nothing else can.
        //
        // THE DEAD-BAND IS 0.1 mm (1e-8 = the square of 1e-4 m), not an exact compare, and the
        // reason is `outward`: it is derived through two WORLD quaternions, so while the board is
        // being carried it can wobble in the last couple of float digits even though the local
        // relationship is fixed. That wobble is ~1e-9 m here — three orders below the band — while
        // the finest thing a player can dial is 5 mm, so nothing real is ever swallowed and no
        // board grab can turn the re-seat line below into a per-frame log storm.
        if (t.parent == trayRoot
            && Mathf.Abs(localFactor - _dockLocalFactor) < 1e-4f
            && (t.localPosition - localPos).sqrMagnitude < 1e-8f)
            return; // already rigidly attached at this pose
        // Same frame semantics as the old pose-follow: the mount's +Z points "away from the
        // player" (up the board), so the standard 180° yaw puts the cluster's +Z toward the
        // player; +Y comes OUT of the board (cap travel presses into the board).
        Quaternion localRot = Quaternion.Inverse(trayRoot.rotation)
                              * (mount.rotation * Quaternion.Euler(0f, 180f, 0f));

        bool reparented = t.parent != trayRoot;
        if (reparented)
            t.SetParent(trayRoot, worldPositionStays: false);
        t.localPosition = localPos;
        t.localRotation = localRot;
        t.localScale = Vector3.one * localFactor;
        _rootToLocal = 1f / localFactor; // RelayoutColumn's root-local → cluster-local budget factor
        _dockLocalFactor = localFactor;
        // The line fires on a RE-POSE as well as on the first attach, and prints the two seat
        // terms separately. That split is the whole point: the report this round answers was
        // "the offsets do nothing", and the only way to tell from a log which offset a build
        // honoured is to see both of them, named, next to the pose they produced.
        VRLog.Info("WorldUI", (reparented
                                  ? "ButtonCluster docked RIGIDLY under the tray root"
                                  : "ButtonCluster re-seated in place (no rebuild, no pop)") +
                              $" — dock scale {localFactor:F3}, board seat ([Cards] ClusterOffset) " +
                              $"({boardOff.x:F3}, {boardOff.y:F3}, {boardOff.z:F3}) m, group offset " +
                              $"([RoundButtons] Offset) ({cfgOff.x:F3}, {cfgOff.y:F3}, {cfgOff.z:F3}) m " +
                              $"→ tray-root-local pose ({localPos.x:F3}, {localPos.y:F3}, {localPos.z:F3}); " +
                              "board motion carries it with zero latency (no per-frame pose-follow).");
    }

    private void SetDockedLabels(bool docked)
    {
        _ready?.SetDocked(docked);
        _undo?.SetDocked(docked);
        _skip?.SetDocked(docked);
    }

    /// <summary>
    /// The PlayTray instance whose laser-target list holds our buttons (identity
    /// guard — register once per tray; the tray's per-frame loop skips dead or
    /// disabled colliders itself).
    /// </summary>
    private PlayTray? _laserTray;

    /// <summary>
    /// Docked clusters are laser targets too (poke AND laser on every board
    /// element — the board contract): the dominant hand's board laser
    /// (CardsDriver.UpdateBoardLaser) ray-tests the tray's registry and routes
    /// TriggerDown into <see cref="PhysicalButton.OnPoke"/>.
    /// </summary>
    private void RegisterTrayLaserTargets()
    {
        PlayTray? tray = PlayTray.Current;
        if (tray == null || ReferenceEquals(_laserTray, tray))
            return;
        _laserTray = tray;
        _ready?.RegisterLaserTarget(tray);
        _undo?.RegisterLaserTarget(tray);
        _skip?.RegisterLaserTarget(tray);
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            if (!visible)
                return;
        }
        _visible = visible;
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    // ---- click routing (see class doc for the per-button decision) -------------------------

    private static void ClickReady()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.readyButton == null)
            return;
        ClickUiButton(choreographer.readyButton.ButtonComponent);
    }

    private static void ClickUndo()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.m_UndoButton == null)
            return;
        ClickUiButton(choreographer.m_UndoButton.m_UndoButton);
    }

    private static void ClickSkip()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.m_SkipButton == null)
            return;
        ClickUiButton(choreographer.m_SkipButton.skipButton);
    }

    /// <summary>
    /// The game's own programmatic click (BaseButtons.clickButton pattern):
    /// Selectable.IsInteractable precheck + ExecuteEvents.pointerClickHandler.
    /// Extra gate: the mirrored UI lock (ExecuteEvents bypasses raycasters).
    /// </summary>
    private static void ClickUiButton(Selectable? button)
    {
        if (button == null || !button.gameObject.activeInHierarchy || !button.IsInteractable())
            return;
        if (CanvasConversion.IsLockedNow)
        {
            VRLog.Debug("WorldUI", "ButtonCluster: click swallowed — UI locked (modality respected).");
            return;
        }
        ExecuteEvents.Execute(button.gameObject,
            new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
    }

    // ---- one physical button ----------------------------------------------------------------

    /// <summary>
    /// A single pokeable 3D button: base + travelling cap + world TMP label.
    /// Poke events arrive via the Phase-2 <see cref="IPokeable"/> registration; the
    /// PokeInteractor supplies hover/click haptics only while the collider is enabled.
    /// </summary>
    private sealed class PhysicalButton : IPokeable
    {
        private GameObject _rootGo = null!;
        private Transform _cap = null!;
        private BoxCollider _collider = null!;
        private Renderer _capRenderer = null!;
        private Renderer _baseRenderer = null!;
        private TextMeshPro _label = null!;
        private System.Action _onClick = null!;
        private Color _accent;

        private float _capRestY;
        // Cap TOP face in root-local units (the outward/viewer-facing surface). The docked label
        // is seated a fixed margin PROUD of THIS (not the cap CENTER): the round cylinder cap is
        // taller than the shallow square/native cap, so the old center-relative offset left the
        // Round label nearly flush with its top face → at the board's diorama scale the proud gap
        // shrank under a millimetre and the co-planar TMP + opaque cap face z-fought per-eye under
        // stereo (the "flickers very strongly" report). Anchoring to the top gives both shapes the
        // same real proud gap. Set at build.
        private float _capTopLocalY;
        private float _pressT;       // 1 = fully pressed, decays to 0
        private bool _interactable = true;
        private string? _mirroredText;
        private Color _appliedColor;

        // Depth-fire press (user #6): the hand hovering in poke range; the cap follows its
        // fingertip penetration and the press fires only at ~90% of the full travel
        // (ButtonTuning.PressFireFraction), re-arming after it rose back past ~50%.
        // Laser+trigger presses stay immediate (OnPoke with a far-away fingertip).
        private VRHand? _hoverHand;
        private bool _depthArmed = true;

        // Press DEBOUNCE (user: keycaps double-trigger — port the PileStack.OnPoke fix). After a
        // press commits, no second press fires until the cap retracted past PressRearmFraction to
        // re-arm AND this shared cooldown elapsed; the laser path is cooldown-debounced too (never
        // dwelled). Composes with the depth-fire (the ~90% press stays the event; this blocks the
        // retract/re-entry and hover-flicker re-fire).
        private float _nextPressTime;

        // Throttle clock for the grip-gate refusal line (requirement (a) — physical presses need
        // the same hand's grip held; see the depth-fire in Animate).
        private float _nextGripGateLogAt;

        // Dust-dissolve hide / materialize-from-dust show (user #7). Logical hide is instant
        // (collider off, excluded from the column layout); only the visuals shrink out. Appear
        // reverses it: converging dust + a surface fade-in, in place (no scale pop).
        private bool _logicalVisible = true;
        private bool _everShown;     // suppress the dust burst for the initial state settling
        private float _dissolveLeft; // shrink-out countdown, seconds
        private float _appearLeft;   // materialize-from-dust fade-in countdown, seconds

        // ---- SURFACE-FADE WATCHDOG (2026-08-09 "the buttons were invisible, only the text") -----
        //
        // The cluster twin of the PlayTray.BoardButton defect — see the long root-cause header on
        // BoardButton's _showDeadline field for the full mechanism. Short version: the appear fade
        // below USED TO multiply the cap's colour by SmoothStep(0.15, 1, k), so while it ran the cap
        // body was at 15% of its colour (black on a dark board) while the TMP label — a different
        // renderer on its own material, untouched by the fade — kept drawing at full brightness. The
        // ONLY exit from that state is the countdown reaching zero inside Animate().
        //
        // THE MULTIPLY IS GONE (2026-08-09 round 2): the appear now runs the shared assembly ramp
        // (ButtonTuning.AssemblyColor), which cannot render a cap darker than its own rest colour at
        // any user tint. A stranded animation therefore strands a cap BRIGHT, not invisible. The
        // watchdog below is unchanged and still wanted — the strand is still a bug, it is simply no
        // longer able to produce the reported look.
        //
        // AND HERE THAT IS STRICTLY WORSE THAN ON THE BOARD BUTTONS, because Animate() is not a
        // MonoBehaviour Update: it is called from ButtonCluster.Tick, which RETURNS EARLY on every
        // tick where the cluster is not wanted or not placed (see Tick). A cap that is mid-fade when
        // the cluster stops being placed for a moment is simply left at 15% with nothing scheduled to
        // ever restore it — no clock stall required. The countdowns therefore run on the UNSCALED
        // clock like the rest of the mod AND carry a wall-clock deadline that force-completes them
        // (through the same completion path, so the exact colour is re-seated) on the next tick.
        private float _appearDeadline = float.PositiveInfinity;
        private float _dissolveDeadline = float.PositiveInfinity;
        private float _fadeStartedAt;

        /// <summary>Wall-clock grace before the watchdog force-completes a fade — mirror of
        /// <c>PlayTray.BoardButton.FadeWatchdogSlack</c>; a normal frame spike never trips it.</summary>
        private const float FadeWatchdogSlack = 0.35f;

        /// <summary>Throttle for the watchdog line (a relayout can force several caps at once).</summary>
        private static float _nextFadeHealLogAt;
        private Vector3 _slotScale = Vector3.one; // authoritative scale from SetSlot

        /// <summary>Fingertip contact radius — mirror of <c>PokeInteractor.FingertipRadius</c>.</summary>
        private const float FingertipRadius = 0.008f;

        /// <summary>
        /// Cluster-local margin the docked round/square label is held PROUD of the cap TOP face.
        /// FLOAT FIX (user #4 — "the text floats very visibly ABOVE the button"): the previous
        /// 10 mm over-corrected the earlier z-fight bug — on a ~9 mm-deep round puck a 10 mm gap
        /// equals the whole cap height, so the label read as a plane HOVERING a cap-depth above
        /// the face. Cut to 2 mm: the label now sits just barely off the face (reads as printed
        /// ON it), and it still does not flicker because it does not rely on the raw offset alone —
        /// the label's font-material renders in the Transparent queue (3000) with ZWrite OFF and
        /// ZTest LEqual AFTER the opaque BoardLit cap (Geometry queue, ZWrite ON), so it wins the
        /// composite even near-coplanar; this 2 mm only has to clear per-eye depth-buffer precision
        /// noise (sub-millimetre in world after the ~0.84 slot × 0.7 dock × 0.4 board shrink), which
        /// it does with margin. Board Confirm/Undo/gear/rest caps are a different label path
        /// (BoardButton labelZ, proud on the −Z cap FACE) and are untouched by this constant.
        /// </summary>
        internal const float DockedLabelProud = 0.002f;

        /// <summary>Live cap travel, cluster-local meters ([RoundButtons] Travel — authored default 8 mm).</summary>
        private static float TravelLocal => ButtonTuning.RoundCapTravel;

        // Docked label pose (test #19): home pose captured at build, restored on undock.
        private bool _docked;
        private bool _labelAnchored; // prefab LabelAnchor path keeps authoring authority
        private Vector3 _labelHomePos;
        private Quaternion _labelHomeRot;

        /// <summary>Authored cap radius (cluster-local meters) — <see cref="SetSlot"/> scales relative to it.</summary>
        internal float BaseRadius { get; private set; }

        /// <summary>
        /// LOGICAL visibility of this button right now (drives the column layout, user #8).
        /// A button mid-dust-dissolve is already logically hidden — the layout reflows
        /// immediately while its visuals finish shrinking out.
        /// </summary>
        internal bool VisibleNow => _rootGo != null && _logicalVisible;

        /// <summary>Multiplayer cap-STATE read seam: this cap is shown AND pressable right now
        /// (its dimmed-toward-dark-wood disabled look is what the negative case renders). Read by
        /// <see cref="ButtonCluster.BoardSkipEnabled"/> for the board-UI record's cap-state byte.</summary>
        internal bool InteractableNow => _rootGo != null && _logicalVisible && _interactable;

        /// <summary>True for the ONE cluster member a peer's remote control board mirrors — the
        /// docked turn-flow SKIP cap. Set by <see cref="ButtonCluster.Build"/>; the Ready/Undo
        /// twins are forced permanently off (see Tick) and the floating fallback cluster is not a
        /// board, so neither ever reports a press onto the wire.</summary>
        internal bool IsBoardSkip { get; set; }

        /// <summary>
        /// Column-slot assignment (user #8): move the button to <paramref name="localPos"/>
        /// and uniformly scale it so its cap radius reads <paramref name="radius"/> —
        /// mesh, collider and label all ride the transform, so poke/laser targets and
        /// text stay consistent at every auto-fit size. While a dissolve/appear anim runs,
        /// the scale is recorded but the animation keeps transform authority.
        /// </summary>
        public void SetSlot(Vector3 localPos, float radius)
        {
            if (_rootGo == null)
                return;
            Transform t = _rootGo.transform;
            if (t.localPosition != localPos)
                t.localPosition = localPos;
            float s = BaseRadius > 1e-5f ? radius / BaseRadius : 1f;
            _slotScale = Vector3.one * s;
            if (_dissolveLeft <= 0f && _appearLeft <= 0f && !Mathf.Approximately(t.localScale.x, s))
                t.localScale = _slotScale;
        }

        public static PhysicalButton Create(Transform parent, string name, Vector3 localPos,
            float radius, Color accent, System.Action onClick)
        {
            var button = new PhysicalButton { _onClick = onClick, _accent = accent, BaseRadius = radius };

            GameObject? prefab = WorldUIAssets.TryLoadPrefab($"Assets/Bundle/Table/Button_{name}.prefab")
                                 ?? WorldUIAssets.TryLoadPrefab("Assets/Bundle/Table/ReadyButton.prefab");
            if (prefab != null)
                button.BuildFromPrefab(prefab, parent, name, localPos, radius);
            else
                button.BuildProcedural(parent, name, localPos, radius);

            // DEPTH-CORRECT: no draw-on-top. The base/cap are lit opaque BoardLit meshes and
            // the label/native face keep their default ZTest LEqual, so the cluster depth-tests
            // like every other solid object — occluded by walls/geometry in FRONT of it, yet
            // fully visible sitting PROUD of the board face (see ClusterProudOffset in PlaceCluster).
            VRInteractables.RegisterPokeable(button, button._collider);
            return button;
        }

        private void BuildFromPrefab(GameObject prefab, Transform parent, string name, Vector3 localPos, float radius)
        {
            _rootGo = Object.Instantiate(prefab, parent, worldPositionStays: false);
            _rootGo.name = $"GloomhavenVR.Button_{name}";
            _rootGo.transform.localPosition = localPos;

            Transform? cap = _rootGo.transform.Find("Cap");
            _cap = cap != null ? cap : _rootGo.transform;
            _capRenderer = (_cap.GetComponentInChildren<Renderer>() ?? _rootGo.GetComponentInChildren<Renderer>())!;
            _baseRenderer = _rootGo.GetComponentInChildren<Renderer>()!;
            _collider = _cap.GetComponent<BoxCollider>() ?? _cap.gameObject.AddComponent<BoxCollider>();
            _capRestY = _cap.localPosition.y;
            _capTopLocalY = _capRestY; // prefab labels ride the LabelAnchor (SetDocked no-ops on _labelAnchored)
            CreateLabel(_rootGo.transform.Find("LabelAnchor"), radius);
        }

        private void BuildProcedural(Transform parent, string name, Vector3 localPos, float radius)
        {
            _rootGo = new GameObject($"GloomhavenVR.Button_{name}");
            _rootGo.transform.SetParent(parent, worldPositionStays: false);
            _rootGo.transform.localPosition = localPos;

            // Cap shape is CONFIG now (user #8): Round = the classic flattened-cylinder puck;
            // Square = a boxy keycap whose width/height/depth come from the cluster's OWN
            // [RoundButtons] geometry set (category split — the board keycaps have their own
            // [BoardButtons]/[BoardDashboard] sets and never share values with this group).
            // Independent side lengths make rectangular caps. Cluster-local axes while
            // docked: +X lateral (width), +Z down the board (height), +Y out of the board
            // (cap travel axis).
            bool roundShape = ButtonTuning.TransientRound;
            float capW = roundShape ? radius * 2f : ButtonTuning.RoundCapWidth;
            float capH = roundShape ? radius * 2f : ButtonTuning.RoundCapHeight;
            float capD = roundShape ? 0.009f : ButtonTuning.RoundCapDepth;
            if (!roundShape)
                BaseRadius = Mathf.Max(capW, capH) * 0.5f; // column layout tracks the real footprint

            // Base plate (flat box).
            GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            Object.Destroy(basePlate.GetComponent<Collider>());
            basePlate.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            basePlate.transform.localScale = roundShape
                ? new Vector3(radius * 2.4f, 0.012f, radius * 2.4f)
                : new Vector3(capW * 1.2f, 0.012f, capH * 1.2f);
            basePlate.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            _baseRenderer = basePlate.GetComponent<Renderer>();
            // DEPTH-CORRECT: lit opaque BoardLit (the PlayTray solid-keycap path) — writes depth
            // and depth-tests LEqual, so the base is occluded by walls in front and self-occludes
            // like a real object, instead of the old unlit Overlay forced to ZTest Always.
            // T4: dark-WOOD base plaque (matches the tray button surrounds; the shared
            // wood-grain _MainTex from NewKeycapMaterial gives it the carved surface).
            // ButtonTuning.CapWellColor, not a literal: SeatedCapColor floors the cap FACE against
            // this exact colour (2026-08-09 round 3), and a floor whose reference can drift is the
            // bug it was written to end.
            _baseRenderer.sharedMaterial = CreateLitMaterial(ButtonTuning.CapWellColor);

            // Travelling cap: a SMOOTH generated disc (Round) or a boxy keycap (Shape=Square).
            // ROUND FIX (user: "you can see the CORNERS in the 'round' buttons"): the round puck
            // was Unity's ~20-sided PrimitiveType.Cylinder — faceted at cap size. It is now the
            // shared 64-seg CardMesh.GetRoundCap disc (planar XY UVs → the carved-grain keycap
            // _MainTex still maps), authored at REAL size (diameter capW × full height 2·capD) and
            // rotated 90° about X so its face axis lands on +Y — the cap travel axis, viewer side
            // +Y — reproducing the cylinder's placement (top face at _capRestY + capD).
            GameObject cap;
            if (roundShape)
            {
                cap = new GameObject("Cap");
                cap.transform.SetParent(_rootGo.transform, worldPositionStays: false);
                cap.AddComponent<MeshFilter>().sharedMesh = Cards.CardMesh.GetRoundCap(capW, 2f * capD);
                cap.AddComponent<MeshRenderer>();
                cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // disc -Z (front) → +Y (viewer/top)
                cap.transform.localPosition = new Vector3(0f, 0.024f, 0f);
            }
            else
            {
                cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Cap";
                Object.Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(_rootGo.transform, worldPositionStays: false);
                cap.transform.localScale = new Vector3(capW, capD, capH); // cube: depth is full Y size
                cap.transform.localPosition = new Vector3(0f, 0.015f + capD * 0.5f, 0f); // cap bottom at the puck's 0.015 seat
            }
            _cap = cap.transform;
            _capRestY = _cap.localPosition.y;
            // Cap TOP face (viewer side): the round disc's half-height is capD (full height 2·capD);
            // the cube spans ±0.5×scale.y so its half-height is capD/2. The docked label seats a
            // fixed margin proud of this, keeping the same real gap on the taller round cap that the
            // shallow square cap already had (round-label flicker fix).
            _capTopLocalY = _capRestY + (roundShape ? capD : capD * 0.5f);
            _capRenderer = cap.GetComponent<Renderer>();
            _capRenderer.sharedMaterial = CreateLitMaterial(_accent); // lit opaque, depth-correct (see base)

            // ANTIQUE cap (user #5): NO game-default sprite face any more. The cluster
            // buttons previously wore the game's sampled 9-slice button sprite flat on
            // the cap — the one set of visible board buttons still showing "the game's
            // default look" while the gear/Confirm/Undo keycaps got the T4 antique
            // restyle. The cap now reads exactly like those: wood-grain lit keycap
            // (NewKeycapMaterial) in the worn accent colour + parchment ENGRAVED label
            // (StyleEngravedLabel below) — same colour family and style as the
            // VR-settings gear button.

            // Poke collider slightly proud of the cap (primitive box — poke contract).
            _collider = _rootGo.AddComponent<BoxCollider>();
            if (roundShape)
            {
                _collider.center = new Vector3(0f, 0.026f, 0f);
                _collider.size = new Vector3(radius * 2f, 0.03f, radius * 2f);
            }
            else
            {
                _collider.center = new Vector3(0f, _capRestY, 0f);
                _collider.size = new Vector3(capW, capD + 0.024f, capH);
            }

            CreateLabel(null, radius);
        }

        private void CreateLabel(Transform? anchor, float radius)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(anchor != null ? anchor : _rootGo.transform, worldPositionStays: false);
            if (anchor == null)
            {
                labelGo.transform.localPosition = new Vector3(0f, 0.012f, -(radius * 2.4f) * 0.5f - 0.012f);
                labelGo.transform.localRotation = Quaternion.Euler(55f, 180f, 0f);
            }
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            // Native label (test #25 item 3): the game's HUD font + parchment-gold
            // button-text colour when sampled; white otherwise.
            _label.color = NativeButtonSkin.HasFont ? NativeButtonSkin.LabelColor : Color.white;
            NativeButtonSkin.ApplyFont(_label);
            NativeButtonSkin.StyleEngravedLabel(_label); // T4: carved-into-the-cap label look
            // Draw the label above the native sprite face (test #26): both transparent,
            // ZWrite off — sorting order decides, and the face uses sortingOrder 1. Both
            // stay TINY (≤3) so the held-figure info panel host canvas (StatPanelSurface,
            // sortingOrder 10) composites over them; ApplyFont above additionally forced
            // the label's font-material instance depth-honest (queue 3000 + ZTest LEqual,
            // not the HUD asset's on-top 4003/Always), so real geometry occludes the text.
            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 3;
            _label.text = string.Empty;
            // The label mirrors the game's LOCALIZED button texts (SetState), whose
            // length varies per state/language — long strings previously wrapped past
            // the 0.05 m box and clipped (test #12). Fit: shrink/wrap inside the box.
            Core.TmpFit.Fit(_label, 0.24f, 0.07f, maxFontSize: 0.35f);
            // Undocked labels park IN FRONT of the base plate (free-floating); docked ones sit
            // on the opaque cap, where the plate lands a hair inside the cap and depth-fails
            // (invisible) — one registration is safe for both states.
            MrBacking.Label(_label);
            _labelAnchored = anchor != null;
            _labelHomePos = labelGo.transform.localPosition;
            _labelHomeRot = labelGo.transform.localRotation;
        }

        /// <summary>
        /// Lit, opaque, depth-writing material for the button base/cap — the same
        /// <c>GloomhavenVR/BoardLit</c> path PlayTray uses for its solid board keycaps
        /// (gear / follow-toggle / Confirm / Undo). Opaque Geometry queue + default ZTest
        /// LEqual + ZWrite on means the cluster depth-tests and self-occludes like every
        /// other solid object: occluded by walls/geometry in FRONT of it, visible sitting
        /// PROUD of the board face, and it no longer overpaints a card held before it.
        /// Falls back to the flat material if the BoardLit shader is unavailable.
        /// </summary>
        private static Material CreateLitMaterial(Color color)
        {
            Shader? lit = Cards.PlayTray.BoardLitShader();
            if (lit != null)
                // Task #5a: route through the shared keycap-material helper so the cluster's
                // Undo|Ready|Skip caps get the same carved-grain _MainTex as the tray keycaps
                // (grayscale grain × colour) when the bundle ships it — plain tint otherwise.
                return Cards.PlayTray.NewKeycapMaterial(lit, color);
            return WorldUIAssets.CreateFlatMaterial(color); // overlay:false → Standard/Sprites fallback
        }

        /// <summary>
        /// Docked label pose (test #19): at the table edge the label is a tilted
        /// sign BEHIND the cap — on the control board that sign would lie across
        /// the tray's slot captions, so docked labels flip flat ONTO the cap
        /// (readable from the board's viewer side, text-up pointing up the board)
        /// and re-fit into the 0.11 button pitch. Procedural labels only; a bundle
        /// prefab's LabelAnchor keeps authoring authority.
        /// </summary>
        public void SetDocked(bool docked)
        {
            if (_docked == docked)
                return;
            _docked = docked;
            if (_label == null || _labelAnchored)
                return;
            Transform lt = _label.transform;
            if (docked)
            {
                // Flat ONTO the cap, held DockedLabelProud off the cap TOP face (not the cap
                // CENTER as before): the round cylinder cap's top is capD above centre vs the
                // square cap's capD/2, so a centre-relative offset left the round label almost
                // flush and it z-fought the opaque cap face per-eye under stereo (the flicker).
                // Anchoring to the real top gives both shapes the same proud gap; the label's
                // material already renders after the cap (queue 3000 + ZTest LEqual via
                // NativeButtonSkin.StyleEngravedLabel, sortingOrder 3), so proud + LEqual is stable.
                lt.localPosition = new Vector3(0f, _capTopLocalY + DockedLabelProud, 0f);
                lt.localRotation = Quaternion.Euler(90f, 180f, 0f);
                Core.TmpFit.Fit(_label, 0.105f, 0.045f, maxFontSize: 0.30f);
            }
            else
            {
                lt.localPosition = _labelHomePos;
                lt.localRotation = _labelHomeRot;
                Core.TmpFit.Fit(_label, 0.24f, 0.07f, maxFontSize: 0.35f);
            }
        }

        /// <summary>
        /// Watchdog line for the 2026-08-09 "the buttons were invisible, only their text was still
        /// there" report: a cap animation that outlived its authored duration plus
        /// <see cref="FadeWatchdogSlack"/> was force-completed. Names the cap, the animation and the
        /// WALL-CLOCK seconds it actually spent faded, so the window shows up in the next hardware
        /// log instead of depending on the player catching it. Throttled.
        /// </summary>
        private void LogFadeForced(string anim, float authored)
        {
            float held = Time.unscaledTime - _fadeStartedAt;
            if (Time.unscaledTime < _nextFadeHealLogAt)
                return;
            _nextFadeHealLogAt = Time.unscaledTime + 0.5f;
            VRLog.Warn("WorldUI", $"KEYCAP FADE HEALED: '{(_rootGo != null ? _rootGo.name : "cluster cap")}' " +
                $"was still mid-'{anim}' after {held:F2} s of WALL-CLOCK time (authored {authored:F2} s " +
                $"+ {FadeWatchdogSlack:F2} s slack; Time.timeScale {Time.timeScale:F2}) — force-completed " +
                "and the exact cap colour re-seated. Since the assembly ramp replaced the " +
                "multiply-toward-black fade, a stranded cap sits BRIGHTER than its rest colour (dust), " +
                "never invisible — so this line no longer explains a 'button gone, text still floating' " +
                "report; Animate() is skipped on any tick the cluster is not placed, so this is still " +
                "the path that can strand one, and it is still worth knowing about.");
        }

        /// <summary>Register with the tray's board-laser registry (docked mode).</summary>
        public void RegisterLaserTarget(PlayTray tray) => tray.RegisterLaserTarget(_collider, this);

        /// <summary>Attributable name for laser-click logs (plain class, no MonoBehaviour name).</summary>
        public override string ToString() => _rootGo != null ? _rootGo.name : nameof(PhysicalButton);

        public void Destroy()
        {
            VRInteractables.UnregisterPokeable(this);
            if (_rootGo != null)
                Object.Destroy(_rootGo);
        }

        // ---- mirroring -------------------------------------------------------------------

        public void MirrorReady(ReadyButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            bool visible = real.gameObject.activeInHierarchy && real.IsVisibility;
            bool canUse = visible && real.IsInteractable && !locked
                          && !real.warningMask.gameObject.activeSelf;
            // Worn-BRASS accent while the state is a "confirm" flavor (>= CONTINUE commits
            // StepComplete) — T4: antique brass instead of the old saturated gold.
            Color accent = real.buttonState >= ReadyButton.EButtonState.EREADYBUTTONCONTINUE
                ? new Color(0.68f, 0.52f, 0.24f)
                : _accent;
            SetState(visible, canUse, real.buttonText != null ? real.buttonText.text : null, accent);
        }

        public void MirrorUndo(UndoButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            bool visible = real.gameObject.activeInHierarchy;
            bool canUse = visible && real.m_UndoButton != null && real.m_UndoButton.interactable && !locked;
            SetState(visible, canUse, real.m_ButtonText != null ? real.m_ButtonText.text : null, null);
        }

        public void MirrorSkip(SkipButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            // VISIBILITY IS THE GAME'S OWN, NOT THE GAMEOBJECT'S (MP leak fix, hardware test
            // 2026-08-04: "der 'Bewegen überspringen'-Button erschien auf MEINEM Board obwohl
            // mein MITSPIELER die Bewegung ausführte"). The Choreographer toggles m_SkipButton
            // ACTIVE on EVERY client whenever a PLAYER actor gets a skippable step
            // (Choreographer.cs:4353 `Toggle(message.m_ActorSpawningMessage.Type ==
            // CActor.EType.Player, GUI_SKIP_MOVEMENT, …)` — no owner gate); what hides it for
            // the non-acting players is the per-frame interactability check driving the
            // CanvasGroup ALPHA to 0 (SkipButton.CheckButtonInteractability →
            // `ChangeCanvasAlpha(skipButton.interactable && canvasGroup.interactable)`, where
            // skipButton.interactable requires Choreographer.ThisPlayerHasTurnControl —
            // ButtonOnBlockingPanel.cs). Vanilla 2D therefore renders NOTHING on the peer's
            // screen while the flag object is active. Mirroring activeInHierarchy alone is what
            // put a (dimmed) skip cap on the local board for the PEER's movement — so the mirror
            // now requires the game's own visibility too, at the game's own threshold (its base
            // IsInteractable() reads `canvasGroup.alpha > 0.5f`). This also feeds BoardSkipShown,
            // so the board-UI wire bit stops claiming a skip control the owner cannot see.
            bool visible = real.gameObject.activeInHierarchy
                           && real.canvasGroup != null && real.canvasGroup.alpha > 0.5f;
            bool canUse = visible && real.skipButton != null && real.skipButton.interactable && !locked;
            SetState(visible, canUse, real.buttonText != null ? real.buttonText.text : null, null);
        }

        private void SetState(bool visible, bool interactable, string? text, Color? accentOverride)
        {
            if (_rootGo == null)
                return;
            if (visible != _logicalVisible)
            {
                _logicalVisible = visible;
                if (visible)
                {
                    // Cancel a running dissolve, restore the layout scale, materialize from dust.
                    _dissolveLeft = 0f;
                    _rootGo.transform.localScale = _slotScale;
                    if (!_rootGo.activeSelf)
                        _rootGo.SetActive(true);
                    // APPEAR = MATERIALIZE FROM DUST (user: emerge from dust, matched to the crumble —
                    // NOT a scale pop): converging dust motes settle onto the cap while its surface
                    // fades up to full colour, IN PLACE (see Animate). Only once the button has been
                    // shown before (suppresses the build-then-settle storm) and while the animation is
                    // enabled ([ButtonAnim] Enable). Input is live immediately.
                    _appearLeft = _everShown && ButtonTuning.ButtonAnimEnabled ? ButtonTuning.AppearSeconds : 0f;
                    // Watchdog armed on the UNSCALED wall clock (see the field header).
                    _fadeStartedAt = Time.unscaledTime;
                    _appearDeadline = _appearLeft > 0f
                        ? _fadeStartedAt + _appearLeft + FadeWatchdogSlack : float.PositiveInfinity;
                    _dissolveDeadline = float.PositiveInfinity;
                    _collider.enabled = _interactable; // re-sync after the hide forced it off
                    if (_appearLeft > 0f)
                    {
                        // Paint frame ZERO of the assembly here, not on the next Animate: waiting
                        // would show one frame of the finished cap before it starts arriving — a
                        // pop in front of the anti-pop animation.
                        if (_capRenderer != null)
                            _capRenderer.sharedMaterial.color =
                                ButtonTuning.AssemblyColor(_appliedColor,
                                    ButtonTuning.AssemblyPhase(0f, ButtonTuning.CapPart.Top));
                        ButtonTuning.LogAnim(_rootGo.name, "appear (assemble out of dust)");
                        if (ButtonTuning.AppearParticlesEnabled)
                            ButtonDissolveFx.PlayMaterialize(_cap.position, _rootGo.transform.up,
                                BaseRadius * 2f * Mathf.Abs(_rootGo.transform.lossyScale.x),
                                _appliedColor);
                    }
                }
                else
                {
                    // LOGICAL hide is immediate (user #7): input off now, layout reflows now
                    // (VisibleNow is false already) — only the visuals shrink out while the
                    // pooled dust burst sweeps the cap away in its face color.
                    _hoverHand = null;
                    _depthArmed = true;
                    _collider.enabled = false;
                    if (_everShown && _rootGo.activeInHierarchy && ButtonTuning.ButtonAnimEnabled)
                    {
                        _dissolveLeft = ButtonTuning.DissolveSeconds;
                        _fadeStartedAt = Time.unscaledTime;
                        _dissolveDeadline = _fadeStartedAt + _dissolveLeft + FadeWatchdogSlack;
                        _appearDeadline = float.PositiveInfinity;
                        ButtonTuning.LogAnim(_rootGo.name, "disappear (dust dissolve)");
                        ButtonDissolveFx.Play(_cap.position, _rootGo.transform.up,
                            BaseRadius * 2f * Mathf.Abs(_rootGo.transform.lossyScale.x),
                            _appliedColor);
                    }
                    else
                    {
                        _rootGo.SetActive(false); // initial settling / hidden cluster / anim off — silent pop
                    }
                }
            }
            if (!_logicalVisible)
                return;

            if (_interactable != interactable)
            {
                _interactable = interactable;
                // Disabled: dimmed, and the collider goes away so the PokeInteractor
                // produces neither events nor haptics.
                _collider.enabled = interactable;
            }

            // ANTIQUE cap colours (user #5 — the VR-settings-gear style): worn accent
            // wood-grain when usable; disabled sinks toward DARK WOOD (an unlit carved
            // plaque) instead of multiplying toward black — the grain texture stays
            // readable, the state contrast (parchment-warm available vs dark-wood
            // disabled) stays clear. The game-default sprite face is gone (see Build).
            Color baseColor = accentOverride ?? _accent;
            Color applied = interactable
                ? baseColor
                : Color.Lerp(baseColor, new Color(0.17f, 0.13f, 0.09f), 0.75f);
            // USER DEBUG OPTION: [ButtonColors] ClusterCapTint (multiplier, default white = no change)
            // lets the user darken/re-hue the round-phase caps live so the label reads over them.
            applied *= ButtonTuning.ClusterCapTint;
            // SEATED (2026-08-09 round 3 — "die buttons waren unsichtbar und nur der text darauf
            // sichtbar"). The line above is the one that made the comment two lines up untrue: the
            // disabled look deliberately sinks toward DARK WOOD "instead of multiplying toward
            // black", and then this multiply took it toward black anyway. At the user's 0.5 cluster
            // tint the disabled cap lands near (0.085, 0.065, 0.045) against a
            // ButtonTuning.CapWellColor (0.15, 0.12, 0.08) base plate — darker than its own recess.
            // A cluster cap is a SINGLE material: unlike the board keycaps it has no bright bevel
            // ring to keep a silhouette, so it goes uniformly black under a fully lit label, which
            // is the report verbatim. SeatedCapColor is a per-channel Max, so every enabled look the
            // player configured comes back bit-for-bit unchanged.
            applied = ButtonTuning.SeatedCapColor(applied);
            if (applied != _appliedColor)
            {
                _appliedColor = applied;
                // A state change that lands DURING an assembly re-aims the ramp instead of writing
                // the settled colour over it — otherwise the cap flashes finished inside its own
                // arrival (the twin of BoardButton.UpdateColor's re-aim).
                _capRenderer.sharedMaterial.color = _appearLeft > 0f
                    ? ButtonTuning.AssemblyColor(applied, ButtonTuning.AssemblyPhase(
                        1f - Mathf.Clamp01(_appearLeft / ButtonTuning.AppearSeconds), ButtonTuning.CapPart.Top))
                    : applied;
            }
            if (_label != null)
            {
                Color labelBase = NativeButtonSkin.HasFont ? NativeButtonSkin.LabelColor : Color.white;
                _label.color = interactable ? labelBase : new Color(labelBase.r, labelBase.g, labelBase.b, 0.35f);
                // Tofu fix (user report 2026-08-04): the mirrored game string may carry a glyph
                // this cluster's harvested font cannot render — strip it (whitespace-collapsed)
                // rather than show TMP's hollow box. Same-instance fast path when clean, so the
                // reference compare below keeps its allocation-free steady state.
                text = NativeButtonSkin.SanitizeLabel(_label, text);
                // Reference compare first: the game only reassigns the string on change.
                if (!ReferenceEquals(text, _mirroredText) && text != _mirroredText)
                {
                    _mirroredText = text;
                    _label.text = text ?? string.Empty;
                    if (_label.font == null)
                    {
                        // Late font pickup goes through the skin so the depth-honest
                        // material fix (queue 3000 + ZTest LEqual) rides the new font too.
                        NativeButtonSkin.ApplyFont(_label);
                        NativeButtonSkin.StyleEngravedLabel(_label); // T4: engraved look rides along
                    }
                }
            }
        }

        /// <summary>
        /// Cap animation (code-driven, no Animator), one call per cluster tick:
        /// dust-dissolve shrink-out / materialize-from-dust fade-in (user #7), finger-follow cap
        /// travel and the DEPTH-FIRE press (user #6) — the press fires exactly when
        /// the fingertip has pushed the cap to ~90% of its full travel, re-arms after
        /// it rose back past ~50%. Releasing early = no fire, the cap springs back.
        /// </summary>
        public void Animate()
        {
            if (_rootGo == null)
                return;

            // Dissolve shrink-out: logically hidden already — finish visuals, deactivate.
            if (_dissolveLeft > 0f)
            {
                // UNSCALED clock + wall-clock deadline (see the _appearDeadline field header).
                _dissolveLeft -= Time.unscaledDeltaTime;
                if (_dissolveLeft > 0f && Time.unscaledTime >= _dissolveDeadline)
                {
                    LogFadeForced("dust dissolve", ButtonTuning.DissolveSeconds);
                    _dissolveLeft = 0f;
                }
                float k = Mathf.Max(0f, _dissolveLeft / ButtonTuning.DissolveSeconds);
                _rootGo.transform.localScale = _slotScale * k;
                // Same assembly curve, run backwards — the cap crumbles back into the warm dust it
                // was built out of instead of shrinking as a dark chip (see ButtonTuning's block
                // comment; brightness here is purely additive, so no tint can make it vanish early).
                if (_capRenderer != null)
                    _capRenderer.sharedMaterial.color = ButtonTuning.AssemblyColor(_appliedColor,
                        ButtonTuning.AssemblyPhase(k, ButtonTuning.CapPart.Top));
                if (_dissolveLeft <= 0f)
                {
                    _dissolveDeadline = float.PositiveInfinity;
                    _rootGo.transform.localScale = _slotScale; // restore for the next show
                    if (_capRenderer != null)
                        _capRenderer.sharedMaterial.color = _appliedColor; // leave the exact colour behind
                    _rootGo.SetActive(false);
                }
                return;
            }
            if (!_rootGo.activeSelf)
                return;
            _everShown = true;

            // Assemble-out-of-dust appear (user: emerge from dust, NOT a scale pop; purely visual —
            // input is live from frame one). The cap stays at its layout scale IN PLACE while its
            // OPAQUE surface cools out of the warm parchment-brass dust into its applied colour,
            // under the converging dust cloud. On completion it snaps back to the exact colour.
            if (_appearLeft > 0f)
            {
                // UNSCALED clock + wall-clock deadline. Reaching zero here is the ONLY thing that
                // restores the cap's real colour, and this method is skipped entirely on any tick
                // where the cluster is not placed — so without the deadline a cap could sit
                // mid-assembly under a fully readable label indefinitely. (Before the assembly ramp
                // replaced the multiply-toward-black fade, "mid-assembly" meant 15% brightness, i.e.
                // invisible; now it means too bright. The deadline stays either way.)
                _appearLeft -= Time.unscaledDeltaTime;
                if (_appearLeft > 0f && Time.unscaledTime >= _appearDeadline)
                {
                    LogFadeForced("assemble-out-of-dust", ButtonTuning.AppearSeconds);
                    _appearLeft = 0f;
                }
                float k = 1f - Mathf.Max(0f, _appearLeft / ButtonTuning.AppearSeconds);
                _rootGo.transform.localScale = _slotScale; // materialize in place — no grow/scale pop
                if (_capRenderer != null)
                    _capRenderer.sharedMaterial.color = ButtonTuning.AssemblyColor(_appliedColor,
                        ButtonTuning.AssemblyPhase(k, ButtonTuning.CapPart.Top));
                if (_appearLeft <= 0f)
                {
                    _appearDeadline = float.PositiveInfinity;
                    _rootGo.transform.localScale = _slotScale;
                    if (_capRenderer != null)
                        _capRenderer.sharedMaterial.color = _appliedColor; // exact colour restored
                }
            }

            if (_pressT > 0f)
                _pressT = Mathf.Max(0f, _pressT - Time.deltaTime * 6f);

            // Finger-follow + depth-fire (user #6): the cap visually tracks the fingertip
            // penetration; the click commits only at the bottom of the travel.
            float follow = _hoverHand != null && _interactable ? FollowDepth01(_hoverHand) : 0f;
            if (_hoverHand != null && _interactable)
            {
                if (follow >= ButtonTuning.PressFireFraction)
                {
                    // GRIP CHORD (hardware MP test 2026-08, requirement (a)): the cluster's
                    // Ready/Undo/Skip caps are BOARD buttons, so their physical fingertip press
                    // commits only while the SAME hand's grip is held — the identical chord the
                    // fingertip-on-tile ping and the tray keycaps (PlayTray BoardButton) require.
                    // The cap still follows the finger; laser presses (OnPoke with a far
                    // fingertip) stay grip-free. Throttled Info line so a "did not react"
                    // report is answerable from the log.
                    //
                    // EMPTY HAND added 2026-08 with the "alle buttons" round (the report that put
                    // the same chord on the decision dock, Hands.Interact.PokeInteractor
                    // .PressAllowed): the grip also GRABS, so a carrying hand pressed the grip to
                    // carry, not to press — dragging the board this cluster is docked to must not
                    // fire the caps the dragging hand's own fingertip sweeps. Same second half
                    // BoardPick.TryNearPick has always had.
                    if (!_hoverHand.GripPressed || _hoverHand.Grabber.Held != null)
                    {
                        if (Time.unscaledTime >= _nextGripGateLogAt)
                        {
                            _nextGripGateLogAt = Time.unscaledTime + 1f;
                            VRLog.Info("WorldUI", $"{_rootGo.name} poke WITHHELD ({_hoverHand.Side}) — " +
                                                  "physical board-button presses require the same " +
                                                  "hand's GRIP held AND that hand to be empty (grip " +
                                                  $"{(_hoverHand.GripPressed ? "held" : "open")}, hand " +
                                                  $"{(_hoverHand.Grabber.Held != null ? "carrying something" : "empty")}; " +
                                                  "accidental-press guard); laser clicks are unaffected.");
                        }
                    }
                    // DEBOUNCE (user): fire only when re-armed AND past the shared cooldown, so a
                    // retract-then-push or a hover flicker inside one poke cannot double-fire.
                    else if (_depthArmed && Time.unscaledTime >= _nextPressTime)
                    {
                        _depthArmed = false;
                        Fire(_hoverHand, "poke-depth");
                    }
                }
                else if (follow <= ButtonTuning.PressRearmFraction)
                {
                    _depthArmed = true;
                }
            }
            else
            {
                _depthArmed = true;
            }

            float depth01 = Mathf.Max(follow, _pressT > 0f ? Mathf.Sin(_pressT * Mathf.PI) : 0f);
            float y = _capRestY - TravelLocal * depth01;
            if (Mathf.Approximately(_cap.localPosition.y, y))
                return;
            Vector3 p = _cap.localPosition;
            p.y = y;
            _cap.localPosition = p;
        }

        /// <summary>
        /// Fingertip penetration normalised to 0..1 of the cap travel (the BoardButton
        /// math): penetration = FingertipRadius·handScale − distance(tip → collider),
        /// converted through the button's world scale on the travel axis (+Y).
        /// Pure per-frame function — framerate independent, no accumulation.
        /// </summary>
        private float FollowDepth01(VRHand hand)
        {
            if (_collider == null || !_collider.enabled || !hand.HasPose)
                return 0f;
            Vector3 tip = hand.Rig.IndexTip.position;
            float dist = Vector3.Distance(tip, _collider.ClosestPoint(tip));
            float penetration = FingertipRadius * hand.WorldScale - dist;
            if (penetration <= 0f)
                return 0f;
            float travelWorld = TravelLocal * Mathf.Abs(_rootGo.transform.lossyScale.y);
            return travelWorld > 1e-6f ? Mathf.Clamp01(penetration / travelWorld) : 0f;
        }

        /// <summary>Is this OnPoke a physical fingertip contact (vs. a laser TriggerDown routed here)?</summary>
        private bool IsFingerContact(VRHand hand)
        {
            if (_collider == null || !hand.HasPose)
                return false;
            Vector3 tip = hand.Rig.IndexTip.position;
            return Vector3.Distance(tip, _collider.ClosestPoint(tip)) <= 0.02f * hand.WorldScale;
        }

        // ---- IPokeable -------------------------------------------------------------------

        public void OnPokeEnter(VRHand hand) => _hoverHand = hand; // arm the finger-follow/depth-fire

        public void OnPokeExit(VRHand hand)
        {
            if (ReferenceEquals(hand, _hoverHand))
                _hoverHand = null; // cap springs back via Animate; depth-fire re-arms
        }

        public void OnPoke(VRHand hand)
        {
            if (!_interactable)
                return;
            // DEPTH-FIRE (user #6): a fingertip CONTACT no longer fires — Animate tracks
            // the penetration and commits at ~90% of the cap travel. Only the laser path
            // (CardsDriver routes TriggerDown here with the fingertip nowhere near the
            // collider) keeps the immediate press.
            if (IsFingerContact(hand))
            {
                _hoverHand = hand;
                return;
            }
            // Laser press: immediate, but cooldown-debounced (trigger bounce + cross-path
            // poke+laser double-fire within the shared window). Never dwelled.
            if (Time.unscaledTime < _nextPressTime)
                return;
            Fire(hand, "laser");
        }

        /// <summary>
        /// Commit a press (depth-fire or laser): stamp the debounce cooldown, kick the cap
        /// dip, haptic + log, then invoke. Single entry so both paths share the cooldown.
        /// </summary>
        private void Fire(VRHand hand, string source)
        {
            _nextPressTime = Time.unscaledTime + ButtonTuning.PokePressCooldownSeconds;
            _pressT = 1f;
            hand.SendHaptic(HapticPreset.ClickPulse);
            // MULTIPLAYER (1:1 ruling, keycap ANIMATIONS): publish the press edge so the mirrored
            // SKIP cap on every peer's copy of this board dips with this one. Only the DOCKED skip
            // is mirrored at all (MirrorReady/MirrorUndo force the twins off), so only it reports;
            // the floating fallback cluster and the two dead twins stay silent by construction.
            if (IsBoardSkip && BoardSkipShown)
                Cards.BoardCapPress.Report(Net.NetProtocol.CapPressSkip);
            VRLog.Info("WorldUI", $"{_rootGo.name} pressed (source={source}, {hand.Side}).");
            _onClick();
        }
    }
}
