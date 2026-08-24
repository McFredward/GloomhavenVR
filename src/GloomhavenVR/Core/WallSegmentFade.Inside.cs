using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// INSIDE THE MAP — the fade policy stands down while the player is standing IN the board.
///
/// <para>THE REPORT (user, 2026-08-24, ModBuild 241, verbatim):</para>
/// <para><i>"Ich möchte dass du die Ausblendung der Wände dir nochmal anschaust. Folgendes
/// Szenario: Wenn ich alleine bin (oder MP Wandsynchronisierung deaktiviert habe) und ich mich
/// so klein mache, dass ich IN der Map stehe, dann sollten alle Wände voll sichtbar sein. Das
/// ist aber nicht der Fall. Wenn man z.B. durch eine andere von außen sieht wohinter ein
/// weiterer Raum ist, wird die Mauer so gut wie immer per Default ausgeblendet. Hier muss ein
/// guter Mittelweg gefunden werden, dass wenn man außen schaut die störenden Wände ausgeblendet
/// werden, aber wenn man IN der Map drinnen ist soll das nicht passieren, damit man auch alle
/// Wände in seiner Pracht betrachten kann."</i></para>
///
/// <para>WHY THE EXISTING CRITERION IS RIGHT — AND WHY IT INVERTS INSIDE. The decision this
/// class gates is the one documented on <see cref="WallSegmentFade"/>: a wall fades when it
/// hides at least <see cref="WallFadeTuning.On"/> of the frustum-visible floor samples of some
/// room. For the posture the mod was built around — a giant looking down at a diorama — that is
/// exactly the right question, because a wall standing between the eye and a room IS the
/// obstruction and nothing else in the scene is. Shrink the player until his eyes are below the
/// parapet and the SAME question stops describing an obstruction: the wall a metre in front of
/// his face is no longer in the way of the thing he wants to see, it IS the thing he wants to
/// see. The criterion did not become wrong; it became blind to where he is standing. Hence a
/// test for WHERE, and a policy that stands down while that test holds — not a new coverage
/// metric.</para>
///
/// <para>WHAT THE HARDWARE LOG ACTUALLY SHOWED (ModBuild 241, LogOutput.log lines 10355-10810 —
/// the run that produced the report). His shrink is visible as a continuous zoom from
/// <c>rig world scale 8.28</c> down to <c>1.15</c> world units per real metre, and the fade
/// diagnostic follows him down: <c>headY</c> falls from <c>10.69</c> to the band
/// <c>1.53 .. 2.96</c> against a floor plane at <c>sampY 0.05</c> and wall AABB tops at
/// <c>wy[-0.04..3.54]</c> / <c>[-0.04..3.86]</c>. He is standing between the floor and the wall
/// crest. Two things happen there, and neither is what a reader of the constants would guess:
/// <list type="number">
/// <item>THE COVERAGE FRACTIONS COLLAPSE. Inside the room the near walls read
///   <c>raw 0.00 .. 0.13</c>, <c>ema 0.00 .. 0.13</c> — at or below the 0.10 low bar, an order
///   of magnitude under the same walls' outside readings (<c>raw 0.81 ema 0.80</c>,
///   <c>raw 0.38</c>, <c>raw 0.31</c> at <c>headY 10.67</c>). The frustum-culled numerator is
///   why: standing in the room, most of the room's own floor is behind the head, so
///   <c>vis</c> drops from <c>16/16</c> to <c>0/16 .. 11/16</c>. The walls therefore do NOT
///   stay faded because the metric says they should. They stay faded because they were faded
///   BEFORE he shrank and the un-fade dwell (2.5 s moved, 7 s rotation-only) holds them, and
///   because of (2).</item>
/// <item>HIS HEAD ENTERS WALL AABBs. <c>BlockedFraction</c> returns a hard <c>1f</c> for
///   <c>seg.Bounds.Contains(headPos)</c> — "wall in the face". That path is identifiable in the
///   log because it reports the room's TOTAL as visible regardless of the frustum: lines 10705
///   (<c>vis 2/16 headY 1.95 | 'Wall 2' raw1.00 ema0.95 blk16/16 v16 ON 0.87</c>) and 10761
///   (<c>vis 8/16 headY 2.29 | 'Wall 6' raw1.00 ema0.77 blk16/16 v16 ON 1.00</c>). Those are
///   the ONLY two head-inside-AABB events in the whole 25 MB session, and both are inside this
///   window. Each one re-arms a fade through the 0.20 s enter dwell that then takes 2.5-7 s to
///   fall out again — visible as the churn at lines 10572/10580/10661/10717/10725/10802:
///   Wall 2 ON, OFF, ON, OFF; Wall 6 OFF, ON, OFF; Wall 4 ON, OFF.</item>
/// </list>
/// The <c>LATCH WARN</c> watchdog fired ZERO times in the session, which is the confirmation
/// that nothing was broken: every one of those fades was the Schmitt trigger and the dwell doing
/// precisely what they were told. The defect is the policy, not the machinery.</para>
///
/// <para>THE TEST: GEOMETRY, NOT A CONFIG VALUE. INSIDE means the head is inside the BOARD
/// VOLUME — the union XZ footprint of every decision-valid room and its walls, from the board's
/// floor plane up to the WALL CREST. Both terms come from geometry the player can see, and the
/// answer is the same however he got there: shrinking, stick flight (the log's
/// <c>[Comfort] stick flight: LIVE</c>), a world-grab that drops him in, or a scenario that
/// starts him low. The effective world scale is deliberately NOT a term. It cannot be one: the
/// log shows it is a continuous dial the player rides from 8.28 down to 1.15 and back inside a
/// single scenario (and the map room sits at 198.12), so no threshold on it names a posture. It
/// appears in the falsifier line as a CONVERSION ONLY — every bar in the decision is in world
/// units, compared against world-unit geometry, which is the "…Meters against a WORLD-unit
/// product" trap this repo has already paid for once.</para>
///
/// <para>THE BOUNDARY AND ITS HYSTERESIS. The measured quantity is the signed distance of the
/// head to that box: negative inside, positive outside, one scalar, so the existing Schmitt
/// shape applies unchanged — two bars and two dwells, expressed as fractions of the crest height
/// C so they scale with the board and not with a number somebody picked:
/// <list type="bullet">
/// <item>ENTER INSIDE at <c>sd &lt;= -0.10·C</c> (about 0.35 wu at the logged C = 3.5 wu): the
///   eye is a tenth of a wall height below the crest and over the footprint.</item>
/// <item>LEAVE INSIDE at <c>sd &gt;= +0.35·C</c> (about 1.24 wu): a band of 0.45·C the head has
///   to cross before the verdict can flip back.</item>
/// <item>Dwells: 0.20 s to enter (<see cref="FadeDriver.EnterDwellSeconds"/>, the same prompt
///   dwell the fade decision uses), <see cref="WallFadeTuning.DwellMoved"/> = 2.5 s to leave.
///   The POLARITY is deliberately the opposite of the fade Schmitt's and this is not an
///   oversight: there the prompt direction is "get out of the way", here the prompt direction is
///   "give him his walls back", and in both cases the delayed direction is the one that deletes
///   geometry.</item>
/// </list>
/// A DOORWAY IS NOT A BOUNDARY CASE under this test, which is the whole reason the box is the
/// BOARD and not a room: a player standing in a doorway is deep inside the footprint and far
/// below the crest, so the signed distance there is comfortably negative and nothing can
/// chatter. The only place the bars are ever close is the board's outer surface — the perimeter
/// and the crest plane — and that is what the 0.45·C band plus the two dwells exist for.</para>
///
/// <para>THE BARS ARE CALIBRATED AGAINST THE LOG, NOT PICKED. Feeding the ModBuild 241 numbers
/// through them — floor plane 0.00, median crest 3.54 wu, so volume y 0.00..3.54 — the enter bar
/// lands at -0.354 wu and the exit bar at +1.239 wu, i.e. INSIDE needs the eye below
/// <c>headY 3.19</c> and OUTSIDE needs it above <c>headY 4.78</c>. Every one of the seventeen
/// diag samples from the shrunk window (<c>headY 1.53 .. 2.96</c>) clears the enter bar with its
/// height term alone (slab -1.76 .. -0.58), and every sample from the giant postures
/// (<c>headY 8.33 .. 20.34</c>) clears the exit bar (slab +4.79 .. +16.80). The hysteresis band
/// sits inside a 5.4 wu gap in which the session contains no steady-state sample at all — the
/// two populations are cleanly separated and the boundary is in the empty space between them,
/// which is where a Schmitt band belongs.</para>
///
/// <para>WHAT STANDING DOWN MEANS: A HARD OFF, WITH ONE CARVE-OUT MEASURED IN MESHES.
/// ModBuild 241-250 shipped the weaker form of this rule — a substituted threshold pair
/// (0.98/0.90 instead of the live bars) rather than a stand-down — on the argument that 0.98 is
/// "unreachable by any wall he can look at" and reachable "only through
/// <c>BlockedFraction</c>'s hard <c>1f</c> for a head inside the wall's own AABB". THE FIRST
/// ARGUMENT WAS SOUND AND THE SECOND WAS THE BUG. The hardware log of ModBuild 250
/// (.planning/debug/Player.log, and the photograph wandausblendung.jpg it belongs to) shows the
/// escape hatch firing as the STEADY STATE inside the board:
/// <list type="bullet">
/// <item>75 of the session's throttled <c>diag:</c> samples carry the head-inside-AABB
///   signature — a wall reporting <c>blk N/N v N</c> against the room TOTAL while the frustum
///   only had part of the room in view (e.g. <c>vis 8/16 headY 3.30 | 'Wall 4' raw1.00 ema1.00
///   blk16/16 v16 ON 1.00 wy[-0.42..5.00]</c>). The ModBuild 241 session this class was written
///   against contained exactly TWO such events in 25 MB; at figure scale inside a room they are
///   continuous.</item>
/// <item>The reason is that <c>Segment.Bounds</c> is not a wall. It is the union AABB of every
///   renderer the segment owns, grown further by stacked-shell adoption and compared with a
///   <c>BlockEps</c> up to 0.90 wu. 'Wall 4' in that log owns scattered
///   <c>PCG_FR_Pillar_Tree_Trunk_01_PR</c> pieces at world XZ (-9.4,-2.4), (-12.9,-0.4),
///   (-6.9,1.9) and (-10.4,3.9): its box spans roughly x[-13..-6], z[-3..4] and y[-0.42..5.00] —
///   most of the room, and almost all of it AIR. A player standing on open floor is inside it,
///   so <c>BlockedFraction</c> returns its hard <c>1f</c>, the EMA reaches 1.00, and 1.00 clears
///   a 0.98 bar as easily as a 0.25 one. The raised bar therefore did nothing at all for the
///   walls that matter, which is precisely the report: <i>"Die Wandausblendung ist immer noch
///   streng … ich bin voll IN dem Spiel drin und schaue nach draußen nicht nach drinnen, warum
///   wird es dann ausgeblendet?"</i> (user, 2026-08-24, wandausblendung.jpg — standing inside the
///   room at figure scale, looking out past a group of monsters, with the masonry behind them
///   gone and the forest environment showing through).</item>
/// </list>
/// So the rule becomes what the report asks for literally: while INSIDE holds, this client's OWN
/// occlusion decision is FORCED OFF for every wall — <c>seg.State</c> and <c>seg.PendingRaw</c>
/// are cleared every frame, not every evaluation, so a skipped evaluation cannot leave a stale
/// decision standing and the rule owns the final value that feeds the ramp.</para>
///
/// <para>THE CARVE-OUT, AND WHY IT IS KEYED TO A MESH. Being sealed inside opaque geometry with
/// no way out but backing up blind is still worse than a wall dissolving, so the escape hatch
/// stays — re-keyed from the segment's union box to the world AABB of an actual wall MESH
/// (<c>Renderer.bounds</c> over <c>Segment.Renderers</c> and the plain-body meshes), with no
/// epsilon and no growth. "The camera is inside a wall" is a statement about masonry, and the
/// only geometry that IS masonry is the mesh. A segment in the carve-out runs the ORDINARY
/// policy with the live bars — no substituted pair anywhere — so the head-in-stone case
/// dissolves after the usual 0.20 s enter dwell exactly as it did before. Every segment in the
/// carve-out is NAMED in the falsifier line, mesh and all, so an over-broad hatch can never
/// again be invisible. The cheap union test still guards the walk (a mesh box is contained in
/// the union box), so the per-renderer loop only runs for a segment the head is plausibly in,
/// and only while INSIDE holds.</para>
///
/// <para>PRESSING HIS FACE AGAINST A WALL from outside its meshes is not the trapped case and
/// stays solid, which is correct — a wall seen from 20 cm away is still a wall he is looking
/// at.</para>
///
/// <para>WALLS ALREADY FADED WHEN HE CROSSES IN are released on the rising edge
/// (<see cref="FadeDriver.ReleaseFadesForInside"/>), immediately, without waiting out the exit
/// dwell. That does not weaken the dwell's own reasoning, it satisfies it: the dwell exists so
/// that ROTATION ALONE almost never brings a wall back, and crossing this boundary is not
/// rotation — it is a translation or a rescale of at least 0.45·C of world geometry, which is
/// the most decisive perspective change the driver is able to observe. The release only clears
/// <c>State</c> and <c>PendingRaw</c>; the EMA is left alone (so the numbers stay honest), the
/// normal 0.12 s ramp brings the walls back through the ordinary animated un-fade, and every
/// restore path — foliage, asset siblings, mounted props, stacked shells, body meshes — runs
/// exactly as it does for any other un-fade. Each release logs its own <c>fade OFF</c> line.</para>
///
/// <para>MULTIPLAYER. The rule acts on <c>seg.State</c>, which is this client's OWN decision, and
/// that placement decides all three halves of the contract on its own:
/// <list type="bullet">
/// <item>Own fades inside: suppressed. That is the request.</item>
/// <item>BROADCAST: stops with them, because <see cref="FadeDriver.SampleFadedKeys"/> samples
///   <c>seg.State</c> and nothing else. A client standing inside must not be sending fades it is
///   not applying — a peer would then be opening a wall on the strength of a decision this
///   machine already overruled. Consistency here is free, and it is free precisely because the
///   gate sits on the local decision rather than on the delivery.</item>
/// <item>PEER fades arriving over wire record 17: UNTOUCHED. <c>RemoteWantsFade</c> composes at
///   the TARGET, independently of <c>seg.State</c>, so a teammate's fade still opens the wall
///   for anyone whose own <c>[WallFade] SyncPeerFades</c> is on. Suppressing that would silently
///   rewrite a receiver-side setting for the other players, and it is not what was asked: the
///   report scopes itself to "wenn ich alleine bin (oder MP Wandsynchronisierung deaktiviert
///   habe)". A player who wants his walls while a teammate outside is opening them already has
///   the toggle for it.</item>
/// </list></para>
///
/// <para>THE 3D MAP ROOM IS NOT AFFECTED, and this was checked rather than assumed. In the
/// ModBuild 241 log the map room occupies lines 790-9782 (<c>world scale 198.12</c>) and the
/// only thing this subsystem prints in all of it is
/// <c>room registry refresh: 0 to 0 LOGICAL room(s) from 0 volume renderer(s)</c> — there is no
/// <c>TilesOcclusionGenerator</c> room set there, so <c>Tick</c> returns at its
/// <c>_roomBounds.Count == 0</c> guard before any decision runs. The board volume is built from
/// exactly those rooms, so it is never valid there and this rule can never fire in the map room
/// however small the player makes himself.</para>
///
/// <para>COST. The per-frame test is a point against ONE cached <see cref="Bounds"/>: three
/// absolute values, three subtractions, six min/max and one square root, then two compares and
/// two dwell compares. No scene query, no per-segment work, no allocation — it is safe to run
/// every frame rather than on the evaluation cadence, which is what makes the boundary edge
/// prompt even when <c>[Optimize] WallFadeEvalInterval</c> is skipping evaluations. Building the
/// volume is O(rooms + segments) with one sort of at most one float per segment, and it runs
/// once per rescan inside its own <c>WallFade.Commit.BoardVolume</c> phase, so it prices itself
/// in the BUDGET line next to the other twenty-three.</para>
///
/// <para>ALTERNATIVES REJECTED.
/// <list type="bullet">
/// <item>A RAISED BAR (0.98/0.90 substituted for the live pair) was what ModBuild 241-250
///   shipped, on the argument that it keeps the head-in-stone escape hatch for one extra
///   constant. It is retired, not re-litigated: the escape hatch it relied on was keyed to
///   <c>seg.Bounds</c>, so it fired from open floor and the raised bar spared nothing. The
///   hatch is now keyed to a MESH, which makes it narrow enough that the surrounding policy no
///   longer has to be soft to accommodate it.</item>
/// <item>A THRESHOLD ON THE WORLD SCALE ("below N world units per metre, stand down") was
///   rejected because the log falsifies it: the scale is a continuous dial ridden from 8.28 to
///   1.15 and back within one scenario, the map room sits at 198.12, and the same scale describes
///   a giant leaning close and a person standing on the floor. It also answers nothing for a
///   player who flew in at an unchanged scale.</item>
/// <item>HEAD HEIGHT ALONE (below the crest, ignoring XZ) was rejected because it would stand
///   the fade down for a player crouching to eye level BESIDE the board, which is the one
///   viewpoint the report explicitly wants fading kept for: "wenn man außen schaut die störenden
///   Wände ausgeblendet werden".</item>
/// <item>A PER-ROOM inside test (is the head in THIS wall's room) was rejected as the wrong
///   granularity and a chatter source: it makes a doorway a boundary, it would fade the walls of
///   the room next door while he stands in this one — which is most of what he is complaining
///   about — and it multiplies one O(1) test by the segment count.</item>
/// <item>SUPPRESSING PEER FADES while inside was rejected: see the multiplayer paragraph. It
///   would break a receiver-side contract for other players to fix a single-player symptom the
///   reporter had already scoped away from it.</item>
/// <item>NEW CONFIG ENTRIES for the two bars were not added. The class header's own rule is that
///   thresholds needing hardware iteration become steppers and geometry-derived constants stay
///   code-owned; these are fractions of a measured crest height, not preferences, and a first
///   hardware round should test one thing. The falsifier line prints every one of them with its
///   live value, so promoting them later needs no guesswork.</item>
/// </list></para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // --- INSIDE-THE-MAP constants (see class header) -------------------------------------
        // Both bars are FRACTIONS OF THE CREST HEIGHT C (the board's own median wall height in
        // world units), never absolute distances: the board is the yardstick, so the boundary
        // means the same thing on a low ruin and on a keep.
        /// <summary>Schmitt high bar: enter INSIDE when the head is at least this fraction of
        /// the crest height INSIDE the board volume (signed distance is negative there).</summary>
        private const float InsideEnterDepthFraction = 0.10f;
        /// <summary>Schmitt low bar: leave INSIDE only once the head is this fraction of the
        /// crest height OUTSIDE the volume — a 0.45·C band the head must cross to flip back.</summary>
        private const float InsideExitDepthFraction = 0.35f;
        /// <summary>How many carve-out / external-signal walls the falsifier names before it
        /// starts counting the rest. The whole point of the line is that an over-broad escape
        /// hatch can never again hide behind a bare number, so this is generous.</summary>
        private const int InsideNameCap = 6;
        /// <summary>Floor under the derived crest height, so a degenerate or half-generated
        /// board can never produce a zero-height volume that the head is trivially outside.</summary>
        private const float BoardCrestMinWU = 0.5f;
        /// <summary>Cadence of the falsifier line while INSIDE holds. Edges print unthrottled.</summary>
        private const float InsideLogIntervalSeconds = 2f;

        // --- cached board volume (rebuilt once per rescan, read O(1) per frame) --------------
        /// <summary>The board's own volume: union XZ footprint of every decision-valid room and
        /// its walls, Y from the board floor plane to the wall crest. Valid only when
        /// <see cref="_boardVolumeValid"/>.</summary>
        private Bounds _boardVolume;
        private bool _boardVolumeValid;
        /// <summary>Crest height above the floor plane (world units) — the yardstick both
        /// boundary bars are expressed in.</summary>
        private float _boardCrestWU;
        /// <summary>Board floor plane (world units), the minimum over decision-valid rooms.</summary>
        private float _boardFloorY;
        private int _boardVolumeRooms;
        private int _boardVolumeWalls;
        /// <summary>Per-segment crest samples; a field so the commit phase allocates nothing.</summary>
        private readonly List<float> _crestScratch = new();
        // Announce-once signature (rooms, walls, and the volume quantized to 0.1 wu) so the
        // BOARD VOLUME line prints on a real change — a reveal, a fresh scenario — and not
        // every two seconds forever.
        private int _bvSigRooms = -1, _bvSigWalls = -1, _bvSigCrest, _bvSigX, _bvSigZ;

        // --- INSIDE state ------------------------------------------------------------------
        private bool _insideBoard;
        private bool _insidePending;
        private float _insidePendingSince;
        /// <summary>Last signed distance of the head to the board volume (wu, negative inside).</summary>
        private float _lastInsideSigned;
        /// <summary>Last per-axis slabs of that distance — which TERM decided the verdict.</summary>
        private float _lastInsideMarginY;
        private float _lastInsideMarginXZ;
        // --- stand-down census (all MEASURED on the last pass, never intended) --------------
        /// <summary>Fadeable walls the stand-down held at State=false on the last pass.</summary>
        private int _insideForcedSolid;
        /// <summary>Of those, how many the LIVE bars would have faded — the number that proves
        /// the rule did work. A session where the player stands inside, walls dissolve, and this
        /// reads 0 says the rule is not what is holding them.</summary>
        private int _insideWouldHaveFaded;
        /// <summary>Highest smoothed coverage among the forced-solid walls, and whose.</summary>
        private float _insideWorstSmooth;
        private string _insideWorstWall = "-";
        /// <summary>Walls exempted because the head is inside one of their MESHES, named.</summary>
        private int _insideCarveOut;
        private readonly List<string> _insideCarveNames = new();
        /// <summary>Forced-solid walls an EXTERNAL signal still hides, named with which signal.
        /// Peer fades (wire record 17) are the external case the report grants; a gate lift is
        /// this client's own decision propagating and is counted separately so the line cannot
        /// pass one off as the other.</summary>
        private int _insideExternalPeer;
        private int _insideExternalGate;
        private readonly List<string> _insideExternalNames = new();
        /// <summary>Forced-solid walls with NO external signal that still carry a non-zero fade
        /// after the ramp — mid-unfade is normal and transient, a value that persists here is a
        /// writer this loop does not know about.</summary>
        private int _insideResidual;
        private float _insideResidualWorst;
        private string _insideResidualWall = "-";
        /// <summary>False until a pass has actually run under the rule, so the falsifier can say
        /// "no pass yet" instead of printing zeros that look like a measurement.</summary>
        private bool _insideCensusTaken;
        /// <summary>Walls released by the last INSIDE rising edge.</summary>
        private int _insideReleased;
        private float _nextInsideLogTime;

        /// <summary>
        /// COMMIT PHASE 24 — see <c>RescanCore</c>. Rebuild the cached board volume. Deliberately
        /// LAST: it reads the room registry AND every segment's final decision AABB, so it must
        /// run after the gate-bounds guarantee that is itself deliberately last.
        /// </summary>
        private void CommitBoardVolume()
        {
            _boardVolumeValid = false;
            _boardVolumeRooms = 0;
            _boardVolumeWalls = 0;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float floorY = float.MaxValue;
            for (int i = 0; i < _roomBounds.Count; i++)
            {
                // Only rooms the coverage decision itself trusts (tile-anchored plane, non-empty
                // grid). A guessed frame must not define where the player is standing either.
                if (!RoomDecisionValid(i))
                    continue;
                Bounds b = _roomBounds[i];
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                if (i < _roomFloorY.Count && _roomFloorY[i] < floorY) floorY = _roomFloorY[i];
                _boardVolumeRooms++;
            }
            if (_boardVolumeRooms == 0 || floorY == float.MaxValue)
            {
                // No decision-valid room: the 3D map room and every pre-generation frame. The
                // volume stays invalid and the INSIDE rule can never fire.
                AnnounceBoardVolume();
                return;
            }

            // The walls belong to the footprint too — the room renderers are the FLOOR proxies,
            // so a player standing inside a perimeter wall would otherwise read as outside the
            // board, which is the one place the head-in-stone escape hatch has to work.
            _crestScratch.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds || !RoomDecisionValid(seg.RoomIndex))
                    continue;
                Bounds b = seg.Bounds;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                _boardVolumeWalls++;
                // Doorways and engulfing pieces are in the FOOTPRINT but not in the crest
                // sample: an arch top is not a wall height, and an engulfing AABB is the very
                // shape the coverage metric already refuses to trust.
                if (seg.DoorRoot == null && !seg.Engulfing)
                    _crestScratch.Add(b.max.y - floorY);
            }
            if (_crestScratch.Count == 0)
            {
                AnnounceBoardVolume();
                return;
            }

            // MEDIAN, not max: a keep's stacked superstructure is a legitimate segment AABB
            // three stories tall, and taking the tallest would put the crest plane above every
            // wall in the scenario — the volume would then swallow a player who is plainly
            // looking down at the board from outside.
            _crestScratch.Sort();
            float crest = Mathf.Max(_crestScratch[_crestScratch.Count / 2], BoardCrestMinWU);
            _boardCrestWU = crest;
            _boardFloorY = floorY;
            _boardVolume = new Bounds(
                new Vector3((minX + maxX) * 0.5f, floorY + crest * 0.5f, (minZ + maxZ) * 0.5f),
                new Vector3(Mathf.Max(maxX - minX, 0f), crest, Mathf.Max(maxZ - minZ, 0f)));
            _boardVolumeValid = true;
            AnnounceBoardVolume();
        }

        /// <summary>
        /// Print the board volume whenever it materially changes (new scenario, room reveal).
        /// This is the line that proves the rule was ARMED with real numbers even in a session
        /// where the player never went inside — an instrument that only speaks when it triggers
        /// cannot distinguish "did not happen" from "never ran".
        /// </summary>
        private void AnnounceBoardVolume()
        {
            int sigCrest = Mathf.RoundToInt(_boardCrestWU * 10f);
            int sigX = _boardVolumeValid ? Mathf.RoundToInt(_boardVolume.size.x * 10f) : 0;
            int sigZ = _boardVolumeValid ? Mathf.RoundToInt(_boardVolume.size.z * 10f) : 0;
            if (_boardVolumeRooms == _bvSigRooms && _boardVolumeWalls == _bvSigWalls
                && sigCrest == _bvSigCrest && sigX == _bvSigX && sigZ == _bvSigZ)
                return;
            _bvSigRooms = _boardVolumeRooms;
            _bvSigWalls = _boardVolumeWalls;
            _bvSigCrest = sigCrest;
            _bvSigX = sigX;
            _bvSigZ = sigZ;
            if (!_boardVolumeValid)
            {
                VRLog.Info(Name,
                    "BOARD VOLUME: none — "
                    + $"{_boardVolumeRooms} decision-valid room(s), {_boardVolumeWalls} wall(s) "
                    + "with a decision AABB. The INSIDE-the-map stand-down cannot fire until a "
                    + "room is tile-anchored with a non-empty floor grid; this is also the "
                    + "steady state of the 3D map room, which has no occlusion volumes at all.");
                return;
            }
            Vector3 mn = _boardVolume.min, mx = _boardVolume.max;
            VRLog.Info(Name,
                $"BOARD VOLUME: x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, "
                + $"z {mn.z:F1}..{mx.z:F1} world units — union footprint of "
                + $"{_boardVolumeRooms} decision-valid room(s) and {_boardVolumeWalls} wall(s), "
                + $"floor plane {_boardFloorY:F2}, MEDIAN wall crest {_boardCrestWU:F2} wu over "
                + $"{_crestScratch.Count} wall(s). INSIDE bars: enter at signed distance "
                + $"<= {-InsideEnterDepthFraction * _boardCrestWU:F2} wu, leave at "
                + $">= {InsideExitDepthFraction * _boardCrestWU:F2} wu (dwell "
                + $"{EnterDwellSeconds:F2}s in / {WallFadeTuning.DwellMoved:F2}s out); while "
                + "INSIDE this client's own occlusion decision is FORCED OFF for every wall "
                + "(no substituted bars — the live bars stay "
                + $"{WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2} and apply only to a wall "
                + "whose own masonry contains the head, and to nothing else).");
        }

        /// <summary>
        /// THE INSIDE TEST — signed distance of the head to the cached board volume, through the
        /// Schmitt bars and dwells. O(1) and allocation-free by construction (see class header),
        /// so it is called every frame rather than on the evaluation cadence: the boundary
        /// crossing is a deliberate act and its edge must not wait out a skipped evaluation.
        /// Returns the debounced verdict.
        /// </summary>
        private bool UpdateInsideBoard(Vector3 headPos, float now, float rigScale)
        {
            if (!_boardVolumeValid)
            {
                if (_insideBoard || _insidePending)
                    ResetInsideBoardState();
                return false;
            }

            Vector3 c = _boardVolume.center, e = _boardVolume.extents;
            float qx = Mathf.Abs(headPos.x - c.x) - e.x;
            float qy = Mathf.Abs(headPos.y - c.y) - e.y;
            float qz = Mathf.Abs(headPos.z - c.z) - e.z;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
            // Exact point-to-box signed distance: the outside part is Euclidean, the inside part
            // is the least-negative slab (how far the head is from the NEAREST face).
            float sd = Mathf.Sqrt(ox * ox + oy * oy + oz * oz)
                       + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f);
            _lastInsideSigned = sd;
            _lastInsideMarginY = qy;
            _lastInsideMarginXZ = Mathf.Max(qx, qz);

            bool want = _insideBoard
                ? sd < InsideExitDepthFraction * _boardCrestWU
                : sd <= -InsideEnterDepthFraction * _boardCrestWU;
            if (want != _insidePending)
            {
                _insidePending = want;
                _insidePendingSince = now;
            }
            if (_insidePending != _insideBoard)
            {
                // Prompt IN, delayed OUT — the walls-return direction is the safe one; see the
                // polarity note in the class header.
                float dwell = _insidePending ? EnterDwellSeconds : WallFadeTuning.DwellMoved;
                if (now - _insidePendingSince >= dwell)
                {
                    _insideBoard = _insidePending;
                    if (_insideBoard)
                        ReleaseFadesForInside(now);
                    else
                        _insideReleased = 0;
                    LogInsideState(headPos, rigScale, edge: true);
                    _nextInsideLogTime = now + InsideLogIntervalSeconds;
                }
            }
            return _insideBoard;
        }

        /// <summary>
        /// Drop the cached volume AND the verdict that was measured against it. Called on a
        /// scene load: a box measured in the previous scenario is not evidence about this one,
        /// and between the load and the first commit the head could sit anywhere relative to it.
        /// The next commit rebuilds both and the BOARD VOLUME line re-announces them.
        /// </summary>
        private void InvalidateBoardVolume()
        {
            _boardVolumeValid = false;
            _boardVolumeRooms = 0;
            _boardVolumeWalls = 0;
            _boardCrestWU = 0f;
            _bvSigRooms = -1;
            _bvSigWalls = -1;
            ResetInsideBoardState();
        }

        /// <summary>Drop the INSIDE verdict (scenario teardown, toggle off, no valid board).</summary>
        private void ResetInsideBoardState()
        {
            _insideBoard = false;
            _insidePending = false;
            _insidePendingSince = 0f;
            _insideReleased = 0;
            _insideCensusTaken = false;
            _insideForcedSolid = 0;
            _insideWouldHaveFaded = 0;
            _insideWorstSmooth = 0f;
            _insideWorstWall = "-";
            _insideCarveOut = 0;
            _insideCarveNames.Clear();
            _insideExternalPeer = 0;
            _insideExternalGate = 0;
            _insideExternalNames.Clear();
            _insideResidual = 0;
            _insideResidualWorst = 0f;
            _insideResidualWall = "-";
        }

        /// <summary>
        /// INSIDE rising edge: hand back every wall this client had faded, immediately, without
        /// waiting out the exit dwell. Only the DECISION is cleared — the EMA is left intact and
        /// the normal 0.12 s ramp plus every restore path (foliage, siblings, mounted, stacked,
        /// body) does the rest, so a released wall is indistinguishable from any other un-fade.
        /// Justification against the dwell's own reasoning is in the class header. Peer fades and
        /// gate lifts are untouched here; they are not this client's decision to overrule.
        /// </summary>
        private void ReleaseFadesForInside(float now)
        {
            _insideReleased = 0;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.State)
                    continue;
                seg.State = false;
                seg.PendingRaw = false;
                seg.PendingSince = now;
                seg.GateLiftUntil = 0f;
                _insideReleased++;
                LogStateFlip(seg); // the ordinary fade OFF line, so the release is attributable
            }
        }

        /// <summary>Reset the per-pass census the falsifier line reports.</summary>
        private void BeginInsideCensus()
        {
            _insideForcedSolid = 0;
            _insideWouldHaveFaded = 0;
            _insideWorstSmooth = 0f;
            _insideWorstWall = "-";
            _insideCarveOut = 0;
            _insideCarveNames.Clear();
            _insideExternalPeer = 0;
            _insideExternalGate = 0;
            _insideExternalNames.Clear();
            _insideResidual = 0;
            _insideResidualWorst = 0f;
            _insideResidualWall = "-";
            _insideCensusTaken = true;
        }

        /// <summary>
        /// THE STAND-DOWN. Called for every segment AFTER the decision chain and OUTSIDE the
        /// evaluation cadence, so it owns the final <c>seg.State</c> that feeds the ramp and a
        /// skipped evaluation cannot leave a stale decision standing.
        ///
        /// <para>Returns true when this segment's own occlusion decision was forced/held OFF by
        /// the rule — the caller then measures the OUTCOME against the external signals.</para>
        ///
        /// <para>Segments already held solid by another rule (boundless, engulfing, doorway,
        /// room without a valid floor grid) are not counted: they would be solid anyway and
        /// claiming them would inflate the falsifier's own number.</para>
        /// </summary>
        private bool ApplyInsideStandDown(Segment seg, Vector3 headPos, float now)
        {
            if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                || !RoomDecisionValid(seg.RoomIndex))
                return false;

            // THE CARVE-OUT: the head is inside actual masonry, not merely inside the segment's
            // union box. Such a segment runs the ORDINARY live policy — no substituted bars
            // anywhere — so walking into stone still dissolves it after the usual enter dwell.
            if (seg.Bounds.Contains(headPos) && HeadInsideWallMesh(seg, headPos, out string mesh))
            {
                _insideCarveOut++;
                if (_insideCarveNames.Count < InsideNameCap)
                {
                    string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    _insideCarveNames.Add($"'{wall}' (head inside mesh '{mesh}')");
                }
                return false;
            }

            _insideForcedSolid++;
            if (seg.Smooth >= (seg.State ? WallFadeTuning.Off : WallFadeTuning.On))
                _insideWouldHaveFaded++;
            if (seg.Smooth > _insideWorstSmooth)
            {
                _insideWorstSmooth = seg.Smooth;
                _insideWorstWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }

            if (seg.State || seg.PendingRaw)
            {
                seg.State = false;
                seg.PendingRaw = false;
                seg.PendingSince = now;
            }
            // A gate lift is this client's own decision reaching a second wall; while the rule
            // holds, its LINGER must not outlive the decision that armed it.
            seg.GateLiftUntil = 0f;
            return true;
        }

        /// <summary>
        /// Measure what actually became of a forced-solid wall, after the ramp. Called from the
        /// tick with the composed external signals, so the falsifier reports outcomes rather
        /// than the intentions of the branch above.
        /// </summary>
        private void NoteInsideOutcome(Segment seg, bool remoteFade, bool gateLift, int peerId)
        {
            if (remoteFade || gateLift)
            {
                if (remoteFade)
                    _insideExternalPeer++;
                else
                    _insideExternalGate++;
                if (_insideExternalNames.Count < InsideNameCap)
                {
                    string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    _insideExternalNames.Add(remoteFade
                        ? $"'{wall}' (peer {peerId}, wire record 17)"
                        : $"'{wall}' (gate lift)");
                }
                return;
            }
            if (seg.Fade <= 0.005f)
                return;
            _insideResidual++;
            if (seg.Fade > _insideResidualWorst)
            {
                _insideResidualWorst = seg.Fade;
                _insideResidualWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
        }

        /// <summary>
        /// THE STAND-DOWN FALSIFIER. Printed ONLY while the rule is in force, and every number
        /// in it is measured on the pass that just ran:
        /// <list type="bullet">
        /// <item>how many walls the rule forced solid, and how many of those the LIVE bars would
        ///   have faded — if walls are dissolving while the player stands inside and this second
        ///   number is 0, the rule is not what is holding them and the search moves elsewhere;
        ///   </item>
        /// <item>how many an EXTERNAL signal still hides, named and attributed — peer fades are
        ///   the exception the report grants, gate lifts are counted apart because they are this
        ///   client's own decision and must not be passed off as external;</item>
        /// <item>how many carry a fade with NO signal behind them at all, which is the shape of
        ///   a writer this loop does not own.</item>
        /// </list>
        /// </summary>
        private void LogInsideStandDown()
        {
            if (!_insideCensusTaken)
            {
                VRLog.Info(Name,
                    "WALLS FORCED SOLID (INSIDE): no pass has run under the rule yet — the "
                    + "verdict flipped this frame and the numbers below would be an artifact of "
                    + "the ordering, not a measurement.");
                return;
            }
            string carve = _insideCarveOut == 0
                ? "0 wall(s) carved out"
                : $"{_insideCarveOut} wall(s) carved out because the head is inside their own "
                  + $"masonry: {string.Join(", ", _insideCarveNames)}"
                  + (_insideCarveOut > _insideCarveNames.Count
                      ? $", +{_insideCarveOut - _insideCarveNames.Count} more" : "");
            int external = _insideExternalPeer + _insideExternalGate;
            string ext = external == 0
                ? "0 wall(s) are still hidden by an external signal"
                : $"{external} wall(s) are still hidden by another signal "
                  + $"({_insideExternalPeer} peer, {_insideExternalGate} gate lift): "
                  + string.Join(", ", _insideExternalNames)
                  + (external > _insideExternalNames.Count
                      ? $", +{external - _insideExternalNames.Count} more" : "");
            string residual = _insideResidual == 0
                ? "and none of the forced-solid walls carries an unexplained fade"
                : $"and {_insideResidual} forced-solid wall(s) still carry a fade with NO signal "
                  + $"behind it — worst '{_insideResidualWall}' at fade "
                  + $"{_insideResidualWorst:F2} (transient while a fade ramps out; persistent "
                  + "means a writer this loop does not own)";
            VRLog.Info(Name,
                $"WALLS FORCED SOLID (INSIDE): {_insideForcedSolid} wall(s) held at their own "
                + $"decision OFF this pass, {_insideWouldHaveFaded} of which the live bars "
                + $"{WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2} would have faded "
                + $"(worst '{_insideWorstWall}' at ema {_insideWorstSmooth:F2}); "
                + $"{carve}; {ext}, {residual}.");
        }

        /// <summary>
        /// THE FALSIFIER. One line that can disagree with itself: the verdict, the term that
        /// decided it, the head against the board volume in world units AND in real metres, the
        /// live bars, how many walls the raised bar spared this evaluation, and the coverage of
        /// the worst offender among them. Every number is an outcome, not an intention.
        ///
        /// <para>The real-metre column is a CONVERSION of the world-unit numbers by the live rig
        /// scale and nothing in the decision reads it — the decision's bars are world units
        /// compared against world-unit geometry. It is here because "wie tief stehe ich drin" is
        /// unanswerable in world units at a zoom the player rides from 8.28 to 1.15.</para>
        /// </summary>
        private void LogInsideState(Vector3 headPos, float rigScale, bool edge)
        {
            if (!_boardVolumeValid)
                return;
            Vector3 mn = _boardVolume.min, mx = _boardVolume.max;
            bool byY = _lastInsideMarginY >= _lastInsideMarginXZ;
            string term = _insideBoard
                ? (byY ? "HEIGHT (the eye is below the wall crest, and that is the tighter of "
                       + "the two terms)"
                       : "FOOTPRINT (the head is over the board, and that is the tighter of the "
                       + "two terms)")
                : (byY ? "HEIGHT (the eye is at or above the wall crest)"
                       : "FOOTPRINT (the head is beyond the board edge)");
            string metres = rigScale > 1e-4f
                ? $"{(headPos.y - _boardFloorY) / rigScale:F2} m above the floor plane, walls "
                  + $"{_boardCrestWU / rigScale:F2} m tall, signed distance "
                  + $"{_lastInsideSigned / rigScale:F2} m"
                : "n/a (rig scale unreadable)";
            VRLog.Info(Name,
                $"INSIDE THE MAP: {(_insideBoard ? "YES" : "no")}"
                + $"{(edge ? " [EDGE]" : "")} — head "
                + $"({headPos.x:F1}, {headPos.y:F2}, {headPos.z:F1}) vs board volume "
                + $"[x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, z {mn.z:F1}..{mx.z:F1}] "
                + $"world units, signed distance {_lastInsideSigned:F2} wu "
                + $"(slabs: Y {_lastInsideMarginY:F2}, XZ {_lastInsideMarginXZ:F2}); "
                + $"deciding term {term}. Crest {_boardCrestWU:F2} wu, bars enter "
                + $"<= {-InsideEnterDepthFraction * _boardCrestWU:F2} / leave "
                + $">= {InsideExitDepthFraction * _boardCrestWU:F2} wu. In real metres at rig "
                + $"scale {rigScale:F2} wu per metre: {metres}. "
                + (_insideBoard
                    ? "This client's own occlusion fade is FORCED OFF for every wall; the "
                      + "per-pass counts are on the WALLS FORCED SOLID (INSIDE) line. "
                      + $"{_insideReleased} wall(s) were released on entry."
                    : $"Normal bars {WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2} in force — "
                      + "the fade policy is running unchanged."));
        }
    }
}
