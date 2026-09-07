using System.Collections.Generic;
using GloomhavenVR.Hands.Interact;
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
/// <para><b>THAT LIST WAS THE WHOLE TRUTH UNTIL MODBUILD 473, AND IT IS NOT ANY MORE.</b> A
/// multi-hex prop now publishes a <see cref="PropFootprint"/> — the union of the hexes it stands
/// on — and every one of the three tests above measures THAT instead, through
/// <c>Hands.Interact.IGrabReachVolume</c>, which <c>GrabbableProp</c> implements. A prop with no
/// footprint (every single-hex prop, and any multi-hex prop whose union could not be described)
/// still takes the <c>ClosestPoint</c> expression above, unchanged, against the same registered
/// collider. See <see cref="PropFootprint"/> for the report that forced it and the numbers that
/// name it.</para>
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

        /// <summary>ModBuild 445, AND THIS IS THE GOLD-PILE ROUTE. The prop HAS an authored
        /// collider and it is not a usable pick shape — switched off, on a deactivated object, or
        /// a non-convex mesh — so a renderer-bounds box was built to stand in for it. Distinct
        /// from <see cref="BoundsBox"/> on purpose: "the prop had none" and "the prop had one and
        /// it was dead" are different facts about the game's content, and the census has to be
        /// able to tell them apart.</summary>
        BoundsOverUnusable,

        /// <summary>The renderer-bounds box a previous scan built for this same visual, found
        /// again by name after a registry re-key. Single-hex twin of
        /// <see cref="SpanReused"/>.</summary>
        BoundsReused,
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
    /// <param name="footprint">ModBuild 473 — THE VOLUME THE HANDS ACTUALLY MEASURE for a
    /// multi-hex prop: the union of its hexes rather than their bounding box. Null for every prop
    /// that covers ONE hex, and null whenever the union cannot be described (see
    /// <see cref="PropFootprint.Build"/>); a null footprint means <c>GrabbableProp</c> measures the
    /// returned collider with the identical <c>ClosestPoint</c> expression it always did. The
    /// collider above is still what the prop REGISTERS — the registry, the pick-shape predicate
    /// and the <see cref="SpannedHexes"/> census all keep reading it — and it is still the box a
    /// re-key finds again by name.</param>
    internal static Collider? Resolve(GameObject visual, CObjectProp prop, Collider? own,
        out Route route, out int hexes, out string hexSource, out PropFootprint? footprint)
    {
        footprint = null;
        hexes = CoveredHexes(prop, out hexSource);

        if (hexes <= 1)
            return SingleHex(visual, own, out route);

        // THE HEXES ARE LOCATED FIRST NOW (ModBuild 473), before the reuse branch rather than
        // after it. The spanning BOX is a Unity component parented on the prop visual and it
        // survives a registry re-key; the FOOTPRINT is a plain object owned by the registry ENTRY,
        // and a re-key builds a new entry — so it has to be built on both routes or a re-keyed
        // prop would silently fall back to its bounding box, which is the defect this build is
        // fixing. Locating them costs one PathingBlockers walk and one array index, at
        // registration only.
        bool located = TryLocateHexes(prop, HexCentres);
        if (!located)
            HexCentres.Clear();

        // A box this or an earlier scan built for this same visual. Transform.Find looks at DIRECT
        // children only, which is exactly right: BuildSpanning (and BuildPropCollider) parent the
        // holder straight onto the visual, and a deep search could otherwise adopt something that
        // merely shares the name.
        Transform existing = visual.transform.Find(ReachHolderName);
        Collider? reused = existing != null ? existing.GetComponent<Collider>() : null;
        if (reused != null)
        {
            route = Route.SpanReused;
            Bounds reusedDrawn = DrawnBounds(visual, out bool reusedDrew);
            if (reusedDrew)
                footprint = PropFootprint.Build(visual, HexCentres, reusedDrawn);
            return reused;
        }

        Collider? span = BuildSpanning(visual, HexCentres, out Bounds drawnBounds, out bool drew);
        if (span != null)
        {
            route = Route.SpanBuilt;
            if (drew)
                footprint = PropFootprint.Build(visual, HexCentres, drawnBounds);
            return span;
        }

        // The prop draws nothing measurable, so there is no volume to build. Fall back to what the
        // prop would have got before this class existed rather than refusing it: one hex of reach
        // is a smaller defect than a prop that cannot be picked up at all.
        return SingleHex(visual, own, out route);
    }

    /// <summary>
    /// The AABB over everything the prop DRAWS, or an empty box when it draws nothing measurable.
    /// Split out of <see cref="BuildSpanning"/> (ModBuild 473) because the reuse route needs the
    /// same vertical extent without building a second box.
    /// </summary>
    private static Bounds DrawnBounds(GameObject visual, out bool drew)
    {
        drew = false;
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
            return default;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.sqrMagnitude <= 1e-10f)
            return default;
        drew = true;
        return bounds;
    }

    /// <summary>
    /// The single-hex answer, kept in one place so both call sites in <see cref="Resolve"/> are
    /// provably the same expression.
    ///
    /// <para><b>ModBuild 445 — "has a collider" was never the question; "has a USABLE PICK SHAPE"
    /// is.</b> This method used to read <c>if (own != null) return own;</c>. An enemy-drop gold
    /// pile has an authored collider that is present and SWITCHED OFF, so it took the
    /// <see cref="Route.PropsOwn"/> branch and registered a shape for which
    /// <c>Collider.ClosestPoint</c> is degenerate — it hands back the query point, i.e. every
    /// reach test in the mod read that pile at exactly 0 mm no matter where the hand was. The
    /// consequences were the whole 2026-09-05 round: the pile could never be highlighted or
    /// grabbed (<c>ProximityGrabber</c> skipped it on precisely this test, which is how we know
    /// the collider is off), and the figure-resize gesture was dead board-wide because its
    /// prop-beats-shell veto believed that zero. See
    /// <see cref="Hands.Interact.VRInteractables.IsUsablePickShape"/>.</para>
    ///
    /// <para><b>A prop whose own collider IS usable is untouched.</b> That is every prop that
    /// worked yesterday: the predicate is true for it, the first branch returns the identical
    /// reference, and the pick radius those props are tuned against is measured from the same
    /// shape it always was. Nothing is widened for them.</para>
    ///
    /// <para><b>The stand-in is the box this file already knew how to build.</b>
    /// <c>BuildPropCollider</c>'s renderer-bounds trigger on Ignore Raycast — the branch a prop
    /// with no collider at all has always taken. It is looked up by NAME first, for exactly the
    /// reason the multi-hex path documents: a registry re-key drops and re-adds every entry over
    /// the same GameObjects inside one frame, and building a second box each time would litter the
    /// prop with holders. Before this build that lookup was accidental — the built box was itself
    /// a child collider, so the next scan's <c>GetComponentInChildren</c> found it and called it
    /// "the prop's own". <see cref="OwnPickShape"/> now skips holders deliberately, so the route
    /// the log prints is honest, and this by-name lookup is what replaces the accident.</para>
    /// </summary>
    private static Collider? SingleHex(GameObject visual, Collider? own, out Route route)
    {
        bool hadOwn = own != null;
        if (VRInteractables.IsUsablePickShape(own))
        {
            route = Route.PropsOwn;
            return own;
        }

        // Direct children only — the same reasoning the multi-hex reuse lookup carries: the holder
        // is parented straight onto the visual, and a deep search could adopt something that
        // merely shares the name.
        Transform existing = visual.transform.Find(ReachHolderName);
        Collider? reused = existing != null ? existing.GetComponent<Collider>() : null;
        if (VRInteractables.IsUsablePickShape(reused))
        {
            route = hadOwn ? Route.BoundsOverUnusable : Route.BoundsReused;
            return reused;
        }

        route = hadOwn ? Route.BoundsOverUnusable : Route.BoundsBox;
        return FigureGrabDriver.BuildPropCollider(visual);
    }

    /// <summary>
    /// THE PROP'S OWN PICK SHAPE — the first collider in the prop's subtree that a reach test may
    /// actually believe, or null.
    ///
    /// <para>Replaces the bare <c>visual.GetComponentInChildren&lt;Collider&gt;()</c> that
    /// <c>PropGrab.Scan</c> used to run. Two differences, both deliberate:</para>
    /// <list type="number">
    ///   <item><b>It skips colliders that are not usable pick shapes and keeps looking.</b> A prop
    ///   whose FIRST collider is switched off but which carries a live one elsewhere in its
    ///   subtree now registers the live one instead of a degenerate zero. For every prop whose
    ///   first collider is fine — which is every prop that worked before this build — the answer
    ///   is the identical reference the old expression returned.</item>
    ///   <item><b>It skips our own <see cref="ReachHolderName"/> holders</b>, so "the prop's own
    ///   collider" means the prop's, and a box a previous scan built is reported by
    ///   <see cref="SingleHex"/> as reuse rather than as content. The holder is still found and
    ///   still reused — one line further down, by name.</item>
    /// </list>
    /// <para><c>includeInactive: true</c> so the walk can SEE a collider parked on a deactivated
    /// object; it is then refused by the predicate like any other unusable shape, and
    /// <paramref name="present"/> counts it. Reading that population is the point: "this prop has
    /// no collider" and "this prop has one and it is dead" are different findings about the game's
    /// content, and the registration log has to be able to say which.</para>
    /// </summary>
    /// <param name="visual">The prop's GameObject.</param>
    /// <param name="present">How many colliders the prop's subtree carries at all, our own holders
    /// excluded — log only.</param>
    /// <param name="usable">How many of those are usable pick shapes — log only.</param>
    internal static Collider? OwnPickShape(GameObject visual, out int present, out int usable)
    {
        present = 0;
        usable = 0;
        Collider? first = null;
        Collider[] all = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Collider c = all[i];
            if (c == null || c.gameObject.name == ReachHolderName)
                continue;
            present++;
            if (!VRInteractables.IsUsablePickShape(c))
                continue;
            usable++;
            if (first == null)
                first = c;
        }
        return first;
    }

    /// <summary>
    /// The <c>[Props]</c> census's PICKSHAPE column: <c>usable/present</c> over the prop's own
    /// colliders, our holders excluded.
    ///
    /// <para>It replaces a <c>collider=yes|no</c> column that was true and useless. A gold pile
    /// reads <c>0/1</c> — it HAS a collider and none of them may be believed — which is the whole
    /// 2026-09-05 defect stated in three characters, on the line that was already being printed.
    /// A healthy prop reads <c>1/1</c> or better, and a prop that genuinely has none reads
    /// <c>0/0</c>, which is a different fact and takes a different remedy (the renderer-bounds box
    /// this file has always built for it).</para>
    /// </summary>
    internal static string DescribeCensus(GameObject? visual)
    {
        if (visual == null)
            return "n/a";
        OwnPickShape(visual, out int present, out int usable);
        return $"{usable}/{present}";
    }

    /// <summary>One clause naming why the prop's own collider was refused, for the registration
    /// log — the FIRST one found, since a prop carrying several dead shapes is dead for one reason
    /// in practice. Empty string when the prop has no colliders of its own at all.</summary>
    internal static string DescribeOwnRefusal(GameObject visual)
    {
        Collider[] all = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Collider c = all[i];
            if (c == null || c.gameObject.name == ReachHolderName)
                continue;
            return VRInteractables.DescribePickShape(c);
        }
        return string.Empty;
    }

    /// <summary>
    /// A TRIGGER BOX OVER EVERYTHING THE PROP DRAWS, plus the centre of every hex it stands on.
    ///
    /// <para><b>MODBUILD 473 — THIS BOX IS NO LONGER WHAT A HAND IS MEASURED AGAINST.</b> It is
    /// the shape the prop REGISTERS: the registry needs one live collider per target, the
    /// pick-shape predicate is asked about it, and <see cref="SpannedHexes"/> still measures it.
    /// The reach itself is <see cref="PropFootprint"/>, because this box is an AABB and an AABB
    /// over a three-hex obstacle's renderers measured 3.6 x 3.4 HEXES across in the ModBuild 472
    /// host log for a prop standing on three — a hand anywhere inside it read 0 mm, which both
    /// ate the figure on the next hex and answered from 392 mm real away. Everything below
    /// describes the BOX and stays true of it; none of it is a claim about the reach any more.</para>
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
    private static Collider? BuildSpanning(GameObject visual, List<Vector3> hexCentres,
        out Bounds drawnBounds, out bool drew)
    {
        drawnBounds = DrawnBounds(visual, out drew);
        if (!drew)
            return null;
        Bounds bounds = drawnBounds;

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
            case Route.BoundsOverUnusable:
                return "a box over its renderer bounds STANDING IN FOR THE PROP'S OWN COLLIDER, "
                       + "which is present but is not a usable pick shape (single-hex)";
            case Route.BoundsReused:
                return "the renderer-bounds box an earlier scan built for this visual (single-hex)";
            case Route.SpanBuilt:
                return "a NEW spanning trigger box over every hex it stands on";
            case Route.SpanReused:
                return "the spanning trigger box an earlier scan built for this visual";
            default:
                return "unknown";
        }
    }
}

