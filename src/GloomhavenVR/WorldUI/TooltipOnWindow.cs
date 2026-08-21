using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE SECOND TOOLTIP FAMILY — the "local" tooltips, which never touch
/// <c>CanvasManager.tooltipCanvas</c> at all (user report 2026-08-21, verbatim: <i>"beim Mouseover
/// über die Gegenstände sollten diese auch in dem Window angezeigt werden. Bei vielem passiert
/// nichts und bei anderen sieht man den Mouseover 3D verdreht, nicht auf dem Fenster sitzend.
/// Gewährleiste dass alle Mouseovers die zu dem Fenster gehören auch perfekt ausgerichtet auf dem
/// Fenster angezeigt werden."</i>).
///
/// <para>THERE IS NOT ONE TOOLTIP MECHANISM IN THIS GAME, THERE ARE TWO, AND ONLY ONE OF THEM WAS
/// EVER COVERED. <see cref="WorldTooltips"/> owns the SHARED singleton — one canvas, one
/// <c>UITooltip</c>, raised by <c>UITooltipTarget</c> and its subclasses
/// (<c>UITextTooltipTarget</c>, <c>UIPrefabTooltipTarget</c>, <c>UIItemCardTooltipTarget</c>,
/// <c>UIPerkModifiersTooltip</c>) and by <c>TextMeshProTooltip</c>. That is the mechanism the
/// merchant's PRICE hint uses, and the ModBuild-190 hardware log proves it works there
/// (<c>Hover hint OWNER = floated window '…UI Shop Item Window' (hovered 'Price' …)</c>). The
/// merchant's ITEM hint is a completely different object: <c>UIPartyItemInventoryTooltip</c>, a
/// widget that already LIVES inside the shop window's own hierarchy, re-parents itself under the
/// hovered slot and positions itself. Nothing in the mod had ever heard of it, which is why the
/// price hint lands perfectly and the item hint does not appear at all.</para>
///
/// <para>THE FAMILY, ENUMERATED FROM THE DECOMPILED SOURCES (every one of these can appear on a
/// floated window; none of them goes near <c>UITooltip</c>):</para>
/// <list type="bullet">
/// <item><description><c>UIPartyItemInventoryTooltip</c> — the item CARD hint of the merchant
/// (<c>UIShopItemInventory.OnHoveredItemBuy/OnHoveredItemSell</c> → <c>itemTooltip.Show(item,
/// itemUI.transform, …)</c>, UIShopItemInventory.cs:922/943/951), the party inventory display and
/// the character equipment display. This is the one the report is about.</description></item>
/// <item><description><c>UILocalTooltip</c> — the base every "local" tooltip derives from, plus its
/// subclasses <c>UIItemLocalTooltip</c> (the quest popup's item card) and
/// <c>UIQuestEnemyStatsPopup</c> (the quest popup's enemy stat card).</description></item>
/// <item><description><c>UITempleSlotTooltip</c> — the temple blessing hint, raised by
/// <c>UITempleShopSlot</c>.</description></item>
/// <item><description><c>TooltipUI</c> — the hint <c>ExtendedButton</c> carries itself
/// (<c>tooltip</c> + <c>autoDisplayTooltip</c>, ExtendedButton.cs:33-34), raised from
/// <c>HighlightFinished</c>.</description></item>
/// </list>
///
/// <para>ROOT CAUSE, AND IT IS ONE DEFECT PRODUCING BOTH REPORTED SYMPTOMS. Every member of the
/// family finishes its placement with the same line — <c>transform.position +=
/// rect.DeltaWorldPositionToFitTheScreen(UIManager.Instance.UICamera, margin)</c>
/// (UIPartyItemInventoryTooltip.cs:96 and :241, UILocalTooltip.cs:91, UITempleSlotTooltip.cs:80
/// and :114; <c>TooltipUI.ToggleEnable</c> uses the <c>DeltaPositionToFitTheScreen</c> twin,
/// TooltipUI.cs:36). That helper is a SCREEN-SPACE clamp expressed as a WORLD delta, and on a
/// floated window every one of its inputs is wrong:</para>
/// <list type="number">
/// <item><description>It computes its bounds with <c>camera.ScreenToWorldPoint(Vector2)</c>
/// (RectTransformExtensions.cs:130-131). A <c>Vector2</c> promotes to a <c>Vector3</c> with
/// <b>z = 0</b>, and z is the DISTANCE FROM THE CAMERA — so on the perspective UI camera both
/// bounds resolve to the camera's own world position. The "delta to fit the screen" it returns is
/// therefore "the delta that puts this rect's corner on the UI camera".</description></item>
/// <item><description>The rect it is measuring is a converted window's content, i.e. world metres
/// at the map room's ~198x rig scale (the ModBuild-190 log measures the shop host at
/// <c>panel scale 0.08255 m/px</c> with anchors around <c>(-192, -798, -270)</c>), while the UI
/// camera is the map camera the mod FREEZES. Hundreds of world units of correction, per hover.</description></item>
/// <item><description>The delta has only world X and Y components (the helper never writes z), so
/// adding it to a rect that hangs under a ROTATED window host moves the box OFF the window's plane
/// — it is a world-axis translation applied to something whose "flat" is the panel's plane.</description></item>
/// </list>
///
/// <para>AND THAT SINGLE TERM EXPLAINS BOTH HALVES OF THE REPORT, because the helper's four
/// branches return DIFFERENT PARTIAL deltas depending on which corner fell outside
/// (RectTransformExtensions.cs:136-147, each clamped with <c>Mathf.Max(0f, …)</c> /
/// <c>Mathf.Min(0f, …)</c>): some hovers get a correction that throws the box clean out of the room
/// — <i>"bei vielem passiert nichts"</i> — and some get a partial one that leaves it visible but
/// off the plane — <i>"bei anderen sieht man den Mouseover 3D verdreht, nicht auf dem Fenster
/// sitzend"</i>. Same line, same frame, different branch.</para>
///
/// <para>THE FIX IS TO CUT THE TERM, NOT TO RE-PLACE THE BOX. This is the important difference from
/// <see cref="MapRoom.HoverCardPose"/>, whose game-side follower had to be stood down outright
/// because <c>UIFollowMapLocationInsideArea</c> computed a genuinely wrong number for every icon
/// from a frozen camera. Here the game's OTHER placement terms are all CORRECT under conversion and
/// must be kept: <c>SetParent(target, worldPositionStays: false)</c> makes the widget a child of the
/// hovered rect, which is a child of the mod's world-space host — so it inherits the window's pose,
/// scale and plane for free — and <c>anchoredPosition = offset</c> is honest uGUI pixels in that
/// same basis. Strip only the screen-fit delta (<see cref="Patches.TooltipWindowPatches"/>) and the
/// vanilla 2D layout IS the correct world layout. Nothing is re-derived, so nothing can drift.</para>
///
/// <para>WHAT THIS CLASS ADDS ON TOP, AND WHY EACH PIECE EXISTS:</para>
/// <list type="bullet">
/// <item><description>FLATTEN (<see cref="Flatten"/>, every frame from <see cref="LateTick"/>).
/// Converted WINDOWS are never flattened: <c>CanvasConversion.Convert</c> takes <c>flatten2D</c> as
/// an opt-in and <c>ModalFallback</c> does not pass it (only PropInfo / StatPanel / EnemyReveal do),
/// so any baked local rotation or local z inside a floated window renders as literal geometry. The
/// item hint carries exactly that: its card comes from <c>ObjectPool.SpawnCard(item.ID, Item,
/// cardHolder, resetLocalScale: true, resetToMiddle: true)</c> — with <c>resetLocalRotation</c> left
/// at its default <b>false</b> (ObjectPool.cs:415, 481-483), while the pool hands back RECYCLED card
/// GameObjects and only ever zeroes their local <b>z</b> (ObjectPool.cs:484-486). A card that was
/// last used somewhere that rotated it arrives still rotated. Under a perspective UI camera that is
/// a styling nudge; on a world-space host it is the reported "3D verdreht". Rotation and z are the
/// two components no game writer re-asserts per frame, so clamping them here can never become a
/// write war.</description></item>
/// <item><description>THE IN-PLANE CLAMP (<see cref="Settle"/>). Cutting the screen fit removes the
/// game's own "keep it on screen" rule, so it is replaced by the same rule measured against the
/// RIGHT rectangle: the owning window. Deliberately NOT run from <see cref="LateTick"/> — it writes
/// the same x/y the game writes, and two writers of one number in two unordered LateUpdates is the
/// alternating value that makes the two eyes disagree in MultiPass. It runs as a POSTFIX on the
/// game's own placement calls instead, so it is always the last write of that call, by
/// construction.</description></item>
/// <item><description>ATTRIBUTION (<see cref="NoteSlotHover"/>). A tooltip that never appears must
/// not be mistakable for an item that simply has no tooltip. Every known tooltip-bearing slot
/// reports its hover here, and if nothing is placed within <see cref="HoverVerdictFrames"/> the
/// class writes ONE line per slot kind carrying a census of the family inside that window — how many
/// instances exist, how many are active — so the next hardware log answers "nothing was raised" vs
/// "something was raised and went somewhere invisible" with numbers.</description></item>
/// </list>
///
/// <para>OWNERSHIP, NOT TOPMOST-NESS, AND NOT PRESENCE. Every decision here is gated on
/// <see cref="ModalFallback.FindOwningWindow"/> — the floated host that is an ANCESTOR of the widget
/// — for the same reason <see cref="WorldTooltips.ResolveHostOwner"/> is: a widget that belongs to
/// no floated window (the flat screen, a board surface, a scenario canvas) is left EXACTLY as the
/// game left it, screen fit and all.</para>
/// </summary>
internal static class TooltipOnWindow
{
    private const string Scope = "WorldUI";

