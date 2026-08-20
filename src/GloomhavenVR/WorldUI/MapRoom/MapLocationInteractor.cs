using System.Collections.Generic;
using System.Reflection;
using Code.State;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP ROOM'S INPUT PATH — laser and fingertip on the campaign map's location icons
/// (worldmap-3d.md phase 4). Owned by <see cref="MapRoomDriver"/>, alive only while the room
/// stands.
///
/// <para>NO NEW AUTHORITY IS CREATED, AND THAT IS THE WHOLE DESIGN. A click here is
/// <c>ExecuteEvents.Execute(location.gameObject, …, pointerClickHandler)</c> — the identical
/// dispatch the game makes for a gamepad press (<c>MapLocation.OnGamepadClick</c>, decompiled
/// GH.Runtime/MapLocation.cs:266-269) and the identical handler a mouse click reaches
/// (<c>OnPointerClick</c>, :278). That handler calls <c>Select()</c> (:660), which gates on
/// <c>IsSelectable()</c> and on the game's own <c>m_OnClickAction</c> delegate. So every rule the
/// flat game applies — including whatever it does about who may pick a location in multiplayer —
/// applies here unchanged, because this is the same seam and not a shortcut past it. Nothing new
/// goes on the wire. (Same precedent as <c>WorldUI.ButtonCluster</c>, which cites the game's own
/// <c>BaseButtons.clickButton</c> for Ready/Undo/Skip.)</para>
///
/// <para>HOVER IS REPRODUCED, NOT SHARED, because the game's own hover driver cannot work in VR.
/// <c>MapLocationSelector.Update</c> raycasts the SCREEN CENTRE through <c>Camera.main</c>
/// (decompiled Assets.Script.AdventureMap/MapLocationSelector.cs:15,44) — and in VR the map camera
/// is frozen (the mod prefix-skips <c>CameraController.LateUpdate</c>), so screen centre points at
/// a fixed arbitrary spot on the map and it would fight every hover this class sets. It is
/// therefore prefixed off while the room stands
/// (<c>WorldUI.Patches.MapLocationSelectorGate</c>) and its logic reproduced here EXACTLY: the
/// same <c>OnPointerEnter</c>/<c>OnPointerExit</c> calls and the same
/// <c>StateMachine.Enter(LocationHover | WorldMap)</c> transitions, so the game's navigation state
/// machine sees the same sequence it always did and every downstream panel keeps working.</para>
///
/// <para>THE LAYER IS MEASURED, NOT HARD-CODED. The game's selector holds
/// <c>private readonly LayerMask _layerMask = 32768</c> (i.e. layer 15) — a private field, so
/// citing the number here would create a second copy that nothing keeps honest. Instead the mask
/// is built from the LIVE locations' own <c>gameObject.layer</c> on every rescan. If the game ever
/// moves them, this follows; if a scene has none, the mask is not touched at all.</para>
///
/// <para>LIFECYCLE. Locations are destroyed and respawned wholesale by
/// <c>MapChoreographer.InitMap</c> (a quest unlock, a city↔world switch, a travel animation), so
/// nothing may be cached across frames without a re-scan. Same cadence as
/// <see cref="MapIconLayer"/>'s icon cache, and every registration is undone in
/// <see cref="Release"/> — which <see cref="MapRoomDriver.StandDown"/> always reaches.</para>
/// </summary>
internal sealed class MapLocationInteractor
{
    private const string Scope = "MapRoom";

    /// <summary>Frames between location re-scans. Same cadence and same reason as
    /// <see cref="MapIconLayer"/>'s decal cache.</summary>
    private const int RescanIntervalFrames = 15;

    /// <summary>Cached reflection handle for <c>MapLocation._boxCollider</c> — the icon's own hit
    /// box, as opposed to <c>_snappingBoxCollider</c> (decompiled MapLocation.cs:49-54). Both are
    /// private [SerializeField]s and both are BoxColliders, so "take the first BoxCollider" would
    /// be a coin flip; the field name is the only thing that distinguishes them.</summary>
    private static FieldInfo? _boxColliderField;
    private static bool _boxColliderFieldMissing;

    private readonly List<MapLocation> _locations = new(64);
    private readonly List<MapLocationPoke> _pokes = new(64);
    private readonly List<MapLocation> _found = new(64);    // rescan scratch, reused
    private readonly List<MapLocation> _scratch = new(64);  // GetComponentsInChildren sink

    private int _scanFrame = int.MinValue;
    private int _maskInForce;
    private bool _maskTaken;
    private int _maskWasLeft;
    private int _maskWasRight;

