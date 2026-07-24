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
/// changes (ButtonTuning.Version rebuild, tray/board switch, per-board cluster scale). A
/// tray teardown destroys the parented cluster with it — Tick detects the dead root,
/// releases the poke registrations and lazily rebuilds against the next tray.
/// Labels flip flat onto the caps there (the table-edge label sign would lie
/// across the slot captions), the buttons register as tray laser targets (poke AND
/// laser on every board element — the board contract), and the cluster hides with
/// the tray. The floating table-edge slot remains the no-tray fallback.
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
    // reading it here would double-apply it. 10 mm matches PlayTray's proud magnitude
    // (FixedProudZ = 5 mm) with generous headroom over the mount's own 6 mm; the caps already
    // stand ~24 mm proud so this never reads as "floating". Scaled by the mount's world scale
    // (rig × grab × 0.7 dock) so it tracks the cluster's rendered size on every board variant.
    private const float ClusterProudOffset = 0.010f;

    private GameObject? _root;
    private PhysicalButton? _ready;
    private PhysicalButton? _undo;
    private PhysicalButton? _skip;
    private bool _visible;

    // Right-column layout state (user #8).
    private bool _dockedNow;
    private float _rootToLocal = 1f / 0.7f; // root-local → cluster-local unit factor (mount carries the 0.7 dock scale)
    private float _dockLocalFactor = -1f;   // tray-root-local dock scale the rigid attach was computed at
    private int _tuningVersion;             // ButtonTuning pull-based live-apply (rebuild on change)
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
            return;
        }

        // Rigid-parenting teardown seam: the cluster root lives UNDER the tray root while
        // docked, so a tray/board teardown destroyed it externally. Release the stale
        // PhysicalButtons (poke registrations, laser bookkeeping) before rebuilding.
        if (_root == null && _ready != null)
            Shutdown();

        // ButtonTuning live-apply (user #8/#9): ANY [RoundButtons] entry change (offsets,
        // shape, cap size, W/H/D/travel) rebuilds the cluster's buttons from scratch —
        // cheap (three buttons) and covers every knob with one path; the rebuild also
        // re-attaches the rigid dock pose with the fresh offsets.
        ButtonTuning.Bind();
        if (_root != null && _tuningVersion != ButtonTuning.Version)
            Shutdown();

        if (_root == null)
            Build();
        if (_root == null)
            return;

        bool placed = PlaceCluster();
        SetVisible(placed);
        if (!placed)
            return;

        bool locked = CanvasConversion.IsLockedNow;
        // Button layout policy: confirmations/undo live ONLY on the right-hand board pads now
        // (the mod Confirm/Undo on the control board). The center cluster's Ready + Undo twins
        // are forced permanently OFF (null → hidden) so there is never a duplicate "Fortfahren"/
        // Undo here. ONLY Skip stays mirrored (it has no right-pad equivalent and only appears
        // when the game marks the step skippable). The no-tray floating fallback is unaffected —
        // it still shows this cluster, but Ready/Undo are intentionally hidden there too.
        _ready!.MirrorReady(null, locked);
        _undo!.MirrorUndo(null, locked);
        _skip!.MirrorSkip(choreographer!.m_SkipButton, locked);

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

    public void Shutdown()
    {
        _ready?.Destroy();
        _undo?.Destroy();
        _skip?.Destroy();
        _ready = _undo = _skip = null;
        _laserTray = null; // a rebuilt cluster must re-register its laser targets
        _dockedNow = false;
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

        // Mod layer (render-only — pokes go through the VRInteractables registry).
        VRLayers.Apply(_root);
        _tuningVersion = ButtonTuning.Version; // fresh build reflects current config
        VRLog.Info("WorldUI", $"ButtonCluster geometry config applied — {ButtonTuning.Describe()}.");
        VRLog.Info("WorldUI", "ButtonCluster built (Undo | Ready | Skip) — DEPTH-CORRECT: lit opaque " +
                              $"BoardLit caps at natural ZTest LEqual, seated {ClusterProudOffset * 1000f:0} mm " +
                              "proud of the board face; labels depth-honest (per-label font-material instance, " +
                              "queue 3000 + ZTest LEqual). ANTIQUE caps (user #5): wood-grain keycap + engraved " +
                              "parchment label, the VR-settings-gear style (game-default sprite face removed). " +
                              "Docked layout (user #8): RIGHT-side auto-fit column beside the Undo/gear pads.");
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
            AttachDocked(t, trayRoot, mount);
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
    /// FIXED local pose — the right-column anchor (constants above) plus the configurable
    /// [RoundButtons] group offset (X sideways, Y up-board, Z toward the player) and the
    /// depth-correct proud seat along the board-face normal. The tray's own transform pass
    /// then carries the cluster with ZERO latency, exactly like the Confirm/Undo keycaps —
    /// no per-frame world-pose writes. Recomputed only when the attachment parameters
    /// change: fresh build (config change → Version rebuild → re-attach with new offsets),
    /// tray/board switch (parent differs) or a per-board cluster-scale change (the
    /// localFactor epsilon below). The frame matches the old pose-follow EXACTLY:
    /// world rotation = mount.rotation × 180° yaw (+Z toward the player, +Y out of the
    /// board), world scale = mount lossy scale — both re-expressed as constants local to
    /// the tray root, so the docked look is bit-identical, just rigid.
    /// </summary>
    private void AttachDocked(Transform t, Transform trayRoot, Transform mount)
    {
        float trayLossy = trayRoot.lossyScale.x;
        float mountLossy = mount.lossyScale.x;
        if (trayLossy < 1e-6f || mountLossy < 1e-6f)
            return; // degenerate mid-teardown scales — keep the last good attachment
        float localFactor = mountLossy / trayLossy; // 0.7 dock shrink × per-board ClusterScale
        if (t.parent == trayRoot && Mathf.Abs(localFactor - _dockLocalFactor) < 1e-4f)
            return; // already rigidly attached with current parameters

        // DEPTH-CORRECT proud seat: lift the cluster ClusterProudOffset along the board-face
        // normal toward the player (mount.up in world = tray-root-local -Z) so the lit opaque
        // caps and base plate stand clear of the raised board rim. The user's OffsetZ rides
        // the same outward axis (+ = further toward the player). Both are tray-root-local
        // meters; the proud offset scales with the dock factor so it tracks the rendered size.
        Vector3 cfgOff = ButtonTuning.TransientOffset;
        Vector3 outward = Quaternion.Inverse(trayRoot.rotation) * mount.up; // ≈ (0, 0, -1)
        Vector3 localPos = new Vector3(ColumnCenterX + cfgOff.x, ColumnCenterY + cfgOff.y, ColumnRootZ)
                           + outward * (ClusterProudOffset * localFactor + cfgOff.z);
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
        if (reparented)
            VRLog.Info("WorldUI", "ButtonCluster docked RIGIDLY under the tray root (fixed local pose, " +
                                  $"dock scale {localFactor:F3}, offset ({cfgOff.x:F3}, {cfgOff.y:F3}, {cfgOff.z:F3}) m) — " +
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

        // Dust-dissolve hide / quick scale-in show (user #7). Logical hide is instant
        // (collider off, excluded from the column layout); only the visuals shrink out.
        private bool _logicalVisible = true;
        private bool _everShown;     // suppress the dust burst for the initial state settling
        private float _dissolveLeft; // shrink-out countdown, seconds
        private float _appearLeft;   // scale-in countdown, seconds
        private Vector3 _slotScale = Vector3.one; // authoritative scale from SetSlot

        /// <summary>Fingertip contact radius — mirror of <c>PokeInteractor.FingertipRadius</c>.</summary>
        private const float FingertipRadius = 0.008f;

        /// <summary>Root-local margin the docked label is held PROUD of the cap top face (flicker fix — see <see cref="_capTopLocalY"/>).</summary>
        private const float DockedLabelProud = 0.010f;

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
            _baseRenderer.sharedMaterial = CreateLitMaterial(new Color(0.15f, 0.12f, 0.08f));

            // Travelling cap (squashed cylinder, or boxy keycap when Shape=Square).
            GameObject cap = GameObject.CreatePrimitive(
                roundShape ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            cap.name = "Cap";
            Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            if (roundShape)
            {
                cap.transform.localScale = new Vector3(capW, capD, capH); // cylinder height = 2*y
                cap.transform.localPosition = new Vector3(0f, 0.024f, 0f);
            }
            else
            {
                cap.transform.localScale = new Vector3(capW, capD, capH); // cube: depth is full Y size
                cap.transform.localPosition = new Vector3(0f, 0.015f + capD * 0.5f, 0f); // cap bottom at the puck's 0.015 seat
            }
            _cap = cap.transform;
            _capRestY = _cap.localPosition.y;
            // Cap TOP face (viewer side): the Unity cylinder spans ±1×scale.y so its half-height
            // is capD; the cube spans ±0.5×scale.y so its half-height is capD/2. The docked label
            // seats a fixed margin proud of this, keeping the same real gap on the taller round
            // cap that the shallow square cap already had (round-label flicker fix).
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
            bool visible = real.gameObject.activeInHierarchy;
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
                    // Cancel a running dissolve, restore the layout scale, quick scale-in.
                    _dissolveLeft = 0f;
                    _rootGo.transform.localScale = _slotScale;
                    if (!_rootGo.activeSelf)
                        _rootGo.SetActive(true);
                    _appearLeft = _everShown ? ButtonTuning.AppearSeconds : 0f;
                    _collider.enabled = _interactable; // re-sync after the hide forced it off
                }
                else
                {
                    // LOGICAL hide is immediate (user #7): input off now, layout reflows now
                    // (VisibleNow is false already) — only the visuals shrink out while the
                    // pooled dust burst sweeps the cap away in its face color.
                    _hoverHand = null;
                    _depthArmed = true;
                    _collider.enabled = false;
                    if (_everShown && _rootGo.activeInHierarchy)
                    {
                        _dissolveLeft = ButtonTuning.DissolveSeconds;
                        ButtonDissolveFx.Play(_cap.position, _rootGo.transform.up,
                            BaseRadius * 2f * Mathf.Abs(_rootGo.transform.lossyScale.x),
                            _appliedColor);
                    }
                    else
                    {
                        _rootGo.SetActive(false); // initial settling / hidden cluster — silent pop
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
            if (applied != _appliedColor)
            {
                _appliedColor = applied;
                _capRenderer.sharedMaterial.color = applied;
            }
            if (_label != null)
            {
                Color labelBase = NativeButtonSkin.HasFont ? NativeButtonSkin.LabelColor : Color.white;
                _label.color = interactable ? labelBase : new Color(labelBase.r, labelBase.g, labelBase.b, 0.35f);
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
        /// dust-dissolve shrink-out / appear scale-in (user #7), finger-follow cap
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
                _dissolveLeft -= Time.deltaTime;
                float k = Mathf.Max(0f, _dissolveLeft / ButtonTuning.DissolveSeconds);
                _rootGo.transform.localScale = _slotScale * k;
                if (_dissolveLeft <= 0f)
                {
                    _rootGo.transform.localScale = _slotScale; // restore for the next show
                    _rootGo.SetActive(false);
                }
                return;
            }
            if (!_rootGo.activeSelf)
                return;
            _everShown = true;

            // Appear scale-in (quick, purely visual — input is live from frame one).
            if (_appearLeft > 0f)
            {
                _appearLeft -= Time.deltaTime;
                float k = 1f - Mathf.Max(0f, _appearLeft / ButtonTuning.AppearSeconds);
                _rootGo.transform.localScale = _slotScale * Mathf.SmoothStep(0.55f, 1f, k);
                if (_appearLeft <= 0f)
                    _rootGo.transform.localScale = _slotScale;
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
                    // DEBOUNCE (user): fire only when re-armed AND past the shared cooldown, so a
                    // retract-then-push or a hover flicker inside one poke cannot double-fire.
                    if (_depthArmed && Time.unscaledTime >= _nextPressTime)
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
            VRLog.Info("WorldUI", $"{_rootGo.name} pressed (source={source}, {hand.Side}).");
            _onClick();
        }
    }
}
