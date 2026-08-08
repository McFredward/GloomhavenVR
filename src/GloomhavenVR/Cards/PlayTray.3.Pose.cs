using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 3 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Regions: debug-menu live apply, the fixed base positions for board-attached elements,
// ReapplyOrientation, rebuild helpers, board-switch pose, PersistPoseToConfig.

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ debug-menu live apply --

    /// <summary>
    /// PART F live-apply: move the slot snap-glow / wanted-glow overlays to a new per-board
    /// offset in place (no rebuild). Base local-Z is preserved; the offset adds on top.
    /// Item 1: the two overlays are a PAIR, so a per-board SPACING spreads them apart along
    /// the slot-local X (the board's long/inter-slot axis) — slot 0 (left) −½, slot 1 (right) +½.
    /// </summary>
    internal void SetOverlayOffset(Vector3 offset, float spacing)
    {
        for (int i = 0; i < 2; i++)
        {
            float xSpread = (i == 0 ? -0.5f : 0.5f) * spacing; // spread the pair apart along the slot axis
            if (_slotHighlights[i] != null)
                _slotHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, SlotGlowBaseZ + offset.z);
            if (_wantedHighlights[i] != null)
                _wantedHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, WantedGlowBaseZ + offset.z);
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
    /// control the user asked for. Mechanism: the six bundle anchors are descendants of the
    /// visual, so after posing the mesh their captured root-local poses are written back,
    /// pinning slots, rest tokens and Confirm/Undo (and every element attached to them) exactly
    /// where they were. The mesh collider rides the mesh, so the laser lands on what you see.
    /// </summary>
    internal void SetAssetPose(Vector3 offset, Vector3 euler)
    {
        if (_visual == null || _root == null)
            return;
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
    /// PART F live-apply: move the generic Confirm/Undo buttons to a new per-board offset +
    /// inter-button spacing (instant). Confirm (upper) takes +spacing/2 along the board's short
    /// axis, Undo (lower) −spacing/2.
    /// </summary>
    internal void SetConfirmUndoOffset(Vector3 offset, float spacing)
    {
        // Item D / requirement 9a: auto-fit the generic cluster's buttons in a vertical stack. The
        // member COUNT is dynamic — normally Confirm(0)/Undo(1); while a held usable item card is
        // clipped into the use slot the item "Use" confirm joins as member 1 (the slot directly ABOVE
        // Undo), pushing the pair to a 3-member stack. GenericClusterY packs any count into the column
        // (at 2 it reproduces the tuned ±spacing/2 pair; at 3 it spreads within GenericColumnHeight so
        // Use lands squarely above Undo). Undo is ALWAYS the bottom member (index count-1).
        int count = GenericCount;
        if (_confirm != null)
            _confirm.transform.localPosition = offset + new Vector3(0f, GenericClusterY(0, count, spacing), 0f);
        if (_itemUseConfirm != null && _itemUseActive)
            _itemUseConfirm.transform.localPosition = offset + new Vector3(0f, GenericClusterY(1, count, spacing), 0f);
        if (_undo != null)
            _undo.transform.localPosition = offset + new Vector3(0f, GenericClusterY(count - 1, count, spacing), 0f);
    }

    /// <summary>
    /// Item D: how many buttons the mod-owned GENERIC cluster currently lays out (Confirm + Undo).
    /// The layout/size math below is parametric so the cluster can hold &gt;2 — the game can activate
    /// up to four turn-flow buttons at once (readyButton/skip/undo/select, decompiled-confirmed).
    /// </summary>
    private const int GenericButtonCount = 2;

    /// <summary>
    /// Requirement 9a: true while the item "Use" confirm is a live member of the generic cluster (a held
    /// usable item card is clipped into the use slot). It bumps <see cref="GenericCount"/> to 3 so Confirm
    /// (top) / Use (middle, above Undo) / Undo (bottom) auto-fit the column; false restores the tuned pair.
    /// </summary>
    private bool _itemUseActive;

    /// <summary>Live generic-cluster member count: the tuned pair, plus the item "Use" confirm while pending.</summary>
    private int GenericCount => GenericButtonCount + (_itemUseActive ? 1 : 0);

    /// <summary>Vertical room (tray-local meters) the generic cluster packs its buttons into. Kept clear of
    /// the round readout (top edge) and the settings gear (bottom edge) so a 3-member stack (with the item
    /// "Use" confirm) fits between them without colliding.</summary>
    private const float GenericColumnHeight = 0.16f;

    /// <summary>Minimum inter-button gap (tray-local meters) when the generic cluster auto-fits &gt;2 buttons.</summary>
    private const float GenericButtonGap = 0.010f;

    /// <summary>
    /// SUPERSEDED (user: "der Use-Button soll genauso groß sein und sich nach den Werten richten,
    /// die die generischen Buttons vorgegeben haben"). Every generic-cluster member — Confirm, Undo
    /// and the item "Use" confirm alike — now keeps the tuned [BoardButtons] cap size at ANY count,
    /// and the stack makes room by SPACING instead (see <see cref="GenericClusterY"/>). The old
    /// auto-shrink meant a cluster changed size depending on how many members happened to be live,
    /// so the moment Use appeared all three caps snapped to a smaller auto-fit square and none of
    /// them matched the size the player had dialled in. Kept only for callers that still want the
    /// old fit answer; nothing in the cluster path uses it.
    ///
    /// Item D (original): per-button side length for a generic cluster of <paramref name="count"/>
    /// buttons. A pair (or single) keeps the full authored size; from 3 up each cap shrinks so the whole stack
    /// fits <see cref="GenericColumnHeight"/> (auto-scale from the count), floored so it stays pokeable.
    /// </summary>
    internal static float GenericClusterButtonSize(float baseSide, int count)
    {
        if (count <= 2)
            return baseSide;
        float avail = (GenericColumnHeight - (count - 1) * GenericButtonGap) / count;
        return Mathf.Clamp(Mathf.Min(baseSide, avail), 0.02f, baseSide);
    }

    /// <summary>
    /// Local-Y of button <paramref name="index"/> in a top-to-bottom generic stack of
    /// <paramref name="count"/>, centred on the cluster anchor with the TUNED
    /// <paramref name="spacing"/> as the step at every count. At count 2 this is exactly the old
    /// ±spacing/2 pair, so nothing about the tuned Confirm/Undo layout changes.
    ///
    /// WHY the step is the tuned spacing and no longer <see cref="GenericColumnHeight"/>/(count−1):
    /// the old form packed extra members into a FIXED column, which only works if the caps shrink
    /// to match — and shrinking the caps is exactly what the user rejected ("the Use button should
    /// be the same size and follow the values the generic buttons were given"). Sizes now come
    /// purely from the [BoardButtons] tuning, so the stack has to grow instead of the caps
    /// shrinking; growing it symmetrically keeps the cluster centred where the player placed it,
    /// and both directions stay under the player's control through the same spacing/offset dials.
    /// </summary>
    internal static float GenericClusterY(int index, int count, float spacing)
    {
        if (count <= 1)
            return 0f;
        return ((count - 1) * 0.5f - index) * spacing; // member 0 at the top, descending
    }

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

    /// <summary>Items 4/6 live-apply: move + resize the turn-flow ButtonCluster mount (instant).</summary>
    internal void SetClusterLayout(Vector3 offset, float scale)
    {
        if (_clusterMount != null)
        {
            _clusterMount.localPosition = ClusterMountBase + offset;
            _clusterMount.localScale = Vector3.one * (ButtonClusterMountScale * scale);
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

    /// <summary>Items 4/6 live-apply: move the round readout ('Runde N') to a new per-board offset (instant).</summary>
    internal void SetReadoutOffset(Vector3 offset)
    {
        if (_roundLabel != null)
            _roundLabel.transform.localPosition = ReadoutBase + offset;
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

    /// <summary>Fixed base local position of the turn-flow ButtonCluster mount (under the slots).</summary>
    private static Vector3 ClusterMountBase => new(0f, ButtonClusterMountY, -0.006f);

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

    /// <summary>Height budget the "USE" caption is fitted into — the strip BELOW the recess, not the
    /// card's own height (see BuildItemUseSlot for why fitting it to the card height re-created the
    /// overlap the drop is there to prevent).</summary>
    private const float ItemUseLabelHeight = 0.030f;

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
    /// PART F live-apply: rebuild the square Confirm/Undo buttons in place (size changes need a
    /// rebuilt cap). Re-parents onto the SAME anchors, purges the dead laser targets and rebuilds
    /// them from current config. The rest discs are rebuilt separately by CardsDriver
    /// (<c>RestControls.Destroy(); EnsureBuilt(...)</c>).
    /// </summary>
    internal void RebuildAttachedControls()
    {
        if (_root == null)
            return;
        Transform? confirmAnchor = _confirm != null ? _confirm.transform.parent : _confirmAnchor;
        Transform? undoAnchor = _undo != null ? _undo.transform.parent : _undoAnchor;
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
        // Requirement 9a: the item "Use" cluster button is (re)built by BuildButtons — tear the old one
        // down first so a tuning rebuild never leaks/doubles it.
        if (_itemUseConfirm != null)
        {
            Object.DestroyImmediate(_itemUseConfirm.gameObject);
            _itemUseConfirm = null;
        }
        LaserTargets.RemoveAll(static t => t.Collider == null); // drop the just-destroyed (and any other dead) targets
        BuildButtons(confirmAnchor, undoAnchor);
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
        float boardScale = Mathf.Max(0.01f, CardsConfig.BoardScale(board).Value);
        float live = Mathf.Max(1e-4f, _root.localScale.x);
        float trayScale = Mathf.Clamp(live / boardScale, 0.5f, 2f);
        CardsConfig.TrayScale.Value = trayScale;
        float reproduced = trayScale * boardScale;
        if (Mathf.Abs(reproduced - live) > 1e-4f * Mathf.Max(1f, live))
        {
            float adjusted = live / trayScale;
            CardsConfig.BoardScale(board).Value = adjusted;
            VRLog.Info("Cards", $"Board size {live:F2}× is outside what TrayScale alone can express " +
                                $"(0.5–2 × BoardScale {boardScale:F2} = {0.5f * boardScale:F2}–" +
                                $"{2f * boardScale:F2}): BoardScale_{board} re-seated to {adjusted:F2} " +
                                "so the size the player set survives every future re-place.");
        }
        VRLog.Info("Cards", $"Tray layout persisted: fwd {CardsConfig.TrayForward.Value:F2} m, " +
                            $"right {CardsConfig.TrayRight.Value:F2} m, down {CardsConfig.TrayDown.Value:F2} m, " +
                            $"yaw {CardsConfig.TrayYaw.Value:F0}°, pitch {CardsConfig.TrayPitch.Value:F0}° " +
                            $"({CardsConfig.BoardMoveMode.Value}), scale {CardsConfig.TrayScale.Value:F2}×.");
    }
}
