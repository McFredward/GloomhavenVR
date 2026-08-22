using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP ROOM'S BENCHES — a bench at each END of the table the campaign map lies on, built
/// PROCEDURALLY at runtime as one mesh, standing still in the world for as long as the room does.
///
/// <para>USER, verbatim (translated): "On the flat map I can see that next to the table asset the
/// map lies on, there are also BENCHES beside it — I would like to bring those into the environment
/// too, in the SAME ARRANGEMENT, for the immersion." He said the same thing at the very start of
/// this feature and it is quoted in the plan (<c>.planning/worldmap-3d.md:12</c>): <i>"Die world
/// map steht auf einem Tisch mit Bänken an beinden Enden."</i> — a bench at BOTH ENDS. That
/// sentence is the arrangement's count and its orientation, and it is the user's own reading of the
/// picture the flat game draws.</para>
///
/// <para>WHAT COULD AND COULD NOT BE MEASURED, SEPARATED — because this project's rule is that a
/// number read from a source and a number inferred from scene data are never printed as the same
/// kind of fact.</para>
/// <list type="bullet">
///   <item>MEASURED, off the live scene, every time this builds: the table's own footprint and its
///   top plane. "The table" in this room is not an object — it is the PARCHMENT'S world bounds
///   widened by <see cref="TableRimMeters"/>, which is exactly how <see cref="MapRoomSeat"/>,
///   <c>MapButtonRail</c> and <c>MapLocationInteractor</c> already define it (the last of those
///   even uses the same 0.20 m rim). So the benches are sized and placed against the same table
///   every other part of the room is placed against, and they cannot drift from it.</item>
///   <item>MEASURED, off the live scene: which way the table runs. The map's world AABB is
///   190.54 x 237.74 world units in the captured session dump, so its LONG axis is world Z and its
///   SHORT axis world X, and the ends the benches stand at are the ends of the long axis.</item>
///   <item>MEASURED, off the live rig: the floor plane and the rig scale, from
///   <see cref="MapRoomSeat.Seat"/>. Every dimension below is a REAL METRE and is multiplied by
///   that scale exactly once, at build. This room runs at ~198 world units per metre and mixing the
///   two has shipped as a bug here before.</item>
///   <item>NOT MEASURABLE, and this is stated rather than papered over: THE GAME'S OWN TABLE AND
///   BENCHES ARE NOT REACHABLE AS OBJECTS. The captured MAP SCENE REPORT (a live phase-0 dump)
///   lists the map roots as holding exactly ONE renderer — the parchment, a 190.54 x 0.13 x 237.74
///   slab — and <c>TOTAL 0 renderer(s)</c> besides it; the decompiled <c>MapChoreographer</c>
///   carries no table, bench, seat, stool or chair field; and a grep of the decompiled game for
///   bench/stool/chair finds only <c>BenchedCharacter</c>, which is a party member on the bench,
///   not furniture. So NO GAME-SIDE BENCH WAS MEASURED, because none was found to measure. The
///   sizes below therefore come from the project's OWN already-measured furniture spec
///   (<c>unity/.../Editor/BuildMapTable.cs</c>, whose 0.45 m seat is the cellar stool's measured
///   seat height and whose 0.13 m knee gap and 0.3815 m seat depth are two boards of the
///   photographed <c>dark_wooden_planks</c>), and they are labelled AUTHORED in the log.</item>
///   <item>AND THE INSTRUMENT SHIPS WITH THE PROP. <see cref="SurveyNeighbours"/> runs ONCE, at
///   build, and lists every NON-MOD renderer standing near the map and below its top plane, in
///   world units AND in real metres. If the game does have furniture out there after all, the next
///   log says so with numbers and this file's constants can be corrected from them. A claim that
///   something is not there is worth much more when the thing that would have found it also ran.
///   </item>
/// </list>
///
/// <para>IT DOES NOT MOVE THE PLAYER, AND THAT IS STRUCTURAL, NOT CAREFUL. This class only ever
/// READS <see cref="MapRoomSeat.Seat"/>; it writes nothing back, touches no rig transform, no
/// parchment, no icon, no window and no game object of any kind. <c>MapRoomSeat</c>'s own doc names
/// the constraint — "Phase 1 already stands the player as though the table were there, so Phase 2
/// cannot change the player's eye line" — and the only new value this file adds to that class is
/// <see cref="MapRoomSeat.SeatHeightMeters"/>, which nothing in the seat solve reads.</para>
///
/// <para>WHEN THE PLAYER WOULD BE STANDING IN A BENCH, THE BENCH MOVES. The seat is solved on the
/// side the flat game reads the map from, and in the captured session that is the map's SHORT side
/// (camera euler (80, 90, 0) => the player stands on -X), i.e. at right angles to both benches, so
/// nothing collides. But the camera decides that, not this file, and if a bench's footprint ever
/// contains the player's standing point the BENCH is pushed outward until it clears him by
/// <see cref="BodyClearanceMeters"/> — never the player. Moving furniture is a decision; moving an
/// occupied player is the teleport the ModBuild-131 ruling forbids.</para>
///
/// <para>THE FEET GO THROUGH THE FLOOR ON PURPOSE. The room's floor and the player's tracking floor
/// are NOT the same plane: the captured room placement prints "the player's real floor is y
/// -154.55, i.e. 23.90 world units = perceived 0.12 m above the room floor", because the
/// environment is placed by the diorama rule (underside - 0.75 x extent) while the seat is placed
/// by <c>TableTopHeightMeters</c>. A leg cut to the player's floor would hang 12 cm in the air —
/// visible — so every leg runs <see cref="FootSinkMeters"/> = 0.20 m BELOW it instead. A leg buried
/// in the floor cannot be seen; a floating one can. This also means the prop needs no reference to
/// the environment lane at all, which keeps it working in mixed reality and in Default/OffBlack
/// where there is no room and no floor.</para>
///
/// <para>MULTIPLAYER: nothing on the wire, and nothing foreclosed. Every input is pure game-scene
/// state (the parchment's bounds) plus the local rig scale, so two clients standing in the same map
/// build byte-identical benches with zero packets — the same argument the environment room already
/// makes for the moon. <see cref="Seats"/> publishes the four bench places (two per bench, 0.65 m
/// apart, facing the table) in world space, which is the shape plan §5 phase 8 asks for: "fixed
/// bench seats by stable player index … so every client agrees who sits where without a single byte
/// on the wire". Seat ASSIGNMENT is deliberately not built here; the geometry it would need is.</para>
///
/// <para>COST: one combined mesh, <see cref="TriangleCount"/> triangles, ONE MeshRenderer with ONE
/// material, so ONE draw call. Built once per room, never written again — no Update, no
/// per-frame allocation, no MaterialPropertyBlock, no collider (a collider here would put furniture
/// into the laser's pick path, and the benches are scenery, not targets). Unity's dynamic batching
/// cannot touch it (>300 verts) and it does not need to: it is already one renderer.</para>
/// </summary>
internal sealed class MapRoomBenches
{
    private const string Scope = "MapRoom";