    /// <summary>Frames between two allocating <c>GetComponentsInChildren</c> censuses of a floated
    /// window. The cached widgets are re-checked every frame (a field read); only the SCAN is
    /// gated, because a window's tooltip widgets are created with the window, not per hover.</summary>
    private const int ScanIntervalFrames = 20;

    /// <summary>Frames a slot hover is given to produce a tooltip before it is declared silent.
    /// Generous on purpose: <c>UIShopItemInventory</c> raises one of its hints through
    /// <c>SkipAFrameAndNotifyNewItemTooltip</c> (a <c>WaitForEndOfFrame</c> coroutine,
    /// UIShopItemInventory.cs:973), and a <c>UIWindow.Show()</c> may fade in.</summary>
    private const int HoverVerdictFrames = 12;

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity — same
    /// threshold as <c>CanvasConversion.FlattenSubtree</c> and <c>WorldTooltips.FlattenSubtree</c>,
    /// so the three flatteners agree on what "flat" means.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels (see above).</summary>
    private const float FlattenZEpsilon = 0.01f;

    /// <summary>World metres of overhang past the window edge below which the clamp does nothing —
    /// a box that is already inside must never be nudged, or the "correction" itself becomes the
    /// per-frame jitter.</summary>
    private const float ClampEpsilon = 1e-4f;