/// <summary>
/// THE UNION OF THE HEXES A PROP STANDS ON — the reach volume that replaced the bounding box.
///
/// <para><b>THE REPORT (2026-09-07 hardware round), verbatim.</b> "Wenn eine Figur neben einem
/// Prop steht das mehrere tiles umfasst, bekommt man die Figur schwierig bis kaum gegriffen, da
/// das highlight immer auf die nebenstehende Prop highlightet und es dann greift statt die Figure.
/// Verbessere die greif-ranges. Die Hindernisse die mehrere tiles umfassen kann man auch schon von
/// viel zu weit weg aufheben, also ihre 'hitbox' in der es reagiert ist zu groß. Soll sich ähnlich
/// anfühlen wie bei den Figuren auch."</para>
///
/// <para><b>ONE CAUSE, BOTH HALVES, AND THE MODBUILD 472 HOST LOG CARRIES THE NUMBERS.</b>
/// <c>PropReach.BuildSpanning</c> makes a multi-hex prop's reach volume an AXIS-ALIGNED BOUNDING
/// BOX over every renderer in its subtree, and every reach test in the mod was
/// <c>Distance(hand, collider.ClosestPoint(hand))</c> against exactly that one collider. A hand
/// ANYWHERE INSIDE that box reads 0, and 0 wins every election outright.</para>
/// <list type="bullet">
///   <item>The log measures the box. <c>[Props] pre-grab highlight ENGAGED (Right near
///   'ThreeHexObstacle' Obstacle)</c> prints its renderer span as <c>size (6.16, 3.52, 5.86)</c>
///   world units, and <c>[FigureGrab] PICK VOLUME</c> prints <c>one hex is 109 mm real at this
///   zoom</c> at <c>rig world scale 15.73</c>, i.e. one hex is 1.71 world units. So the volume is
///   3.6 x 3.4 HEXES across for a prop the census calls <c>hexes=3(PathingBlockers)
///   REACHHEXES=3/3</c> — the neighbouring figure's hex is INSIDE it, at distance 0.</item>
///   <item>The same two lines put the box at 392 x 373 mm REAL at the hand, against the 40 mm the
///   figure pick radius admits (<c>PICK VOLUME: 40 mm real at the hand</c>): "von viel zu weit
///   weg" stated as a ratio, not as an adjective.</item>
///   <item>The yardstick he names is measured in the same log. <c>[FigureGrab] FIGURE REACH</c>
///   prints every figure's pick collider — <c>a CapsuleCollider … size (1.00, 2.00, 1.00) wu</c>,
///   i.e. 0.58 hex across and 1.17 hexes tall, on eleven distinct actors.</item>
/// </list>
///
/// <para><b>THE SHAPE.</b> One upright CYLINDER per covered hex: centred on that hex's own centre,
/// radius half the runtime hex width (<c>UnityGameEditorRuntime.s_TileSize.x * 0.5</c> — the same
/// term <c>VRRigDriver.ResolveWorldScale</c> and <c>SkyAlternative</c> read), running over the
/// prop's DRAWN vertical range. The reach distance is the MINIMUM over those cylinders, so the
/// volume is their union and nothing else. Three consequences, one per clause of the report:</para>
/// <list type="number">
///   <item><b>It cannot reach a hex the prop does not stand on.</b> A cylinder extends half a hex
///   PITCH in every direction, so its surface stops exactly half way to the next hex centre. A
///   figure standing there has its capsule surface 1.21 wu from our hex centre against our 0.86 wu,
///   so it can never be inside the prop's volume — whatever the prop draws, or overhangs.</item>
///   <item><b>It admits at a figure's margin, and that margin is not a new number.</b> Nothing
///   here is tuned: <c>GrabbableProp.AllowsHand</c> keeps admitting at
///   <c>FigureGrabConfig.PickRadiusRealMeters</c> — the FIGURE's own dial, the same 40 mm — only
///   now measured from this surface instead of from the box. The prop's half-hex 54 mm plus that
///   40 mm is 94 mm from a hex centre, against a figure's 32 mm capsule radius plus the same
///   40 mm = 72 mm from a mini's axis. "Ähnlich wie bei den Figuren", in one unit.</item>
///   <item><b>The vertical extent is the figure's rule too: the body it draws.</b> A figure answers
///   over its own capsule, floor to head; a prop answers over its own drawn floor-to-ceiling. The
///   three-hex obstacle in his scenario draws 3.52 wu tall against a figure's 2.00, so HEIGHT was
///   never what "too far away" was about, and it is deliberately not narrowed — narrowing it would
///   make a tall rock unreachable from the side the player can see.</item>
/// </list>
///
/// <para><b>A ONE-HEX PROP HAS NO FOOTPRINT AT ALL, and that is structural rather than promised.</b>
/// <c>PropReach.Resolve</c> returns before this class is ever mentioned for every prop
/// <c>PropReach.CoveredHexes</c> counts as 1 — which is every prop that is not a multi-hex
/// obstacle, and everything it cannot answer. <c>GrabbableProp</c> holds a null reference in that
/// case and takes the identical <c>ClosestPoint</c> expression it always took, against the
/// identical collider. There is no branch that could widen or narrow those props, because there is
/// no object for a branch to read.</para>
///
/// <para><b>NO SCENE QUERY, NO ALLOCATION, NO PHYSICS.</b> Built once per prop at registration;
/// <see cref="Distance"/> is arithmetic over a fixed array and is safe on the per-frame election
/// path. <c>FindObjectsOfType</c> is a recorded trap in this project and none is used. The cells
/// are stored in the VISUAL's local space and transformed back per query, so they follow the prop
/// exactly as a child collider would and a re-posed prop can never leave a stale volume behind.</para>
/// </summary>
internal sealed class PropFootprint
{
    /// <summary>One covered hex, in the prop visual's LOCAL space. <see cref="LocalCentre"/> is the
    /// hex centre lifted to the drawn body's mid-height; the two lengths are local.</summary>
    private readonly struct Cell
    {
        internal readonly Vector3 LocalCentre;
        internal readonly float LocalRadius;
        internal readonly float LocalHalfHeight;

        internal Cell(Vector3 localCentre, float localRadius, float localHalfHeight)
        {
            LocalCentre = localCentre;
            LocalRadius = localRadius;
            LocalHalfHeight = localHalfHeight;
        }
    }

