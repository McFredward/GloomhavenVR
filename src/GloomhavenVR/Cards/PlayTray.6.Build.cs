using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 6 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Regions: build helpers, raycast seating (REMOVED), the Overlay / BoardLit shaders, the keycap
// grain texture, RenderOnTop (REMOVED), MeasureBoardLocalExtents, board diagnostics.

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ build --

    // Board layout constants (local meters; -Z = element/viewer side).
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>
    /// The board's top (far) edge in board-LOCAL meters (+Y = the board's back/far edge). Board-
    /// anchored floaters (e.g. the pile browse fan) offset UP from here so they hover above the
    /// board face; a board-root child inherits the live board scale + pose automatically.
    /// </summary>
    internal const float BoardTopLocalY = BoardH * 0.5f;
    private const float SlotSpacing = 0.155f; // between slot centers
    private const float RestZoneX = -0.245f;
    private const float ButtonZoneX = 0.235f;

    /// <summary>
    /// The board's authored half width in board-LOCAL meters (fallback for
    /// <see cref="MeasureBoardLocalExtents"/> when the board has no renderers yet).
    /// </summary>
    internal const float BoardHalfWidthLocal = BoardW * 0.5f;

    /// <summary>
    /// Sanity band for the MEASURED top edge (board-local meters past the authored plate). The
    /// visible board (bundled frame + decorations) overhangs the authored plate by a few cm at
    /// most; anything beyond this is a transient outlier (e.g. a card mid-flight that is still
    /// parented under the tray while it animates home) and must never drag a board-anchored
    /// floater — or, worse, the enemy-info clearance — metres into the sky.
    /// </summary>
    private const float BoardExtentSanityMargin = 0.35f;

    /// <summary>Reused scan buffer for <see cref="MeasureBoardLocalExtents"/> (no steady-state allocation).</summary>
    private static readonly List<MeshRenderer> ExtentScratch = new(48);

    /// <summary>
    /// The control board's REAL rendered extents in the board's OWN LOCAL space: the top (far)
    /// edge <paramref name="topLocalY"/> and the half width <paramref name="halfLocalX"/>, both in
    /// board-local metres (multiply by the root's lossy scale for world metres).
    ///
    /// Derived from the tray's combined MESH-RENDERER bounds mapped into board-root-local coords —
    /// so a board TILT does not inflate the extent the way a world AABB would — because the VISIBLE
    /// board (bundled frame + decorations) is LARGER than the authored plate constants: anything
    /// that clears "the board" using <see cref="BoardTopLocalY"/> alone under-estimates the real
    /// edge and still ends up sitting inside the board (the WorldTooltips "still inside" report).
    /// Degrades to the authored constants when the board has no renderers yet (procedural build
    /// mid-frame), and clamps the measurement into a sane band (<see cref="BoardExtentSanityMargin"/>).
    ///
    /// Shared by every surface that must stay clear of the board (<c>WorldUI.WorldTooltips</c>'s
    /// above-the-edge hint anchor and <c>WorldUI.Surfaces.EnemyRevealSurface</c>'s spawn clearance),
    /// so both see the same board and neither carries its own copy of this math.
    /// </summary>
    internal static void MeasureBoardLocalExtents(Transform root, out float topLocalY, out float halfLocalX)
    {
        topLocalY = BoardTopLocalY;      // authored fallbacks
        halfLocalX = BoardHalfWidthLocal;
        if (root == null)
            return;

        ExtentScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, ExtentScratch);
        Matrix4x4 worldToLocal = root.worldToLocalMatrix;
        bool has = false;
        float maxLocalY = float.NegativeInfinity;
        float maxLocalAbsX = 0f;
        for (int i = 0; i < ExtentScratch.Count; i++)
        {
            MeshRenderer mr = ExtentScratch[i];
            if (mr == null || !mr.enabled)
                continue;

            // Prefer the renderer's own LOCAL mesh bounds mapped through (worldToLocal ×
            // rendererLocalToWorld) → board-root-local, which is tilt-tight; fall back to the
            // renderer's world AABB corners mapped into local space when there is no mesh.
            Bounds b;
            Matrix4x4 toBoardLocal;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            Mesh? mesh = mf != null ? mf.sharedMesh : null;
            if (mesh != null)
            {
                b = mesh.bounds;
                toBoardLocal = worldToLocal * mr.transform.localToWorldMatrix;
            }
            else
            {
                b = mr.bounds; // world AABB
                toBoardLocal = worldToLocal;
            }

            Vector3 c = b.center, e = b.extents;
            for (int s = 0; s < 8; s++)
            {
                Vector3 corner = c + new Vector3(
                    (s & 1) == 0 ? -e.x : e.x,
                    (s & 2) == 0 ? -e.y : e.y,
                    (s & 4) == 0 ? -e.z : e.z);
                Vector3 local = toBoardLocal.MultiplyPoint3x4(corner);
                if (local.y > maxLocalY)
                    maxLocalY = local.y;
                float absX = Mathf.Abs(local.x);
                if (absX > maxLocalAbsX)
                    maxLocalAbsX = absX;
                has = true;
            }
        }
        ExtentScratch.Clear();
        if (!has)
            return;

        topLocalY = Mathf.Clamp(maxLocalY, BoardTopLocalY, BoardTopLocalY + BoardExtentSanityMargin);
        halfLocalX = Mathf.Clamp(maxLocalAbsX, BoardHalfWidthLocal, BoardHalfWidthLocal + BoardExtentSanityMargin);
    }

    private void BuildProceduralBoard()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "TrayBoard";
        Object.Destroy(board.GetComponent<Collider>());
        board.transform.SetParent(_root, worldPositionStays: false);
        board.transform.localScale = new Vector3(BoardW, BoardH, 0.012f);
        board.transform.localPosition = new Vector3(0f, 0f, 0.010f);
        Tint(board, new Color(0.16f, 0.13f, 0.10f));

        // Subtle raised edge so the board reads as a desk/tray, not a floating slab.
        var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lip.name = "TrayLip";
        Object.Destroy(lip.GetComponent<Collider>());
        lip.transform.SetParent(_root, worldPositionStays: false);
        lip.transform.localScale = new Vector3(BoardW + 0.015f, 0.02f, 0.018f);
        lip.transform.localPosition = new Vector3(0f, -BoardH * 0.5f - 0.002f, 0.008f);
        Tint(lip, new Color(0.11f, 0.09f, 0.07f));

        for (int i = 0; i < 2; i++)
        {
            var slot = new GameObject($"Slot{i + 1}").transform;
            slot.SetParent(_root, worldPositionStays: false);
            slot.localPosition = new Vector3((i == 0 ? -0.5f : 0.5f) * SlotSpacing, 0.015f, 0f);
            _slots[i] = slot;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = "Frame";
            Object.Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(slot, worldPositionStays: false);
            frame.transform.localScale = new Vector3(w * 1.12f, h * 1.12f, 1f);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.003f); // behind the card, in front of the board
            Tint(frame, i == 0 ? new Color(0.55f, 0.45f, 0.22f) : new Color(0.30f, 0.29f, 0.27f));

            var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
            inner.name = "FrameInner";
            Object.Destroy(inner.GetComponent<Collider>());
            inner.transform.SetParent(slot, worldPositionStays: false);
            inner.transform.localScale = new Vector3(w * 1.04f, h * 1.04f, 1f);
            inner.transform.localPosition = new Vector3(0f, 0f, 0.0025f);
            Tint(inner, new Color(0.12f, 0.10f, 0.08f));
        }

        // Rest zone (test #24 item 3): backdrop + two button anchors. RestControls
        // builds a comfortable BoardButton at each anchor (short rest on top, long
        // rest below — they sit together); the REAL native "Kurze Rast" widget docks
        // over the SHORT anchor (TrayControlDockSurface) and hides the mod short
        // button while it holds. NO mod-drawn rest header anymore: the redundant
        // "Kurze Rast" caption that used to sit above the button duplicated the
        // native widget's own label (each button already carries its localized
        // caption). Plate widened to 0.14 to seat the wider button footprints;
        // centered at RestZoneX -0.245 it spans x -0.315..-0.175 (left edge clears
        // the board edge -0.32; right edge clears slot 0's left content edge ≈ -0.129
        // by 0.046), y -0.10..0.08 (height 0.18).
        var restBack = GameObject.CreatePrimitive(PrimitiveType.Quad);
        restBack.name = "RestZone";
        Object.Destroy(restBack.GetComponent<Collider>());
        restBack.transform.SetParent(_root, worldPositionStays: false);
        restBack.transform.localScale = new Vector3(0.14f, 0.18f, 1f);
        restBack.transform.localPosition = new Vector3(RestZoneX, -0.01f, 0.003f);
        Tint(restBack, new Color(0.12f, 0.11f, 0.10f));

        // Button anchors: the RestControls BoardButton bases sit at z 0.004 in front
        // of the plate (z-order like CONFIRM/UNDO at z -0.006), viewer-side caps
        // proud. Short at y 0.04 (button 0.115×0.04 → y 0.02..0.06), long at y -0.05
        // (→ y -0.07..-0.03): a 0.05 gap between them, both inside the plate.
        _shortRestAnchor = new GameObject("ShortRestToken").transform;
        _shortRestAnchor.SetParent(_root, worldPositionStays: false);
        _shortRestAnchor.localPosition = new Vector3(RestZoneX, 0.04f, -0.006f);

        _longRestAnchor = new GameObject("LongRestToken").transform;
        _longRestAnchor.SetParent(_root, worldPositionStays: false);
        _longRestAnchor.localPosition = new Vector3(RestZoneX, -0.05f, -0.006f);
    }

    /// <summary>
    /// Test #24 item 7: a full-board LASER-RETICLE surface so the ray's hit dot
    /// renders ANYWHERE on the control board's face, not only on its discrete
    /// buttons/panels. An invisible thin plane collider matching the board face,
    /// registered as a laser target — <see cref="CardsDriver"/> ray-tests it
    /// geometrically (Collider.Raycast, layer-independent) with all the other
    /// LaserTargets and clamps the beam to the hit (UiHitOverride → reticle).
    ///
    /// PRECEDENCE (no stolen clicks): the plane sits at z 0.002 — BEHIND every
    /// viewer-side widget (board buttons at z ≤ -0.004, rest/CONFIRM/UNDO anchors
    /// -0.006, the docked native uGUI hosts, the DecisionDock row) and behind the
    /// slotted cards (z 0). CardsDriver keeps the NEAREST hit, so a real button /
    /// token / card always wins where the ray crosses it; the docked native widgets
    /// win through the game's own uGUI raycast (the RayUgui-closer guard stands the
    /// board laser down entirely). The surface only wins where the ray points at
    /// BARE board, where it shows the reticle and does NOTHING else — its
    /// <see cref="BoardSurfaceTarget"/> OnPoke is a no-op, so a trigger on bare board
    /// never activates anything and (as a bonus) the beam no longer passes THROUGH
    /// the floating board to click a hex behind it. Laser reticle only: it is NOT a
    /// PokeableBehaviour and is never registered for fingertip poke.
    /// </summary>
    private void BuildBoardSurface()
    {
        if (_root == null)
            return;
        // The bundled board already registered its real MeshCollider as the laser
        // surface (DEFECT 2, EnsureBuilt) — the synthetic flat plane below is only for
        // the procedural fallback board, which has no mesh collider of its own.
        if (_boardColliderRegistered)
            return;
        var go = new GameObject("BoardSurface");
        go.transform.SetParent(_root, worldPositionStays: false);
        // Between the slotted cards (z 0) and the opaque board face (z 0.004): the
        // reticle sits ~2 mm proud of the board, behind the cards and all widgets.
        go.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(BoardW, BoardH, 0.002f);
        box.isTrigger = true;
        var target = go.AddComponent<BoardSurfaceTarget>();
        RegisterLaserTarget(box, target);
    }

    // Item 6 (test #29): the card-slot captions ("INITIATIVE", "2") are GONE. They
    // sat in the recessed slot-floor plane and clipped THROUGH the seated card at the
    // player's oblique angle; the initiative already reads on the docked track and the
    // second slot needs no label. BuildSlotLabels + its call in EnsureBuilt were
    // removed outright (no no-op stub). This note used to add "AddCaption stays live for the
    // pick field's SELECT caption, so nothing goes unused" — the pick field has since gone too,
    // and AddCaption with it.

    /// <summary>
    /// Build the generic cluster's keycaps onto the board's BUTTON SEATS.
    ///
    /// <para><paramref name="seats"/> is indexed by seat (0 = commit, 1 = undo, 2 = the third
    /// recess), each entry the bundled anchor resolved by <see cref="BoardAnchors.ResolveSeats"/> or
    /// null when this board has no such recess.</para>
    ///
    /// <para><b>THE TWO-ANCHOR FALLBACK, STATED.</b> A board that supplies only seats 0 and 1 — every
    /// board in the shipped bundle, and any board the asset lane has not regenerated — is built
    /// EXACTLY as it was before this change: Confirm on seat 0, the item "Use" cap sharing it, Undo
    /// on seat 1, at the same offsets from the same dials. Seat 2 simply has no anchor and no
    /// occupant, and <see cref="SeatAnchor"/> answers null for it, so nothing is built there and
    /// nothing is synthesised there either. A procedural anchor is created ONLY for seats 0 and 1
    /// and ONLY when they are missing (the no-bundle procedural board), at the same
    /// <see cref="ButtonZoneX"/> positions the two hardcoded fallbacks always used — an invented
    /// third anchor would be a keycap floating on a board with no recess under it, which is worse
    /// than not having the seat.</para>
    /// </summary>
    private void BuildButtons(Transform?[] seats)
    {
        if (_root == null)
            return;

        // Seats 0/1 must exist for the cluster to work at all, so they fall back to the authored
        // procedural anchors. Seat 2 does NOT: absent means absent (see the summary).
        Transform confirmParent = seats.Length > 0 && seats[0] != null
            ? seats[0]!
            : NewAnchor(BoardAnchors.SeatName(0), new Vector3(ButtonZoneX, 0.045f, -0.006f));
        Transform undoParent = seats.Length > 1 && seats[1] != null
            ? seats[1]!
            : NewAnchor(BoardAnchors.SeatName(1), new Vector3(ButtonZoneX, -0.06f, -0.006f));
        // Remember what the cluster actually got, so SetConfirmUndoOffset and the seat accessors
        // read the SAME transforms the caps were parented to — including the synthesised ones.
        if (seats.Length > 0) seats[0] = confirmParent;
        if (seats.Length > 1) seats[1] = undoParent;

        // PART B + C: Confirm/Undo are real 3D keycaps, sized and positioned from the ACTIVE
        // board's config. The full X/Y/Z offset (X/Y in plane, Z = proud toward the player)
        // REPLACES the old inset + raycast reseat, so the buttons seat at a predictable depth per
        // board (debug-menu tunable). Round-2: the cap SHAPE is per-board (Square boxy keycap by
        // default, or Round disc), and a per-board SPACING spreads Confirm/Undo apart.
        ControlBoard active = CardsConfig.CurrentBoard;
        // Item D: the generic-button area is a SEATED cluster of BoardAnchors.ButtonSeatCount = 3
        // seats, one per physical button recess the boards now carry (user, 2026-08: "3 statt 2 Slots
        // fuer die buttons"). THREE, not four: the "FOUR turn-flow buttons at once" this paragraph
        // used to claim was corrected by the enumeration in WorldUI/ButtonCluster.cs — every
        // readyButton/m_SkipButton/m_UndoButton toggle site in the decompiled Choreographer was read,
        // six states reach three, and none reaches four for the caps this board draws
        // (m_selectButton is mutually exclusive with the ready button at every site that raises it,
        // and the mod mirrors no cap for it). Believe the ButtonCluster table.
        // The layout is SetConfirmUndoOffset's; it does NOT auto-scale the caps from the live count —
        // see the paragraph below for why that was taken out.
        // Today the mod owns Confirm + Undo here, on seats 0 and 1; SEAT 2 IS RESOLVED BUT UNOCCUPIED
        // — the real Skip/Select turn-flow buttons still dock via the WorldUI ButtonCluster mount (not
        // one of these files), and moving Skip onto seat 2 changes the meaning of wire fields a peer
        // already derives its copy of that cap from (Net/RemoteBoardFurniture's skipSeat solve, ids
        // 81..88). That is a cross-lane round, not this one. The item
        // "Use" confirm built below is a fourth cap but NOT a fourth seat — it shares Confirm's
        // (PlayTray.GenericPrimarySlot), which is what makes "Auswahl beenden" and "Benutzen" land in
        // the same place. Nothing here reads the count: SetConfirmUndoOffset owns the layout.
        // Cap size is the TUNED size at every count (user: "der Use-Button soll genauso groß sein und
        // sich nach den Werten richten, die die generischen Buttons vorgegeben haben"). It used to be
        // run through GenericClusterButtonSize, which shrank every cap once a third member joined — so
        // the instant an item clipped into the use slot, Confirm and Undo silently resized too and the
        // whole cluster stopped matching the dialled-in values. The stack now makes room by SPACING
        // (GenericClusterY), which is itself a tuned value, so every member is exactly the size the
        // player asked for and the geometry stays theirs.
        Vector3 off = CardsConfig.ConfirmUndoOffset(active).Value;
        float spacing = CardsConfig.GenericButtonSpacing(active).Value;
        bool round = CardsConfig.GenericButtonShape(active).Value == ButtonShape.Round;

        // Category split (user: "every value applies ONLY to its own category"): the
        // Confirm/Undo keycaps read the [BoardButtons] set EXCLUSIVELY — independent
        // WIDTH/HEIGHT (rectangular keycaps), DEPTH and TRAVEL, all with the authored
        // numeric defaults (0.073 × 0.073 × 0.036 / 4 mm — no 0=Auto sentinel any more).
        // ROUND caps take [BoardButtons] Width as their DIAMETER since the 2026-08 retirement of
        // [Cards] ConfirmUndoSize_{board}: that dial fed ONLY this round branch after the category
        // split above moved the (default) square caps onto [BoardButtons] — with the shipped
        // Square shape it was a dead dial in the debug menu (user report). ONE family now sizes
        // Confirm/Undo in both shapes, and a [BoardButtons] edit live-rebuilds either via the
        // ButtonTuning.Version watch (ApplyButtonTuningIfChanged).
        WorldUI.ButtonTuning.Bind();
        float side = WorldUI.ButtonTuning.BoardCapWidth; // round-cap diameter (== square cap width)
        // Square caps ALWAYS keep the tuned [BoardButtons] W×H, whatever the member count — the item
        // "Use" confirm is built from the very same rectSize/depth/travel below, so it is identical
        // to Confirm and Undo by construction and follows every tuning change with them.
        var rectSize = round
            ? new Vector2(side, side)
            : new Vector2(WorldUI.ButtonTuning.BoardCapWidth, WorldUI.ButtonTuning.BoardCapHeight);
        float capDepth = WorldUI.ButtonTuning.BoardCapDepth;
        float capTravel = WorldUI.ButtonTuning.BoardCapTravel;

        // Initial labels are overwritten by the live game-widget label each TickStatus
        // (ConfirmLabel()/UndoLabel()); route the fallback literals through the game keys.
        _confirm = BoardButton.Create(confirmParent, rectSize,
            new Color(0.35f, 0.46f, 0.28f), // T4: muted sage green — antique, still clearly "go"
            Core.Loc.Game("GUI_CONFIRM", "Confirm"),
            () => ConfirmRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board);
        _confirm.WireCap = Net.NetProtocol.CapPressConfirm; // mirror this cap's press dip to peers
        _confirm.DisabledReason = CardsGameApi.DescribeConfirmGate; // built only on rejection
        _confirm.ActivationGuard = ConfirmGuardRemaining; // accident window (test #19)
        RegisterLaserTarget(_confirm.Collider!, _confirm);

        _undo = BoardButton.Create(undoParent, rectSize,
            new Color(0.44f, 0.31f, 0.20f), // T4: worn leather brown (kept — already antique)
            Core.Loc.Game("GUI_UNDO", "Undo"),
            () => UndoRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board);
        _undo.WireCap = Net.NetProtocol.CapPressUndo;
        _undo.DisabledReason = CardsGameApi.DescribeUndoGate;
        RegisterLaserTarget(_undo.Collider!, _undo);

        // Requirement 9a: while the item "Use" confirm is a live cluster member, it is a GENERIC
        // cluster board button in THIS column — same board keycap look as Confirm/Undo (never the old
        // bespoke keycap beside the slot); onClick routes to the ItemsPile-supplied use action.
        //
        // IT IS PARENTED TO confirmParent ON PURPOSE, and that is now load-bearing: a placed item
        // hides Confirm outright, so the two caps are mutually exclusive and share ONE seat
        // (PlayTray.GenericPrimarySlot). Sharing the anchor is what reduces "same position" to "same
        // localPosition" — SetConfirmUndoOffset writes one Vector3 to both.
        //
        // IT IS NOW BUILT UNCONDITIONALLY AND HIDDEN, instead of being built when it becomes active
        // and destroyed when it stops. User report 2026-08-09: "Wenn man einen abgelegten Gegenstand
        // von der 'Benutzen'-Overlay-Area wieder aufhebt, verschwinden die Knöpfe … ABER die
        // verschwinden und tauchen ohne die Animation die sonst kommt auf." A cap that is DESTROYED
        // cannot crumble and a cap that is CREATED cannot assemble — construction is the one
        // transition the mod's authored appear/disappear can never cover. Existing from the board's
        // first frame and toggling through SetVisible puts it on the same animated path as every
        // other cap on the board. (Hidden means gameObject inactive with the collider off, so it is
        // not laser-targetable either — the board laser scan skips !enabled/!activeInHierarchy
        // colliders, CardsDriver.3.Laser.cs.)
        _itemUseConfirm = BoardButton.Create(confirmParent, rectSize,
            new Color(0.35f, 0.46f, 0.28f), // muted sage green — the "use / go" accent, like Confirm
            // Same MOD string the recess caption below now carries (Loc "item_use_area"): the
            // game key GUI_USE does not resolve in this build, so BOTH used to ship the English
            // fallback and a German board read "USE" on the cap and "USE" on the engraving. The
            // cap's live wording still rides wire record 13, so peers keep seeing the owner's
            // word — and the surrender picks still override the label entirely.
            _itemUseConfirmLabel ?? Core.Loc.Mod("item_use_area"),
            () => _itemUseConfirmAction?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board);
        _itemUseConfirm.WireCap = Net.NetProtocol.CapPressItemUse;
        _itemUseConfirm.SetState(enabled: true, accent: true); // always pressable while shown (no game gate)
        RegisterLaserTarget(_itemUseConfirm.Collider!, _itemUseConfirm);

        // SEAT EVERY CAP'S VISIBILITY BEFORE IT CAN EVER BE RENDERED — the ORDER half of the
        // 2026-08-09 report ("Weiterhin sieht man den 'Rückgängig machen' Knopf ganz kurz aufblitzen
        // und wieder verschwinden. Es ist richtig, dass der Knopf hierzu nicht angezeigt werden soll
        // - verhindere nur dieses 'Aufblitzen'.").
        //
        // A BoardButton is born VISIBLE (`_logicalVisible = true`, GameObject active) because that is
        // the only sensible default for a freshly built control. The predicates that decide whether
        // Confirm/Undo may be shown at all live in TickStatus and run on the NEXT tick — so a rebuilt
        // Undo was drawn for a frame or two and then taken away again, which is exactly a flash. Note
        // the second half of the ugliness: by the time TickStatus hid it, the fresh cap had ticked, so
        // it did not merely vanish, it played a full crumble for a button that was never meant to be
        // there at all.
        //
        // The fix is the ORDER, not a second suppressor: whoever rebuilds these caps remembers what
        // the live ones were showing and hands it back here, and it is applied INSIDE the build call,
        // before the first render and inside the new cap's build-then-settle window — so the re-seat
        // is silent by construction (BoardButton.SetVisible pops when !Settled) rather than by a
        // special case. `null` means "no previous state to restore" (a first build), where born-
        // visible is right and TickStatus refines it a tick later exactly as it always did.
        if (_confirmShownBeforeRebuild == false)
            _confirm?.SetVisible(false);
        if (_undoShownBeforeRebuild == false)
            _undo?.SetVisible(false);
        _confirmShownBeforeRebuild = null;
        _undoShownBeforeRebuild = null;
        // The item-use cap is the one whose resting state is HIDDEN: it exists on every board from
        // frame one and only shows while a usable card is clipped into the recess.
        if (!_itemUseActive)
            _itemUseConfirm?.SetVisible(false);

        SetConfirmUndoOffset(off, spacing); // Confirm+Use share the top seat, Undo the bottom one, Z proud
        string seatTwo = seats.Length > 2 && seats[2] != null
            ? $"'{seats[2]!.name}' y{GenericSeatY(2, spacing) * 1000f:+0.0;-0.0} mm (no occupant yet)"
            : "ABSENT on this board";
        VRLog.Info("Cards", $"Board: Confirm/Undo built as 3D {(round ? "round" : "square")} keycaps " +
                            $"{rectSize.x:F3}×{rectSize.y:F3} m for {active} (offset {off}, spacing {spacing:F3} m; " +
                            $"seat 0 '{confirmParent.name}' y{GenericSeatY(0, spacing) * 1000f:+0.0;-0.0} mm, " +
                            $"seat 1 '{undoParent.name}' y{GenericSeatY(1, spacing) * 1000f:+0.0;-0.0} mm, " +
                            $"seat 2 {seatTwo})" +
                            (round ? "." : " — square caps are beveled keycaps: state-colour top + BRIGHT lit bevel ring + dark warm walls (3-submesh, high contrast) for unmistakable 3D."));
        VRLog.Info("Cards", $"Board: button geometry config applied — {WorldUI.ButtonTuning.Describe()}.");
        _tuningVersion = WorldUI.ButtonTuning.Version; // fresh build reflects current config
        _capGeometryKey = CapGeometryKey(); // what these caps were BUILT from (see RebuildAttachedControls)
    }

    /// <summary>
    /// What the live Confirm/Undo/Use caps were BUILT from — everything <see cref="BuildButtons"/>
    /// bakes into the geometry of a cap and therefore cannot change without a real rebuild: the
    /// active board, the cap SHAPE, and the <c>[BoardButtons]</c> W/H/D/travel + <c>[ButtonColors]</c>
    /// tints that <c>ButtonTuning.Version</c> covers.
    ///
    /// <para>Deliberately NOT in here: the cluster OFFSET, the SPACING and the member COUNT. Those
    /// only move caps, and <see cref="SetConfirmUndoOffset"/> moves them in place from the live
    /// count — which is the whole reason the item-use toggle no longer needs to destroy anything.
    /// (The caps have not been auto-SIZED from the count since the user's "der Use-Button soll
    /// genauso groß sein" ruling; if that ever comes back, the count belongs in this key.)</para>
    /// </summary>
    private int CapGeometryKey()
    {
        ControlBoard board = CardsConfig.CurrentBoard;
        int key = (int)board;
        key = key * 31 + (int)CardsConfig.GenericButtonShape(board).Value;
        key = key * 31 + WorldUI.ButtonTuning.Version;
        return key;
    }

    /// <summary>The <see cref="CapGeometryKey"/> the live caps were built from.</summary>
    private int _capGeometryKey;

    /// <summary>
    /// What the Confirm / Undo caps were SHOWING when a rebuild tore them down, handed forward so
    /// <see cref="BuildButtons"/> can seat the replacements in the same state before they are ever
    /// drawn. Null = nothing to restore (first build). See the seat block in BuildButtons.
    /// </summary>
    private bool? _confirmShownBeforeRebuild;
    private bool? _undoShownBeforeRebuild;

    private Transform NewAnchor(string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_root, worldPositionStays: false);
        // PART B: seat the gear / follow-toggle at a FIXED small proud depth toward the player,
        // in place of the old raycast (which floated them up to 5 cm off the board). Predictable
        // and depth-correct; the debug menu does not expose these individually, so a fixed proud
        // keeps them consistent across every board.
        Vector3 seated = new(localPos.x, localPos.y, -FixedProudZ);
        t.localPosition = seated;
        t.localRotation = _boardFaceFrame; // face the functional board face like the bundle anchors
        VRLog.Info("Cards", $"Board: '{name}' seated at a fixed proud local-Z {seated.z * 1000f:F1} mm " +
                            "toward the player (predictable, no raycast).");
        return t;
    }

    /// <summary>
    /// PART B: fixed predictable proud depth (toward the player, −Z) at which the mod-built
    /// HUD widgets the debug menu does NOT expose individually — the settings gear, the
    /// follow-toggle and the round readout — seat, in place of the old unreliable raycast.
    /// Small and depth-correct: they rest just in front of the authored plane, always the
    /// same amount, so they never float 5 cm off the board again.
    /// </summary>
    private const float FixedProudZ = 0.005f;

    /// <summary>
    /// Item A/4: local-Z thickness (meters) of the SQUARE Confirm/Undo keycaps — the total
    /// protrusion toward the player. Raised over successive passes (0.014 → 0.03 → 0.036) so the
    /// side walls + bevel have real area at the board's oblique angle; taller = physically more
    /// side visible (item 4 lever c). Press travel (4 mm) is unchanged. Exact real-world
    /// protrusion is logged per cap by <see cref="BoardButton.LogCapDiagnostics"/>.
    /// </summary>
    private const float SquareCapThickness = 0.036f;

    /// <summary>
    /// Item 4: width (meters) of the lit 45° CHAMFER ring around the front edge of the square
    /// keycaps — the bright "catch-light" bevel that makes the cap read as raised even viewed
    /// near top-down. It consumes this much of both the front plateau inset AND the front depth
    /// (a true 45°). Clamped in <see cref="CardMesh.BuildBeveledKeycap"/> to ≤ 90 % of the cap's
    /// half-size and depth. One line to retune how chunky the lit edge reads (~7 mm ≈ a fat,
    /// clearly-visible chamfer on a ~120 mm cap).
    /// </summary>
    private const float SquareCapBevel = 0.007f;

    /// <summary>Base local-Z of the slot snap-glow (per-board SlotOverlayOffset.z adds on top).</summary>
    private const float SlotGlowBaseZ = -0.006f;

    /// <summary>Base local-Z of the wanted-slot glow (per-board SlotOverlayOffset.z adds on top).</summary>
    private const float WantedGlowBaseZ = -0.004f;

    // ------------------------------------------------------------------ raycast seating (REMOVED) --

    // SeatStandoff / SeatProud / ReseatProud / SeatOnBoardFace are GONE — dead code, ~85 lines.
    // ReseatProud had ZERO callers, so SeatOnBoardFace's only caller was itself dead, and the two
    // consts were used only inside SeatOnBoardFace. 17862bb replaced raycast auto-seating with
    // per-board config offsets and a fixed proud Z, and every widget has gone through NewAnchor /
    // FixedProudZ since. The registry ("Suspected vestigial") claimed these still had callers,
    // read off the surrounding prose rather than the call graph; they did not.
    //
    // KEEP THE REASON, because re-adding this is the tempting move: the raycast reseat floated the
    // gear -30..-50 mm off the Oak and Steel boards. "No more -50 mm surprises" is the whole point
    // of the fixed proud Z. Do not route NewAnchor back through a surface raycast because it is
    // "more accurate". See INVARIANTS-Cards.md, "Raycast auto-seating is GONE".

    /// <summary>
    /// ITEM 2 ground truth (logged once per board, after placement): for the round
    /// readout, gear, follow toggle, a reference slot and its card — world position, the
    /// readable-face direction (−forward), and how it compares to the board functional-
    /// face normal and the head direction — so an in-game log SHOWS which way each element
    /// faces and whether it sits proud, even without an HMD capture.
    /// </summary>
    private void LogBoardFaceDiagnostics()
    {
        if (_boardDiagLogged || _root == null)
            return;
        _boardDiagLogged = true;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        Vector3 headPos = head != null ? head.transform.position : Vector3.zero;
        Vector3 headDir = head != null ? head.transform.forward : Vector3.forward;
        // Functional face normal toward the viewer, in the CURRENT (placed) world pose.
        Vector3 faceTowardViewer = (_root.rotation * _boardFaceFrame) * Vector3.back;
        VRLog.Info("Cards", $"ITEM2 diag — board face: toward-viewer normal {faceTowardViewer}, " +
            $"head dir {headDir}, head→board {(_root.position - headPos).normalized}, " +
            $"root pos {_root.position}, buildNormal(nF world) {_boardFaceNormalWorld}.");
        LogElementFacing("RoundReadout", _roundLabel != null ? _roundLabel.transform : null, faceTowardViewer, headPos);
        LogElementFacing("FollowToggle", _followToggle != null ? _followToggle.transform : null, faceTowardViewer, headPos);
        LogElementFacing("Slot0", _slots[0], faceTowardViewer, headPos);
        LogElementFacing("Slot0Card", _occupants[0] != null ? _occupants[0]!.transform : null, faceTowardViewer, headPos);
        // Item A: conclusive square-cap wall diagnostic (real mm thickness + shader + queue).
        _confirm?.LogCapDiagnostics("Confirm");
        _undo?.LogCapDiagnostics("Undo");
        // Item 7b: prove the gear/pin caps are now SOLID OPAQUE beveled keycaps (BoardLit, opaque
        // queue, no alpha-blend, no Overlay/RenderOnTop) like the other keycaps — the diag reports
        // "OPAQUE: YES" for both, settling the old see-through look.
        _followToggle?.LogCapDiagnostics("Pin/FIXIERT");
    }

    private static void LogElementFacing(string label, Transform? t, Vector3 faceTowardViewer, Vector3 headPos)
    {
        if (t == null)
        {
            VRLog.Info("Cards", $"ITEM2 diag — {label}: not present.");
            return;
        }
        // TMP / quad / card all read on their LOCAL −Z. The readable face points at the
        // player when −forward aligns with the toward-viewer face normal (dot ~ +1) and
        // back toward the head (headDot > 0).
        Vector3 readable = -t.forward;
        float faceDot = Vector3.Dot(readable, faceTowardViewer);
        Vector3 toElem = (t.position - headPos).normalized;
        float headDot = Vector3.Dot(readable, -toElem);
        VRLog.Info("Cards", $"ITEM2 diag — {label}: pos {t.position}, readable(-Z) {readable}, " +
            $"vs faceNormal dot {faceDot:F2} (want ~+1), vs head dot {headDot:F2} (want >0) → " +
            $"{(headDot > 0.2f ? "FACES the player" : "faces AWAY from the player")}.");
    }

    // AddCaption is gone with the pick field, its last caller. It built a TextMeshPro caption
    // that shrank to fit a given box (Core.TmpFit.Fit, test #12) — that helper is still the way
    // to add a board caption; there is simply no board caption left in this class.

    private static void Tint(GameObject go, Color color, bool overlay = false)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        // Items 5/6: board-docked HUD widgets (round-readout plate, gear/follow-toggle
        // bodies) route to the bundled GloomhavenVR/Overlay shader, the ONLY one that
        // exposes _ZTest — so RenderOnTop's SetInt("_ZTest", Always) actually takes and
        // the widget draws over the now-OPAQUE board. Overlay missing (bundle not updated)
        // → fall back to Standard (widget may be occluded until the new bundle ships).
        //
        // Round 15 (the LIGHTING hole, see CardMesh.EmissionFloorFactor): everything else
        // now prefers the bundled BoardLit — these are the procedural fallback board's
        // opaque quads/cubes (TrayBoard, TrayLip, the slot 'Frame'/'FrameInner' plates
        // directly behind the seated cards, RestZone), and on the old Standard path they
        // rendered albedo × light ≈ BLACK in the game's dark scenes, exactly like the card
        // slab. BoardLit is the shader the real tray uses for precisely this reason (baked
        // studio rig + ambient floor, no scene light needed) and these surfaces need no
        // alpha clip, so option 1 of the round-15 fix applies cleanly. When BoardLit is
        // absent the Standard fallback gets the same emission floor as the card slab
        // (ApplyEmissionFloor no-ops on Overlay/Sprites, which are already unlit).
        Shader? shader = overlay ? OverlayShader() : BoardLitShader();
        shader ??= Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var m = new Material(shader) { color = color };
            CardMesh.ApplyEmissionFloor(m);
            renderer.sharedMaterial = m;
        }
    }

    // ---- Overlay shader (items 5/6) --------------------------------------------------
    // The "logged once each way" latches are gone with the inlined lookup: Core.BundleShaders owns
    // both lines now, so a shader cannot be announced twice from two call sites either.
    private static Shader? _overlayShader;

    /// <summary>
    /// The bundled <c>GloomhavenVR/Overlay</c> shader (loaded from the asset bundle at
    /// runtime): an unlit shader that — unlike <c>Sprites/Default</c> and <c>Standard</c>
    /// — EXPOSES <c>_ZTest</c>, which is what let the (now removed) RenderOnTop helper force a
    /// board-HUD widget to draw over the opaque control board — the root cause of the invisible
    /// readout / gear / toggle / glows. The shader is still used as an unlit/additive base for
    /// the glow and readout materials; nothing forces ZTest any more (the widgets seat proud
    /// instead). Cached; re-found until present so a late bundle load still
    /// resolves. Null when the bundle lacks it — callers fall back to Standard/Sprites and
    /// the widget may be occluded until the new bundle ships. Logged once each way.
    /// </summary>
    internal static Shader? OverlayShader()
    {
        // A bundled shader is NOT discoverable via Shader.Find until something loads it into
        // memory. BoardLit resolves only because a bundle PREFAB's material references it;
        // GloomhavenVR/Overlay is referenced ONLY by runtime C#, so it is never loaded and
        // Shader.Find returns null (root cause of the STILL-invisible gear/glows in build 0258fbb).
        // The find-then-probe-the-bundles logic that used to be inlined here now lives in
        // Core.BundleShaders — the same trap cost ModBuild 153 a whole hardware round at a THIRD
        // site (HauntFigures.Albedo), because this fix was written down as a comment and a comment
        // is not a guard. There is now one implementation and a wire test that fails the build gate
        // if a bare Shader.Find on a GloomhavenVR/* name is written anywhere in src/ again.
        _overlayShader ??= Core.BundleShaders.Resolve(
            "GloomhavenVR/Overlay", "Cards",
            "board HUD widgets (round readout, gear/follow-toggle, slot glows) get the only shader "
            + "in reach that exposes _ZTest, so they are not occluded by the opaque board.",
            "Board HUD widgets fall back to Standard/Sprites and may be occluded by the board.");
        return _overlayShader;
    }

    /// <summary>An Overlay-shader material tinted <paramref name="color"/>, or null when the shader is absent.</summary>
    private static Material? OverlayMaterial(Color color)
    {
        Shader? s = OverlayShader();
        return s != null ? new Material(s) { color = color } : null;
    }

    // ---- BoardLit shader (item 5: solid, shaded button walls) -------------------------
    private static Shader? _boardLitShader;

    /// <summary>
    /// Item 5: the bundled <c>GloomhavenVR/BoardLit</c> shader — a self-contained BAKED-lit
    /// shader (two fixed studio directions + an ambient floor, independent of the scene's own
    /// lights). The square keycaps (Confirm/Undo, and Rest when set to Square) used
    /// <c>Shader.Find("Standard")</c>, which strips to the UNLIT <c>Sprites/Default</c> fallback
    /// in the game build — every cube face then rendered the same flat colour, so the side WALLS
    /// never shaded and the button read as a floating flat square ("the walls aren't rendered").
    /// BoardLit shades by world normal, so the box's side walls visibly darken relative to its
    /// front face and it reads as a solid protruding 3D button even in the unlit void/menu scenes
    /// (the same reason the board mesh and the hands use it). Unlike <c>GloomhavenVR/Overlay</c>
    /// it is referenced by bundle PREFAB materials, so <c>Shader.Find</c> resolves it directly;
    /// still re-found until present and probed across loaded bundles, mirroring
    /// <see cref="OverlayShader"/>. Null only when the bundle lacks it — callers fall back to
    /// Standard/Legacy/Sprites (the pre-fix flat look). Logged once each way.
    /// </summary>
    internal static Shader? BoardLitShader()
    {
        _boardLitShader ??= Core.BundleShaders.Resolve(
            "GloomhavenVR/BoardLit", "Cards",
            "square board buttons get shaded, solid side walls (baked-lit, works in the unlit scenes).",
            "Square board buttons fall back to Standard/Sprites and their side walls may read flat.");
        return _boardLitShader;
    }

    // ---- keycap grain texture (task #5a: carved-wood/parchment surface) ----------------
    private const string GrainAlbedoPath = "Assets/Bundle/Table/KeycapGrain_albedo.png";
    private const string GrainNormalPath = "Assets/Bundle/Table/KeycapGrain_normal.png";
    private static Texture2D? _grainAlbedo;
    private static Texture2D? _grainNormal;
    private static bool _grainProbed; // includes the cached "not found" state (no per-material retry)

    /// <summary>
    /// Task #5a: load the shared tileable grain texture ONCE from whichever loaded bundle holds
    /// it (same probe pattern as <see cref="BoardLitShader"/>/<see cref="OverlayShader"/>), so
    /// every keycap material reuses one <see cref="Texture2D"/>. Caches the "not found" state
    /// too, so an older bundle without the texture never retries. Sets Repeat wrap so the planar
    /// keycap UVs (CardMesh.BuildBeveledKeycap) tile cleanly. Logged once each way.
    /// </summary>
    private static void EnsureGrainLoaded()
    {
        if (_grainProbed)
            return;
        _grainProbed = true;
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (b == null) continue;
            _grainAlbedo ??= b.LoadAsset<Texture2D>(GrainAlbedoPath);
            _grainNormal ??= b.LoadAsset<Texture2D>(GrainNormalPath);
            if (_grainAlbedo != null && _grainNormal != null) break;
        }
        if (_grainAlbedo != null)
        {
            _grainAlbedo.wrapMode = TextureWrapMode.Repeat;
            if (_grainNormal != null)
                _grainNormal.wrapMode = TextureWrapMode.Repeat;
            VRLog.Info("Cards", $"Keycap grain texture loaded ('{GrainAlbedoPath}'" +
                                $"{(_grainNormal != null ? " + normal map" : "")}) — 3D board keycaps get a " +
                                "carved wood/parchment surface (grayscale grain × per-submesh state tint).");
        }
        else
        {
            VRLog.Info("Cards", $"Keycap grain texture '{GrainAlbedoPath}' not in bundle (older bundle) — " +
                                "board keycaps keep the plain per-state tint (white _MainTex).");
        }
    }

    /// <summary>
    /// Task #5a: a BoardLit keycap material tinted <paramref name="color"/>, with the shared
    /// carved-grain texture assigned to <c>_MainTex</c> (and the normal map to <c>_BumpMap</c>
    /// when present) IF the bundle ships it. BoardLit does <c>alb = tex2D(_MainTex,uv) * _Color</c>,
    /// so a grayscale grain × the per-submesh state colour keeps the top-state / lit-bevel /
    /// dark-wall value signalling while adding surface texture. Graceful fallback: if the grain
    /// texture is absent the material is EXACTLY as before (white _MainTex, plain tint).
    /// </summary>
    internal static Material NewKeycapMaterial(Shader shader, Color color)
    {
        var m = new Material(shader) { color = color };
        EnsureGrainLoaded();
        if (_grainAlbedo != null)
        {
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _grainAlbedo);
            if (_grainNormal != null && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", _grainNormal);
        }
        // Round 15: when the bundle lacks BoardLit this material is the STANDARD fallback, which
        // renders albedo × light ≈ black in the dark VR scenes — give it the same self-illumination
        // floor as the card slab (no-op on BoardLit itself; see CardMesh.EmissionFloorFactor).
        CardMesh.ApplyEmissionFloor(m);
        return m;
    }

    /// <summary>
    /// Item 5: the lit shader for the square keycap body, falling back to Standard/Legacy/Sprites
    /// when the bundle lacks BoardLit. BoardLit shades side walls even in an unlit scene; the
    /// built-in fallbacks only shade when the scene actually has lights (and go flat otherwise).
    /// </summary>
    private static Shader? BoxCapShader() =>
        BoardLitShader()
        ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");

    /// <summary>
    /// Item 5: an emissive glow material for the slot/pick insert telegraphs. Now delegates to
    /// the shared <see cref="CardGlow.MakeGlowMaterial"/> so the board slot glow and the hand-fan
    /// insertion glow are produced by the SAME recipe (identical look). Behaviour unchanged.
    /// </summary>
    private static Material? MakeGlowMaterial(Color color) => CardGlow.MakeGlowMaterial(color);

    // ---- RenderOnTop (REMOVED) -------------------------------------------------------
    //
    // The forced draw-over-the-board helper is GONE — 0 call sites at HEAD (19 repo-wide hits:
    // 1 declaration and 18 comments, every one of them saying the widget in question no longer
    // uses it: "drop the RenderOnTop shine-through", "NO RenderOnTop", "depth-correct now"). The
    // method outlived every caller: once the readout/gear/toggle/glows were seated PROUD with
    // negative local Z, they occlude naturally and shining them through the board became a
    // regression rather than a fix.
    //
    // TWO LESSONS SURVIVE IT, and both are implemented elsewhere — WorldUI/NativeButtonSkin.cs
    // and WorldUI/ActorBars.cs. Whoever writes the next such helper needs them:
    //  - Use `.materials` (per-renderer INSTANCES), never `sharedMaterial`: the shared bundle
    //    material clothes other objects, and a shared write is global.
    //  - Set BOTH `_ZTest` AND `_ZTestMode` under HasProperty guards. The quad/Tint (Standard)
    //    path exposes `_ZTest`; TextMeshPro's distance-field material exposes `_ZTestMode`. An
    //    earlier version set only `_ZTest` under a guard and was therefore a SILENT NO-OP on
    //    every non-TMP widget (cb62991, corrected in d56e4c8).

    /// <summary>Depth-first name lookup. Delegates to <see cref="BoardAnchors.FindDeep"/> — the
    /// board's anchor names and the walk that finds them belong together, and this was one of three
    /// identical private copies.</summary>
    private static Transform? FindDeep(Transform root, string name) => BoardAnchors.FindDeep(root, name);
}
