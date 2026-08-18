using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// Restores the game's OWN prop-hover path under VR — the one that produces the
/// water / trap / hazardous-terrain / spawner hint cards.
///
/// USER REPORT (ModBuild 158, verbatim): "Weiterhin wenn ich mit dem laser drauf hovere
/// kommt kein Hinweis." — said about the water hexes of a crypt room. The cause is NOT
/// water-specific; water is only the most visible victim.
///
/// MECHANISM (all READ FROM SOURCE — decompiled GH.Runtime, re-verified 2026-08).
/// Gloomhaven has TWO independent hover-hint producers:
///
/// 1. THE HEX PATH — <c>WorldspaceStarHexDisplay.ShowTooltipForTile</c> (WSHD.cs:3370-3614),
///    fed by <c>Interactable()</c> → <c>MF.FindInteractableAtMousePosition</c>, which the mod
///    ALREADY prefixes with the VR pick ray (<see cref="MF_FindInteractableAtMousePosition_Patch"/>).
///    This path works in VR. It branches on <c>cObjectProp.ObjectType</c> for MoneyToken,
///    Chest, GoalChest, Obstacle, Door, PressurePlate, Portal, CarryableQuestItem and
///    Resource, plus dungeon-exit / blocked-node / spawner-name fallbacks. It has
///    <b>no branch for ANY <c>Terrain*</c> type and none for <c>Trap</c></b> — verified by
///    reading every branch of the method. So gold, chests, doors and obstacles still hint in
///    VR, and water never could, even in vanilla, through this path.
///
/// 2. THE PROP PATH — <c>IHoverable.OnCursorEnter/OnCursorExit</c>. Grepping the whole
///    decompiled tree, the ONLY caller of those two methods is <c>HoverRegisterer.Update</c>
///    (HoverRegisterer.cs:22-93). Six types implement <c>IHoverable</c>; four of them own a
///    hint card that exists NOWHERE else:
///      • <c>UnityGameEditorDifficultTerrainProp</c> → <c>UIPropInfoPanel.ShowDifficultTerrain</c>
///        — DIFFICULT_TERRAIN_TOOLTIP + "$MoveCost$: 2" (UIPropInfoPanel.cs:194-222).
///        <c>ObjectImportType.TerrainWater</c> AND <c>TerrainRubble</c> both map to
///        <c>CObjectDifficultTerrain</c> (CMapScenarioState.cs:1240-1320) — this is the water card.
///      • <c>UnityGameEditorTrapProp</c> → <c>UIPropInfoPanel.ShowTrap</c>.
///      • <c>UnityGameEditorHazardousTerrainProp</c> → <c>UIPropInfoPanel.ShowHazardousTerrain</c>
///        (<c>TerrainHotCoals</c>, <c>TerrainThorns</c>).
///      • <c>UnityGameEditorSpawnerProp</c> → <c>ActorStatPanel.Show(CSpawner)</c>.
///      • <c>UnityGameEditorDoorProp</c> → the attached-actor stat panel, and the only place
///        that ever disables a door's hover capsule once the door opens.
///      • <c>CInteractable</c> — empty virtual no-op, no override anywhere.
///
/// THE BREAK. <c>HoverRegisterer.Update</c> builds its ray by un-projecting the game cursor:
/// <code>
///   private Camera m_Camera;                       // = Camera.main, cached in Awake()
///   Ray ray = m_Camera.ScreenPointToRay(InputManager.CursorPosition);
///   Physics.Raycast(ray, out hitInfo, 1000f, targetLayer);
/// </code>
/// In VR those two halves come from DIFFERENT cameras:
/// - <c>InputManager.CursorPosition</c> is patched
///   (<see cref="InputManager_CursorPosition_Patch"/>) to
///   <c>BoardPick.TryGetCursorScreenPoint</c>, which projects the VR pick point through
///   <c>VRRigDriver.HeadCamera</c> (BoardPick.cs:174) — the mod's own camera.
/// - <c>m_Camera</c> is <c>Camera.main</c>. The head camera is a NEW GameObject
///   ("GloomhavenVR.HeadCamera", VRRigDriver.HeadCamera.cs:311) that is never tagged
///   MainCamera, and the game's scenario camera is left in place, merely FROZEN
///   (<c>m_IsCameraCodeControlDisabled = true</c>, VRRigDriver.cs:~703) and stripped of stereo
///   by <c>VRCameraPolicy</c> — its GameObject stays active, so <c>HoverRegisterer</c>'s own
///   "camera went inactive, re-fetch" branch (:28-31) never fires either.
/// So <c>Camera.main</c> is the PARKED orbit camera at the vantage the player had when the
/// scenario loaded, and the pixel handed to it was measured in the head camera's screen
/// space, at a different position, orientation, FOV and rig scale. The resulting ray lands
/// nowhere near the hex the laser is on. Nothing in the mod ever patched
/// <c>HoverRegisterer</c>, so this had been dead since the rig was introduced.
///
/// THIS IS THE ASYMMETRY THE USER SEES: doors, chests and gold have a second producer (the
/// hex path, correctly re-aimed); water, traps, hazardous terrain and spawners have only the
/// prop path, and the prop path was aimed by a parked camera.
///
/// THE FIX (this prefix). Do for <c>HoverRegisterer.Update</c> exactly what
/// <see cref="MF_FindInteractableAtMousePosition_Patch"/> already does for the game's other
/// pick: drop the screen-space un-projection entirely and raycast the VR pick ray
/// (<see cref="BoardPick.TryGetGameRay"/> — fingertip or laser, arbitrated near-over-far)
/// against <c>HoverRegisterer</c>'s OWN serialized <c>targetLayer</c> mask, then run the
/// original's enter/exit bookkeeping ON THE ORIGINAL'S OWN LISTS. Camera.main is never
/// touched, so the freeze cannot matter.
///
/// <para><b>Why the original's own lists, not ours.</b> <c>m_CursorTargets</c> is the
/// game's record of "who currently believes the cursor is on them". If we kept a private
/// copy and this patch were ever removed mid-session (hot reload — <c>Harmony.UnpatchSelf</c>
/// in <c>Plugin.OnDestroy</c>), the original would resume with an EMPTY list while a card was
/// on screen and would never fire the matching <c>OnCursorExit</c> — a hint card stuck
/// forever. Writing through <c>FieldRefAccess</c> into the game's lists means the state
/// machine is literally the game's own; unpatching is seamless in both directions.</para>
///
/// <para><b>Enter/exit pairing.</b> Every dispatch below is a transcription of
/// HoverRegisterer.cs:38-92, in the same order, with the same membership tests — an
/// <c>OnCursorEnter</c> happens only on <c>Add</c> to <c>m_CursorTargets</c> and an
/// <c>OnCursorExit</c> only on <c>Remove</c>/<c>Clear</c> from it, so "a card is showing"
/// and "this IHoverable is in the list" are the same statement by construction. The three
/// early-out branches of the original (gate closed, ray missed, hit had no IHoverable) are
/// all preserved, and the two that leave the list non-empty both drain it through
/// <see cref="ExitAll"/> first. The one thing this prefix ADDS is a fourth drain: in a live
/// scenario with no VR pick at all this frame, the original would have swept its parked-camera
/// ray and could have latched a card; we exit everything instead (see the parked-pointer note
/// below). Net effect: the list is empty on every frame where nothing is under the pointer,
/// which is exactly the invariant that makes a stuck card impossible.</para>
///
/// <para><b>The parked mouse pointer cannot drive this.</b> Project rule (WorldUI
/// <c>TooltipRaiseGuard</c> / <c>MouseWorldSurfaceCut</c>): in VR only a MOD pointer — laser or
/// fingertip — may raise a tooltip on a world surface, because the game's own EventSystem
/// mouse sits at a PARKED desktop pixel and a fixed pixel through a moving head camera is a
/// world ray that sweeps the room by itself. Those guards work on <c>PointerEventData.pointerId</c>
/// and therefore cannot cover <c>HoverRegisterer</c>, which is a raw physics raycast. Here the
/// equivalent guarantee is structural rather than a predicate: the ONLY ray this code can ever
/// cast is <c>BoardPick.TryGetGameRay</c>, i.e. the laser or the gripped fingertip. It never
/// reads <c>InputManager.CursorPosition</c> and never touches a camera, so there is no pixel
/// for the parked mouse to sit at. Vanilla (mouse) behaviour is reached in exactly one case,
/// <c>!BoardPick.InScenario</c> — 2D menu / no scenario <c>Controller</c> — where there is no
/// board, no VR pick, and the desktop mouse IS the player. Inside a scenario the prefix always
/// returns false. The original's own UI gate is kept and is likewise VR-truthful, because
/// <c>UIManager.IsPointerOverUI</c> is already patched to "the picking hand's beam is latched on
/// a UI surface" (<see cref="UIManager_IsPointerOverUI_Patch"/>).</para>
///
/// <para><b>No double-fire with the hex path.</b> The two producers write different panels
/// with one overlap. The hex path drives <c>UITextInfoPanel.Show</c> and the single
/// <c>UIPropInfoPanel.ShowQuestItem</c>; this path drives <c>ShowTrap</c> /
/// <c>ShowHazardousTerrain</c> / <c>ShowDifficultTerrain</c> and <c>ActorStatPanel</c>. The
/// overlap is the shared <c>UIPropInfoPanel</c> singleton, and it is resolved by the game's own
/// typed hide: <c>UIPropInfoPanel.Hide(params EPropType[])</c> only acts when the CURRENT
/// content matches (UIPropInfoPanel.cs:266). So <c>ShowTooltipForTile</c>'s
/// <c>Hide(EPropType.QuestItem)</c> (WSHD.cs:3575) — and the mod's mirror of it in
/// <see cref="HexHoverClear"/>:295 — cannot erase a terrain/trap card; that is what
/// HexHoverClear's comment at :293 already asserts, and it stays true. The remaining case (a
/// hex carrying BOTH a quest item and difficult terrain, where the two producers would trade the
/// panel) is unchanged vanilla behaviour, not something this patch introduces. No prop type gains
/// a SECOND hint: the four restored types have no hex-path branch at all, and the hex-path types
/// (gold, chest, obstacle, door, portal, plate, resource, quest item) carry no <c>IHoverable</c>
/// except the door, whose <c>OnCursorEnter</c> shows only the attached-actor stat panel and never
/// touches <c>UIPropInfoPanel</c>. The second shared singleton, <c>ActorStatPanel</c> (spawner
/// hover, door-attached-actor hover, and the hex path's enemy popup), is safe for the same
/// reason and by the game's own construction: <c>HideForActor</c> / <c>HideForSpawner</c>
/// (ActorStatPanel.cs:1210-1226) each hide only when the panel is showing THAT subject AND the
/// other slot is empty, so a restored spawner exit cannot tear down a monster popup the hex hover
/// raised, or the reverse.</para>
///
/// <para><b>Cost.</b> One <c>Physics.Raycast</c> per frame — the SAME one the original casts,
/// with the same mask and the same length (1000 m for the laser; a few centimetres for the
/// fingertip pick, which is <c>BoardPick</c>'s near-ray budget). Nothing is added: the original
/// method does not run. The per-frame allocation the original had is removed —
/// <c>GetComponents&lt;IHoverable&gt;()</c> returned a fresh array every hit frame; the
/// list-filling overload writes into the game's own <c>m_CurrentRaycastTargets</c> instead. No
/// throttle: hover latency is what the user is complaining about, and there is no extra work to
/// throttle. Diagnostics ARE throttled (below) — the raycast is not.</para>
///
/// <para><b>Multiplayer.</b> Hover is local presentation; nothing here goes on the wire. The
/// mod's <c>TrackHover</c> extension record (NetProtocol.ExtIdTrackHover) samples the INITIATIVE
/// TRACK hover latch (<c>InitiativeTrackActorAvatar.highlighted</c>, via
/// <c>Net/InitiativeHoverSampler</c>) — a uGUI pointer-enter latch on a screen widget, with no
/// connection to <c>HoverRegisterer</c>, <c>IHoverable</c> or board props. This patch changes
/// nothing it samples.</para>
/// </summary>
[HarmonyPatch(typeof(HoverRegisterer), "Update")]
internal static class HoverPickPatch
{
    private const string Scope = "HoverPick";

