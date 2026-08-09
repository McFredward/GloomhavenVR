using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// TOOLTIPS MAY ONLY BE RAISED BY A REAL VR POINTER, AND NEVER WHILE THE BEAM IS ON A CARD FAN
/// (user report 2026-08-09, verbatim: "Wenn ich mit Laser auf meiner Hand über die Karten in dem
/// Fächer fahre blitzt hin&amp;wieder ein tooltip am controllboard auf - immer nur ganz kurz. Das
/// soll aber vollständig unterdrückt werden.").
///
/// <para>ROOT CAUSE — THE GAME'S OWN MOUSE POINTER SWEEPS THE ROOM WHEN THE HEAD MOVES. This is
/// the SAME defect <see cref="Cards.CardFaceRaycaster"/> was written for (task #5, card-area
/// highlights that followed head movement), one surface family later. <c>InControlInputModule.Process
/// → ProcessMouseEvent</c> runs EVERY frame (InControlInputModule.cs:234-248) and its
/// <c>GetMousePointerEventData</c> position is the PARKED desktop/virtual mouse pixel
/// (<c>InputSystemUtilities.GetMousePosition</c>, PointerInputModuleExtended.cs:269-307) — not
/// anything the player is aiming. Its <c>EventSystem.RaycastAll</c> reaches every enabled
/// <see cref="GraphicRaycaster"/>, and the mod's converted panels are WORLD-SPACE canvases whose
/// <c>worldCamera</c> is the HEAD camera (<c>CanvasConversion.WorldCamera</c>). A FIXED pixel
/// through a MOVING head camera is a world ray that sweeps across the control board's docked
/// panels on its own; every time it crosses a <c>UITooltipTarget</c> (an element of the element
/// board, an actor bar row, an initiative entry) the game raises its ONE shared tooltip, and
/// <c>WorldTooltips</c> faithfully presents it in the board's TOOLTIP AREA. That is the reported
/// flash — and it is brief because the very next head movement takes the ray off the target again,
/// so the widened show fade (<c>WorldTooltips.FadeGraceSeconds</c>) is interrupted and re-tweened
/// back down.</para>
///
/// <para>THE HARDWARE LOG SAYS IT OUTRIGHT (ModBuild 102, LogOutput.log 3150-3200): with the hand
/// fan OPEN and the beam PROVABLY off the panels — <c>Right ray occluded by the raised card fan —
/// blocked panel grab bar 'TrayHandle'</c>, i.e. <c>RayUguiDriver</c> suppressed its hover that
/// frame — the log still shows <c>Board tooltip published for the wire (165 chars, board-owned)</c>
/// + <c>Tooltip parked in the board TOOLTIP AREA</c> repeating, with NO <c>uGUI hover ENTER</c>
/// from any mod pointer anywhere near. A tooltip nobody's pointer entered is by elimination the
/// game's mouse pointer's, and its repetition (the same text published again and again) is the
/// flicker the user describes.</para>
///
/// <para>THE FIX IS AT THE RAISE, NOT AT THE PRESENTATION. Two rules, each in exactly one place:</para>
/// <list type="bullet">
/// <item><description>GHOST RULE (<see cref="BlockGhostHover"/>, on <c>UITooltipTarget.OnPointerEnter</c>
/// — the base-class method every target kind inherits, so one patch covers
/// <c>UITextTooltipTarget</c>, <c>UIPrefabTooltipTarget</c> and <c>UIItemCardTooltipTarget</c>
/// alike): on a WORLD-SPACE canvas, only a MOD pointer may raise a tooltip. The desktop mouse has
/// no business hovering a surface that exists in the room, and neither has the game's
/// selection-follows-mouse path (<c>OnSelect</c> → <c>OnPointerEnter(new PointerEventData(...))</c>,
/// pointer id 0), which is the SAME head-swept ray one dispatch later. Screen-space canvases are
/// untouched, so the FlatScreen / Menu2D mouse path — where the player really is driving a mouse
/// through <c>VirtualMouseBridge</c> — keeps its tooltips exactly as before.</description></item>
/// <item><description>FAN RULE (<see cref="BlockWhileBeamOnFan"/>, on the static
/// <c>UITooltip.Show</c> — deliberately the LAST common seam, so it holds for every raise path
/// there is, including the map's anchor-less <c>UITooltip.AnchorToRect(null, …)</c> hex tooltips
/// and any direct call): while a hand's beam is on card-fan geometry, no tooltip may be raised at
/// all. "On fan geometry" is <c>RayInteractor.FanOccluderDistance</c>, the mod's ONE answer to that
/// question (<c>ComputeFanOccluder</c> — hand fan, browse arc for discard/burnt, item fan), which
/// already carries the 0.15 s pull-jerk hold, so a one-frame dropout between two cards cannot open
/// a window either. This is the user's requirement stated literally: pointing at a card in ANY fan
/// shows no tooltip, whatever the beam happens to graze behind it.</description></item>
/// </list>
///
/// <para>REVERSIBLE / VANILLA-SAFE: the ghost rule gates on <see cref="WorldUIConfig.ConversionActive"/>
/// (no conversion ⇒ no world-space surfaces ⇒ the prefix returns true and desktop play is
/// byte-identical), the fan rule on a fan actually being raised in front of a live beam. Nothing
/// is mutated: a blocked enter simply never sets <c>UITooltipTarget.TooltipShown</c>, so the
/// matching exit is a no-op and the game's own visual-state machine is never left mid-tween.</para>
///
/// <para>MULTIPLAYER: <c>WorldTooltips.WireText</c> is composed from the SHOWN tooltip, so a
/// tooltip that is never raised is never published to peers either — the remote board
/// (<c>Net.RemoteBoardTooltip</c>) shows exactly what the local board shows, which is nothing.
/// No wire change is needed, and the 1:1 rule holds by construction rather than by a second gate.</para>
/// </summary>
[HarmonyPatch]
internal static class TooltipRaiseGuard
{
    /// <summary>Throttle for the suppression evidence line (one per burst, not per hover).</summary>
    private const float LogThrottleSeconds = 5f;

