using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// HOW FAR A PROP REACHES, and over how many HEXES.
///
/// <para><b>THE REPORT (2026-09-03 hardware round), verbatim.</b> "Es gibt props die mehrere tiles
/// überspannen, das highlighting erscheint allerdings nur wenn ich beim prop über ein einziges
/// feld mit der hand bin. Genauso kann ich es nur bei einem Feld greifen. Bei props die mehrere
/// tiles überspannen soll es genau so greifbar sein und das highlighting reagieren bei allen
/// anderen Feldern die auch betroffen sind." His board carries <c>TwoHexObstacle</c> as well as
/// <c>OneHexObstacle</c> (both are named in the ModBuild 367 logs), and only ONE of a two-hex
/// obstacle's hexes answers the hand.</para>
///
/// <para><b>THE CAUSE, and it is structural rather than a tuning number.</b> A prop's whole
/// spatial presence to the hands is ONE <c>Collider</c> — the one <c>PropGrab.Scan</c> passes to
/// <c>VRInteractables.RegisterGrabbable</c>. Every reach test the mod runs is a
/// <c>ClosestPoint</c> against exactly that one collider and nothing else:</para>
/// <list type="bullet">
///   <item><c>ProximityGrabber.UpdateHighlight</c> — <c>Vector3.Distance(palm,
///   collider.ClosestPoint(palm))</c>, which is what elects the HOVER GLOW;</item>
///   <item><c>ProximityGrabber.FindNearestGripGrabbable</c> and <c>LogNoCandidateRefusal</c> —
///   the same expression;</item>
///   <item><see cref="GrabbableProp.AllowsHand"/> — <c>Vector3.Distance(pinch,
///   _collider.ClosestPoint(pinch)) &lt;= admit</c>, which is what gates the GRAB.</item>
/// </list>
/// <para>So "highlighting and grabbing cover one hex" is the same sentence as "the registered
/// collider covers one hex", and the two halves of the report cannot be separated — which matches
/// what he wrote.</para>
///
/// <para><b>WHICH COLLIDER GETS REGISTERED, before this class existed.</b> Scan took
/// <c>visual.GetComponentInChildren&lt;Collider&gt;()</c> — the FIRST collider in a depth-first
/// walk of the prop's subtree, the prop's own authored one — and only fell back to
/// <c>FigureGrabDriver.BuildPropCollider</c> (an AABB over every renderer, which WOULD have
/// spanned the whole prop) when the prop had none at all. The ModBuild 367 hardware log settles
/// which branch these obstacles take, verbatim: <c>[Props] registry OPEN: ... Collider: the
/// prop's own.</c> So the authored collider is what is registered, and the authored collider is
/// what covers one hex. The fallback that would have been correct is the one that never runs.</para>
///
/// <para><b>WHY THE PROP'S OWN COLLIDER IS A ONE-HEX SHAPE.</b> The game reaches a prop by
/// RAYCAST on the "Hovering" layer, and <c>UnityGameEditorObject.Start</c>
/// (UnityGameEditorObject.cs:62-64) sets that layer on the prop ROOT only — never on children.
/// The authored collider is therefore a single shape on the root, sized for the game's own
/// mouse pick of the hex the prop is anchored on. A two-hex obstacle's second hex was never part
/// of any collider, because the flat game never needed one: it picks props through the BOARD.</para>
///
/// <para><b>WHERE THE FOOTPRINT COMES FROM, and the one source that looked right and is not.</b>
/// The game publishes a prop's covered hexes in two places.</para>
/// <list type="number">
///   <item><b><c>Coverage</c> children — GONE AT RUNTIME, do not reach for them.</b> The obvious
///   source (Choreographer.PlaceRandomProps:15528 reads them, one per occupied hex) is destroyed
///   before any of this code can see it: <c>UnityGameEditorObject.Start</c> walks
///   <c>GetComponentsInChildren&lt;Coverage&gt;()</c>, converts each one to a pathing blocker and
///   then calls <c>Object.Destroy(obj.gameObject)</c> on it (UnityGameEditorObject.cs:71-76).
///   Every prop visual is an instantiated prefab with a <c>UnityGameEditorObject</c>
///   (Choreographer.SpawnProp:13152-13157 refuses to spawn one without it), so <c>Start</c> has
///   always run by the time a discovery scan two seconds later looks at the prop. Reading Coverage
///   here would be a rule that can never fire — this project has shipped one of those before and
///   paid six builds for it. It is not read.</item>
///   <item><b><c>CObjectObstacle.PathingBlockers</c> — the live source, and it is pure serialized
///   state.</b> One <c>TileIndex</c> per hex the obstacle blocks, present on every obstacle in
///   every scenario: the procgen path writes it from the Coverage children
///   (Choreographer.cs:15526-15535), the level editor asserts on obstacles missing their own hex
///   from it (LevelEditorController.BulkCheckForIncorrectPathingBlockersInObstacles:2884) and
///   splits multi-hex obstacles by <c>PathingBlockers.Count &gt; 1</c> (:2484), and the rule
///   library pathfinds over it (CActor.cs:2449, CAbilityMove.cs:1416). It arrives with the
///   scenario state, i.e. BEFORE the visual does, so it can never be the thing a scan is waiting
///   on. It is replicated state every peer already agrees on, which is what makes reading it
///   multiplayer-safe.</item>
/// </list>
/// <para>A third, weaker source is the prop's own <c>EPropType</c> — <c>TwoHexObstacle</c>,
/// <c>ThreeHexCurvedObstacle</c> and friends literally name their hex count. It cannot LOCATE the
/// hexes, so it is used only as a fallback COUNT and as the census's cross-check on the first.</para>
///
/// <para><b>NOTHING HERE WRITES GAME STATE.</b> <c>PathingBlockers</c> and the client tile array
/// are read, never assigned; the only object this class creates is a mod-owned trigger box on the
/// Ignore Raycast layer, parented under the prop visual so Unity destroys it with the prop.</para>
///
/// <para><b>NO SCENE QUERY.</b> Everything below is a list walk over a prop's own blockers plus an
/// array index into <c>ClientScenarioManager.ClientTileArray</c>, and it runs on the discovery
/// scan's cadence (once per prop, at registration) and on the census cadence — never per frame.
/// <c>FindObjectsOfType</c> is a recorded trap in this project and none is used.</para>
/// </summary>
internal static class PropReach
{
    /// <summary>The name <see cref="FigureGrabDriver.BuildPropCollider"/> already gives its
    /// holder. Deliberately the SAME name for the spanning box: the two are the same thing playing
    /// the same role (a mod-owned trigger volume the proximity election measures against), they
    /// are mutually exclusive on one prop because a prop's hex count never changes, and the
    /// re-key reuse below finds either of them by this one name.</summary>
    internal const string ReachHolderName = "VR_PropReach";