    /// <summary>
    /// A transform inside a local tooltip whose real 3D we clamped — its original local z and local
    /// rotation, for <see cref="Shutdown"/>. Same record shape and same discipline as
    /// <c>WorldTooltips.FlattenEntry</c>: destroyed entries are pruned, so the list stays bounded to
    /// the live (pooled) subtree.
    /// </summary>
    private struct FlatRecord
    {
        public Transform Transform;
        public float OriginalLocalZ;
        public Quaternion OriginalLocalRotation;
    }

    private static readonly List<FlatRecord> Flattened = new(64);

    // ---- the family table --------------------------------------------------------------------
    // Matched BY COMPONENT TYPE, never by name. UILocalTooltip is listed as the base on purpose:
    // GetComponentsInChildren<UILocalTooltip> returns UIItemLocalTooltip and UIQuestEnemyStatsPopup
    // too, so the three of them are one entry and a fourth subclass would need no change here.
    private static readonly List<UILocalTooltip> LocalScratch = new(8);
    private static readonly List<UIPartyItemInventoryTooltip> PartyItemScratch = new(8);
    private static readonly List<UITempleSlotTooltip> TempleScratch = new(8);
    private static readonly List<TooltipUI> ButtonScratch = new(16);

    /// <summary>One live local-tooltip widget inside a floated window, with the kind label its
    /// diagnostics are deduped by.</summary>
    private readonly struct Known
    {
        public readonly Component Widget;
        public readonly string Kind;

        public Known(Component widget, string kind)
        {
            Widget = widget;
            Kind = kind;
        }
    }

    private static readonly List<Known> Live = new(16);
    private static int _scanFrame = int.MinValue;

    /// <summary>Kinds whose "matched + placed" evidence line was already written (once per kind per
    /// session — the steady state of a held hover must not allocate a log line per frame).</summary>
    private static readonly HashSet<string> PlacedLogged = new();

    /// <summary>Slot kinds whose "hover produced nothing" verdict was already written.</summary>
    private static readonly HashSet<string> SilentLogged = new();

    /// <summary>Kinds whose screen-fit cut was already reported (written from the patch).</summary>
    private static readonly HashSet<string> CutLogged = new();