    // ---- THE ARRANGEMENT, IN REAL METRES ----------------------------------------------------
    // Sources are named per constant. Everything here is multiplied by the rig scale EXACTLY ONCE,
    // in Build, and never again.

    /// <summary>How far the table reaches past the parchment's own edge, real metres. Not a new
    /// number: <c>MapLocationInteractor.TableRimMeters</c> is the same 0.20 m, and it is what that
    /// file already treats as "the wood around the map" when it decides whether a ray is on the
    /// table. Restated here rather than shared because the two files are owned by different lanes;
    /// if one ever changes, the log below prints the table's derived size and the disagreement is
    /// visible in one line.</summary>
    internal const float TableRimMeters = 0.20f;

    /// <summary>Clear space between the table's end and the bench's near edge, real metres.
    /// AUTHORED — <c>BuildMapTable.BenchGap</c>: "knees clear the top, and a STANDING player (the
    /// normal case in VR) has somewhere to put his feet without stepping on a bench".</summary>
    internal const float BenchGapMeters = 0.13f;

    /// <summary>Bench seat depth, real metres. AUTHORED — <c>BuildMapTable.SeatD</c>, which is two
    /// boards of the photographed <c>dark_wooden_planks</c> (0.3815 m).</summary>
    internal const float SeatDepthMeters = 0.3815f;

    /// <summary>Seat plank thickness, real metres. AUTHORED — <c>BuildMapTable.SeatT</c>.</summary>
    internal const float SeatThicknessMeters = 0.045f;

    /// <summary>How far in from each end of the bench a leg stands, real metres. AUTHORED —
    /// <c>BuildMapTable</c> puts the legs of a 1.30 m bench at ±0.50 m, i.e. 0.15 m in.</summary>
    internal const float LegInsetMeters = 0.15f;

    /// <summary>Leg width ACROSS the bench (i.e. along its depth) and thickness ALONG it, real
    /// metres. AUTHORED — <c>BuildMapTable</c>'s 0.2145 m x 0.055 m plank leg.</summary>
    internal const float LegWidthMeters = 0.2145f;

    /// <inheritdoc cref="LegWidthMeters"/>
    internal const float LegThicknessMeters = 0.055f;

    /// <summary>How far in from each end of the bench the stretcher rail stops, real metres.
    /// AUTHORED — <c>BuildMapTable</c>'s 1.02 m rail on a 1.30 m bench.</summary>
    internal const float RailInsetMeters = 0.14f;

    /// <summary>Stretcher rail depth and height, real metres. AUTHORED — <c>BuildMapTable</c>.</summary>
    internal const float RailDepthMeters = 0.08f;

    /// <inheritdoc cref="RailDepthMeters"/>
    internal const float RailHeightMeters = 0.075f;

    /// <summary>How far below the PLAYER'S tracking floor every leg is cut off, real metres. See
    /// THE FEET GO THROUGH THE FLOOR ON PURPOSE in the class doc: the measured disagreement between
    /// the room floor and the player's floor is 0.12 m, and this covers it with margin.</summary>
    internal const float FootSinkMeters = 0.20f;

    /// <summary>Deliberate interpenetration between parts, real metres. AUTHORED —
    /// <c>BuildMapTable.Weld</c>: parts that share a face exactly z-fight along it, and 5 mm of
    /// overlap is invisible at any viewing distance a player can reach at a table.</summary>
    internal const float WeldMeters = 0.005f;