    /// <summary>Layer 2, Ignore Raycast — the same literal <c>BuildPropCollider</c> uses, and for
    /// the same reason: a trigger on this layer is invisible to the game's physics and to every
    /// picking path, mod and vanilla, so it can only ever be measured against by the proximity
    /// election.</summary>
    private const int IgnoreRaycastLayer = 2;

    /// <summary>Which collider the prop ended up registering, for the log. Not a policy input —
    /// nothing branches on it.</summary>
    internal enum Route
    {
        /// <summary>The prop's own authored collider, unchanged. Every single-hex prop.</summary>
        PropsOwn,

        /// <summary>The renderer-bounds box <c>BuildPropCollider</c> makes for a prop that has no
        /// collider at all. Every single-hex prop without one; unchanged.</summary>
        BoundsBox,

        /// <summary>A spanning box built this scan for a multi-hex prop.</summary>
        SpanBuilt,

        /// <summary>The spanning box a previous scan built for this same visual, found again after
        /// a registry re-key. See the reuse note in <see cref="Resolve"/>.</summary>
        SpanReused,
    }

    /// <summary>The covered hexes' world positions, refilled per call. One shared scratch list
    /// because every caller is on the Unity main thread and none of them holds it across a call
    /// into another one — <see cref="Resolve"/> fills it and hands it straight to
    /// <see cref="BuildSpanning"/>, <see cref="SpannedHexes"/> fills it and reads it.</summary>
    private static readonly List<Vector3> HexCentres = new(4);