    private MapLocation? _hover;
    private bool _reported;

    /// <summary>Locations registered on the most recent scan (log material).</summary>
    internal int RegisteredCount => _locations.Count;

    /// <summary>
    /// Per-frame upkeep while the map room stands. Rescans on the cadence, keeps the pick mask on
    /// both hands, resolves the hovered location from the shared ray pick and dispatches a click
    /// on the trigger edge.
    /// </summary>
    internal void Tick()
    {
        // THE `_scanFrame == int.MinValue` TERM IS LOAD-BEARING, NOT DEFENSIVE (ModBuild 179).
        // Without it the FIRST test is `Time.frameCount - int.MinValue`, which OVERFLOWS to a large
        // NEGATIVE number — so `>= RescanIntervalFrames` is false and the scan never runs, not once,
        // for the whole session. That is exactly what shipped in 178: no locations were ever found,
        // the pick mask was never narrowed, and the feature was silent about all of it.
        // MapIconLayer and MapRoomDriver.TickPredicate both special-case the sentinel for this
        // reason; this one did not.
        if (_scanFrame == int.MinValue || Time.frameCount - _scanFrame >= RescanIntervalFrames)
        {
            _scanFrame = Time.frameCount;
            Rescan();
        }

        // THE MASK IS OWNED UNCONDITIONALLY, INCLUDING WHEN THERE ARE NO LOCATIONS — see ApplyMask.
        ApplyMask();

        if (_locations.Count == 0)
        {
            SetHover(null, "no map locations in the scene");
            return;
        }

        // THE HOVER COMES FROM THE SHARED RAY PICK, not from a raycast of this class's own. The
        // pick is already computed once per hand per frame, it already carries the fan/board
        // occluder arbitration every other consumer honours, and using it is also what makes the
        // BEAM END ON THE ICON: RayInteractor clamps the drawn beam and places the reticle at its
        // own hit, so a second private raycast would hover a location the beam does not point at.
        MapLocation? want = PickFrom(VRHands.Primary) ?? PickFrom(OtherHand(VRHands.Primary));
        SetHover(want, "laser");

        if (_hover == null)
            return;
        VRHand? clicking = TriggerEdgeHand();
        if (clicking == null)
            return;
        // Claim the trigger WITHOUT moving the beam: the far-click half alone (see
        // RayInteractor.SuppressFarClick's own doc on why the two duties are separate).
        clicking.Ray.SuppressFarClick();
        Dispatch(_hover, $"{clicking.Side} trigger");
    }

    /// <summary>Undo everything: unregister the poke adapters, hand the pick mask back, and drop a
    /// live hover through the game's own exit path so no icon is left highlighted.</summary>
    internal void Release(string reason)
    {
        SetHover(null, $"released ({reason})");
        int hadPokes = _pokes.Count;
        for (int i = 0; i < _pokes.Count; i++)
        {
            MapLocationPoke poke = _pokes[i];
            if (poke == null)
                continue;
            VRInteractables.UnregisterPokeable(poke);
            Object.Destroy(poke);
        }
        _pokes.Clear();
        _locations.Clear();
        _scanFrame = int.MinValue;
        _reported = false;

        if (_maskTaken)
        {
            _maskTaken = false;
            VRHand? left = VRHands.Left;
            if (left != null)
                left.Ray.Mask = _maskWasLeft;
            VRHand? right = VRHands.Right;
            if (right != null)
                right.Ray.Mask = _maskWasRight;
            VRLog.Info(Scope, $"MAP ROOM location input released ({reason}) — {hadPokes} poke "
                              + "registration(s) dropped, both hands' pick masks handed back to "
                              + $"0x{_maskWasLeft:X8}/0x{_maskWasRight:X8}. Nothing on a MapLocation was "
                              + "modified; only this mod's own adapter components existed and they are gone.");
        }
        _maskInForce = 0;
    }

    // ---- scanning ---------------------------------------------------------------------------