    // ---- the silent-hover watch ---------------------------------------------------------------
    private static bool _hoverPending;
    private static string _hoverKind = string.Empty;
    private static string _hoverWhat = string.Empty;
    private static ConvertedPanel? _hoverWindow;
    private static int _hoverFrame;

    /// <summary>The window and frame of the most recent successful <see cref="Settle"/> — the
    /// evidence the silent-hover watch checks itself against.</summary>
    private static ConvertedPanel? _lastSettleWindow;
    private static int _lastSettleFrame = int.MinValue;

    // Reused measurement buffers (no steady-state allocation). Two of them, because the clamp
    // measures the BOX and the WINDOW in one expression and one shared buffer would clobber itself
    // — the same reason WorldTooltips carries CornerScratch and CornerScratchB.
    private static readonly Vector3[] BoxCorners = new Vector3[4];
    private static readonly Vector3[] WinCorners = new Vector3[4];
    private static readonly List<Transform> SubtreeScratch = new(128);

    /// <summary>
    /// PER-FRAME, AND DELIBERATELY ONLY THE TWO COMPONENTS NO GAME WRITER RE-ASSERTS: local rotation
    /// and local z. The in-plane position is settled from the game's own placement calls instead
    /// (<see cref="Settle"/>) — see the class doc for why that split is not optional.
    ///
    /// <para>Registered in <c>WorldUIModule</c> AFTER <c>CanvasConversion.Late</c>, so a panel whose
    /// own flatten sweep runs (the opt-in surfaces) has already had its say, and BEFORE
    /// <c>CanvasConversion.Order</c>, which must stay last because it measures poses.</para>
    /// </summary>
    internal static void LateTick()
    {
        if (!WorldUIConfig.ConversionActive)
            return;

        PruneFlattened();
        Rescan();

        for (int i = 0; i < Live.Count; i++)
        {
            Component widget = Live[i].Widget;
            if (widget == null)
                continue;
            // A widget that is not drawn costs nothing to skip, and skipping it means the flatten
            // never runs against a hierarchy the game is mid-way through rebuilding (the tooltip's
            // card is pooled: recycled on hide, re-spawned on show).
            if (!widget.gameObject.activeInHierarchy)
                continue;
            if (ModalFallback.FindOwningWindow(widget.transform) == null)
                continue; // not inside a floated window — vanilla behaviour, untouched
            Flatten(widget.transform);
        }

        TickSilentHoverWatch();
    }