    /// <summary>
    /// HOW MANY HEXES DOES THIS PROP STAND ON? The count only — see <see cref="TryLocateHexes"/>
    /// for where they are.
    ///
    /// <para>Returns 1 for anything it cannot answer better, which is the conservative direction:
    /// a prop counted as single-hex takes the pre-existing code path unchanged, so an unknown prop
    /// can only ever behave exactly as it does today.</para>
    /// </summary>
    /// <param name="prop">The scenario-state prop.</param>
    /// <param name="source">Which term answered — printed by the census so a wrong count names its
    /// own origin instead of being argued about.</param>
    internal static int CoveredHexes(CObjectProp? prop, out string source)
    {
        if (prop == null)
        {
            source = "no prop";
            return 1;
        }

        // THE GAME'S OWN FOOTPRINT FIRST. PathingBlockers is per INSTANCE: a three-hex obstacle
        // that the editor split into single-hex pieces (LevelEditorController.cs:2496-2501 clears
        // the list and writes back exactly one entry) reports 1 here and is correctly treated as
        // a one-hex prop, where the EPropType fallback below would have said 3 and widened it.
        // That is the whole reason the per-instance list is asked first.
        if (prop is CObjectObstacle obstacle)
        {
            List<TileIndex> blockers = obstacle.PathingBlockers;
            if (blockers != null && blockers.Count > 0)
            {
                source = "PathingBlockers";
                return blockers.Count;
            }
        }

        int byType = HexesForType(PropLift.ResolvePropType(prop));
        if (byType > 0)
        {
            source = "EPropType";
            return byType;
        }

        source = "assumed";
        return 1;
    }

    /// <summary>
    /// The hex count the game's own prop FAMILY names, or 0 for a family that does not name one.
    ///
    /// <para>These five are exactly <c>PropLift.SolidObstacleTypes</c> — the whitelist of obstacle
    /// families a hand may lift — and the names are the game's, not ours. Everything else (gold,
    /// chests, traps, quest items, resources, the dark pit) stands on the single hex it was placed
    /// on and falls through to 0, which <see cref="CoveredHexes"/> reads as "one hex, unchanged
    /// path".</para>
    /// </summary>
    private static int HexesForType(EPropType type)
    {
        switch (type)
        {
            case EPropType.OneHexObstacle:
                return 1;
            case EPropType.TwoHexObstacle:
                return 2;
            case EPropType.ThreeHexObstacle:
            case EPropType.ThreeHexCurvedObstacle:
            case EPropType.ThreeHexStraightObstacle:
                return 3;
            default:
                return 0;
        }
    }

    /// <summary>
    /// WHERE the covered hexes are, in world space, or false when they cannot be located right now.
    ///
    /// <para><c>PathingBlockers</c> entries are ARRAY INDICES into the tile grid — the game reads
    /// them as <c>ScenarioManager.Tiles[blocker.X, blocker.Y]</c>
    /// (CustomObjectPositionToChildMaterials.cs:50-53) — and <c>ClientScenarioManager</c> holds
    /// the parallel client array with the tile GameObjects
    /// (<c>ClientTileArray</c>, ClientScenarioManager.cs:60/104-116), which is what
    /// <c>Choreographer.SpawnProp</c> itself uses to place a prop
    /// (<c>ClientTileArray[prop.ArrayIndex.X, prop.ArrayIndex.Y].m_GameObject.transform.position</c>,
    /// Choreographer.cs:13137/13161). So this is the game's own index-to-world conversion, taken
    /// from the same array, and no coordinate maths of ours sits between the two.</para>
    ///
    /// <para>Failure here is NOT a reason to refuse the prop, and that is deliberate: the spanning
    /// volume is built from the prop's RENDERER BOUNDS and does not need these positions at all
    /// (see <see cref="BuildSpanning"/>). They only tighten the box and feed the census column. A
    /// prop whose hexes cannot be located still gets its spanning box.</para>
    /// </summary>
    private static bool TryLocateHexes(CObjectProp prop, List<Vector3> into)
    {
        into.Clear();
        if (prop is not CObjectObstacle obstacle)
            return false;
        List<TileIndex> blockers = obstacle.PathingBlockers;
        if (blockers == null || blockers.Count == 0)
            return false;

        ClientScenarioManager csm = ClientScenarioManager.s_ClientScenarioManager;
        if (csm == null)
            return false;
        CClientTile[,] tiles = csm.ClientTileArray;
        if (tiles == null)
            return false;
        int width = tiles.GetLength(0);
        int height = tiles.GetLength(1);

        for (int i = 0; i < blockers.Count; i++)
        {
            TileIndex blocker = blockers[i];
            if (blocker == null)
                continue;
            // BOUNDS-CHECKED because the index is serialized scenario data and an out-of-range
            // entry would throw inside a discovery scan — and a scan that dies leaves the board
            // with no grabbable props at all (the same reasoning PropLift.ResolvePropType records
            // for its try/catch).
            if (blocker.X < 0 || blocker.X >= width || blocker.Y < 0 || blocker.Y >= height)
                continue;
            CClientTile tile = tiles[blocker.X, blocker.Y];
            GameObject? tileObject = tile != null ? tile.m_GameObject : null;
            if (tileObject == null)
                continue;
            into.Add(tileObject.transform.position);
        }
        return into.Count > 0;
    }