    private void Rescan()
    {
        // TWO ROUTES, THE CHOREOGRAPHER'S FIRST. MapChoreographer instantiates every location under
        // m_VillagesParent / m_ScenariosParent (decompiled MapChoreographer.cs:601,611,635), which is
        // the same pair MapIconLayer collects its decals from — so asking the parents directly is
        // both the cheapest and the most faithful question. FindObjectsOfType is kept as the fallback
        // for a save/version that parents them elsewhere, exactly as the icon layer does.
        _found.Clear();
        global::MapChoreographer? choreo = MapRoomDriver.Choreographer;
        if (choreo != null)
        {
            CollectFrom(choreo.m_VillagesParent);
            CollectFrom(choreo.m_ScenariosParent);
        }
        int fromParents = _found.Count;
        if (_found.Count == 0)
        {
            MapLocation[] sweep = Object.FindObjectsOfType<MapLocation>();
            for (int i = 0; i < sweep.Length; i++)
            {
                if (sweep[i] != null && sweep[i].gameObject.activeInHierarchy)
                    _found.Add(sweep[i]);
            }
        }

        // Nothing changed? The common case by far — locations only churn on InitMap.
        if (_found.Count == _locations.Count && SameSet(_found))
        {
            ReportOnce(fromParents);
            return;
        }

        for (int i = 0; i < _pokes.Count; i++)
        {
            MapLocationPoke poke = _pokes[i];
            if (poke == null)
                continue;
            VRInteractables.UnregisterPokeable(poke);
            Object.Destroy(poke);
        }
        _pokes.Clear();
        _locations.Clear();

        int mask = 0;
        for (int i = 0; i < _found.Count; i++)
        {
            MapLocation loc = _found[i];
            if (loc == null)
                continue;
            _locations.Add(loc);
            mask |= 1 << loc.gameObject.layer;

            BoxCollider? box = HitBoxOf(loc);
            if (box == null)
                continue;
            MapLocationPoke poke = loc.gameObject.AddComponent<MapLocationPoke>();
            poke.Bind(this, loc);
            VRInteractables.RegisterPokeable(poke, box);
            _pokes.Add(poke);
        }
        _maskInForce = mask;

        _maskInForce = mask;
        _reported = false;   // the set changed — say so once more
        ReportOnce(fromParents);
    }

    private void CollectFrom(GameObject? parent)
    {
        if (parent == null)
            return;
        parent.GetComponentsInChildren(includeInactive: false, _scratch);
        for (int i = 0; i < _scratch.Count; i++)
        {
            if (_scratch[i] != null && !_found.Contains(_scratch[i]))
                _found.Add(_scratch[i]);
        }
    }

    /// <summary>
    /// ONE line per scan OUTCOME, and it is emitted for a count of ZERO too. ModBuild 178's scan
    /// never ran (an int.MinValue overflow) and said nothing about it, so the hardware round could
    /// only report "nothing happens" — the log has to be able to distinguish "found nothing",
    /// "found them but the click was refused" and "never looked".
    /// </summary>
    private void ReportOnce(int fromParents)
    {
        if (_reported)
            return;
        _reported = true;
        if (_locations.Count == 0)
        {
            VRLog.Warn(Scope, "MAP ROOM location input found NO MapLocation — neither under the "
                              + "choreographer's Villages/Scenarios parents nor in a scene-wide sweep. "
                              + "Laser and fingertip have nothing to hover, which is a real gap, not a "
                              + "quiet success. Pick mask is 0x0 (see the mask line): that is deliberate "
                              + "and keeps the window grab bars grabbable. If the map visibly HAS icons, "
                              + "compare against the MAP ROOM icons line — the icon layer counts DECALS "
                              + "and this counts MapLocation components; a disagreement between the two "
                              + "numbers is the next thing to chase.");
            return;
        }
        VRLog.Info(Scope, $"MAP ROOM location input armed — {_locations.Count} MapLocation(s) "
                          + $"({fromParents} via the choreographer's own Villages/Scenarios parents, "
                          + $"{_locations.Count - fromParents} via the scene-wide fallback), "
                          + $"{_pokes.Count} of them fingertip-pressable, pick mask 0x{_maskInForce:X8} "
                          + "MEASURED off their own gameObject.layer (the game's own selector holds the "
                          + "same value in a private field, which is why it is measured and not copied). "
                          + "A click is ExecuteEvents.pointerClickHandler on the real MapLocation — the "
                          + "same dispatch the game's gamepad path makes — so IsSelectable() and the "
                          + "game's own click action still decide, and nothing new goes on the wire.");
    }

    /// <summary>Set comparison, NOT index comparison: neither route guarantees an order, and
    /// comparing by position would tear down and rebuild every adapter on the rescan cadence
    /// forever. O(n²) over ~30 icons at 4 Hz — free.</summary>
    private bool SameSet(List<MapLocation> found)
    {
        for (int i = 0; i < found.Count; i++)
        {
            if (found[i] == null || !_locations.Contains(found[i]))
                return false;
        }
        return true;
    }