    /// <summary>Bounds on the derived bench length, real metres. A bench shorter than this is not a
    /// bench, and one longer than this is a beam — both mean the parchment measured something
    /// absurd, and a clamp keeps a bad read as an ugly prop rather than as geometry stretching to
    /// the horizon.</summary>
    internal const float MinBenchLengthMeters = 0.90f;

    /// <inheritdoc cref="MinBenchLengthMeters"/>
    internal const float MaxBenchLengthMeters = 2.60f;

    /// <summary>How much room a standing player is given before a bench is pushed out of him, real
    /// metres. Roughly a shoulder width plus the toes.</summary>
    internal const float BodyClearanceMeters = 0.30f;

    /// <summary>Distance between the two seat places on one bench, real metres. AUTHORED —
    /// <c>BuildMapTable</c>'s seat anchors sit at z = ±0.325, i.e. 0.65 m apart.</summary>
    internal const float SeatPitchMeters = 0.65f;

    /// <summary>Real metres one tile of the shared keycap grain covers on the bench. Chosen so the
    /// grain reads as boards rather than as a pattern: at 0.60 m a 1.36 m seat carries a little over
    /// two tiles along its length.</summary>
    internal const float GrainTileMeters = 0.60f;

    /// <summary>Brightness of the bench wood relative to the table buttons' own well colour. The
    /// benches must read as the SAME wood as the rail that stands on the table, one step lighter so
    /// they separate from it at a distance instead of merging into one dark mass.</summary>
    private const float WoodBoost = 1.9f;

    /// <summary>Boxes per bench: seat, two legs, one stretcher rail.</summary>
    private const int BoxesPerBench = 4;

    /// <summary>Benches: one at each end of the table's long axis (the user's "an beiden Enden").</summary>
    internal const int BenchCount = 2;

    /// <summary>Triangles in the finished prop: 2 benches x 4 boxes x 12 triangles.</summary>
    internal const int TriangleCount = BenchCount * BoxesPerBench * 12;

    /// <summary>How many neighbouring renderers the one-shot survey will name before it stops.</summary>
    private const int SurveyCap = 12;

    /// <summary>How far out the survey looks, as a multiple of the map's own widest extent.</summary>
    private const float SurveyRadiusFactor = 1.5f;

    /// <summary>
    /// THE ARRANGEMENT AS PURE ARITHMETIC — no Unity scene access, no engine ECall, so it can be
    /// reasoned about (and, if a later round wants, tested) without a player. Distances are REAL
    /// METRES; the world-unit conversion happens once, in <see cref="Build"/>.
    /// </summary>
    internal readonly struct Arrangement
    {
        /// <summary>True when the table's LONG axis is world Z (i.e. the benches stand at ±Z).
        /// Measured from the parchment's world AABB, which is the frame every other part of this
        /// room already treats as the table.</summary>
        internal readonly bool LongAxisIsZ;

        /// <summary>Half the table's extent along its long axis (map half-extent + rim), metres.</summary>
        internal readonly float TableHalfLengthMeters;

        /// <summary>The table's full extent across its short axis (map extent + two rims), metres —
        /// and therefore the length of each bench.</summary>
        internal readonly float TableWidthMeters;

        /// <summary>Bench length along the short axis, metres. Equals
        /// <see cref="TableWidthMeters"/> unless the clamp bit.</summary>
        internal readonly float BenchLengthMeters;

        /// <summary>Distance from the map centre to the bench's own centre line along the long
        /// axis, metres, for the bench on the +long side and the -long side. They differ only when
        /// one of them had to be pushed clear of the player.</summary>
        internal readonly float PositiveOffsetMeters;

        /// <inheritdoc cref="PositiveOffsetMeters"/>
        internal readonly float NegativeOffsetMeters;

        /// <summary>How far each bench had to be pushed OUT to clear the player's standing point,
        /// metres. Zero in the normal case; log material when it is not.</summary>
        internal readonly float PositivePushMeters;

        /// <inheritdoc cref="PositivePushMeters"/>
        internal readonly float NegativePushMeters;

        /// <summary>True when <see cref="MinBenchLengthMeters"/>/<see cref="MaxBenchLengthMeters"/>
        /// had to bite — i.e. the map measured something the furniture rule did not expect.</summary>
        internal readonly bool LengthClamped;

        internal Arrangement(bool longAxisIsZ, float tableHalfLength, float tableWidth,
                             float benchLength, float positiveOffset, float negativeOffset,
                             float positivePush, float negativePush, bool lengthClamped)
        {
            LongAxisIsZ = longAxisIsZ;
            TableHalfLengthMeters = tableHalfLength;
            TableWidthMeters = tableWidth;
            BenchLengthMeters = benchLength;
            PositiveOffsetMeters = positiveOffset;
            NegativeOffsetMeters = negativeOffset;
            PositivePushMeters = positivePush;
            NegativePushMeters = negativePush;
            LengthClamped = lengthClamped;
        }

        /// <summary>Unit world direction of the table's LONG axis.</summary>
        internal Vector3 LongAxis => LongAxisIsZ ? Vector3.forward : Vector3.right;

        /// <summary>Unit world direction of the table's SHORT axis — the direction a bench runs.</summary>
        internal Vector3 ShortAxis => LongAxisIsZ ? Vector3.right : Vector3.forward;

        /// <summary>Signed offset of bench <paramref name="index"/> (0 = +long, 1 = -long).</summary>
        internal float SignedOffsetMeters(int index) =>
            index == 0 ? PositiveOffsetMeters : -NegativeOffsetMeters;
    }