    /// <summary>
    /// Put ONE local tooltip where it belongs, called as a postfix on the game's own placement
    /// method for that family (so it is always the last write of that call — see the class doc).
    /// A widget outside every floated window returns immediately and keeps today's behaviour.
    /// </summary>
    internal static void Settle(Component? widget, string kind)
    {
        if (!WorldUIConfig.ConversionActive || widget == null)
            return;
        if (widget.transform is not RectTransform rect)
            return;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(rect);
        if (owner == null || !owner.IsAlive || owner.HostRect == null)
            return;

        // Flatten FIRST: the clamp measures the box's world corners, and a box that is still
        // carrying a stale pooled rotation would be measured at the wrong extent.
        Flatten(rect);

        RectTransform host = owner.HostRect;
        Quaternion hostRot = host.rotation;
        Vector3 right = hostRot * Vector3.right;
        Vector3 up = hostRot * Vector3.up;

        // Unity's GetWorldCorners order: 0 = bottom-left, 1 = top-left, 2 = top-right,
        // 3 = bottom-right. Distances give the window's half extents in its own basis; the box is
        // PROJECTED onto that same basis so a widget nested in a scrolled or re-pivoted sub-rect is
        // still sized along the axes it is being placed along.
        host.GetWorldCorners(WinCorners);
        rect.GetWorldCorners(BoxCorners);
        Vector3 winCenter = (WinCorners[0] + WinCorners[2]) * 0.5f;
        float winHalfW = Vector3.Distance(WinCorners[0], WinCorners[3]) * 0.5f;
        float winHalfH = Vector3.Distance(WinCorners[0], WinCorners[1]) * 0.5f;
        Vector3 boxCenter = (BoxCorners[0] + BoxCorners[2]) * 0.5f;
        float boxHalfW = Mathf.Abs(Vector3.Dot(BoxCorners[3] - BoxCorners[0], right)) * 0.5f;
        float boxHalfH = Mathf.Abs(Vector3.Dot(BoxCorners[1] - BoxCorners[0], up)) * 0.5f;

        Vector3 off = boxCenter - winCenter;
        float offX = Vector3.Dot(off, right);
        float offY = Vector3.Dot(off, up);
        // A box LARGER than the window clamps to the window centre (the max() floors) instead of
        // flipping sign — overflowing symmetrically is the readable failure, and it is the same
        // rule WorldTooltips.ResolveMenuFrameCenter uses for the shared canvas.
        float limX = Mathf.Max(0f, winHalfW - boxHalfW);
        float limY = Mathf.Max(0f, winHalfH - boxHalfH);
        float wantX = Mathf.Clamp(offX, -limX, limX);
        float wantY = Mathf.Clamp(offY, -limY, limY);

        float dx = wantX - offX;
        float dy = wantY - offY;
        bool corrected = Mathf.Abs(dx) > ClampEpsilon || Mathf.Abs(dy) > ClampEpsilon;
        if (corrected)
        {
            // Applied through the widget's OWN PARENT basis, never as a world position: writing
            // transform.position on a child of a rotated, 198x-scaled host is precisely the
            // mistake this whole class exists to undo. z is dropped because the correction is
            // in-plane by construction and the flatten above owns z.
            Vector3 worldDelta = right * dx + up * dy;
            Transform? parent = rect.parent;
            Vector3 localDelta = parent != null ? parent.InverseTransformVector(worldDelta) : worldDelta;
            Vector3 lp = rect.localPosition;
            rect.localPosition = new Vector3(lp.x + localDelta.x, lp.y + localDelta.y, 0f);
        }

        _lastSettleWindow = owner;
        _lastSettleFrame = Time.frameCount;

        if (!PlacedLogged.Add(kind))
            return;
        Vector3 lossy = host.lossyScale;
        VRLog.Info(Scope, DrawOrderEvidence(rect, owner));
        VRLog.Info(Scope,
            $"LOCAL TOOLTIP '{kind}' laid FLAT ON its OWNING floated window "
            + $"'{(owner.HostGo != null ? owner.HostGo.name : "?")}' — it hangs under "
            + $"'{(rect.parent != null ? rect.parent.name : "<no parent>")}' inside that host, so it "
            + $"inherits the window's plane, rotation and scale ({lossy.x:F5} m/px) by PARENTING "
            + "rather than by a pose this mod computes. Box "
            + $"{boxHalfW * 2f:F3}x{boxHalfH * 2f:F3} m in a {winHalfW * 2f:F3}x{winHalfH * 2f:F3} m "
            + $"window; in-plane clamp {(corrected ? $"MOVED it by ({dx:F3}, {dy:F3}) m to keep it inside" : "was a no-op (already inside)")}. "
            + "The game's own screen-fit term is cut for this rect (see TooltipWindowPatches): "
            + "DeltaWorldPositionToFitTheScreen resolves BOTH of its screen bounds to the UI "
            + "camera's own position (ScreenToWorldPoint of a Vector2 carries z = 0, i.e. zero "
            + "distance from the camera) and returns a WORLD X/Y delta — which on a rotated host at "
            + "map-room rig scale is hundreds of units in a direction that has nothing to do with "
            + "the window's plane. That single term is both reported symptoms: a full branch throws "
            + "the box out of the room ('nichts passiert'), a partial branch leaves it visible but "
            + "off the plane ('3D verdreht').");
    }

