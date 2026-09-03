using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// FREE-STANDING MASS UNITS (ModBuild 406, user report 2026-09-03, eingang-faded-nicht.jpg:
/// <i>"der eingestürzte Eingang faded immer noch nicht"</i> — the collapsed cave entrance, a
/// rock arch about two hexes wide standing between the head and the figures, stays solid while
/// the walls behind it fade and only the crystals inside it go).
///
/// <para><b>THE MECHANISM, READ OFF THE ModBuild 405 LOG.</b> The arch is
/// <c>PCG_CV_Entrance_01_PR</c>: 37 architecture-scale rock meshes (<c>CV_Generic_Rock_01/04</c>
/// with their <c>LOD0/1/2</c>, 3-7 wu³ each, y[2.5..4.6]) under NO <c>ProceduralWall</c>. Their
/// materials carry the game's wall-fade TOGGLE but not a WallFade shader NAME, and the adoption
/// sweep (<see cref="FadeDriver.AdoptShaderMatchedWalls"/>) is fed by the NAME-only fact
/// (<c>RendererFact.WallFadeShader</c>), so they never became a shader-adopted group — the same
/// "gate narrower than its choke point" shape ModBuild 386 fixed on the split-wall path, one lane
/// over. The stacked lane declined them (no wall column under them); the mounted lane's volume
/// guard then adopted them RIDE-ONLY (ModBuild 402, ORPHANED ARCHITECTURE) onto the nearest
/// segment at gap 0.00 — which for most of them is <c>'AA : (f1289624-…)'</c>, the map tile's
/// own shader-adopted group (19 crystal renderers whose union box spans the whole tile, so
/// EVERYTHING on the tile is at gap 0.00 to it). That group's coverage is measured on the
/// CRYSTALS alone (<see cref="FadeDriver.RayHitsWallMesh"/> walks Renderers/Foliage/Siblings/
/// Body/Stacked and deliberately NOT Mounted), reads raw 0.31 / ema 0.26 on the diag row against
/// an enter bar of 0.35, and so sits <c>off 0.00</c> — and a rider on a segment at fade 0.00 is
/// solid by construction. <c>!FAT</c> on that row is a FLAG (the box is fat in both horizontal
/// axes), not a refusal: nothing acts on it unless the box also engulfs 40 % of the room's
/// samples, which a tile-edge group does not. The four rocks that DID vanish rode 'Wall 4' at
/// fade 1.00 — the same election, a different winner by the ModBuild 402 fade tie-break.</para>
///
/// <para><b>THE FIX: A FORMATION THAT IS IN NO WALL RUN BECOMES ITS OWN FADEABLE UNIT.</b> This
/// lane runs AFTER the stacked lane (walls and their superstructure keep first claim) and BEFORE
/// the prop-unit and mounted passes (which then see the unit's members as owned). It sweeps the
/// fact table for meshes that are ARCHITECTURE by the mounted lane's own two guards (AABB volume
/// above <see cref="FadeDriver.MountedMaxMeshVolumeWU3"/>, or fat on two axes), and either
/// AIRBORNE (foot at least <see cref="FadeDriver.GroundExclusionHeightWU"/> over the room floor)
/// or TALL (top at least <see cref="FadeDriver.FreeStandingMinTopWU"/> over it — the entrance's
/// two SIDES go to the ground and hide the figures as the lintel does, and the STANDING PROP
/// ruling's own height term says a floor-footed mesh that tall is not a floor prop), under no
/// <c>ProceduralWall</c>, listed by no other segment, not a figure, not a standing prop (both
/// arms, height term included), not in a doorway arch or water rect. The ground band cannot be
/// reached: a ground-band renderer is one whose AABB top sits within 1.0 wu of the floor (the
/// predicate <see cref="FadeDriver.StripGroundRenderers"/> and the WALL-PATH AUDIT share), every
/// member kept has its top ≥ 2.5 wu over that floor, and a unit writes only to its own members. Those are clustered by XZ proximity (AABB gap ≤
/// <see cref="FadeDriver.FreeStandingLinkXZ"/>) into units; a unit whose top does not reach
/// <see cref="FadeDriver.FreeStandingMinTopWU"/> over its room floor is not view-blocking and is
/// refused; a unit whose XZ box contains <see cref="FadeDriver.EngulfSampleFraction"/> of its
/// room's samples is a ceiling or the room itself and is refused (that is the ONE reading of the
/// fat box that must keep holding: a map tile never fades as a whole). Each unit is a
/// <see cref="Segment"/> like any adopted wall group: same room association, same coverage
/// ray test on its OWN meshes, same Schmitt trigger and dwell, same MPB delivery through the
/// materials' own wall-fade toggle (or the dissolve swap for a member with no channel, via the
/// Body list), same wire key for the peers. It is exempt from the engulf SPLIT (a fat box is what
/// a formation is; the containment refusal above is its guard) and from the adoption sweep's
/// refresh/dead-key cycle (which would dissolve a segment that owns no NAME-matched renderer on
/// every rescan and pop the arch back solid).</para>
///
/// <para><b>WHAT IT DOES NOT TOUCH.</b> Nothing under a <c>ProceduralWall</c> (the WALL MEMBER
/// leftover class cannot gain a member here), nothing on the floor (the STANDING PROP two-arm
/// rule and the 1.0 wu ground band are tested BEFORE clustering), no Light, no game state. The
/// mounted lane's ORPHANED ARCHITECTURE path is unchanged and now only sees what this lane
/// refused — its count is expected to drop to zero in the entrance scene, and a non-zero count
/// beside a formed unit names a piece this lane's bars excluded.</para>
///
/// <para><b>COST.</b> One pass over the fact table on rescan only (cheap field tests; the
/// component walks run on the few candidates that pass the size and height prefilters), a
/// union-find over at most <see cref="FadeDriver.FreeStandingMaxCandidates"/> candidates, then
/// one segment fill per unit. Reported in milliseconds on the FREE-STANDING MASS line and under
/// the <c>WallFade.Commit.FreeStanding</c> PerfMonitor scope. Per frame the unit costs exactly
/// what one more wall segment costs.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>ModBuild 406: this segment is a FREE-STANDING MASS unit formed by
        /// <see cref="FadeDriver.CollectFreeStandingMasses"/> — architecture-scale airborne meshes
        /// in no ProceduralWall run, clustered by XZ proximity. Anchored on one member's
        /// transform; refreshed only by its own lane (the adoption sweep and the engulf split
        /// skip it).</summary>
        public bool IsFreeStanding;
        /// <summary>How many of this unit's members stand on the floor (foot under the 1.0 wu
        /// airborne bar, admitted because their top clears 2.5 wu over the room floor — the
        /// entrance's two sides). Diag only.</summary>
        public int FreeStandingFloorFooted;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>A unit must reach this high over its room's floor to be view-blocking at
        /// all; a lintel course lower than this is looked over from a standing head. Chosen at
        /// the STANDING PROP rule's own height cap (2.5 wu — "a course of masonry can never
        /// qualify" as a floor prop), so the two rules meet at one number.</summary>
        private const float FreeStandingMinTopWU = 2.5f;

        /// <summary>XZ AABB gap (wu) under which two candidate meshes are one formation. The
        /// entrance's rocks overlap or touch (gap 0.00 on every election row of the 405 log);
        /// half a world unit joins pieces that interlock and keeps two formations a hex apart
        /// separate.</summary>
        // ModBuild 413: 0.5 wu split the collapsed area into two units (z ranges [-0.4..0.8] and
        // [-4.05..-0.95], gap 0.55) that were then judged apart — the partial fade of
        // teilweises_faden.jpg. A formation is one piece within a hex; formations a hex apart
        // stay separate.
        private const float FreeStandingLinkXZ = 1.2f;

        /// <summary>Union-find bound. The entrance is 37 meshes; a scene with more airborne
        /// architecture than this in NO wall run is reported (cap hit) rather than clustered
        /// in O(n²).</summary>
        private const int FreeStandingMaxCandidates = 256;

        /// <summary>How many units the FREE-STANDING MASS clause names per rescan.</summary>
        private const int FreeStandingNameCap = 4;

        private readonly List<int> _freeCandidates = new();
        private readonly List<Bounds> _freeCandidateBounds = new();
        private readonly List<int> _freeParent = new();
        private readonly HashSet<Renderer> _freeListed = new();
        private readonly List<int> _freeClusterRoots = new();
        private readonly List<int> _freeClusterOf = new();
        private readonly List<MeshRenderer> _freeMembers = new();
        private readonly HashSet<Segment> _freeTouched = new();
        private readonly List<Component> _freeDeadKeys = new();
        private readonly List<Segment> _freeUnits = new();
        private readonly List<Segment> _freeReuseScratch = new();

        private int _censusFreeCandidates;
        private int _censusFreeClusters;
        private int _censusFreeUnits;
        private int _censusFreeMembers;
        private int _censusFreeBodyMembers;
        private int _censusFreeRefusedTop;
        private int _censusFreeRefusedEngulf;
        private int _censusFreeRefusedNoRoom;
        private int _censusFreeDropped;
        private bool _censusFreeCapHit;
        private bool _censusFreeNoFloor;
        private float _censusFreeMillis;

        /// <summary>
        /// The lane — see the file header. Runs inside the rescan between the stacked and the
        /// prop-unit phases; never per frame.
        /// </summary>
        private void CollectFreeStandingMasses()
        {
            float t0 = (float)RescanClock.Elapsed.TotalMilliseconds;
            _censusFreeCandidates = 0;
            _censusFreeClusters = 0;
            _censusFreeUnits = 0;
            _censusFreeMembers = 0;
            _censusFreeBodyMembers = 0;
            _censusFreeRefusedTop = 0;
            _censusFreeRefusedEngulf = 0;
            _censusFreeRefusedNoRoom = 0;
            _censusFreeDropped = 0;
            _censusFreeCapHit = false;
            _censusFreeNoFloor = false;
            _censusFreeRefusedDoorway = 0;
            _censusFreeRefusedNonOccluding = 0;
            _censusFreeDroppedEmpty = 0;
            _doorwayRefusalNames.Clear(); // one list per rescan: unit members, riders, hanging plants
            BeginDoorwayScan();           // ModBuild 413 — doorway kinds and blockade member lists
            _freeTouched.Clear();
            _freeUnits.Clear();

            try
            {
                CollectFreeStandingMassesCore();
            }
            finally
            {
                // Every unit that was not re-formed this rescan is gone: its members died,
                // moved under a wall's claim, or dropped below a bar. Restore-everywhere
                // discipline — a dropped unit may hold nothing hidden.
                _freeDeadKeys.Clear();
                foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
                {
                    if (kv.Value.IsFreeStanding && !_freeTouched.Contains(kv.Value))
                        _freeDeadKeys.Add(kv.Key);
                }
                foreach (Component dead in _freeDeadKeys)
                {
                    if (_live.Segments.TryGetValue(dead, out Segment? gone))
                    {
                        DropFreeStandingUnit(gone);
                        _censusFreeDropped++;
                    }
                    _live.Segments.Remove(dead);
                }
                foreach (Segment seg in _live.Segments.Values)
                {
                    if (seg.IsFreeStanding)
                        _freeUnits.Add(seg);
                }
                _censusFreeUnits = _freeUnits.Count;
                _censusFreeMillis = (float)RescanClock.Elapsed.TotalMilliseconds - t0;
            }
        }

        private void CollectFreeStandingMassesCore()
        {
            // Floor bar: the lowest anchored room floor, exactly as the mounted lane's airborne
            // bar is derived. No anchored room means no valid decision anywhere — nothing is
            // formed, and existing units die in the finally block above.
            float minFloorY = float.PositiveInfinity;
            for (int i = 0; i < _live.RoomFloorY.Count && i < _live.RoomFloorAnchored.Count; i++)
            {
                if (_live.RoomFloorAnchored[i] && _live.RoomFloorY[i] < minFloorY)
                    minFloorY = _live.RoomFloorY[i];
            }
            if (float.IsInfinity(minFloorY) || _factCount == 0)
            {
                _censusFreeNoFloor = true;
                return;
            }

            // Everything some OTHER segment drives is off limits: walls and their foliage,
            // siblings, body and stacked pieces, plus dressing a faded wall is still holding
            // (the mounted lane's own sticky rule — a rider cannot change owner while hidden).
            _freeListed.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                if (seg.IsFreeStanding)
                    continue;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null) _freeListed.Add(r);
                }
                foreach (MeshRenderer f in seg.Foliage)
                {
                    if (f != null) _freeListed.Add(f);
                }
                foreach (MeshRenderer s in seg.Siblings)
                {
                    if (s != null) _freeListed.Add(s);
                }
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer != null) _freeListed.Add(p.Renderer);
                }
                foreach (MountedProp p in seg.Stacked)
                {
                    if (p.Renderer != null) _freeListed.Add(p.Renderer);
                }
                if (seg.MountedState != 0 || seg.Fade > 0f)
                {
                    foreach (MountedProp p in seg.Mounted)
                    {
                        if (p.Renderer != null) _freeListed.Add(p.Renderer);
                    }
                }
            }

            // THE DOORWAY PASS FIRST (ModBuild 413, WallSegmentFade.Blockade.cs): every mesh under
            // a ProceduralDoorway is sorted by the game's own line — a scenario door's content is
            // never a candidate, a decoration doorway's content is ONE unit by subtree, formed
            // here before the XZ sweep so that sweep never sees it.
            CollectBlockadeMembers();
            FormBlockadeUnits();

            // Candidates. The cached fact bounds may only REJECT (they can be a cycle stale);
            // every accept below is re-read from the live renderer.
            float airborneBar = minFloorY + GroundExclusionHeightWU;
            float prefilterBar = airborneBar - CensusBoundsSlackWU;
            // FLOOR-FOOTED ARCHITECTURE (second commit of ModBuild 406): the entrance's two
            // SIDES go to the ground and hide the figures exactly as the lintel does. The
            // STANDING PROP ruling's own height term ("≤ 2.5 wu so a course of masonry can never
            // qualify") says a floor-footed mesh whose top clears 2.5 wu is NOT a floor prop, so
            // until now such a mesh belonged to nobody. A mesh whose TOP reaches the top bar
            // qualifies whatever its foot; a lower mesh must still be airborne.
            float topBar = minFloorY + FreeStandingMinTopWU;
            float prefilterTopBar = topBar - CensusBoundsSlackWU;
            _freeCandidates.Clear();
            _freeCandidateBounds.Clear();
            for (int fi = 0; fi < _factCount; fi++)
            {
                ref RendererFact f = ref _facts[fi];
                if (f.Mesh == null || f.Mod || f.FoliageShader || f.WaterSurface || f.Figure)
                    continue;
                if (f.Bounds.min.y < prefilterBar && f.Bounds.max.y < prefilterTopBar)
                    continue;
                if (!IsArchitectureScale(f.Bounds.size, CensusBoundsSlackWU))
                    continue;
                MeshRenderer r = f.Mesh!;
                if (r == null)
                    continue;
                if (!r.enabled && !_mountedTouched.ContainsKey(r))
                    continue; // the GAME disabled it — not ours to manage
                if (_freeListed.Contains(r))
                    continue;
                Bounds b = r.bounds;
                if ((b.min.y < airborneBar && b.max.y < topBar) || !IsArchitectureScale(b.size, 0f))
                    continue;
                // ModBuild 413: anything under a ProceduralDoorway belongs to the doorway pass —
                // a scenario door's content is refused and counted, a decoration doorway's
                // content is already in its ONE unit (or excluded there by its own terms) and
                // must not be re-clustered by XZ here.
                ProceduralDoorway? dwOf = DoorwayOf(r.transform);
                if (dwOf != null)
                {
                    if (ClassifyDoorway(dwOf, out string dwEvidence) == DoorwayKind.ScenarioDoor)
                    {
                        _censusFreeRefusedDoorway++;
                        NoteDoorwayRefusal("unit member", f.Name ?? r.name, b,
                            $"SCENARIO DOOR '{DoorwayLabel(dwOf)}' — {dwEvidence}");
                    }
                    continue;
                }
                if (IsDoorwayAssembly(r, b, f.Name ?? r.name, out string doorWhy))
                {
                    _censusFreeRefusedDoorway++;
                    NoteDoorwayRefusal("unit member", f.Name ?? r.name, b, doorWhy);
                    continue; // an arch piece a wall or tile placed (user ruling 2026-08-02)
                }
                if (IsWaterProtected(b))
                    continue; // fountain/pond (user ruling 2026-08-09)
                if (IsFigureOrActorRenderer(r))
                    continue; // FIGURES are never touched (round-7 ruling, Lights severity)
                // A wall run's own member is the wall path's business (cache, split or stacked);
                // taking it here would move a WALL MEMBER leftover into a unit instead of naming
                // the defect where it is.
                if (r.GetComponentInParent<ProceduralWall>() != null)
                    continue;
                if (IsNonOccludingRenderer(r))
                {
                    _censusFreeRefusedNonOccluding++;
                    continue; // a light shaft, fog or glow hides nothing and never fades
                }
                // STANDING PROP (both arms): a prop unit whose union reaches the floor is never
                // wall geometry on any path — the same choke point every wall lane asks.
                if (IsStandingFigureProp(r))
                    continue;
                if (_freeCandidates.Count >= FreeStandingMaxCandidates)
                {
                    _censusFreeCapHit = true;
                    break;
                }
                _freeCandidates.Add(fi);
                _freeCandidateBounds.Add(b);
            }
            _censusFreeCandidates = _freeCandidates.Count;
            if (_freeCandidates.Count == 0)
                return;

            // Union-find by XZ proximity.
            int n = _freeCandidates.Count;
            _freeParent.Clear();
            for (int i = 0; i < n; i++)
                _freeParent.Add(i);
            for (int i = 0; i < n; i++)
            {
                Bounds a = _freeCandidateBounds[i];
                for (int j = i + 1; j < n; j++)
                {
                    Bounds c = _freeCandidateBounds[j];
                    float gx = Mathf.Max(0f, Mathf.Max(a.min.x - c.max.x, c.min.x - a.max.x));
                    float gz = Mathf.Max(0f, Mathf.Max(a.min.z - c.max.z, c.min.z - a.max.z));
                    if (gx <= FreeStandingLinkXZ && gz <= FreeStandingLinkXZ)
                        FreeUnion(i, j);
                }
            }
            _freeClusterRoots.Clear();
            _freeClusterOf.Clear();
            for (int i = 0; i < n; i++)
            {
                int root = FreeFind(i);
                int idx = _freeClusterRoots.IndexOf(root);
                if (idx < 0)
                {
                    idx = _freeClusterRoots.Count;
                    _freeClusterRoots.Add(root);
                }
                _freeClusterOf.Add(idx);
            }
            _censusFreeClusters = _freeClusterRoots.Count;

            for (int cluster = 0; cluster < _freeClusterRoots.Count; cluster++)
                FormFreeStandingUnit(cluster);
        }

        /// <summary>The fat-axes half of the architecture guard alone (ModBuild 413): what a
        /// Foliage-family mesh is judged on, because the volume half reads a draped vine sheet as
        /// architecture.</summary>
        private static bool IsFatOnTwoAxes(Vector3 size)
        {
            int fatAxes = (size.x > MountedMaxSpanWU ? 1 : 0)
                + (size.y > MountedMaxSpanWU ? 1 : 0)
                + (size.z > MountedMaxSpanWU ? 1 : 0);
            return fatAxes >= 2;
        }

        /// <summary>The mounted lane's two architecture guards, as one predicate: bulky on two
        /// axes, or above the sconce volume. <paramref name="slack"/> widens the test for the
        /// cached-bounds prefilter so a stale box can only reject.</summary>
        private static bool IsArchitectureScale(Vector3 size, float slack)
        {
            int fatAxes = (size.x + slack > MountedMaxSpanWU ? 1 : 0)
                + (size.y + slack > MountedMaxSpanWU ? 1 : 0)
                + (size.z + slack > MountedMaxSpanWU ? 1 : 0);
            if (fatAxes >= 2)
                return true;
            float volume = (size.x + slack) * (size.y + slack) * (size.z + slack);
            return volume > MountedMaxMeshVolumeWU3;
        }

        private int FreeFind(int i)
        {
            while (_freeParent[i] != i)
            {
                _freeParent[i] = _freeParent[_freeParent[i]];
                i = _freeParent[i];
            }
            return i;
        }

        private void FreeUnion(int a, int b)
        {
            int ra = FreeFind(a), rb = FreeFind(b);
            if (ra != rb)
                _freeParent[rb] = ra;
        }

        private void FormFreeStandingUnit(int cluster)
        {
            // Union box and room, BEFORE anything is claimed: the bars are judged against the
            // unit's own room floor, and a refused cluster leaves every member exactly where the
            // mounted lane found it last build.
            Bounds union = default;
            bool have = false;
            for (int i = 0; i < _freeClusterOf.Count; i++)
            {
                if (_freeClusterOf[i] != cluster)
                    continue;
                if (!have) { union = _freeCandidateBounds[i]; have = true; }
                else union.Encapsulate(_freeCandidateBounds[i]);
            }
            if (!have)
                return;
            int room = NearestRoomFor(union);
            if (room < 0 || !RoomDecisionValid(room))
            {
                _censusFreeRefusedNoRoom++;
                return;
            }
            float floorY = _live.RoomFloorY[room];
            float foot = floorY + GroundExclusionHeightWU;
            float top = floorY + FreeStandingMinTopWU;
            // Members are re-judged against THIS room's floor (the candidate sweep used the
            // scene-wide minimum, which can only admit more): a piece that is neither airborne
            // nor tall enough to be view-blocking leaves the unit here. THE GROUND BAND STAYS
            // SOLID BY CONSTRUCTION: a ground-band renderer is one whose AABB TOP sits within
            // GroundExclusionHeightWU (1.0 wu) of its room's floor — the one predicate
            // StripGroundRenderers and the WALL-PATH AUDIT's "ground-band (solid by design)"
            // count share — and every member kept here has its top ≥ 2.5 wu over that floor
            // (airborne members by foot ≥ 1.0 AND the unit-level top bar below; floor-footed
            // members by this very test), so no member can be in the band, and the unit writes
            // to nothing but its own members.
            _freeMembers.Clear();
            int floorFooted = 0;
            union = default;
            have = false;
            for (int i = 0; i < _freeClusterOf.Count; i++)
            {
                if (_freeClusterOf[i] != cluster)
                    continue;
                Bounds b = _freeCandidateBounds[i];
                bool airborne = b.min.y >= foot;
                if (!airborne && b.max.y < top)
                    continue;
                MeshRenderer? r = _facts[_freeCandidates[i]].Mesh;
                if (r == null)
                    continue;
                if (!airborne)
                    floorFooted++;
                _freeMembers.Add(r);
                if (!have) { union = b; have = true; }
                else union.Encapsulate(b);
            }
            if (!have)
                return;
            if (union.max.y < floorY + FreeStandingMinTopWU)
            {
                _censusFreeRefusedTop++;
                return;
            }
            // THE GUARD THAT MUST KEEP HOLDING: a box that contains the room's floor is the
            // room (a tile, a ceiling), and the whole tile never fades.
            if (InsideRoomFraction(union, room) >= EngulfSampleFraction)
            {
                _censusFreeRefusedEngulf++;
                return;
            }

            FillFreeStandingUnit(_freeMembers, union, floorFooted, null, null);
        }

        /// <summary>
        /// THE SHARED FILL (ModBuild 413): the XZ-clustered path and the decoration-doorway path
        /// both end here. Identity across rescans, the adopted-group refresh member by member
        /// (toggle-native → Renderers on the wall MPB; no channel → Body on the dissolve swap),
        /// the tile-group pull, room association. Returns the segment, or null when a foreign
        /// segment already holds the requested anchor.
        /// </summary>
        private Segment? FillFreeStandingUnit(List<MeshRenderer> members, Bounds union, int floorFooted,
            Transform? anchorOverride, string? blockadeLabel)
        {
            Segment? seg = null;
            if (anchorOverride != null)
            {
                // A decoration doorway is keyed on its own transform — stable for the life of the
                // doorway, whatever its members do. An older XZ-clustered unit anchored on one of
                // these members is superseded and dropped through the ordinary restore path.
                if (_live.Segments.TryGetValue(anchorOverride, out Segment? existing))
                {
                    if (!existing.IsFreeStanding)
                        return null;
                    seg = existing;
                }
                else
                {
                    seg = new Segment { Anchor = anchorOverride, FromWallCache = false, IsFreeStanding = true };
                    _live.Segments.Add(anchorOverride, seg);
                }
                foreach (MeshRenderer m in members)
                {
                    if (_live.Segments.TryGetValue(m.transform, out Segment? old)
                        && old.IsFreeStanding && !ReferenceEquals(old, seg) && !_freeTouched.Contains(old))
                    {
                        DropFreeStandingUnit(old);
                        _live.Segments.Remove(m.transform);
                        _censusFreeDropped++;
                    }
                }
            }
            else
            {
                // Identity across rescans: a unit whose anchor is still a member keeps its segment —
                // its EMA, dwell and fade. Two old units merged by this cluster keep the more faded
                // one; the other is dropped through the ordinary restore path.
                _freeReuseScratch.Clear();
                foreach (MeshRenderer m in members)
                {
                    if (_live.Segments.TryGetValue(m.transform, out Segment? old)
                        && old.IsFreeStanding && !_freeTouched.Contains(old))
                    {
                        _freeReuseScratch.Add(old);
                    }
                }
                foreach (Segment old in _freeReuseScratch)
                {
                    if (seg == null || old.Fade > seg.Fade
                        || (old.Fade == seg.Fade && old.Renderers.Count > seg.Renderers.Count))
                    {
                        seg = old;
                    }
                }
                foreach (Segment old in _freeReuseScratch)
                {
                    if (ReferenceEquals(old, seg))
                        continue;
                    DropFreeStandingUnit(old);
                    _live.Segments.Remove(old.Anchor!);
                    _censusFreeDropped++;
                }
                if (seg == null)
                {
                    Transform anchor = ChooseFreeStandingAnchor(members);
                    if (_live.Segments.TryGetValue(anchor, out Segment? clash))
                    {
                        // A foreign segment already keyed on this transform (a split piece is keyed
                        // on its RENDERER, never a transform, so this is theoretical) — leave it.
                        if (!clash.IsFreeStanding)
                            return null;
                        seg = clash;
                    }
                    else
                    {
                        seg = new Segment { Anchor = anchor, FromWallCache = false, IsFreeStanding = true };
                        _live.Segments.Add(anchor, seg);
                    }
                }
            }
            _freeTouched.Add(seg);

            // Fill — the adopted-group refresh, member by member. A member with the wall-fade
            // toggle (the entrance's rocks are all 'wallfade-native') joins Renderers and is
            // driven by the same MPB the masonry gets; a member with no channel joins Body and
            // is driven by the dissolve swap, as a plain-mesh wall course is.
            BeginRefresh(seg);
            seg.PrevBody.Clear();
            seg.PrevBody.AddRange(seg.Body);
            seg.Body.Clear();
            foreach (MeshRenderer m in members)
            {
                if (CollectWallFadeInfo(m, seg))
                {
                    seg.Renderers.Add(m);
                }
                else
                {
                    if (!_mountedTouched.TryGetValue(m, out MountedProp? prop))
                        prop = ClassifyProp(m);
                    else if (!m.enabled && seg.BodyState == 0)
                        seg.BodyState = 1; // taken over HIDDEN: a Fade-0 unit must restore it, not keep it
                    seg.Body.Add(prop);
                    _censusFreeBodyMembers++;
                }
                _censusFreeMembers++;
            }
            seg.Bounds = union;
            seg.HasBounds = true;
            seg.FreeStandingFloorFooted = floorFooted;
            seg.BlockadeLabel = blockadeLabel;
            // THE CRYSTALS (ModBuild 413, "inklusive der Kristalle"): the map tile's
            // shader-adopted group members standing inside this unit's footprint leave that
            // group and join the unit, so ONE reading, one trigger and one ramp carry them.
            seg.FreeStandingPulled = PullTileGroupMembers(seg);
            if (seg.Body.Count > 0 && seg.ShaderNames == "?")
                seg.ShaderNames = "plain (no fade shader — dissolve-swap delivery)";
            FinishRefresh(seg);
            if (seg.BodyState != 0)
            {
                foreach (MountedProp prev in seg.PrevBody)
                {
                    if (prev.Renderer != null && !seg.Body.Contains(prev))
                        RestoreProp(prev);
                }
                if (seg.Body.Count == 0)
                    seg.BodyState = 0;
            }
            seg.PrevBody.Clear();
            seg.DoorRoot = null;
            AssociateRoom(seg);
            return seg;
        }

        private readonly List<MeshRenderer> _pullScratch = new();

        /// <summary>
        /// Pull the shader-adopted groups' renderers (the tile's crystals, a DOORWAY or gate
        /// segment's adopted pieces under a DECORATION doorway) that stand inside this unit's XZ
        /// footprint (+ the mounted link) and clear the ground band, out of their group and into
        /// the unit's own Renderers. Cache walls and split pieces are never touched; a renderer
        /// under a SCENARIO door's assembly is never touched. Runs inside the fill, after
        /// BeginRefresh and before FinishRefresh, every rescan — the adoption sweep re-forms the
        /// group first each rescan, so the pull is idempotent and the MPB is continuous: a pulled
        /// renderer keeps its block while the unit is fading and is cleared only when the unit is
        /// solid.
        /// </summary>
        private int PullTileGroupMembers(Segment unit)
        {
            int pulled = 0;
            Bounds box = unit.Bounds;
            float floorY = unit.RoomIndex >= 0 && unit.RoomIndex < _live.RoomFloorY.Count
                ? _live.RoomFloorY[unit.RoomIndex]
                : float.NegativeInfinity;
            int room = NearestRoomFor(box);
            if (room >= 0 && room < _live.RoomFloorY.Count)
                floorY = _live.RoomFloorY[room];
            float minX = box.min.x - MountedLinkMaxXZ, maxX = box.max.x + MountedLinkMaxXZ;
            float minZ = box.min.z - MountedLinkMaxXZ, maxZ = box.max.z + MountedLinkMaxXZ;
            float band = floorY + GroundExclusionHeightWU;
            foreach (Segment g in _live.Segments.Values)
            {
                if (ReferenceEquals(g, unit) || g.FromWallCache || g.IsFreeStanding || g.FromSplitRun
                    || g.Renderers.Count == 0)
                {
                    continue;
                }
                bool doorish = g.DoorRoot != null || g.IsGateColumn;
                _pullScratch.Clear();
                foreach (MeshRenderer r in g.Renderers)
                {
                    if (r == null)
                        continue;
                    Bounds b = r.bounds;
                    if (b.center.x < minX || b.center.x > maxX || b.center.z < minZ || b.center.z > maxZ)
                        continue;
                    if (b.max.y <= band)
                        continue; // the ground band stays solid
                    ProceduralDoorway? dw = DoorwayOf(r.transform);
                    if (dw != null && ClassifyDoorway(dw, out _) == DoorwayKind.ScenarioDoor)
                        continue; // a scenario door's own content never moves
                    if (doorish && dw == null)
                        continue; // a DOORWAY/gate segment's piece outside any decoration doorway stays
                    _pullScratch.Add(r);
                }
                if (_pullScratch.Count == 0)
                    continue;
                foreach (MeshRenderer r in _pullScratch)
                {
                    if (!CollectWallFadeInfo(r, unit))
                        continue; // a standing prop keeps its protection; nothing else refuses here
                    g.Renderers.Remove(r);
                    if (g.HasBlock && unit.Fade <= 0f)
                        r.SetPropertyBlock(null); // the group had it faded, the unit is solid: restore
                    unit.Renderers.Add(r);
                    unit.Bounds.Encapsulate(r.bounds);
                    pulled++;
                }
                // The group's decision box shrinks to what it still owns.
                g.HasBounds = false;
                foreach (MeshRenderer r in g.Renderers)
                {
                    if (r == null)
                        continue;
                    if (!g.HasBounds) { g.Bounds = r.bounds; g.HasBounds = true; }
                    else g.Bounds.Encapsulate(r.bounds);
                }
                foreach (MountedProp p in g.Body)
                {
                    if (p.Renderer == null)
                        continue;
                    if (!g.HasBounds) { g.Bounds = p.Renderer.bounds; g.HasBounds = true; }
                    else g.Bounds.Encapsulate(p.Renderer.bounds);
                }
            }
            return pulled;
        }

        private int _censusFreeRefusedDoorway;
        private int _censusFreeRidersRefusedDoorway;
        private int _censusFreeRefusedNonOccluding;
        private int _censusFreeRidersRefusedNonOccluding;
        private int _censusFreeDroppedEmpty;

        /// <summary>Unity's transparent queue starts at 3000 and the alpha-test (cutout) queue
        /// at 2450; everything from 2500 up is transparent, additive or overlay — drawn without
        /// depth writes, and a thing that writes no depth hides nothing.</summary>
        private const int NonOccludingRenderQueue = 2500;

        private readonly Dictionary<Shader, bool> _shaderNonOccluderVerdict = new();

        /// <summary>
        /// A RENDERER THAT CANNOT OCCLUDE (ModBuild 410; user: <i>"Lichtstrahlen blocken in
        /// keinster Weise die Sicht und sollen daher nie faden"</i>). The 407 log formed two
        /// units out of <c>'LightShaft_Prefab'</c> (<c>sh='LightShaftShd' q3000</c>, y[0.7..7.0])
        /// and one of them read <c>blk 9/16 … ON fade 1.00</c> — its AABB is the whole beam
        /// volume, and a ray through a beam was counted as a ray through a wall.
        ///
        /// <para>Non-occluding when EVERY material is: on a known non-occluder shader family
        /// (light shaft, fog, glow, beam, god ray, volumetric, particle, additive) or at a render
        /// queue ≥ <see cref="NonOccludingRenderQueue"/>. A mesh with one opaque or cutout
        /// material still occludes. The shader-family verdict is cached per Shader; the queue is
        /// a field read per material. Such a renderer is never a unit member (so the unit's own
        /// <see cref="RayHitsWallMesh"/> walk cannot see it — admission is the guard, there is no
        /// second test in the per-frame path), never a mesh rider, and never in the plain-mesh
        /// dissolve swap.</para>
        /// </summary>
        private bool IsNonOccludingRenderer(Renderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            bool any = false;
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    continue;
                any = true;
                Shader sh = m.shader;
                bool family = false;
                if (sh != null)
                {
                    if (!_shaderNonOccluderVerdict.TryGetValue(sh, out family))
                    {
                        family = IsNonOccluderShaderName(sh.name);
                        _shaderNonOccluderVerdict[sh] = family;
                    }
                }
                if (!family && m.renderQueue < NonOccludingRenderQueue)
                    return false; // one opaque/cutout material — it occludes
            }
            return any; // no material at all draws nothing and occludes nothing
        }

        private static bool IsNonOccluderShaderName(string name)
        {
            const System.StringComparison ic = System.StringComparison.OrdinalIgnoreCase;
            return name.IndexOf("LightShaft", ic) >= 0
                || name.IndexOf("Light Shaft", ic) >= 0
                || name.IndexOf("GodRay", ic) >= 0
                || name.IndexOf("Volumetric", ic) >= 0
                || name.IndexOf("Fog", ic) >= 0
                || name.IndexOf("Glow", ic) >= 0
                || name.IndexOf("Beam", ic) >= 0
                || name.IndexOf("Particle", ic) >= 0
                || name.IndexOf("Additive", ic) >= 0;
        }

        /// <summary>
        /// A UNIT EMPTIED AFTER ITS FILL IS DROPPED (ModBuild 410). The prop-unit cohesion pass
        /// runs after this lane and may move a unit's renderers to another owner; the 407 log
        /// shows <c>'CV_Pillar_Generic_02' 0 renderer(s) +0 plain, 2 toggle-native</c> — a segment
        /// with bounds and no meshes, which <see cref="RayHitsWallMesh"/> treats as "no meshes to
        /// ask: keep the broad-phase verdict", i.e. its whole AABB blocks. Runs in the mounted
        /// phase, before riders are attached.
        /// </summary>
        private void PruneEmptyFreeStandingUnits()
        {
            _censusFreeDroppedEmpty = 0;
            for (int i = _freeUnits.Count - 1; i >= 0; i--)
            {
                Segment unit = _freeUnits[i];
                if (unit.Renderers.Count > 0 || unit.Body.Count > 0)
                    continue;
                DropFreeStandingUnit(unit);
                if (unit.Anchor != null)
                    _live.Segments.Remove(unit.Anchor);
                _freeUnits.RemoveAt(i);
                _censusFreeDroppedEmpty++;
            }
            _censusFreeUnits = _freeUnits.Count;
        }

        /// <summary>
        /// DOORWAYS NEVER FADE (ModBuild 410, torbogen_faded.jpg — the stone gate wall with its
        /// cave rock and stalagmites dissolved as a unit while the round door plate hung in the
        /// air; user: <i>"Torbögen bzw. die Elemente mit den Toren/Türen dürfen nicht faden"</i>).
        ///
        /// <para>WHY THE UNIT LANE TOOK IT. The standing doorway rule protects fade-SHADER
        /// renderers hugging a door root (they land in a DOORWAY segment held solid) and the arch
        /// rect (<see cref="IsArchProtected"/>). The doorway ASSEMBLY — the gate wall the
        /// <see cref="ProceduralDoorway"/> generates under the door prop, its rock, its
        /// stalagmites, the plate — is toggle-native masonry in no ProceduralWall run, which is
        /// exactly the unit lane's population, and the arch test recognises the arch mesh, not
        /// the assembly around it. Worse, a unit anchored on the door plate's own transform is
        /// keyed on the SAME transform the DOORWAY segment would use.</para>
        ///
        /// <para>THE RULE, for unit members and riders alike: anything under a
        /// <see cref="ProceduralDoorway"/> or a <see cref="UnityGameEditorDoorProp"/>, anything
        /// within <see cref="DoorwayLinkMaxXZ"/> of a door root (the very radius that makes a
        /// frame a DOORWAY segment), and anything in an arch rect is refused and counted.</para>
        /// </summary>
        private bool IsDoorwayAssembly(Renderer r, Bounds probe, string name)
            => IsDoorwayAssembly(r, probe, name, out _);

        /// <summary>
        /// THE GAME'S LINE (ModBuild 413 — see WallSegmentFade.Blockade.cs). A renderer under a
        /// ProceduralDoorway is doorway content the lanes must refuse ONLY when that doorway's
        /// door prop is a SCENARIO DOOR — one the scenario state names in its DoorProps, the very
        /// lookup <c>UnityGameEditorDoorProp.OnCursorEnter</c> performs. A doorway the scenario does
        /// not name, or one with no door prop at all, is DECORATION: its content is its own ONE
        /// unit and is never refused here (riders and hanging plants inside it follow that unit).
        /// A renderer under NO doorway is refused only by the arch rect — an arch piece a wall
        /// or a tile placed. No name test, no distance test: the ModBuild 411 distance term
        /// refused the whole entrance for standing near its own door, the ModBuild 412 name
        /// term let a real gate's rubble fade; both are gone. <paramref name="why"/> carries the
        /// term and the evidence for the clause.
        /// </summary>
        private bool IsDoorwayAssembly(Renderer r, Bounds probe, string name, out string why)
        {
            why = string.Empty;
            ProceduralDoorway? dw = DoorwayOf(r.transform);
            if (dw == null)
            {
                if (!IsArchProtected(probe, name))
                    return false;
                why = "ARCH RECT (not under a doorway — an arch piece a wall or tile placed)";
                return true;
            }
            if (ClassifyDoorway(dw, out string evidence) == DoorwayKind.Decoration)
                return false;
            why = $"SCENARIO DOOR '{DoorwayLabel(dw)}' — {evidence}";
            return true;
        }

        private const int DoorwayRefusalNameCap = 4;
        private readonly List<string> _doorwayRefusalNames = new();

        private void NoteDoorwayRefusal(string lane, string name, Bounds b, string why)
        {
            if (_doorwayRefusalNames.Count >= DoorwayRefusalNameCap)
                return;
            _doorwayRefusalNames.Add(
                $"{lane} '{name}' y[{b.min.y:F1}..{b.max.y:F1}] c({b.center.x:F1},{b.center.z:F1}) — {why}");
        }

        private int _censusFreeRiders;
        private int _censusFreeRidersMesh;
        private int _censusFreeRidersParticles;
        private int _censusFreeRidersFloorFooted;
        private int _censusFreeRidersRefusedStanding;
        private int _censusFreeRidersRefusedArchitecture;

        /// <summary>A floor-footed rider may not sit lower than this under its unit's room floor —
        /// deeper is a renderer buried under the floor plane (a fog volume, a floor decal's
        /// oversized box), not something growing on the formation's base.</summary>
        private const float FreeStandingRiderMaxUnderFloorWU = 0.5f;

        /// <summary>
        /// RIDERS OF A FREE-STANDING UNIT (third commit of ModBuild 406; kristalle_faden.jpg —
        /// the formation fades, the purple crystal clusters growing on its base stay solid and
        /// stick out of the faded rock).
        ///
        /// <para>WHY THE MOUNTED LANE LEFT THEM. It DOES elect a free-standing unit as an owner
        /// (the election walks every segment with bounds), but its first term is the airborne
        /// bar — a candidate whose anchor is under floor + 1.0 wu is <c>belowBar</c> and is never
        /// bound to anything. A crystal growing on the arch's ROCK BASE has its foot at 0.2-0.8 wu:
        /// it stands on the formation, not on the room floor, and no rule could tell the two
        /// apart because a wall has no footprint a prop could stand INSIDE. A formation has one.</para>
        ///
        /// <para>THE RULE. Runs inside the mounted pass, after its election and before its leavers
        /// loop (so a rider re-adopted here is not restored one loop later, and a rider the sticky
        /// rule already carried is skipped). A drawing mesh or particle system that no segment
        /// owns rides the nearest unit when it is either AIRBORNE and hugs the unit exactly as
        /// wall-mounted dressing hugs a wall (XZ gap ≤ <see cref="MountedLinkMaxXZ"/>, inside the
        /// unit's Y span plus the cap overhang), or FLOOR-FOOTED with its anchor INSIDE the unit's
        /// XZ footprint — bounded by the footprint so a floor prop beside the formation is
        /// untouched. Every mounted-lane refusal stands: figures, the doorway arch, water,
        /// standing props by the two-arm rule (an obstacle crystal is a game element and keeps
        /// its protection), and the dressing-size guard (architecture is the unit's own business,
        /// never a rider). Lights are never written: only Renderers are ever listed, and the
        /// MountedProp delivery mutates the renderer's MPB, a particle system's start colour /
        /// size / emission rate and <c>Renderer.enabled</c>, snapshotted and restored bit-for-bit.
        /// Sticky-while-faded, the dissolve ramp and the restore discipline are the mounted
        /// lane's own, unchanged: a rider is a <see cref="MountedProp"/> on <c>seg.Mounted</c>.</para>
        /// </summary>
        private void CollectFreeStandingRiders(float minFloorY)
        {
            _censusFreeRiders = 0;
            _censusFreeRidersMesh = 0;
            _censusFreeRidersParticles = 0;
            _censusFreeRidersFloorFooted = 0;
            _censusFreeRidersRefusedStanding = 0;
            _censusFreeRidersRefusedArchitecture = 0;
            _censusFreeRidersRefusedDoorway = 0;
            _censusFreeRidersRefusedNonOccluding = 0;
            PruneEmptyFreeStandingUnits(); // after the prop-unit pass, before anything rides
            if (_freeUnits.Count == 0 || float.IsInfinity(minFloorY) || _factCount == 0)
                return;
            // Reach: the union of every unit's footprint plus the link, so the fact sweep
            // rejects almost everything on cached bounds before a live read.
            float reachMinX = float.PositiveInfinity, reachMaxX = float.NegativeInfinity;
            float reachMinZ = float.PositiveInfinity, reachMaxZ = float.NegativeInfinity;
            foreach (Segment unit in _freeUnits)
            {
                if (!unit.HasBounds)
                    continue;
                if (unit.Bounds.min.x < reachMinX) reachMinX = unit.Bounds.min.x;
                if (unit.Bounds.max.x > reachMaxX) reachMaxX = unit.Bounds.max.x;
                if (unit.Bounds.min.z < reachMinZ) reachMinZ = unit.Bounds.min.z;
                if (unit.Bounds.max.z > reachMaxZ) reachMaxZ = unit.Bounds.max.z;
            }
            if (float.IsInfinity(reachMinX))
                return;
            float reach = MountedLinkMaxXZ + CensusBoundsSlackWU;
            reachMinX -= reach; reachMaxX += reach;
            reachMinZ -= reach; reachMaxZ += reach;
            float floorGate = minFloorY - FreeStandingRiderMaxUnderFloorWU - CensusBoundsSlackWU;

            for (int fi = 0; fi < _factCount; fi++)
            {
                ref RendererFact f = ref _facts[fi];
                if (f.R == null || f.Mod || f.Figure || !f.Mountable)
                    continue;
                if (f.Anchor.y < floorGate)
                    continue;
                bool inReach = f.Bounds.max.x >= reachMinX && f.Bounds.min.x <= reachMaxX
                    && f.Bounds.max.z >= reachMinZ && f.Bounds.min.z <= reachMaxZ;
                if (!inReach && f.Particles)
                    inReach = f.Anchor.x >= reachMinX && f.Anchor.x <= reachMaxX
                        && f.Anchor.z >= reachMinZ && f.Anchor.z <= reachMaxZ;
                if (!inReach)
                    continue;
                Renderer c = f.R!;
                if (c == null || _mountedOwned.Contains(c) || _attachmentOwned.ContainsKey(c))
                    continue;
                if (!c.enabled && !_mountedTouched.ContainsKey(c))
                    continue; // the GAME disabled it — not ours to manage
                bool particles = f.Particles;
                Bounds b = c.bounds;
                Vector3 anchorPt = particles
                    ? c.transform.position
                    : new Vector3(b.center.x, b.min.y, b.center.z);
                float anchorY = anchorPt.y;
                float topY = particles ? anchorY : b.max.y;

                Segment? best = null;
                float bestGap = float.PositiveInfinity;
                bool bestFloorFooted = false;
                foreach (Segment unit in _freeUnits)
                {
                    if (!unit.HasBounds || !RoomDecisionValid(unit.RoomIndex))
                        continue;
                    float gap = particles
                        ? HorizontalGap(unit.Bounds, anchorPt)
                        : HorizontalGap(unit.Bounds, b);
                    if (gap > MountedLinkMaxXZ || gap >= bestGap)
                        continue;
                    float floorY = _live.RoomFloorY[unit.RoomIndex];
                    bool airborne = anchorY >= floorY + MountedClearanceWU;
                    if (airborne)
                    {
                        if (anchorY > unit.Bounds.max.y + MountedLinkMaxAboveTopWU)
                            continue; // floats above the formation, not in it
                        if (topY < unit.Bounds.min.y)
                            continue; // below its span
                    }
                    else
                    {
                        // Floor-footed: INSIDE the footprint only — it stands on the
                        // formation's own rock. A prop beside the formation has its anchor
                        // outside the box and is untouched.
                        if (anchorPt.x < unit.Bounds.min.x || anchorPt.x > unit.Bounds.max.x
                            || anchorPt.z < unit.Bounds.min.z || anchorPt.z > unit.Bounds.max.z)
                        {
                            continue;
                        }
                        if (anchorY < floorY - FreeStandingRiderMaxUnderFloorWU)
                            continue; // buried under the floor plane
                    }
                    best = unit;
                    bestGap = gap;
                    bestFloorFooted = !airborne;
                }
                if (best == null)
                    continue;

                Bounds archProbe = particles ? new Bounds(anchorPt, Vector3.zero) : b;
                if (IsDoorwayAssembly(c, archProbe, f.Name ?? c.name, out string riderDoorWhy))
                {
                    _censusFreeRidersRefusedDoorway++;
                    NoteDoorwayRefusal("rider", f.Name ?? c.name, archProbe, riderDoorWhy);
                    continue; // doorways never fade (user rulings 2026-08-07 / 2026-09-04)
                }
                if (IsWaterProtected(archProbe))
                    continue; // fountain/pond (user ruling 2026-08-09)
                if (IsFigureOrActorRenderer(c))
                    continue; // FIGURES are never touched (round-7 ruling, Lights severity)
                if (!particles && IsNonOccludingRenderer(c))
                {
                    _censusFreeRidersRefusedNonOccluding++;
                    continue; // a light-shaft or glow MESH never fades; particle systems are
                              // effects and keep riding as the mounted lane's own class
                }
                // ModBuild 413: a Foliage-family mesh (the game's own "this is a plant") is judged
                // on the two-fat-axes term only — a 1.1 x 1.5 x 1.0 wu vine sheet is 1.65 wu³ and
                // the volume term read it as architecture.
                if (!particles && (f.FoliageShader ? IsFatOnTwoAxes(b.size) : IsArchitectureScale(b.size, 0f)))
                {
                    _censusFreeRidersRefusedArchitecture++;
                    continue; // architecture is judged by the unit's own bars, never as dressing
                }
                if (IsStandingFigureProp(c))
                {
                    _censusFreeRidersRefusedStanding++;
                    continue; // a floor-standing prop unit (an obstacle crystal) is a game element
                }
                if (best.Mounted.Count >= MountedMaxPerSegment)
                    continue;
                if (!_mountedTouched.TryGetValue(c, out MountedProp? prop))
                    prop = ClassifyProp(c);
                best.Mounted.Add(prop);
                _mountedOwned.Add(c);
                _attachmentOwned[c] = new OwnerRef(best, "free-standing rider");
                NoteOwnershipChange(c,
                    $"mounted:'{(best.Anchor != null ? best.Anchor.name : "<dead>")}'(free-standing rider)");
                _censusFreeRiders++;
                if (particles)
                    _censusFreeRidersParticles++;
                else
                    _censusFreeRidersMesh++;
                if (bestFloorFooted)
                    _censusFreeRidersFloorFooted++;
            }
        }

        /// <summary>The rider figures for one unit's clause entry, read off its live Mounted list
        /// (sticky carries included), so the count is what rides, not what this rescan adopted.</summary>
        private void CountFreeStandingRiders(Segment unit, out int total, out int meshes,
            out int particles, out int floorFooted)
        {
            total = 0; meshes = 0; particles = 0; floorFooted = 0;
            float floorY = unit.RoomIndex >= 0 && unit.RoomIndex < _live.RoomFloorY.Count
                ? _live.RoomFloorY[unit.RoomIndex] : float.NegativeInfinity;
            foreach (MountedProp p in unit.Mounted)
            {
                Renderer r = p.Renderer;
                if (r == null)
                    continue;
                total++;
                bool ps = r is ParticleSystemRenderer;
                if (ps) particles++; else meshes++;
                float anchorY = ps ? r.transform.position.y : r.bounds.min.y;
                if (anchorY < floorY + MountedClearanceWU)
                    floorFooted++;
            }
        }

        /// <summary>The member that names the unit: the lowest instance id among members whose
        /// name is not a LOD level (so the diag reads 'CV_Generic_Rock_04', not 'LOD0'), else the
        /// lowest id of all. Instance ids are stable for the life of the object, so the anchor
        /// is stable across rescans while that member lives.</summary>
        private static Transform ChooseFreeStandingAnchor(List<MeshRenderer> members)
        {
            MeshRenderer? best = null;
            bool bestNamed = false;
            foreach (MeshRenderer m in members)
            {
                bool named = !m.name.StartsWith("LOD", System.StringComparison.OrdinalIgnoreCase);
                if (best == null || (named && !bestNamed)
                    || (named == bestNamed && m.GetInstanceID() < best.GetInstanceID()))
                {
                    best = m;
                    bestNamed = named;
                }
            }
            return best!.transform;
        }

        /// <summary>Everything a unit may have hidden goes back — the same restore set every
        /// other segment death runs — and the caller removes the key.</summary>
        private void DropFreeStandingUnit(Segment seg)
        {
            if (seg.HasBlock)
            {
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                        r.SetPropertyBlock(null);
                }
                seg.HasBlock = false;
            }
            RestoreSegmentFoliage(seg);
            RestoreSegmentSiblings(seg);
            RestoreSegmentMounted(seg);
            RestoreSegmentStacked(seg);
            RestoreSegmentBody(seg);
        }

        /// <summary>
        /// The clause appended to the FREE-STANDING MASS line: what this lane formed this rescan
        /// and, for up to <see cref="FreeStandingNameCap"/> units, what the decision currently
        /// reads for it. A unit formed THIS rescan has not been evaluated yet and says so rather
        /// than printing a 0/0 that reads as "hides nothing".
        /// </summary>
        private string FreeStandingUnitsClause()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append(" FREE-STANDING UNITS (ModBuild 406 — an architecture-scale formation in no ")
              .Append("ProceduralWall run is now its OWN fadeable segment, judged on its own meshes ")
              .Append("by the same ray test, bars and MPB path as a wall; that is the case the ")
              .Append("sentence before this one calls open): ");
            if (_censusFreeNoFloor)
            {
                sb.Append("NOT RUN — no tile-anchored room floor this rescan, so no airborne bar ")
                  .Append("exists and nothing could be formed");
            }
            else if (_censusFreeUnits == 0)
            {
                sb.Append("no unit formed — ")
                  .Append(_censusFreeCandidates).Append(" candidate(s) passed the airborne + ")
                  .Append("architecture + no-wall-run + standing-prop tests, ")
                  .Append(_censusFreeClusters).Append(" cluster(s), of which ")
                  .Append(_censusFreeRefusedTop).Append(" refused for a top under ")
                  .Append(FreeStandingMinTopWU.ToString("0.0")).Append(" wu over the floor, ")
                  .Append(_censusFreeRefusedEngulf).Append(" refused as room-engulfing (a tile ")
                  .Append("or ceiling — never fades whole), ")
                  .Append(_censusFreeRefusedNoRoom).Append(" refused for no decision-valid room")
                  .Append("; refused before clustering: ").Append(_censusFreeRefusedDoorway)
                  .Append(" doorway, ").Append(_censusFreeRefusedNonOccluding)
                  .Append(" non-occluding");
            }
            else
            {
                sb.Append(_censusFreeUnits).Append(" unit(s) live from ")
                  .Append(_censusFreeCandidates).Append(" candidate(s) in ")
                  .Append(_censusFreeClusters).Append(" cluster(s) (")
                  .Append(_censusFreeMembers).Append(" member(s), ")
                  .Append(_censusFreeBodyMembers).Append(" of them plain-mesh via the dissolve ")
                  .Append("swap; refused: ").Append(_censusFreeRefusedTop).Append(" too low, ")
                  .Append(_censusFreeRefusedEngulf).Append(" room-engulfing, ")
                  .Append(_censusFreeRefusedNoRoom).Append(" no valid room, ")
                  .Append(_censusFreeRefusedDoorway).Append(" doorway, ")
                  .Append(_censusFreeRefusedNonOccluding).Append(" non-occluding; ")
                  .Append(_censusFreeDropped).Append(" earlier unit(s) dropped, ")
                  .Append(_censusFreeDroppedEmpty)
                  .Append(" emptied by the prop-unit pass and dropped): ");
                int named = 0;
                foreach (Segment seg in _freeUnits)
                {
                    if (named >= FreeStandingNameCap)
                        break;
                    if (named > 0)
                        sb.Append("; ");
                    named++;
                    string name = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    Bounds b = seg.Bounds;
                    float floorY = seg.RoomIndex >= 0 && seg.RoomIndex < _live.RoomFloorY.Count
                        ? _live.RoomFloorY[seg.RoomIndex] : 0f;
                    // ModBuild 413: a decoration-doorway unit says so, with the evidence term, and
                    // every unit says how many crystals it pulled out of the tile group.
                    if (seg.BlockadeLabel != null)
                        sb.Append("BLOCKADE UNIT ").Append(seg.BlockadeLabel).Append(" ONE reading — ");
                    sb.Append('\'').Append(name).Append("' ")
                      .Append(seg.Renderers.Count).Append(" renderer(s) +")
                      .Append(seg.Body.Count).Append(" plain (")
                      .Append(seg.FreeStandingPulled).Append(" pulled from the tile group), ")
                      .Append(seg.ToggleNative).Append(" toggle-native, ")
                      .Append(seg.FreeStandingFloorFooted).Append(" of ")
                      .Append(seg.Renderers.Count + seg.Body.Count)
                      .Append(" members floor-footed, AABB c(")
                      .Append(b.center.x.ToString("F1")).Append(',')
                      .Append(b.center.y.ToString("F1")).Append(',')
                      .Append(b.center.z.ToString("F1")).Append(") s(")
                      .Append(b.size.x.ToString("F1")).Append(',')
                      .Append(b.size.y.ToString("F1")).Append(',')
                      .Append(b.size.z.ToString("F1")).Append(") foot ")
                      .Append((b.min.y - floorY).ToString("F2")).Append(" / top ")
                      .Append((b.max.y - floorY).ToString("F2")).Append(" wu over room ")
                      .Append(seg.RoomIndex).Append("'s floor, ");
                    CountFreeStandingRiders(seg, out int riders, out int riderMeshes,
                                            out int riderParticles, out int riderFloor);
                    sb.Append("riders ").Append(riders).Append(" (").Append(riderMeshes)
                      .Append(" mesh, ").Append(riderParticles).Append(" particle, floor-footed ")
                      .Append(riderFloor).Append("), ");
                    if (seg.LastRoomTotal <= 0)
                    {
                        sb.Append("NOT YET JUDGED (formed this rescan — the first coverage ")
                          .Append("reading is next frame; the diag row carries FREE beside it)");
                    }
                    else
                    {
                        sb.Append("blk ").Append(seg.LastBlocked).Append('/')
                          .Append(seg.LastRoomTotal).Append(" v").Append(seg.LastRoomVisible)
                          .Append(" raw ").Append(seg.LastRaw.ToString("F2"))
                          .Append(" ema ").Append(seg.Smooth.ToString("F2"))
                          .Append(seg.State ? " ON" : " off")
                          .Append(" fade ").Append(seg.Fade.ToString("F2"));
                    }
                }
                if (_freeUnits.Count > named)
                    sb.Append("; +").Append(_freeUnits.Count - named).Append(" more");
                sb.Append(". Riders adopted this rescan: ").Append(_censusFreeRiders)
                  .Append(" (").Append(_censusFreeRidersMesh).Append(" mesh, ")
                  .Append(_censusFreeRidersParticles).Append(" particle, floor-footed ")
                  .Append(_censusFreeRidersFloorFooted).Append("; refused ")
                  .Append(_censusFreeRidersRefusedStanding).Append(" standing prop(s) and ")
                  .Append(_censusFreeRidersRefusedArchitecture)
                  .Append(" architecture-scale mesh(es), ")
                  .Append(_censusFreeRidersRefusedDoorway)
                  .Append(" doorway, ").Append(_censusFreeRidersRefusedNonOccluding)
                  .Append(" non-occluding mesh(es) — the sticky carries are in the per-unit ")
                  .Append("counts, not here)");
            }
            if (!_censusFreeNoFloor)
                sb.Append(DoorwayCensusClause()); // ModBuild 413 — every doorway, its kind, the evidence
            if (_doorwayRefusalNames.Count > 0)
            {
                // ModBuild 411: WHICH term refused, and where the nearest door root stands, so a
                // formation refused beside a door and a doorway refused as itself read differently.
                sb.Append(" Doorway refusals named (up to ").Append(DoorwayRefusalNameCap)
                  .Append(", structure only — ancestry or arch rect, never distance): ")
                  .Append(string.Join("; ", _doorwayRefusalNames)).Append('.');
            }
            if (_censusFreeCapHit)
            {
                sb.Append(" [CANDIDATE CAP ").Append(FreeStandingMaxCandidates)
                  .Append(" HIT — the sweep stopped early, a truncated census is not absence]");
            }
            sb.Append(" — ").Append(_censusFreeMillis.ToString("F2"))
              .Append(" ms this rescan, per frame it costs one wall segment.");
            return sb.ToString();
        }
    }
}