    /// <summary>
    /// Solve the arrangement from the table the room already has. PURE — the only inputs are the
    /// parchment's world AABB, the player's floor point, and the rig scale, all of which the seat
    /// solve has already produced.
    /// </summary>
    /// <param name="parchmentWorld">The parchment renderer's world bounds (the map itself).</param>
    /// <param name="floorPositionWorld">The player's tracking-floor point (read, never written).</param>
    /// <param name="scale">Game units per real metre.</param>
    internal static bool SolveArrangement(Bounds parchmentWorld, Vector3 floorPositionWorld,
                                          float scale, out Arrangement arrangement)
    {
        arrangement = default;
        if (!(scale > 0.0001f) || float.IsNaN(scale) || float.IsInfinity(scale))
            return false;

        // THE DEGENERATE GUARD IS IN WORLD UNITS, ON PURPOSE — MapRoomSeat.MinUsableExtent is a
        // WORLD-unit threshold ("the map GameObject exists but its mesh has not been built yet"),
        // so it is tested BEFORE the division, against the same quantity MapRoomSeat.Solve tests.
        // Testing it after the division would compare a metre against a world unit, which is the
        // exact unit slip this room has shipped before (the 0.15 mm laser).
        Vector3 size = parchmentWorld.size;
        if (Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) < MapRoomSeat.MinUsableExtent)
            return false;
        float xMeters = Mathf.Abs(size.x) / scale;
        float zMeters = Mathf.Abs(size.z) / scale;

        bool longIsZ = zMeters >= xMeters;
        float longMeters = longIsZ ? zMeters : xMeters;
        float shortMeters = longIsZ ? xMeters : zMeters;

        // THE TABLE. Not an object — the map's own footprint widened by the rim, exactly as
        // MapLocationInteractor already widens it to decide what "on the table" means.
        float tableHalfLength = longMeters * 0.5f + TableRimMeters;
        float tableWidth = shortMeters + 2f * TableRimMeters;

        float benchLength = Mathf.Clamp(tableWidth, MinBenchLengthMeters, MaxBenchLengthMeters);
        bool clamped = !Mathf.Approximately(benchLength, tableWidth);

        // The bench's centre line: past the table's end, past the knee gap, then half a seat.
        float baseOffset = tableHalfLength + BenchGapMeters + SeatDepthMeters * 0.5f;

        // ...AND OUT OF THE PLAYER, IF HE HAPPENS TO BE STANDING THERE. The seat is solved on the
        // side the flat game reads the map from; that is usually at right angles to both benches,
        // but the game's camera decides it, not this file.
        Vector3 toPlayer = (floorPositionWorld - parchmentWorld.center) / scale;
        float alongLong = longIsZ ? toPlayer.z : toPlayer.x;
        float alongShort = longIsZ ? toPlayer.x : toPlayer.z;
        float positivePush = PushClearOfPlayer(baseOffset, benchLength, +alongLong, alongShort);
        float negativePush = PushClearOfPlayer(baseOffset, benchLength, -alongLong, alongShort);

