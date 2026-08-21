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
}
