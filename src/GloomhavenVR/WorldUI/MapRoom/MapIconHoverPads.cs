using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// A HIT TARGET SHAPED LIKE THE ICON THE PLAYER CAN ACTUALLY SEE (ModBuild 188).
///
/// <para>THE PROBLEM THIS EXISTS FOR, stated as the two measurements that disagree. In the map
/// room the visible location icon is NOT the game's own decal — a Decalicious deferred decal
/// contributes nothing to a forward camera, so <see cref="MapIconLayer"/> re-draws each one as a
/// textured quad for the head camera at
/// <c>pos = (decal.position.x, parchment.bounds.max.y + 0.10, decal.position.z)</c> with the
/// footprint <c>decal.lossyScale.xz</c> at the decal's own yaw (MapIconLayer.Tick). The thing the
/// laser could hit, meanwhile, was the game's authored <c>MapLocation._boxCollider</c>: centred on
/// <c>transform.position + locationConfig.ColliderCenter</c>, sized <c>locationConfig.ColliderSize</c>
/// (decompiled MapLocation.cs:233-250, fed from <c>UIInfoTools.GetLocationConfig(location.ID)</c> at
/// :391-393, and NOT touched at all when that config is null), sitting at the location's own height
/// on the 3D map rather than on the parchment's top plane.</para>
///
/// <para>Nothing keeps those two rectangles equal, and the game itself pulls them apart on purpose:
/// <c>GlobalSettings.AdventureLocationMaterialSettings.ShouldOverrideLocationScale</c> rescales
/// <c>MeshParent</c> — i.e. the decal, i.e. the DRAWN icon — per location mesh
/// (MapLocation.cs:418-421) and leaves <c>_boxCollider</c> exactly where it was. So per icon, and
/// only per icon, the drawn art can be larger than its hit box (you point at the icon you see and
/// hit nothing: <b>no mouseover, for that symbol only</b>) or smaller than it (you hover from
/// beside the icon). Both halves of the hardware report follow from that one fact, and the second
/// one — the vertical frame — is why the card that DID appear was not sitting on its symbol: the
/// old anchor mixed <c>loc.transform.position.xz</c> with <c>box.bounds.max.y</c>, two different
/// frames, neither of which is where the icon is drawn.</para>
///
/// <para>THE FIX IS TO PICK WHAT IS DRAWN. One thin, mod-owned box collider per location, placed
/// at exactly the quad <see cref="MapIconLayer"/> draws — same centre, same yaw, same footprint —
/// on the LOCATION'S OWN LAYER so the existing pick mask (measured off that same layer,
/// <see cref="MapLocationInteractor"/>) sees it with no new plumbing, and so every consumer of the
/// shared ray pick keeps its arbitration: the beam clamps on the pad, the reticle sits on the icon,
/// the fan/board occluders still win when they are nearer. The pad is also the hover card's anchor,
/// which makes "the card sits above the symbol" true BY CONSTRUCTION rather than by two numbers
/// agreeing.</para>
///
/// <para>THE GAME'S OWN COLLIDER IS NOT REMOVED FROM THE PICTURE — the union is deliberate. A pad
/// can only ADD reach (an icon that could not be hovered becomes hoverable); dropping the authored
/// box would REMOVE reach from icons whose box is the generous one, which nobody asked for. The
/// arbitration is stated once, in <c>MapLocationInteractor.PickFrom</c>: the nearest PAD wins, and
/// an authored hit box only decides when no pad is on the ray at all. That ordering is what stops
/// one location's oversized box from stealing the hover of the neighbour whose icon the player is
/// pointing at.</para>
///
/// <para>NOTHING IS WRITTEN TO A GAME OBJECT. The pads are separate scene-root GameObjects owned
/// by this class; the locations are only READ (their layer, and their decal's transform).
/// <see cref="Release"/> destroys every one of them and is reached from
/// <c>MapLocationInteractor.Release</c>, i.e. from <c>MapRoomDriver.StandDown</c>.</para>
/// </summary>
internal sealed class MapIconHoverPads
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// Lift above the parchment's top face, world units. COPIED AS A VALUE from
    /// <c>MapIconLayer.IconLiftWorld</c> (a private const in a file this lane does not own) for the
    /// one reason that matters: the pad has to be coplanar with the quad that layer draws, or the
    /// card would be anchored to a plane the icon is not on. Both numbers are printed in the pad
    /// report below, so a future divergence shows up as a measurement rather than as a mystery.
    /// </summary>
    private const float IconLiftWorld = 0.10f;

    /// <summary>
    /// Pad thickness as a fraction of the icon's SHORTER footprint edge. The drawn icon is a flat
    /// quad with no thickness at all; a zero-thickness collider is not hittable, and a thick one
    /// would start catching rays that pass beside the icon on their way to something else. A third
    /// of the short edge is enough for any grazing angle a seated player can produce and still
    /// leaves the pad thinner than the gap between two neighbouring icons.
    /// </summary>
    private const float PadThicknessFraction = 0.35f;

    /// <summary>Smallest footprint edge a pad is built for, world units. A decal scaled to nothing
    /// is a location the game is hiding; giving it a hit box would hover an invisible icon.</summary>
    private const float MinPadEdgeWorld = 0.05f;

    /// <summary>
    /// The scale factor <c>MapLocation.Highlight</c> puts on <c>MeshParent</c> while a location is
    /// highlighted (<c>c_HighlightedNodeScaleFactor</c>, decompiled MapLocation.cs:121, applied at
    /// :545-551). The decal is a child of <c>MeshParent</c> (MapLocation.cs:401), so its
    /// <c>lossyScale</c> — the footprint this class measures — GROWS BY THIS FACTOR the moment the
    /// hover starts. Dividing it back out keeps the pad the same size hovered or not: without it
    /// the hover target would inflate 20% as soon as it was acquired and shrink again as soon as it
    /// was lost, which is a latch that makes a hover both harder to leave and able to reach over a
    /// neighbour's icon while it lasts.
    /// </summary>
    private const float HighlightedNodeScaleFactor = 1.2f;

    /// <summary>The Decalicious <c>Decal</c> type, reached by name — it lives in an unreferenced
    /// assembly, exactly as <see cref="MapIconLayer"/> reaches it.</summary>
    private static System.Type? _decalType;
    private static bool _decalTypeMissing;

    private readonly Dictionary<Collider, MapLocation> _byCollider = new(64);
    private readonly List<MapLocation> _padLocations = new(64);
    private readonly List<BoxCollider> _padColliders = new(64);
    private readonly List<Transform> _padDecals = new(64);
    private readonly List<GameObject> _padObjects = new(64);

    private float _planeY;
    private bool _havePlane;
    private float _parchmentTop;

    /// <summary>Pads standing right now (log material).</summary>
    internal int PadCount => _padObjects.Count;

    /// <summary>Locations that got NO pad on the last rebuild, i.e. whose drawn icon could not be
    /// located (log material — this is the count that explains a coverage gap).</summary>
    internal int NoDecalCount { get; private set; }

    /// <summary>The icon plane the pads were built on (parchment top + lift), world Y.</summary>
    internal float PlaneY => _planeY;

    /// <summary>True while a parchment was available to define the icon plane.</summary>
    internal bool HavePlane => _havePlane;

    /// <summary>Parchment top the plane was derived from (log material).</summary>
    internal float ParchmentTop => _parchmentTop;

    /// <summary>
    /// Start a rebuild: drop every pad and re-derive the icon plane from the live parchment. The
    /// plane is a single Y for the whole map because that is how the icons are DRAWN — the icon
    /// layer flattens every decal onto <c>parchment.bounds.max.y + IconLiftWorld</c>, so a card
    /// anchored to a location's own height on the relief would float above or below its symbol by
    /// however much that location's terrain differs from the map's top.
    /// </summary>
    internal void Begin(MeshRenderer? parchment)
    {
        DestroyPads();
        NoDecalCount = 0;
        _havePlane = parchment != null;
        _parchmentTop = parchment != null ? parchment.bounds.max.y : 0f;
        _planeY = _parchmentTop + IconLiftWorld;
    }

    /// <summary>
    /// Build the pad for one location and return its collider, or null when this location's drawn
    /// icon cannot be found (no <c>Decal</c> type at all, no decal under the location yet, or a
    /// footprint scaled to nothing). A null return is not an error: the caller falls back to the
    /// game's own authored hit box and SAYS SO in the census line.
    /// </summary>
    internal BoxCollider? Build(MapLocation loc)
    {
        if (loc == null || !_havePlane)
            return null;
        Transform? decal = FindDecal(loc);
        if (decal == null)
        {
            NoDecalCount++;
            return null;
        }

        Vector3 size = FootprintOf(decal, loc);
        if (size.x < MinPadEdgeWorld || size.z < MinPadEdgeWorld)
        {
            NoDecalCount++;
            return null;
        }

        var go = new GameObject($"GloomhavenVR.MapIconPad_{loc.name}");
        // The LOCATION'S layer, not a layer of our own: the pick mask is MEASURED off
        // loc.gameObject.layer (MapLocationInteractor.Rescan), so a pad that shares it is visible
        // to every hand's pick with no second mask to keep in sync. Layer 15 in the shipping game —
        // and the only game code that raycasts it is MapLocationSelector, which the map room
        // prefixes off (WorldUI.Patches.MapLocationSelectorGate), so these pads cannot be seen by
        // the game at all.
        go.layer = loc.gameObject.layer;
        var col = go.AddComponent<BoxCollider>();
        // Solid, not a trigger: Physics.queriesHitTriggers is a project-wide setting this mod does
        // not own, and a pick target that vanishes because someone else flipped it is not a pick
        // target. Nothing here has a Rigidbody, so a solid collider still touches no game physics.
        col.isTrigger = false;
        col.size = size;

        _padObjects.Add(go);
        _padColliders.Add(col);
        _padLocations.Add(loc);
        _padDecals.Add(decal);
        _byCollider[col] = loc;
        PosePad(_padObjects.Count - 1);
        return col;
    }

    /// <summary>
    /// Re-pose every pad from its decal, every frame. The map does not move, but the icons do: the
    /// game rescales <c>MeshParent</c> on highlight and the choreographer animates locations in and
    /// out. Thirty transform writes and no allocation, and it removes the whole class of "the pad
    /// is where the icon WAS at the last rescan" bugs — the pad is where the icon is being drawn
    /// THIS frame, which is the only claim this class makes.
    /// </summary>
    internal void Tick(MeshRenderer? parchment)
    {
        if (_padObjects.Count == 0)
            return;
        if (parchment != null)
        {
            _parchmentTop = parchment.bounds.max.y;
            _planeY = _parchmentTop + IconLiftWorld;
        }
        for (int i = 0; i < _padObjects.Count; i++)
            PosePad(i);
    }

    private void PosePad(int i)
    {
        GameObject go = _padObjects[i];
        Transform? decal = _padDecals[i];
        MapLocation loc = _padLocations[i];
        if (go == null)
            return;
        if (decal == null || loc == null)
        {
            // The map was rebuilt under us and the rescan has not caught up yet (up to 15 frames).
            // A pad whose location is gone must not keep standing in the beam: switch it off now
            // rather than let it eat a ray on behalf of an icon that no longer exists.
            if (go.activeSelf)
                go.SetActive(false);
            return;
        }
        // Active exactly when the icon is drawn: MapIconLayer skips decals whose object is
        // inactive, so a pad for one would be a hit box on nothing.
        bool draws = decal.gameObject.activeInHierarchy;
        if (go.activeSelf != draws)
            go.SetActive(draws);
        if (!draws)
            return;
        Vector3 dp = decal.position;
        go.transform.position = new Vector3(dp.x, _planeY, dp.z);
        go.transform.rotation = Quaternion.Euler(0f, decal.eulerAngles.y, 0f);
        _padColliders[i].size = FootprintOf(decal, loc);
    }

    /// <summary>
    /// The drawn footprint: <c>decal.lossyScale.xz</c> — the value <see cref="MapIconLayer"/> feeds
    /// its quad matrix — with the highlight inflation divided back out (see
    /// <see cref="HighlightedNodeScaleFactor"/>) and the pad's thickness on Y.
    /// </summary>
    private static Vector3 FootprintOf(Transform decal, MapLocation loc)
    {
        Vector3 ds = decal.lossyScale;
        float inflate = loc != null && loc.IsHighlighted ? HighlightedNodeScaleFactor : 1f;
        // ModBuild 189 — THE PAD IS THE DRAWN ICON, SO IT TAKES THE SIZE DIAL TOO. The user asked
        // for the map symbols to be enlargeable ("Die Symbole auf der Map sind sehr klein"), and
        // MapIconLayer now multiplies each drawn quad by [MapRoom] IconScale — or by
        // GloomhavenIconScale for the capital. This class's whole contract is "the pad is exactly
        // the quad MapIconLayer draws" (see the class doc), and it derives the footprint
        // INDEPENDENTLY from decal.lossyScale — so without this multiply the two silently disagree
        // the moment a dial leaves 1.0, and pointing at a symbol you can plainly see would miss it
        // again. That is the bug ModBuild 188 fixed, and re-introducing it through a size slider
        // would be a poor trade.
        //
        // ScaleForDrawnQuad is the layer's own published number — asking it, rather than reading
        // the two config entries here, means the capital test and the clamp cannot drift apart
        // between the drawn icon and the thing you point at.
        float dial = MapIconLayer.ScaleForDrawnQuad(decal);
        float sx = Mathf.Abs(ds.x) * dial / inflate;
        float sz = Mathf.Abs(ds.z) * dial / inflate;
        float thickness = Mathf.Max(Mathf.Min(sx, sz) * PadThicknessFraction, MinPadEdgeWorld);
        return new Vector3(sx, thickness, sz);
    }

    /// <summary>The location whose DRAWN ICON this collider is, or false for anything else.</summary>
    internal bool TryLocation(Collider? hit, out MapLocation loc)
    {
        loc = null!;
        if (hit == null)
            return false;
        if (!_byCollider.TryGetValue(hit, out MapLocation found) || found == null)
            return false;
        loc = found;
        return true;
    }

    /// <summary>
    /// Where a hover card belongs for <paramref name="loc"/>: the top-centre of its drawn icon.
    /// False when this location has no pad — the caller then falls back to the game's hit box and
    /// logs which of the two answered, so a bad placement on hardware is attributable to a
    /// mechanism rather than to a guess.
    /// </summary>
    internal bool TryAnchor(MapLocation loc, out Vector3 world)
    {
        world = default;
        if (loc == null)
            return false;
        for (int i = 0; i < _padLocations.Count; i++)
        {
            if (!ReferenceEquals(_padLocations[i], loc))
                continue;
            BoxCollider col = _padColliders[i];
            if (col == null || !col.gameObject.activeInHierarchy)
                return false;
            // bounds is the world AABB of a box that is only ever YAWED, so its centre is the pad's
            // centre and its max.y is the pad's top face — both exact, neither needing the rotation.
            Bounds b = col.bounds;
            world = new Vector3(b.center.x, b.max.y, b.center.z);
            return true;
        }
        return false;
    }

    /// <summary>Destroy every pad. Idempotent; reached from <c>MapLocationInteractor.Release</c>.</summary>
    internal void Release(string reason)
    {
        int had = _padObjects.Count;
        DestroyPads();
        if (had > 0)
            VRLog.Info(Scope, $"MAP ROOM icon pads released ({reason}) — {had} mod-owned hit box(es) "
                              + "destroyed. Nothing on a MapLocation was modified: the pads were separate "
                              + "scene objects and the game's own colliders were never touched.");
    }

    private void DestroyPads()
    {
        for (int i = 0; i < _padObjects.Count; i++)
        {
            if (_padObjects[i] != null)
                Object.Destroy(_padObjects[i]);
        }
        _padObjects.Clear();
        _padColliders.Clear();
        _padLocations.Clear();
        _padDecals.Clear();
        _byCollider.Clear();
    }

    /// <summary>
    /// The location's own drawn decal. Same route <see cref="MapIconLayer"/> takes to the same
    /// objects (the Decalicious type by name), asked per location instead of per parent — so a
    /// pad is only ever built for a decal that genuinely hangs under THIS location, never for a
    /// neighbour's.
    /// </summary>
    private static Transform? FindDecal(MapLocation loc)
    {
        if (_decalTypeMissing)
            return null;
        if (_decalType == null)
        {
            _decalType = HarmonyLib.AccessTools.TypeByName("Decal");
            if (_decalType == null)
            {
                _decalTypeMissing = true;
                VRLog.Warn(Scope, "MAP ROOM icon pads: the Decalicious 'Decal' type is not present, so the "
                                  + "drawn icon footprint cannot be measured. CONSEQUENCE: hover falls back to "
                                  + "the game's authored MapLocation._boxCollider for every icon — which is "
                                  + "exactly the per-icon coverage the pads exist to make uniform — and hover "
                                  + "cards anchor on that box instead of on the icon. Nothing throws; the map "
                                  + "room is otherwise unaffected.");
                return null;
            }
        }
        Component? c = loc.GetComponentInChildren(_decalType, includeInactive: true);
        return c != null ? c.transform : null;
    }
}
