using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// THE SEAMS FOR THE SECOND TOOLTIP FAMILY — the game's own placement calls for every "local"
/// tooltip, i.e. the ones that never touch <c>CanvasManager.tooltipCanvas</c> and therefore were
/// invisible to <see cref="WorldTooltips"/>. The whole diagnosis, the family list and the reason
/// each of these three groups exists live in <see cref="TooltipOnWindow"/>; this file is only the
/// wiring, and every patch here is a no-op unless the rect involved is inside a floated window
/// (<c>ModalFallback.FindOwningWindow</c> — ownership, never topmost-ness).
///
/// <list type="bullet">
/// <item><description>THE CUT (<see cref="CutWorldFitCameraMargin"/> and friends). The game ends
/// every local-tooltip placement with <c>transform.position += rect.DeltaWorldPositionToFitTheScreen
/// (UIManager.Instance.UICamera, margin)</c>. That helper resolves both of its screen bounds through
/// <c>camera.ScreenToWorldPoint(Vector2)</c>, whose implicit <c>z = 0</c> is ZERO DISTANCE FROM THE
/// CAMERA (RectTransformExtensions.cs:130-131), so what it returns is not "the delta to fit the
/// screen" but "the delta that drags this corner onto the UI camera" — expressed in world X/Y, on a
/// rect that hangs under a rotated host at the map room's ~198x rig scale, measured against the map
/// camera the mod freezes. Prefixed to return <c>Vector3.zero</c> for exactly those rects. The rect
/// is already a CHILD of the window's host, so the game's remaining placement terms
/// (<c>SetParent(target, worldPositionStays: false)</c> + <c>anchoredPosition</c>) put it on the
/// window's plane, at the window's scale and rotation, all by themselves.</description></item>
/// <item><description>THE SETTLE (<see cref="SettleLocal"/> and friends). Postfixes on the game's
/// OWN placement methods, so the mod's in-plane clamp is the last write of that call by
/// construction. It is deliberately NOT done from a LateUpdate step: the game writes the same x/y
/// from its own LateUpdate, and two writers of one number in two unordered LateUpdates is the
/// alternating value that makes the two eyes disagree in MultiPass. Since the 2026-08-21
/// visibility round the same postfix also RAISES the box to the last child of the window's own content root
/// (<c>TooltipOnWindow.RaiseToWindowTop</c>) — the ModBuild-190 hardware log proved a correctly
/// placed box invisible underneath the very item list it hangs inside, and the seam has to be here
/// for the same reason the clamp is: the game re-parents the box on every hover
/// (UIPartyItemInventoryTooltip.cs:189), so the only moment "where it ended up" is knowable is
/// immediately after the game's own placement returned.</description></item>
/// <item><description>THE ATTRIBUTION (<see cref="NoteShopHover"/> and friends). Every hover on a
/// tooltip-bearing SLOT is reported, so a hover that raises nothing at all becomes one logged
/// verdict with a census instead of silence.</description></item>
/// <item><description>THE REPLACEMENT (<see cref="PlaceAbilityCardPreview"/>, ModBuild 194). The
/// ability-card loadout screen's hover preview is not a tooltip at all — it is the card's own
/// <c>FullAbilityCard</c> child — and its broken term is an ABSOLUTE world-position ASSIGNMENT
/// rather than an added helper, so there is nothing to return zero for. That one method is skipped
/// for cards on a floated window and the game's own pixel offset is rebuilt in the window's basis
/// instead. Two companions go with it: the RELEASE on the hide edge (the owner is a pooled row) and
/// the same ATTRIBUTION as above.</description></item>
/// </list>
///
/// <para>REVERSIBLE / VANILLA-SAFE: every entry point returns immediately unless
/// <see cref="WorldUIConfig.ConversionActive"/> AND the rect resolves to a floated window, so with
/// VR off — and on every screen-space surface, the flat screen included — the game's arithmetic runs
/// byte-identically. Nothing is mutated on the way out: the cut simply returns a different number,
/// and the flatten and the raise are both recorded and handed back by
/// <c>TooltipOnWindow.Shutdown</c> (parent, sibling index, anchors, pivot, anchored position,
/// local scale), on stand-down and when the owning window dies.</para>
///
/// <para>LOCAL DISPLAY ONLY — MULTIPLAYER IS UNAFFECTED. Nothing here reads or writes game state:
/// the cut changes a return value used for one widget's on-screen position, and the raise changes
/// one widget's parent inside one player's own converted window. No rule library, no Bolt/FFSNet
/// call, nothing on the wire, and no observable difference for any other player — a remote client
/// runs its own tooltips through its own copy of this code or, with VR off, not at all.</para>
/// </summary>
[HarmonyPatch]
internal static class TooltipWindowPatches
{
    // ---- THE CUT ------------------------------------------------------------------------------