    private static float _lastGhostLog = float.NegativeInfinity;
    private static float _lastFanLog = float.NegativeInfinity;

    /// <summary>
    /// GHOST RULE — see the class doc. Prefix on the base <c>UITooltipTarget.OnPointerEnter</c>
    /// (public, interface-dispatched via <c>IPointerEnterHandler</c>, far too large to inline).
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UITooltipTarget), nameof(UITooltipTarget.OnPointerEnter))]
    private static bool BlockGhostHover(UITooltipTarget __instance, PointerEventData eventData)
    {
        if (!WorldUIConfig.ConversionActive || __instance == null)
            return true; // VR off / nothing converted — vanilla flat behaviour
        // A mod pointer is a real aimed pointer: laser or fingertip. Everything else here is the
        // game's own mouse/touch/selection path, which in VR is not aimed by anybody.
        if (eventData != null && UguiPointer.IsModPointerId(eventData.pointerId))
            return true;
        if (!IsOnWorldSurface(__instance))
            return true; // screen-space (FlatScreen / Menu2D) — the mouse there IS the player

        float now = Time.unscaledTime;
        if (now - _lastGhostLog > LogThrottleSeconds)
        {
            _lastGhostLog = now;
            // Info, not Debug, on purpose: BepInEx's default disk config drops Debug, and this is
            // the line the next hardware log needs to show the gate firing where it should.
            VRLog.Info("WorldUI",
                $"Tooltip raise SUPPRESSED on world surface '{__instance.name}' — the event came " +
                $"from pointer id {(eventData != null ? eventData.pointerId.ToString() : "<none, OnSelect>")}, " +
                "i.e. the game's own mouse/selection pointer, which sits at a PARKED screen pixel and " +
                "therefore sweeps a world ray across the board's panels whenever the HEAD moves. In VR " +
                "only a mod pointer (laser/poke) may raise a tooltip on a world-space canvas.");
        }
        return false;
    }

    /// <summary>
    /// FAN RULE — see the class doc. Prefix on the static <c>UITooltip.Show</c>, the one seam every
    /// raise path funnels through.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.Show))]
    private static bool BlockWhileBeamOnFan()
    {
        if (!BeamOnFan())
            return true;

        float now = Time.unscaledTime;
        if (now - _lastFanLog > LogThrottleSeconds)
        {
            _lastFanLog = now;
            VRLog.Info("Cards",
                "Tooltip raise SUPPRESSED — a hand's beam is on CARD-FAN geometry " +
                "(RayInteractor.FanOccluderDistance: hand fan / discard+burnt browse arc / item fan, " +
                "incl. its 0.15 s pull-jerk hold). Pointing at a fan card must never flash a tooltip " +
                "at the control board (user ruling 2026-08-09).");
        }
        return false;
    }

    /// <summary>
    /// Is either hand's beam currently on a raised card fan? Both hands are asked because both
    /// browse fans sweep with BOTH hands (user ruling 2026-08-03) — the pointing hand is not
    /// necessarily the primary one. <c>Ray.Active</c> is implied by a finite occluder distance
    /// (an inactive ray resets it to +inf, see <c>RayInteractor.Tick</c>) and is read anyway so
    /// the intent survives a future change there.
    /// </summary>
    private static bool BeamOnFan() => BeamOnFan(VRHands.Left) || BeamOnFan(VRHands.Right);

    private static bool BeamOnFan(VRHand? hand) =>
        hand != null && hand.Ray != null && hand.Ray.Active
        && !float.IsPositiveInfinity(hand.Ray.FanOccluderDistance);

    /// <summary>
    /// Does this tooltip target live on a WORLD-SPACE canvas (a converted panel, a mod-built
    /// surface, a card face)? Asked of the ROOT canvas: a converted window keeps the game's own
    /// nested canvases, whose render mode is inherited from the root that actually draws them.
    /// Resolved per pointer-enter (a handful per second at most), never per frame.
    /// </summary>
    private static bool IsOnWorldSurface(UITooltipTarget target)
    {
        Canvas? canvas = target.GetComponentInParent<Canvas>();
        Canvas? root = canvas != null ? canvas.rootCanvas : null;
        return root != null && root.renderMode == RenderMode.WorldSpace;
    }
}