    /// <summary>
    /// "IN FRONT OF ITS HOST" IS A DRAW-ORDER QUESTION, AND IT IS NOT THIS CLASS'S TO ANSWER —
    /// so it is MEASURED here instead of assumed. Geometry cannot decide it: this widget is a real
    /// CHILD of the window's host, so it is coplanar with it by construction, and pushing it proud
    /// would fight the flatten that keeps it on the plane in the first place. What decides it is
    /// uGUI's own rule: a nested canvas with <c>overrideSorting</c> draws at its own
    /// <c>sortingOrder</c> (which <c>CanvasConversion</c>'s adoption owns — it clears the flag, and
    /// concedes ownership of the NUMBER when a game writer keeps flipping it back), and a widget
    /// with no canvas of its own draws in HIERARCHY order, i.e. after its earlier siblings and
    /// BEFORE its later ones.
    ///
    /// <para>That second case is the one that could still hide a correctly placed box: the tooltip
    /// re-parents itself under the hovered slot, so every list row BELOW that slot is a later
    /// sibling of the slot's own parent and paints over it. This line reports the numbers that
    /// separate "the box is somewhere invisible" from "the box is exactly where it should be and
    /// something is painted on top of it", once per kind, so the next hardware log settles it as a
    /// fact rather than as my guess.</para>
    /// </summary>
    private static string DrawOrderEvidence(RectTransform rect, ConvertedPanel owner)
    {
        Canvas own = rect.GetComponent<Canvas>();
        Canvas nested = own != null ? own : rect.GetComponentInParent<Canvas>();
        Transform? parent = rect.parent;
        int index = parent != null ? rect.GetSiblingIndex() : -1;
        int siblings = parent != null ? parent.childCount : 0;
        int hostOrder = owner.HostCanvas != null ? owner.HostCanvas.sortingOrder : 0;
        return "LOCAL TOOLTIP draw order (measured, not assumed): the box hangs at sibling "
               + $"{index + 1} of {siblings} under '{(parent != null ? parent.name : "<none>")}'; "
               + $"nearest canvas '{(nested != null ? nested.name : "<none — draws in the host's batch>")}' "
               + $"overrideSorting={(nested != null && nested.overrideSorting ? "TRUE" : "false")} "
               + $"order={(nested != null ? nested.sortingOrder.ToString() : "n/a")}, host canvas order "
               + $"{hostOrder}. READ IT LIKE THIS: with overrideSorting false and no canvas of its own "
               + "the box draws in HIERARCHY order, so any later sibling of its parent paints over it "
               + "— if the box is reported placed but still cannot be seen, that is the reason and the "
               + "fix belongs in the nested-canvas adoption (CanvasConversion), not here. With "
               + "overrideSorting TRUE the number shown is what it actually draws at, and it must be "
               + "at or above the host order to be in front.";
    }

    /// <summary>
    /// A tooltip-bearing SLOT was hovered. Recorded so that a hover which produces no tooltip at
    /// all becomes an attributable log line instead of silence (<see cref="TickSilentHoverWatch"/>).
    ///
    /// <para>The slot type is resolved by CONTAINMENT on purpose, and that is not the mistake
    /// <c>IsMapRoomHoverCard</c> made: the question here really is "is this button PART OF an item
    /// slot", not "IS this button an item slot" — the hover arrives on whatever graphic the pointer
    /// entered, and the slot is its ancestor by design.</para>
    /// </summary>
    internal static void NoteSlotHover(Component? slot, string kind, bool hovered)
    {
        if (!WorldUIConfig.ConversionActive || slot == null)
            return;
        if (!hovered)
        {
            _hoverPending = false;
            return;
        }
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(slot.transform);
        if (owner == null)
            return; // slot is not in a floated window — nothing here applies
        _hoverPending = true;
        _hoverKind = kind;
        _hoverWhat = slot.gameObject.name;
        _hoverWindow = owner;
        _hoverFrame = Time.frameCount;
    }

    /// <summary>
    /// Report ONCE per rect kind that the game's screen-fit term was cut for a widget on a floated
    /// window. Called from the patch, which is the only place that knows which helper was asked.
    /// </summary>
    internal static void NoteScreenFitCut(string helper, RectTransform rect, ConvertedPanel owner)
    {
        // Deduped on the HELPER (a compile-time literal), not on a composed key: this runs on the
        // game's own placement path — for UILocalTooltip that is every LateUpdate of every shown
        // hint — so the steady state must be one HashSet lookup and nothing else. Composing the key
        // would allocate a string per frame, and DescribeKind's four GetComponentInParent walks are
        // exactly the work that must not happen once the line has been written.
        if (!CutLogged.Add(helper))
            return;
        VRLog.Info(Scope,
            $"SCREEN-FIT CUT: the game's '{helper}' was asked to keep '{rect.name}' "
            + $"({DescribeKind(rect)}) on screen, but that rect lives inside the floated window "
            + $"'{(owner.HostGo != null ? owner.HostGo.name : "?")}' — world metres at map-room rig "
            + "scale, measured against a UI camera this mod has frozen. The helper resolves both of "
            + "its screen bounds through camera.ScreenToWorldPoint(Vector2), whose implicit z = 0 is "
            + "ZERO DISTANCE FROM THE CAMERA, so its 'delta to fit the screen' is really 'the delta "
            + "that drags this corner onto the UI camera' — a world X/Y translation that takes the "
            + "box off the window's plane. Returning zero instead: the widget is already a CHILD of "
            + "the window's host, so the game's remaining terms (SetParent + anchoredPosition) "
            + "place it correctly on their own. Rects outside every floated window are untouched.");
    }