    private static BoxCollider? HitBoxOf(MapLocation loc)
    {
        if (_boxColliderFieldMissing)
            return loc.GetComponent<BoxCollider>();
        if (_boxColliderField == null)
        {
            _boxColliderField = typeof(MapLocation).GetField(
                "_boxCollider", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_boxColliderField == null)
            {
                _boxColliderFieldMissing = true;
                VRLog.Warn(Scope, "MapLocation._boxCollider not found by name — falling back to the "
                                  + "first BoxCollider on the object. If fingertip presses start landing "
                                  + "on the wrong footprint, this is why: the snapping collider and the "
                                  + "hit collider are both private BoxColliders and only the name tells "
                                  + "them apart.");
                return loc.GetComponent<BoxCollider>();
            }
        }
        return _boxColliderField.GetValue(loc) as BoxCollider ?? loc.GetComponent<BoxCollider>();
    }

    // ---- picking ----------------------------------------------------------------------------

    /// <summary>
    /// Own both hands' physics pick mask for as long as the room stands — AND OWN IT EVEN WHEN NO
    /// LOCATION WAS FOUND, in which case the mask is ZERO.
    ///
    /// <para>THIS IS NOT A DETAIL; IT IS WHY THE WINDOW GRAB BARS COULD NOT BE GRABBED (ModBuild
    /// 179). <c>RayInteractor.Mask</c> starts at <c>Physics.DefaultRaycastLayers</c> — nearly every
    /// layer — and the only other writer is <c>Board.BoardDriver.SyncRayMask</c>, which narrows it
    /// to the game's hex-selection layers and returns early unless a scenario <c>Controller</c> is
    /// alive. In the map room nothing narrowed it, so the pick hit the table, the room and the map
    /// itself. <c>RayGrabDriver</c> then refuses a bar grab whenever that pick is NEARER than the
    /// bar ("no grabbing through objects"), and the bars hang low, over the table — so the refusal
    /// fired on essentially every attempt. In a scenario the same test is safe only BECAUSE the
    /// mask is narrow there. A wide-open mask is not a neutral default; it is a promise that
    /// everything in the room is a pick target.</para>
    ///
    /// <para>Zero is therefore the correct value when there are no icons: in this room the ONLY
    /// physics pick targets that mean anything are the location icons. Everything else the player
    /// points at — window bars, window widgets — is served by <c>RayGrabDriver</c>/<c>RayUguiDriver</c>
    /// through their own geometric tests, which do not use this mask at all.</para>
    /// </summary>
    private void ApplyMask()
    {
        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        if (!_maskTaken)
        {
            _maskTaken = true;
            _maskWasLeft = left != null ? left.Ray.Mask.value : Physics.DefaultRaycastLayers;
            _maskWasRight = right != null ? right.Ray.Mask.value : Physics.DefaultRaycastLayers;
            VRLog.Info(Scope, $"MAP ROOM pick mask TAKEN: 0x{_maskWasLeft:X8}/0x{_maskWasRight:X8} → "
                              + $"0x{_maskInForce:X8} ({_locations.Count} location icon(s)). A mask of 0 "
                              + "is CORRECT here and not a failure: the icons are the only physics pick "
                              + "targets in this room, and window bars/widgets are served by their own "
                              + "geometric tests. Leaving it at Physics.DefaultRaycastLayers is what made "
                              + "RayGrabDriver refuse every bar grab in 178 — the pick hit the table in "
                              + "front of the bar and the 'no grabbing through objects' rule fired.");
        }
        // Re-asserted every frame rather than latched: the hands are rebuilt on a skin change and
        // a fresh RayInteractor starts on Physics.DefaultRaycastLayers. Board.BoardDriver.SyncRayMask
        // is the only other writer and it returns early unless Controller.Instance is alive — a
        // scenario-scene object that cannot exist here (decompiled Controller.cs:73,80).
        if (left != null && left.Ray.Mask.value != _maskInForce)
            left.Ray.Mask = _maskInForce;
        if (right != null && right.Ray.Mask.value != _maskInForce)
            right.Ray.Mask = _maskInForce;
    }

    private static VRHand? OtherHand(VRHand? hand) =>
        hand == null ? null : hand == VRHands.Left ? VRHands.Right : VRHands.Left;

    private static MapLocation? PickFrom(VRHand? hand)
    {
        if (hand == null || !hand.HasPose)
            return null;
        if (!hand.Ray.TryGetPick(out PickPose pick) || !pick.HasHit || pick.HitCollider == null)
            return null;
        return pick.HitCollider.GetComponentInParent<MapLocation>();
    }

