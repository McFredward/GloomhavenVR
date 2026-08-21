using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

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
/// <para>THE SECOND HALF OF THE SAME STORY (user report 2026-08-21 against ModBuild 190, verbatim:
/// <i>"Beim Händler sind nun gar keine Mouseovers mehr sichtbar von den Gegenständen."</i>). The
/// placement above is CORRECT and the user confirms it landed — and the box still cannot be seen.
/// THE MOD'S OWN INSTRUMENTATION PREDICTED THIS ONE BUILD IN ADVANCE and named the cause, so it is
/// read here as a measurement rather than re-derived as a theory (Player.log of that session,
/// lines 3811 / 3859-3860):</para>
/// <list type="number">
/// <item><description><c>Adopted nested canvas 'UI Party Inventory Item Tooltip' in
/// 'GloomhavenVR.Panel_Modal_UI Shop Item Window' (overrideSorting <b>True→false</b>,
/// sortingOrder=1000 …)</c> — the widget ships with its OWN canvas at <c>overrideSorting = true,
/// sortingOrder = 1000</c>. That is not decoration: it is the entire reason a vanilla item hint is
/// visible at all. <c>CanvasConversion</c>'s generic nested-canvas adoption clears the flag (so a
/// nested canvas cannot beat the host's distance ladder — CanvasConversion.2.Adopt.cs), and it
/// carved out exactly one exception, the uGUI dropdown overlay. The tooltip is the same class of
/// object and had no carve-out.</description></item>
/// <item><description><c>the box hangs at sibling 6 of 6 under 'UI Shop Item Slot Variant(Clone)';
/// nearest canvas … overrideSorting=false order=132, host canvas order 132</c> — with the flag
/// cleared the box draws in HIERARCHY order, and it lives inside ONE item row of a scroll list.
/// Every LATER ROW is a later sibling of an ancestor and paints over it.</description></item>
/// </list>
///
/// <para>AND CLEARING THAT ONE FLAG COSTS A SECOND THING, WHICH IS PROBABLY THE LOUDER HALF. uGUI
/// resolves a graphic's clipper by walking up the parents and STOPPING at the first canvas with
/// <c>overrideSorting</c> (<c>MaskUtilities.GetRectMaskForClippable</c> / <c>GetStencilDepth</c>,
/// which takes <c>FindRootSortOverrideCanvas</c> as its stop). An overriding canvas is therefore
/// how a vanilla tooltip escapes the clipper of the list it is parented into — and the merchant's
/// item list is a <c>ScrollRect</c> with a working viewport clipper (the mod's own SCROLL CLIP pass
/// logged nothing for that window, i.e. it found one already there). With the flag cleared the box
/// is clipped to the item list's viewport, and the item hint is drawn BESIDE the row, i.e. largely
/// outside it. Clipped away entirely reads as <i>"gar keine Mouseovers mehr sichtbar"</i> exactly
/// as well as painted-over does; both are one write, and both end here.</para>
///
/// <para>THE FIX IS A REPARENT, NOT A SORTING WRITE, AND THAT IS A RULING RATHER THAN A
/// PREFERENCE. Re-asserting <c>overrideSorting</c> on that canvas would put this class in a
/// per-frame write war with the mod's OWN adoption guard (<c>CanvasConversion.ReassertAdoptedSorting</c>
/// clears it every frame for a modal host) — and the value alternating frame to frame is precisely
/// what made the two MultiPass eyes disagree in ModBuild 179 (<i>"flackert stark"</i>,
/// <see cref="NestedCanvasRecord.ConcededOverrideSorting"/>). Owning the NUMBER instead is no
/// better: the conceded branch writes that number every frame too. So this class touches no flag
/// and no order. It moves the box to the LAST CHILD OF THE WINDOW'S OWN CONTENT ROOT
/// (<see cref="RaiseToWindowTop"/>), which answers both halves with one hierarchy write: last
/// sibling of the top level is painted after every other piece of that window's content, and a
/// clipper it is no longer a descendant of cannot clip it. The world pose the placement above
/// produced is carried across verbatim, so nothing that works today moves by a millimetre.</para>
///
/// <para>AND IT IS THE GAME'S OWN WRITE, IN A WIDER PARENT. The item hint carries a
/// <c>UIWindow</c>, and <c>UIWindow.Show</c> → <c>Focus</c> already ends in
/// <c>transform.SetAsLastSibling()</c> for exactly this widget (UIWindow.cs:445-451 via
/// <c>UIUtility.BringToFront</c>, UIUtility.cs:19). "Be the last thing drawn in my parent" is
/// therefore the game's own stated intent for this box, not an invention here; all this class
/// changes is WHICH parent that "last" is measured in, from one row of a scroll list to the window
/// the row belongs to. Nobody else writes a tooltip's sibling index, so there is exactly one writer
/// of the value — the condition the sorting flag could never satisfy.</para>
///
/// <para>WHAT THIS CLASS ADDS ON TOP, AND WHY EACH PIECE EXISTS:</para>
/// <list type="bullet">
/// <item><description>THE RAISE (<see cref="RaiseToWindowTop"/>, from <see cref="Settle"/>). See
/// above. FULLY REVERSIBLE and only ever while the box is still OURS: parent, sibling index,
/// anchors, pivot, anchored position and local scale are recorded before the first move and written
/// back on release, on stand-down and on teardown — and the moment the game re-parents the box
/// itself (it does, on every hover: <c>SetParent(target, worldPositionStays: false)</c>) the record
/// is dropped and re-taken instead of fought over.</description></item>
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
///
/// <para>=====================================================================================</para>
///
/// <para>THE THIRD FAMILY, AND IT IS NOT A TOOLTIP AT ALL (ModBuild 194). User report, verbatim:
/// <i>"Wenn ich das Kartenmenü eines Characters in der UI aufmache werden die mouseover Karten nicht
/// richtig angezeigt, in dem Menü in dem man die Karten umstellen kann, was man auswählt und was
/// nicht. Gewährleiste das hier die mouseovers funktionieren."</i> That screen is
/// <c>UIPartyCharacterAbilityCardsDisplay</c>, and THE FIRST QUESTION WAS WHETHER ITS HOVER POPUP
/// GOES THROUGH EITHER MECHANISM ABOVE. It does not, and it is not a popup either: hovering a row
/// switches on the card's OWN <c>FullAbilityCard</c> child (<c>AbilityCardUI.OnPointerEnter</c> →
/// <c>ToggleFullCardPreview(true, fullCardHolder)</c>, AbilityCardUI.cs:393 / :1031). There is no
/// shared instance, no <c>UITooltip</c>, no <c>UILocalTooltip</c>: <see cref="WorldTooltips"/> never
/// sees it and the family table below never matched it.</para>
///
/// <para>SO WHICH OF THE TWO PRIOR FIXES REACHED IT, MEASURED RATHER THAN ASSUMED — the ModBuild-193
/// hardware log answers both halves on its own lines:</para>
/// <list type="number">
/// <item><description>THE 190 CUT DID REACH IT, AND IT WAS NOT ENOUGH. Player.log line 14493:
/// <c>SCREEN-FIT CUT: … 'DeltaWorldPositionToFitTheScreen(camera, marginX, marginY)' … 'Full' (other
/// floated-window content) … inside the floated window 'GloomhavenVR.Panel_Modal_New Party display'</c>.
/// The two-margin overload is <c>AbilityCardUI.cs:1075</c>, so the cut fired — the prefix is on the
/// extension method and is blind to which family asked. But the cut only zeroes the DELTA that is
/// ADDED afterwards, and the line BEFORE it is the real damage:
/// <c>fullAbilityCard.transform.position = new Vector3(base.transform.position.x + 40f,
/// base.transform.position.y - 45f)</c> (AbilityCardUI.cs:1074). That is an ABSOLUTE WORLD position
/// built from two pixel-sized constants along the WORLD x/y axes — and a two-argument
/// <c>Vector3</c> carries <b>z = 0</b>, so the card is also teleported to world z = 0. On the flat
/// screen those units are canvas pixels and the write is correct; on a floated host whose own pose
/// is <c>(52.53, 167.99, 90.40)</c> world units, yawed, at 0.1734 m per uGUI pixel, it throws the
/// card ~90 units off the window plane and ~40 units sideways. Same bug CLASS as 190 — a
/// screen-space assumption expressed as a world write — different TERM, and the 190 patch could
/// never have covered it because it patches the helper, not the assignment.</description></item>
/// <item><description>THE 192 FIX DID NOT REACH IT, AND IT NEEDS TO. Player.log, same second:
/// <c>Adopted nested canvas 'Full' in 'GloomhavenVR.Panel_Modal_New Party display' (overrideSorting
/// <b>True→false</b>, sortingOrder=116, raycaster added)</c>. <c>ToggleFullCardPreview</c> ADDS a
/// <c>Canvas</c> with <c>overrideSorting = true, sortingOrder = 10</c> on show and DESTROYS it on
/// hide (AbilityCardUI.cs:1049-1061) — the same "escape my list's clipper and draw over it" trick
/// the merchant's item hint uses, and the conversion's generic nested-canvas adoption clears the
/// flag for the same reason. The card lives in <c>abilityCardsPanel.content</c>, i.e. inside a
/// <c>ScrollRect</c> with a working viewport clipper, and it is drawn BESIDE its row on purpose. So
/// with the flag cleared it is both painted over by later rows and cut to the viewport it is
/// deliberately outside of. <see cref="RaiseToWindowTop"/> is exactly the answer, and nothing was
/// calling it for this widget.</description></item>
/// </list>
///
/// <para>THE FIX IS THEREFORE THE SAME TWO MOVES THIS CLASS ALREADY MAKES, EXTENDED TO ONE MORE
/// WIDGET, plus the one thing that is genuinely new: the broken term here is an ASSIGNMENT rather
/// than an added helper, so it cannot be zeroed — it has to be replaced. <see cref="PlaceFullCardPreview"/>
/// re-expresses the game's OWN intent (+40, −45 uGUI pixels from the card row's pivot) in the
/// WINDOW'S basis instead of the world's, then hands the widget to <see cref="Settle"/> for the
/// identical flatten / raise / in-plane-clamp pass every other family gets. It runs as a PREFIX that
/// SKIPS <c>ChangeFullCardPosition</c>, not as a postfix that corrects it, so the world write never
/// lands even for one frame. Nothing on the wire, nothing about game state: this moves one widget
/// inside one player's own converted window.</para>
///
/// <para>AND IT IS RELEASED ON EVERY UN-HOVER (<see cref="ReleaseRaiseOf"/>, from the
/// <c>ToggleFullCardPreview(false)</c> postfix), which matters more here than for the tooltips: an
/// <c>AbilityCardUI</c> is POOLED, and <c>ObjectPool.RecycleCard</c> recycles the ROW, not the full
/// card. A full card left parented to the window's content root would be an orphan the pool cannot
/// see. The pool does carry a defensive re-parent for exactly this (ObjectPool.cs:545-547), but
/// relying on someone else's safety net instead of handing the widget back is not the discipline
/// this project uses.</para>
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

    /// <summary>Relative deviation of the two parent chains' lossy scale below which the raise
    /// writes no <c>localScale</c> at all. A tooltip normally moves between two rects of the very
    /// same basis (both are plain uGUI layout inside one window), so the expected value is exactly
    /// 1 and the expected number of scale writes is zero; the compensation exists only so that a
    /// window which DOES scale its list cannot resize the box by moving it.</summary>
    private const float RaiseScaleEpsilon = 0.001f;

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

    /// <summary>
    /// A local tooltip this class RAISED to the last child of its owning window's content root
    /// (<see cref="RaiseToWindowTop"/>) — everything needed to put it back EXACTLY where the game
    /// had it. Same restore discipline as <c>MapRoom.MapTravelConfirm</c>, for the same reason:
    /// the game moves its own UI, and taking something back from wherever it has since put it is a
    /// write war this project has already paid for once.
    ///
    /// <para><see cref="OriginalLocalScale"/> is the only recorded value that can survive the
    /// game's own <c>SetParent(target, worldPositionStays: false)</c>, so it is also the only one
    /// that has to be handed back BEFORE a re-raise — otherwise a scale compensation would compound
    /// once per hover. Anchors/pivot are recorded but never written: changing them would silently
    /// re-aim the game's own <c>anchoredPosition = offset</c> on the NEXT hover, which computes its
    /// placement before this class is ever called.</para>
    /// </summary>
    private struct RaiseRecord
    {
        public RectTransform Rect;
        public ConvertedPanel Owner;
        public Transform? OriginalParent;
        public int OriginalSiblingIndex;
        public Vector2 OriginalAnchorMin;
        public Vector2 OriginalAnchorMax;
        public Vector2 OriginalPivot;
        public Vector2 OriginalAnchoredPosition;
        public Vector3 OriginalLocalScale;
        public bool ScaleWritten;
        public RectTransform RaisedTo;
    }

    private static readonly List<RaiseRecord> Raised = new(8);

    // ---- the family table --------------------------------------------------------------------
    // Matched BY COMPONENT TYPE, never by name. UILocalTooltip is listed as the base on purpose:
    // GetComponentsInChildren<UILocalTooltip> returns UIItemLocalTooltip and UIQuestEnemyStatsPopup
    // too, so the three of them are one entry and a fourth subclass would need no change here.
    private static readonly List<UILocalTooltip> LocalScratch = new(8);
    private static readonly List<UIPartyItemInventoryTooltip> PartyItemScratch = new(8);
    private static readonly List<UITempleSlotTooltip> TempleScratch = new(8);
    private static readonly List<TooltipUI> ButtonScratch = new(16);

    // ModBuild 194: the ability-card hover preview. Listed here for two reasons, both of them the
    // same ones the item-card hint is listed for. (1) The census behind SILENT HOVER must be able to
    // NAME this family, or a card hover that produces nothing reads as "this window contains no
    // tooltip widget of any known kind" — which would be a false verdict, not a missing one. (2) The
    // per-frame Flatten: the loadout screen spawns its rows with
    // ObjectPool.SpawnCard(..., resetLocalRotation: false, ...)
    // (UIPartyCharacterAbilityCardsDisplay.cs:279), which is the EXACT defect the class doc derives
    // for the merchant's pooled item card — a recycled card that was last used somewhere that
    // rotated it arrives still rotated, which is a styling nudge under a perspective UI camera and
    // literal 3D on a world-space host. Only the hovered card is ever active, so this costs one
    // subtree per frame at most.
    private static readonly List<FullAbilityCard> FullCardScratch = new(24);

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

    /// <summary>Rect names whose raise was DECLINED because the box is anchor-stretched to its
    /// parent (see <see cref="RaiseToWindowTop"/>) — one line each, never per frame.</summary>
    private static readonly HashSet<string> StretchLogged = new();

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
        {
            // STAND-DOWN: conversion switched off (VR off, hot reload, a scene without floated
            // windows). Every raised box goes home before this class stops running, because a
            // reversible move that is never reversed is not reversible.
            if (Raised.Count > 0)
                ReleaseAllRaises("conversion stood down");
            return;
        }

        PruneFlattened();
        TickRaises();
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
        // Then RAISE, and only then clamp — the clamp writes through the widget's own parent
        // basis, so it must be the last of the three and it must see the FINAL parent.
        string raiseNote = RaiseToWindowTop(rect, owner);

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
        VRLog.Info(Scope, DrawOrderEvidence(rect, owner, raiseNote));
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
    /// "IN FRONT OF ITS HOST" IS A DRAW-ORDER QUESTION, AND THE PREVIOUS BUILD'S VERSION OF THIS
    /// LINE ANSWERED IT ONE BUILD BEFORE IT WAS ASKED — <i>"the box hangs at sibling 6 of 6 under
    /// 'UI Shop Item Slot Variant(Clone)' … with overrideSorting false and no canvas of its own the
    /// box draws in HIERARCHY order, so any later sibling of its parent paints over it"</i>. That
    /// is why this line exists at all, and why it is now written to be read AFTER the raise rather
    /// than as a prediction. Geometry still cannot decide the question: the widget is a real CHILD
    /// of the window's host, coplanar with it by construction, and pushing it proud would fight the
    /// flatten that keeps it on the plane. What decides it is uGUI's own rule — a nested canvas with
    /// <c>overrideSorting</c> draws at its own <c>sortingOrder</c> (which <c>CanvasConversion</c>'s
    /// adoption owns: it clears the flag, and concedes ownership of the NUMBER when a game writer
    /// keeps flipping it back), and a widget without one draws in HIERARCHY order, after its earlier
    /// siblings and before its later ones.
    ///
    /// <para>So the numbers printed here are the ones that decide it, all measured on the box's
    /// FINAL hierarchy position: where it ended up, what it draws at, what the host draws at, how
    /// many siblings still paint after it, and how many clippers still cut it. Once per kind — a
    /// held hover must not allocate a line per frame — and phrased so that every outcome, including
    /// the one that says this fix was the wrong one, has a written reading.</para>
    /// </summary>
    private static string DrawOrderEvidence(RectTransform rect, ConvertedPanel owner, string raiseNote)
    {
        Canvas own = rect.GetComponent<Canvas>();
        Canvas nested = own != null ? own : rect.GetComponentInParent<Canvas>();
        Transform? parent = rect.parent;
        int index = parent != null ? rect.GetSiblingIndex() : -1;
        int siblings = parent != null ? parent.childCount : 0;
        int hostOrder = owner.HostCanvas != null ? owner.HostCanvas.sortingOrder : 0;
        bool overriding = nested != null && nested.overrideSorting;
        int drawOrder = overriding ? nested!.sortingOrder : hostOrder;
        int painters = CountLaterPainters(rect, owner);
        string clippers = DescribeClippers(rect, owner);
        return "LOCAL TOOLTIP draw order (measured AFTER the raise, not assumed): the box hangs at "
               + $"sibling {index + 1} of {siblings} under '{(parent != null ? parent.name : "<none>")}'; "
               + $"{raiseNote} It draws at order {drawOrder} "
               + (overriding
                   ? $"(its own canvas '{nested!.name}' has overrideSorting TRUE, so that number IS its order)"
                   : $"(inherited from the host — nearest canvas '{(nested != null ? nested.name : "<none>")}' "
                     + "has overrideSorting false, so HIERARCHY order decides)")
               + $", host canvas order {hostOrder}. "
               + $"STILL PAINTING AFTER IT inside this window's content: {painters} sibling(s) on its "
               + $"whole ancestor chain up to the window root. CLIPPERS it is still a descendant of "
               + $"(enabled RectMask2D / stencil Mask between it and the host, named): {clippers}. "
               + "READ IT LIKE THIS, AND IT ANSWERS THE WHOLE QUESTION WITHOUT GUESSING: 0 later "
               + "painters + 0 clippers + order at or above the host order = the box is genuinely on "
               + "top of its window and anything still invisible is NOT a draw-order or clipping "
               + "problem (look at activity, alpha or the raise being declined). A NON-ZERO painter "
               + "count means the raise did not reach the top level — the box is still nested inside "
               + "window content and that content paints over it, which is the ModBuild-190 failure "
               + "verbatim. A NON-ZERO clipper count naming a scroll VIEWPORT means the box is still "
               + "cut to that viewport, which for a hint drawn BESIDE its row means cut away "
               + "entirely; a count naming only the window root itself is benign (that clip is the "
               + "window frame, and the in-plane clamp already keeps the box inside it). An "
               + "overrideSorting-TRUE canvas whose order is BELOW the host order is the one outcome "
               + "this class cannot fix by reparenting — a canvas that overrides sorting ignores "
               + "hierarchy — and that one does belong to the nested-canvas adoption "
               + "(CanvasConversion.2.Adopt.cs / ReassertAdoptedSorting's conceded branch).";
    }

    /// <summary>
    /// How many siblings still paint AFTER this rect, counted at every level of its ancestor chain
    /// up to (and excluding) the window's own content root. This is the number that decides
    /// hierarchy-order visibility: uGUI paints a canvas's content depth-first in sibling order, so
    /// every later sibling of every ancestor draws over the whole subtree the rect lives in. The
    /// walk stops at <c>owner.Target</c> because above that the only siblings are the mod's own
    /// decorations (the close X, the grab bar), which carry their own overriding canvases and are
    /// deliberately above everything.
    /// </summary>
    private static int CountLaterPainters(RectTransform rect, ConvertedPanel owner)
    {
        int later = 0;
        Transform? stop = owner.Target;
        Transform? node = rect;
        // Bounded by the hierarchy depth; the stop is an ancestor by construction (Settle only
        // runs for rects inside the owning window), and the null test covers the case where it
        // is not, so this cannot walk the whole scene.
        while (node != null && node.parent != null && !ReferenceEquals(node, stop))
        {
            Transform p = node.parent;
            later += p.childCount - 1 - node.GetSiblingIndex();
            node = p;
        }
        return later;
    }

    /// <summary>
    /// How many enabled clippers the rect is still a DESCENDANT of, between it and the host. uGUI
    /// resolves a graphic's clipper by walking its parents and stopping at the first canvas with
    /// <c>overrideSorting</c> (<c>MaskUtilities.GetRectMaskForClippable</c> and, for stencil masks,
    /// <c>GetStencilDepth</c> with <c>FindRootSortOverrideCanvas</c> as its stop) — which is
    /// exactly the flag the conversion's nested-canvas adoption clears. So on a floated window this
    /// count is the honest one: whatever is listed here WILL cut the box.
    /// </summary>
    private static string DescribeClippers(RectTransform rect, ConvertedPanel owner)
    {
        int clippers = 0;
        var names = new List<string>(4);
        Transform? stop = owner.HostRect;
        Transform? node = rect.parent;
        while (node != null)
        {
            var rectMask = node.GetComponent<RectMask2D>();
            bool clips = rectMask != null && rectMask.enabled;
            var stencil = node.GetComponent<Mask>();
            clips |= stencil != null && stencil.enabled && stencil.graphic != null && stencil.graphic.enabled;
            if (clips)
            {
                clippers++;
                if (names.Count < 4)
                    names.Add(node.name);
            }
            if (ReferenceEquals(node, stop))
                break;
            node = node.parent;
        }
        return clippers == 0
            ? "0 (nothing between it and the host clips it)"
            : $"{clippers} [{string.Join(", ", names)}]";
    }

    // ---- the raise ----------------------------------------------------------------------------

    /// <summary>
    /// MOVE ONE PLACED BOX TO THE LAST CHILD OF ITS WINDOW'S CONTENT ROOT, carrying its world pose
    /// across unchanged. The full root cause is in the class doc; the short version is that the
    /// widget's own canvas ships at <c>overrideSorting = true</c> for two reasons at once — to draw
    /// over the list it is parented into, and to escape that list's viewport clipper — and the
    /// conversion's generic nested-canvas adoption clears the flag. Both of those are hierarchy
    /// facts as well as sorting facts, so ONE hierarchy write answers both without touching a flag
    /// this mod has already lost a write war over.
    ///
    /// <para>WHAT IS AND IS NOT WRITTEN. Parent, sibling index and local position: yes. Local
    /// scale: only when the two parent chains actually differ in lossy scale by more than
    /// <see cref="RaiseScaleEpsilon"/> (they should not — expect zero such writes), so that moving
    /// the box can never resize it. Anchors and pivot: NEVER, and that is load-bearing. The game
    /// places this family with <c>SetParent(slot, worldPositionStays: false)</c> followed by
    /// <c>anchoredPosition = offset</c> (UIPartyItemInventoryTooltip.cs:189/240) — both of which run
    /// BEFORE the postfix that calls this method — so re-anchoring the box here would silently
    /// re-aim the game's own placement on the NEXT hover, and the position that finally works would
    /// be lost to fix a draw order.</para>
    ///
    /// <para>Returns the clause the evidence line reads out, so the next hardware log states what
    /// was done rather than what was intended.</para>
    /// </summary>
    private static string RaiseToWindowTop(RectTransform rect, ConvertedPanel owner)
    {
        RectTransform content = owner.Target;
        if (content == null || ReferenceEquals(rect, content) || !rect.IsChildOf(content))
        {
            // Not this window's content (or IS its root) — there is no "top" to move it to and
            // nothing here applies. Vanilla hierarchy, untouched.
            return "no raise (the box is not inside this window's content root).";
        }

        int held = IndexOfRaise(rect);
        if (held >= 0)
        {
            RaiseRecord rec = Raised[held];
            if (rec.RaisedTo != null && ReferenceEquals(rect.parent, rec.RaisedTo))
            {
                KeepLastSibling(rect);
                return "already raised to the window's content root (still ours; only the sibling "
                       + "index is re-asserted).";
            }
            // The game re-parented it — every hover does. Hand back the ONE write of ours that
            // survives a SetParent before measuring a fresh home, so a scale compensation can
            // never compound across hovers, and drop the stale record.
            if (rec.ScaleWritten)
                rect.localScale = rec.OriginalLocalScale;
            Raised.RemoveAt(held);
        }

        if (ReferenceEquals(rect.parent, content))
        {
            // The game already put it at the top level. Nothing to reparent — but "last" still has
            // to be asserted, and it is recorded so the sibling index is handed back on release.
            RecordRaise(rect, owner, content, scaleWritten: false);
            KeepLastSibling(rect);
            return "no reparent needed (the game had already parented it to the window's content "
                   + "root); raised to LAST sibling there.";
        }

        // A stretched rect derives its SIZE from its parent, so reparenting one silently resizes
        // it. Declining is the honest answer: a mis-sized hint is worse than a hidden one, and the
        // line below names the widget so the next round can decide deliberately.
        if (rect.anchorMin != rect.anchorMax)
        {
            if (StretchLogged.Add(rect.name))
            {
                VRLog.Warn(Scope,
                    $"LOCAL TOOLTIP RAISE DECLINED for '{rect.name}' in "
                    + $"'{(owner.HostGo != null ? owner.HostGo.name : "?")}': the box is anchor-STRETCHED "
                    + $"to its parent (anchorMin {rect.anchorMin}, anchorMax {rect.anchorMax}), so its "
                    + "size is derived from that parent and re-parenting it would resize it. "
                    + "CONSEQUENCE: this hint keeps drawing in hierarchy order inside its row and may "
                    + "be painted over or clipped by the row list — i.e. the ModBuild-190 symptom "
                    + "persists for THIS widget only. Every non-stretched hint is raised normally.");
            }
            return "raise DECLINED (anchor-stretched to its parent — see the warning above).";
        }

        Vector3 worldPos = rect.position;
        Vector3 preLossy = rect.lossyScale;
        Transform? previous = rect.parent;
        RecordRaise(rect, owner, content, scaleWritten: false);

        rect.SetParent(content, worldPositionStays: false);
        Vector3 postLossy = rect.lossyScale;
        Vector3 ratio = new Vector3(
            SafeRatio(preLossy.x, postLossy.x),
            SafeRatio(preLossy.y, postLossy.y),
            SafeRatio(preLossy.z, postLossy.z));
        bool scaled = Mathf.Abs(ratio.x - 1f) > RaiseScaleEpsilon
                      || Mathf.Abs(ratio.y - 1f) > RaiseScaleEpsilon
                      || Mathf.Abs(ratio.z - 1f) > RaiseScaleEpsilon;
        if (scaled)
        {
            Vector3 ls = rect.localScale;
            rect.localScale = new Vector3(ls.x * ratio.x, ls.y * ratio.y, ls.z * ratio.z);
            MarkScaleWritten(rect);
        }

        // The pose is carried across as a WORLD point resolved into the new parent's basis — never
        // as transform.position, which on a rotated host at map-room rig scale is the mistake this
        // whole class exists to undo. z is dropped: the correction is in-plane by construction and
        // the flatten owns z.
        Vector3 local = content.InverseTransformPoint(worldPos);
        rect.localPosition = new Vector3(local.x, local.y, 0f);
        rect.SetAsLastSibling();

        return $"RAISED from '{(previous != null ? previous.name : "<none>")}' to the LAST child of "
               + $"the window's content root '{content.name}' (world pose carried across verbatim"
               + (scaled ? $", local scale compensated by {ratio}" : ", no scale write needed")
               + ").";
    }

    // ---- the ability-card hover preview (the third family) -------------------------------------

    /// <summary>Kinds whose "the game's world write was replaced" line was already logged.</summary>
    private static readonly HashSet<string> ReplacedLogged = new();

    /// <summary>Reasons a raise was released whose line was already logged (a release happens on
    /// EVERY un-hover, so the steady state must be one HashSet lookup and nothing else).</summary>
    private static readonly HashSet<string> ReleasedLogged = new();

    /// <summary>
    /// PLACE ONE ABILITY-CARD HOVER PREVIEW ON ITS WINDOW, REPLACING the game's world-space
    /// assignment rather than correcting it afterwards. Full derivation in the class doc; the short
    /// version is that <c>AbilityCardUI.ChangeFullCardPosition</c> writes
    /// <c>fullAbilityCard.transform.position = new Vector3(row.position.x + 40f, row.position.y - 45f)</c>
    /// (AbilityCardUI.cs:1074) — pixel-sized constants applied along the WORLD axes, with an implicit
    /// <c>z = 0</c>, which on a yawed host at ~0.17 m per uGUI pixel puts the card tens of metres off
    /// the window's plane.
    ///
    /// <para>WHAT IS REBUILT AND IN WHICH BASIS. The game's INTENT is unambiguous and worth keeping:
    /// "put the big card <paramref name="offsetXPixels"/> right and <paramref name="offsetYPixels"/>
    /// down of the row's pivot, in canvas pixels". The only thing wrong with it is the basis, so the
    /// same offset is walked along the HOST'S right/up axes scaled by the host's own metres-per-pixel
    /// (<c>host.lossyScale.x</c> — the very number the placed-log prints as <c>m/px</c>), and the
    /// resulting world point is resolved through the widget's CURRENT parent before it is written as
    /// a <c>localPosition</c>. Never <c>transform.position</c>: writing a world position on a child
    /// of a rotated, 198x-scaled host is the mistake this whole class exists to undo, and resolving
    /// through the current parent is also what makes this correct on the SECOND hover, when
    /// <see cref="RaiseToWindowTop"/> has already moved the widget to the window's content root.</para>
    ///
    /// <para>Returns TRUE when this class took the placement over — the caller (a Harmony PREFIX)
    /// then skips the game's method entirely, so the broken write never lands even for one frame. It
    /// returns FALSE for every card that is not inside a floated window (the scenario hand fan, the
    /// flat screen, VR off), and those run the game's arithmetic byte-identically.</para>
    /// </summary>
    internal static bool PlaceFullCardPreview(Component? row, Component? fullCard,
        float offsetXPixels, float offsetYPixels, string kind)
    {
        if (!WorldUIConfig.ConversionActive || row == null || fullCard == null)
            return false;
        if (fullCard.transform is not RectTransform rect)
            return false;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(rect);
        if (owner == null || !owner.IsAlive || owner.HostRect == null)
            return false; // not on a floated window — vanilla arithmetic, untouched

        RectTransform host = owner.HostRect;
        Quaternion hostRot = host.rotation;
        // Metres per uGUI pixel on this host. x is read on purpose (the conversion scales the host
        // uniformly; the placed-log prints this same component as the window's "m/px").
        float metresPerPixel = host.lossyScale.x;
        Vector3 want = row.transform.position
                       + hostRot * Vector3.right * (offsetXPixels * metresPerPixel)
                       + hostRot * Vector3.up * (offsetYPixels * metresPerPixel);
        Transform? parent = rect.parent;
        Vector3 local = parent != null ? parent.InverseTransformPoint(want) : want;
        // z is dropped: the offset is in-plane by construction and Flatten (inside Settle) owns z.
        rect.localPosition = new Vector3(local.x, local.y, 0f);

        if (ReplacedLogged.Add(kind))
        {
            VRLog.Info(Scope,
                $"FULL-CARD PLACEMENT REPLACED for '{kind}' on the floated window "
                + $"'{(owner.HostGo != null ? owner.HostGo.name : "?")}': the game assigns an ABSOLUTE "
                + "WORLD position here (AbilityCardUI.cs:1074, "
                + "'fullAbilityCard.transform.position = new Vector3(row.position.x + "
                + $"{offsetXPixels:F0}f, row.position.y - {-offsetYPixels:F0}f)') — two CANVAS-PIXEL "
                + "constants walked along the WORLD x/y axes, and a two-argument Vector3 carries an "
                + "implicit z = 0, so the card is also snapped to world z = 0. Correct on a "
                + "screen-space canvas, meaningless on a host at "
                + $"{metresPerPixel:F5} m per uGUI pixel whose plane is yawed away from world x/y. "
                + "The SAME offset is now walked along the HOST'S right/up axes at that scale and "
                + "written as a localPosition through the widget's current parent, so the game's own "
                + "intent survives in the only basis where it means anything. This is a DIFFERENT "
                + "term from the ModBuild-190 screen-fit cut: that one zeroes the delta ADDED after "
                + "this line (and still does, see SCREEN-FIT CUT), this one replaces the assignment "
                + "itself. Cards outside every floated window keep the game's arithmetic exactly.");
        }

        Settle(fullCard, kind);
        return true;
    }

    /// <summary>
    /// HAND ONE RAISED WIDGET BACK, on the game's own hide edge. Restores parent, sibling index,
    /// anchors, pivot, anchored position and local scale — and, per <see cref="RestoreRaise"/>, ONLY
    /// while the widget is still parented where this class put it; a widget the game has since moved
    /// is the game's again.
    ///
    /// <para>This exists because the ability-card preview is not a tooltip: its owner is a POOLED
    /// row, and <c>ObjectPool.RecycleCard</c> recycles the row rather than the preview. Leaving the
    /// preview parented to the window's content root would strand it there when the loadout screen
    /// repopulates. A no-op when the widget was never raised, so it is safe to call on every hide.</para>
    /// </summary>
    internal static void ReleaseRaiseOf(Component? widget, string why)
    {
        if (widget == null || widget.transform is not RectTransform rect)
            return;
        int i = IndexOfRaise(rect);
        if (i < 0)
            return;
        RestoreRaise(Raised[i]);
        Raised.RemoveAt(i);
        if (!ReleasedLogged.Add(why))
            return;
        VRLog.Info(Scope,
            $"LOCAL TOOLTIP RAISE released for '{rect.name}' ({why}) — parent, sibling index, "
            + "anchors, pivot, anchored position and local scale restored verbatim, and only because "
            + "the widget was still parented where this class put it. READ IT LIKE THIS: this line "
            + "appearing means the hide edge is wired and a pooled row can be recycled without "
            + "stranding its preview under the window root; its ABSENCE after a session of hovering "
            + "means the release never ran and the pool's own defensive re-parent "
            + "(ObjectPool.cs:545-547) is the only thing keeping the hierarchy sane. Logged once per "
            + "reason.");
    }

    /// <summary>Ratio of two lossy-scale components, with a zero denominator answering 1 (a
    /// degenerate basis must not produce an infinity that then lands on a transform).</summary>
    private static float SafeRatio(float pre, float post) =>
        Mathf.Abs(post) < 1e-6f ? 1f : pre / post;

    private static void RecordRaise(RectTransform rect, ConvertedPanel owner, RectTransform raisedTo,
        bool scaleWritten)
    {
        Raised.Add(new RaiseRecord
        {
            Rect = rect,
            Owner = owner,
            OriginalParent = rect.parent,
            OriginalSiblingIndex = rect.GetSiblingIndex(),
            OriginalAnchorMin = rect.anchorMin,
            OriginalAnchorMax = rect.anchorMax,
            OriginalPivot = rect.pivot,
            OriginalAnchoredPosition = rect.anchoredPosition,
            OriginalLocalScale = rect.localScale,
            ScaleWritten = scaleWritten,
            RaisedTo = raisedTo,
        });
    }

    private static void MarkScaleWritten(RectTransform rect)
    {
        int i = IndexOfRaise(rect);
        if (i < 0)
            return;
        RaiseRecord rec = Raised[i];
        rec.ScaleWritten = true;
        Raised[i] = rec;
    }

    private static int IndexOfRaise(RectTransform rect)
    {
        for (int i = 0; i < Raised.Count; i++)
        {
            if (ReferenceEquals(Raised[i].Rect, rect))
                return i;
        }
        return -1;
    }

    /// <summary>Assert "last sibling" only when it is not already true. Idempotent and single-
    /// writer: nothing else in this mod writes a tooltip's sibling index, so this can never become
    /// the alternating write the sorting flag once was.</summary>
    private static void KeepLastSibling(RectTransform rect)
    {
        Transform? parent = rect.parent;
        if (parent == null)
            return;
        if (rect.GetSiblingIndex() != parent.childCount - 1)
            rect.SetAsLastSibling();
    }

    /// <summary>
    /// Per-frame upkeep of the raised set: drop records whose widget or window died (restoring
    /// first, while it is still ours), and re-assert "last sibling" for the ones still raised — a
    /// window that spawns content while a hint is up would otherwise paint over it.
    /// </summary>
    private static void TickRaises()
    {
        for (int i = Raised.Count - 1; i >= 0; i--)
        {
            RaiseRecord rec = Raised[i];
            if (rec.Rect == null || rec.RaisedTo == null || rec.Owner == null || !rec.Owner.IsAlive)
            {
                RestoreRaise(rec); // no-ops unless the box is still parented where we put it
                Raised.RemoveAt(i);
                continue;
            }
            if (!ReferenceEquals(rec.Rect.parent, rec.RaisedTo))
                continue; // the game took it back; the next placement call re-takes it cleanly
            KeepLastSibling(rec.Rect);
        }
    }

    /// <summary>
    /// Put ONE raised box back exactly where the game had it — parent, sibling index, anchors,
    /// pivot, anchored position and local scale — and ONLY while it is still parented where this
    /// class put it. A box the game has since moved is the game's again; taking it back would be
    /// the write war.
    /// </summary>
    private static void RestoreRaise(RaiseRecord rec)
    {
        RectTransform rect = rec.Rect;
        if (rect == null || rec.RaisedTo == null || !ReferenceEquals(rect.parent, rec.RaisedTo))
            return;
        if (rec.ScaleWritten)
            rect.localScale = rec.OriginalLocalScale;
        if (rec.OriginalParent == null)
            return; // its home was destroyed (a pooled row) — leave it flat inside the window
        rect.SetParent(rec.OriginalParent, worldPositionStays: false);
        rect.SetSiblingIndex(Mathf.Clamp(rec.OriginalSiblingIndex, 0,
            Mathf.Max(0, rec.OriginalParent.childCount - 1)));
        rect.anchorMin = rec.OriginalAnchorMin;
        rect.anchorMax = rec.OriginalAnchorMax;
        rect.pivot = rec.OriginalPivot;
        rect.anchoredPosition = rec.OriginalAnchoredPosition;
    }

    private static void ReleaseAllRaises(string why)
    {
        int restored = Raised.Count;
        for (int i = 0; i < Raised.Count; i++)
            RestoreRaise(Raised[i]);
        Raised.Clear();
        if (restored > 0)
        {
            VRLog.Info(Scope,
                $"LOCAL TOOLTIP RAISE released for {restored} widget(s) ({why}) — parent, sibling "
                + "index, anchors, pivot, anchored position and local scale restored verbatim for "
                + "every box still parented where this class put it; anything the game had already "
                + "moved was left alone on purpose.");
        }
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
        ReleaseAllRaises("module shutdown / hot reload");
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
        FullCardScratch.Clear();
        PlacedLogged.Clear();
        SilentLogged.Clear();
        CutLogged.Clear();
        StretchLogged.Clear();
        ReplacedLogged.Clear();
        ReleasedLogged.Clear();
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

            host.GetComponentsInChildren(includeInactive: true, FullCardScratch);
            for (int k = 0; k < FullCardScratch.Count; k++)
                Add(FullCardScratch[k], "FullAbilityCard (ability-card hover preview)");
        }
        LocalScratch.Clear();
        PartyItemScratch.Clear();
        TempleScratch.Clear();
        ButtonScratch.Clear();
        FullCardScratch.Clear();
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