    /// <summary>
    /// THE COLLIDER A PROP REGISTERS. One call, one answer, and the single-hex answer is the one
    /// this file inherited.
    ///
    /// <para><b>A ONE-HEX PROP IS NOT TOUCHED, and the guarantee is structural rather than a
    /// promise.</b> When <see cref="CoveredHexes"/> says 1 — which it says for every prop that is
    /// not a multi-hex obstacle, and for anything it cannot answer — the two lines below are the
    /// two lines <c>PropGrab.Scan</c> already ran: the prop's own collider if it has one, else
    /// <c>BuildPropCollider</c>. No new geometry is computed, no box is resized, nothing is
    /// widened. The pick radius those props are tuned against is measured from the same shape it
    /// was measured from yesterday, and a prop that grabs itself from a NEIGHBOURING hex — the
    /// next bug report if this widened anything — is impossible for them by construction.</para>
    ///
    /// <para><b>THE MULTI-HEX ANSWER IS ONE VOLUME, NOT SEVERAL COLLIDERS.</b> The alternative was
    /// to register the same <see cref="Hands.Interact.IGrabbable"/> once per covered hex, each
    /// against its own box. <c>VRInteractables.RegisterGrabbable</c> forecloses it in its first
    /// statement — <c>UnregisterGrabbable(target)</c>, and <c>UnregisterGrabbable</c> removes
    /// EVERY entry whose <c>Target</c> is reference-equal — so a second registration deletes the
    /// first and a prop would end up with the reach of whichever box registered last. Making it
    /// work would mean changing the frozen Phase-2 interactables API, and the election loops that
    /// walk that list assume one entry per target throughout: <c>UpdateHighlight</c>'s
    /// <c>isCurrent</c> comparison, its rival-switch margin and its exit-dwell blocker string all
    /// read "the entry for this target", and a target appearing twice would race itself for the
    /// highlight every frame. One volume needs none of that.</para>
    ///
    /// <para><b>THE BOX IS REUSED ACROSS A RE-KEY, exactly as the built one already was.</b> A
    /// state sync hands back fresh <c>CObjectProp</c> instances for an unchanged board, so
    /// <c>PropGrab.Scan</c> drops every entry and re-adds it over the SAME GameObjects (its phase
    /// 2 comment carries the account). Destroying the box on the drop and rebuilding it on the add
    /// would hand the new entry the doomed one, because <c>Object.Destroy</c> is deferred to end
    /// of frame. So the box is a child of the prop VISUAL, it outlives the registry entry, Unity
    /// destroys it with the prop, and this method finds it again by name.</para>
    /// </summary>
    /// <param name="visual">The prop's GameObject, from <c>PropVisualLookup</c>.</param>
    /// <param name="prop">The scenario-state prop, for its footprint.</param>
    /// <param name="own">The prop's own authored collider as <c>PropGrab.Scan</c> already resolved
    /// it (<c>GetComponentInChildren&lt;Collider&gt;()</c>), passed in rather than re-queried so
    /// the single-hex path is the identical expression it always was.</param>
    /// <param name="route">Which collider came back — log only.</param>
    /// <param name="hexes">How many hexes the prop covers, for the caller's log.</param>
    /// <param name="hexSource">Which term produced <paramref name="hexes"/>.</param>
    internal static Collider? Resolve(GameObject visual, CObjectProp prop, Collider? own,
        out Route route, out int hexes, out string hexSource)
    {
        hexes = CoveredHexes(prop, out hexSource);

        if (hexes <= 1)
            return SingleHex(visual, own, out route);

        // A box this or an earlier scan built for this same visual. Transform.Find looks at DIRECT
        // children only, which is exactly right: BuildSpanning (and BuildPropCollider) parent the
        // holder straight onto the visual, and a deep search could otherwise adopt something that
        // merely shares the name.
        Transform existing = visual.transform.Find(ReachHolderName);
        Collider? reused = existing != null ? existing.GetComponent<Collider>() : null;
        if (reused != null)
        {
            route = Route.SpanReused;
            return reused;
        }

        bool located = TryLocateHexes(prop, HexCentres);
        if (!located)
            HexCentres.Clear();
        Collider? span = BuildSpanning(visual, HexCentres);
        if (span != null)
        {
            route = Route.SpanBuilt;
            return span;
        }

        // The prop draws nothing measurable, so there is no volume to build. Fall back to what the
        // prop would have got before this class existed rather than refusing it: one hex of reach
        // is a smaller defect than a prop that cannot be picked up at all.
        return SingleHex(visual, own, out route);
    }