    /// <summary>
    /// Floor between two diagnostic lines (unscaled seconds). Dispatches are already
    /// change-gated by construction — they only happen when the hovered SET changes — but a
    /// laser jittering across a prop edge can alternate enter/exit every frame, so the log
    /// gets a hard rate cap and reports how many lines it swallowed.
    /// </summary>
    private const float MinLogIntervalSeconds = 0.25f;

    /// <summary>
    /// The serialized mask the game itself hovers with (<c>[SerializeField] private LayerMask
    /// targetLayer</c>, HoverRegisterer.cs:6-7). Read from the live instance rather than
    /// reconstructed: every hoverable prop is forced onto the "Hovering" layer in its
    /// <c>Start()</c> (UnityGameEditorObject.cs:62-65 and each <c>UnityGameEditor*Prop</c>), but
    /// the mask's actual VALUE lives in a shipped scene asset that is not in this repo, so
    /// hard-coding <c>LayerMask.GetMask("Hovering")</c> would be a guess. This is the game's own
    /// answer.
    /// </summary>
    private static readonly AccessTools.FieldRef<HoverRegisterer, LayerMask> TargetLayerRef =
        AccessTools.FieldRefAccess<HoverRegisterer, LayerMask>("targetLayer");

    /// <summary>The game's record of what currently believes the cursor is on it (HoverRegisterer.cs:11).</summary>
    private static readonly AccessTools.FieldRef<HoverRegisterer, List<IHoverable>?> CursorTargetsRef =
        AccessTools.FieldRefAccess<HoverRegisterer, List<IHoverable>?>("m_CursorTargets");