    private readonly Transform _visual;
    private readonly Cell[] _cells;

    private PropFootprint(Transform visual, Cell[] cells)
    {
        _visual = visual;
        _cells = cells;
    }

    /// <summary>How many covered hexes this volume is the union of — printed beside an elected
    /// prop on the hover line, so a wrong footprint names its own size.</summary>
    internal int Count => _cells.Length;

    /// <summary>The cylinder radius in REAL METRES AT THE HAND — half a hex, restated in the unit
    /// the figure pick radius is quoted in so the two stand comparably on one line.</summary>
    /// <param name="handWorldScale">The rig world scale the reading is taken at.</param>
    internal float RadiusRealMeters(float handWorldScale)
    {
        if (_cells.Length == 0 || _visual == null)
            return 0f;
        return _cells[0].LocalRadius * UniformScale(_visual) / Mathf.Max(handWorldScale, 1e-4f);
    }

    /// <summary>
    /// Distance from <paramref name="point"/> to this footprint in WORLD units — zero inside it,
    /// the true Euclidean gap outside. A drop-in for
    /// <c>Vector3.Distance(point, collider.ClosestPoint(point))</c>, which is what every reach test
    /// in this mod is.
    /// </summary>
    /// <param name="cell">Which covered hex answered — the index into the prop's own
    /// <c>PathingBlockers</c> order; -1 when there is nothing to measure. Log only, and nothing
    /// branches on it.</param>
    internal float Distance(Vector3 point, out int cell)
    {
        cell = -1;
        if (_visual == null || _cells.Length == 0)
            return float.PositiveInfinity;

        float scale = UniformScale(_visual);
        float best = float.PositiveInfinity;
        for (int i = 0; i < _cells.Length; i++)
        {
            Cell c = _cells[i];
            Vector3 centre = _visual.TransformPoint(c.LocalCentre);
            float radius = c.LocalRadius * scale;
            float halfHeight = c.LocalHalfHeight * scale;

            // The cylinder's axis is WORLD up, not the prop's: the hex grid is a world-space
            // lattice and board props stand upright on it. A prop tilted in a hand is never
            // measured here — GrabbableProp.CanGrab is false for the whole of a hold.
            float dx = point.x - centre.x;
            float dz = point.z - centre.z;
            float radial = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - radius);
            float axial = Mathf.Max(0f, Mathf.Abs(point.y - centre.y) - halfHeight);
            float d = Mathf.Sqrt(radial * radial + axial * axial);
            if (d < best)
            {
                best = d;
                cell = i;
            }
        }
        return best;
    }

    /// <summary>
    /// The one uniform factor a local length is converted to world with.
    ///
    /// <para>The LARGEST absolute component of the visual's lossy scale, not the average: board
    /// props are uniformly scaled, where every choice agrees exactly, and for a non-uniform one the
    /// largest component is the conservative direction — a slightly wider cylinder rather than a
    /// hand that has secretly left the volume. Read per query rather than captured at build,
    /// because a held prop is rescaled by the stretch gesture.</para>
    /// </summary>
    private static float UniformScale(Transform t)
    {
        Vector3 lossy = t.lossyScale;
        return Mathf.Max(1e-4f,
            Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))));
    }

    /// <summary>
    /// Build the union of hex cylinders, or null when the pieces are not all there.
    ///
    /// <para>Null — i.e. the prop keeps the bounding box it had in ModBuild 472 — when the hexes
    /// could not be located (no <c>PathingBlockers</c>, or no client tile array yet), when the
    /// game's runtime hex size is not resolvable, or when the prop draws nothing measurable.
    /// Refusing to narrow a volume that cannot be described is the conservative direction, and the
    /// registration log says which prop got which.</para>
    /// </summary>
    /// <param name="visual">The prop's GameObject.</param>
    /// <param name="hexCentres">World centres of the hexes the prop stands on.</param>
    /// <param name="drawn">The AABB over the prop's renderers — its VERTICAL extent is all that is
    /// taken from it; the horizontal extent is the hex grid's, by construction.</param>
    internal static PropFootprint? Build(GameObject visual, List<Vector3> hexCentres, Bounds drawn)
    {
        if (visual == null || hexCentres.Count == 0)
            return null;

        // The runtime hex WIDTH in world units, taken from the 'Hex' resource's BoxCollider
        // (decompiled UnityGameEditorRuntime) — the same term VRRigDriver.ResolveWorldScale and
        // SkyAlternative read, so the cylinder is half a hex by the GAME's measure and never by a
        // constant of ours. It is zero before a scenario's tiles exist, which is exactly the case
        // this refuses on rather than inventing a radius for.
        float hexWidth = UnityGameEditorRuntime.s_TileSize.x;
        if (!(hexWidth > 1e-4f))
            return null;

        float halfHeight = drawn.extents.y;
        if (!(halfHeight > 0f))
            return null;

        Transform t = visual.transform;
        Vector3 lossy = t.lossyScale;
        float scale = Mathf.Max(1e-4f,
            Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))));
        float midY = drawn.center.y;

        var cells = new Cell[hexCentres.Count];
        for (int i = 0; i < hexCentres.Count; i++)
        {
            Vector3 centre = new(hexCentres[i].x, midY, hexCentres[i].z);
            cells[i] = new Cell(t.InverseTransformPoint(centre), hexWidth * 0.5f / scale,
                halfHeight / scale);
        }
        return new PropFootprint(t, cells);
    }
}