    private static VRHand? TriggerEdgeHand()
    {
        VRHand? primary = VRHands.Primary;
        if (primary != null && primary.HasPose && primary.TriggerDown)
            return primary;
        VRHand? other = OtherHand(primary);
        return other != null && other.HasPose && other.TriggerDown ? other : null;
    }

    // ---- hover + click ----------------------------------------------------------------------

    /// <summary>
    /// Move the hover, reproducing <c>MapLocationSelector.Update</c>'s transitions exactly: enter
    /// the location and push <c>LocationHover</c> with its payload; leave it and fall back to
    /// <c>WorldMap</c>. Both halves are guarded — this runs on a scene the game is free to tear
    /// down under us.
    /// </summary>
    internal void SetHover(MapLocation? want, string why)
    {
        if (ReferenceEquals(want, _hover))
            return;

        MapLocation? had = _hover;
        _hover = want;

        try
        {
            if (had != null)
                had.OnPointerExit(null);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MapLocation.OnPointerExit threw ({why}): {ex.Message}");
        }

        try
        {
            if (want != null)
            {
                want.OnPointerEnter(null);
                StateMachineEnterHover(want);
            }
            else if (had != null)
            {
                StateMachineEnterWorldMap();
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MapLocation hover enter threw ({why}): {ex.Message}");
        }
    }

    /// <summary>Drop the hover only if <paramref name="loc"/> is the one currently held.</summary>
    internal void ClearHoverIf(MapLocation loc, string why)
    {
        if (ReferenceEquals(loc, _hover))
            SetHover(null, why);
    }

    private static void StateMachineEnterHover(MapLocation loc)
    {
        if (!Singleton<UINavigation>.IsInitialized)
            return;
        UINavigation nav = Singleton<UINavigation>.Instance;
        if (nav == null || nav.StateMachine == null)
            return;
        nav.StateMachine.Enter(CampaignMapStateTag.LocationHover, new MapLocationStateData(loc));
    }

    private static void StateMachineEnterWorldMap()
    {
        if (!Singleton<UINavigation>.IsInitialized)
            return;
        UINavigation nav = Singleton<UINavigation>.Instance;
        if (nav == null || nav.StateMachine == null)
            return;
        nav.StateMachine.Enter(CampaignMapStateTag.WorldMap);
    }

    /// <summary>
    /// The click. One dispatch, through the game's own handler chain — see the class doc on why
    /// this and not <c>Select()</c> directly.
    /// </summary>
    internal void Dispatch(MapLocation loc, string source)
    {
        if (loc == null)
            return;
        EventSystem? es = EventSystem.current;
        var data = new PointerEventData(es!) { button = PointerEventData.InputButton.Left };
        try
        {
            ExecuteEvents.Execute(loc.gameObject, data, ExecuteEvents.pointerClickHandler);
            VRLog.Info(Scope, $"MAP ROOM location CLICK on '{loc.name}' ({source}) — dispatched as "
                              + "ExecuteEvents.pointerClickHandler, i.e. exactly a left mouse click. "
                              + "MapLocation.OnPointerClick → Select() decides from here (IsSelectable "
                              + "plus the game's own m_OnClickAction); if nothing happened, it refused, "
                              + "and it would have refused the same click in the flat game.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MAP ROOM location click on '{loc.name}' threw: {ex}");
        }
    }
}

/// <summary>
/// Fingertip adapter for one <see cref="MapLocation"/> — added by
/// <see cref="MapLocationInteractor"/> and destroyed with it. Holds no state of its own: hover and
/// click both route back through the interactor so the laser and the finger can never disagree
/// about which icon is hovered.
/// </summary>
internal sealed class MapLocationPoke : MonoBehaviour, IPokeable
{
    private MapLocationInteractor? _owner;
    private MapLocation? _location;

    internal void Bind(MapLocationInteractor owner, MapLocation location)
    {
        _owner = owner;
        _location = location;
    }

    public void OnPokeEnter(VRHand hand)
    {
        if (_owner != null && _location != null)
            _owner.SetHover(_location, $"{hand.Side} fingertip");
    }

    public void OnPokeExit(VRHand hand)
    {
        // Only drop a hover this adapter actually OWNS — the laser may already have moved on to
        // another icon this frame, and an unconditional clear would blank a hover that is
        // legitimately live. This is the whole reason the two input paths share one hover field.
        if (_owner != null && _location != null)
            _owner.ClearHoverIf(_location, $"{hand.Side} fingertip left");
    }

    public void OnPoke(VRHand hand)
    {
        if (_owner != null && _location != null)
            _owner.Dispatch(_location, $"{hand.Side} fingertip");
    }
}
