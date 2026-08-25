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
    /// <para><b>A MISSING SEAT IS FILLED, AND SEAT 2 IS THE ONE THAT NEEDS IT.</b> Every seat has an
    /// occupant now — Confirm/Use, Undo and the turn-flow SKIP — so "absent means absent" is no
    /// longer an option for any of them: it would mean a control the player cannot reach. Two
    /// ladders, in order:
    /// <list type="number">
    ///   <item><b>SOME seats resolved</b> — the OLD TWO-ANCHOR BOARD, which is the case that is on
    ///     somebody's disk right now (the plugin ships against whatever bundle is installed). The
    ///     missing seat is EXTRAPOLATED from the resolved ones at the board's own measured pitch
    ///     (<see cref="BoardAnchors.SeatExtrapolation"/>), i.e. seat 2 lands where that board's
    ///     third recess would have been cut, and the peer mirror extrapolates identically from the
    ///     same numbers.</item>
    ///   <item><b>NO seats at all</b> — the no-bundle PROCEDURAL board. All three are synthesised at
    ///     <see cref="ButtonZoneX"/>, evenly spaced by <see cref="Defaults.StackPitchFallback"/>
    ///     about the midpoint the old hardcoded pair straddled (y −0.0075). The two synthesised
    ///     seats therefore MOVE relative to the previous build (seat 0 +0.045 → +0.069, seat 1
    ///     −0.060 → −0.0075) — on a mod-drawn slab whose two anchors were arbitrary literals.</item>
    /// </list>
    /// On a fully authored board neither ladder is reached.</para>
    /// </summary>
    private void BuildButtons(Transform?[] seats)
    {
        if (_root == null)
            return;

        // ---- THIS BOARD'S OWN RECESS PITCH, measured BEFORE anything is synthesised --------------
        // The per-board term that used to be three hand-dialled GenericButtonSpacing_{board}
        // constants, read off the anchors the board itself exports (76.5 mm Oak, 80.1 Steel, 70.1
        // Bronze — a spread no single scale factor reproduces, which is exactly why a constant could
        // not do this job for three boards). A peer's copy of the same prefab measures the same
        // number, so it costs no wire field; see BoardAnchors.StackPitch.
        //
        // BEFORE, not after: a synthesised seat's step is a number this file chose, and averaging it
        // in with the board's own would let the fallback contaminate the measurement on exactly the
        // board that still has a real one to give (the two-anchor case below).
        float? measuredPitch = BoardAnchors.StackPitch(seats);
        _seatPitch = measuredPitch ?? Defaults.StackPitchFallback;

        // ALL THREE SEATS MUST EXIST, because all three have an occupant now (Confirm/Use, Undo, the
        // turn-flow SKIP) — see the summary for the two fallback ladders and why "absent means
        // absent" stopped being an option for seat 2.
        const float fallbackMidY = -0.0075f;   // the old (+0.045, −0.060) pair's own midpoint
        for (int s = 0; s < seats.Length && s < BoardAnchors.ButtonSeatCount; s++)
        {
            if (seats[s] != null)
                continue;
            int from = BoardAnchors.NearestResolvedSeat(seats, s);
            if (from >= 0 && measuredPitch != null)
            {
                // The two-anchor board: continue ITS OWN step rather than inventing a position.
                // Parented to the resolved neighbour so the extrapolation is one local offset — the
                // very expression Net/RemoteBoardFurniture adds on its side, from the same pitch.
                seats[s] = NewSeatUnder(seats[from]!, BoardAnchors.SeatName(s),
                                        BoardAnchors.SeatExtrapolation(s, from, measuredPitch.Value));
                continue;
            }
            float y = fallbackMidY + Defaults.StackPitchFallback * ((BoardAnchors.ButtonSeatCount - 1) * 0.5f - s);
            seats[s] = NewAnchor(BoardAnchors.SeatName(s), new Vector3(ButtonZoneX, y, -0.006f));
        }
        Transform confirmParent = seats[0]!;
        Transform undoParent = seats[1]!;
        Transform skipParent = seats[2]!;

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
        // The mod owns all three seats now: Confirm on 0, Undo on 1, the turn-flow SKIP on 2. Seat 2
        // was resolved-but-unoccupied until 2026-08-25 because the Skip was drawn by
        // WorldUI/ButtonCluster.cs from a separate [RoundButtons] column and a peer solved its copy
        // term for term from record 28's ids 81..88. The user retired that group outright ("Ich
        // möchte daher, dass die Button-Gruppe der 'Überspringen Buttons' komplett verschwindet"), so
        // the cluster, the column, the geometry family and those wire fields are gone and the cap is
        // an ordinary board keycap built from the same [BoardButtons] set as its two siblings. The
        // item "Use" confirm built below is a fourth cap but NOT a fourth seat — it shares Confirm's
        // (PlayTray.GenericPrimarySlot), which is what makes "Auswahl beenden" and "Benutzen" land in
        // the same place. Nothing here reads the count: SetConfirmUndoOffset owns the layout.
        // Cap size is the TUNED size at every count (user: "der Use-Button soll genauso groß sein und
        // sich nach den Werten richten, die die generischen Buttons vorgegeben haben"). It used to be
        // run through GenericClusterButtonSize, which shrank every cap once a third member joined — so
        // the instant an item clipped into the use slot, Confirm and Undo silently resized too and the
        // whole cluster stopped matching the dialled-in values. The stack now makes room by SPACING
        // (GenericClusterY), which is itself a tuned value, so every member is exactly the size the
        // player asked for and the geometry stays theirs.
        Vector3 off = CardsConfig.ConfirmUndoOffset.Value;
        float spacing = CardsConfig.ButtonStackSpacing.Value;
        bool round = CardsConfig.GenericButtonShape(active).Value == ButtonShape.Round;

        // Category split (user: "every value applies ONLY to its own category"): the
        // Confirm/Undo keycaps read the [BoardButtons] set EXCLUSIVELY — independent
        // WIDTH/HEIGHT (rectangular keycaps), DEPTH and TRAVEL, all with the authored
        // numeric defaults (0.063 × 0.065 × 0.036 / 4 mm — no 0=Auto sentinel any more; the 0.073
        // quoted here before was ButtonTuning's pre-Bind fallback, not the bound default).
        // ROUND caps take [BoardButtons] Width as their DIAMETER since the 2026-08 retirement of
        // [Cards] ConfirmUndoSize_{board}: that dial fed ONLY this round branch after the category
        // split above moved the (default) square caps onto [BoardButtons] — with the shipped
        // Square shape it was a dead dial in the debug menu (user report). ONE family now sizes
        // Confirm/Undo in both shapes, and a [BoardButtons] edit live-rebuilds either via the
        // ButtonTuning.Version watch (ApplyButtonTuningIfChanged).
        WorldUI.ButtonTuning.Bind();
        float capDepth = WorldUI.ButtonTuning.BoardCapDepth;
        float capTravel = WorldUI.ButtonTuning.BoardCapTravel;

        // ---- FIT THE CAP TO THIS BOARD'S SEAT RECESS -------------------------------------------
        // (RESTORED: this block was lost in the ModBuild 272 merge while the PEER side of the very
        // same fit — Net/RemoteBoardFurniture's FitCapSize call — landed. On dev as merged, the owner
        // built unfitted caps and every peer drew fitted ones: a straight 1:1 divergence on the
        // control the 1:1 ruling is about. Both sides call the one BoardAnchors.FitCapSize again.)
        //
        // The tuned [BoardButtons] pair is 0.063 × 0.065 m — Defaults.BoardButtons_Width/Height, which
        // is what his cfg holds; ButtonTuning.DefaultBoardWidth's 0.073 is only the PRE-BIND fallback
        // inside Clamped() and is never the live cap. The three re-authored boards cut their button
        // recesses at 74.6 × 64.3 (Oak), 81.0 × 70.1 (Steel) and 61.2 × 51.9 mm (Bronze) of usable
        // FLOOR, so the 65 mm height does not fit any of the three and Bronze's width does not fit
        // either. The fit SHRINKS the cap to the board's own recess and never grows it, so the global
        // stays the ceiling the user dialled in rather than a value this code rewrites — see
        // BoardAnchors.FitCapSize for the whole argument, and _seatMinHalf for why the tightest seat
        // is the one that decides.
        //
        // THE MARGIN IS THE CAP'S OWN TRAVEL, clamped into a sane band: [BoardButtons] Travel is the
        // one LENGTH in the cap's tuning family that means clearance rather than size, and it is
        // already how far the cap moves inside this well on every press. The clamp is what stops a
        // Travel of 0 from producing a cap that fills the recess edge to edge (and an extreme one
        // from eating the cap); the band's ends are the 1 mm the assembler already uses as its
        // "a hair proud" seat clearance and the 8 mm fingertip radius the poke test works to.
        float seatMargin = Mathf.Clamp(capTravel, 0.001f, 0.008f);
        Vector2 tunedSize = new(WorldUI.ButtonTuning.BoardCapWidth, WorldUI.ButtonTuning.BoardCapHeight);
        Vector2 fitted = BoardAnchors.FitCapSize(tunedSize, _seatMinHalf, seatMargin);
        // ROUND caps take [BoardButtons] Width as their DIAMETER since the 2026-08 retirement of
        // [Cards] ConfirmUndoSize_{board}: that dial fed ONLY this round branch after the category
        // split above moved the (default) square caps onto [BoardButtons] — with the shipped
        // Square shape it was a dead dial in the debug menu (user report). ONE family now sizes
        // Confirm/Undo in both shapes, and a [BoardButtons] edit live-rebuilds either via the
        // ButtonTuning.Version watch (ApplyButtonTuningIfChanged).
        //
        // A DISC MUST FIT BOTH AXES, so the fitted diameter is min(W, H) and not the fitted width:
        // the recesses are WIDER than they are tall on all three boards (they are rounded rects on a
        // short-axis stack of three), so taking the width would have put a disc through the top and
        // bottom recess walls on every board. Before the fit this branch read Width alone and was
        // correct only because nothing constrained it. RemoteBoardFurniture.GenericCap takes the same
        // min().
        float side = Mathf.Min(fitted.x, fitted.y); // round-cap diameter
        // Square caps keep the tuned [BoardButtons] W×H wherever the seat allows it, whatever the
        // member count — the item "Use" confirm is built from the very same rectSize/depth/travel
        // below, so it is identical to Confirm and Undo by construction and follows every tuning
        // change with them.
        var rectSize = round ? new Vector2(side, side) : fitted;
        // The LAYOUT needs the size too: BoardAnchors.ClampSeatPose bounds a cap's in-plane offset by
        // the gap between THIS cap and the recess wall, so SetConfirmUndoOffset has to know what was
        // actually built. Written before the first layout call at the end of this method.
        _capRectSize = rectSize;

        // Initial labels are overwritten by the live game-widget label each TickStatus
        // (ConfirmLabel()/UndoLabel()); route the fallback literals through the game keys.
        _confirm = BoardButton.Create(confirmParent, rectSize,
            new Color(0.35f, 0.46f, 0.28f), // T4: muted sage green — antique, still clearly "go"
            Core.Loc.Game("GUI_CONFIRM", "Confirm"),
            () => ConfirmRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board,
            // The SYMBOL carved into this cap's face, and the board whose material carries it.
            // The four generic caps KEEP their live text as well (user, explicitly: "Die
            // generischen Buttons haben immer unterschiedlichen Text darauf, d.h. auf denen sollte
            // der Text auch erhalten bleiben") — CapSymbols moves the caption into the band below
            // the symbol rather than replacing it.
            capRole: CapRole.Confirm, capStyle: active);
        _confirm.WireCap = Net.NetProtocol.CapPressConfirm; // mirror this cap's press dip to peers
        _confirm.DisabledReason = CardsGameApi.DescribeConfirmGate; // built only on rejection
        _confirm.ActivationGuard = ConfirmGuardRemaining; // accident window (test #19)
        RegisterLaserTarget(_confirm.Collider!, _confirm);

        _undo = BoardButton.Create(undoParent, rectSize,
            new Color(0.44f, 0.31f, 0.20f), // T4: worn leather brown (kept — already antique)
            Core.Loc.Game("GUI_UNDO", "Undo"),
            () => UndoRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board,
            capRole: CapRole.Undo, capStyle: active);
        _undo.WireCap = Net.NetProtocol.CapPressUndo;
        _undo.DisabledReason = CardsGameApi.DescribeUndoGate;
        RegisterLaserTarget(_undo.Collider!, _undo);

        // THE TURN-FLOW SKIP, on the board's third recess. Built from the SAME rectSize, the SAME
        // depth, the SAME travel, the SAME shape branch and the same cap category as Confirm and
        // Undo — that identity is the requirement, not an implementation detail ("so dass all diese
        // buttons gleich aussehen"). What it keeps of its old self is its ACCENT COLOUR, verbatim
        // the (0.37, 0.44, 0.56) antique slate-blue the retired cluster built its skip cap in
        // (WorldUI/ButtonCluster.Build) — so the one thing a player recognised the control by
        // survives the move, and the peer mirror's own SkipColor already holds that exact triple.
        // Its two siblings keep theirs the same way: sage "go" for Confirm, worn leather for Undo.
        _skip = BoardButton.Create(skipParent, rectSize,
            new Color(0.37f, 0.44f, 0.56f), // antique slate-blue — the retired cluster cap's accent
            Core.Loc.Game("GUI_SKIP_MOVEMENT", "Skip"),
            () => SkipRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board,
            capRole: CapRole.Skip, capStyle: active);
        _skip.WireCap = Net.NetProtocol.CapPressSkip;
        _skip.DisabledReason = CardsGameApi.DescribeSkipGate;
        RegisterLaserTarget(_skip.Collider!, _skip);

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
            capCategory: WorldUI.ButtonTuning.CapCategory.Board,
            capRole: CapRole.ItemUse, capStyle: active);
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
        // The SKIP cap's resting state is HIDDEN — the game raises its widget only on a skippable
        // step — so a first build (null) hides it too rather than showing a cap for one frame.
        if (_skipShownBeforeRebuild != true)
            _skip?.SetVisible(false);
        _confirmShownBeforeRebuild = null;
        _undoShownBeforeRebuild = null;
        _skipShownBeforeRebuild = null;
        // The item-use cap is the one whose resting state is HIDDEN: it exists on every board from
        // frame one and only shows while a usable card is clipped into the recess.
        if (!_itemUseActive)
            _itemUseConfirm?.SetVisible(false);

        SetConfirmUndoOffset(off, spacing); // Confirm+Use share the top seat, Undo the middle, Skip the bottom
        VRLog.Info("Cards", $"Board: Confirm/Undo/Skip built as 3D {(round ? "round" : "square")} keycaps " +
                            $"{rectSize.x:F3}×{rectSize.y:F3} m for {active} ([Cards] ConfirmUndoOffset {off}, " +
                            $"ButtonStackSpacing ×{spacing:F3} on this board's own measured " +
                            $"{_seatPitch * 1000f:F1} mm recess pitch; " +
                            $"seat 0 '{confirmParent.name}' y{GenericSeatY(0, spacing) * 1000f:+0.0;-0.0} mm, " +
                            $"seat 1 '{undoParent.name}' y{GenericSeatY(1, spacing) * 1000f:+0.0;-0.0} mm, " +
                            $"seat 2 '{skipParent.name}' y{GenericSeatY(2, spacing) * 1000f:+0.0;-0.0} mm)" +
                            (round ? "." : " — square caps are beveled keycaps: state-colour top + BRIGHT lit bevel ring + dark warm walls (3-submesh, high contrast) for unmistakable 3D."));
        VRLog.Info("Cards", _seatMinHalf != null
            ? $"Board: cap size FITTED to the '{active}' seat recess — tuned " +
              $"{tunedSize.x * 1000f:F1} × {tunedSize.y * 1000f:F1} mm, recess floor " +
              $"{_seatMinHalf.Value.x * 2000f:F1} × {_seatMinHalf.Value.y * 2000f:F1} mm, margin " +
              $"{seatMargin * 1000f:F1} mm/side ([BoardButtons] Travel) ⇒ built " +
              $"{rectSize.x * 1000f:F1} × {rectSize.y * 1000f:F1} mm, leaving " +
              $"±({BoardAnchors.SeatSlack(_seatMinHalf.Value, rectSize).x * 1000f:F1}, " +
              $"{BoardAnchors.SeatSlack(_seatMinHalf.Value, rectSize).y * 1000f:F1}) mm of in-well slack " +
              "for the tuned offset to move it in."
            : $"Board: cap size NOT fitted — the '{active}' board carries no SeatExtent measurement, so " +
              $"the caps are the tuned {tunedSize.x * 1000f:F1} × {tunedSize.y * 1000f:F1} mm and their " +
              "offsets are unclamped, exactly as before.");
        VRLog.Info("Cards", $"Board: button geometry config applied — {WorldUI.ButtonTuning.Describe()}.");
        _tuningVersion = WorldUI.ButtonTuning.Version; // fresh build reflects current config
        _capGeometryKey = CapGeometryKey(); // what these caps were BUILT from (see RebuildAttachedControls)
    }

    /// <summary>
    /// ONE LINE PER CAP SAYING WHERE IT ACTUALLY LANDED — cap centre in board-local metres, the seat
    /// anchor it hangs off, the gap between them, and whether the cap is still ON THE BOARD.
    ///
    /// <para><b>WHY THIS EXISTS, and it is not decoration.</b> The keycaps are seated at
    /// <c>seat anchor + [Cards] ConfirmUndoOffset_{board}</c>. Those offsets are hand-tuned, and two
    /// of the three shipped ones are not nudges at all: <c>ConfirmUndoOffset_Steel.x = +0.462</c> and
    /// <c>_Bronze.x = +0.447</c> — 46 cm, on a board 64 cm wide. They are that large because the
    /// SHIPPED Steel and Bronze boards had their zones MIRRORED against Oak (button pads on the −X
    /// side, rest pads on +X), so the user dialled the whole cluster across the board to put it back
    /// on the right-hand side. The re-authored boards are canonical — seats on +X on all three — and
    /// the same dial now pushes the cluster 46 cm FURTHER right, i.e. ~37 cm clear off the board.
    /// The identical trap sits on <c>RestButtonOffset_Steel.x = −0.44</c> / <c>_Bronze.x = −0.445</c>.</para>
    ///
    /// <para>Nothing here CHANGES a value — those are his numbers and a silent flip is the defect
    /// this project keeps paying for. What it does is make the failure NAMED instead of a board with
    /// invisible buttons: it prints the offending dial, the measured board half-width, and the exact
    /// offset that would put the cap back in its recess. A log line beats a screenshot, and a
    /// screenshot beats a bug report.</para>
    /// </summary>
    private void LogSeatOccupancy(Transform confirmParent, Transform undoParent, Transform?[] seats,
                                  Vector2 capSize)
    {
        if (_root == null || _confirm == null)
            return;
        MeasureBoardLocalExtents(_root, out float topLocalY, out float halfLocalX);
        Vector3 dialled = CardsConfig.ConfirmUndoOffset.Value;

        for (int seat = 0; seat < seats.Length; seat++)
        {
            Transform? anchor = seat == 0 ? confirmParent : seat == 1 ? undoParent : seats[seat];
            if (anchor == null)
                continue;
            BoardButton? cap = seat == 0 ? _confirm : seat == 1 ? _undo : _skip;
            Vector3 anchorLocal = _root.InverseTransformPoint(anchor.position);
            Vector3 capLocal = cap != null
                ? _root.InverseTransformPoint(cap.transform.position)
                : anchorLocal + SeatLocalPose(seat);   // an unoccupied seat: where a cap WOULD land

            float overX = Mathf.Abs(capLocal.x) + capSize.x * 0.5f - halfLocalX;
            float overY = Mathf.Abs(capLocal.y) + capSize.y * 0.5f - topLocalY;
            bool offBoard = overX > 0f || overY > 0f;
            string who = seat == 0 ? "CONFIRM/USE" : seat == 1 ? "UNDO" : "SKIP";
            string line = $"Board: {who} seat {seat} — anchor local ({anchorLocal.x:F4}, {anchorLocal.y:F4}), " +
                          $"cap local ({capLocal.x:F4}, {capLocal.y:F4}), board half-extent " +
                          $"({halfLocalX:F4}, {topLocalY:F4}) m.";
            if (!offBoard)
            {
                VRLog.Info("Cards", line);
                continue;
            }
            // The correction is stated, not applied: what [Cards] ConfirmUndoOffset would have to be
            // for this cap to sit centred in its own recess (i.e. cancel the anchor-relative drift
            // and keep only the tuned Z proud depth).
            VRLog.Warn("Cards", line +
                $" OFF THE BOARD by ({Mathf.Max(0f, overX) * 1000f:F0}, {Mathf.Max(0f, overY) * 1000f:F0}) mm. " +
                $"[Cards] ConfirmUndoOffset is currently ({dialled.x:F3}, {dialled.y:F3}, {dialled.z:F3}); " +
                $"an offset of (0.000, 0.000, {dialled.z:F3}) would centre the cap in its authored recess. " +
                "This value was tuned against a board whose button zone was on the OTHER side — it is a " +
                "46 cm relocation, not a nudge — and the re-authored boards put the seats where the " +
                "offset used to have to reach. NOT auto-corrected: it is a tuned value, and the same " +
                "applies to [Cards] RestButtonOffset.");
        }
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
    private bool? _skipShownBeforeRebuild;

    /// <summary>
    /// A synthesised button seat hung off a RESOLVED one, at a local offset — the two-anchor board's
    /// extrapolated third recess. Unlike <see cref="NewAnchor"/> this keeps the parent's own Z (the
    /// offset carries none), because the neighbour is a real authored anchor already seated at the
    /// board's own proud depth: re-seating it at <c>FixedProudZ</c> would put one cap of the column
    /// at a different depth from the other two.
    /// </summary>
    private Transform NewSeatUnder(Transform parent, string name, Vector3 localOffset)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, worldPositionStays: false);
        t.localPosition = localOffset;
        t.localRotation = Quaternion.identity;   // the parent anchor already faces the board face
        VRLog.Info("Cards", $"Board: '{name}' EXTRAPOLATED from '{parent.name}' at " +
                            $"({localOffset.x:F4}, {localOffset.y:F4}, {localOffset.z:F4}) m — this board " +
                            "supplies no such recess, so the seat continues the pitch its own anchors " +
                            "define. A peer's mirror extrapolates from the same two numbers.");
        return t;
    }

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

    // THE FIXED 7 mm CHAMFER IS GONE (round 2, 2026-08-25). It was a length in METERS applied to
    // caps whose fitted sizes run from 53.2 x 43.9 mm (Bronze) to 63.0 x 62.1 mm (Steel), so the
    // same constant was 0.159 of Bronze's short side and 0.113 of Steel's — and on Bronze it left a
    // field too small to hold two lines of caption, which is the whole of the "AUSWAHL BEEN"
    // report. The bezel is a FRACTION of the cap's own short side now, and it is a five-zone signet
    // profile rather than one chamfer: CapFaceLayout.Bezel{Chamfer,Rim,Step}, applied inside
    // CardMesh.BuildBeveledKeycap. Nothing here passes it, so nothing here can disagree with the
    // atlas generator about where the field ends.

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
        _skip?.LogCapDiagnostics("Skip");
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
    /// <summary>
    /// THE IDLE FACE COLOUR OF AN ENABLED, UN-ACCENTED BOARD KEYCAP — PER BOARD since round 2.
    ///
    /// <para><b>THIS SUMMARY DESCRIBES THE ModBuild 286 SOLVE, WHICH WAS SUPERSEDED IN ROUND 5.
    /// READ THE REMARKS BELOW FIRST</b> — its numbers, its palette and its three values are all
    /// the PREVIOUS state, kept because the reasoning that produced them is still the reasoning
    /// a future round will be tempted to repeat, and the remarks say why it must not.</para>
    ///
    /// <para><b>THE USER'S COMPLAINT, and why this is the lever.</b> <i>"Die Textur die dort gewählt
    /// ist, ist einheitlich und passt sonst nicht wirklich zum Styl."</i> ModBuild 281 measured the
    /// three idle cap faces at CIELAB ΔE 13.8 (oak↔steel), 10.3 (steel↔bronze) and <b>3.7
    /// (oak↔bronze)</b> — oak and bronze were, measurably, the same colour. That round found the
    /// cause and could not act on it: <c>BoardLit</c> computes <c>alb = tex2D(_MainTex, uv) *
    /// _Color</c>, so a per-board plate only ever MODULATES this colour, and one strong warm
    /// parchment × the <c>[ButtonColors] BoardCapTint</c> of 0.5, applied identically on all three
    /// boards, is a term a ±20 % material cast cannot survive. A CHROMA BOOST was tried and
    /// rejected ON MEASUREMENT (ΔE 3.7 → 3.9 → 3.1 for k = 1.0 … 2.6, and 99.99 % of oak's texels
    /// clipped at k = 1.6): the separation lives in the already-crushed blue channel. The one lever
    /// that works is this colour, and it was left alone because it is the user's tuning. His report
    /// is the authorisation.</para>
    ///
    /// <para><b>THE OLD VALUE WAS <c>(0.600, 0.510, 0.350)</c> ON ALL THREE BOARDS</b> — written
    /// here so it can be put back in one edit. The three below were SOLVED, not chosen: over a
    /// hue × chroma grid, maximising the minimum pairwise ΔE subject to (a) each cap's rendered
    /// LUMINANCE staying within ±2 % of what it is today — so nothing gets brighter or darker, only
    /// differently coloured — (b) under 0.5 % clipped texels, and (c) staying inside the board's own
    /// authored palette band (oak parchment/pale honey, steel pewter, bronze brass/warm gold).
    /// Measured result, through the shipped gamma chain and BoardLit's flat-face shade:</para>
    /// <code>
    ///   pair            ΔE before   ΔE after
    ///   oak ↔ steel        12.4        20.8
    ///   steel ↔ bronze      9.5        35.8
    ///   oak ↔ bronze        2.8        17.2     &lt;- the pair that read as one colour
    /// </code>
    /// <para>Luminance moved by −0.27 % (oak), +0.05 % (steel) and −0.28 % (bronze) against the caps
    /// that are on the board today, and no texel clips — the brightest channel any admissible face
    /// can reach is 0.415, and <c>normalise_plate</c> clips the plate to [0, 1], so clipping is
    /// impossible by construction rather than by luck. The full derivation, its instrument
    /// validation (it reproduces the ModBuild 281 record's own three numbers to 0.04 ΔE) and its
    /// null control are in <c>.planning/debug/keycaps2/plates_deltae.txt</c>.</para>
    ///
    /// <para>A cap with NO board (the map room's keycap-skinned furniture) keeps the original warm
    /// parchment: it has no board to belong to, and changing it would be a change nobody asked
    /// for.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>ROUND 5 REPLACED ALL THREE, AND THE REASON IS THAT THE SOLVE ABOVE HIT ITS TARGET
    /// AND THE TARGET WAS WRONG.</b> <i>"Ich mag die Textur gar nicht. Sie passt überhaupt nicht zu
    /// dem jeweiligen Board."</i> (user, 2026-08-25 — the fourth rejection, and the third naming
    /// the material rather than the shape.) The three values above were solved to sit inside
    /// <c>unity/board-prep/buttons/cap_deltae.PALETTE</c>, a hue+chroma window per board authored
    /// as "oak parchment / steel pewter / bronze brass". Those windows were written BEFORE any
    /// picture existed of what a button on that board should look like. Measured against the
    /// boards themselves, which sit at CIELAB hue 65.6° / 58.5° / 91.8°, the shipped caps landed
    /// at 91.4° / 223.8° / 65.6° — <b>+25.8°, +165.3° and −26.1° from their own boards</b>, and
    /// bronze was inside its authored window the whole time. A solver maximising separation
    /// inside an unvalidated palette gives three caps maximally different from each other and
    /// belonging to nothing.</para>
    ///
    /// <para><b>WHY THE COLOUR COULD NOT LIVE IN THE TEXTURE BEFORE, AND CAN NOW.</b>
    /// <c>BoardLit</c> computes <c>alb = tex2D(_MainTex, uv) * _Color</c>, so the cap's hue is the
    /// PRODUCT of the plate and this colour. <c>cap_atlas.normalise_plate</c> re-based every plate
    /// to mean <b>0.837</b> — the mean of the greyscale texture the plates replaced, copied across
    /// because it was there and required by nothing. Reaching it took gain 3.46 on oak and clipped
    /// <b>68.5 %</b> of oak's field texels in at least one channel, so oak's modulator was very
    /// nearly a CONSTANT and the rendered cap was whatever this colour said. Round 5 solves the
    /// plate's level against the guarantee that actually constrains it instead (see
    /// <see cref="WorldUI.ButtonTuning.SeatedCapColor"/> and <c>cap_atlas.FIELD_TARGET_LUM</c>);
    /// oak's clipping falls to 3.9 % and the rendered field's contrast rises 4.12 % → 11.63 %.
    /// With the plate no longer flat there is a colour here that lands the product exactly on the
    /// board's own material, and these are it.</para>
    ///
    /// <para><b>THESE THREE AND <c>cap_atlas.FIELD_TARGET_LUM</c> ARE ONE DECISION.</b> Each is
    /// <c>target ÷ (fieldMean × shade × BoardCapTint)</c> for its board, so re-basing the atlas
    /// without re-solving these puts the caps back off-colour and nothing in the build will say
    /// so. <c>cap_belong.py --report</c> reads the shipped PNG and asserts the pair still agree.
    /// Measured, through the shipped gamma chain and <c>BoardLit</c>'s flat-face shade, with
    /// CIEDE2000 against the round-4 option each cap was built from, re-exposed to the cap's own
    /// luminance (the option is a studio shot at L* 36–42; the cap renders at L* 20–24, and
    /// comparing those lightnesses measures the exposure rather than the material):</para>
    /// <code>
    ///   board    ΔE2000 vs its option      hue error vs its own board     cap-to-cap ΔE2000
    ///            full        hue+chroma    before      after              before   after
    ///   oak      20.7 → 13.9  15.4 → 4.5   +25.8° →  −1.4°                oak↔st  14.1 → 14.8
    ///   steel    15.8 → 12.0  10.5 → 1.4  +165.3° →  +7.2°                oak↔br  12.1 → 13.6
    ///   bronze   19.9 → 17.0  12.2 → 4.1   −26.1° →  +3.4°                st↔br   23.1 →  8.2
    /// </code>
    /// <para>The after-hue errors are the OPTIONS' OWN errors against their boards, because the
    /// cap now IS that material at a different exposure — this is exact, not optimised.</para>
    ///
    /// <para><b>THE ONE NUMBER THAT GOT WORSE, AND WHY IT WAS NOT WORTH DEFENDING.</b> Steel↔bronze
    /// falls from 23.1 to 8.2. That is close to the 6.8 collapse a chroma-safe normaliser was
    /// rejected for in ModBuild 289, and it is NOT the same failure. 6.8 was three boards
    /// converging on ONE hue because a neutral modulator left only a shared state colour to tell
    /// them apart. 8.2 is the separation pewter and patinated bronze genuinely have at this
    /// brightness — the caps sit at hue 64.2° / 65.7° / 95.2°, and steel is told from oak by
    /// chroma (C* 3.6 vs 26.8), not by hue. The 23.1 was manufactured: it was steel rotated to
    /// BLUE and bronze rotated to ORANGE, a separation invented by the state colours and present
    /// in neither material. Three caps that are far apart and all wrong is the defect, not the
    /// bar. Cap-to-cap ΔE improves on the other two pairs.</para>
    ///
    /// <para>Luminance is held: each board's rendered idle face keeps the luminance it has today
    /// to within 0.5 %, so this round changes colour and nothing else. Every channel stays inside
    /// <c>[CapWellColor × CapSeatContrast ÷ BoardCapTint, 1]</c>, so
    /// <see cref="WorldUI.ButtonTuning.SeatedCapColor"/> never lifts one channel and not another —
    /// a floor that engages asymmetrically is a hue shift nobody solved for.</para>
    ///
    /// <para><b><c>[ButtonColors] BoardCapTint</c> WAS NOT TOUCHED</b>, though the ModBuild 289
    /// note names it as "the lever that would free this properly". It is the user's own tuning
    /// surface, a peer clamps it to [0, 1] on the mirror path
    /// (<c>RemoteBoardFurniture</c>), and the headroom this needed was already sitting unused in
    /// the constant right here. Reaching for a config round first would have been a change to
    /// four tint families to avoid changing three numbers.</para>
    /// </remarks>
    internal static Color BoardIdleColor(ControlBoard? style) => style switch
    {
        // SOLVED by unity/board-prep/buttons/cap_belong.py --solve; see the remarks above. The
        // C* / hue quoted is of the RENDERED FACE, not of this colour, which is a modulator's
        // partner and not a colour anything displays on its own.
        ControlBoard.Oak => new Color(0.759f, 0.539f, 0.456f),      // renders oak, h 64.2 C* 26.8
        ControlBoard.Steel => new Color(0.588f, 0.557f, 0.506f),    // renders pewter, h 65.7 C* 3.6
        ControlBoard.Bronze => new Color(0.549f, 0.547f, 0.529f),   // renders patina, h 95.2 C* 13.0
        _ => new Color(0.600f, 0.510f, 0.350f),
    };

    internal static Material NewKeycapMaterial(Shader shader, Color color) =>
        NewKeycapMaterial(shader, color, CapRole.Plain, style: null);

    /// <summary>
    /// THE ONE PLACE A BOARD KEYCAP GETS ITS SURFACE — the owner's caps and every peer's mirror of
    /// them both come through here, which is what makes the two identical BY CONSTRUCTION rather
    /// than by two rules that agree.
    ///
    /// <para><paramref name="style"/> non-null selects that board's own keycap ATLAS
    /// (<see cref="CapSymbols"/>: carved oak / forged iron / cast bronze, one texture per board)
    /// and <paramref name="role"/> selects the CELL inside it, i.e. which symbol is carved into
    /// the face. Both are pushed onto the material's texture transform, which is all BoardLit
    /// needs: it runs <c>o.uv = TRANSFORM_TEX(v.uv, _MainTex)</c> and samples <c>_BumpMap</c> and
    /// <c>_MRSMap</c> with that same <c>i.uv</c>, so one ST write re-aims every map at once. The
    /// _BumpMap transform is written too, because the STANDARD fallback shader (bundle without
    /// BoardLit) gives the normal map its own ST and would otherwise sample the whole atlas
    /// through a cell-sized albedo.</para>
    ///
    /// <para><paramref name="style"/> null — or a bundle with no atlas for that style — keeps the
    /// shared <c>KeycapGrain</c> pair and writes NO transform, so the material is exactly what it
    /// was before this existed. That is the path the map-room's own keycap-skinned furniture takes
    /// (<c>MapButtonRail</c>, <c>MapTableLegs</c>): they want the keycap SURFACE, they are not
    /// board buttons, and they must not start wearing a board's symbols.</para>
    /// </summary>
    internal static Material NewKeycapMaterial(Shader shader, Color color, CapRole role, ControlBoard? style)
    {
        var m = new Material(shader) { color = color };
        if (style != null && CapSymbols.TryAtlas(style.Value, out Texture2D atlas, out Texture2D? atlasNormal))
        {
            CapSymbols.CellTransform(atlas, role, out Vector2 scale, out Vector2 offset);
            if (m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", atlas);
                m.SetTextureScale("_MainTex", scale);
                m.SetTextureOffset("_MainTex", offset);
            }
            if (atlasNormal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", atlasNormal);
                m.SetTextureScale("_BumpMap", scale);
                m.SetTextureOffset("_BumpMap", offset);
            }
            CardMesh.ApplyEmissionFloor(m);
            return m;
        }
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
    /// Re-aim a LIVE keycap material at a different <see cref="CapRole"/>'s cell, in place.
    ///
    /// <para>The follow/pin toggle is the reason this exists: it means two different things
    /// ("FIXIERT" — an anchor; "FOLGEN" — two footprints) and it flips between them at a press.
    /// A role is a sub-rectangle of one texture, so the flip is two float2 writes on a material
    /// instance — no rebuild, no second texture, and nothing for the dust dissolve to play over.
    /// A no-op when the bundle has no atlas for this style, so the toggle keeps its word label.</para>
    ///
    /// <para>Returns true when the material really was re-aimed, so a caller can keep its own
    /// state in step with what is drawn rather than assuming.</para>
    /// </summary>
    internal static bool SetKeycapRole(Material? m, CapRole role, ControlBoard style)
    {
        if (m == null || !CapSymbols.TryAtlas(style, out Texture2D atlas, out Texture2D? atlasNormal))
            return false;
        CapSymbols.CellTransform(atlas, role, out Vector2 scale, out Vector2 offset);
        if (m.HasProperty("_MainTex"))
        {
            m.SetTextureScale("_MainTex", scale);
            m.SetTextureOffset("_MainTex", offset);
        }
        if (atlasNormal != null && m.HasProperty("_BumpMap"))
        {
            m.SetTextureScale("_BumpMap", scale);
            m.SetTextureOffset("_BumpMap", offset);
        }
        return true;
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