    /// <summary>The game's per-frame scratch list (HoverRegisterer.cs:13) — reused so we allocate nothing.</summary>
    private static readonly AccessTools.FieldRef<HoverRegisterer, List<IHoverable>?> CurrentTargetsRef =
        AccessTools.FieldRefAccess<HoverRegisterer, List<IHoverable>?>("m_CurrentRaycastTargets");

    // ---- diagnostics state -------------------------------------------------------------------

    /// <summary>Unscaled time at which the next diagnostic line may be emitted.</summary>
    private static float _nextLogTime;

    /// <summary>Lines swallowed by the rate cap since the last emitted line.</summary>
    private static int _suppressed;

    private static int _errorLogs;

    /// <summary>Hot-reload hygiene (BoardModule.Shutdown) — all state here is static.</summary>
    public static void Reset()
    {
        _nextLogTime = 0f;
        _suppressed = 0;
        _errorLogs = 0;
    }

    // ---- patch -------------------------------------------------------------------------------

    private static bool Prefix(HoverRegisterer __instance)
    {
        // WorldUI lesson (worldui-input-gotchas): an unguarded NRE in a per-frame game path
        // starves everything downstream of it. On failure fall THROUGH to the original — a
        // mis-aimed vanilla hover is strictly better than no hover loop at all, and the original
        // heals the shared lists on its own next pass.
        try
        {
            return Run(__instance);
        }
        catch (Exception e)
        {
            if (_errorLogs < 3)
            {
                _errorLogs++;
                VRLog.Error(Scope, $"prefix failed — falling through to the vanilla (parked-camera) hover: {e}");
            }
            return true;
        }
    }