    /// <summary>Restore every local 3D we clamped and drop all per-session state (hot reload /
    /// module shutdown). Reversibility contract: the values we wrote were the game's own — a pooled
    /// card's leftover rotation, a prefab's baked depth — and the flat 2D UI reads them exactly as
    /// it did before the mod touched them.</summary>
    internal static void Shutdown()
    {
        for (int i = 0; i < Flattened.Count; i++)
        {
            Transform tf = Flattened[i].Transform;
            if (tf == null)
                continue;
            Vector3 lp = tf.localPosition;
            tf.localPosition = new Vector3(lp.x, lp.y, Flattened[i].OriginalLocalZ);
            tf.localRotation = Flattened[i].OriginalLocalRotation;
        }
        Flattened.Clear();
        Live.Clear();
        LocalScratch.Clear();
        PartyItemScratch.Clear();
        TempleScratch.Clear();
        ButtonScratch.Clear();
        PlacedLogged.Clear();
        SilentLogged.Clear();
        CutLogged.Clear();
        _scanFrame = int.MinValue;
        _hoverPending = false;
        _hoverWindow = null;
        _lastSettleWindow = null;
        _lastSettleFrame = int.MinValue;
    }

    // ---- internals ----------------------------------------------------------------------------

    /// <summary>
    /// Rebuild the list of local-tooltip widgets living inside floated windows. Bounded to the
    /// CONVERTED panels rather than the scene: the only tooltips this class may touch are the ones
    /// a floated window owns, so the panel list is both the correct scope and the cheap one. The
    /// allocating component walk is cadence-gated (<see cref="ScanIntervalFrames"/>); the poses are
    /// re-asserted every frame from the cached list.
    /// </summary>
    private static void Rescan()
    {
        if (_scanFrame != int.MinValue && Time.frameCount - _scanFrame < ScanIntervalFrames)
            return;
        _scanFrame = Time.frameCount;

        Live.Clear();
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel panel = panels[i];
            if (panel == null || !panel.IsAlive || panel.HostGo == null)
                continue;
            GameObject host = panel.HostGo;

            host.GetComponentsInChildren(includeInactive: true, LocalScratch);
            for (int k = 0; k < LocalScratch.Count; k++)
                Add(LocalScratch[k], KindOfLocal(LocalScratch[k]));

            host.GetComponentsInChildren(includeInactive: true, PartyItemScratch);
            for (int k = 0; k < PartyItemScratch.Count; k++)
                Add(PartyItemScratch[k], "UIPartyItemInventoryTooltip (item card hint)");

            host.GetComponentsInChildren(includeInactive: true, TempleScratch);
            for (int k = 0; k < TempleScratch.Count; k++)
                Add(TempleScratch[k], "UITempleSlotTooltip (blessing hint)");

            host.GetComponentsInChildren(includeInactive: true, ButtonScratch);
            for (int k = 0; k < ButtonScratch.Count; k++)
                Add(ButtonScratch[k], "TooltipUI (ExtendedButton hint)");
        }
        LocalScratch.Clear();
        PartyItemScratch.Clear();
        TempleScratch.Clear();
        ButtonScratch.Clear();
    }

    private static void Add(Component? widget, string kind)
    {
        if (widget != null)
            Live.Add(new Known(widget, kind));
    }

    /// <summary>The concrete subclass is the useful label here (the base is shared by three very
    /// different hints), so the kind is read off the instance rather than off the table.</summary>
    private static string KindOfLocal(UILocalTooltip t) =>
        t == null ? "UILocalTooltip" : t.GetType().Name + " (UILocalTooltip)";

    /// <summary>Name the family a rect belongs to, for the cut diagnostic. Containment, because the
    /// helper is called on the WIDGET's rect and the component may sit on a parent of it.</summary>
    private static string DescribeKind(RectTransform rect)
    {
        if (rect.GetComponentInParent<UIPartyItemInventoryTooltip>() != null)
            return "UIPartyItemInventoryTooltip";
        UILocalTooltip local = rect.GetComponentInParent<UILocalTooltip>();
        if (local != null)
            return local.GetType().Name;
        if (rect.GetComponentInParent<UITempleSlotTooltip>() != null)
            return "UITempleSlotTooltip";
        if (rect.GetComponentInParent<TooltipUI>() != null)
            return "TooltipUI";
        return "other floated-window content";
    }

    /// <summary>
    /// Clamp the REAL 3D out of one tooltip subtree — local rotation to identity, local z to zero —
    /// recording every write for <see cref="Shutdown"/>. The ROOT is included, unlike
    /// <c>CanvasConversion.FlattenSubtree</c>, which skips <c>panel.Target</c> because the
    /// conversion owns that pose: here the root IS the rect the game writes a world position on, so
    /// its own z is exactly the out-of-plane error and must be zeroed too. X/Y are NEVER touched —
    /// slide/fade animations keep playing flat, and the in-plane position belongs to
    /// <see cref="Settle"/>.
    /// </summary>
    private static void Flatten(Transform root)
    {
        SubtreeScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, SubtreeScratch);
        for (int i = 0; i < SubtreeScratch.Count; i++)
        {
            Transform tf = SubtreeScratch[i];
            if (tf == null)
                continue;
            Vector3 lp = tf.localPosition;
            Quaternion lr = tf.localRotation;
            bool tiltedRot = Quaternion.Angle(lr, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(lp.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!IsRecorded(tf))
            {
                Flattened.Add(new FlatRecord
                {
                    Transform = tf,
                    OriginalLocalZ = lp.z,
                    OriginalLocalRotation = lr,
                });
            }
            if (tiltedRot)
                tf.localRotation = Quaternion.identity;
            if (tiltedZ)
                tf.localPosition = new Vector3(lp.x, lp.y, 0f);
        }
        SubtreeScratch.Clear();
    }

    private static bool IsRecorded(Transform tf)
    {
        for (int i = 0; i < Flattened.Count; i++)
        {
            if (ReferenceEquals(Flattened[i].Transform, tf))
                return true;
        }
        return false;
    }

    /// <summary>Pooled tooltip content comes and goes per hover, so the flatten record must stay
    /// bounded to the live subtree — the same discipline as the other two flatteners.</summary>
    private static void PruneFlattened()
    {
        for (int i = Flattened.Count - 1; i >= 0; i--)
        {
            if (Flattened[i].Transform == null)
                Flattened.RemoveAt(i);
        }
    }

    /// <summary>
    /// A HOVER THAT PRODUCED NOTHING MUST SAY SO. Fires at most once per slot kind and carries the
    /// census that separates the two possible answers: "this window contains no tooltip widget of
    /// any known family" (the mechanism is one this class does not cover yet — name it and it can
    /// be added) versus "a widget exists and is even active, but nothing placed it" (the raise path
    /// ran and the box went somewhere invisible, or the raise was withdrawn by a hover flicker).
    /// </summary>
    private static void TickSilentHoverWatch()
    {
        if (!_hoverPending || Time.frameCount - _hoverFrame < HoverVerdictFrames)
            return;
        _hoverPending = false;

        ConvertedPanel? window = _hoverWindow;
        if (window == null || !window.IsAlive)
            return;
        // Something WAS placed in this very window since the hover — the mechanism works; nothing
        // to report.
        if (ReferenceEquals(_lastSettleWindow, window) && _lastSettleFrame >= _hoverFrame)
            return;
        if (!SilentLogged.Add(_hoverKind))
            return;

        int total = 0;
        int active = 0;
        var families = new HashSet<string>();
        for (int i = 0; i < Live.Count; i++)
        {
            Component widget = Live[i].Widget;
            if (widget == null || ModalFallback.FindOwningWindow(widget.transform) != window)
                continue;
            total++;
            families.Add(Live[i].Kind);
            if (widget.gameObject.activeInHierarchy)
                active++;
        }

        VRLog.Warn(Scope,
            $"SILENT HOVER: '{_hoverKind}' on '{_hoverWhat}' inside the floated window "
            + $"'{(window.HostGo != null ? window.HostGo.name : "?")}' raised NO local tooltip "
            + $"within {HoverVerdictFrames} frames. Census of that window: {total} known local-tooltip "
            + $"widget(s), {active} of them active — [{(families.Count == 0 ? "none" : string.Join(", ", families))}]. "
            + "READ IT LIKE THIS: zero widgets means this slot uses a tooltip MECHANISM this class "
            + "does not cover (the shared UITooltip canvas is WorldTooltips' business and logs "
            + "separately; anything else is a family to add here, and its name is the fix). A "
            + "non-zero count with zero active means the game never called Show — the raise was "
            + "blocked or withdrawn upstream (a hover flicker: every pointer EXIT runs the slot's "
            + "own Hide). A non-zero count WITH active widgets means the widget is up and this "
            + "class failed to settle it — which would be a bug here, not upstream. CONSEQUENCE: "
            + "the player sees no explanation for this item; nothing else is affected. Further "
            + "silent hovers of this kind are not logged.");
    }
}
