using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// OCCLUDER VERDICTS — the line that answers "diese Wand verdeckt offensichtlich Felder und
/// fadet trotzdem nicht" without another build.
///
/// <para><b>WHY IT EXISTS.</b> User report 2026-09-06, <c>hauswand1.jpg</c>: in a symmetric city
/// scenario one tall timber-framed house never fades from one side, fades normally from the
/// other, and its mirrored twin on the far side of the level behaves identically. Every wall
/// instrument this subsystem already ships was asked that question in the ModBuild 463 log and
/// none of them could answer it:</para>
/// <list type="bullet">
///   <item>the <c>diag:</c> line names the THREE widest-covering segments, so a wall reading
///   0.00 forever is exactly the wall it never prints;</item>
///   <item>the <c>PER-WALL</c> line names eight and printed "<c>+1 more</c>" on a nine-wall
///   pass, so at least one deciding wall was never named on any line of that session;</item>
///   <item>and both of them identify a wall by <c>Anchor.name</c>, which is NOT unique. The same
///   log's FAIL-SAFE GAPS line reads
///   <c>'Wall 1','Wall 2',…,'Wall 8','Wall 1','Wall 2'</c> — the scenario's two map tiles each
///   carry their own 'Wall 1'. In a mirror-symmetric level that is precisely the pair the user
///   is reporting, and no existing line can tell the two apart.</item>
/// </list>
///
/// <para><b>WHAT IT ADDS THAT NO EXISTING LINE CARRIES.</b> Three things, and each one is a
/// different cause the report is compatible with:</para>
/// <list type="number">
///   <item><b>OFF-GRID HEXES.</b> The SAMPLE GRID line already alarms that
///   <c>N of M distinct CMap(s) got a sample grid</c> — the 463 log reads 1 of 2, with 34 of 78
///   keyed hexes "in the registry and in NO room's denominator". What it does NOT say is whether
///   any WALL stands between the head and those hexes. This line measures exactly that, with the
///   SAME ray acceptance the coverage metric uses. A wall reading <c>blk 0/16</c> while hiding
///   nine off-grid hexes is not a threshold decision and no bar can rescue it: the hexes it hides
///   are in no denominator, so its coverage is 0.00 by construction — and from the other side of
///   the same wall the hexes it hides ARE in the denominator, which is the view dependence and
///   the mirrored twin in one mechanism.</item>
///   <item><b>THE CEILING.</b> <c>RoomBlockedFraction</c> puts only frustum-visible samples in the
///   numerator and the WHOLE room grid in the denominator, so a wall's coverage can never exceed
///   <c>visible/total</c>. When the head is close enough that the grid is mostly off-screen, that
///   ceiling can sit BELOW the enter bar and no wall in the room can fade whatever it hides. The
///   <c>diag:</c> line prints <c>vis N/16</c> and the bars, twelve columns apart, and nothing has
///   ever compared them. This line does the comparison and names the verdict.</item>
///   <item><b>THE SEGMENTS THAT WERE NEVER CONSIDERED.</b> Held solid by a fail-safe or a standing
///   ruling, they appear in NO coverage line at all today, which reads exactly like a wall that
///   does not exist. They are listed here with the term that holds them.</item>
/// </list>
///
/// <para><b>IDENTITY.</b> Every named wall carries <c>#instanceID</c>. That is the only stable id
/// available — <c>Anchor.name</c> repeats, and a hierarchy path is not membership.</para>
///
/// <para><b>COST.</b> The classification walk is the segment table and no ray work. The hex sweep
/// runs ONLY for segments that were considered and came out solid, only against hexes that are
/// in the head's frustum and that the game itself calls playable, and at most
/// <c>VerdictMaxSegmentsPerPass</c> segments per pass from a rotating cursor — so a scenario with
/// no off-grid CMap and no solid wall pays a walk of the segment table and nothing else. Emission
/// is change-gated on the verdict classes and the named set (never on the numbers, which move
/// every frame) with a <c>VerdictHeartbeatSeconds</c> heartbeat, so a stationary player gets one
/// line and not a flood.</para>
///
/// <para><b>WHY A SEPARATE FILE.</b> So it cannot collide with concurrent work on the other parts.
/// It declares no static field, so <c>check-partial-order.py</c>'s hazard cannot arise here.</para>
///
/// <para>MULTIPLAYER: read-only, presentation-only, local. Nothing here writes game state, no
/// renderer is touched and nothing is networked.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>Segments whose hexes are swept per pass, from a rotating cursor. The sweep is
        /// (segment x in-frustum playable hex) ray pairs; 24 against a 200-hex registry is the
        /// same order of work as one coverage pass, and it runs at most every
        /// <see cref="VerdictThrottleSeconds"/>.</summary>
        private const int VerdictMaxSegmentsPerPass = 24;

        /// <summary>Names printed per pass, contradicted classes first. Past this the line prints
        /// the residue COUNT — a truncated list is not absence.</summary>
        private const int VerdictMaxNamed = 8;

        private const float VerdictThrottleSeconds = 2f;

        /// <summary>Re-print an unchanged verdict set this often, so a held instrument cannot be
        /// mistaken for a stopped one.</summary>
        private const float VerdictHeartbeatSeconds = 20f;

        private float _nextVerdictTime;
        private float _nextVerdictHeartbeat;
        private string _lastVerdictKey = string.Empty;
        private int _verdictCursor;

        // Scratch, reused: nothing allocates per pass beyond the emitted string.
        private readonly HashSet<object> _verdictGriddedMaps = new();
        private readonly List<Vector3> _verdictHex = new();       // in-frustum PLAYABLE hex centres
        private readonly List<object> _verdictHexMap = new();     // …their CMap, index-aligned
        private readonly List<bool> _verdictHexGridded = new();   // …whether that CMap got a grid
        private readonly List<string> _verdictOffGridNamed = new();
        private readonly List<string> _verdictCeilingNamed = new();
        private readonly List<string> _verdictBelowNamed = new();
        private readonly List<string> _verdictHeldNamed = new();
        private readonly List<string> _verdictNames = new();
        private readonly System.Text.StringBuilder _verdictSb = new();

        /// <summary>
        /// One pass of the occluder-verdict instrument. <paramref name="head"/> may be null (no
        /// head camera this frame): the line is skipped rather than reporting a frustum nobody
        /// looked through.
        /// </summary>
        private void LogOccluderVerdicts(Camera? head, Vector3 headPos, float now)
        {
            if (head == null || _live.Segments.Count == 0 || now < _nextVerdictTime)
                return;
            _nextVerdictTime = now + VerdictThrottleSeconds;

            // ── Which CMaps of the tile registry actually got a sample grid ──────────────────
            // Rebuilt here rather than read from _censusMapsSampled: that set belongs to the
            // change-gated SAMPLE GRID census, which may not have run this rescan, and a stale
            // membership set would silently invert this line's whole reading.
            _verdictGriddedMaps.Clear();
            for (int r = 0; r < _live.RoomSampleCount.Count; r++)
            {
                object? roomMap = r < _roomMapKeys.Count ? _roomMapKeys[r] : null;
                if (roomMap == null || _live.RoomSampleCount[r] <= 0)
                    continue;
                if (r < _roomTileGrid.Count && _roomTileGrid[r])
                    _verdictGriddedMaps.Add(roomMap);
            }

            // ── The hexes THIS head can see, as the game classifies them ────────────────────
            // PLAYABLE only (ClassifyHex's own verdict, index-aligned with the hex list): an EDGE
            // hex or one an obstacle stands on is not a "Feld" the user is asking about, and it is
            // not in any denominator either, so counting it here would manufacture the alarm.
            _verdictHex.Clear();
            _verdictHexMap.Clear();
            _verdictHexGridded.Clear();
            int registryHexes = 0, offGridHexes = 0;
            foreach (KeyValuePair<object, List<Vector3>> kv in _tilesByMap)
            {
                bool gridded = _verdictGriddedMaps.Contains(kv.Key);
                List<byte>? why = _tileWhyByMap.TryGetValue(kv.Key, out List<byte> w) ? w : null;
                List<Vector3> hexes = kv.Value;
                registryHexes += hexes.Count;
                if (!gridded)
                    offGridHexes += hexes.Count;
                for (int i = 0; i < hexes.Count; i++)
                {
                    if (why != null && i < why.Count && why[i] != HexPlayable)
                        continue;
                    Vector3 at = hexes[i];
                    at.y += FloorSampleEpsilon; // the plane the coverage metric samples on
                    if (!OcclusionFade.InFrustum(head, at))
                        continue;
                    _verdictHex.Add(at);
                    _verdictHexMap.Add(kv.Key);
                    _verdictHexGridded.Add(gridded);
                }
            }
            int inViewOffGrid = 0;
            for (int i = 0; i < _verdictHexGridded.Count; i++)
            {
                if (!_verdictHexGridded[i])
                    inViewOffGrid++;
            }

            float onBar = WallFadeTuning.On;
            float offBar = WallFadeTuning.Off;

            // ── Classify EVERY segment; ray work only for the solid, considered ones ────────
            int occluders = 0, clear = 0, offGridSolid = 0, ceiling = 0, belowBar = 0;
            int notConsidered = 0, noBounds = 0, noRoomGrid = 0, doorways = 0, gates = 0, engulf = 0;
            int sweptSegs = 0, unsweptSolid = 0;
            _verdictOffGridNamed.Clear();
            _verdictCeilingNamed.Clear();
            _verdictBelowNamed.Clear();
            _verdictHeldNamed.Clear();

            int index = -1;
            int total = _live.Segments.Count;
            foreach (Segment seg in _live.Segments.Values)
            {
                index++;
                string id = seg.Anchor != null
                    ? $"'{seg.Anchor.name}'#{seg.Anchor.GetInstanceID()}"
                    : "'<dead>'#0";
                if (!seg.HasBounds)
                {
                    notConsidered++;
                    noBounds++;
                    if (_verdictHeldNamed.Count < VerdictMaxNamed)
                        _verdictHeldNamed.Add($"{id} NOT-CONSIDERED:NO-BOUNDS (owns no renderer "
                            + "with bounds, so AssociateRooms skips it and RoomIndex stays -1)");
                    continue;
                }
                if (seg.DoorRoot != null || seg.IsGateColumn)
                {
                    notConsidered++;
                    if (seg.DoorRoot != null)
                        doorways++;
                    else
                        gates++;
                    if (_verdictHeldNamed.Count < VerdictMaxNamed)
                        _verdictHeldNamed.Add($"{id} NOT-CONSIDERED:"
                            + (seg.DoorRoot != null ? "DOORWAY" : "GATE")
                            + " (standing user ruling — doors and arches never fade)");
                    continue;
                }
                if (seg.Engulfing)
                {
                    notConsidered++;
                    engulf++;
                    if (_verdictHeldNamed.Count < VerdictMaxNamed)
                        _verdictHeldNamed.Add($"{id} NOT-CONSIDERED:ENGULF (unsplittable "
                            + "room-engulfing wall)");
                    continue;
                }
                if (!RoomDecisionValid(seg.RoomIndex))
                {
                    notConsidered++;
                    noRoomGrid++;
                    if (_verdictHeldNamed.Count < VerdictMaxNamed)
                        _verdictHeldNamed.Add($"{id} NOT-CONSIDERED:NO-ROOM-GRID r{seg.RoomIndex} "
                            + "(room unanchored or holds no sample grid — held FAIL-SAFE solid)");
                    continue;
                }
                if (seg.State)
                {
                    occluders++;
                    continue;
                }

                // Solid AND considered: this is the population the user is pointing at.
                float ceil = seg.LastRoomTotal > 0
                    ? seg.LastRoomVisible / (float)seg.LastRoomTotal
                    : 0f;
                if (sweptSegs >= VerdictMaxSegmentsPerPass || index < _verdictCursor)
                {
                    unsweptSolid++;
                    // Unswept still gets a CLASS from numbers already measured, so it is never
                    // silently missing — only its hex counts wait for the next pass.
                    if (ceil < onBar)
                        ceiling++;
                    else if (seg.LastBlocked > 0)
                        belowBar++;
                    else
                        clear++;
                    continue;
                }
                sweptSegs++;
                int hidesCounted = 0, hidesOffGrid = 0, hidesOtherRoom = 0;
                object? ownMap = seg.RoomIndex >= 0 && seg.RoomIndex < _roomMapKeys.Count
                    ? _roomMapKeys[seg.RoomIndex]
                    : null;
                Bounds b = seg.Bounds;
                for (int h = 0; h < _verdictHex.Count; h++)
                {
                    Vector3 at = _verdictHex[h];
                    Vector3 to = at - headPos;
                    float dist = to.magnitude;
                    if (dist < 0.001f)
                        continue;
                    // The SAME acceptance RoomBlockedFraction uses — same epsilon, same broad
                    // AABB phase, same narrow per-piece test. A looser test here would report
                    // hexes the metric itself would have rejected and invent the alarm.
                    float eps = Mathf.Max(seg.BlockEps, BlockEpsDistFraction * dist);
                    var ray = new Ray(headPos, to / dist);
                    if (!b.IntersectRay(ray, out float d) || (d >= dist - eps && !b.Contains(at)))
                        continue;
                    if (!RayHitsWallMesh(seg, ray, dist, eps, at, out _, out _, out _))
                        continue;
                    if (!_verdictHexGridded[h])
                        hidesOffGrid++;
                    else if (ownMap != null && ReferenceEquals(_verdictHexMap[h], ownMap))
                        hidesCounted++;
                    else
                        hidesOtherRoom++;
                }
                string nums = $"{id} r{seg.RoomIndex} ema{seg.Smooth:F2} blk{seg.LastBlocked}/"
                    + $"{seg.LastRoomTotal} vis{seg.LastRoomVisible} ceil{ceil:F2} — hides "
                    + $"{hidesCounted} counted + {hidesOffGrid} OFF-GRID + {hidesOtherRoom} "
                    + "other-room";
                if (hidesOffGrid > 0 && hidesCounted == 0)
                {
                    offGridSolid++;
                    if (_verdictOffGridNamed.Count < VerdictMaxNamed)
                        _verdictOffGridNamed.Add($"[SOLID:OFF-GRID] {nums}");
                }
                else if (ceil < onBar)
                {
                    ceiling++;
                    if (_verdictCeilingNamed.Count < VerdictMaxNamed)
                        _verdictCeilingNamed.Add($"[SOLID:CEILING] {nums}");
                }
                else if (hidesCounted > 0 || hidesOffGrid > 0 || hidesOtherRoom > 0
                         || seg.LastBlocked > 0)
                {
                    belowBar++;
                    if (_verdictBelowNamed.Count < VerdictMaxNamed)
                        _verdictBelowNamed.Add($"[SOLID:BELOW-BAR] {nums}");
                }
                else
                {
                    clear++;
                }
            }
            // Rotating cursor: advance only while solid segments are still waiting, and wrap.
            // A pass that swept NOTHING while segments are still waiting means the cursor has run
            // past every solid segment (the table shrank, or the ones ahead of it all faded), so
            // it wraps immediately — otherwise it would sit there and the deferred population
            // would never be measured while the line kept reporting it as merely deferred.
            _verdictCursor = unsweptSolid > 0 && sweptSegs > 0 ? _verdictCursor + sweptSegs : 0;
            if (_verdictCursor >= total)
                _verdictCursor = 0;

            _verdictNames.Clear();
            _verdictNames.AddRange(_verdictOffGridNamed);
            _verdictNames.AddRange(_verdictCeilingNamed);
            _verdictNames.AddRange(_verdictBelowNamed);
            _verdictNames.AddRange(_verdictHeldNamed);
            int named = Mathf.Min(_verdictNames.Count, VerdictMaxNamed);

            // Change gate on the CLASSES and the NAMED SET, never on the numbers — ema/blk/ceil
            // move every frame and would defeat the gate entirely.
            _verdictSb.Length = 0;
            _verdictSb.Append(occluders).Append('/').Append(clear).Append('/').Append(offGridSolid)
                      .Append('/').Append(ceiling).Append('/').Append(belowBar).Append('/')
                      .Append(noBounds).Append('/').Append(noRoomGrid).Append('/').Append(doorways)
                      .Append('/').Append(gates).Append('/').Append(engulf);
            for (int i = 0; i < named; i++)
            {
                // The id and the class carry the identity; the numbers after the em dash do not.
                string entry = _verdictNames[i];
                int cut = entry.IndexOf(" r", System.StringComparison.Ordinal);
                _verdictSb.Append('|').Append(cut > 0 ? entry.Substring(0, cut) : entry);
            }
            string key = _verdictSb.ToString();
            bool changed = key != _lastVerdictKey;
            if (!changed && now < _nextVerdictHeartbeat)
                return;
            _lastVerdictKey = key;
            _nextVerdictHeartbeat = now + VerdictHeartbeatSeconds;

            _verdictSb.Length = 0;
            for (int i = 0; i < named; i++)
                _verdictSb.Append(i > 0 ? " ; " : string.Empty).Append(_verdictNames[i]);
            if (_verdictNames.Count > named)
                _verdictSb.Append(" ; +").Append(_verdictNames.Count - named)
                          .Append(" more in these classes, not named this pass");
            if (named == 0)
                _verdictSb.Append("none — every segment is either an OCCLUDER or SOLID:CLEAR, "
                    + "i.e. no wall's verdict is contradicted by the geometry this pass");

            // HW-VERIFY: the wall the user is pointing at, and the term that decided it.
            VRLog.Note(Name,
                $"OCCLUDER VERDICTS{(changed ? string.Empty : " (unchanged — heartbeat)")}: head "
                + $"({headPos.x:F1},{headPos.y:F2},{headPos.z:F1}), {total} segment(s), bars "
                + $"on≥{onBar:F2}/off<{offBar:F2}. REGISTRY: {registryHexes} keyed hex(es) over "
                + $"{_tilesByMap.Count} CMap(s); {offGridHexes} of them sit on a CMap that got NO "
                + $"sample grid ({inViewOffGrid} of those are PLAYABLE and in the head's frustum "
                + "right now) — that population is in no wall's denominator, so a wall hiding only "
                + "those reads coverage 0.00 by construction and no threshold can move it. "
                + $"BY VERDICT: {occluders} OCCLUDER, {offGridSolid} SOLID:OFF-GRID, "
                + $"{ceiling} SOLID:CEILING (its room's visible/total is below the enter bar, so "
                + "no wall of that room can reach the bar this frame whatever it hides), "
                + $"{belowBar} SOLID:BELOW-BAR, {clear} SOLID:CLEAR (hides nothing in view — the "
                + $"verdict agrees with the geometry), {notConsidered} NOT-CONSIDERED (NO-BOUNDS "
                + $"{noBounds}, NO-ROOM-GRID {noRoomGrid}, DOORWAY {doorways}, GATE {gates}, "
                + $"ENGULF {engulf}). Hex sweep covered {sweptSegs} solid segment(s) this pass, "
                + $"{unsweptSolid} deferred to the next (rotating cursor — a deferred wall still "
                + "carries a class, only its hex counts wait). NAMED, contradicted class first — "
                + "the id is the anchor INSTANCE ID because scenario wall names REPEAT across map "
                + "tiles (the ModBuild 463 log's FAIL-SAFE GAPS line reads 'Wall 1'…'Wall 8', "
                + "'Wall 1', 'Wall 2'), so a name alone cannot identify a wall in a "
                + $"mirror-symmetric level: {_verdictSb}");
        }
    }
}
