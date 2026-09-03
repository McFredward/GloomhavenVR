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
    /// <b>THE FIFTH MEMBER OF THIS FAMILY, AND THE ONE ModBuild 191 DID NOT KNOW ABOUT</b>
    /// (ModBuild 367). <c>DeltaWorldPositionToFitRectTransform(this RectTransform, Camera,
    /// RectTransform areaToFit, bool checkBothAxies)</c> — RectTransformExtensions.cs:155-207.
    ///
    /// <para>User, 2026-09-03: <i>"Wenn ich bei der Szenario-Auswahl am Ende die persoenlichen
    /// Quests ausgewaehlt habe, dann aber bei einem Character nochmal draufklicke um meine Meinung
    /// zu aendern sind die Abstaende kaputt (siehe fenster-abstand.jpg). Ich will das es wieder
    /// ganz links spawnt wie es war und der button rechts daneben, nicht darueber liegend. Das
    /// gilt auch wenn ich dort noch anderen Charactereigenschaften wieder auf mache."</i></para>
    ///
    /// <para><b>IT IS THE SAME DEFECT AS THE FOUR ABOVE, WITH A VIEWPORT AS THE FRAME INSTEAD OF
    /// THE SCREEN.</b> It decides whether a corner has escaped by projecting BOTH rects through the
    /// camera it is handed — eight <c>camera.WorldToScreenPoint</c> calls, :162-167 — and then
    /// returns a RAW WORLD-CORNER DIFFERENCE (<c>array[2].x - array2[2].x</c>, :189) which the
    /// caller adds to <c>.position</c>. Every caller passes <c>UIManager.Instance.UICamera</c>,
    /// which in VR is parked and does not render a floated panel at all: the decision is taken on a
    /// screen the rect is not on, and the correction is then applied to a <c>WorldSpace</c> canvas
    /// standing metres away at roughly 1/1000 scale. Both halves are wrong independently, which is
    /// the whole reason this family is cut rather than corrected.</para>
    ///
    /// <para><b>WHAT IT COST, MEASURED IN THE 2026-09-03 LOG.</b> The battle-goal picker places its
    /// card column with it (<c>UIBattleGoalPickerWindow.RefreshPosition</c>, :202-207, called from
    /// <c>Display</c> and again one frame later from <c>RefreshPositionDelayed</c>, which is why
    /// re-opening the picker is when it shows). Shoved right, the column pushed the party display's
    /// painted union from <c>-960..-104</c> authored px (picker closed) out to <c>-960..763</c> px,
    /// and the mod's own confirm seat — which puts 'Verlies betreten' one 24 px gap right of
    /// everything the window paints — then had 187 px of room for a 307 px control and was clamped
    /// back to <c>653..960</c>, i.e. 110 px INSIDE the cards. <c>LOADOUT CONFIRM RESEATED</c> caught
    /// the move whole: <c>(719,0)</c> px in one step. So "nicht ganz links" and "darueber liegend"
    /// are one cause, and neutralising this helper answers both.</para>
    ///
    /// <para><b>AND IT ANSWERS HIS SECOND SENTENCE BY CONSTRUCTION.</b> "Das gilt auch wenn ich
    /// dort noch anderen Charactereigenschaften wieder auf mache" is a request about a CLASS, and
    /// the class is the caller set of this one method: <c>UIBattleGoalPickerWindow</c> (the
    /// personal-quest picker), <c>UIPartyCharacterAbilityCardsDisplay</c> (the ability cards, the
    /// other thing a character row opens) and <c>UIFollowMapLocationInsideArea</c>. Cutting the
    /// helper covers all three; cutting the picker would have fixed the screenshot and left the
    /// second one to be reported next round.</para>
    ///
    /// <para><b>ZERO IS THE AUTHORED LAYOUT, NOT A GUESS.</b> The caller's two preceding statements
    /// already put the container on the seat the artist authored and rebuilt its layout; this
    /// helper only ever ADDS to that. Returning zero therefore leaves the column exactly where the
    /// flat game puts it whenever it already fits — which on a 1920x1080 canvas the mod floats
    /// WHOLE it always does, because the mod does not crop a floated window and so there is no
    /// viewport left for it to overflow.</para>
    ///
    /// <para><b>** THAT LAST PARAGRAPH IS FALSIFIED, AND THE ZERO WAS THE SECOND HALF OF THE
    /// DEFECT (ModBuild 386). **</b> The user re-reported the same screen after 367 shipped:
    /// <i>"Beim erneuten Oeffnen der persoenlichen Quest nachdem man es schon geoeffnet hatte sind
    /// die Abstaende falsch"</i>. The 380 hardware log has the cut RUNNING on this exact rect
    /// (second_logs/LogOutput.log:8713, on <c>'Container'</c> inside
    /// <c>GloomhavenVR.Panel_Modal_New Party display</c>) and the column shoved anyway, so
    /// "neutralising this helper answers both" is not what happened.</para>
    ///
    /// <para><b>THE MEASUREMENT THAT KILLS THE ZERO.</b> The pre-reveal fit line for the same
    /// window in the WORKING state names the column's rects outright — <c>'UI Battle Goal Picker
    /// Slot/Rewards' 512x129px at (-617,203)</c> (second_logs/LogOutput.log:8653), i.e. the goal
    /// cards span <c>-617..-105</c> authored px, one gap right of the 300 px character column at
    /// <c>-960..-660</c>. The user's screenshot (fenster-abstand2.jpg) measures the SAME cards, at
    /// 1:1 against that 328 px character column, at roughly <c>+190..+760</c> px — hard against
    /// the right of the picker's own 1920 px canvas, about 1000 px right of where they belong.
    /// <c>RefreshPosition</c> (UIBattleGoalPickerWindow.cs:202-207) assigns a FIXED cached
    /// <c>slotContainerPosition</c>, force-rebuilds, and then adds this call's answer. For this
    /// caller the helper is the only term that can produce two different seats at all — the other
    /// two are a constant and a rebuild — so <b>a flat zero makes the column's seat a CONSTANT</b>,
    /// and "returning zero leaves it where the flat game puts it" is a claim about a seat nobody
    /// has ever measured.</para>
    ///
    /// <para><b>WHAT IS NOT PROVEN, STATED BEFORE THE FIX RATHER THAN AFTER IT.</b> The 385 log
    /// (.planning/debug/LogOutput.log) falsifies the simple story in which this zero is the whole
    /// cause. With the cut LIVE the party display's painted union alternates between
    /// <c>-960..-104/-80/-90</c> at 123-128 graphic(s) and <c>-960..960</c> at 595-696, several
    /// times over, while the picker stays open (LOADOUT CONFIRM SEAT LANE, 9 BESIDE against 4
    /// OVER). A constant seat cannot alternate, so SOMETHING ELSE also moves this content. The
    /// full-frame state's own furthest-outside contributor names it as a battle-goal part —
    /// <c>'Information/Rewards' by 373 px</c> below a 1080 px frame — i.e. the goal list drawn at
    /// full length rather than the two-card view, which is a clipping/extent question and not a
    /// question about this delta.</para>
    ///
    /// <para><b>WHY THE CHANGE SHIPS ANYWAY, AND WHAT DECIDES IT NEXT ROUND.</b> Not because the
    /// zero is proven to be the mover — it is not — but because zero is the wrong ANSWER for this
    /// caller under either story, and because it made the mod blind: both 367 lines are written
    /// once per session, so no log yet says how often the helper ran, on what, or what seat came
    /// out. The replacement is the game's own clamp recomputed in a basis that is correct by
    /// construction, and it carries the per-outcome line described on
    /// <see cref="NoteViewportFit"/>. If the next log shows this prefix moving the column by
    /// (0,0) while the seat it prints is already a thousand pixels right, the helper is INNOCENT
    /// and the mover is upstream of <c>RefreshPosition</c> — one round, one number.</para>
    ///
    /// <para><b>WHAT REPLACES THE ZERO: THE SAME FIT, DONE IN THE WINDOW'S OWN BASIS.</b> Both
    /// halves of the vanilla helper are still wrong on a floated window and neither is used. The
    /// decision is retaken in the OWNING HOST'S local space (authored uGUI px, the frame both
    /// rects actually live in) instead of through the parked <c>UIManager.Instance.UICamera</c>,
    /// and the correction is handed back as <c>HostRect.TransformVector(dx, dy, 0)</c> instead of
    /// a raw world-corner difference — so the caller's <c>position +=</c> slides the rect along
    /// the window's own plane by exactly the authored pixels the clamp asked for, at any rig
    /// scale, yaw or panel scale. Where the rect already fits, the delta is zero and the outcome
    /// is byte-identical to the 367 cut.</para>
    ///
    /// <para><b>THE ZERO SURVIVES AS THE FALLBACK</b>, for the case the local basis cannot be
    /// trusted: no <c>HostRect</c>, no <c>areaToFit</c>, or an <c>areaToFit</c> that resolves to a
    /// DIFFERENT floated window (or to none) — three states in which host-local coordinates would
    /// be comparing two frames. Those take the 367 behaviour unchanged.</para>
    ///
    /// <para>The four SCREEN-fit overloads above keep their flat zero and are untouched by this
    /// build. Their frame is <c>Screen.width/height</c>, which a window standing in the room is
    /// genuinely not on and for which no local basis exists; this one has a real in-window frame
    /// handed to it as an argument, and that is the whole difference.</para>
    ///
    /// <para>The <c>bool</c> is in the signature because the method has a DEFAULT argument, not an
    /// overload: <c>AccessTools</c> matches on the full parameter list including
    /// <c>checkBothAxies</c>, and a three-type array would not resolve.</para>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RectTransformExtensions),
        nameof(RectTransformExtensions.DeltaWorldPositionToFitRectTransform),
        new[] { typeof(RectTransform), typeof(Camera), typeof(RectTransform), typeof(bool) })]
    private static bool CutWorldFitRectTransform(RectTransform rectTransform, RectTransform areaToFit,
                                                 ref Vector3 __result)
    {
        ArmViewportFitOnce();
        if (!WorldUIConfig.ConversionActive || rectTransform == null)
            return true; // VR off — the game's arithmetic runs byte-identically

        ConvertedPanel? owner = ModalFallback.FindOwningWindow(rectTransform);
        if (owner == null)
        {
            // Not inside a floated window: the flat screen, or a surface the mod never took. The
            // helper's own answer is the right one there and is handed back untouched.
            NoteViewportFit(ViewportFitVerdict.NoFloatedHost, rectTransform, null, 0f, 0f);
            return true;
        }

        // The 367 fallback, kept for every state in which host-local coordinates would be
        // comparing two different frames. See THE ZERO SURVIVES AS THE FALLBACK above.
        RectTransform host = owner.HostRect;
        if (host == null || areaToFit == null
            || !ReferenceEquals(ModalFallback.FindOwningWindow(areaToFit), owner))
        {
            TooltipOnWindow.NoteScreenFitCut("DeltaWorldPositionToFitRectTransform(camera, areaToFit)",
                                             rectTransform, owner);
            __result = Vector3.zero;
            NoteViewportFit(ViewportFitVerdict.CutToZero, rectTransform, owner, 0f, 0f);
            return LogViewportFitCutOnce(rectTransform);
        }

        Vector2 fit = WindowLocalFitDelta(rectTransform, areaToFit, host);
        __result = fit == Vector2.zero
            ? Vector3.zero
            : host.TransformVector(new Vector3(fit.x, fit.y, 0f));
        NoteViewportFit(ViewportFitVerdict.FittedInWindow, rectTransform, owner, fit.x, fit.y);
        return false;
    }

    /// <summary>
    /// THE ModBuild 367 LINE, NARROWED TO THE STATE IT IS STILL TRUE OF. It is only written on the
    /// fallback branch now, because that is the only branch that still returns a flat zero.
    /// Always returns false (skip the original), so it reads as the tail of the caller.
    /// </summary>
    private static bool LogViewportFitCutOnce(RectTransform rectTransform)
    {
        if (_loggedViewportFitCut)
            return false;
        _loggedViewportFitCut = true;
        // HW-VERIFY: this is the one line that says the 2026-09-03 layout report was answered on
        // hardware. The shared SCREEN-FIT CUT line beside it is VRLog.Info, i.e. the DEBUG tier,
        // which a default-level log does not carry — and promoting THAT line would promote it for
        // all five helpers at once, including UILocalTooltip's, which is written on every
        // LateUpdate of every shown hint. One line of its own instead.
        VRLog.Note("WorldUI", $"VIEWPORT FIT CUT: the game asked to slide '{rectTransform.name}' back "
            + "inside a viewport rect, and that rect is drawing inside a window this mod floated "
            + "into the room. The helper decides in SCREEN space through the parked "
            + "UIManager.Instance.UICamera and then returns a WORLD-space correction, so on a "
            + "WorldSpace canvas metres away at ~1/1000 scale it can only shove the content "
            + "sideways. Zero returned instead, which leaves the seat the caller's own "
            + "anchoredPosition + LayoutRebuilder just gave it. THIS IS THE 2026-09-03 REPORT: the "
            + "battle-goal column landing right of the character rows and 'Verlies betreten' being "
            + "clamped on top of it. Logged once per session; the cut keeps running."
            + " SINCE ModBuild 386 THIS IS THE FALLBACK BRANCH ONLY, and the sentence above about "
            + "the seat being left alone is the thing that build falsified: the picker's own "
            + "RefreshPosition assigns a fixed cached seat and RELIES on this helper to place the "
            + "column, so a flat zero left it on the authored full-canvas seat, about 1000 px "
            + "right of the character rows. The normal branch now re-decides the same clamp in the "
            + "owning host's authored-pixel basis; this line means that basis was not usable "
            + "(no host rect, no area, or an area owned by another window).");
        return false;
    }

    private static bool _loggedViewportFitCut;
    private static bool _armedViewportFit;

    /// <summary>
    /// ONE-SHOT PROOF OF LIFE. The line above is written at most once per session, so a log without
    /// it is ambiguous between "the patch never attached" and "no fitted rect was ever inside a
    /// floated window" — the exact ambiguity this project has already paid rounds for. This says
    /// which of the two it is.
    /// </summary>
    private static void ArmViewportFitOnce()
    {
        if (_armedViewportFit)
            return;
        _armedViewportFit = true;
        // HW-VERIFY: proof the viewport-fit prefix is LIVE.
        VRLog.Note("WorldUI", "VIEWPORT FIT GATE ARMED: the game asked for a "
            + "DeltaWorldPositionToFitRectTransform and this prefix ran on it. If no VIEWPORT FIT "
            + "CUT line follows in this log, that means no fitted rect was ever inside a floated "
            + "window — not that the patch is missing."
            + " ModBuild 386 SUPERSEDES THAT LAST SENTENCE: the cut is the FALLBACK branch now, so "
            + "its absence no longer carries that meaning. The per-outcome line marked "
            + ViewportFitMarker + " is the one that says what this prefix decided, on every "
            + "outcome including the one where the game's own answer was handed straight back.");
    }

    // ---- THE WINDOW-LOCAL VIEWPORT FIT (ModBuild 386) ------------------------------------------

    /// <summary>
    /// THE GREP TOKEN FOR THE PER-OUTCOME LINE, HOISTED. It is a constant and not a literal in the
    /// middle of the sentence for one reason, learned the expensive way on 2026-09-03: the two 367
    /// lines beside it each SPELL OUT the marker they tell the reader to grep for, so grepping that
    /// marker returns the line telling you to grep it and the reading is wrong before it starts.
    /// Nothing printed by <see cref="NoteViewportFit"/> repeats these words.
    /// </summary>
    private const string ViewportFitMarker = "WINDOW LOCAL VIEWPORT FIT";

    /// <summary>What the prefix decided for one call. One value per BRANCH, never per symptom.</summary>
    private enum ViewportFitVerdict
    {
        /// <summary>No floated owner: the game's own arithmetic ran and its answer stands.</summary>
        NoFloatedHost,

        /// <summary>Floated, but no usable host-local basis — the ModBuild 367 zero.</summary>
        CutToZero,

        /// <summary>Floated, basis usable: the clamp was retaken in the host's authored pixels.</summary>
        FittedInWindow,
    }

    /// <summary>
    /// The fit the game meant, done in the frame the rects are actually in.
    ///
    /// <para>Both rects' world corners are pulled into <paramref name="host"/>'s local space, which
    /// for a converted window is its own authored uGUI pixels (the host rect is 1988x1080 px on the
    /// character screen, and its children are the game's own uGUI subtree). There the fit is an
    /// axis-aligned clamp, and the LOW edge wins when a rect is larger than the area on an axis —
    /// which is the same precedence the vanilla helper has, since its first branch tests corner 0
    /// (bottom-left) and returns before the corner-2 branch can look.</para>
    ///
    /// <para>The answer is returned in those SAME authored pixels; the caller turns it into the
    /// world vector with <c>host.TransformVector</c>, so the game's <c>transform.position +=</c>
    /// becomes a slide along the window's own plane of exactly this many authored pixels — correct
    /// at any rig scale, panel scale, yaw or pitch, and never off the plane. Returns
    /// <c>Vector2.zero</c> when the rect already fits, which is the common case.</para>
    /// </summary>
    private static Vector2 WindowLocalFitDelta(RectTransform rect, RectTransform area,
                                               RectTransform host)
    {
        if (!LocalBounds(rect, host, out Vector2 rectMin, out Vector2 rectMax)
            || !LocalBounds(area, host, out Vector2 areaMin, out Vector2 areaMax))
            return Vector2.zero;

        return new Vector2(AxisFit(rectMin.x, rectMax.x, areaMin.x, areaMax.x),
                           AxisFit(rectMin.y, rectMax.y, areaMin.y, areaMax.y));
    }

    /// <summary>One axis of the clamp. Low edge first — see <see cref="WindowLocalFitDelta"/>.</summary>
    private static float AxisFit(float min, float max, float areaMin, float areaMax)
    {
        if (min < areaMin)
            return areaMin - min;
        if (max > areaMax)
            return areaMax - max;
        return 0f;
    }

    /// <summary>Scratch for the two <c>GetWorldCorners</c> calls (main thread, one call at a time).</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>
    /// A rect's axis-aligned bounds in <paramref name="host"/>'s local space. All four corners are
    /// converted and min/max'd rather than taking corners 0 and 2, so a rect that carries a
    /// rotation of its own inside the window still yields the box that actually has to fit.
    /// </summary>
    private static bool LocalBounds(RectTransform? rect, RectTransform? host,
                                    out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;
        if (rect == null || host == null)
            return false;
        rect.GetWorldCorners(CornerScratch);
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = host.InverseTransformPoint(CornerScratch[i]);
            if (i == 0)
            {
                min = new Vector2(local.x, local.y);
                max = min;
                continue;
            }
            min = new Vector2(Mathf.Min(min.x, local.x), Mathf.Min(min.y, local.y));
            max = new Vector2(Mathf.Max(max.x, local.x), Mathf.Max(max.y, local.y));
        }
        return true;
    }

    // Running counts, one per branch, plus how many of the fitted calls actually MOVED anything.
    private static int _viewportFitVanilla;
    private static int _viewportFitZeroed;
    private static int _viewportFitFitted;
    private static int _viewportFitMoved;

    /// <summary>Change gate: verdict + rect name + "did it move", plus a floor so the line cannot
    /// go permanently quiet while the prefix is still running (a change-gated line with a constant
    /// reason prints once and then reads exactly like a stopped tick).</summary>
    private static string _viewportFitSignature = string.Empty;
    private static int _viewportFitCallsAtLastLine = -1;

    /// <summary>Re-print the same verdict after this many further calls, so silence means STOPPED.</summary>
    private const int ViewportFitRepeatEvery = 400;

    /// <summary>
    /// THE OUTCOME LINE. It reports only what this method can see: which branch ran, on which rect,
    /// what the four running counts are, and — on the fitted branch — the rect's own box in the
    /// host's authored pixels before and after the delta, beside the area it was clamped into. It
    /// does NOT claim the user's layout is fixed; the seat it prints is the evidence for that, and
    /// the party display's own union lines are the independent second reading.
    /// </summary>
    private static void NoteViewportFit(ViewportFitVerdict verdict, RectTransform rect,
                                        ConvertedPanel? owner, float dx, float dy)
    {
        switch (verdict)
        {
            case ViewportFitVerdict.NoFloatedHost: _viewportFitVanilla++; break;
            case ViewportFitVerdict.CutToZero: _viewportFitZeroed++; break;
            default:
                _viewportFitFitted++;
                if (dx != 0f || dy != 0f)
                    _viewportFitMoved++;
                break;
        }

        int calls = _viewportFitVanilla + _viewportFitZeroed + _viewportFitFitted;
        string name = rect != null ? rect.name : "<destroyed>";
        string signature = (int)verdict + "|" + name + "|" + (dx != 0f || dy != 0f ? "1" : "0");
        if (signature == _viewportFitSignature
            && calls - _viewportFitCallsAtLastLine < ViewportFitRepeatEvery)
            return;
        _viewportFitSignature = signature;
        _viewportFitCallsAtLastLine = calls;

        string window = owner != null && owner.HostGo != null ? owner.HostGo.name : "none";
        string outcome = verdict switch
        {
            ViewportFitVerdict.NoFloatedHost =>
                "the rect is not inside a window this mod floated, so the game's own answer was "
                + "returned unchanged and this prefix changed nothing",
            ViewportFitVerdict.CutToZero =>
                "the rect IS on a floated window but no usable in-window basis was available (no "
                + "host rect, no area rect, or an area rect owned by a different window), so the "
                + "correction was zeroed — the ModBuild 367 behaviour, now the fallback",
            _ =>
                "the clamp was retaken in the owning window's own authored pixels and handed back "
                + "as a slide along that window's plane, replacing a correction the game had "
                + "decided on a screen this rect is not drawn on",
        };

        // The seat is measured on EVERY branch that has a host to measure it in, not only on the
        // one that moves the rect. A branch that reports "moved it by (0,0)" while the seat it
        // prints is already wrong says the mover is upstream of this call — which is the one thing
        // a single hardware round has to be able to decide.
        string seat = "no floated host to measure a seat in";
        if (owner != null && LocalBounds(rect, owner.HostRect, out Vector2 rmin, out Vector2 rmax))
        {
            float x0 = rmin.x;
            float x1 = rmax.x;
            float y0 = rmin.y;
            float y1 = rmax.y;
            seat = $"the rect sits at x {x0:0}..{x1:0}, y {y0:0}..{y1:0} authored px "
                 + $"in that window's frame and the correction moves it by ({dx:0},{dy:0}) px, "
                 + $"to x {x0 + dx:0}..{x1 + dx:0}, y {y0 + dy:0}..{y1 + dy:0}";
        }

        // HW-VERIFY: the 2026-09-03 report "beim erneuten Oeffnen ... sind die Abstaende falsch".
        VRLog.Note("WorldUI", $"{ViewportFitMarker} on '{name}' (window '{window}'): {outcome}. "
            + $"{seat}. RUNNING COUNTS over this session: {_viewportFitFitted} call(s) took the "
            + $"in-window clamp ({_viewportFitMoved} of them moved the rect at all), "
            + $"{_viewportFitZeroed} took the zero fallback, {_viewportFitVanilla} were left to the "
            + "game. HOW TO READ IT: the personal-quest column is a rect named 'Container' inside "
            + "the party display; on the working layout its goal cards span roughly -617..-105 "
            + "authored px, one gap right of the character rows at -960..-660. A seat printed near "
            + "+190..+760 is the displacement the user photographed. This line is written when the "
            + $"branch, the rect or the moved/not-moved answer changes, and again every "
            + $"{ViewportFitRepeatEvery} calls so that a silent log means the prefix STOPPED, not "
            + "that it settled. AND THE ONE DECISION IT EXISTS TO SETTLE: a correction of (0,0) "
            + "printed beside a seat that is ALREADY a thousand px right means this helper is not "
            + "what moved the column and the mover is upstream of the caller's own "
            + "anchoredPosition + layout rebuild; a correction that carries the column from the "
            + "right of the frame back beside the character rows means it was.");
    }

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
    /// the only method of that name on the type). This is the SWAP menu's row: clicking an equipment
    /// slot opens <c>UIPartyItemInventoryDisplay</c>, and hovering one of its rows is what the
    /// 2026-08-22 report ("in the menu for swapping items I now see NO overlays at all") is about.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyItemSlot), nameof(UIPartyItemSlot.OnHovered))]
    private static void NotePartyItemHover(UIPartyItemSlot __instance, bool hovered) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UIPartyItemSlot (party inventory item)", hovered);

    /// <summary>
    /// <c>UIPartyCharacterEquippementSlot.OnHovered()</c> / <c>.OnUnHovered()</c> — the character
    /// screen's EQUIPMENT rows (the "Ausrüstung" list), wired from the slot's own
    /// <c>ExtendedButton.onMouseEnter/onMouseExit</c> (UIPartyCharacterEquippementSlot.cs:138-139) and
    /// the path that goes on to call <c>itemTooptip.Show(...)</c>
    /// (UIPartyCharacterEquipmentDisplay.cs:451-465).
    ///
    /// <para>THIS FAMILY WAS THE ONE THE REPORT IS ABOUT AND IT HAD NO ATTRIBUTION AT ALL. Only the
    /// merchant's slot and the swap list's slot were reported, so an equipment hover that raised
    /// nothing produced no line of any kind — indistinguishable from an equipment hover that was never
    /// made. Both edges are wired, because a one-sided hover watch reports every pointer that merely
    /// moved on as a silent failure.</para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyCharacterEquippementSlot), "OnHovered")]
    private static void NoteEquipmentSlotHover(UIPartyCharacterEquippementSlot __instance) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UIPartyCharacterEquippementSlot (equipment row)",
            hovered: true);

    /// <summary>See <see cref="NoteEquipmentSlotHover"/> — the exit edge, which withdraws the pending
    /// verdict.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyCharacterEquippementSlot), "OnUnHovered")]
    private static void NoteEquipmentSlotUnhover(UIPartyCharacterEquippementSlot __instance) =>
        TooltipOnWindow.NoteSlotHover(__instance, "UIPartyCharacterEquippementSlot (equipment row)",
            hovered: false);

    // ---- THE SHOW-EDGE VERDICT (ModBuild 196) -------------------------------------------------
    //
    // "No overlays at all" has four possible causes and four different fixes (never created, created
    // and destroyed, created off-window, created fully transparent), and nothing in this mod could
    // tell them apart: SILENT HOVER only fires when NOTHING was placed, so a widget that WAS placed
    // and then went invisible looked exactly like success. These two postfixes park every item-tooltip
    // show request; TooltipOnWindow.TickShowWatches judges it a dozen frames later against the
    // widget's observable state and names which of the four it was, with a running census.
    //
    // THE SEAM IS THE PRIVATE Build, NOT EITHER PUBLIC Show, and that is the whole reason this one
    // patch covers all three windows. The type ships TWO Show overloads and the three call sites do
    // not agree on which they use: the merchant and the swap list call
    // Show(item, target, boundTo, information, state, service) (UIShopItemInventory.cs:922/943,
    // UIPartyItemInventoryDisplay.cs:387) while the character screen's equipment view calls
    // Show(item, target, offset) (UIPartyCharacterEquipmentDisplay.cs:458). Patching one overload
    // would have produced a census that silently excluded the very window under investigation — the
    // same shape of blindness the kind-keyed log dedup had. Both overloads funnel into the single
    // private Build (UIPartyItemInventoryTooltip.cs:157/206/211), so one seam is the whole family and
    // there is no overload to get wrong.

    /// <summary>
    /// <c>UIPartyItemInventoryTooltip.Build(...)</c> — the one method both public <c>Show</c>
    /// overloads funnel into, and therefore the single seam that sees the merchant, the character
    /// screen's equipment view and the item-SWAP inventory alike. Postfix: it runs after the widget
    /// has been re-parented, after the pooled <c>ItemCardUI</c> has been spawned and after
    /// <c>window.Show()</c>, i.e. once the game considers the request finished — which is exactly the
    /// moment worth parking for a verdict.
    ///
    /// <para>The verdict itself is a dozen frames later (<c>TooltipOnWindow.TickShowWatches</c>),
    /// because the show path is not finished when this returns: <c>UIWindow.Show</c> may fade, and the
    /// merchant even defers one of its hints by a frame through <c>SkipAFrameAndNotifyNewItemTooltip</c>
    /// (UIShopItemInventory.cs:973). Judging on this frame would report every healthy tooltip as a
    /// failure.</para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPartyItemInventoryTooltip), "Build")]
    private static void NoteItemTooltipBuilt(UIPartyItemInventoryTooltip __instance,
        RectTransform target) =>
        TooltipOnWindow.NoteTooltipShowRequested(__instance,
            "UIPartyItemInventoryTooltip (item card hint)",
            target != null ? target.name : "<no target>");

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