    private static bool Run(HoverRegisterer registerer)
    {
        // Outside a live scenario there is no board, no VR pick and the desktop mouse IS the
        // player (2D menu / world map / no scenario Controller): vanilla, untouched.
        if (!BoardPick.InScenario)
            return true;

        List<IHoverable>? cursorTargets = CursorTargetsRef(registerer);
        List<IHoverable>? currentTargets = CurrentTargetsRef(registerer);
        if (cursorTargets == null || currentTargets == null)
            return true; // Awake has not run yet — the original's own null-camera guard covers it

        // THE ORIGINAL'S GATE, verbatim (HoverRegisterer.cs:32, De Morgan'd into the "closed"
        // form). UIManager.IsPointerOverUI is the VR truth here, not the EventSystem's parked
        // mouse — see UIManager_IsPointerOverUI_Patch.
        if (UIManager.IsPointerOverUI
            || (ControllerInputAreaManager.IsEnabled
                && !ControllerInputAreaManager.IsFocusedArea(EControllerInputAreaType.WorldMap)))
        {
            ExitAll(cursorTargets, "the pointer is on UI, or a non-WorldMap controller input area owns input");
            return false;
        }

        // The VR pick ray — fingertip (grip-held, centimetres) or laser (1000 m), arbitrated
        // near-over-far by BoardPick. This is the ONLY ray this method can cast.
        if (!BoardPick.TryGetGameRay(out Vector3 origin, out Vector3 direction, out float maxDistance))
        {
            // In a scenario, but no hand produced a pick this frame (untracked / mode policy
            // disabled both interactors). The original would have swept its parked-camera ray
            // from the parked mouse pixel here; nothing is under a pointer that does not exist.
            ExitAll(cursorTargets, "no VR pick this frame (BoardPick.Source=None — hand untracked or mode policy)");
            return false;
        }

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, TargetLayerRef(registerer)))
        {
            ExitAll(cursorTargets, "the VR pick ray hit nothing on the hover mask");
            return false;
        }

        GameObject? hitObject = hit.transform != null ? hit.transform.gameObject : null;
        if (hitObject == null)
        {
            ExitAll(cursorTargets, "the hover raycast reported a hit with no transform");
            return false;
        }

        // HoverRegisterer.cs:39 uses GetComponents on the HIT GameObject — NOT
        // GetComponentInParent, which is what MF.FindInteractableAtMousePosition uses. Keep that
        // difference: every UnityGameEditor*Prop puts its collider and its IHoverable on the same
        // object, and widening to the parent would start hovering things the game never hovers.
        // The list overload fills the game's own scratch list in place, so the fresh array the
        // original allocated on every hit frame is gone.
        currentTargets.Clear();
        hitObject.GetComponents(currentTargets);

        // EXIT pass (HoverRegisterer.cs:40-48): anything that was hovered and is not under the
        // ray any more. The original writes `m_CursorTargets.Remove(hoverable)` inside this
        // descending-index loop; RemoveAt(i) is the identical operation here, because the list is
        // reference-unique by construction (the ENTER pass below only ever adds on !Contains) so
        // Remove's "first match" IS index i. Used instead of Remove so the element does not have
        // to be laundered past the nullable analysis.
        for (int i = cursorTargets.Count - 1; i >= 0; i--)
        {
            IHoverable hoverable = cursorTargets[i];
            if (currentTargets.Contains(hoverable))
                continue;
            hoverable?.OnCursorExit();
            cursorTargets.RemoveAt(i);
            LogDispatch("EXIT", hoverable, hitObject, hit.distance);
        }

        // Hit something with no IHoverable on it (HoverRegisterer.cs:49-52). The exit pass above
        // has already emptied the list, so there is nothing left to do.
        if (currentTargets.Count <= 0)
            return false;

        // ENTER pass (HoverRegisterer.cs:54-61).
        foreach (IHoverable target in currentTargets)
        {
            if (target == null || cursorTargets.Contains(target))
                continue;
            cursorTargets.Add(target);
            target.OnCursorEnter();
            LogDispatch("ENTER", target, hitObject, hit.distance);
        }

        return false;
    }

    /// <summary>
    /// Drain every live hover (HoverRegisterer.cs:65-76 / 80-91, the same block twice in the
    /// original). Silent and allocation-free when nothing is hovered, which is the common case
    /// for a laser that is off the board — so this can be called every frame.
    /// </summary>
    private static void ExitAll(List<IHoverable> cursorTargets, string reason)
    {
        if (cursorTargets.Count <= 0)
            return;

        foreach (IHoverable target in cursorTargets)
            target?.OnCursorExit();

        LogDrain(cursorTargets.Count, reason);
        cursorTargets.Clear();
    }

    // ---- diagnostics -------------------------------------------------------------------------

    /// <summary>
    /// One line per enter/exit DISPATCH — which pointer drove it, what it resolved to, the prop
    /// type, and which direction was dispatched. Rate-capped (see
    /// <see cref="MinLogIntervalSeconds"/>) so a moving laser cannot flood the log; the
    /// swallowed count is carried onto the next line so the reader is never lied to about
    /// how many transitions there were.
    ///
    /// <para>WHAT WOULD DISPROVE THE CLAIM IN THE CLASS DOC: if this line never appears while
    /// the laser is visibly on a water hex, the ray is not reaching the hover mask — the pick is
    /// fine (HexHover proves that separately) but <c>targetLayer</c> does not contain the prop's
    /// layer, or the prop has no collider on the same GameObject as its <c>UnityGameEditor*Prop</c>.
    /// If it appears with a plausible prop but no card is visible, the break is downstream, in
    /// <c>UIPropInfoPanel</c> / <c>WorldUI PropInfoSurface</c>, not here. If ENTER lines appear
    /// without a matching EXIT before the next ENTER on a different prop, the bookkeeping
    /// transcription is wrong.</para>
    /// </summary>
    private static void LogDispatch(string direction, IHoverable? target, GameObject? hitObject, float distance)
    {
        if (!TakeLogSlot(out int swallowed))
            return;

        VRLog.Info(Scope,
            $"hover {direction} — pointer {DescribePointer()} → {DescribeProp(hitObject)} " +
            $"[IHoverable {(target != null ? target.GetType().Name : "null")}] on hex {DescribeHex(hitObject)}, " +
            $"ray hit at {distance:0.00} m{Suffix(swallowed)}");
    }

    private static void LogDrain(int count, string reason)
    {
        if (!TakeLogSlot(out int swallowed))
            return;

        VRLog.Info(Scope,
            $"hover EXIT ×{count} (all) — {reason}; pointer {DescribePointer()}{Suffix(swallowed)}");
    }

    private static bool TakeLogSlot(out int swallowed)
    {
        swallowed = 0;
        float now = Time.unscaledTime;
        if (now < _nextLogTime)
        {
            _suppressed++;
            return false;
        }

        swallowed = _suppressed;
        _suppressed = 0;
        _nextLogTime = now + MinLogIntervalSeconds;
        return true;
    }

    private static string Suffix(int swallowed) =>
        swallowed > 0 ? $" (+{swallowed} dispatch line(s) suppressed by the {MinLogIntervalSeconds:0.00}s log cap)." : ".";

    private static string DescribePointer()
    {
        VRHand? hand = BoardPick.SourceHand;
        string source = BoardPick.Source switch
        {
            BoardPick.PickSource.Near => "FINGERTIP",
            BoardPick.PickSource.Far => "LASER",
            _ => "none"
        };
        return hand != null ? $"{source}({hand.Side})" : source;
    }

    /// <summary>
    /// The prop the hover resolved to: its instance name (which is the key the game itself
    /// matches on — <c>CObjectDifficultTerrain.InstanceName == difficultTerrain.name</c>,
    /// UIPropInfoPanel.cs:207) and its authored <c>ObjectImportType</c>.
    /// </summary>
    private static string DescribeProp(GameObject? go)
    {
        if (go == null)
            return "(no hit object)";
        try
        {
            var authored = go.GetComponent<UnityGameEditorObject>();
            return authored != null
                ? $"'{go.name}' ({authored.m_ObjectType})"
                : $"'{go.name}' (no UnityGameEditorObject — type unknown)";
        }
        catch
        {
            return $"'{go.name}'";
        }
    }

    /// <summary>
    /// The hex under the pointer. Props are not children of their tile, so the hover hit itself
    /// usually has no <c>TileBehaviour</c> above it; fall back to the board pick's own hit, which
    /// is cast on the hex-selection mask and IS the tile (BoardPick.ResolveCursorWorld uses the
    /// same lookup).
    /// </summary>
    private static string DescribeHex(GameObject? go)
    {
        try
        {
            TileBehaviour? tile = go != null ? go.GetComponentInParent<TileBehaviour>() : null;
            if (tile == null)
            {
                Collider? pickHit = BoardPick.HitCollider;
                tile = pickHit != null ? pickHit.GetComponentInParent<TileBehaviour>() : null;
            }
            if (tile == null || tile.m_ClientTile == null || tile.m_ClientTile.m_Tile == null)
                return "(unresolved)";
            return $"[{tile.m_ClientTile.m_Tile.m_ArrayIndex.X},{tile.m_ClientTile.m_Tile.m_ArrayIndex.Y}]";
        }
        catch
        {
            return "(unresolved)";
        }
    }
}
