using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 3 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Regions: debug-menu live apply, the fixed base positions for board-attached elements,
// ReapplyOrientation, rebuild helpers, board-switch pose, PersistPoseToConfig.

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ debug-menu live apply --

    /// <summary>
    /// PART F live-apply: move AND size the slot snap-glow / wanted-glow overlays in place (no
    /// rebuild). Base local-Z is preserved; the offset adds on top.
    /// Item 1: the two overlays are a PAIR, so a per-board SPACING spreads them apart along
    /// the slot-local X (the board's long/inter-slot axis) — slot 0 (left) −½, slot 1 (right) +½.
    /// 2026-08-11: the SIZE rides along here too, because it is the same dial as the resting card's
    /// (<see cref="SlotCardScale"/>) and the re-home at the bottom of this loop already applies that
    /// half — resizing anywhere else would let the two drift apart for a frame.
    /// </summary>
    internal void SetOverlayOffset(Vector3 offset, float spacing)
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        float snap = SnapGlowScale;
        float wanted = WantedGlowScale;
        for (int i = 0; i < 2; i++)
        {
            float xSpread = (i == 0 ? -0.5f : 0.5f) * spacing; // spread the pair apart along the slot axis
            if (_slotHighlights[i] != null)
            {
                _slotHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, SlotGlowBaseZ + offset.z);
                _slotHighlights[i]!.transform.localScale = new Vector3(w * snap, h * snap, 1f);
            }
            if (_wantedHighlights[i] != null)
            {
                _wantedHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, WantedGlowBaseZ + offset.z);
                _wantedHighlights[i]!.transform.localScale = new Vector3(w * wanted, h * wanted, 1f);
            }
            // (The round-13 SEAT LINER used to be re-seated here as a third member of the overlay
            // group. It is GONE — user report 2026-08-13, verbatim: "Uns ist aufgefallen das unter
            // den Kartenoverlays noch so ein weiteres Brett liegt. Verschiebt man die Kartenoverlays
            // geht dieses Brett mit. … Dieses Brett das zu den overlays gehört soll komplett weg."
            // The overlay group is now exactly what its name says: the two glows and the card seat.)
            // Item B: the placed card's resting spot follows the SAME overlay offset/spread — the
            // glow AND the physical card move together. Re-home any resting (non-held) occupant now;
            // freshly placed cards read the coupled offset via SlotHomeOffsetFor.
            VRCard? occ = _occupants[i];
            if (occ != null && !occ.IsHeld)
                occ.SetHome(_slots[i]!, SlotHomeOffsetFor(i), Quaternion.identity, SlotCardScale);
        }
    }

    /// <summary>
    /// PART F live-apply, but for the BOARD MESH ITSELF: pose the visual asset (per-board offset
    /// in board-local meters + pitch/yaw/roll degrees about the board root) WITHOUT moving
    /// anything that docks to it. BoardTilt tilts the WHOLE board — this is the complementary
    /// control the user asked for. Mechanism: the bundle anchors (the four frame anchors plus the
    /// board's button seats — <c>PlayTray.LiveBoardAnchors</c>) are descendants of the
    /// visual, so after posing the mesh their captured root-local poses are written back,
    /// pinning slots, rest tokens and Confirm/Undo (and every element attached to them) exactly
    /// where they were. The mesh collider rides the mesh, so the laser lands on what you see.
    /// </summary>
    internal void SetAssetPose(Vector3 offset, Vector3 euler)
    {
        if (_visual == null || _root == null)
            return;
        // THE MESH MAY NOT LEAVE THE CONTROLS THAT SIT ON IT — the sibling of the seat clamp, and
        // clamped HERE rather than at the two call sites (EnsureBuilt and CardsDriver's live
        // apply) so neither can forget and the live menu shows the pose that is actually in force.
        // On a board with no measured recess this is a no-op by construction. See
        // BoardAnchors.ClampAssetPose for the measurement that made this necessary.
        Vector3 reqOffset = offset, reqEuler = euler;
        BoardAnchors.ClampAssetPose(ref offset, ref euler, _assetAnchorRadius);
        if (BoardAnchors.AssetPoseWasClamped(reqOffset, reqEuler, offset, euler))
            VRLog.Info("Cards", $"Board mesh pose CLAMPED: requested offset {reqOffset:F3} / euler " +
                                $"{reqEuler:F1}° would have walked the mesh out from under the pinned " +
                                $"control set; applied {offset:F3} / {euler:F1}° " +
                                $"(anchor radius {_assetAnchorRadius:F3} m, lift budget " +
                                $"{BoardAnchors.MaxAnchorLift * 1000f:F1} mm). These dials were tuned " +
                                "on a board this bundle no longer carries.");
        _visual.localPosition = _visualBasePos + offset;
        _visual.localRotation = Quaternion.Euler(euler) * _visualBaseRot;
        for (int i = 0; i < _assetPinnedAnchors.Count; i++)
        {
            (Transform t, Vector3 lp, Quaternion lr) = _assetPinnedAnchors[i];
            if (t == null)
                continue;
            t.position = _root.TransformPoint(lp);
            t.rotation = _root.rotation * lr;
        }
    }

    /// <summary>PART F live-apply: move the initiative-track mount to a new per-board local position.</summary>
    internal void SetInitiativeOffset(Vector3 offset)
    {
        if (_initiativeMount != null)
            _initiativeMount.localPosition = offset;
    }

    /// <summary>
    /// PART F live-apply: re-seat every member of the generic cluster from the shared
    /// <c>[Cards] ConfirmUndoOffset</c> nudge and the shared <c>[Cards] ButtonStackSpacing</c>
    /// multiplier (instant). Confirm takes seat 0, Undo seat 1, the turn-flow SKIP seat 2 — and the
    /// item "Use" confirm is written to the SAME seat as Confirm (see
    /// <see cref="GenericPrimarySlot"/>). Every pose is ANCHOR-LOCAL: each cap hangs off its own
    /// recess anchor, so what is written here is the tuned nudge plus the stack spread on top of
    /// where the MESH puts that seat (<see cref="GenericSeatY"/>), never the seat's whole position.
    /// </summary>
    internal void SetConfirmUndoOffset(Vector3 offset, float spacing)
    {
        // Item D: the generic cluster is a vertical stack laid out by GenericSeatY. Confirm takes the
        // PRIMARY seat (0), Undo seat 1, the turn-flow SKIP seat 2 — one cap per physical recess.
        // At the shipped spacing of 1 every term GenericSeatY contributes is exactly ZERO, so what
        // renders is the board's own authored layout and nothing else.
        // ONE SEAT FOR THE COMMIT CAP — computed once, written to BOTH caps (user, verbatim: "Die
        // Position des Buttons 'Auswahl beendet' bei den allgemeinen Buttons sollte EXAKT die gleiche
        // sein wie der 'Benutzen' button der erscheint wenn ein Item in dem Slot liegt und genutzt
        // werden kann. Aktuell gibt es da einen Offset auf der Y-Achse."). See GenericPrimarySlot for
        // the mechanism that produced that offset and why the CONFIRM slot is the surviving seat.
        _seatOffset = offset;
        _seatSpacing = spacing;
        // …AND EVERY CAP STAYS INSIDE THE RECESS IT SITS IN. On a board that carries a measured seat
        // (BoardAnchors.SeatExtent — i.e. one the assembler cut and measured), the in-plane part of
        // the tuned offset and the whole stack term are bounded by the slack between this cap and the
        // recess wall; Z is untouched. On a board with no measurement — every bundle shipped so far,
        // and the procedural fallback — nothing is bounded and this is bit-identical to the previous
        // build, Oak's tuned 8 mm nudge included. The whole argument, and why this is NOT a
        // canonical/mirrored test, is on BoardAnchors.ClampSeatPose.
        Vector3 primary = BoardAnchors.ClampSeatPose(
            offset, GenericSeatY(GenericPrimarySlot, spacing), _seatMinHalf, _capRectSize);
        if (_confirm != null)
            _confirm.transform.localPosition = primary;
        // Written unconditionally, not only while active: the cap is built on the SAME anchor as
        // Confirm and merely hidden when idle, so seating it every time removes the one state the old
        // code had to be in for the two to agree.
        if (_itemUseConfirm != null)
            _itemUseConfirm.transform.localPosition = primary;
        Vector3 undoPose = BoardAnchors.ClampSeatPose(
            offset, GenericSeatY(GenericUndoSlot, spacing), _seatMinHalf, _capRectSize);
        if (_undo != null)
            _undo.transform.localPosition = undoPose;
        // THE THIRD MEMBER — the turn-flow SKIP (user, 2026-08-25: "Ich möchte daher, dass die
        // Button-Gruppe der 'Überspringen Buttons' komplett verschwindet … so dass all diese buttons
        // gleich aussehen und untereinander in den jeweiligen Slots sitzen"). It is written through
        // the identical expression as the other two — same offset, same stack term, same clamp — so
        // "sits in the seats like the others" is a property of the arithmetic rather than of three
        // numbers being kept in step.
        Vector3 skipPose = BoardAnchors.ClampSeatPose(
            offset, GenericSeatY(GenericSkipSlot, spacing), _seatMinHalf, _capRectSize);
        if (_skip != null)
            _skip.transform.localPosition = skipPose;
        LogPrimarySeat(primary, spacing);
        LogSeatClamp(offset, spacing, primary, undoPose);
    }

    /// <summary>
    /// The generic cluster's PRIMARY (commit) slot — the seat BOTH the board's CONFIRM keycap and the
    /// item "Benutzen" keycap are written to.
    ///
    /// <para>THE BUG THIS CLOSES, mechanically. Both caps are children of the very same anchor
    /// (<c>BuildButtons</c> parents <see cref="_itemUseConfirm"/> to <c>confirmParent</c>), so their
    /// local Y was the whole difference. The item cap used to be a THIRD stack member: while it was
    /// live the cluster's member count returned 3, which — under the CENTRED stack of the day — put Confirm at <c>+spacing</c>, Use at
    /// <c>0</c> and Undo at <c>−spacing</c>; while it was idle the count fell back to 2 and Confirm
    /// sat at <c>+spacing/2</c>. Since a placed item HIDES Confirm and Undo outright (the ModBuild
    /// 103 rule in <c>TickStatus</c>), the two caps were never on screen together — the player only
    /// ever saw the visible one of the pair, "Auswahl beenden" at <c>+spacing/2</c> and "Benutzen" at
    /// <c>0</c>. That difference — exactly HALF the tuned <c>[Cards] GenericButtonSpacing</c>, 5 mm
    /// board-local on the shipped Steel board — is the reported Y offset. It was never a tuning
    /// value: it fell out of a member count, so no number could be nudged to remove it.</para>
    ///
    /// <para>WHY THE CONFIRM SLOT IS THE SURVIVING SEAT rather than the item cap's old one. The two
    /// caps are mutually exclusive by construction, so the cluster only ever needs TWO seats and the
    /// third one was the drift. Of the two candidates only the Confirm slot is a TUNED quantity: the
    /// player's <c>[Cards] ConfirmUndoOffset_{board}</c> places the cluster and
    /// <c>GenericButtonSpacing_{board}</c> spreads Confirm against Undo, and that spread is defined
    /// as the gap BETWEEN the pair. Had the pair moved down onto the item cap's old seat instead, the
    /// Confirm↔Undo gap would have silently shrunk by spacing/2 on every board and the spacing dial
    /// would have become an asymmetric one that only moves Undo — a live retune of two shipped
    /// per-board values as a side effect of an alignment fix. Seating the transient cap on the tuned
    /// slot changes nothing the player dialled in, and it holds under board rescale, a board switch
    /// and any future relayout because the seat is one expression evaluated once above: whatever the
    /// anchor and spacing resolve to, both caps get that same Vector3.</para>
    /// </summary>
    private const int GenericPrimarySlot = 0;

    /// <summary>The generic cluster's UNDO seat. Named rather than derived from the member count:
    /// deriving it as <c>count - 1</c> is what let a third member push Undo down the board (see
    /// <see cref="GenericSeatY"/>), and Undo has never been "the last one", it has been "the second
    /// one".</summary>
    private const int GenericUndoSlot = 1;

    /// <summary>
    /// The cluster's THIRD seat — the recess the three-button boards add on the right-hand side
    /// (user, 2026-08: "3 statt 2 Slots für die buttons") — and its occupant since 2026-08-25: the
    /// turn-flow SKIP cap.
    ///
    /// <para>It used to be a resolved but EMPTY seat, because the Skip was drawn by
    /// <c>WorldUI.ButtonCluster</c> from a separate <c>[RoundButtons]</c> column at board-local
    /// (0.148, −0.124) and a peer's copy of it was solved term for term from record 28's ids
    /// 81..88. The user closed that: "Die Boards sind nun alle so umgebaut, dass sie jeweils drei
    /// Slots haben. Ich möchte daher, dass die Button-Gruppe der 'Überspringen Buttons' komplett
    /// verschwindet." The cluster, the column, its geometry family and its wire fields are gone; the
    /// cap is an ordinary generic keycap on this seat, built from the same <c>[BoardButtons]</c>
    /// size, depth and travel as Confirm and Undo, which is what "all diese buttons gleich aussehen"
    /// means once it is code.</para>
    ///
    /// <para>SEAT 2 AND NOT SEAT 0, and the order is the one the enumeration in the old cluster file
    /// asked for — "Confirm/Use · Undo · Skip so seat 0 never moves". Confirm and Undo keep the
    /// recesses they already sat in.</para>
    /// </summary>
    private const int GenericSkipSlot = 2;

    /// <summary>The cluster offset/spacing the live caps were last seated from — so
    /// <see cref="SeatLocalPose"/> can answer for a seat NO cap occupies without re-reading the
    /// config (and without disagreeing with where the occupied seats actually are). Seeded to the
    /// active board's dials on the first <see cref="SetConfirmUndoOffset"/>, which every build
    /// path ends with.</summary>
    private Vector3 _seatOffset;
    private float _seatSpacing = Defaults.ButtonStackSpacing;

    /// <summary>
    /// THIS BOARD'S OWN button-recess pitch, measured off its anchors at build time
    /// (<c>BoardAnchors.StackPitch</c>) — the per-board term that used to be three hand-dialled
    /// <c>GenericButtonSpacing_{board}</c> constants. <see cref="Defaults.StackPitchFallback"/>
    /// until a board has been measured, and on any board that supplies fewer than two seats.
    /// </summary>
    private float _seatPitch = Defaults.StackPitchFallback;

    /// <summary>The board's own button-recess pitch, for the peer mirror and the log. Board-local
    /// metres, one step down the stack.</summary>
    internal float SeatPitch => _seatPitch;

    /// <summary>
    /// The bundled anchor of generic-cluster seat <paramref name="seat"/>, or null when this board
    /// has no such recess (seat 2 on every board shipped so far). Seats 0/1 answer with the
    /// procedural fallback anchor on a bundle-less board, because that is genuinely where their caps
    /// hang.
    /// </summary>
    internal Transform? SeatAnchor(int seat) =>
        seat >= 0 && seat < _buttonSeats.Length ? _buttonSeats[seat] : null;

    /// <summary>
    /// The ANCHOR-LOCAL position a cap seated at <paramref name="seat"/> takes — the same expression
    /// <see cref="SetConfirmUndoOffset"/> writes to the live caps, evaluated for a seat that may have
    /// no cap in it. Meaningless without <see cref="SeatAnchor"/>: the pose is relative to THAT
    /// transform, and the three anchors are at three different places on the board.
    /// </summary>
    internal Vector3 SeatLocalPose(int seat) =>
        BoardAnchors.ClampSeatPose(_seatOffset, GenericSeatY(seat, _seatSpacing), _seatMinHalf, _capRectSize);

    /// <summary>
    /// The cap W×H the live generic keycaps were BUILT at (already seat-fitted — see
    /// <c>BuildButtons</c>). <see cref="SetConfirmUndoOffset"/> needs it because the clamp's bound is
    /// the gap between THIS cap and the recess wall: a smaller cap legitimately has more room to be
    /// nudged inside the same well. Set by every build before the first layout call; the tuned
    /// [BoardButtons] pair until then, which is what an unmeasured board would use anyway.
    /// </summary>
    private Vector2 _capRectSize = new(Defaults.BoardButtons_Width, Defaults.BoardButtons_Height);

    /// <summary>
    /// One line whenever the clamp actually BIT, naming the dial and both poses. Silent when the
    /// tuned offset already sits the cap inside its seat, which is every board that has not had its
    /// asset regenerated under it.
    /// </summary>
    private void LogSeatClamp(Vector3 offset, float spacing, Vector3 primary, Vector3 undoPose)
    {
        if (_seatMinHalf == null)
            return;
        Vector3 wantPrimary = offset + new Vector3(0f, GenericSeatY(GenericPrimarySlot, spacing), 0f);
        Vector3 wantUndo = offset + new Vector3(0f, GenericSeatY(GenericUndoSlot, spacing), 0f);
        if (!BoardAnchors.SeatPoseWasClamped(wantPrimary, primary)
            && !BoardAnchors.SeatPoseWasClamped(wantUndo, undoPose))
            return;
        Vector2 slack = BoardAnchors.SeatSlack(_seatMinHalf.Value, _capRectSize);
        VRLog.Info("Cards", "Board: SEAT CLAMP — the tuned in-plane offset would have put a generic cap " +
            $"outside its own recess, so it was bounded to the ±({slack.x * 1000f:F1}, {slack.y * 1000f:F1}) mm " +
            "of slack this cap has in the well. [Cards] ConfirmUndoOffset = " +
            $"({offset.x:F3}, {offset.y:F3}, {offset.z:F3}), ButtonStackSpacing = {spacing:F3}x this " +
            $"board's own {_seatPitch * 1000f:F1} mm recess pitch ⇒ " +
            $"seat 0 wanted ({wantPrimary.x:F4}, {wantPrimary.y:F4}) got ({primary.x:F4}, {primary.y:F4}), " +
            $"seat 1 wanted ({wantUndo.x:F4}, {wantUndo.y:F4}) got ({undoPose.x:F4}, {undoPose.y:F4}) m. " +
            "Z is untouched. Those dial values were measured against the board asset that was installed " +
            "when they were tuned; this board's recesses are authored and measured, so the recess is " +
            "where the cap goes.");
    }

    /// <summary>How many button seats the LIVE board supplies. 2 on every board shipped so far, 3 on
    /// a board the asset lane has regenerated with the third recess.</summary>
    internal int GenericSeatCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _buttonSeats.Length; i++)
                if (_buttonSeats[i] != null)
                    n++;
            return n;
        }
    }

    /// <summary>Last primary seat announced by <see cref="LogPrimarySeat"/> — NaN until the first
    /// application, so a fresh board always logs exactly once and a re-layout only speaks up when the
    /// shared seat actually moved.</summary>
    private Vector3 _alignLoggedSeat = new(float.NaN, float.NaN, float.NaN);

    /// <summary>
    /// One <c>BUTTON ALIGN</c> line per distinct shared seat: names the anchor the pose was derived
    /// from, states whether both caps really hang off it, and prints the local offset both of them
    /// were written to — so a hardware log proves "Auswahl beenden" and "Benutzen" agree instead of
    /// leaving it to a screenshot.
    /// </summary>
    private void LogPrimarySeat(Vector3 seat, float spacing)
    {
        if (_confirm == null || _itemUseConfirm == null)
            return;
        if ((seat - _alignLoggedSeat).sqrMagnitude < 1e-12f)
            return;
        _alignLoggedSeat = seat;
        Transform? anchor = _confirm.transform.parent;
        string anchorName = anchor != null ? anchor.name : "<none>";
        bool shared = anchor != null && ReferenceEquals(anchor, _itemUseConfirm.transform.parent);
        VRLog.Info("Cards", "BUTTON ALIGN: generic-cluster commit seat derived from anchor " +
                            $"'{anchorName}' — {(shared ? "shared by BOTH caps" : "MISMATCH: the item cap hangs off a different anchor")}; " +
                            $"local offset {seat.x:F4}, {seat.y:F4}, {seat.z:F4} m " +
                            $"[seat {GenericPrimarySlot} of {GenericButtonCount}; the board supplies " +
                            $"{GenericSeatCount} seat(s) at a measured {_seatPitch * 1000f:F1} mm pitch, " +
                            $"spacing x{spacing:F3}] — " +
                            "CONFIRM 'Auswahl beenden' and item-USE 'Benutzen' are written to this one pose.");
    }

    /// <summary>
    /// Item D: how many SEATS the mod-owned GENERIC cluster addresses. THREE since the boards gained
    /// a third button recess (user, 2026-08: "3 statt 2 Slots für die buttons"), and three is the
    /// ceiling rather than a round number: <c>WorldUI/ButtonCluster.cs</c> read every
    /// <c>readyButton</c>/<c>m_UndoButton</c>/<c>m_SkipButton</c> toggle site in the decompiled
    /// <c>Choreographer</c> and found six states where all three are live at once, and none that
    /// reaches four for the caps this board draws.
    ///
    /// <para>THIS NUMBER NO LONGER MOVES ANY CAP, which is the reason it could be raised at all,
    /// and the argument survived the 2026-08-25 restructure in a stronger form. When it was raised
    /// from 2 to 3 the stack term still carried a layout, so a top-anchored form was what kept seat
    /// 0 fixed against a count change; the seats now come from the board's own anchors and the term
    /// is a SPREAD that is zero at the shipped dial (<see cref="GenericSeatY"/>), so the count moves
    /// nothing at any spacing. It is a bound on the seat INDEX and a fact for the log.</para>
    /// </summary>
    private const int GenericButtonCount = BoardAnchors.ButtonSeatCount;

    /// <summary>
    /// Requirement 9a: true while the item "Use" confirm is a live member of the generic cluster (a held
    /// usable item card is clipped into the use slot).
    ///
    /// <para>It no longer changes the cluster's seat layout. It used to bump the member count to 3 so the item
    /// cap could claim a slot of its own between Confirm and Undo — the mechanism that produced the
    /// reported Y offset, since a placed item hides Confirm and Undo anyway and the extra slot only ever
    /// displaced the one cap that was actually on screen. The cap now shares the Confirm seat
    /// (<see cref="GenericPrimarySlot"/>); this flag is purely a VISIBILITY/label state.</para>
    /// </summary>
    private bool _itemUseActive;


    // CAP AUTO-SHRINK IS GONE, AND MUST NOT COME BACK. `GenericClusterButtonSize(baseSide, count)`
    // and its two constants (GenericColumnHeight 0.16, GenericButtonGap 0.010) lived here and
    // shrank every cap to fit a FIXED column once a cluster grew past two members. User ruling:
    // "der Use-Button soll genauso groß sein und sich nach den Werten richten, die die generischen
    // Buttons vorgegeben haben". The moment the item "Use" confirm appeared, all three caps snapped
    // to a smaller auto-fit square and none of them matched the size the player had dialled in — a
    // cluster that changes size depending on how many members happen to be live. Every member now
    // keeps the tuned [BoardButtons] cap size at ANY count and the stack makes room by SPACING
    // instead (GenericSeatY below). The function had already been left unwired and unreferenced;
    // it is deleted, this note is what remains, and re-wiring it re-opens the ruling.

    /// <summary>
    /// ANCHOR-LOCAL Y of generic-cluster seat <paramref name="index"/>: the SPREAD the user's
    /// dimensionless <paramref name="spacing"/> adds on top of the recess this board already cut,
    /// given that board's own measured pitch (<paramref name="pitch"/>). Each cap is parented to its
    /// own recess anchor, so this is a delta, never the seat's whole position.
    ///
    /// <para><b>THE THREE-SEAT FORM IS CENTRE-ANCHORED AND ZERO AT THE DEFAULT:</b>
    /// <c>pitch·(spacing−1)·(1 − index)</c> — seat 0 up by one scaled step, seat 1 fixed, seat 2 down
    /// by one. At <c>spacing = 1</c> every seat gets exactly 0 on every board, which is the whole
    /// point: the shipped picture is the board's own authored layout, not a number this file
    /// chose.</para>
    ///
    /// <para><b>WHY IT IS NO LONGER TOP-ANCHORED.</b> The predecessor was <c>(0.5 − index)·spacing</c>,
    /// a two-seat top-anchored form, and top-anchoring was the correct answer to the question it was
    /// asked. Every member then hung off ONE anchor, so the stack term carried the whole layout, and
    /// pinning seat 0 was what stopped a change in the member COUNT from moving caps the user had
    /// dialled in — the defect this cluster had already paid for once (the "Auswahl beenden" /
    /// "Benutzen" Y offset, which "was never a tuning value: it fell out of a member count",
    /// <see cref="GenericPrimarySlot"/>). Both halves of that question are gone: the count is fixed
    /// at <c>BoardAnchors.ButtonSeatCount</c>, and each cap has its own anchor, so the stack term no
    /// longer carries any layout at all. What top-anchoring would now do instead is a defect of its
    /// own — it TRANSLATES the whole column down the board as the dial grows, so a control the user
    /// asked for as "the Y distance between the stacked buttons" would also silently be a "move the
    /// group" control. Centre-anchoring keeps the middle cap still and makes the dial purely a
    /// spread, which is what he asked for.</para>
    ///
    /// <para>WHAT MOVES AT THE DEFAULT, stated rather than implied: the old form put seat 0 at
    /// <c>+spacing/2</c> and seat 1 at <c>−spacing/2</c> of the retired per-board
    /// <c>GenericButtonSpacing_{board}</c> — −8 mm on Oak, +10 mm on Steel, +60 mm on Bronze — i.e.
    /// the two caps were pulled off their own recess centres by ±4, ±5 and ±30 mm. They are on their
    /// centres now. (On Steel and Bronze most of that was already being confiscated by
    /// <c>BoardAnchors.ClampSeatPose</c>, whose slack is ±4.0 and ±4.4 mm.)</para>
    ///
    /// <para>The STEP is the board's own pitch and still never the caps' size — auto-shrink is gone
    /// and must not come back (the note above).</para>
    /// </summary>
    internal static float GenericSeatY(int index, float pitch, float spacing) =>
        BoardAnchors.StackDelta(index, BoardAnchors.ButtonSeatCount, pitch, spacing);

    /// <summary>The stack term for a seat on THIS board, at THIS board's measured pitch.</summary>
    private float GenericSeatY(int index, float spacing) =>
        GenericSeatY(index, _seatPitch, spacing);

    /// <summary>PART F live-apply: move the discard/burn pile mount to a new per-board offset (instant).</summary>
    internal void SetPileOffset(Vector3 offset)
    {
        if (_pileMount != null)
            _pileMount.localPosition = PileMountBase + offset;
    }

    /// <summary>PART F live-apply: move the ACTIVE-cards mount to a new per-board offset (instant).</summary>
    internal void SetActiveOffset(Vector3 offset)
    {
        if (_activeMount != null)
            _activeMount.localPosition = ActiveMountBase + offset;
    }

    /// <summary>Items 4/6 live-apply: move + resize the OBJECTIVES ('Aufgaben') dock mount (instant).</summary>
    internal void SetObjectivesLayout(Vector3 offset, float scale)
    {
        if (_objectivesMount != null)
        {
            _objectivesMount.localPosition = ObjectivesMountBase + offset;
            _objectivesMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Items 4/6 live-apply: move + resize the ELEMENT infusion ('Elemente') dock mount (instant).</summary>
    internal void SetElementsLayout(Vector3 offset, float scale)
    {
        if (_elementMount != null)
        {
            _elementMount.localPosition = ElementMountBase + offset;
            _elementMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Item C live-apply: move + resize the shared DECISION DOCK mount (instant; the surface pose-follows it).</summary>
    internal void SetDecisionLayout(Vector3 offset, float scale)
    {
        if (_decisionMount != null)
        {
            _decisionMount.localPosition = DecisionMountBase + offset;
            _decisionMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>
    /// Items 4/6 live-apply: move the round readout ('Runde N') to a new per-board offset (instant).
    ///
    /// <para>IT MOVES THE READOUT ROOT, NOT THE LABEL, and the distinction became load-bearing when
    /// the backing plate was deleted. The label used to BE the readout root — <c>BuildRoundReadout</c>
    /// added the <c>TextMeshPro</c> to the same GameObject the plate hung off — so writing
    /// <c>ReadoutBase + offset</c> onto its transform was correct. The engraved number is a CHILD
    /// of that root instead (<c>BoardEngraving.Create</c> owns its own flush depth, which no caller
    /// may overwrite), so the same write would have added the base a second time and put "Runde N"
    /// a quarter of a board away the first time anyone touched the dial.</para>
    /// </summary>
    internal void SetReadoutOffset(Vector3 offset)
    {
        Transform? root = _roundLabel != null ? _roundLabel.transform.parent : null;
        if (root != null)
            root.localPosition = ReadoutBase + offset;
    }

    /// <summary>Items 4/6 live-apply: move the FOLLOW/PIN toggle button to a new per-board offset (instant).</summary>
    internal void SetPinOffset(Vector3 offset)
    {
        if (_followAnchor != null)
            _followAnchor.localPosition = PinBase + offset;
    }

    // WHY SOME OF THE MOUNT BASES BELOW ARE `internal` AND NOT `private`
    // ---------------------------------------------------------------
    // A remote player's control board (Net/RemoteBoardLayout) must dock ITS copies of these panels
    // where the OWNER's board docks them, and the only way to guarantee that without hand-tuning is
    // to read the SAME expression the local board reads. The alternative — a second copy of each
    // number in Net/ — is precisely the drift scripts/check-mirrors.sh exists to police, and here it
    // is avoidable at zero cost: these are authored layout constants with no behaviour attached, so
    // widening them leaks nothing and gives the remote board one source of truth instead of a
    // mirror. (The per-board OFFSET that adds on top is read from `Defaults`, which both sides
    // already share; only the local player's own debug-menu RE-tuning stays private, as ever.)

    /// <summary>Fixed base local position of the discard/burn pile mount (per-board PileOffset adds on top).</summary>
    internal static Vector3 PileMountBase => new(BoardW * 0.5f + 0.012f, 0f, -0.004f);

    /// <summary>Fixed base local position of the ACTIVE-cards mount (per-board ActiveOffset adds on top).</summary>
    internal static Vector3 ActiveMountBase => new(BoardW * 0.5f + 0.012f + ActiveMountOffsetX, 0f, -0.004f);

    // ---- Fixed base positions for the remaining board-attached elements (items 4/6). Each
    // per-board offset from the debug menu ADDS on top of these. ----

    /// <summary>Fixed base local position of the OBJECTIVES ('Aufgaben') dock mount.</summary>
    internal static Vector3 ObjectivesMountBase => new(-BoardW * 0.5f - 0.012f, 0f, -0.004f);

    /// <summary>Fixed base local position of the ELEMENT infusion ('Elemente') dock mount (left column below objectives).</summary>
    internal static Vector3 ElementMountBase =>
        new(-BoardW * 0.5f - 0.012f,
            -(ObjectivesMountMaxHeight * 0.5f + 0.012f + ElementMountMaxHeight * 0.5f),
            -0.004f);

    /// <summary>
    /// Item C: fixed base local position of the shared DECISION DOCK mount (hangs below the board).
    ///
    /// <para>THE Y IS −0.447, NOT THE AUTHORED −0.290, AND THAT IS DELIBERATE (ModBuild 90). Every
    /// shipped board carried <c>[Cards] DecisionOffset_&lt;board&gt;</c> = (0, −0.157, 0), so the
    /// mount has always SAT at −0.447 — but the Y of that offset could not move the decision area
    /// itself (the dock's up-axis solve cancelled the mount's Y out; see
    /// <c>WorldUI.Surfaces.DecisionDockSurface.MountOffsetUp</c>). The moment that dial went live,
    /// leaving the 157 mm inside it would have dropped the area 157 mm below the shipped picture on
    /// first launch. So the shipped displacement moved DOWN here, into the base where it always
    /// belonged, and the offset defaults to 0 on all three boards: same mount seat to the
    /// micrometre, same area, and the dial now starts from "no displacement" and means it.</para>
    ///
    /// <para>INTERNAL for the same reason as the other bases (see the note above): a remote player's
    /// board reads THIS expression — <c>Net.RemoteBoardFurniture.DecisionMount</c> aliases it — so
    /// the mirrored drawer cannot drift from the local one through a hand-copied number.</para>
    /// </summary>
    internal static Vector3 DecisionMountBase => new(0f, -0.447f, -0.020f);

    /// <summary>
    /// Fixed base local position of the ITEM-USE clip-in slot (items rework, requirement 3):
    /// UNDER the board's bottom edge in the right (Confirm/Undo) column, proud toward the player.
    /// The per-board <see cref="CardsConfig.ItemUseSlotOffset"/> adds on top (debug-menu tunable).
    /// </summary>
    private static Vector3 ItemUseSlotBase => new(ButtonZoneX, -BoardH * 0.5f - 0.095f, -0.020f);

    /// <summary>Gap between the item-use recess's bottom edge and the "USE" caption's centre line
    /// (tray-local metres). Big enough that the caption clears the glow rim (1.28× the card) as well
    /// as the card itself, so nothing ever overlaps the clipped-in card — see BuildItemUseSlot.</summary>
    private const float ItemUseLabelDrop = 0.026f;

    /// <summary>Height budget the item-use caption is fitted into — the strip BELOW the recess, not
    /// the card's own height (see BuildItemUseSlot for why fitting it to the card height re-created
    /// the overlap the drop is there to prevent).
    ///
    /// <para>The value is the PILE CAPTIONS' own (PileViewer.PileStack.Create fits "ABGEWORFEN" /
    /// "VERBRANNT" / "GEGENSTÄNDE" into 0.095 × 0.024 at a 0.22 ceiling, on stacks whose shipped
    /// scale is 1). Sharing the box is what makes the recess caption the same physical size as the
    /// pile captions instead of the keycap-sized text the user read as a button — see the styling
    /// note in BuildItemUseSlot.</para></summary>
    private const float ItemUseLabelHeight = 0.024f;

    /// <inheritdoc cref="ItemUseLabelHeight"/>
    private const float ItemUseLabelWidth = 0.095f;

    /// <summary>The board's CAPTION colour — muted parchment, the pile captions' own tone. The
    /// bright keycap cream (1, 0.92, 0.72) it replaced is reserved for things that are actually
    /// pressable (see BuildItemUseSlot).</summary>
    private static readonly Color ItemUseLabelColor = new(0.85f, 0.8f, 0.7f);

    /// <summary>Fixed base local position of the round readout ('Runde N', top-right).</summary>
    internal static Vector3 ReadoutBase => new(ButtonZoneX, 0.125f, -FixedProudZ);

    /// <summary>Fixed base local position of the FOLLOW/PIN toggle button (bottom-right corner).</summary>
    private static Vector3 PinBase => new(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -FixedProudZ);

    /// <summary>
    /// PART F live-apply: recompute the board rotation + scale from the ACTIVE board's config
    /// (BoardTilt/Yaw/Scale/PosOffset). FOLLOW mode re-derives the full pose (incl. posOffset)
    /// from the head; PINNED KEEPS the world position and only re-orients + rescales in place.
    /// </summary>
    internal void ReapplyOrientation()
    {
        if (_root == null)
            return;
        if (CardsConfig.TrayFollow.Value)
        {
            PlaceAtHead(); // re-derives position (incl. BoardPosOffset), rotation and scale
            return;
        }
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        // Plain world frame, like PlaceAtHead (user decision 2026-08 — the board is deliberately
        // decoupled from the world tilt; identical to the old level-frame math at tilt 0).
        Vector3 flatForward = head.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        ControlBoard board = CardsConfig.CurrentBoard;
        _root.rotation = ComputeBoardRotation(flatForward, board); // KEEP position (pinned)
        _root.localScale = Vector3.one * ComputeBoardScale(board);
        // Freeze-sentinel announcement: this path only runs from the settings/debug-menu
        // orientation tuning (CardsDriver._applyOrientation) — an EXPLICIT user action, which
        // the FIXIERT ruling allows.
        NotePinnedWrite("user settings live-apply (ReapplyOrientation — tilt/yaw/scale tuning)");
    }

    /// <summary>
    /// PART F live-apply: bring the Confirm / Undo / item-use keycaps back in line with the current
    /// config — REBUILDING them only when their geometry really changed (a cap's size, depth, travel
    /// and shape are baked into its mesh, so those need a fresh cap), and otherwise UPDATING THE
    /// LIVE CAPS IN PLACE. Re-parents onto the SAME anchors and purges dead laser targets either
    /// way. The rest discs are rebuilt separately by CardsDriver
    /// (<c>RestControls.Destroy(); EnsureBuilt(...)</c>).
    ///
    /// <para>WHY THE IN-PLACE PATH EXISTS (user report 2026-08-09, "die verschwinden und tauchen
    /// ohne die Animation die sonst kommt auf" — the item-flow caps skipping their appear/disappear).
    /// This method was an unconditional teardown, and <c>SetItemUseConfirmVisible</c> calls it every
    /// time a card enters or leaves the use recess, because adding/removing the "Use" member changes
    /// the cluster COUNT. The fresh hardware log shows what that costs: <b>33</b> "Confirm/Undo built
    /// as 3D square keycaps" lines in one session, every single one of them immediately followed by
    /// an "ITEM clip-in" / "ITEM clip-in CANCEL" line, and ZERO from a tuning change. So every clip-in
    /// and every take-back DESTROYED and RECREATED all three caps — and destruction/creation is the
    /// one transition the mod's authored crumble/assemble can never cover: a destroyed cap cannot
    /// dissolve and a newborn cap cannot materialize. That is the reported defect, exactly.</para>
    ///
    /// <para>NOTHING ABOUT THE PAIR ACTUALLY DEPENDED ON THE COUNT. The caps stopped being auto-sized
    /// from the member count when the user ruled that the Use button must keep the tuned
    /// <c>[BoardButtons]</c> size ("der Use-Button soll genauso groß sein …"); all the count still
    /// drove was each cap's local Y, which <see cref="SetConfirmUndoOffset"/> writes on the live
    /// transforms. With the item-use cap now BUILT ONCE AND HIDDEN (see <c>BuildButtons</c>), a
    /// clip-in is a re-layout plus three <c>SetVisible</c> calls — i.e. the ordinary animated path
    /// every other cap on the board already uses — and the rebuild storm is gone with it. Since the
    /// item cap took over the Confirm SEAT (<see cref="GenericPrimarySlot"/>) the count does not move
    /// at all any more, so the clip-in re-layout is now a no-op that only re-reads the tuning.</para>
    /// </summary>
    internal void RebuildAttachedControls()
    {
        if (_root == null)
            return;
        // Did anything a cap's GEOMETRY is baked from actually change? (Board, shape, and the
        // [BoardButtons]/[ButtonColors] entries ButtonTuning.Version covers — see CapGeometryKey.)
        // If not, and the caps are alive, update them where they stand.
        if (_confirm != null && _undo != null && _skip != null && _itemUseConfirm != null
            && CapGeometryKey() == _capGeometryKey)
        {
            // The item-use cap's wording is a live property, not a build input: the surrender picks
            // relabel it ("ITEM ABGEBEN") without changing a single dimension. TMP auto-sizing
            // (Core.TmpFit.Fit leaves enableAutoSizing on) re-fits the new string inside the same
            // cap, which is what the rebuild used to be doing the long way round.
            _itemUseConfirm.SetLabel(_itemUseConfirmLabel ?? Core.Loc.Mod("item_use_area"));
            // Re-seat every cap from the live per-board offsets. The member count no longer moves
            // (the item cap shares Confirm's seat), so this is a re-read of the tuning, not a reflow.
            SetConfirmUndoOffset(CardsConfig.ConfirmUndoOffset.Value,
                CardsConfig.ButtonStackSpacing.Value);
            // CardsDriver's control-rebuild path relies on this call to drop the laser targets of
            // the REST caps it destroyed just before calling us (see CardsDriver.ApplyBoardTuning),
            // so the purge happens on both paths, not only on the teardown one.
            LaserTargets.RemoveAll(static t => t.Collider == null);
            // Debug channel, not Info: this is the path the item flow takes on EVERY clip-in, and the
            // whole point of the change is that it stops being an event worth a line in the log. The
            // Info line that IS worth reading ("Confirm/Undo built as 3D … keycaps") now marks a real
            // rebuild — if the next hardware log shows it once per board instead of 33 times per
            // session, this is why.
            VRLog.Debug("Cards", "Board: control caps updated IN PLACE (label + layout only) — no " +
                                 "geometry input changed, so nothing was destroyed and every cap keeps " +
                                 "its authored appear/disappear.");
            return;
        }
        // Remember what the live caps were SHOWING so the replacements can be seated in the same
        // state before their first render — the anti-flash ordering (see the seat block in
        // BuildButtons). Captured here, where the old caps still exist.
        _confirmShownBeforeRebuild = _confirm != null ? _confirm.LogicalVisible : (bool?)null;
        _undoShownBeforeRebuild = _undo != null ? _undo.LogicalVisible : (bool?)null;
        _skipShownBeforeRebuild = _skip != null ? _skip.LogicalVisible : (bool?)null;
        // Re-seat on the SAME seats. `_buttonSeats` already holds them — BuildButtons writes back the
        // anchor each cap was actually parented to, including a synthesised procedural one — so the
        // live cap's parent is only a cross-check, not the source of truth it used to be. (The old
        // fallback here was `_confirmAnchor`/`_undoAnchor`, which are the ContinueMount/UndoDockMount
        // transforms for the game's NATIVE docked widgets at the hardcoded ButtonZoneX, NOT the
        // board's button recesses: with both caps somehow null it would have rebuilt them on the
        // wrong anchors. Nothing observed it, but it was wrong, and the seat array makes it right.)
        if (_confirm != null && _buttonSeats.Length > 0)
            _buttonSeats[0] = _confirm.transform.parent;
        if (_undo != null && _buttonSeats.Length > 1)
            _buttonSeats[1] = _undo.transform.parent;
        if (_skip != null && _buttonSeats.Length > 2)
            _buttonSeats[2] = _skip.transform.parent;
        if (_confirm != null)
        {
            Object.DestroyImmediate(_confirm.gameObject);
            _confirm = null;
        }
        if (_undo != null)
        {
            Object.DestroyImmediate(_undo.gameObject);
            _undo = null;
        }
        if (_skip != null)
        {
            Object.DestroyImmediate(_skip.gameObject);
            _skip = null;
        }
        // Requirement 9a: the item "Use" cluster button is (re)built by BuildButtons — tear the old one
        // down first so a tuning rebuild never leaks/doubles it.
        if (_itemUseConfirm != null)
        {
            Object.DestroyImmediate(_itemUseConfirm.gameObject);
            _itemUseConfirm = null;
        }
        LaserTargets.RemoveAll(static t => t.Collider == null); // drop the just-destroyed (and any other dead) targets
        BuildButtons(_buttonSeats);
    }

    /// <summary>
    /// ButtonTuning live-apply, checked once per <see cref="TickStatus"/> tick: a geometry
    /// entry changed (width/height/depth/travel — anything the pull-based
    /// <see cref="WorldUI.ButtonTuning.Version"/> counter covers) → rebuild the affected
    /// keycaps in place on their existing anchors; the rebuild bakes each category's own
    /// travel per instance ([BoardButtons] Confirm/Undo, [BoardDashboard] gear/pin).
    /// </summary>
    private int _tuningVersion;

    private void ApplyButtonTuningIfChanged()
    {
        if (_root == null || _tuningVersion == WorldUI.ButtonTuning.Version)
            return;
        _tuningVersion = WorldUI.ButtonTuning.Version;
        RebuildAttachedControls();
        RebuildDashboardButtons();
        VRLog.Info("Cards", "Board: ButtonTuning changed → Confirm/Undo/gear/follow keycaps " +
                            $"rebuilt live — {WorldUI.ButtonTuning.Describe()}.");
    }

    /// <summary>Rebuild the gear + follow-toggle keycaps on their existing anchors (ButtonTuning live-apply).</summary>
    private void RebuildDashboardButtons()
    {
        if (_root == null)
            return;
        if (_followToggle != null)
        {
            Object.DestroyImmediate(_followToggle.gameObject);
            _followToggle = null;
        }
        // The toggle's ENGRAVED caption is a sibling under the same anchor, not a child of the cap
        // — it is cut into the board, not standing on the key — so tearing the cap down does not
        // take it with it. Without this it would be re-created on every [BoardButtons] edit and the
        // board would slowly acquire a stack of identical carvings, each a fraction of a millimetre
        // in front of the last.
        if (_followEngraving != null)
        {
            Object.DestroyImmediate(_followEngraving.gameObject);
            _followEngraving = null;
        }
        LaserTargets.RemoveAll(static t => t.Collider == null);
        CreateDashboardButtons();
    }

    /// <summary>Remove laser targets whose collider was destroyed (e.g. a rest-button rebuild).</summary>
    internal void PurgeDeadLaserTargets() => LaserTargets.RemoveAll(static t => t.Collider == null);

    // ------------------------------------------------------------------ board-switch pose --

    /// <summary>
    /// PART D: capture the live board's world pose so a board SWITCH can re-apply it to the
    /// new board (instead of re-anchoring to the head). Returns false when no board exists.
    /// </summary>
    internal bool TryCapturePose(out Vector3 position, out Quaternion rotation, out Vector3 localScale)
    {
        if (_root == null)
        {
            position = default;
            rotation = Quaternion.identity;
            localScale = Vector3.one;
            return false;
        }
        position = _root.position;
        rotation = _root.rotation;
        localScale = _root.localScale;
        return true;
    }

    /// <summary>
    /// PART D: re-apply a captured world pose to a freshly built board on a SWITCH — the new
    /// board spawns in the EXACT same place instead of re-placing at the head. Marks the tray
    /// placed and re-pins it (PINNED) at the preserved pose.
    /// </summary>
    internal void RestorePose(Vector3 position, Quaternion rotation, Vector3 localScale)
    {
        if (_root == null)
            return;
        _root.position = position;
        _root.rotation = rotation;
        _root.localScale = localScale;
        NotePinnedWrite("board-switch/rebuild pose restore (RestorePose — verbatim world pose)");
        _placed = true;
        _placementDeferLogged = false;
        if (!CardsConfig.TrayFollow.Value && _root.parent != _pinRoot)
            ApplyFollowMode(); // re-pin at the preserved world pose (worldPositionStays)
        VRLog.Info("Cards", "Control board switch: preserved the previous board's world pose (no re-place at head).");
        LogBoardFaceDiagnostics();
    }

    /// <summary>
    /// Persist the CURRENT root pose back into the config (called by
    /// the shared <see cref="WorldUI.PanelGrabHandle"/> when the last gripping hand lets go): the inverse
    /// of <see cref="PlaceAtHead"/> — head-relative offsets in real meters, yaw
    /// relative to the head's flat forward, and the size multiplier. BepInEx writes
    /// the ConfigFile on set, so the layout survives sessions.
    /// </summary>
    internal void PersistPoseToConfig()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;

        Transform headT = head.transform;
        // Exact inverse of PlaceAtHead, so like it, everything runs in the PLAIN WORLD frame
        // (user decision 2026-08, supersedes item 11: the board is deliberately decoupled from
        // the world tilt, so persisting world axes is now the CORRECT round-trip — what the
        // grab left in world space is what the next placement re-creates).
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        if (scale < 1e-5f)
            return;
        Vector3 delta = _root.position - headT.position; // world-frame offset
        CardsConfig.TrayForward.Value = Vector3.Dot(delta, flatForward) / scale;
        CardsConfig.TrayRight.Value = Vector3.Dot(delta, right) / scale;
        CardsConfig.TrayDown.Value = -delta.y / scale;

        // Yaw + pitch: decompose the tray's WORLD rotation into heading and x-pitch
        // (WorldUI.LevelPose — the same decomposition the grab carry rebuilds from, so what the
        // grab wrote is exactly what is read back; any roll a Free-mode carry left is dropped
        // here, which for the Limited modes is the sanitizer, and for Free is the documented
        // best-effort persistence). PART B: BoardYaw rides on top of TrayYaw, so subtract it to
        // recover the grab-written raw yaw (seeded so Oak is unchanged).
        ControlBoard board = CardsConfig.CurrentBoard;
        WorldUI.LevelPose.Decompose(_root.rotation, out float trayHeading, out float trayPitchX);
        float headHeading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        CardsConfig.TrayYaw.Value = Mathf.DeltaAngle(headHeading, trayHeading) - CardsConfig.BoardYaw(board).Value;

        // Item 12: persist the grab-authored pitch as TrayPitch (degrees added to BoardTilt;
        // x-pitch = 90 − tilt − TrayPitch inverted). Only the modes that APPLY a pitch write it —
        // Begrenzt keeps the stored value untouched so toggling modes round-trips losslessly.
        BoardMoveMode mode = CardsConfig.BoardMoveMode.Value;
        if (mode != BoardMoveMode.Limited)
        {
            float pitchOffset = Mathf.DeltaAngle(0f, 90f - CardsConfig.BoardTilt(board).Value - trayPitchX);
            if (mode == BoardMoveMode.LimitedPitch)
            {
                (float min, float max) = CardsConfig.BoardPitchWindow;
                pitchOffset = Mathf.Clamp(pitchOffset, min, max);
            }
            CardsConfig.TrayPitch.Value = pitchOffset;
        }

        // SIZE MUST ROUND-TRIP EXACTLY, or the board silently resizes on the next re-place.
        // The live size is ComputeBoardScale = ClampedTrayScale × BoardScale, and the two-handed
        // gesture writes localScale directly over PanelGrabHandle's much wider [0.15, 2] range —
        // so with BoardScale 0.4 the config could only ever express [0.2, 0.8]. A board the player
        // had pinched to 1.94 persisted as TrayScale 2 (clamped) and snapped to 0.80 the next time
        // anything re-derived the pose. That was the "es hat seine Größe geändert" half of the
        // 2026-08-03 report; the recall was only what triggered the re-derivation.
        // So: keep TrayScale's documented 0.5–2 grab semantics, and absorb whatever does not fit
        // into the per-board multiplier (a free float, hand-edit/debug-menu territory) so the
        // PRODUCT is bit-exact what the player is looking at.
        //
        // A CARRY IS NOT A RESIZE, and since 2026-08-25 that distinction is load-bearing rather
        // than merely tidy. The apparent-size PUSH (PlayTray.ClampApparentSize) can legitimately
        // leave the live localScale different from the configured one — that is the whole feature —
        // and this method runs on EVERY grab release, including a one-hand carry that touched no
        // size at all. Persisting the pushed size from a carry would write a value the player never
        // authored into his config and, being outside TrayScale's band, would ratchet it straight
        // into the hand-tuned BoardScale_{board}. So the size is written only when the grab actually
        // changed it. _scaleAtGrabStart is captured on the rising edge of the grip in
        // TickLostWatchdog and consumed here; when it is unset (a release whose grab began while the
        // watchdog was standing down) the old unconditional behaviour is the fallback, because
        // dropping a real resize is the worse of the two failures.
        float boardScale = Mathf.Max(0.01f, CardsConfig.BoardScale(board).Value);
        float live = Mathf.Max(1e-4f, _root.localScale.x);
        float grabStart = _scaleAtGrabStart;
        _scaleAtGrabStart = -1f;
        bool resized = !(grabStart > 0f) || Mathf.Abs(live - grabStart) > grabStart * ScaleNoiseEpsilon;
        if (!resized)
        {
            VRLog.Info("Cards", $"Tray layout persisted: fwd {CardsConfig.TrayForward.Value:F2} m, " +
                                $"right {CardsConfig.TrayRight.Value:F2} m, down {CardsConfig.TrayDown.Value:F2} m, " +
                                $"yaw {CardsConfig.TrayYaw.Value:F0}°, pitch {CardsConfig.TrayPitch.Value:F0}° " +
                                $"({CardsConfig.BoardMoveMode.Value}); SIZE NOT WRITTEN — the grab was a " +
                                $"carry, localScale {grabStart:F3} → {live:F3} is within float noise, so " +
                                $"TrayScale stays {CardsConfig.TrayScale.Value:F2}× and " +
                                $"BoardScale_{board} stays {boardScale:F5}.");
            return;
        }

        float trayScale = Mathf.Clamp(live / boardScale, CardsConfig.TrayScaleMin, CardsConfig.TrayScaleMax);
        CardsConfig.TrayScale.Value = trayScale;
        float reproduced = trayScale * boardScale;
        if (Mathf.Abs(reproduced - live) > 1e-4f * Mathf.Max(1f, live))
        {
            float adjusted = live / trayScale;
            CardsConfig.BoardScale(board).Value = adjusted;
            // WARN, not Info: this re-seat is a RATCHET and it is the second half of the user's
            // 2026-08-15 size report ("weiterhin hat sich damit auch das maximum und minimum wieder
            // verschoben"). BoardScale_{board} is what the settings window measures its OWN range
            // against (0.5–2 × BoardScale), so every absorption MOVES the range the player can dial
            // — his ModBuild 158 log fired it twice in one session, Steel 0.54 → 1.00 → 1.13.
            //
            // ModBuild 268 makes this line REACHABLE ONLY BY A BUG. The two-hand gesture window is
            // now intersected with exactly the band this code can express
            // (IPanelGrabOwner.GrabScaleLimits), so a released size outside it means the gesture and
            // the persistence disagree about the same arithmetic — which is the one thing this
            // absorption cannot silently fix. If it appears in a 268+ log, do NOT widen the band:
            // compare the window GrabScaleLimits returned against TrayScaleMin/Max × BoardScale at
            // that timestamp and fix whichever of the two is wrong.
            VRLog.Warn("Cards", $"Board size {live:F2}× is outside what TrayScale alone can express " +
                                $"({CardsConfig.TrayScaleMin}–{CardsConfig.TrayScaleMax} × BoardScale " +
                                $"{boardScale:F2} = {CardsConfig.TrayScaleMin * boardScale:F2}–" +
                                $"{CardsConfig.TrayScaleMax * boardScale:F2}): BoardScale_{board} " +
                                $"re-seated to {adjusted:F2} so the size the player set survives every " +
                                "future re-place. THIS MOVES THE SETTINGS WINDOW'S OWN MIN/MAX and " +
                                "since ModBuild 268 the gesture window is supposed to make it " +
                                "unreachable — report this line.");
        }
        VRLog.Info("Cards", $"Tray layout persisted: fwd {CardsConfig.TrayForward.Value:F2} m, " +
                            $"right {CardsConfig.TrayRight.Value:F2} m, down {CardsConfig.TrayDown.Value:F2} m, " +
                            $"yaw {CardsConfig.TrayYaw.Value:F0}°, pitch {CardsConfig.TrayPitch.Value:F0}° " +
                            $"({CardsConfig.BoardMoveMode.Value}), scale {CardsConfig.TrayScale.Value:F2}× " +
                            $"(RESIZED: localScale {grabStart:F3} → {live:F3}).");
    }
}
