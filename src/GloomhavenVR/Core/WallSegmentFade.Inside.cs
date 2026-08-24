using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// BOARD VOLUME — an OBSERVATION of where the player's head is relative to the board, and a
/// RETIREMENT RECORD for the two policies that were built on it. Nothing in this file gates a
/// fade any more. Read the retirement record before proposing a third one.
///
/// <para>WHAT WAS TRIED, AND WHAT THE HARDWARE SAID.
/// <list type="number">
/// <item>ModBuild 241-250 — A RAISED BAR. While the head read INSIDE, the coverage pair
///   0.98/0.90 was substituted for the live bars. It was inert: <c>BlockedFraction</c>'s
///   head-inside-AABB shortcut returned a hard <c>1f</c> from OPEN FLOOR, because the AABB it
///   tested was the segment's union box, and 1.00 clears 0.98 exactly as easily as it clears
///   0.10. 75 diag samples in the ModBuild 250 log carry that signature.</item>
/// <item>ModBuild 251 — A HARD STAND-DOWN. While the head read INSIDE, this client's own
///   occlusion decision was forced OFF for every wall at once, with a mesh-keyed carve-out. It
///   worked exactly as designed and the user rejected it in one build:
///   <i>"Seit deiner letzten Änderung gibt es in der Map nur noch zwei Stati: Entweder alle
///   grünen Wände verschwinden auf einmal, oder alle sind da. Ich will aber das jede Wand
///   einzeln verschwinden kann und andere bleiben."</i></item>
/// </list></para>
///
/// <para>WHY BOTH FAILED — ONE CAUSE, TWO SYMPTOMS. A scene-wide switch cannot express a
/// per-wall fact, and the trigger was never measuring what it claimed to. In the ModBuild 251
/// log the verdict flips YES/no four times in one session, and the deciding term is
/// <c>FOOTPRINT</c> as often as <c>HEIGHT</c> (27 against 26):
/// <c>INSIDE THE MAP: YES — head (0.1, 2.78, 12.4) … deciding term FOOTPRINT (the head is over
/// the board) … 0.34 m above the floor plane, walls 0.61 m tall</c>. Those are real metres: the
/// board is a diorama with 61 cm walls and his eyes are a third of a metre above its floor
/// plane. "INSIDE THE MAP" was firing when he leaned over his own table. Each rising edge then
/// ran a release across ALL segments and each pass forced them ALL solid —
/// <c>WALLS FORCED SOLID (INSIDE): 6 wall(s) held … 6 wall(s) were released on entry</c> — which
/// is exactly the "alle auf einmal" he reported, and is also a prime suspect for the un-animated
/// return he reported in the same breath.</para>
///
/// <para>WHAT REPLACES IT: NOTHING. The per-wall coverage metric is the whole decision, which is
/// what the user asked for in his own words — <i>"Es sollte anhand der verdeckten Boden-tiles des
/// jeweiligen Raumes berechnet werden, oder?"</i> — and what the subsystem was always designed to
/// do. The narrow case the stand-down existed to protect (his head genuinely inside masonry) is
/// already handled PER WALL by <see cref="FadeDriver.HeadInsideWallMesh"/> feeding
/// <c>BlockedFraction</c>'s hard <c>1f</c>: it dissolves the one wall he is standing in and
/// touches no other. A global rule was never needed for it.</para>
///
/// <para>THE STANDING RULE FOR ANYONE READING THIS LATER: this subsystem may not acquire a
/// scene-wide fade switch. If a future symptom seems to want one, it is a per-wall measurement
/// that is wrong. Two builds have now been spent proving that.</para>
///
/// <para>WHAT SURVIVES, AND WHY. The board volume and the INSIDE verdict are kept as a pure
/// DIAGNOSTIC. Their commit phase is already priced in the BUDGET line, the test is ~15 float
/// ops, and "where was his head relative to the board, in real metres" is the single question
/// that would have caught both failures above in round one — the metre column is what shows a
/// 0.61 m wall and a head 0.34 m up. The <c>INSIDE THE MAP</c> line says explicitly that it
/// gates nothing, so it can never again be mistaken for a policy.</para>
///
/// <para>THE TEST ITSELF (unchanged, diagnostic only). INSIDE means the head is inside the BOARD
/// VOLUME — the union XZ footprint of every decision-valid room and its walls, from the board's
/// floor plane up to the median WALL CREST. The measured quantity is the signed distance of the
/// head to that box (negative inside), through a Schmitt pair expressed as fractions of the
/// crest height C so they scale with the board: enter at <c>sd &lt;= -0.10·C</c>, leave at
/// <c>sd &gt;= +0.35·C</c>, dwells 0.20 s in and <see cref="WallFadeTuning.DwellMoved"/> out.
/// The median (not the max) crest keeps a keep's stacked superstructure from putting the crest
/// plane above every wall in the scenario. The 3D map room has no occlusion volumes at all, so
/// the volume is never valid there.</para>
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
        /// <summary>How many walls the per-wall falsifier names individually before it starts
        /// counting the rest. The whole point of that line is to show the walls DISAGREEING, so
        /// it has to name enough of them to see a split.</summary>
        private const int PerWallNameCap = 8;
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
        private float _nextInsideLogTime;

        // --- R2 PER-WALL INDEPENDENCE CENSUS (measured per pass, never intended) ------------
        /// <summary>Fadeable walls judged this pass, and how many of them ended up faded. The
        /// whole point of the falsifier is that these two disagree: 0 or N means every wall
        /// reached the same verdict, anything between them is the independence the user asked
        /// for, demonstrated rather than asserted.</summary>
        private int _pwTotal;
        private int _pwFaded;
        /// <summary>Per-wall detail, in the order the segment table yields it: name, own room,
        /// own smoothed coverage, own verdict. This is the line that answers "jede Wand
        /// einzeln".</summary>
        private readonly List<string> _pwNames = new();
        /// <summary>Session tallies: passes where the walls DISAGREED versus passes where they
        /// were unanimous. A session with zero mixed passes is the defect he reported.</summary>
        private int _pwMixedPasses;
        private int _pwUniformPasses;
        /// <summary>Widest and narrowest smoothed coverage seen this pass, with names — the
        /// spread that proves the walls are being measured separately.</summary>
        private float _pwMaxSmooth, _pwMinSmooth;
        private string _pwMaxWall = "-", _pwMinWall = "-";
        /// <summary>Walls whose fade came from somewhere other than their own decision.</summary>
        private int _pwPeerDriven, _pwGateDriven;
        private float _nextPerWallLogTime;

        // --- R1 ANIMATION-PATH CENSUS ------------------------------------------------------
        /// <summary>Fades in flight this pass that ran the CONTINUOUS occluded-map gradient
        /// sweep, and the count that had to fall back on the two-texture path whose branches do
        /// not meet at Fade == 1 (LOW / mixed shader variants).</summary>
        private int _animSmooth;
        private int _animStepped;
        private readonly List<string> _animSteppedNames = new();

        // --- R1 FOLIAGE CHANNEL CENSUS -----------------------------------------------------
        /// <summary>THE BLIND SPOT THIS ROUND CLOSED. The ModBuild 253 ANIMATION line read
        /// "0 on the stepped two-texture path: every transition in flight is animated end to
        /// end" while the user was watching walls pop. It was true and it was not the picture:
        /// it counted only the WALL renderers, and the DISSOLVE CENSUS it sat next to has no
        /// foliage bucket either. 345 foliage attachments — the visible mass of every scrub
        /// wall — were animated by nobody's measurement. These count them by the channel each
        /// piece actually got.</summary>
        private int _folNative, _folSwapped, _folOwnChannel, _folNoChannel;
        private readonly List<string> _folNoChannelNames = new();

        // --- R2 NUMERATOR ADMISSION CENSUS -------------------------------------------------
        /// <summary>Per wall: how many of its pieces the standing test admitted to the occlusion
        /// numerator and how many it excluded as ground dressing, with the widest excluded piece
        /// named. This is what settles "which renderer inflated Wall 2" without another round.
        /// </summary>
        private readonly List<string> _admitNames = new();

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
                + $"{EnterDwellSeconds:F2}s in / {WallFadeTuning.DwellMoved:F2}s out). The "
                + "verdict is DIAGNOSTIC — it gates no fade; every wall is decided solely by its "
                + $"own coverage against the live bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2}.");
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
                float dwell = _insidePending ? EnterDwellSeconds : WallFadeTuning.DwellMoved;
                if (now - _insidePendingSince >= dwell)
                {
                    _insideBoard = _insidePending;
                    // NO RELEASE. ModBuild 251 handed every faded wall back here in one step;
                    // that is the "alle auf einmal" the user rejected, and it bypassed each
                    // wall's own exit dwell. Crossing this boundary is now an observation with
                    // no side effects at all.
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

        /// <summary>Drop the INSIDE verdict (scenario teardown, toggle off, no valid board).
        /// Purely diagnostic state now — nothing downstream reads it.</summary>
        private void ResetInsideBoardState()
        {
            _insideBoard = false;
            _insidePending = false;
            _insidePendingSince = 0f;
        }

        /// <summary>
        /// Reset the per-pass independence census. Called every frame from the tick, so the
        /// numbers the falsifier prints always describe the pass that just ran.
        /// </summary>
        private void BeginPerWallCensus()
        {
            _pwTotal = 0;
            _pwFaded = 0;
            _pwNames.Clear();
            _pwMaxSmooth = 0f;
            _pwMinSmooth = float.MaxValue;
            _pwMaxWall = "-";
            _pwMinWall = "-";
            _pwPeerDriven = 0;
            _pwGateDriven = 0;
            _animSmooth = 0;
            _animStepped = 0;
            _animSteppedNames.Clear();
            _folNative = 0;
            _folSwapped = 0;
            _folOwnChannel = 0;
            _folNoChannel = 0;
            _folNoChannelNames.Clear();
            _admitNames.Clear();
        }

        /// <summary>
        /// Record the dissolve channel ONE foliage piece actually received this frame — read off
        /// the record <c>EnsureDissolveChannel</c> just filled in, so it reports what the piece
        /// got rather than what it was meant to get. A piece with no channel at all cannot
        /// dissolve; it can only switch off, and it is named.
        /// </summary>
        private void NoteFoliageChannel(Segment seg, MountedProp p)
        {
            if (p.NativeFade)
            {
                if (p.SwapCopies != null)
                    _folSwapped++;
                else
                    _folNative++;
                return;
            }
            if (p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0 || p.System != null)
            {
                _folOwnChannel++;
                return;
            }
            _folNoChannel++;
            if (_folNoChannelNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string piece = p.Renderer != null ? p.Renderer.name : "<dead>";
                _folNoChannelNames.Add($"'{piece}' on '{wall}'");
            }
        }

        /// <summary>
        /// R2 FALSIFIER, second half. Record how ONE wall's pieces split between the occlusion
        /// numerator and the ground dressing the standing test excludes, and name the widest
        /// excluded piece — the one the user asked about directly
        /// (<i>"größere nicht begehbare Flächen … kann es sein, dass diese Flächen irgendeine
        /// Rolle bei dem Problem spielen?"</i>). Measured by re-reading the live bounds, so it
        /// reports the same verdict the numerator used.
        /// </summary>
        private void NoteAdmission(Segment seg)
        {
            if (!seg.HasBounds || seg.DoorRoot != null || _admitNames.Count >= PerWallNameCap)
                return;
            int admitted = 0, excluded = 0;
            float widestExcluded = 0f;
            string widestName = "-";
            CountAdmission(seg.Renderers, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Foliage, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Siblings, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            if (admitted == 0 && excluded == 0)
                return;
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            _admitNames.Add($"'{wall}' {admitted} admitted / {excluded} excluded"
                + (excluded > 0 ? $", widest excluded '{widestName}' {widestExcluded:F1} wu" : ""));
        }

        private static void CountAdmission(List<MeshRenderer> list, ref int admitted,
            ref int excluded, ref float widestExcluded, ref string widestName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                MeshRenderer r = list[i];
                if (r == null)
                    continue;
                Bounds b = r.bounds;
                if (IsStandingPiece(b))
                {
                    admitted++;
                    continue;
                }
                excluded++;
                float widest = Mathf.Max(b.size.x, b.size.z);
                if (widest > widestExcluded)
                {
                    widestExcluded = widest;
                    widestName = r.name;
                }
            }
        }

        /// <summary>
        /// Record ONE wall's own verdict, taken from its own coverage of its own room. Only
        /// decision-eligible walls are counted: a doorway, an engulfing segment, a boundless one
        /// or a wall whose room has no valid floor grid is held solid by a different rule, and
        /// counting those would let the falsifier claim independence it did not measure.
        /// </summary>
        private void NotePerWallVerdict(Segment seg)
        {
            if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                || !RoomDecisionValid(seg.RoomIndex))
                return;
            _pwTotal++;
            if (seg.State)
                _pwFaded++;
            if (seg.Smooth > _pwMaxSmooth)
            {
                _pwMaxSmooth = seg.Smooth;
                _pwMaxWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (seg.Smooth < _pwMinSmooth)
            {
                _pwMinSmooth = seg.Smooth;
                _pwMinWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (_pwNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _pwNames.Add($"'{wall}' r{seg.RoomIndex} ema {seg.Smooth:F2} "
                    + $"blk {seg.LastBlocked}/{seg.LastRoomTotal} "
                    + (seg.State ? "FADED" : "solid"));
            }
            NoteAdmission(seg);
        }

        /// <summary>Record where a wall's fade came from when it was not its own decision.</summary>
        private void NotePerWallOutcome(Segment seg, bool remoteFade, bool gateLift, int peerId)
        {
            if (seg.DoorRoot != null)
                return;
            if (remoteFade)
                _pwPeerDriven++;
            else if (gateLift && !seg.State)
                _pwGateDriven++;
        }

        /// <summary>
        /// Record which animation path a fade in flight actually took this frame. Called from
        /// <c>Apply</c>, so it reports the branch that ran rather than the branch intended.
        /// </summary>
        private void NoteAnimationPath(Segment seg, bool smooth)
        {
            if (smooth)
            {
                _animSmooth++;
                return;
            }
            _animStepped++;
            if (_animSteppedNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _animSteppedNames.Add($"'{wall}' "
                    + (seg.VariantLow && seg.VariantHigh ? "HIGH+LOW" : "LOW")
                    + $" fade {seg.Fade:F2}");
            }
        }

        /// <summary>
        /// R2 FALSIFIER (user ruling 2026-08-24: <i>"Ich will aber das jede Wand einzeln
        /// verschwinden kann und andere bleiben"</i>). One line that shows the walls DISAGREEING
        /// — every number measured on the pass that just ran, and the per-wall detail printed so
        /// the disagreement is visible rather than asserted.
        ///
        /// <para>THE NUMBER THAT MATTERS is "faded X of Y". The defect he reported has X equal to
        /// 0 or to Y on every single pass; independence means X spends time strictly between
        /// them. The session tallies below count exactly that, so one grep answers it: a session
        /// with <c>0 mixed</c> has reproduced the defect, whatever the rest of the line says.</para>
        ///
        /// <para>The coverage SPREAD (widest against narrowest, with names) is the second half of
        /// the proof: walls can only disagree about the verdict if they are being measured
        /// separately, and a spread of zero would mean they are not.</para>
        /// </summary>
        private void LogPerWallIndependence()
        {
            if (_pwTotal == 0)
                return;
            bool mixed = _pwFaded > 0 && _pwFaded < _pwTotal;
            if (mixed)
                _pwMixedPasses++;
            else
                _pwUniformPasses++;
            float minSmooth = _pwMinSmooth == float.MaxValue ? 0f : _pwMinSmooth;
            VRLog.Info(Name,
                $"PER-WALL: faded {_pwFaded} of {_pwTotal} decision-eligible wall(s) this pass — "
                + (mixed
                    ? "MIXED, so the walls are disagreeing and each one is deciding for itself"
                    : _pwFaded == 0
                        ? "all solid (unanimous: no wall's own coverage clears its bar)"
                        : "all faded (unanimous — check the spread below before calling it a "
                          + "global switch; unanimity is legitimate when every wall really is "
                          + "in the way)")
                + $". Session so far: {_pwMixedPasses} mixed pass(es) vs {_pwUniformPasses} "
                + "unanimous — a session that never goes mixed is the defect reported on "
                + "2026-08-24. Coverage spread this pass: widest "
                + $"'{_pwMaxWall}' {_pwMaxSmooth:F2}, narrowest '{_pwMinWall}' {minSmooth:F2}, "
                + $"bars {WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}. Not its own decision: "
                + $"{_pwPeerDriven} peer-driven, {_pwGateDriven} gate-lifted. Per wall: "
                + string.Join(" | ", _pwNames)
                + (_pwTotal > _pwNames.Count ? $" | +{_pwTotal - _pwNames.Count} more" : "")
                + ". Numerator admission (standing pieces that can hide floor vs ground dressing "
                + "that only rides the fade): " + string.Join(" | ", _admitNames) + ".");
        }

        /// <summary>
        /// R1 FALSIFIER (user ruling 2026-08-24: <i>"Wände sollen niemals einfach so auftauchen
        /// und wieder verschwinden … IMMER mit der Animation, niemals ohne"</i>). Reports which
        /// animation path every fade IN FLIGHT actually took this frame — measured in
        /// <c>Apply</c>, not inferred.
        ///
        /// <para>A wall on the CONTINUOUS path sweeps one cutoff across the occluded map's
        /// world-Y gradient from fully solid to exactly the held look, with no texture swap and
        /// no step. A wall on the STEPPED path is a LOW or mixed shader variant, whose two
        /// branches do not meet at <c>Fade == 1</c>: it is fully clipped just below 1 and gets
        /// its foundation band back at 1. Those are named, because a non-zero stepped count is
        /// the remaining un-animated transition in the subsystem and it needs a shader change
        /// rather than a tuning.</para>
        ///
        /// <para>Silent when nothing is fading, so the line's presence already means a
        /// transition was in flight when it printed.</para>
        /// </summary>
        private void LogAnimationPaths()
        {
            int foliage = _folNative + _folSwapped + _folOwnChannel + _folNoChannel;
            if (_animSmooth == 0 && _animStepped == 0 && foliage == 0)
                return;
            VRLog.Info(Name,
                $"ANIMATION: {_animSmooth + _animStepped} wall renderer fade(s) in flight this "
                + $"frame — {_animSmooth} on the CONTINUOUS occluded-map gradient sweep (solid → "
                + "held look in one unbroken cutoff ramp, no texture swap, no step at either end)"
                + (_animStepped == 0
                    ? " and 0 on the stepped two-texture path"
                    : $", {_animStepped} still on the STEPPED two-texture path — these pop their "
                      + "foundation band at the Fade==1 boundary and need a shader change: "
                      + string.Join(", ", _animSteppedNames)
                      + (_animStepped > _animSteppedNames.Count
                          ? $", +{_animStepped - _animSteppedNames.Count} more" : ""))
                // THE HALF THE ModBuild 253 LINE DID NOT COUNT. It reported only the numbers
                // above, said "every transition in flight is animated end to end", and was
                // believed — while the largest population in the scene was measured by nobody.
                + $". FOLIAGE this frame: {foliage} attachment(s) — {_folNative} on the wall's "
                + $"own native ramp, {_folSwapped} on swapped masonry-fade copies, "
                + $"{_folOwnChannel} on their own alpha/cutoff/particle channel, "
                + (_folNoChannel == 0
                    ? "and 0 with NO dissolve channel at all: every foliage piece in flight is "
                      + "animated, not switched."
                    : $"and {_folNoChannel} with NO CHANNEL AT ALL — these cannot dissolve, they "
                      + "can only switch off, and they are the pop: "
                      + string.Join(", ", _folNoChannelNames)
                      + (_folNoChannel > _folNoChannelNames.Count
                          ? $", +{_folNoChannel - _folNoChannelNames.Count} more" : "")));
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
                + "THIS VERDICT GATES NOTHING — it is an observation, not a policy (two builds "
                + "were spent proving a scene-wide switch cannot express a per-wall fact; see "
                + "the retirement record on WallSegmentFade.Inside.cs). Every wall is decided "
                + $"solely by its own coverage against the live bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2} — the PER-WALL line carries that pass's verdicts.");
        }
    }
}