    /// <summary>
    /// <c>DeltaWorldPositionToFitTheScreen(this RectTransform, Camera, float margin)</c> — the
    /// overload every local tooltip uses (UIPartyItemInventoryTooltip.cs:96/241,
    /// UILocalTooltip.cs:91, UITempleSlotTooltip.cs:80/114,
    /// UIPartyCharacterEquipmentDisplay.cs:634).
    ///
    /// <para><c>UITooltip</c> calls it too (UITooltip.cs:445/456), and deliberately still gets its
    /// real answer: the shared tooltip canvas is NOT re-parented under a host, so
    /// <c>FindOwningWindow</c> returns null for it and <see cref="WorldTooltips"/> keeps absorbing
    /// that write by frame-pinning, exactly as it does today.</para>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RectTransformExtensions),
        nameof(RectTransformExtensions.DeltaWorldPositionToFitTheScreen),
        new[] { typeof(RectTransform), typeof(Camera), typeof(float) })]
    private static bool CutWorldFitCameraMargin(RectTransform rectTransform, ref Vector3 __result) =>
        !Cut(rectTransform, "DeltaWorldPositionToFitTheScreen(camera, margin)", ref __result);

    /// <summary>The two-margin overload (AbilityCardUI.cs:1075, UILevelUpCard.cs:122) — same
    /// arithmetic, same failure on a floated window.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RectTransformExtensions),
        nameof(RectTransformExtensions.DeltaWorldPositionToFitTheScreen),
        new[] { typeof(RectTransform), typeof(Camera), typeof(float), typeof(float) })]
    private static bool CutWorldFitCameraMarginXY(RectTransform rectTransform, ref Vector3 __result) =>
        !Cut(rectTransform, "DeltaWorldPositionToFitTheScreen(camera, marginX, marginY)", ref __result);

    /// <summary>The screen-space twin used by <c>TooltipUI.ToggleEnable</c> (TooltipUI.cs:36) and
    /// <c>CardHandHeader</c>: it projects the rect's WORLD corners through the same camera and
    /// returns a delta in the same broken units.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RectTransformExtensions),
        nameof(RectTransformExtensions.DeltaPositionToFitTheScreen),
        new[] { typeof(RectTransform), typeof(Camera), typeof(float) })]
    private static bool CutFitCameraMargin(RectTransform rectTransform, ref Vector3 __result) =>
        !Cut(rectTransform, "DeltaPositionToFitTheScreen(camera, margin)", ref __result);

    /// <summary>The camera-less overload, which compares WORLD corners against
    /// <c>Screen.width/height</c> directly (RectTransformExtensions.cs:26-47) — i.e. metres against
    /// pixels. Included for completeness: on a floated window it can only ever be nonsense.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RectTransformExtensions),
        nameof(RectTransformExtensions.DeltaPositionToFitTheScreen),
        new[] { typeof(RectTransform), typeof(float) })]
    private static bool CutFitMargin(RectTransform rectTransform, ref Vector3 __result) =>
        !Cut(rectTransform, "DeltaPositionToFitTheScreen(margin)", ref __result);

    /// <summary>
    /// True when this rect's screen fit must be neutralized — i.e. VR is converting AND the rect
    /// lives inside a floated window. Sets <paramref name="result"/> to zero in that case. Two
    /// component-free checks plus one parent walk per call; the walk is
    /// <c>ModalFallback.FindOwningWindow</c>, which exits on the first frame there are no converted
    /// windows at all.
    /// </summary>
    private static bool Cut(RectTransform rectTransform, string helper, ref Vector3 result)
    {
        if (!WorldUIConfig.ConversionActive || rectTransform == null)
            return false;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(rectTransform);
        if (owner == null)
            return false; // not on a floated window — vanilla arithmetic, untouched
        TooltipOnWindow.NoteScreenFitCut(helper, rectTransform, owner);
        result = Vector3.zero;
        return true;
    }

    // ---- THE SETTLE ---------------------------------------------------------------------------

    /// <summary>
    /// <c>UILocalTooltip.RefreshPosition()</c> — the base method every local tooltip's placement
    /// funnels through, called from <c>Show()</c> and from its own <c>LateUpdate</c>
    /// (UILocalTooltip.cs:80-93). Patching the BASE covers <c>UIItemLocalTooltip</c> and
    /// <c>UIQuestEnemyStatsPopup</c> too: neither overrides it (they override the virtual
    /// <c>SetPosition</c> it calls), so one patch is the whole subclass tree.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UILocalTooltip), nameof(UILocalTooltip.RefreshPosition))]
    private static void SettleLocal(UILocalTooltip __instance)
    {
        // Gated on the game's OWN IsShown, because RefreshPosition early-returns on a hidden
        // tooltip (UILocalTooltip.cs:87) and a postfix runs on that path too. Settling a hidden box
        // would be invisible but not harmless: it would stamp the silent-hover watch with "something
        // was placed" and hide exactly the failure that watch exists to report.
        if (__instance == null || !__instance.IsShown)
            return;
        TooltipOnWindow.Settle(__instance, __instance.GetType().Name + " (UILocalTooltip)");
    }

    /// <summary>
    /// <c>UIPartyItemInventoryTooltip.RefreshPosition(Vector2)</c> — the merchant / party inventory
    /// / equipment item CARD hint, and the widget the 2026-08-21 report is about.
    /// <c>Build(...)</c> reaches it after it has re-parented itself under the hovered slot and after
    /// the pooled <c>ItemCardUI</c> has been spawned into <c>cardHolder</c>
    /// (UIPartyItemInventoryTooltip.cs:157-209), which is exactly when the box is worth measuring.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyItemInventoryTooltip),
        nameof(UIPartyItemInventoryTooltip.RefreshPosition))]
    private static void SettlePartyItem(UIPartyItemInventoryTooltip __instance) =>
        TooltipOnWindow.Settle(__instance, "UIPartyItemInventoryTooltip (item card hint)");

    /// <summary>
    /// <c>UITempleSlotTooltip.Show(...)</c> — the blessing hint. Its <c>Build</c> is private and
    /// does not route through a shared refresh, so <c>Show</c> is the seam; it runs after the
    /// re-parent and after <c>window.Show()</c> (UITempleSlotTooltip.cs:79-89).
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITempleSlotTooltip), nameof(UITempleSlotTooltip.Show))]
    private static void SettleTemple(UITempleSlotTooltip __instance) =>
        TooltipOnWindow.Settle(__instance, "UITempleSlotTooltip (blessing hint)");

    /// <summary>
    /// <c>TooltipUI.ToggleEnable(bool)</c> — the hint an <c>ExtendedButton</c> carries itself
    /// (<c>tooltip</c> + <c>autoDisplayTooltip</c>). Only the SHOW side is settled: the hide side
    /// deactivates the object, and moving something on its way out is a visible jump for no gain.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TooltipUI), nameof(TooltipUI.ToggleEnable))]
    private static void SettleButtonTooltip(TooltipUI __instance, bool active)
    {
        if (active)
            TooltipOnWindow.Settle(__instance, "TooltipUI (ExtendedButton hint)");
    }

    // ---- THE ATTRIBUTION ----------------------------------------------------------------------

    /// <summary>
    /// <c>UIShopItemSlot.OnHovered(bool)</c> — the merchant's own hover entry point, raised from
    /// its <c>ExtendedButton.onMouseEnter/onMouseExit</c> listeners (UIShopItemSlot.cs:166-173) and
    /// the method that goes on to call <c>itemTooltip.Show(...)</c>. Reported so a hover that
    /// produces no tooltip at all is logged with a reason instead of vanishing.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIShopItemSlot), nameof(UIShopItemSlot.OnHovered))]
    private static void NoteShopHover(UIShopItemSlot __instance, bool hovered) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UIShopItemSlot (merchant item)", hovered);

    /// <summary><c>UIPartyItemSlot.OnHovered(bool)</c> — the party inventory's item slot (private;
    /// the only method of that name on the type).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyItemSlot), nameof(UIPartyItemSlot.OnHovered))]
    private static void NotePartyItemHover(UIPartyItemSlot __instance, bool hovered) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UIPartyItemSlot (party inventory item)", hovered);

    /// <summary><c>UITempleShopSlot.Select()</c> — the temple's blessing slot raises its hover from
    /// here (UITempleShopSlot.cs:355-363).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITempleShopSlot), nameof(UITempleShopSlot.Select))]
    private static void NoteTempleHover(UITempleShopSlot __instance) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UITempleShopSlot (temple blessing)", hovered: true);

    /// <summary><c>UITempleShopSlot.Deselect()</c> — withdraws the pending verdict, so a hover the
    /// player moved off is never reported as a silent failure.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITempleShopSlot), nameof(UITempleShopSlot.Deselect))]
    private static void NoteTempleUnhover(UITempleShopSlot __instance) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UITempleShopSlot (temple blessing)", hovered: false);

    // ---- THE ABILITY-CARD HOVER PREVIEW (the third family) ------------------------------------
    //
    // The whole diagnosis — including which of the two prior fixes reached this widget and which did
    // not, both read off the ModBuild-193 hardware log rather than assumed — lives in
    // TooltipOnWindow's class doc. Three seams, mirroring the three above: the REPLACEMENT (the
    // broken term here is an assignment, so it is skipped and rebuilt rather than zeroed), the
    // RELEASE (the owner is a pooled row, so the raise must be handed back on the hide edge), and
    // the ATTRIBUTION (a card hover that shows nothing must be a logged verdict, not silence).

    /// <summary>The game's own offset for the big card, in CANVAS PIXELS, read verbatim off
    /// <c>AbilityCardUI.ChangeFullCardPosition</c> (AbilityCardUI.cs:1074:
    /// <c>new Vector3(base.transform.position.x + 40f, base.transform.position.y - 45f)</c>). Kept as
    /// named constants rather than folded into the mod's own numbers, because the INTENT is the
    /// game's and only the BASIS was wrong: if a game update moves the preview, these two values are
    /// the single place that has to follow.</summary>
    private const float FullCardOffsetXPixels = 40f;

    /// <summary>See <see cref="FullCardOffsetXPixels"/>. Negative: the game subtracts 45.</summary>
    private const float FullCardOffsetYPixels = -45f;

    /// <summary>
    /// <c>AbilityCardUI.ChangeFullCardPosition()</c> — the ability-card loadout screen's hover
    /// preview, and the widget the 2026-08-21 card-menu report is about. PREFIX, and it SKIPS the
    /// original for cards on a floated window: the method's first act is an ABSOLUTE world-position
    /// assignment (AbilityCardUI.cs:1074), so unlike the screen-fit helpers there is no delta to
    /// return zero for — correcting it in a postfix would still let the wrong pose land for a frame,
    /// and on a 90 Hz stereo display a one-frame teleport of a card-sized object is visible.
    ///
    /// <para>Returning <c>true</c> (run the original) is the default and covers every card that is
    /// NOT inside a floated window — the scenario hand fan, the flat screen, VR off — so
    /// <c>CardsHandUI</c> and every other <c>AbilityCardUI</c> consumer keeps byte-identical
    /// behaviour. The ownership test is <c>ModalFallback.FindOwningWindow</c> inside
    /// <c>PlaceFullCardPreview</c>, the same predicate every other patch in this file is gated on.</para>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.ChangeFullCardPosition))]
    private static bool PlaceAbilityCardPreview(AbilityCardUI __instance)
    {
        if (__instance == null || __instance.fullAbilityCard == null)
            return true;
        return !TooltipOnWindow.PlaceFullCardPreview(
            __instance, __instance.fullAbilityCard,
            FullCardOffsetXPixels, FullCardOffsetYPixels,
            "FullAbilityCard (ability-card hover preview)");
    }

    /// <summary>
    /// <c>AbilityCardUI.ToggleFullCardPreview(bool, Transform)</c> — BOTH the hide edge and the
    /// attribution seam, in one postfix because both want the same moment.
    ///
    /// <para>THE RELEASE (hide edge). Postfix, so it is the last thing that happens on an un-hover,
    /// and it hands the raise back (parent, sibling index, anchors, pivot, anchored position, local
    /// scale) while the widget is still ours. This matters more than for the tooltip families
    /// because the OWNER IS POOLED: <c>ObjectPool.RecycleCard</c> recycles the card ROW, and a
    /// preview left parented to the window's content root would outlive it there. A no-op for a
    /// widget that was never raised, so it costs one list scan per un-hover and nothing else.</para>
    ///
    /// <para>THE ATTRIBUTION, AND WHY IT IS HERE RATHER THAN ON <c>OnPointerEnter</c>. The obvious
    /// seam for "the player hovered a card" is <c>AbilityCardUI.OnPointerEnter</c> (AbilityCardUI.cs:374,
    /// wired from the prefab's <c>ExtendedButton.onMouseEnter</c> UnityEvent). It is the WRONG one
    /// for the silent-hover watch: that method DELIBERATELY declines to preview in several modes
    /// (<c>ActionSelection</c>, <c>Preview</c>, <c>alwaysShowFullCard</c>,
    /// <c>isInFurtherAbilityPanel</c>, AbilityCardUI.cs:376-389), and a hover that was never meant
    /// to show anything would then be reported as a hover that FAILED to show something — a false
    /// verdict, which is worse than no verdict. <c>ToggleFullCardPreview</c> runs only once the game
    /// has decided a preview IS wanted, so a silent-hover warning raised from here always means a
    /// preview was genuinely requested and genuinely did not appear.</para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.ToggleFullCardPreview))]
    private static void ReleaseAbilityCardPreview(AbilityCardUI __instance, bool isHighlighted)
    {
        if (__instance == null)
            return;
        TooltipOnWindow.NoteSlotHover(__instance, "AbilityCardUI (ability-card loadout row)",
            isHighlighted);
        FullAbilityCard full = __instance.fullAbilityCard;
        if (isHighlighted)
        {
            // THE SHOW EDGE, VERIFIED (ModBuild 195). "The overlay must come up reliably, every time"
            // is only checkable if a hover that produced nothing is COUNTED, and it has to be counted
            // apart from a hover that produced something invisible — those two have completely
            // different fixes and the 194 report cannot distinguish them on its own.
            if (full == null || !full.gameObject.activeInHierarchy)
            {
                TooltipOnWindow.NoteShowEdgeWithoutWidget(
                    "AbilityCardUI (ability-card loadout row)",
                    __instance.gameObject.name);
            }
            return;
        }

        TooltipOnWindow.ReleaseRaiseOf(full,
            "the ability-card hover preview was hidden — the pointer left the card row");

        // THE ModBuild-194 LEAK, ENDED HERE. The game just asked Unity to destroy the Canvas it added
        // for this hover; Unity refuses while the GraphicRaycaster this mod's nested-canvas adoption
        // added still depends on it, and a canvas that survives one hide never lets the game configure
        // a fresh one again. Full derivation and the log evidence are on
        // TooltipOnWindow.CompleteCanvasTeardown; it is a no-op whenever the game's own destroy went
        // through, and it never runs on a preview that is still active.
        TooltipOnWindow.CompleteCanvasTeardown(full);
    }

    // ---- the desynced premise -----------------------------------------------------------------

    /// <summary>
    /// Reflected access to <c>AbilityCardUI.previewingFullCard</c> (private). Resolved ONCE and
    /// tolerated as null: a game update that renames the field must degrade to "no repair", never to
    /// an exception on a hover path — an unguarded throw here would starve VR input.
    /// </summary>
    private static readonly AccessTools.FieldRef<AbilityCardUI, bool>? PreviewingFullCardRef =
        ResolvePreviewingFullCardRef();

    private static AccessTools.FieldRef<AbilityCardUI, bool>? ResolvePreviewingFullCardRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<AbilityCardUI, bool>("previewingFullCard");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI",
                "ABILITY-CARD PREVIEW FLAG not reachable (" + ex.GetType().Name + ": " + ex.Message
                + ") — AbilityCardUI.previewingFullCard could not be bound, so the desync repair "
                + "described on TooltipOnWindow is DISABLED for this session. CONSEQUENCE: if that "
                + "flag is ever left set while the preview widget is switched off, that card can no "
                + "longer preview (ToggleFullCardPreview early-returns on it, AbilityCardUI.cs:1037). "
                + "Everything else in this file is unaffected.");
            return null;
        }
    }

    /// <summary>
    /// <c>AbilityCardUI.ToggleFullCardPreview(bool, Transform)</c> — PREFIX, and it corrects ONE
    /// PREMISE rather than taking over the method: it never returns false, so the game's own show/hide
    /// always runs.
    ///
    /// <para>THE METHOD'S FIRST REAL DECISION IS <c>if (previewingFullCard == isHighlighted) {
    /// ChangeFullCardPosition(); return; }</c> (AbilityCardUI.cs:1037-1041) — "I am already in the
    /// state you asked for, so I will only re-position". That is correct as long as the flag is true,
    /// and the flag has a documented way to go stale: <c>ToggleFullCard(active: false)</c> switches the
    /// widget OFF without clearing it (AbilityCardUI.cs:1005-1012), and it is reached from
    /// <c>SetMode</c> (AbilityCardUI.cs:930) and from
    /// <c>UIPartyCharacterAbilityCardsDisplay.HideFullCards</c> (:351-356, itself called from
    /// <c>LevelUpState</c>). A card left in that state is <b>permanently unable to preview</b>: every
    /// hover takes the early return and only moves an object nobody can see. Per card, monotonic, never
    /// recovering — one of exactly two shapes the ModBuild-194 report can have.</para>
    ///
    /// <para>So the flag is compared against the observable truth (<c>activeSelf</c> of the widget the
    /// flag is about) and, on a SHOW request where the two disagree, set to match reality. The game
    /// then takes its own full show path, unmodified. Nothing is bypassed, no state is invented, and —
    /// this being a one-shot correction on a disagreement rather than a value asserted every frame —
    /// it cannot become a write war. Gated on a floated window like every other patch here, so flat
    /// play is byte-identical.</para>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.ToggleFullCardPreview))]
    private static void RepairAbilityCardPreviewFlag(AbilityCardUI __instance, bool isHighlighted)
    {
        if (!isHighlighted || PreviewingFullCardRef == null || !WorldUIConfig.ConversionActive)
            return;
        if (__instance == null || __instance.fullAbilityCard == null)
            return;
        // activeSelf, not activeInHierarchy: the question is what THIS widget's own switch says, and a
        // whole window being hidden must not be read as a desynced card.
        if (__instance.fullAbilityCard.gameObject.activeSelf)
            return;
        if (!PreviewingFullCardRef(__instance))
            return; // the flag already agrees with reality — the game will show it normally
        if (ModalFallback.FindOwningWindow(__instance.transform) == null)
            return; // not on a floated window — vanilla behaviour, untouched
        PreviewingFullCardRef(__instance) = false;
        TooltipOnWindow.NotePreviewFlagRepair(__instance.gameObject.name);
    }
}
