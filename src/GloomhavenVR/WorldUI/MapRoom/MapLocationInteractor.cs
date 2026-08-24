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
/// <para>THE ROOM'S SHARED SELECTION RIDES THAT SAME SEAM AND NO OTHER (report 13, ModBuild 226).
/// <see cref="AdoptSelection"/> is reached from <c>Net.RemoteMapRoom</c> when a peer's selection
/// EDGE arrives, and all it can do is the two things a local hand can do: dispatch the same
/// <c>pointerClickHandler</c>, or call the game's own <c>Deselect()</c>. So "everyone follows" is
/// still the game deciding, one location at a time, and an edge that names a location this client
/// cannot resolve does nothing at all.</para>
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
/// <para>WHAT IS HOVERED IS WHAT IS DRAWN (ModBuild 188). Up to 187 the hover target was the game's
/// authored <c>MapLocation._boxCollider</c> — and that collider is not the icon the player sees.
/// The icon is re-drawn for the head camera by <see cref="MapIconLayer"/> from the DECAL's
/// transform, onto the parchment's top plane; the collider is authored separately per location
/// (<c>UIInfoTools.GetLocationConfig</c>), sits at the location's own height, and is left untouched
/// when the game rescales the icon art (<c>ShouldOverrideLocationScale</c>, decompiled
/// MapLocation.cs:418-421). Two independent rectangles in two different frames, diverging PER ICON
/// — which is both halves of the 187 report at once: the icons whose art outgrew their box could
/// not be hovered at all, and the cards that did appear were anchored on a point that is not where
/// their symbol is. <see cref="MapIconHoverPads"/> puts a mod-owned hit box on the drawn quad and
/// this class prefers it (<see cref="PickFrom"/>) and anchors on it
/// (<see cref="TryHoverAnchor"/>).</para>
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

    /// <summary>The DRAWN icons' own hit boxes — see <see cref="MapIconHoverPads"/> for why the
    /// game's authored collider is not the same rectangle as the icon the player sees, and why
    /// that one fact produces both halves of the ModBuild 187 hardware report.</summary>
    private readonly MapIconHoverPads _pads = new();

    /// <summary>Hit buffer for the pad-preferring pick. Sixteen is far more than the number of
    /// location colliders any single ray can cross; a full buffer only means the arbitration
    /// chooses among the first sixteen, which is still a location either way.</summary>
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    /// <summary>Census of the last scan, for <see cref="ReportOnce"/> (log material only).</summary>
    private int _withQuest;
    private int _withPad;
    private int _withGameBox;
    private string _kinds = string.Empty;

    private int _scanFrame = int.MinValue;
    private int _maskInForce;
    private bool _maskTaken;
    private int _maskWasLeft;
    private int _maskWasRight;

    private MapLocation? _hover;
    private bool _reported;

    /// <summary>Locations registered on the most recent scan (log material).</summary>
    internal int RegisteredCount => _locations.Count;

    // ---- what the wire reads off this class (ModBuild 222, records 20 + 21) -----------------
    // Deliberately RAW: these hand out MapLocation references and a change counter, and nothing
    // about identity, hashing or the wire appears in this file. The key derivation lives in
    // Net/RemoteMapRoom.cs so the dependency keeps pointing Net → WorldUI, which is the direction
    // the rest of the project already has (Net/RemoteStorySync calls into ModalFallback, never the
    // other way round).

    /// <summary>The location this client's own pointer is on, or null. This is the fact request
    /// 2c ("Die mouseover Infotafeln sollen synchronisiert werden") publishes.</summary>
    internal MapLocation? Hover => _hover;

    /// <summary>
    /// The location this client has CLICKED and whose quest window is standing — the pre-commit
    /// SELECTION, which the game does not sync in either direction.
    ///
    /// <para>The COMMITTED selection is a different fact and is already on the game's own wire,
    /// host-authoritatively and keyed by <c>Location.ID</c>
    /// (<c>UIMapMultiplayerController.ConfirmSelectedLocation</c> →
    /// <c>SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ, …)</c> carrying a
    /// <c>LocationToken</c>, received by <c>MapChoreographer.ProxySelectedLocation</c>). A second
    /// channel for THAT is still forbidden.</para>
    ///
    /// <para><b>WHAT CHANGED (report 13, user verbatim): "Welches Icon ausgewählt ist wird nicht
    /// richtig synchronisiert. Es soll nur eine einzige Auswahl geben die global alle sehen."</b>
    /// Up to that ruling this value was published as presentation only and a receiver was forbidden
    /// to turn it into a selection. It is now the room's ONE selection: record 20 carries it as an
    /// EDGE and a receiver adopts it through <see cref="AdoptSelection"/>, i.e. through the game's
    /// own click/deselect seams, which is not new authority (see that method). The reason this is
    /// not the forbidden second channel is that the game's SelectQuest action is the HOST's
    /// CONFIRMED quest arriving as a confirm PROMPT — it is not "which icon is selected", it never
    /// travels client→host, and a client's own <c>Select()</c> is purely local.</para>
    /// </summary>
    internal MapLocation? Staged => _selected;

    /// <summary>
    /// ADOPT THE ROOM'S SELECTION — the receiving half of record 20's selection edge.
    ///
    /// <para><paramref name="loc"/> null means "nothing is selected anywhere", i.e. a deselection.
    /// Anything else is the location a peer selected, already resolved against THIS client's live
    /// map by <c>RemoteMapRoom.TryResolveKey</c>.</para>
    ///
    /// <para><b>NO NEW AUTHORITY, AND THAT IS THE ONLY REASON THIS IS ALLOWED TO EXIST.</b> The two
    /// halves are the two the local player's own hands already reach: <see cref="Dispatch"/>, which
    /// is <c>ExecuteEvents.pointerClickHandler</c> on the real <c>MapLocation</c> — the identical
    /// dispatch the game's gamepad path makes — and <see cref="Deselect"/>, which is the game's own
    /// <c>MapLocation.Deselect()</c>. Both still pass <c>IsSelectable()</c> and the game's own
    /// <c>m_OnClickAction</c>, so a refusal here is the game's answer and not ours, and the ordinary
    /// deselect rules (<see cref="TickDeselect"/>) keep owning the selection afterwards.</para>
    ///
    /// <para>IDEMPOTENT BY DESIGN: adopting the selection this client already has does nothing at
    /// all, which is what makes an edge that arrives twice (a duplicated packet, two peers naming
    /// the same node) cost one comparison rather than a second click.</para>
    /// </summary>
    /// <returns>True when something was actually driven — log material for the caller, which owns
    /// the "once per change, naming the node" line.</returns>
    internal bool AdoptSelection(MapLocation? loc, string why)
    {
        if (ReferenceEquals(loc, _selected))
            return false;

        // ORDER MATTERS: drop the old selection FIRST. Two selected locations is a state the game
        // has no concept of — UIQuestPopupManager holds exactly one selectedQuest — and clicking
        // the new one while the old one still stands would leave the old icon's own highlight and
        // quest marker up with nothing to take them down.
        if (_selected != null)
            Deselect($"adopted from the room: {why}");
        if (loc == null)
            return true;
        Dispatch(loc, $"adopted from the room ({why})");
        return true;
    }

    /// <summary>How many live locations the last rescan registered, and the reference at an index.
    /// Handed out as a count + indexer rather than as the list so nobody can hold the list itself
    /// across an <c>InitMap</c> that replaces every entry in it.</summary>
    internal int LocationCount => _locations.Count;

    /// <inheritdoc cref="LocationCount"/>
    internal MapLocation? LocationAt(int index) =>
        index >= 0 && index < _locations.Count ? _locations[index] : null;

    /// <summary>
    /// Bumped once every time <see cref="Rescan"/> REPLACES the location set — a quest unlock, a
    /// city↔world switch, a travel animation, i.e. every <c>MapChoreographer.InitMap</c>.
    ///
    /// <para>THIS IS WHAT MAKES A KEY→LOCATION CACHE SAFE, and it is not optional. Locations are
    /// destroyed and respawned wholesale, so a cache that is not rebuilt points at dead objects;
    /// and the two collectors here refuse to depend on order, so nothing else about the set is
    /// stable enough to diff against. A consumer rebuilds when this number changes and never
    /// otherwise.</para>
    /// </summary>
    internal int ScanGeneration => _scanGeneration;

    private int _scanGeneration;

    /// <summary>
    /// Where a placard belongs for ANY location — the top-centre of its drawn icon plus the same
    /// real-metre gap <see cref="TryHoverAnchor"/> uses, with the same fallback onto the game's
    /// authored hit box when this location has no drawn-icon pad.
    ///
    /// <para>Shares <see cref="TryHoverAnchor"/>'s implementation exactly, because a remote
    /// player's placard and the local hover card must sit at the same height over the same icon —
    /// two anchors that agree by construction rather than by two numbers matching.</para>
    /// </summary>
    internal bool TryAnchorFor(MapLocation? loc, out Vector3 world)
    {
        world = default;
        if (loc == null)
            return false;
        float scale = Rig.RigTarget.Current != null
            ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
            : 1f;
        float lift = HoverCardLiftMeters * scale;
        if (_pads.TryAnchor(loc, out Vector3 padTop))
        {
            world = new Vector3(padTop.x, padTop.y + lift, padTop.z);
            return true;
        }
        BoxCollider? box = HitBoxOf(loc);
        float top = box != null ? box.bounds.max.y : loc.transform.position.y;
        world = new Vector3(loc.transform.position.x, top + lift, loc.transform.position.z);
        return true;
    }

    /// <summary>
    /// Where a hover card belongs right now: the world point just above the hovered icon, or null
    /// while nothing is hovered. Read by <c>ModalFallback</c> to fly the game's own preview popup
    /// over the symbol instead of floating it as a movable window (user ruling: <i>"Bei Mouseovers
    /// über ein Symbol soll es über dem Symbol entsprechend fliegen ohne ein separates Fenster zu
    /// sein das man verschieben kann (immer zum Kopf gedreht) und nur solange der Mouseover
    /// anhält."</i>).
    ///
    /// <para>THE ANCHOR IS THE DRAWN ICON, and until ModBuild 188 it was not. The old answer mixed
    /// two frames that have no reason to agree: <c>loc.transform.position.xz</c> for the horizontal
    /// and the game hit box's <c>bounds.max.y</c> for the vertical. The icon the player actually
    /// SEES is neither — <see cref="MapIconLayer"/> re-draws it as a quad at the DECAL's x/z, on the
    /// parchment's top plane, because a deferred decal contributes nothing to the forward head
    /// camera. So the card was placed above a point that is offset horizontally by whatever the
    /// decal's local offset is, and vertically by however far that location's own terrain height is
    /// from the map's top face. Per icon, differently, which is exactly the report. The pad is
    /// built ON that quad (<see cref="MapIconHoverPads"/>), so its top face is the icon's top face
    /// by construction.</para>
    ///
    /// <para>Plus a fixed real-metre gap carried by the rig scale, so a large location marker is
    /// not covered by its own card at any rig scale.</para>
    /// </summary>
    internal bool TryHoverAnchor(out Vector3 world)
    {
        world = default;
        MapLocation? loc = _hover;
        if (loc == null)
            return false;
        float scale = Rig.RigTarget.Current != null
            ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
            : 1f;
        float lift = HoverCardLiftMeters * scale;
        if (_pads.TryAnchor(loc, out Vector3 padTop))
        {
            world = new Vector3(padTop.x, padTop.y + lift, padTop.z);
            return true;
        }
        // FALLBACK — this location has no drawn-icon pad (no Decalicious type, or its decal has not
        // been instantiated yet). The game's authored hit box is the only thing left to anchor on;
        // it is the pre-188 answer, and the pad census line names how many locations are in this
        // state so a mis-seated card is attributable rather than mysterious.
        BoxCollider? box = HitBoxOf(loc);
        float top = box != null ? box.bounds.max.y : loc.transform.position.y;
        world = new Vector3(loc.transform.position.x, top + lift, loc.transform.position.z);
        return true;
    }

    /// <summary>Gap between the hovered icon's top and the bottom of its hover card, real metres.</summary>
    private const float HoverCardLiftMeters = 0.045f;

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

        // The pads follow the icons every frame (the game rescales a highlighted location's mesh,
        // and the choreographer animates locations in and out) — see MapIconHoverPads.Tick.
        _pads.Tick(MapRoomDriver.ParchmentRenderer);

        if (_locations.Count == 0)
        {
            SetHover(null, "no map locations in the scene");
            return;
        }

        // THE HOVER RIDES ON THE SHARED PICK'S RAY AND ITS ARBITRATION — same origin, same
        // direction, same mask, same fan/board occluder limit — but it resolves the hit ITSELF, so
        // that a location's DRAWN ICON always outranks any authored hit box on the same ray (see
        // PickFrom). The shared pick keeps deciding what the beam and reticle do, and because the
        // pads are ordinary colliders on the locations' own layer it clamps the beam on the pad —
        // i.e. on the icon — for free.
        MapLocation? want = PickFrom(VRHands.Primary, out string how);
        if (want == null)
            want = PickFrom(OtherHand(VRHands.Primary), out how);
        SetHover(want, "laser", how);

        TickHoverVerdict();
        TickDeselect();

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
        // Same reason as in Rescan: the set is gone, so every reference-keyed cache built off it
        // must rebuild rather than point at destroyed objects.
        unchecked { _scanGeneration++; }
        _pads.Release(reason);
        MapHoverVerdict.Reset();
        _scanFrame = int.MinValue;
        _reported = false;
        _hoverFrame = int.MinValue;
        _verdictDone = false;
        _refusedLoggedAt = float.NegativeInfinity;
        // The next room is entitled to its own capital-terms measurement: the guildmaster HUD, the
        // city's unlock state and the option locks are all things that change between rooms.
        _capitalTermsLogged.Clear();
        _capitalForced = false;

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
        // THE PAD TERM IS NOT DEFENSIVE: the parchment can still be resolving when the first scan
        // runs, and a scan that found its locations then never runs its body again. Without this,
        // "no parchment on the first scan" would mean "no drawn-icon pads for the whole session",
        // silently — the same shape of bug as 178's frozen scan cadence.
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        bool padsMissing = _pads.PadCount == 0 && _found.Count > 0 && parchment != null;
        if (_found.Count == _locations.Count && SameSet(_found) && !padsMissing)
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
        _pads.Begin(parchment);

        int mask = 0;
        _withQuest = _withPad = _withGameBox = 0;
        int villages = 0, scenarios = 0, bosses = 0, hqs = 0, stores = 0, other = 0;
        for (int i = 0; i < _found.Count; i++)
        {
            MapLocation loc = _found[i];
            if (loc == null)
                continue;
            _locations.Add(loc);
            mask |= 1 << loc.gameObject.layer;

            switch (loc.MapLocationType)
            {
                case MapLocation.EMapLocationType.Village: villages++; break;
                case MapLocation.EMapLocationType.Scenario: scenarios++; break;
                case MapLocation.EMapLocationType.Boss: bosses++; break;
                case MapLocation.EMapLocationType.Headquarters: hqs++; break;
                case MapLocation.EMapLocationType.Store: stores++; break;
                default: other++; break;
            }
            if (loc.LocationQuest != null)
                _withQuest++;

            // The DRAWN icon first (its pad shares the location's layer, so the mask above already
            // covers it), the game's authored hit box second. The fingertip is registered on
            // whichever one the laser will prefer, so the two input paths can never point at
            // different rectangles of the same icon.
            BoxCollider? pad = _pads.Build(loc);
            if (pad != null)
                _withPad++;
            BoxCollider? box = HitBoxOf(loc);
            if (box != null)
                _withGameBox++;
            BoxCollider? pokeBox = pad != null ? pad : box;
            if (pokeBox == null)
                continue;
            MapLocationPoke poke = loc.gameObject.AddComponent<MapLocationPoke>();
            poke.Bind(this, loc);
            VRInteractables.RegisterPokeable(poke, pokeBox);
            _pokes.Add(poke);
        }
        _maskInForce = mask;
        // THE SET WAS REPLACED. Everything keyed on a MapLocation reference — above all the
        // wire's key→location cache (Net/RemoteMapRoom) — must rebuild now: InitMap destroys and
        // respawns every location, so a cache that survives this points at dead objects.
        unchecked { _scanGeneration++; }
        _kinds = $"{scenarios} Scenario, {villages} Village, {bosses} Boss, {hqs} Headquarters, "
                 + $"{stores} Store, {other} None";

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
        // THE COVERAGE CENSUS (ModBuild 188). "Some symbols show a mouseover and some do not" is a
        // PER-KIND claim, so the log has to state, before any hover happens, how many of each kind
        // there are, how many carry the quest a preview is made of, and how many have a hit target
        // shaped like the icon the player sees. A hover that produces nothing is then read against
        // this line instead of against a guess.
        VRLog.Info(Scope, $"MAP ROOM location census — kinds: {_kinds}. {_withQuest}/{_locations.Count} "
                          + $"carry a LocationQuest (the thing a preview card is MADE of: with none, the "
                          + $"game previews only an available Headquarters or Store). {_withPad}/"
                          + $"{_locations.Count} got a DRAWN-ICON pad and {_withGameBox}/{_locations.Count} "
                          + $"have the game's own authored hit box; {_pads.NoDecalCount} location(s) had no "
                          + $"usable decal to measure. Icon plane y={_pads.PlaneY:F2} (parchment top "
                          + $"{_pads.ParchmentTop:F2} + 0.10), plane resolved={_pads.HavePlane}. The pads are "
                          + "the hover target and the hover card's anchor: what you see is what you hover, "
                          + "and the card sits on the icon's own top face.");
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

    /// <summary>Ray length, real metres — the same bound <c>RayInteractor.MaxDistanceMeters</c>
    /// applies to the shared pick (a private const there, copied as a value so this pick can never
    /// reach further than the beam the player sees).</summary>
    private const float MaxPickMeters = 20f;

    /// <summary>
    /// Which location this hand is pointing at, and HOW it was reached.
    ///
    /// <para>THE ARBITRATION IS THE POINT: <b>the nearest DRAWN ICON wins, and an authored hit box
    /// only decides when no icon is on the ray at all.</b> The two are different rectangles — see
    /// <see cref="MapIconHoverPads"/> — and the union of them is what gives every icon a hover
    /// (which the game's authored boxes alone did not), while the ordering is what stops one
    /// location's oversized box from stealing the hover of the neighbour whose icon the beam is
    /// actually on. A single nearest-hit query cannot express that, which is why this asks for all
    /// hits along the ray and chooses, instead of reading <c>pick.HitCollider</c>.</para>
    ///
    /// <para>Everything else is taken from the shared pick verbatim so the beam and the hover can
    /// never disagree: the same origin and direction (the OpenXR aim pose), the same
    /// <c>Ray.Mask</c>, and the same length — clamped by the hand's own
    /// <c>SolidOccluderDistance</c>, so a raised card fan or the control board still occludes the
    /// map exactly as it occludes it for every other consumer.</para>
    /// </summary>
    private MapLocation? PickFrom(VRHand? hand, out string how)
    {
        how = "no hand";
        if (hand == null || !hand.HasPose)
            return null;
        if (!hand.Ray.TryGetPick(out PickPose pick))
        {
            how = "the ray is stood down";
            return null;
        }

        float limit = Mathf.Min(hand.Ray.SolidOccluderDistance,
                                MaxPickMeters * Mathf.Max(hand.WorldScale, 0.0001f));
        int n = Physics.RaycastNonAlloc(pick.Origin, pick.Direction, _hits, limit, hand.Ray.Mask);

        MapLocation? padHit = null, boxHit = null;
        float padDist = float.PositiveInfinity, boxDist = float.PositiveInfinity;
        Collider? boxCollider = null;
        for (int i = 0; i < n; i++)
        {
            Collider c = _hits[i].collider;
            if (c == null)
                continue;
            float d = _hits[i].distance;
            if (_pads.TryLocation(c, out MapLocation padLoc))
            {
                if (d < padDist)
                {
                    padDist = d;
                    padHit = padLoc;
                }
                continue;
            }
            // Anything else on this mask that BELONGS to a location — its authored hit box. This is
            // a containment question on purpose (the box may hang under the location), and it is
            // only ever consulted after every pad has lost.
            MapLocation? owner = c.GetComponentInParent<MapLocation>();
            if (owner != null && d < boxDist)
            {
                boxDist = d;
                boxHit = owner;
                boxCollider = c;
            }
        }

        if (padHit != null)
        {
            how = $"its DRAWN ICON pad at {padDist:F1} world units ({hand.Side} laser)";
            return padHit;
        }
        if (boxHit != null)
        {
            how = $"the game's own collider '{boxCollider!.name}' at {boxDist:F1} world units "
                  + $"({hand.Side} laser) — no drawn-icon pad was on this ray";
            return boxHit;
        }
        how = n > 0
            ? $"{n} collider(s) on the ray, none of them a location"
            : "nothing on the ray";
        return null;
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
    /// <param name="want">The location to hover, or null to drop the hover.</param>
    /// <param name="why">What moved the hover (log material).</param>
    /// <param name="how">WHICH hit target the pointer reached it through — the drawn-icon pad or
    /// the game's own authored collider. Half of "why did this icon show no card" is "did the pick
    /// even land, and on what", so it travels WITH the verdict instead of in a second line somebody
    /// has to correlate. The default is the fingertip path, which has exactly one answer.</param>
    internal void SetHover(MapLocation? want, string why, string how = "the fingertip")
    {
        if (ReferenceEquals(want, _hover))
            return;

        MapLocation? had = _hover;
        _hover = want;
        // Arm the verdict: WHY this icon did or did not get a card is read a few frames from now,
        // once the game's own show/hide animation has settled. See MapHoverVerdict.
        _hoverFrame = want != null ? Time.frameCount : int.MinValue;
        _hoverHow = how;
        _verdictDone = false;

        // UNDO A FORCED HIGHLIGHT FIRST, AND ONLY ONE THIS CLASS FORCED. The game's own
        // OnPointerExit would in fact undo it too — UnHighlight is NOT gated on IsSelectable
        // (MapLocation.cs:370-377), so it is the one half of the pair the capital could always
        // reach — but the undo is done explicitly and unconditionally here so that a highlight
        // this mod put up can never survive the pointer leaving. The second pass through
        // UnHighlight below is idempotent: it re-writes the same two false flags and re-runs the
        // same Highlight(false, …).
        if (_capitalForced)
        {
            _capitalForced = false;
            if (had != null)
                CapitalHover(had, active: false);
        }

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
                // THE CAPITAL, AND NOTHING ELSE. OnPointerEnter above is a no-op for it because
                // IsSelectable() is false on one term this mod itself broke — see
                // CapitalRouteAllowed. For every other location this test costs one enum compare
                // and returns.
                if (CapitalRouteAllowed(want) && CapitalHover(want, active: true))
                    _capitalForced = true;
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

    private int _hoverFrame = int.MinValue;
    private string _hoverHow = "?";
    private bool _verdictDone;

    /// <summary>
    /// Read the verdict for the live hover once the game has had time to answer it — see
    /// <see cref="MapHoverVerdict"/> for the gate-by-gate reasoning and for why this line exists at
    /// all. Cheap: one frame comparison per tick until it fires, then nothing.
    /// </summary>
    private void TickHoverVerdict()
    {
        if (_verdictDone || _hover == null || _hoverFrame == int.MinValue)
            return;
        if (Time.frameCount - _hoverFrame < MapHoverVerdict.VerdictDelayFrames)
            return;
        _verdictDone = true;
        MapHoverVerdict.Evaluate(_hover, _hoverHow);
    }

    /// <summary>
    /// DESELECTION (ModBuild 183). User: <i>"Ich will ein bereits ausgewähltes icon/Ort wieder
    /// abwählen können indem ich mit Trigger sonst irgendwo hindrücke. Wird das entsprechende
    /// Fenster geschlossen kommt es einem Abwählen gleich."</i>
    ///
    /// <para>Two triggers, both routed through the game's own <c>MapLocation.Deselect</c> — the
    /// exact counterpart of the <c>Select()</c> a click runs, so it passes the same
    /// <c>IsSelectable()</c> and <c>m_OnClickAction</c> guards and cannot desynchronise anything:
    /// <list type="number">
    /// <item>a trigger pull that lands ON THE MAP OR THE TABLE and on no location — "press
    ///   somewhere else <b>on the map</b>";</item>
    /// <item>the quest popup that the selection opened being gone. Closing that window IS the
    ///   deselection in his model, so the selection follows the window rather than the window
    ///   being expected to follow a selection nobody can see.</item>
    /// </list></para>
    ///
    /// <para>TRIGGER (1) WAS TOO WIDE, AND THE USER PAID FOR IT (ModBuild 191). Up to 190 the test
    /// was <c>_hover == null</c> — i.e. ANY trigger edge anywhere with no icon under the ray. User:
    /// <i>"Statt das man mit trigger überall die ausgewählte Quest wieder abwählt soll das nur
    /// passieren wenn man auf der map bzw auf dem Holztisch wo die map draufklickt hinklickt, nicht
    /// wenn man außerhalb von da irgendwo mit dem trigger drückt. Aktuell wählt man die quest so ab
    /// weil ich versucht habe das Fenster zu verschieben mit dem trigger."</i> Grabbing a floated
    /// window's bar, pressing a table button, or pointing at the forest all satisfied "no icon under
    /// the ray", so all three deselected. The absence of a location is NOT the presence of the map.
    /// <see cref="RayOnMapOrTable"/> now asks the positive question, and trigger (2) is untouched —
    /// closing the quest window IS a deselection, which is a standing ruling from ModBuild 183.</para>
    /// </summary>
    private void TickDeselect()
    {
        if (_selected == null)
            return;

        // ModBuild 243 — THE MAP IS SEALED, SO THIS MOD MUST NOT DRIVE A DESELECT THROUGH IT.
        //
        // AdventureMapUIManager.LockOptionsInteraction (:363-386) ends with
        // lockMapInteractionMask.SetActive(lockInteractionRequests.Count > 0) — a full-screen
        // raycast blocker. While it is up the flat game cannot reach ANY MapLocation, so
        // MapLocation.Deselect() and the QuestManager.OnMapLocationQuestSelected(quest, false)
        // -> ShowLogScreen chain hanging off it are unreachable there. This class dispatches onto
        // the MapLocation objects directly and never crosses the mask, so without this guard the VR
        // player drives a transition the flat player cannot — which is exactly the ModBuild 242
        // report: one table click after the quest confirm re-opened the quest list in the middle of
        // the point of no return (second_logs/LogOutput.log:5977 -> :5978).
        //
        // HOVER IS NOT GATED. The same log has twelve quest-preview cards during that lock and
        // reading them is the point of the room. Only the DESELECT is refused, and with it trigger
        // (2) below ("its quest window was closed IS a deselection") for the length of the lock —
        // which is correct for the same reason: during the lock the flat game cannot close that
        // window by hand either.
        if (Singleton<AdventureMapUIManager>.IsInitialized)
        {
            AdventureMapUIManager mapUi = Singleton<AdventureMapUIManager>.Instance;
            if (mapUi != null && mapUi.IsLocked)
                return;
        }

        // (2) the window that the selection opened has gone. GRACE FIRST: the popup takes a few
        // frames to come up after the click, and testing it immediately would deselect the location
        // the same instant it was selected — the classic "the absence of a thing that has not
        // arrived yet is not its departure".
        if (Time.unscaledTime - _selectedAt < SelectionGraceSeconds)
            return;
        if (!QuestPopupOpen())
        {
            Deselect("its quest window was closed — closing that window IS a deselection");
            return;
        }

        // (1) a trigger pull that landed ON THE MAP OR THE TABLE, and on no location.
        VRHand? clicking = TriggerEdgeHand();
        if (clicking == null)
            return;
        if (_hover != null)
            return; // an icon is under the ray: that press is a SELECTION, never a deselection
        if (RayOnMapOrTable(clicking, out string what, out float distance))
            Deselect($"{clicking.Side} trigger pulled with the ray on {what} at {distance:F1} world "
                     + "units and on no location icon");
        else
            NoteDeselectRefused(clicking, what);
    }

    // ---- "is the ray on the map or on the table it lies on?" ---------------------------------

    /// <summary>
    /// How far past the parchment's own edge still counts as THE TABLE, real metres. The map is
    /// seated to read 1.20 m across (<c>MapRoomSeat.TargetMapWidthMeters</c>) and the game's own
    /// bar buttons stand 0.075 m outside its near edge on that table
    /// (<c>MapButtonRail</c>), so a 0.20 m rim is a plausible tabletop margin that still stops
    /// well short of the 0.45 m standoff the player is seated at
    /// (<c>MapRoomSeat.EdgeStandoffMeters</c>) — pointing at your own feet is not pointing at the
    /// table. REAL METRES, multiplied by the hand's world scale at the point of use; this room runs
    /// at ~198 world units per metre and mixing the two has shipped as a bug here before.
    /// </summary>
    private const float TableRimMeters = 0.20f;

    /// <summary>How far BELOW the parchment's underside the table slab reaches, real metres. The
    /// parchment is 0.13 world units thin (~0.6 mm at rig scale); without a body under it a ray
    /// arriving at a shallow angle from across the table would pass under the map's own box and
    /// miss a surface the player is plainly pointing at.</summary>
    private const float TableDropMeters = 0.12f;

    /// <summary>Seconds between REFUSED lines. One per few seconds is enough to prove the gate ran;
    /// per-press would flood a log during ordinary window dragging.</summary>
    private const float RefusedLogIntervalSeconds = 3f;

    private float _refusedLoggedAt = float.NegativeInfinity;

    /// <summary>
    /// Is this hand's ray on the campaign map, or on the table it lies on?
    ///
    /// <para>IDENTIFIED BY OBJECT, NOT BY LAYER. The map is
    /// <c>MapRoomDriver.ParchmentRenderer</c> — the very renderer <see cref="MapParchment"/>
    /// acquired by material name and holds the MapUnlit override on, i.e. a thing this mod owns a
    /// reference to and can print. No layer is assumed (this project has been burned by that), and
    /// no <c>GetComponentInParent</c> is used as an identity test (it answers "related to an X",
    /// never "IS an X").</para>
    ///
    /// <para>THE TABLE IS THE MAP'S OWN TOP PLANE, WIDENED — and that is a modelling decision worth
    /// stating, because the room contains no table object this mod owns: the MAP SCENE REPORT lists
    /// exactly ONE renderer under the map roots (the parchment), and the bundle's
    /// <c>MapTable.prefab</c> is not spawned at runtime. What the player sees as wood under the
    /// parchment is environment geometry belonging to another lane. So "the table" is defined here
    /// the same way <see cref="MapRoomSeat"/> and <c>MapButtonRail</c> already define it — the
    /// parchment's top plane — expanded by <see cref="TableRimMeters"/> horizontally and
    /// <see cref="TableDropMeters"/> downward, giving a slab that a ray aimed at the wood around
    /// the map enters and a ray aimed at the forest, the sky or the player's feet does not.</para>
    ///
    /// <para>A BEAM THAT BELONGS TO SOMETHING ELSE IS NEVER "on the table", however far the ray
    /// would travel if nothing stopped it. A floated window hangs in the air OVER the table, so the
    /// geometric test alone would count every window drag as a press on the map — which is the
    /// user's report verbatim. Two positive claims are honoured first: <c>RayUgui.HasHit</c> (this
    /// hand's beam is on a converted uGUI panel — a floated window's own widgets) and
    /// <c>Ray.HasFreshUiHit</c> (some mod driver has clamped the beam to a UI surface or claimed
    /// this trigger — a window grab bar via <c>RayGrabDriver</c>, a table button cap via
    /// <c>MapButtonRail</c>, the flat screen). Both are published by their owners for exactly this
    /// arbitration; neither is inferred.</para>
    /// </summary>
    /// <param name="what">What the ray was judged to be on — log material, always set.</param>
    /// <param name="distance">Distance along the aim ray to the map/table surface, world units.</param>
    private bool RayOnMapOrTable(VRHand hand, out string what, out float distance)
    {
        distance = float.PositiveInfinity;
        if (hand.RayUgui.HasHit)
        {
            Canvas? canvas = hand.RayUgui.HoveredCanvas;
            GameObject? widget = hand.RayUgui.Hovered;
            what = $"the floated window panel '{(canvas != null ? canvas.name : "<unnamed canvas>")}'"
                   + (widget != null ? $" (widget '{widget.name}')" : "")
                   // WORLD UNITS, not metres: RayUguiDriver compares this against
                   // OcclusionEpsilonMeters * worldScale, i.e. it is a world-unit distance. At the
                   // map room's ~198 units/m a "0.30" here is 1.5 mm, and calling it metres is the
                   // exact unit slip this project has shipped before.
                   + $" at {hand.RayUgui.HitDistance:F2} world units";
            return false;
        }
        if (hand.Ray.HasFreshUiHit)
        {
            what = "a mod-owned UI surface the beam is clamped to — a floated window's grab bar, a "
                   + "table button cap or the flat screen (whoever owns it published a UI hit or "
                   + "claimed this trigger)";
            return false;
        }

        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
        {
            // SAFE FAILURE = NEVER DESELECT. Without the parchment there is no measured map and no
            // measured table, so there is no positive evidence the press was on either. Refusing
            // costs the player one extra press somewhere useful; guessing yes is precisely the
            // behaviour reported as a bug.
            what = "nothing identifiable — the map room holds no parchment renderer this frame, so "
                   + "neither the map nor the table can be measured and the selection is kept";
            return false;
        }
        if (!hand.HasPose || !hand.Ray.TryGetPick(out PickPose pick))
        {
            what = "nothing — this hand's ray is stood down";
            return false;
        }

        float scale = Mathf.Max(hand.WorldScale, 0.0001f);
        float limit = Mathf.Min(hand.Ray.SolidOccluderDistance, MaxPickMeters * scale);
        Bounds map = parchment.bounds;
        Bounds slab = map;
        // Bounds.Expand adds HALF of what it is given to each side, so the rim is doubled going in.
        slab.Expand(new Vector3(TableRimMeters * scale * 2f, 0f, TableRimMeters * scale * 2f));
        slab.SetMinMax(new Vector3(slab.min.x, slab.min.y - TableDropMeters * scale, slab.min.z),
                       slab.max);

        var ray = new Ray(pick.Origin, pick.Direction);
        if (!slab.IntersectRay(ray, out float t))
        {
            what = "neither the map nor the table — the aim ray never enters the map's own slab "
                   + $"(centre {slab.center}, size {slab.size}, world units)";
            return false;
        }
        if (t > limit)
        {
            what = $"the map/table slab, but {t:F1} world units away — past this ray's limit of "
                   + $"{limit:F1} (the raised card fan or the control board is in front of it), so it "
                   + "does not count";
            return false;
        }

        Vector3 p = ray.GetPoint(t);
        bool onMap = p.x >= map.min.x && p.x <= map.max.x && p.z >= map.min.z && p.z <= map.max.z;
        float outsideWorld = Mathf.Max(
            Mathf.Max(map.min.x - p.x, p.x - map.max.x),
            Mathf.Max(map.min.z - p.z, p.z - map.max.z));
        what = onMap
            ? $"the MAP PARCHMENT '{parchment.name}' at {p}"
            : $"the TABLE around the map at {p} — {Mathf.Max(outsideWorld, 0f) / scale * 100f:F1} cm "
              + $"(real) outside the parchment's own edge, within the {TableRimMeters * 100f:F0} cm rim";
        distance = t;
        return true;
    }

    /// <summary>
    /// The REFUSAL line — rate-limited, and it exists because of what the next report will say.
    /// The two possible follow-ups are "it still deselects everywhere" and "it never deselects any
    /// more", and only a line that names WHAT THE RAY WAS ON separates them: if this line appears
    /// while the player is pressing on the map, the surface test is wrong; if it never appears while
    /// they drag a window and the selection still drops, something else is deselecting.
    /// </summary>
    private void NoteDeselectRefused(VRHand hand, string what)
    {
        if (Time.unscaledTime - _refusedLoggedAt < RefusedLogIntervalSeconds)
            return;
        _refusedLoggedAt = Time.unscaledTime;
        VRLog.Info(Scope, $"MAP ROOM deselect REFUSED — {hand.Side} trigger was pulled while "
                          + $"'{(_selected != null ? _selected.name : "<none>")}' is selected, but the ray "
                          + $"was on {what}, not on the map or the table, so the selection was LEFT "
                          + "ALONE. User ruling (ModBuild 191): only a press on the map parchment or on "
                          + "the table it lies on deselects; dragging a window, pressing a button or "
                          + "pointing at the room must not. Rate-limited to one line per "
                          + $"{RefusedLogIntervalSeconds:F0} s.");
    }

    private void Deselect(string why)
    {
        MapLocation? sel = _selected;
        _selected = null;
        if (sel == null)
            return;
        try
        {
            sel.Deselect();
            VRLog.Info(Scope, $"MAP ROOM location DESELECTED '{sel.name}' — {why}. Routed through the "
                              + "game's own MapLocation.Deselect, the exact counterpart of the Select() a "
                              + "click runs, so its guards still decide and nothing goes on the wire.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MapLocation.Deselect threw: {ex.Message}");
        }
    }

    /// <summary>Is the quest popup a selection opens still up? Cached by type; Unity-null revives
    /// the lookup after a scene rebuild.</summary>
    private bool QuestPopupOpen()
    {
        if (_questPopup == null)
            _questPopup = Object.FindObjectOfType<UIQuestPopup>();
        if (_questPopup == null)
            return false;
        // activeInHierarchy, not a UIWindow lookup: UIQuestPopup is a plain MonoBehaviour
        // (decompiled UIQuestPopup.cs:17) and the game shows/hides the object itself.
        return _questPopup.gameObject.activeInHierarchy;
    }

    private MapLocation? _selected;
    private float _selectedAt;
    private UIQuestPopup? _questPopup;

    /// <summary>How long after a selection the quest popup is allowed to still be absent.</summary>
    private const float SelectionGraceSeconds = 1.0f;

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

    // ---- THE CAPITAL (Gloomhaven) -------------------------------------------------------------

    /// <summary>
    /// THE ONE TERM THIS MOD BROKE, AND NOTHING ELSE (ModBuild 196). User: <i>"Bei dem Gloomhaven
    /// Symbol auf der Weltkarte (die Stadt) funktioniert das Overlay nicht mit dem Laser. Alle
    /// anderen Symbole reagieren, Gloomhaven nicht. […] und wenn ich draufdrücke soll es die Karte
    /// auf die Gloomhaven-Stadtkarte umschalten, so wie im Flat-Spiel."</i>
    ///
    /// <para>THE PICK LANDS — this is not an input gap. The ModBuild 195 log has the capital hovered
    /// through its drawn-icon pad at 168.7 world units and refused downstream:
    /// <c>selectable=False, highlighted=False, hasPreview=True</c>. Both halves of the report hang
    /// off that ONE method: hover is <c>OnPointerEnter</c> → <c>CanHighlight()</c> →
    /// <c>IsSelectable()</c> (decompiled MapLocation.cs:253-262, :310-332) and click is
    /// <c>OnPointerClick</c> → <c>Select()</c> → <c>IsSelectable()</c> (:278-291, :660-668).</para>
    ///
    /// <para>For a Headquarters with no <c>LocationQuest</c> (MapLocation.cs:325) that method reads
    /// <c>IsCampaign &amp;&amp; type==Headquarters &amp;&amp; CurrentMode==WorldMap &amp;&amp;
    /// (IsAvailable(City) || IsChoosingLinkedQuestOption())</c>, and
    /// <c>UIGuildmasterHUD.IsAvailable</c> (UIGuildmasterHUD.cs:753-759) is
    /// <c>window.IsVisible &amp;&amp; modes[mode].IsUnlocked &amp;&amp;
    /// disableOptionsRequests.Count == 0</c>. <c>UIWindow.IsVisible</c> is merely
    /// <c>m_CanvasGroup.alpha &gt; 0</c> (UIWindow.cs:305-315) — a statement about FLAT-SCREEN
    /// presentation. In the map room the guildmaster HUD is deliberately not floated, because its VR
    /// surface is the table rail (<see cref="MapButtonRail"/>), so its alpha says nothing whatsoever
    /// about whether the city is reachable. <b>That one term is conceded here and only that one.</b>
    /// <c>modes[City].IsUnlocked</c> ("the city is unlocked", CityMapMode.cs:6-20, which also honours
    /// the tutorial's BuyItem step) and <c>disableOptionsRequests</c> ("options are locked while a
    /// result processes") are genuine game state and are measured and honoured unchanged. If either
    /// of those is what is false, this route REFUSES and the log says which — see
    /// <see cref="ReportCapitalTerms"/>.</para>
    ///
    /// <para>AND <c>IsSelectable()</c> ITSELF IS MEASURED FIRST: if it returns true the ordinary path
    /// already works and this does nothing at all. Nothing here can change what a location that the
    /// game is willing to select does.</para>
    /// </summary>
    private bool CapitalRouteAllowed(MapLocation loc)
    {
        if (_capitalRouteStoodDown || loc == null)
            return false;
        if (loc.MapLocationType != MapLocation.EMapLocationType.Headquarters)
            return false;
        try
        {
            // GH.Runtime is publicized at build time, so every private member below is a direct,
            // compile-checked access — a game update that renames one breaks the BUILD instead of
            // silently standing a term down. The runtime proof that this works for GH.Runtime's
            // privates is two screens up: Rescan reads MapChoreographer.m_VillagesParent /
            // m_ScenariosParent the same way, and the 195 log shows all 41 locations arriving
            // through them.
            if (loc.IsSelectable())
                return false;   // the ordinary path works for this location — never touch it

            MapRuleLibrary.State.CMapState? map = MapRuleLibrary.Adventure.AdventureState.MapState;
            bool campaign = map != null && map.IsCampaign;
            UIGuildmasterHUD? hud = Singleton<UIGuildmasterHUD>.IsInitialized
                ? Singleton<UIGuildmasterHUD>.Instance
                : null;
            bool worldMap = hud != null && hud.CurrentMode == EGuildmasterMode.WorldMap;
            bool cityUnlocked = hud != null
                                && hud.modes != null
                                && hud.modes.TryGetValue(EGuildmasterMode.City, out GuildmasterMode city)
                                && city != null
                                && city.IsUnlocked;
            int optionLocks = hud != null && hud.disableOptionsRequests != null
                ? hud.disableOptionsRequests.Count
                : -1;
            bool hudVisible = hud != null && hud.window != null && hud.window.IsVisible;

            bool allowed = campaign && worldMap && cityUnlocked && optionLocks == 0;
            ReportCapitalTerms(loc, allowed, campaign, worldMap, cityUnlocked, optionLocks, hudVisible);
            return allowed;
        }
        catch (System.Exception ex)
        {
            // NEVER FAIL OPEN. A member that is no longer there (or a Singleton that threw) means the
            // terms could not be measured, and an unmeasured rule is not a passed rule.
            _capitalRouteStoodDown = true;
            VRLog.Warn(Scope, "MAP ROOM capital route STOOD DOWN — the game-rule terms behind "
                              + $"MapLocation.IsSelectable could not be measured: {ex.Message}. "
                              + "CONSEQUENCE: the Gloomhaven city symbol goes back to doing nothing on "
                              + "hover and on click, exactly as in ModBuild 195. That is the safe "
                              + "failure: this route exists only to concede UIGuildmasterHUD's "
                              + "CanvasGroup alpha, and it may not concede a rule it cannot read.");
            return false;
        }
    }

    /// <summary>
    /// ONE line per map-room session naming WHICH of the measured terms was false. The whole fix
    /// rests on the claim that the only broken term is the HUD window's alpha; if a future round
    /// finds it is not, this line says so on the first hover instead of costing another build.
    /// </summary>
    private void ReportCapitalTerms(MapLocation loc, bool allowed, bool campaign, bool worldMap,
                                    bool cityUnlocked, int optionLocks, bool hudVisible)
    {
        string terms = $"IsCampaign={campaign}, CurrentMode==WorldMap={worldMap}, "
                       + $"modes[City].IsUnlocked={cityUnlocked}, disableOptionsRequests="
                       + (optionLocks < 0 ? "unreadable" : optionLocks.ToString())
                       + $", window.IsVisible={hudVisible}";
        // ONE LINE PER DISTINCT MEASUREMENT, not one per session. Two of these terms are
        // transient — disableOptionsRequests fills while a result processes, and the city's unlock
        // can change mid-session — so a single first-hover line could record a refusal the room
        // then grew out of and never correct itself. The set is bounded by the four terms'
        // combinations, so this cannot flood.
        if (!_capitalTermsLogged.Add((allowed ? "armed|" : "refused|") + terms))
            return;
        if (allowed)
        {
            VRLog.Info(Scope, $"MAP ROOM capital route ARMED for '{loc.name}' — MapLocation.IsSelectable "
                              + $"is false while every GAME rule behind it holds ({terms}). The only "
                              + "failing term is UIGuildmasterHUD's own window alpha, i.e. "
                              + "UIWindow.IsVisible, and that is a statement about flat-screen "
                              + "presentation this mod deliberately owns: the HUD is not floated in the "
                              + "map room because its VR surface is the table rail (MapButtonRail). So "
                              + "that ONE term is conceded and no other. Hover reproduces "
                              + "OnPointerEnter's own three lines, and a press calls the game's own "
                              + "MapChoreographer.OnMapLocationSelect — the very delegate installed as "
                              + "m_OnClickAction — which opens the city map and returns false, so the "
                              + "capital is still never SELECTED and still gets no info panel. That is "
                              + "the flat game's behaviour, not an invention."
                              + (hudVisible
                                  ? " NOTE: window.IsVisible measured TRUE here, so the alpha term was "
                                    + "NOT the blocker this time — IsSelectable must be failing on "
                                    + "something this line cannot see (a Store branch cast, or a "
                                    + "changed rule). Read this line first next round."
                                  : string.Empty));
        }
        else
        {
            VRLog.Info(Scope, $"MAP ROOM capital route REFUSED for '{loc.name}' — MapLocation.IsSelectable "
                              + $"is false and so is a GAME rule behind it ({terms}). Only the HUD "
                              + "window's alpha may be conceded here; 'the city is not unlocked' and "
                              + "'options are locked while a result processes' are real state and are "
                              + "honoured. The symbol therefore stays inert, and correctly so — the flat "
                              + "game would refuse the same click.");
        }
    }

    /// <summary>
    /// Reproduce what <c>MapLocation.OnPointerEnter</c> / <c>UnHighlight</c> do once
    /// <c>IsSelectable()</c> has passed — and NOTHING more (MapLocation.cs:253-262, :370-377).
    ///
    /// <para>The enter direction is the game's three lines verbatim: ask the highlight delegate,
    /// and only if it agrees set <c>m_MouseInCollider</c> / <c>m_IsHighlighted</c> and call the
    /// private <c>Highlight(active, isSelected)</c>. That single call IS the whole visible response —
    /// node scale × 1.2, the highlight material swap, <c>OnMapLocationHighlight</c> again (the game
    /// double-calls it too, :255 then :553) and <c>UpdateMarkers</c> (:525-555). The delegate is
    /// <c>MapChoreographer.OnMapLocationHighlight</c>, PUBLIC at MapChoreographer.cs:1304, and for a
    /// Headquarters on the world map it refuses only while the map is initialising or moving
    /// (:1388-1393) — a rule worth keeping, which is why it is asked rather than bypassed.</para>
    ///
    /// <para>The exit direction asks the delegate for fidelity but puts the state back down
    /// REGARDLESS of its answer. A highlight this mod forced up must never outlive the pointer: if
    /// the choreographer happened to be mid-move at that instant the game's own <c>UnHighlight</c>
    /// would leave the capital enlarged for the rest of the session, and unlike a normal location
    /// nothing else here would ever take it down again. <c>Highlight(false, …)</c> is safe in that
    /// state — it re-enters the same refusing delegate, which early-outs.</para>
    /// </summary>
    private bool CapitalHover(MapLocation loc, bool active)
    {
        if (loc == null)
            return false;
        global::MapChoreographer? choreo = MapRoomDriver.Choreographer;
        try
        {
            bool agreed = choreo != null && choreo.OnMapLocationHighlight(loc, active);
            if (active && !agreed)
            {
                VRLog.Debug(Scope, $"MAP ROOM capital hover on '{loc.name}' was refused by "
                                   + "MapChoreographer.OnMapLocationHighlight (map initialising, party "
                                   + "moving, or no choreographer) — left alone, exactly as the game's "
                                   + "own OnPointerEnter would leave it.");
                return false;
            }
            loc.m_MouseInCollider = active;
            loc.m_IsHighlighted = active;
            loc.Highlight(active, loc.IsSelected);
            return true;
        }
        catch (System.Exception ex)
        {
            _capitalRouteStoodDown = true;
            VRLog.Warn(Scope, $"MAP ROOM capital hover threw on '{loc.name}' ({(active ? "enter" : "exit")}): "
                              + $"{ex.Message}. The capital route is stood down for the rest of this "
                              + "session — the symbol reverts to doing nothing rather than being left "
                              + "half-highlighted.");
            return false;
        }
    }

    /// <summary>True while this hover's highlight was forced by <see cref="CapitalHover"/>, so the
    /// exit undoes only what it put up.</summary>
    private bool _capitalForced;

    /// <summary>Measurements already reported by <see cref="ReportCapitalTerms"/> this room
    /// session — one line per distinct outcome, not one per session.</summary>
    private readonly HashSet<string> _capitalTermsLogged = new();

    /// <summary>Set when a member behind the route could not be reached or a call threw. Static: a
    /// game version that moved one of these will not un-move it on the next room.</summary>
    private static bool _capitalRouteStoodDown;

    /// <summary>
    /// The click. One dispatch, through the game's own handler chain — see the class doc on why
    /// this and not <c>Select()</c> directly.
    /// </summary>
    internal void Dispatch(MapLocation loc, string source)
    {
        if (loc == null)
            return;

        // THE CAPITAL TAKES THE GAME'S OWN CLICK DELEGATE, NOT A SYNTHESISED CLICK. A
        // pointerClickHandler here would reach Select(), which gates on the same IsSelectable() that
        // is refusing this location, so it would do nothing — which is ModBuild 195's report. What the
        // flat game runs on this symbol is MapChoreographer.OnMapLocationSelect (MapChoreographer.cs:
        // 1220), the delegate installed as m_OnClickAction at :603/:2897: for a Headquarters on the
        // world map it calls OpenCityMap() and RETURNS FALSE (:1263-1274). Returning false is why
        // Select() never sets m_IsSelected — the capital is correctly never "selected" and never gets
        // an info panel; it just switches the map. Calling it is therefore not a shortcut past a
        // guard, it IS the guarded path: OnMapLocationSelect re-checks m_Initialised, m_IsMoving,
        // MovingToLocation, IsCampaign and CurrentMode itself before it does anything.
        if (CapitalRouteAllowed(loc))
        {
            global::MapChoreographer? choreo = MapRoomDriver.Choreographer;
            if (choreo == null)
            {
                VRLog.Warn(Scope, $"MAP ROOM capital click on '{loc.name}' ({source}) could not run — no "
                                  + "MapChoreographer this frame. Nothing was dispatched.");
                return;
            }
            try
            {
                choreo.OnMapLocationSelect(loc, active: true);
                // Deliberately NOT recorded as _selected: this location is never selected (the
                // delegate returns false before m_IsSelected is set), so arming TickDeselect on it
                // would chase a selection that does not exist.
                VRLog.Info(Scope, $"MAP ROOM capital CLICK on '{loc.name}' ({source}) — routed through "
                                  + "MapChoreographer.OnMapLocationSelect(active: true), the game's own "
                                  + "m_OnClickAction delegate, because MapLocation.Select() gates on the "
                                  + "same IsSelectable() that only the HUD window's alpha is failing. For "
                                  + "a Headquarters on the world map that delegate opens the CITY MAP and "
                                  + "returns false, so no selection and no info panel is created — the "
                                  + "flat game does exactly this. The map will rebuild its locations, and "
                                  + "the next rescan (≤15 frames) picks them up.");
            }
            catch (System.Exception ex)
            {
                VRLog.Warn(Scope, $"MAP ROOM capital click on '{loc.name}' threw: {ex}");
            }
            return;
        }

        EventSystem? es = EventSystem.current;
        var data = new PointerEventData(es!) { button = PointerEventData.InputButton.Left };
        try
        {
            ExecuteEvents.Execute(loc.gameObject, data, ExecuteEvents.pointerClickHandler);
            _selected = loc;   // what a later "press somewhere else" deselects
            _selectedAt = Time.unscaledTime;
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