        arrangement = new Arrangement(longIsZ, tableHalfLength, tableWidth, benchLength,
                                      baseOffset + positivePush, baseOffset + negativePush,
                                      positivePush, negativePush, clamped);
        return true;
    }

    /// <summary>
    /// How far one bench must move OUTWARD, in metres, so that the player's standing point is not
    /// inside its footprint (widened by <see cref="BodyClearanceMeters"/>). Both arguments are
    /// expressed in that bench's own outward direction, so the two ends share one implementation.
    /// Zero whenever the player is beside the bench rather than in it.
    /// </summary>
    internal static float PushClearOfPlayer(float baseOffsetMeters, float benchLengthMeters,
                                            float playerAlongOutward, float playerAlongBench)
    {
        // Beside it, not in it: the player is past either end of the bench's length.
        if (Mathf.Abs(playerAlongBench) > benchLengthMeters * 0.5f + BodyClearanceMeters)
            return 0f;
        float nearEdge = baseOffsetMeters - SeatDepthMeters * 0.5f;
        float wanted = playerAlongOutward + BodyClearanceMeters;
        return wanted > nearEdge ? wanted - nearEdge : 0f;
    }

    // ---- state -------------------------------------------------------------------------------

    private GameObject? _root;
    private Mesh? _mesh;
    private Material? _material;
    private readonly List<Vector3> _verts = new(256);
    private readonly List<Vector3> _norms = new(256);
    private readonly List<Vector2> _uvs = new(256);
    private readonly List<int> _tris = new(384);
    private readonly List<Vector3> _seats = new(BenchCount * 2);
    private readonly List<Quaternion> _seatFacings = new(BenchCount * 2);
    private bool _failureLogged;

    /// <summary>True once the benches stand. Log material, and the one-shot latch.</summary>
    internal bool Standing => _root != null;

    /// <summary>
    /// The four bench places in WORLD space while the benches stand — two per bench,
    /// <see cref="SeatPitchMeters"/> apart, in a deterministic order every client resolves
    /// identically (bench at +long first, then -long; within a bench, +short first). This is the
    /// geometry plan §5 phase 8's "fixed bench seats by stable player index" needs; the assignment
    /// itself is NOT built here, and nothing reads this yet.
    /// </summary>
    internal IReadOnlyList<Vector3> Seats => _seats;

    /// <summary>Facing of each entry in <see cref="Seats"/> — toward the table, i.e. along the
    /// bench's own inward normal, because a person on a bench faces the table and not its centre
    /// (the same rule <c>BuildMapTable</c>'s seat anchors were authored with).</summary>
    internal IReadOnlyList<Quaternion> SeatFacings => _seatFacings;

    /// <summary>
    /// Per-frame upkeep while the map room stands. Builds ONCE and then does nothing at all — the
    /// prop is world-fixed furniture (the ModBuild-131 ruling: nothing in this room re-orients with
    /// the head or re-seats itself), so after the first successful build this is a null test.
    /// </summary>
    internal void Tick()
    {
        if (_root != null)
            return;
        Build();
    }

    /// <summary>Tear the benches down. Idempotent; the only exit.</summary>
    internal void Release(string reason)
    {
        bool had = _root != null;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        if (_mesh != null)
            Object.Destroy(_mesh);
        _mesh = null;
        if (_material != null)
            Object.Destroy(_material);
        _material = null;
        _seats.Clear();
        _seatFacings.Clear();
        _failureLogged = false;
        if (had)
            VRLog.Info(Scope, $"MAP ROOM BENCHES released ({reason}) — the prop, its mesh and its "
                              + "material are destroyed. Nothing on the game's own map was ever "
                              + "touched by them: the benches only READ the parchment's bounds.");
    }

    // ---- build -------------------------------------------------------------------------------

    private void Build()
    {
        if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _))
            return;
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return;
        Bounds table = parchment.bounds;
        float scale = Mathf.Max(seat.Scale, 0.0001f);
        if (!SolveArrangement(table, seat.FloorPosition, scale, out Arrangement a))
            return;

        // THE FRAME. Origin at the map's centre in the horizontal plane and at the PLAYER'S
        // tracking floor vertically, unrotated — the parchment's world AABB is the frame the whole
        // room already uses for "the table" (MapRoomSeat.HalfExtentAlong, MapButtonRail's near-edge
        // solve, MapLocationInteractor's table slab), so the benches share it rather than inventing
        // a second one that could disagree with it.
        var origin = new Vector3(table.center.x, seat.FloorPosition.y, table.center.z);
        _root = new GameObject("GloomhavenVR.MapRoomBenches");
        _root.transform.SetPositionAndRotation(origin, Quaternion.identity);

        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _tris.Clear();
        _seats.Clear();
        _seatFacings.Clear();

        float uvPerWorldUnit = 1f / (GrainTileMeters * scale);
        Vector3 lng = a.LongAxis;
        Vector3 shrt = a.ShortAxis;

        float seatSurface = MapRoomSeat.SeatHeightMeters * scale;
        float seatThick = SeatThicknessMeters * scale;
        float seatDepth = SeatDepthMeters * scale;
        float benchLen = a.BenchLengthMeters * scale;
        float underSeat = seatSurface - seatThick + WeldMeters * scale;
        float footY = -FootSinkMeters * scale;

        for (int i = 0; i < BenchCount; i++)
        {
            Vector3 centre = lng * (a.SignedOffsetMeters(i) * scale);

            // The seat plank: its TOP face is the seat surface.
            Box(centre + Vector3.up * (seatSurface - seatThick * 0.5f),
                lng * seatDepth + shrt * benchLen + Vector3.up * seatThick, uvPerWorldUnit);

            // Two plank legs, wide across the bench and thin along it, running from just under the
            // seat down past the player's floor (see THE FEET GO THROUGH THE FLOOR ON PURPOSE).
            float legOffset = benchLen * 0.5f - LegInsetMeters * scale;
            float legHeight = underSeat - footY;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(centre + shrt * (legOffset * s) + Vector3.up * ((underSeat + footY) * 0.5f),
                    lng * (LegWidthMeters * scale) + shrt * (LegThicknessMeters * scale)
                        + Vector3.up * legHeight, uvPerWorldUnit);
            }

            // The stretcher rail, tucked up under the seat between the legs — the piece that makes
            // this read as a joined bench rather than as a plank on two blocks.
            float railLen = Mathf.Max(benchLen - 2f * RailInsetMeters * scale, 0.2f * benchLen);
            float railH = RailHeightMeters * scale;
            Box(centre + Vector3.up * (underSeat - railH * 0.5f),
                lng * (RailDepthMeters * scale) + shrt * railLen + Vector3.up * railH,
                uvPerWorldUnit);

            // The two seat places on this bench, in world space, facing the table.
            Vector3 inward = -lng * Mathf.Sign(a.SignedOffsetMeters(i));
            for (int s = 1; s >= -1; s -= 2)
            {
                _seats.Add(origin + centre + shrt * (SeatPitchMeters * 0.5f * scale * s)
                           + Vector3.up * seatSurface);
                _seatFacings.Add(Quaternion.LookRotation(inward, Vector3.up));
            }
        }

        _mesh = new Mesh { name = "GloomhavenVR.MapRoomBenches" };
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_norms);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_tris, 0, calculateBounds: true);
        // BoardLit samples a normal map through a tangent frame, so a mesh without tangents lights
        // by an undefined basis. Recalculated once, here, never per frame.
        _mesh.RecalculateTangents();

        var geo = new GameObject("Geo");
        geo.transform.SetParent(_root.transform, worldPositionStays: false);
        geo.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = geo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _material = WoodMaterial();
        // Scenery, not a light source and not a shadow caster: the map room has no shadow-casting
        // light of its own, and asking for shadows would only add a second pass over the same 96
        // triangles for nothing.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        // NO COLLIDER, deliberately — see the class doc. The laser's pick path must not start
        // finding furniture.

        // The head camera's mask is the game map camera's mask OR'd with the mod layer, so this is
        // what makes the prop visible at all (MapRoomDriver.ResolveMapMask).
        VRLayers.Apply(_root);

        Report(seat, table, a, scale);
    }

    /// <summary>
    /// The bench wood: the SAME material family as the table's own button caps
    /// (<c>MapButtonRail</c> builds its caps from exactly this pair), so the two props on this table
    /// are made of one wood. <c>GloomhavenVR/BoardLit</c> is baked-lit — it has an ambient floor and
    /// its own studio key — which is the whole reason it is used here: the map room has no
    /// guaranteed lighting, and an ordinary lit shader renders black in it.
    /// </summary>
    private static Material WoodMaterial()
    {
        Shader? lit = Cards.PlayTray.BoardLitShader();
        return lit != null
            ? Cards.PlayTray.NewKeycapMaterial(lit, ButtonTuning.CapWellColor * WoodBoost)
            : WorldUIAssets.CreateFlatMaterial(ButtonTuning.CapWellColor * WoodBoost);
    }

    // ---- geometry ----------------------------------------------------------------------------

    /// <summary>
    /// One closed axis-aligned box: six quads, 24 vertices, 12 triangles, hard edges (each face
    /// carries its own normals, which is what lets <c>BoardLit</c>'s directional term shape it).
    /// <paramref name="size"/> is a full size per axis, in world units.
    /// </summary>
    private void Box(Vector3 centre, Vector3 size, float uvPerWorldUnit)
    {
        Vector3 h = size * 0.5f;
        Vector3 hx = Vector3.right * h.x, hy = Vector3.up * h.y, hz = Vector3.forward * h.z;
        Face(centre + hx, Vector3.right, hz, hy, uvPerWorldUnit);
        Face(centre - hx, Vector3.left, hz, hy, uvPerWorldUnit);
        Face(centre + hy, Vector3.up, hx, hz, uvPerWorldUnit);
        Face(centre - hy, Vector3.down, hx, hz, uvPerWorldUnit);
        Face(centre + hz, Vector3.forward, hx, hy, uvPerWorldUnit);
        Face(centre - hz, Vector3.back, hx, hy, uvPerWorldUnit);
    }

    /// <summary>
    /// One quad, wound so that it faces <paramref name="outward"/>.
    ///
    /// <para>THE WINDING IS DERIVED, NOT HOPED FOR, and this project has earned that sentence: seven
    /// meshes have shipped wound against the side they are seen from and one was invisible for ten
    /// builds. For the corner order below, triangles (0,1,2) and (0,2,3) give a geometric normal of
    /// Cross(hu, hv) — the same convention Unity's own documented quad uses — so when that
    /// disagrees with the outward direction the V half-axis is NEGATED. That flips the winding and
    /// leaves every uv alone, because uv is read from the vertex POSITION rather than from its
    /// corner index. <see cref="Report"/> then checks the finished solid both ways (positive signed
    /// volume, and every triangle agreeing with its own vertex normal) and says so in the log.</para>
    /// </summary>
    private void Face(Vector3 centre, Vector3 outward, Vector3 hu, Vector3 hv, float uvPerWorldUnit)
    {
        if (Vector3.Dot(Vector3.Cross(hu, hv), outward) < 0f)
            hv = -hv;
        Vector3 u = hu.normalized, v = hv.normalized;
        int b = _verts.Count;
        AddVert(centre - hu - hv, outward, u, v, uvPerWorldUnit);
        AddVert(centre + hu - hv, outward, u, v, uvPerWorldUnit);
        AddVert(centre + hu + hv, outward, u, v, uvPerWorldUnit);
        AddVert(centre - hu + hv, outward, u, v, uvPerWorldUnit);
        _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
        _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
    }

    private void AddVert(Vector3 p, Vector3 n, Vector3 u, Vector3 v, float uvPerWorldUnit)
    {
        _verts.Add(p);
        _norms.Add(n);
        // uv from the vertex's own position, so the grain runs continuously across the whole prop
        // and no part of it can drift when a face's winding is flipped above.
        _uvs.Add(new Vector2(Vector3.Dot(p, u) * uvPerWorldUnit, Vector3.Dot(p, v) * uvPerWorldUnit));
    }

    // ---- the report ---------------------------------------------------------------------------

    /// <summary>
    /// THE ONE BENCH LINE. Everything a hardware round needs to decide whether the scale is right
    /// WITHOUT putting the headset on: every dimension in BOTH real metres and game units, the
    /// triangle count, the draw-call count, the two floor planes and the gap between them, and the
    /// mesh's own winding gate. A wrong scale here shows up as an absurd metre value beside a
    /// plausible world-unit one, which is the failure this project has shipped before.
    /// </summary>
    private void Report(MapRoomSeat.Seat seat, Bounds table, Arrangement a, float scale)
    {
        // THE WINDING GATE, ON THE FINISHED SOLID. Cheap (96 triangles) and it runs once.
        float volume = 0f;
        int disagreeing = 0;
        for (int i = 0; i + 2 < _tris.Count; i += 3)
        {
            Vector3 p0 = _verts[_tris[i]], p1 = _verts[_tris[i + 1]], p2 = _verts[_tris[i + 2]];
            volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), _norms[_tris[i]]) <= 0f)
                disagreeing++;
        }
        float volumeCubicMetres = volume / (scale * scale * scale);
        bool woundRight = volume > 0f && disagreeing == 0;
        if (!woundRight && !_failureLogged)
        {
            _failureLogged = true;
            VRLog.Warn(Scope, $"MAP ROOM BENCHES: the winding gate FAILED — signed volume "
                              + $"{volumeCubicMetres:F4} m^3 (must be positive) and {disagreeing} of "
                              + $"{_tris.Count / 3} triangle(s) disagree with their own vertex normal. "
                              + "The prop will be inside-out or partly invisible. This is the "
                              + "winding bug class this project has shipped seven times; read Face() "
                              + "before changing anything else.");
        }

        float benchOuterMetres = a.PositiveOffsetMeters + a.NegativeOffsetMeters + SeatDepthMeters;
        // A SCALE CROSS-CHECK, not a new fact: re-derived from the two world-space numbers this
        // build actually used, it must come back as MapRoomSeat.TableTopHeightMeters (0.78). If it
        // does not, the rig scale used here and the one the seat was solved with disagree, and
        // every metre in this line is worth nothing.
        float topAboveFloorMetres = (table.max.y - seat.FloorPosition.y) / scale;

        VRLog.Info(Scope,
            $"MAP ROOM BENCHES built: {BenchCount} bench(es), one at each END of the table's long "
            + $"axis (world {(a.LongAxisIsZ ? "Z" : "X")}), {TriangleCount} triangles in ONE combined "
            + $"mesh on ONE MeshRenderer with ONE material = 1 DRAW CALL, no collider, no Update, "
            + "world-fixed (nothing here follows the head).\n"
            + $"  user      : \"Die world map steht auf einem Tisch mit Bänken an beinden Enden.\" — "
            + "count and orientation are HIS description of the flat game's picture. NO GAME-SIDE "
            + "BENCH WAS MEASURED: the MAP SCENE REPORT lists 0 renderers under the map roots "
            + "besides the parchment, and MapChoreographer carries no table/bench/seat field. The "
            + "NEIGHBOURS line below is the instrument that would have found one.\n"
            + $"  table     : MEASURED off the parchment — world bounds size {table.size} "
            + $"({Mathf.Abs(table.size.x) / scale:F3} x {Mathf.Abs(table.size.z) / scale:F3} m), "
            + $"widened by the room's own {TableRimMeters:F2} m rim to "
            + $"{2f * a.TableHalfLengthMeters:F3} x {a.TableWidthMeters:F3} m = "
            + $"{2f * a.TableHalfLengthMeters * scale:F1} x {a.TableWidthMeters * scale:F1} world units, "
            + $"top plane y={seat.TopY:F2}.\n"
            + $"  bench     : {a.BenchLengthMeters:F3} x {SeatDepthMeters:F3} m seat "
            + $"({a.BenchLengthMeters * scale:F1} x {SeatDepthMeters * scale:F1} world units), "
            + $"{SeatThicknessMeters * 1000f:F0} mm thick, seat surface "
            + $"{MapRoomSeat.SeatHeightMeters:F3} m ({MapRoomSeat.SeatHeightMeters * scale:F1} world "
            + $"units) above the player's floor — AUTHORED from BuildMapTable, whose 0.45 m is the "
            + $"cellar stool's own measured seat height. Length was "
            + $"{(a.LengthClamped ? "CLAMPED" : "derived from the table's width")}.\n"
            + $"  placement : centre lines {a.PositiveOffsetMeters:F3} m and "
            + $"{a.NegativeOffsetMeters:F3} m from the map centre "
            + $"({a.PositiveOffsetMeters * scale:F1} / {a.NegativeOffsetMeters * scale:F1} world "
            + $"units) = table half-length {a.TableHalfLengthMeters:F3} + knee gap "
            + $"{BenchGapMeters:F2} + half a seat. Prop spans {benchOuterMetres:F3} m "
            + $"({benchOuterMetres * scale:F1} world units) end to end.\n"
            + $"  the player: NOT MOVED and not moveable from here — this class only reads the seat. "
            + $"He stands at {seat.FloorPosition}, on the map's {(a.LongAxisIsZ ? "X" : "Z")} side "
            + $"(view side {seat.ViewSide}), {MapRoomSeat.EdgeStandoffMeters:F2} m outside the map's "
            + $"near edge. Bench push to clear him: +{a.PositivePushMeters:F3} m / "
            + $"-{a.NegativePushMeters:F3} m "
            + $"({(a.PositivePushMeters + a.NegativePushMeters > 0.0005f ? "A BENCH WAS PUSHED — he was standing in it, and the BENCH moved, never him" : "none needed — the benches are at right angles to where he stands")}).\n"
            + $"  floors    : the player's tracking floor is y={seat.FloorPosition.y:F2}, i.e. "
            + $"{topAboveFloorMetres:F3} m below the parchment top — CROSS-CHECK, this must read "
            + $"{MapRoomSeat.TableTopHeightMeters:F3}, and if it does not then the scale used here "
            + "and the scale the seat was solved with disagree. The environment room's floor is "
            + $"placed by a DIFFERENT rule and measured 0.12 m lower in the captured session, so "
            + $"every leg is cut {FootSinkMeters:F2} m ({FootSinkMeters * scale:F1} world units) "
            + "BELOW the tracking floor: a buried leg cannot be seen, a floating one can.\n"
            + $"  mesh      : {_verts.Count} verts, {_tris.Count / 3} tris, signed volume "
            + $"{volumeCubicMetres:F4} m^3, {disagreeing} triangle(s) disagreeing with their own "
            + $"normal — winding gate {(woundRight ? "PASSED" : "FAILED")}.\n"
            + $"  seats     : {_seats.Count} bench place(s) published for the multiplayer seating "
            + $"phase, {SeatPitchMeters:F2} m apart, facing the table. Deterministic order (+long "
            + "bench first), derived from pure game-scene state, so every client resolves the same "
            + "points with ZERO wire bytes. Nothing assigns them yet — that is phase 8.\n"
            + $"  NEIGHBOURS: {SurveyNeighbours(table, scale)}\n"
            + "  DISPROOF  : if the benches look like doll furniture or like barn doors, the metre "
            + "column above is wrong while the world-unit column looks fine — that is a rig-scale "
            + "slip, not an art problem. If they float, compare the two floors above. If they are "
            + "inside-out or half-missing, the winding gate line says so.");
    }

    /// <summary>
    /// ONE-SHOT SURVEY of what the GAME actually has standing near the map — the instrument that
    /// would find a real table or bench if one exists, so "there was nothing to measure" is a
    /// measurement and not an assumption.
    ///
    /// <para>Mod-owned objects are excluded by LAYER, and that exclusion is exact rather than
    /// hopeful: <c>VRLayers.Apply</c> puts every mod prop — including the whole environment room,
    /// which is what the player currently sees as wood under the map — on the mod layer, so what is
    /// left is the game's own renderers and nothing else.</para>
    ///
    /// <para>COST: one <c>FindObjectsOfType</c>, ONCE, on the frame the benches are built, and never
    /// again while the room stands. That is the same order of cost as the map scene report the room
    /// already emits on entry, and deliberately NOT the per-frame kind ModBuild 196 removed.</para>
    /// </summary>
    private static string SurveyNeighbours(Bounds table, float scale)
    {
        try
        {
            float radius = Mathf.Max(Mathf.Abs(table.size.x), Mathf.Abs(table.size.z))
                           * SurveyRadiusFactor;
            float ceiling = table.max.y;
            int mod = VRLayers.ModLayer;
            MeshRenderer[] all = Object.FindObjectsOfType<MeshRenderer>();
            var sb = new StringBuilder(256);
            int found = 0, named = 0;
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer r = all[i];
                if (r == null || r.gameObject.layer == mod)
                    continue;
                Bounds b = r.bounds;
                float dx = b.center.x - table.center.x;
                float dz = b.center.z - table.center.z;
                if (dx * dx + dz * dz > radius * radius || b.min.y > ceiling)
                    continue;
                found++;
                if (named >= SurveyCap)
                    continue;
                named++;
                sb.Append($"\n              '{r.name}' L{r.gameObject.layer} size {b.size} = "
                          + $"{Mathf.Abs(b.size.x) / scale:F2} x {Mathf.Abs(b.size.y) / scale:F2} x "
                          + $"{Mathf.Abs(b.size.z) / scale:F2} m, centre offset "
                          + $"({dx / scale:F2}, {(b.center.y - table.max.y) / scale:F2}, "
                          + $"{dz / scale:F2}) m from the map's centre/top");
            }
            return found == 0
                ? $"NOTHING. {all.Length} MeshRenderer(s) in the scene; none of them is a non-mod "
                  + $"renderer within {radius / scale:F1} m of the map's centre and below its top "
                  + "plane. The game's map screen really is a bare parchment slab — so the "
                  + "arrangement above could not be copied from it and was taken from the user's own "
                  + "description instead. THIS LINE IS THE PROOF, and if it ever changes, correct "
                  + "the constants in MapRoomBenches from the numbers it prints."
                : $"{found} non-mod renderer(s) stand within {radius / scale:F1} m of the map and "
                  + $"below its top plane ({named} named). READ THESE: if any of them is the game's "
                  + "own table or bench, its size and offset above are the arrangement the user "
                  + "asked to copy, and this file's constants should be corrected to them."
                  + sb;
        }
        catch (System.Exception ex)
        {
            return $"the survey threw and was skipped ({ex.GetType().Name}: {ex.Message}) — the "
                   + "benches are unaffected; only this diagnostic is missing.";
        }
    }
}