    /// <summary>The pre-existing two lines, kept in one place so both call sites in
    /// <see cref="Resolve"/> are provably the same expression.</summary>
    private static Collider? SingleHex(GameObject visual, Collider? own, out Route route)
    {
        if (own != null)
        {
            route = Route.PropsOwn;
            return own;
        }
        route = Route.BoundsBox;
        return FigureGrabDriver.BuildPropCollider(visual);
    }

    /// <summary>
    /// A TRIGGER BOX OVER EVERYTHING THE PROP DRAWS, plus the centre of every hex it stands on.
    ///
    /// <para><b>The renderer bounds are the primary shape, and they are the shape the report
    /// describes.</b> A prop the player says "spans several tiles" is a prop that DRAWS on several
    /// tiles — the ModBuild 367 wall-fade census lists eleven separate meshes under one
    /// <c>TwoHexObstacle</c> unit — so the AABB over its renderers is, by construction, a volume
    /// standing over every hex it occupies. This is the same shape
    /// <c>FigureGrabDriver.BuildPropCollider</c> has been building all along for props that had no
    /// collider; it was simply never reached for the props that had one.</para>
    ///
    /// <para><b>The hex centres are a FLOOR under that, and normally change nothing.</b> They are
    /// encapsulated with their Y CLAMPED into the drawn bounds, so they can only ever widen the
    /// box in X/Z and never in height — a taller box is a real widening (a hand held above a rock
    /// would start electing it) and the hexes have nothing to say about height. What they buy is
    /// the one case the drawn bounds gets wrong: an obstacle that blocks a hex it barely draws on
    /// still answers the hand over that hex.</para>
    ///
    /// <para><b>THE DENOMINATOR IS THE HOLDER'S OWN LOSSY SCALE, and that is a deliberate
    /// difference from <c>BuildPropCollider</c>.</b> <c>SetParent(…, worldPositionStays: true)</c>
    /// rewrites the child's localScale so its WORLD scale is preserved, so this holder's
    /// <c>lossyScale</c> is (1,1,1) — the prop's own lossyScale is NOT the factor between this
    /// box's local size and its world size. The two agree at the unit prop scale every board prop
    /// measured so far has, which is why the difference has never shown up; the holder's own scale
    /// is the term that is right either way. <c>BuildPropCollider</c> is deliberately left alone
    /// with the prop's scale in it, because changing it would move the reach of every single-hex
    /// prop that has no collider, and this round's constraint is that a one-hex prop's reach does
    /// not move. That discrepancy is a real finding and it is recorded here rather than
    /// half-fixed.</para>
    /// </summary>
    private static Collider? BuildSpanning(GameObject visual, List<Vector3> hexCentres)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
            return null;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.sqrMagnitude <= 1e-10f)
            return null;

        float floorY = bounds.min.y;
        float ceilingY = bounds.max.y;
        for (int i = 0; i < hexCentres.Count; i++)
        {
            Vector3 centre = hexCentres[i];
            bounds.Encapsulate(new Vector3(centre.x, Mathf.Clamp(centre.y, floorY, ceilingY), centre.z));
        }

        var holder = new GameObject(ReachHolderName) { layer = IgnoreRaycastLayer };
        holder.transform.SetParent(visual.transform, worldPositionStays: true);
        holder.transform.position = bounds.center;
        holder.transform.rotation = Quaternion.identity;
        var box = holder.AddComponent<BoxCollider>();
        box.isTrigger = true;
        Vector3 lossy = holder.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(1e-4f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(1e-4f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(1e-4f, Mathf.Abs(lossy.z)));
        return box;
    }

    /// <summary>
    /// HOW MANY OF THE PROP'S HEXES THE REGISTERED REACH VOLUME ACTUALLY STANDS OVER — the census
    /// column, and the falsifier for everything above.
    ///
    /// <para>Measured on the collider that is REGISTERED, whatever it is, so the line reads the
    /// truth for a one-hex prop, for a multi-hex prop that got its spanning box, and for a
    /// multi-hex prop that fell back. It is not a tautology of the fix: a two-hex obstacle whose
    /// authored collider is still what got registered reads <c>1/2</c> and names the defect
    /// without a screenshot.</para>
    ///
    /// <para><b>Deliberately the collider's world AABB and not <c>ClosestPoint</c>.</b>
    /// <c>Collider.ClosestPoint</c> is only defined for box, sphere, capsule and CONVEX mesh
    /// colliders; on a non-convex <c>MeshCollider</c> Unity logs an error and hands back the query
    /// point, which would read as "in reach" for every hex on the board and make this column agree
    /// with every broken build. The prop's authored collider is game content and may be any of
    /// those. <c>Bounds.Contains</c> has no such hole, allocates nothing, and answers the question
    /// the column is actually asking — does the volume stand OVER this hex.</para>
    ///
    /// <para>The hex centre is lifted to the box's own centre height before the test: a tile
    /// GameObject sits on the floor and a prop volume that starts above it (a hovering crystal,
    /// say) would otherwise report zero hexes while reaching all of them.</para>
    /// </summary>
    /// <returns>The number of covered hexes the volume stands over; <c>-1</c> when the hexes
    /// cannot be located at all (no <c>PathingBlockers</c>, or no client tile array yet), which is
    /// a different answer from zero and is printed differently.</returns>
    internal static int SpannedHexes(Collider? collider, CObjectProp? prop)
    {
        if (collider == null || prop == null)
            return -1;
        if (!TryLocateHexes(prop, HexCentres))
            return -1;

        Bounds box = collider.bounds;
        int spanned = 0;
        for (int i = 0; i < HexCentres.Count; i++)
        {
            Vector3 centre = HexCentres[i];
            if (box.Contains(new Vector3(centre.x, box.center.y, centre.z)))
                spanned++;
        }
        return spanned;
    }

    /// <summary>One short clause naming the route, for the registration log line.</summary>
    internal static string Describe(Route route)
    {
        switch (route)
        {
            case Route.PropsOwn:
                return "the prop's own collider, unchanged (single-hex)";
            case Route.BoundsBox:
                return "a box over its renderer bounds (the prop had no collider; single-hex)";
            case Route.SpanBuilt:
                return "a NEW spanning trigger box over every hex it stands on";
            case Route.SpanReused:
                return "the spanning trigger box an earlier scan built for this visual";
            default:
                return "unknown";
        }
    }
}
