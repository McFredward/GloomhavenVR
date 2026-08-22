using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP TABLE'S LEGS — four legs, one at each corner of the table the campaign map lies on,
/// built PROCEDURALLY at runtime as one mesh, standing on the floor of the bundled 3D environment
/// and present ONLY in the two bundled 3D environments.
///
/// <para>USER, verbatim (translated), against ModBuild 197: "There really are no benches, I must
/// have dreamt that. But I don't like your benches — remove them again and close the topic for now.
/// Instead I want the TABLE to get TABLE LEGS at its 4 CORNERS, and these should STAND ON THE FLOOR
/// of the environment! The texture should be the SAME as the table's. The table legs should only be
/// visible in the two 3D environments — in mixed reality, in NO environment, or in the DEFAULT
/// environment they should not be there." Every clause of that sentence is a constraint below, and
/// each one is answered by a MEASUREMENT rather than by an assumption.</para>
///
/// <para>THE TABLE IS A REAL GAME OBJECT, AND THAT CORRECTS TWO SHIPPED COMMENTS. Until ModBuild 197
/// this room modelled "the table" as the parchment's world AABB widened by a 0.20 m rim, because the
/// <c>MAP SCENE REPORT</c> prints <c>TOTAL 0 renderer(s)</c> under the map roots besides the
/// parchment. That census is scoped to the map ROOTS. The bench class's one-shot neighbour survey —
/// built precisely to falsify the claim — swept the whole scene instead and found the table
/// standing right there, a sibling rather than a child:</para>
/// <code>
///   'GH_Map_TableTop_Lg' L0 size (306.88, 29.29, 454.81) = 1.55 x 0.15 x 2.30 m,
///                        centre offset (0.00, -0.08, 0.00) m from the map's centre/top
/// </code>
/// <para>A 1.55 x 2.30 m wooden slab 0.15 m thick whose TOP FACE sits 5 mm under the parchment: that
/// is the tabletop the player sees, it belongs to the GAME (layer 0, excluded from nothing, drawn by
/// the same forward head camera that draws everything else here), and it has no legs. So the legs
/// are placed at the corners of THAT renderer's world bounds and are skinned with THAT renderer's own
/// sharedMaterial — literally the same material object, so "the same texture as the table" is an
/// identity and not a match. <see cref="TryFindTable"/> re-derives it by measurement every time this
/// builds and never by name, and <see cref="DescribeCandidates"/> prints what it considered.</para>
///
/// <para>THEY STAND ON THE ROOM'S FLOOR, NOT ON THE PLAYER'S. Those are two different planes and the
/// gap between them is why the bench class buried its feet. The room is placed by the diorama rule
/// (<c>SkyAlternative.TryPlaceRoom</c>: floor = board underside − FloatGap) and the seat by
/// <c>MapRoomSeat.TableTopHeightMeters</c>, and the captured session prints the disagreement:
/// "floor dropped ... to y -178.45; the player's real floor is y -154.55, i.e. 23.90 world units =
/// perceived 0.12 m above the room floor". A bench mostly hides its own feet, so 197 cut them 0.20 m
/// below the PLAYER'S floor and called it done. A table leg's whole job is to be seen reaching the
/// ground, so that trade is not available here: the legs are stood on the ROOM's plane, read from
/// the room root's own transform, which <c>BuildEnvironmentRooms</c> authors the floor at
/// ("Both rooms have CLOSED opaque floors around the origin ... floor at y=0") and which
/// <c>TryPlaceRoom</c> sets to <c>floorY</c> and then never writes again ("WORLD-FIXED from now on").
/// <see cref="FootSinkMeters"/> is 25 mm and covers the terrain RELIEF at the table's corners, not a
/// disagreement between two planes — see its own doc for the arithmetic.</para>
///
/// <para>THE STYLE GATE IS LIVE, AND IT IS EVALUATED EVERY FRAME. <see cref="Tick"/> reads
/// <c>SkyAlternative.Style</c> and <c>MixedReality.BackingsWanted</c> on every call and builds or
/// tears down on the next frame, so changing the environment in the VR options panel takes effect
/// where the player is standing rather than at the next room entry. Present for
/// <see cref="SkyStyle.Cellar"/> and <see cref="SkyStyle.SwampNight"/> — the two bundled 3D rooms,
/// and the only two styles that HAVE a floor to stand on — and absent for
/// <see cref="SkyStyle.Default"/>, <see cref="SkyStyle.OffBlack"/> and under mixed reality. The
/// MR clause is not merely obedience to the ruling: a passthrough world has the player's REAL floor
/// in it, several metres from wherever this mod thinks the room floor is, so a leg drawn to a
/// virtual floor plane would visibly miss the real one. <c>MixedReality.BackingsWanted</c> is the
/// same predicate the MR readability treatment keys off, so there is no second switch to drift.</para>
///
/// <para>IT MOVES NOTHING. This class only READS: the table renderer's bounds and material, the
/// environment room root's position, the parchment's bounds and the solved seat's scale. It writes
/// no game transform, no game material, no rig value, and it has no Update of its own — it is
/// world-fixed furniture, so after the build frame <see cref="Tick"/> is two field reads and a
/// reference compare. No collider, deliberately: the laser's pick path must not start finding
/// furniture.</para>
///
/// <para>MULTIPLAYER: nothing on the wire and nothing to disagree about. Every input is game-scene
/// state plus this client's own local style dial, so two clients build byte-identical legs with zero
/// packets — and a client on a different environment simply has none, which is a presentation
/// difference exactly like the environment itself already is.</para>
///
/// <para>COST: one combined mesh, <see cref="TriangleCount"/> triangles, ONE MeshRenderer with ONE
/// material = ONE draw call, built once, no per-frame allocation and no shadow pass.</para>
/// </summary>
internal sealed class MapTableLegs
{
    private const string Scope = "MapRoom";

    // ---- THE LEG, IN REAL METRES -------------------------------------------------------------
    // Everything here is multiplied by the rig scale EXACTLY ONCE, in Build, and never again. This
    // room runs at ~198 world units per metre and mixing the two has shipped as a bug here before.

    /// <summary>Side of the leg's square section, real metres. AUTHORED. The tabletop measures
    /// 1.55 x 2.30 m and is 0.15 m thick — a heavy board — so a 10 cm post is what carries it; a
    /// thinner leg reads as a folding table and a thicker one as a pillar.</summary>
    internal const float LegSideMeters = 0.10f;

    /// <summary>How far the leg's OUTER face stands in from the tabletop's edge, real metres.
    /// AUTHORED. Small and non-zero: flush would z-fight with the top's own side face at grazing
    /// angles, and a large inset reads as a pedestal rather than as a corner leg.</summary>
    internal const float EdgeInsetMeters = 0.02f;

    /// <summary>How far the leg's top pushes UP into the tabletop, real metres. Parts that share a
    /// face exactly z-fight along it; 5 mm of overlap is invisible at any distance a player can
    /// reach at a table and guarantees no daylight between leg and top.</summary>
    internal const float WeldMeters = 0.005f;

    /// <summary>
    /// How far below the room's floor PLANE each foot is cut, real metres — and this number is 25 mm
    /// rather than the bench class's 200 mm because it is covering something much smaller.
    ///
    /// <para>The plane itself is exact (see the class doc): the room root's transform Y is the floor.
    /// What is NOT exact is the floor ART at the table's corners, because the bake gives both rooms a
    /// gentle relief outside their dead-flat play disc. Read off <c>BuildEnvironmentRooms</c>:
    /// <c>ForestY</c> is identically zero inside r = 1.7 authored m and ramps in over 1.7..4.6 m;
    /// the table's corner radius is 2.31 authored m (the corner is 1.39 perceived m out and the swamp
    /// room stands at 118.87 world units per authored metre against a 198.12 rig scale), where the
    /// ramp is only 0.114 of full strength, giving a relief of about +36/−31 mm authored = about
    /// ±19 mm perceived. <c>CellarFloorY</c> is <c>0.012·Fbm − 0.006</c>, i.e. ±6 mm authored =
    /// about ±5 mm perceived. 25 mm therefore swallows the worst bump in either room.</para>
    ///
    /// <para>THE ASYMMETRY IS THE POINT, and it is the opposite of the bench's: a foot sunk 25 mm
    /// into the ground is a table standing on soft floor, while a foot floating 19 mm above it is a
    /// table hovering. So the error is spent downward, and it is spent in millimetres rather than in
    /// the bench's 20 cm, because these legs are meant to be looked at.</para>
    /// </summary>
    internal const float FootSinkMeters = 0.025f;

    /// <summary>Bounds on the derived leg height, real metres. Shorter than this and the table is
    /// sitting on the floor; taller and something measured the wrong plane. Either way the numbers
    /// are wrong, and a refusal with a log line is worth more than geometry stretching to the
    /// horizon.</summary>
    internal const float MinLegHeightMeters = 0.15f;

    /// <inheritdoc cref="MinLegHeightMeters"/>
    internal const float MaxLegHeightMeters = 1.60f;

    /// <summary>Fallback real metres per UV unit, used only when the tabletop's own texel scale
    /// cannot be measured (unreadable mesh, no UV0). 1.0 m per UV unit is an ordinary wood tiling.
    /// </summary>
    internal const float FallbackMetresPerUv = 1.0f;

    /// <summary>Bounds on the MEASURED metres-per-UV-unit before it is trusted. A tabletop whose UVs
    /// imply a 5 cm or a 20 m texture repeat has a mapping this class cannot reason about; clamping
    /// keeps a strange asset as slightly wrong grain rather than as a single stretched texel.</summary>
    private const float MinMetresPerUv = 0.05f;

    /// <inheritdoc cref="MinMetresPerUv"/>
    private const float MaxMetresPerUv = 20f;

    /// <summary>Fraction trimmed off each side of the table's measured UV rectangle before the legs
    /// are mapped into it. The tabletop's texture may be one region of a shared atlas; staying off
    /// its own border is what keeps a leg from sampling the neighbouring page.</summary>
    private const float UvRectInset = 0.15f;

    /// <summary>A candidate tabletop must be a SLAB: its thickness at most this fraction of its
    /// smaller horizontal extent. This is what rejects the map's fog volume, which is as tall as it
    /// is wide.</summary>
    private const float SlabThicknessFactor = 0.30f;

    /// <summary>A candidate tabletop's TOP face must lie within this many real metres of the
    /// parchment's own top plane — the map lies ON the table, so the two surfaces are flush to
    /// within the map's own thickness. (Measured in the captured session: 6 mm.)</summary>
    private const float TopBandMeters = 0.12f;

    /// <summary>How much larger than the parchment a candidate tabletop may be before it is judged
    /// to be a ground plane rather than a table.</summary>
    private const float MaxTableFactor = 4f;

    /// <summary>Slack allowed when testing that a candidate CONTAINS the parchment, real metres.</summary>
    private const float ContainMarginMeters = 0.02f;

    /// <summary>Frames between attempts while the legs are wanted but something they need (the
    /// table, the room) is not there yet. The scan is a scene sweep, so it is throttled; once the
    /// legs stand, nothing scans again.</summary>
    private const int RetryIntervalFrames = 12;

    /// <summary>How many rejected candidates the survey names before it stops.</summary>
    private const int CandidateCap = 6;

    /// <summary>One leg at each corner of the tabletop.</summary>
    internal const int LegCount = 4;

    /// <summary>Triangles in the finished prop: 4 legs x 6 quads x 2.</summary>
    internal const int TriangleCount = LegCount * 12;

    /// <summary>
    /// The two BUNDLED 3D environments — the only styles that put a room with a floor around the
    /// player, and (per the user's ruling) the only ones the legs appear in. <c>Default</c> keeps the
    /// game's own sky and has no floor at all; <c>OffBlack</c> is deliberately "no environment"; and
    /// mixed reality is handled by its own clause in <see cref="Tick"/> because it OVERRIDES the
    /// style dial (<c>MixedReality.Tick</c>: "MR ON ⇒ the sky is ALWAYS off ... whatever [Sky] Style
    /// says").
    /// </summary>
    internal static bool StyleShowsLegs(SkyStyle style) =>
        style == SkyStyle.Cellar || style == SkyStyle.SwampNight;

    // ---- state -------------------------------------------------------------------------------

    private GameObject? _root;
    private Mesh? _mesh;
    private Material? _material;
    private bool _ownsMaterial;
    private MeshRenderer? _builtAgainstParchment;
    private MeshRenderer? _builtAgainstTable;
    private readonly List<Vector3> _verts = new(128);
    private readonly List<Vector3> _norms = new(128);
    private readonly List<Vector2> _uvs = new(128);
    private readonly List<int> _tris = new(192);
    private Rect _uvRect = new(0f, 0f, 1f, 1f);
    private Vector2 _uvCentre = new(0.5f, 0.5f);
    private float _uvWorldPerU = 1f;
    private float _uvWorldPerV = 1f;
    private int _retryFrame = int.MinValue;
    private SkyStyle _lastStyle = (SkyStyle)(-1);
    private bool _lastMixedReality;
    private bool _lastStanding;
    private bool _gateLogged;
    private string _lastRefusal = "";

    /// <summary>
    /// Per-frame upkeep while the map room stands. THE GATE IS EVALUATED HERE, EVERY FRAME — two
    /// field reads — so a style change in the VR options panel builds or tears the legs down on the
    /// next frame rather than on the next room entry. When the answer has not changed and the prop
    /// already stands, this is a reference compare and nothing else.
    /// </summary>
    internal void Tick()
    {
        SkyStyle style = SkyAlternative.Style != null ? SkyAlternative.Style.Value : SkyStyle.Default;
        bool mixedReality = MixedReality.BackingsWanted;
        bool wanted = !mixedReality && StyleShowsLegs(style);

        if (style != _lastStyle || mixedReality != _lastMixedReality)
        {
            _lastStyle = style;
            _lastMixedReality = mixedReality;
            _gateLogged = false;
            _lastRefusal = "";
            _retryFrame = int.MinValue;
        }

        if (!wanted)
        {
            if (_root != null)
            {
                Release($"the style gate closed — {DescribeGate(style, mixedReality, false)}");
                // Release's own line already carries the gate verdict; the tail below would only
                // repeat it.
                _gateLogged = true;
                _lastStanding = false;
            }
        }
        else if (_root != null)
        {
            // THE ONE THING THAT CAN INVALIDATE A STANDING PROP: a world<->city switch re-finds the
            // parchment, and a different map could stand on a different table. Cheap reference
            // compares (Unity's == also catches a destroyed renderer), no allocation, no scan.
            if (MapRoomDriver.ParchmentRenderer != _builtAgainstParchment || _builtAgainstTable == null)
            {
                Release("the map's parchment or its table changed underneath the prop "
                        + "(world<->city switch) — it rebuilds against the new one");
                _gateLogged = true;
                _lastStanding = false;
            }
        }
        else if (_retryFrame == int.MinValue || Time.frameCount - _retryFrame >= RetryIntervalFrames)
        {
            _retryFrame = Time.frameCount;
            // GUARDED, because this is the one path that touches the GAME's objects — it sweeps the
            // scene for the tabletop and reads its mesh. MapRoomDriver.TickActive runs inside
            // VRRigDriver's per-frame body, and an exception escaping into that starves the player's
            // input for as long as it keeps throwing (WorldUI's oldest recorded defect). A throw here
            // must cost the legs and nothing else, and must not leave half a prop standing.
            try
            {
                Build(style, mixedReality);
            }
            catch (System.Exception ex)
            {
                Release($"the build threw ({ex.GetType().Name})");
                Refuse($"NOT BUILT: the build threw — {ex.GetType().Name}: {ex.Message}. Nothing else "
                       + "in the map room is affected; the legs simply do not exist this session "
                       + "unless the cause clears. THE STACK IS IN Player.log (ModBuild 136+ restores "
                       + "stack traces process-wide), so read it rather than theorising.");
            }
        }

        bool standing = _root != null;
        if (_gateLogged && standing == _lastStanding)
            return;
        _gateLogged = true;
        _lastStanding = standing;
        if (!standing)
            VRLog.Info(Scope, $"MAP TABLE LEGS: {DescribeGate(style, mixedReality, false)}"
                              + (_lastRefusal.Length > 0 ? $" {_lastRefusal}" : ""));
    }

    /// <summary>Tear the legs down. Idempotent; the only exit.</summary>
    internal void Release(string reason)
    {
        bool had = _root != null;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        if (_mesh != null)
            Object.Destroy(_mesh);
        _mesh = null;
        // ONLY a material this class MADE is destroyed. In the normal case _material IS the game
        // tabletop's own sharedMaterial — destroying that would take the table's surface with it,
        // and every other renderer sharing it. This flag is the whole guard.
        if (_ownsMaterial && _material != null)
            Object.Destroy(_material);
        _material = null;
        _ownsMaterial = false;
        _builtAgainstParchment = null;
        _builtAgainstTable = null;
        _retryFrame = int.MinValue;
        if (had)
        {
            VRLog.Info(Scope, $"MAP TABLE LEGS released ({reason}) — the prop and its mesh are "
                              + "destroyed. The game's tabletop, its material and the environment "
                              + "room are untouched: this class only ever READ them.");
        }
    }

    /// <summary>
    /// Record why a build attempt declined, and arm ONE log line for it — but only when the reason
    /// has actually changed. The build retries on <see cref="RetryIntervalFrames"/> while the legs
    /// are wanted and something they need has not arrived, so an unconditional line here would put
    /// six identical paragraphs a second into the log for as long as the condition lasts. The text
    /// is composed from measured values, so "the same reason" really does mean the same reason.
    /// </summary>
    private void Refuse(string reason)
    {
        if (_lastRefusal == reason)
            return;
        _lastRefusal = reason;
        _gateLogged = false;
    }

    /// <summary>Why the legs are or are not wanted, in one clause. Composed only when the answer
    /// changes, never per frame.</summary>
    private static string DescribeGate(SkyStyle style, bool mixedReality, bool standing)
    {
        if (mixedReality)
            return "MIXED REALITY is on, so NO legs — a passthrough world already has the player's "
                   + "real floor in it and a leg drawn to a virtual floor plane would visibly miss "
                   + $"it. ([Sky] Style is {style}, and MR overrides it either way.)";
        if (!StyleShowsLegs(style))
            return $"[Sky] Style is {style}, so NO legs — the user's ruling is the two BUNDLED 3D "
                   + "environments only (Cellar, SwampNight). Default keeps the game's own sky and "
                   + "OffBlack is deliberately no environment; neither has a floor to stand on.";
        return $"[Sky] Style is {style} — one of the two bundled 3D rooms, so the legs are WANTED"
               + (standing ? " and they stand." : ", but they are not standing yet.");
    }

    // ---- build -------------------------------------------------------------------------------

    private void Build(SkyStyle style, bool mixedReality)
    {
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return;
        if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _))
            return;
        float scale = Mathf.Max(seat.Scale, 0.0001f);
        Bounds parch = parchment.bounds;
        if (Mathf.Max(Mathf.Abs(parch.size.x), Mathf.Abs(parch.size.z)) < MapRoomSeat.MinUsableExtent)
            return;

        if (!TryFindTable(parch, scale, out MeshRenderer? table, out string candidates) || table == null)
        {
            Refuse("NOT BUILT: no tabletop was found. " + candidates);
            return;
        }
        if (!TryFindRoomFloor(style, out float floorY, out string floorSource))
        {
            Refuse("NOT BUILT: " + floorSource);
            return;
        }

        Bounds top = table.bounds;
        float footY = floorY - FootSinkMeters * scale;
        float legTopY = top.min.y + WeldMeters * scale;
        float legHeight = legTopY - footY;
        if (legHeight < MinLegHeightMeters * scale || legHeight > MaxLegHeightMeters * scale)
        {
            Refuse($"NOT BUILT: the derived leg height is {legHeight / scale:F3} m "
                   + $"({legHeight:F1} world units), outside the plausible "
                   + $"{MinLegHeightMeters:F2}..{MaxLegHeightMeters:F2} m window — the tabletop's "
                   + $"underside is y={top.min.y:F2} and the room floor is y={floorY:F2} "
                   + $"({floorSource}). One of those two planes is not what this class thinks it "
                   + "is; nothing is built rather than a wrong prop.");
            return;
        }

        // THE FRAME. Origin at the TABLETOP's horizontal centre and at the ROOM FLOOR vertically,
        // unrotated. The table's own AABB is the frame the legs belong to — they are its legs — and
        // anchoring the vertical to the floor is what makes "standing on the floor" structural
        // rather than arithmetic that can drift.
        var origin = new Vector3(top.center.x, footY, top.center.z);
        _root = new GameObject("GloomhavenVR.MapTableLegs");
        _root.transform.SetPositionAndRotation(origin, Quaternion.identity);

        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _tris.Clear();
        AdoptTableUvs(table, top, scale, out string uvSource);

        float side = LegSideMeters * scale;
        float inset = (LegSideMeters * 0.5f + EdgeInsetMeters) * scale;
        // Guard a table so small the insets cross: the legs then sit on the centre line rather than
        // outside the top, which is ugly but bounded.
        float cornerX = Mathf.Max(Mathf.Abs(top.size.x) * 0.5f - inset, side * 0.5f);
        float cornerZ = Mathf.Max(Mathf.Abs(top.size.z) * 0.5f - inset, side * 0.5f);

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var centre = new Vector3(cornerX * sx, legHeight * 0.5f, cornerZ * sz);
                Box(centre, new Vector3(side, legHeight, side));
            }
        }

        _mesh = new Mesh { name = "GloomhavenVR.MapTableLegs" };
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_norms);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_tris, 0, calculateBounds: true);
        // The tabletop's material is the game's own and may well sample a normal map through a
        // tangent frame; a mesh without tangents lights by an undefined basis. Recalculated once,
        // here, never per frame.
        _mesh.RecalculateTangents();

        var geo = new GameObject("Geo");
        geo.transform.SetParent(_root.transform, worldPositionStays: false);
        geo.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = geo.AddComponent<MeshRenderer>();
        _material = table.sharedMaterial;
        _ownsMaterial = false;
        string materialSource;
        if (_material != null)
        {
            materialSource = $"the tabletop's OWN sharedMaterial '{_material.name}' "
                             + $"(shader '{(_material.shader != null ? _material.shader.name : "<null>")}', "
                             + $"{table.sharedMaterials.Length} submesh material(s) on it) — the SAME "
                             + "material object, not a copy and not a match, so the legs cannot drift "
                             + "from the table's look and cost no extra material";
        }
        else
        {
            _material = WoodFallbackMaterial();
            _ownsMaterial = true;
            materialSource = "FALLBACK wood (GloomhavenVR/BoardLit over the button-rail cap colour) — "
                             + "the tabletop renderer carries NO sharedMaterial, which should be "
                             + "impossible for a visible slab. The legs will NOT match the table; "
                             + "read this line before tuning anything else.";
        }
        mr.sharedMaterial = _material;
        // Scenery. The map room has no shadow-casting light of its own, so a shadow pass would be a
        // second pass over 48 triangles for nothing.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        // NO COLLIDER, deliberately: the laser's pick path must not start finding furniture.

        // The head camera's mask is the game map camera's mask OR'd with the mod layer, so this is
        // what makes the prop visible at all (MapRoomDriver.ResolveMapMask).
        VRLayers.Apply(_root);

        _builtAgainstParchment = parchment;
        _builtAgainstTable = table;
        _lastRefusal = "";
        Report(style, mixedReality, seat, parch, table, top, floorY, footY, legHeight, side,
               cornerX, cornerZ, scale, floorSource, materialSource, uvSource, candidates);
    }

    /// <summary>
    /// The fallback skin, used ONLY when the tabletop renderer has no material at all — the same
    /// wood the table's button caps are made of. <c>GloomhavenVR/BoardLit</c> is baked-lit (it has an
    /// ambient floor and its own studio key), which is why it is the fallback: the map room has no
    /// guaranteed lighting and an ordinary lit shader renders black in it.
    /// </summary>
    private static Material WoodFallbackMaterial()
    {
        const float woodBoost = 1.9f;
        Shader? lit = Cards.PlayTray.BoardLitShader();
        return lit != null
            ? Cards.PlayTray.NewKeycapMaterial(lit, ButtonTuning.CapWellColor * woodBoost)
            : WorldUIAssets.CreateFlatMaterial(ButtonTuning.CapWellColor * woodBoost);
    }

    // ---- finding the table ---------------------------------------------------------------------

    /// <summary>
    /// WHICH RENDERER IS THE TABLETOP — by MEASUREMENT, never by name, so a renamed or a
    /// per-map-variant asset still resolves and a coincidence cannot. A candidate qualifies when all
    /// four hold, and each one is there to reject something the captured scene actually contains:
    /// <list type="number">
    ///   <item>it is the game's (not on the mod layer) and is not the parchment itself;</item>
    ///   <item>its horizontal footprint CONTAINS the parchment's — the map lies on the table, so the
    ///   table is at least as big. (This is what rejects <c>FogTarget</c>, which is wider than the
    ///   map in X but narrower in Z.)</item>
    ///   <item>it is a SLAB: thickness at most <see cref="SlabThicknessFactor"/> of its smaller
    ///   horizontal extent, and no more than <see cref="MaxTableFactor"/> times the map across. (The
    ///   first rejects <c>FogTarget</c> a second time — it is as tall as it is wide — the second
    ///   would reject a ground plane.)</item>
    ///   <item>its TOP FACE is flush with the parchment's top plane to within
    ///   <see cref="TopBandMeters"/>. A table under the map, not a floor under the table.</item>
    /// </list>
    /// Among survivors the SMALLEST horizontal footprint wins, so a tabletop nested inside a larger
    /// platform is preferred to the platform.
    ///
    /// <para>COST: one <c>FindObjectsOfType</c>, on the build frame only, and never again while the
    /// legs stand — the same order of cost as the map scene report the room already emits on entry,
    /// and deliberately not the per-frame kind.</para>
    /// </summary>
    internal static bool TryFindTable(Bounds parchment, float scale, out MeshRenderer? table,
                                      out string survey)
    {
        table = null;
        var sb = new StringBuilder(256);
        int considered = 0, named = 0;
        float best = float.MaxValue;
        try
        {
            int mod = VRLayers.ModLayer;
            float margin = ContainMarginMeters * scale;
            float band = TopBandMeters * scale;
            float parchWidest = Mathf.Max(Mathf.Abs(parchment.size.x), Mathf.Abs(parchment.size.z));
            MeshRenderer[] all = Object.FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer r = all[i];
                if (r == null || r.gameObject.layer == mod)
                    continue;
                Bounds b = r.bounds;
                float sizeX = Mathf.Abs(b.size.x), sizeZ = Mathf.Abs(b.size.z);
                float thick = Mathf.Abs(b.size.y);
                bool contains = b.min.x <= parchment.min.x + margin && b.max.x >= parchment.max.x - margin
                             && b.min.z <= parchment.min.z + margin && b.max.z >= parchment.max.z - margin;
                if (!contains)
                    continue;
                considered++;
                bool slab = thick <= SlabThicknessFactor * Mathf.Min(sizeX, sizeZ);
                bool sized = Mathf.Max(sizeX, sizeZ) <= MaxTableFactor * parchWidest;
                bool flush = Mathf.Abs(b.max.y - parchment.max.y) <= band;
                if (slab && sized && flush)
                {
                    float area = sizeX * sizeZ;
                    if (area < best)
                    {
                        best = area;
                        table = r;
                    }
                    continue;
                }
                if (named < CandidateCap)
                {
                    named++;
                    sb.Append($"\n              REJECTED '{r.name}' L{r.gameObject.layer} "
                              + $"{sizeX / scale:F2} x {thick / scale:F2} x {sizeZ / scale:F2} m, top "
                              + $"{(b.max.y - parchment.max.y) / scale:F3} m from the map's plane — "
                              + $"{(slab ? "" : "not a slab; ")}{(sized ? "" : "too big for a table; ")}"
                              + $"{(flush ? "" : "top not flush with the map")}");
                }
            }
        }
        catch (System.Exception ex)
        {
            survey = $"the tabletop sweep threw ({ex.GetType().Name}: {ex.Message}).";
            return false;
        }

        if (table != null)
        {
            Bounds b = table.bounds;
            survey = $"MEASURED, not named: '{table.name}' on layer {table.gameObject.layer} is the "
                     + $"only/smallest non-mod SLAB that contains the map's footprint and whose top "
                     + $"face is flush with the map's own plane — "
                     + $"{Mathf.Abs(b.size.x) / scale:F2} x {Mathf.Abs(b.size.y) / scale:F2} x "
                     + $"{Mathf.Abs(b.size.z) / scale:F2} m, top "
                     + $"{(b.max.y - parchment.max.y) / scale * 1000f:F0} mm from the map's plane. "
                     + $"{considered} renderer(s) contained the map and were tested.{sb}";
            return true;
        }
        survey = $"{considered} non-mod renderer(s) contain the map's footprint and NONE of them is a "
                 + "slab flush with the map's own top plane. Without the tabletop this class has "
                 + "neither the corners to stand legs at nor the material to skin them with, so it "
                 + "builds NOTHING — a leg placed against a guessed footprint is worse than no leg. "
                 + "If the game's map screen really has changed, the rejections below are the "
                 + $"numbers to correct this file's thresholds from.{sb}";
        return false;
    }

    /// <summary>
    /// THE ENVIRONMENT ROOM'S FLOOR PLANE, exactly — the room root's own world Y.
    ///
    /// <para>That value is not an estimate. <c>SkyAlternative.TryPlaceRoom</c> computes
    /// <c>floorY = boardUndersideY − FloatGap</c> and writes it straight into the room's transform
    /// position, then never writes the transform again ("WORLD-FIXED from now on: no per-frame
    /// writes, no rig-scale tracking, no re-seat of any kind"); and the bake authors both rooms with
    /// their floor at local y = 0 ("Both rooms have CLOSED opaque floors around the origin ...
    /// floor at y=0"). So the room root's Y IS the plane, to the bit.</para>
    ///
    /// <para>IT IS FOUND BY NAME, AND THAT IS A COMPROMISE WORTH NAMING. <c>SkyAlternative</c> keeps
    /// its room GameObject private and exposes no floor accessor, and this lane does not own that
    /// file — so the object is looked up by the name that file gives it,
    /// <c>"GloomhavenVR.SkyAlternative.Room." + style</c>. The cost of a rename there is that the
    /// legs stop being built and say so in this log rather than standing in the wrong place. A
    /// two-line <c>internal static bool TryRoomFloorY(out float)</c> on <c>SkyAlternative</c> would
    /// retire the lookup; it is flagged in this round's report.</para>
    /// </summary>
    internal static bool TryFindRoomFloor(SkyStyle style, out float floorY, out string source)
    {
        floorY = 0f;
        string name = "GloomhavenVR.SkyAlternative.Room." + style;
        GameObject? room = GameObject.Find(name);
        if (room == null)
        {
            source = $"the environment room '{name}' is not in the scene (or not active) yet, so "
                     + "there is no floor plane to stand the legs on. The style says it should be "
                     + "there, so this is almost certainly the frame or two before "
                     + "SkyAlternative.TryPlaceRoom lands it; the build retries. If it persists, the "
                     + "room is refusing to place (its own log line says why) or that class renamed "
                     + "its root.";
            return false;
        }
        floorY = room.transform.position.y;
        source = $"the room root '{name}' transform.position.y = {floorY:F2} — SkyAlternative writes "
                 + "the computed floor plane straight into it and never writes it again, and the "
                 + "bake authors both rooms with their floor at local y=0, so this IS the plane";
        return true;
    }

    /// <summary>
    /// TAKE THE TABLE'S TEXTURE COORDINATES, NOT JUST ITS TEXTURE. Sharing the material makes the
    /// legs the same WOOD; it does not make them the same wood at the same SIZE, and a leg whose
    /// grain is twice as fine as the top it holds up reads as a different piece of furniture. So two
    /// things are measured off the tabletop's own mesh and both are used:
    /// <list type="bullet">
    ///   <item>ITS TEXEL SCALE — how many real metres of table one UV unit covers, from the mesh's
    ///   UV span against the renderer's world size. The legs are then mapped at exactly that many
    ///   metres per UV unit, so the grain is the same size on the leg as on the top.</item>
    ///   <item>ITS UV WINDOW — the rectangle of UV space the top actually occupies, trimmed by
    ///   <see cref="UvRectInset"/> a side. Every leg vertex is CLAMPED into it, which is what makes
    ///   "the same texture" survive the possibility that the top is one page of a shared atlas:
    ///   whatever region of the sheet the table's wood lives in, no leg sample can leave it.</item>
    /// </list>
    /// Falls back to the whole 0..1 sheet at <see cref="FallbackMetresPerUv"/> — the ordinary case
    /// for a dedicated texture — when the mesh is unreadable or carries no UVs, and says so.
    /// </summary>
    private void AdoptTableUvs(MeshRenderer table, Bounds top, float scale, out string source)
    {
        _uvRect = new Rect(0f, 0f, 1f, 1f);
        _uvCentre = new Vector2(0.5f, 0.5f);
        _uvWorldPerU = _uvWorldPerV = FallbackMetresPerUv * scale;
        try
        {
            var mf = table.GetComponent<MeshFilter>();
            Mesh? mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                source = $"the tabletop's mesh is {(mesh == null ? "absent" : "not CPU-readable")}, so "
                         + $"the legs are mapped over the FULL 0..1 sheet at {FallbackMetresPerUv:F2} m "
                         + "per UV unit. Correct if the wood turns out to be one page of an atlas (the "
                         + "legs would show a neighbouring page) or a different grain size from the top";
                return;
            }
            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length == 0)
            {
                source = "the tabletop's mesh carries no UV0 at all, so the legs are mapped over the "
                         + $"full 0..1 sheet at {FallbackMetresPerUv:F2} m per UV unit (whatever the "
                         + "table's shader does with texture coordinates, it is not reading them)";
                return;
            }
            int stride = Mathf.Max(1, uv.Length / 4096);
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;
            int sampled = 0;
            for (int i = 0; i < uv.Length; i += stride)
            {
                Vector2 t = uv[i];
                if (float.IsNaN(t.x) || float.IsNaN(t.y))
                    continue;
                sampled++;
                if (t.x < minU) minU = t.x;
                if (t.x > maxU) maxU = t.x;
                if (t.y < minV) minV = t.y;
                if (t.y > maxV) maxV = t.y;
            }
            if (sampled == 0 || !(maxU > minU) || !(maxV > minV))
            {
                source = $"the tabletop's {uv.Length} UV(s) are degenerate, so the legs are mapped over "
                         + $"the full 0..1 sheet at {FallbackMetresPerUv:F2} m per UV unit";
                return;
            }

            // THE TEXEL SCALE. The top is a slab, so its two horizontal extents are what its UV
            // rectangle is stretched across; which extent went to U and which to V is unknowable
            // without walking the triangles, and it does not matter for a SIZE — the average of the
            // two is right to within the slab's own aspect and the clamp catches anything absurd.
            float uSpan = maxU - minU, vSpan = maxV - minV;
            float mU = Mathf.Clamp(Mathf.Abs(top.size.x) / scale / uSpan, MinMetresPerUv, MaxMetresPerUv);
            float mV = Mathf.Clamp(Mathf.Abs(top.size.z) / scale / vSpan, MinMetresPerUv, MaxMetresPerUv);
            float metresPerUv = 0.5f * (mU + mV);
            _uvWorldPerU = _uvWorldPerV = metresPerUv * scale;

            float insetU = uSpan * UvRectInset, insetV = vSpan * UvRectInset;
            _uvRect = Rect.MinMaxRect(minU + insetU, minV + insetV, maxU - insetU, maxV - insetV);
            _uvCentre = _uvRect.center;
            source = $"MEASURED off the tabletop's own mesh '{mesh.name}' ({uv.Length} UV(s), "
                     + $"{sampled} sampled): they span ({minU:F3}..{maxU:F3}, {minV:F3}..{maxV:F3}) "
                     + $"across a {Mathf.Abs(top.size.x) / scale:F2} x {Mathf.Abs(top.size.z) / scale:F2} m "
                     + $"top, i.e. {mU:F3} / {mV:F3} m per UV unit — the legs use the average "
                     + $"{metresPerUv:F3} m per UV unit, so their grain is the SAME SIZE as the top's, "
                     + $"and every leg vertex is clamped into the trimmed window "
                     + $"({_uvRect.xMin:F3}..{_uvRect.xMax:F3}, {_uvRect.yMin:F3}..{_uvRect.yMax:F3}) "
                     + $"so an atlas cannot leak a neighbouring page onto them";
        }
        catch (System.Exception ex)
        {
            source = $"reading the tabletop's UVs threw ({ex.GetType().Name}: {ex.Message}), so the "
                     + $"legs are mapped over the full 0..1 sheet at {FallbackMetresPerUv:F2} m per "
                     + "UV unit";
        }
    }

    // ---- geometry ----------------------------------------------------------------------------

    /// <summary>
    /// One closed axis-aligned box: six quads, 24 vertices, 12 triangles, hard edges (each face
    /// carries its own normals). <paramref name="size"/> is a full size per axis, in world units,
    /// and <paramref name="centre"/> doubles as the box's UV origin so each leg carries its own copy
    /// of the grain rather than sampling the same clamped column four times.
    /// </summary>
    private void Box(Vector3 centre, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3 hx = Vector3.right * h.x, hy = Vector3.up * h.y, hz = Vector3.forward * h.z;
        Face(centre + hx, Vector3.right, hz, hy, centre);
        Face(centre - hx, Vector3.left, hz, hy, centre);
        Face(centre + hy, Vector3.up, hx, hz, centre);
        Face(centre - hy, Vector3.down, hx, hz, centre);
        Face(centre + hz, Vector3.forward, hx, hy, centre);
        Face(centre - hz, Vector3.back, hx, hy, centre);
    }

    /// <summary>
    /// One quad, wound so that it faces <paramref name="outward"/>.
    ///
    /// <para>THE WINDING IS DERIVED, NOT HOPED FOR, and this project has earned that sentence: seven
    /// meshes have shipped wound against the side they are seen from and one was invisible for ten
    /// builds. For the corner order below, triangles (0,1,2) and (0,2,3) give a geometric normal of
    /// Cross(hu, hv) — the same convention Unity's own documented quad uses — so when that disagrees
    /// with the outward direction the V half-axis is NEGATED. <see cref="Report"/> then checks the
    /// finished solid both ways (positive signed volume, and every triangle agreeing with its own
    /// vertex normal) and says so in the log.</para>
    ///
    /// <para>AND THE UV AXES ARE CAPTURED BEFORE THAT FLIP — a small correction to the bench class
    /// this file replaces, which took them after it. Flipping V mirrors the texture on that one face;
    /// on a seamless tiling grain nobody could see it, but these legs are mapped into a FIXED window
    /// of the table's sheet, where a mirrored V walks the sample off the bottom of the window and
    /// clamps a whole face to one row of texels.</para>
    /// </summary>
    private void Face(Vector3 centre, Vector3 outward, Vector3 hu, Vector3 hv, Vector3 uvOrigin)
    {
        Vector3 uAxis = hu.normalized, vAxis = hv.normalized;
        if (Vector3.Dot(Vector3.Cross(hu, hv), outward) < 0f)
            hv = -hv;
        int b = _verts.Count;
        AddVert(centre - hu - hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre + hu - hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre + hu + hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre - hu + hv, outward, uAxis, vAxis, uvOrigin);
        _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
        _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
    }

    private void AddVert(Vector3 p, Vector3 n, Vector3 uAxis, Vector3 vAxis, Vector3 uvOrigin)
    {
        _verts.Add(p);
        _norms.Add(n);
        // uv from the vertex's own OFFSET FROM ITS LEG, at the table's own texel scale and clamped
        // into the table's own UV window. Position-derived, so a winding flip cannot move it;
        // per-leg origin, so all four legs carry the grain rather than one clamped column; window-
        // clamped, so no sample can leave the region the table's wood occupies.
        Vector3 d = p - uvOrigin;
        _uvs.Add(new Vector2(
            Mathf.Clamp(_uvCentre.x + Vector3.Dot(d, uAxis) / _uvWorldPerU, _uvRect.xMin, _uvRect.xMax),
            Mathf.Clamp(_uvCentre.y + Vector3.Dot(d, vAxis) / _uvWorldPerV, _uvRect.yMin, _uvRect.yMax)));
    }

    // ---- the report ---------------------------------------------------------------------------

    /// <summary>
    /// THE ONE TABLE-LEGS LINE. Everything a hardware round needs to decide whether this is right
    /// WITHOUT putting the headset on: the style gate and its decision, every dimension in BOTH real
    /// metres and world units, the triangle and draw-call counts, which renderer was identified as
    /// the table and which material was taken from it, which plane the feet stand on and the
    /// residual left over, and the mesh's own winding gate.
    /// </summary>
    private void Report(SkyStyle style, bool mixedReality, MapRoomSeat.Seat seat, Bounds parch,
                        MeshRenderer table, Bounds top, float floorY, float footY, float legHeight,
                        float side, float cornerX, float cornerZ, float scale, string floorSource,
                        string materialSource, string uvSource, string tableSurvey)
    {
        // THE WINDING GATE, ON THE FINISHED SOLID. Cheap (48 triangles) and it runs once.
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
        if (!woundRight)
            VRLog.Warn(Scope, $"MAP TABLE LEGS: the winding gate FAILED — signed volume "
                              + $"{volumeCubicMetres:F5} m^3 (must be positive) and {disagreeing} of "
                              + $"{_tris.Count / 3} triangle(s) disagree with their own vertex normal. "
                              + "The prop will be inside-out or partly invisible. This is the winding "
                              + "bug class this project has shipped seven times; read Face() before "
                              + "changing anything else.");

        // A SCALE CROSS-CHECK, not a new fact: re-derived from the world-space numbers this build
        // actually used, this must come back as MapRoomSeat.TableTopHeightMeters (0.78).
        float topAbovePlayerFloor = (parch.max.y - seat.FloorPosition.y) / scale;
        float playerAboveRoomFloor = (seat.FloorPosition.y - floorY) / scale;

        VRLog.Info(Scope,
            $"MAP TABLE LEGS built: {LegCount} leg(s), one at each CORNER of the game's own tabletop, "
            + $"{TriangleCount} triangles in ONE combined mesh on ONE MeshRenderer with ONE material "
            + "= 1 DRAW CALL, no collider, no Update, world-fixed (nothing here follows the head).\n"
            + $"  gate      : {DescribeGate(style, mixedReality, true)} The gate is re-evaluated EVERY "
            + "FRAME (two field reads), so switching [Sky] Style at runtime builds or tears these "
            + "down on the NEXT FRAME, not on the next room entry.\n"
            + $"  the table : {tableSurvey}\n"
            + $"  footprint : the tabletop measures {Mathf.Abs(top.size.x) / scale:F3} x "
            + $"{Mathf.Abs(top.size.z) / scale:F3} m ({Mathf.Abs(top.size.x):F1} x "
            + $"{Mathf.Abs(top.size.z):F1} world units), {Mathf.Abs(top.size.y) / scale:F3} m thick. "
            + $"The map on it is {Mathf.Abs(parch.size.x) / scale:F3} x "
            + $"{Mathf.Abs(parch.size.z) / scale:F3} m — so the table is genuinely bigger than the "
            + "parchment, which is why the legs are placed against IT and not against the "
            + "parchment-plus-rim model this room used before.\n"
            + $"  leg       : {LegSideMeters:F3} x {LegSideMeters:F3} m section x "
            + $"{legHeight / scale:F3} m tall ({side:F1} x {side:F1} x {legHeight:F1} world units), "
            + $"corners at +/-{cornerX / scale:F3} x +/-{cornerZ / scale:F3} m "
            + $"(+/-{cornerX:F1} x +/-{cornerZ:F1} world units) from the tabletop's centre, i.e. its "
            + $"outer face {EdgeInsetMeters * 1000f:F0} mm inside the top's edge. Its head is pushed "
            + $"{WeldMeters * 1000f:F0} mm up into the top so no daylight can show at the joint.\n"
            + $"  material  : {materialSource}.\n"
            + $"  texture   : {uvSource}. At that scale the {legHeight / scale:F2} m leg carries "
            + $"{legHeight / _uvWorldPerV:F2} UV unit(s) of grain down its length and "
            + $"{side / _uvWorldPerU:F2} across its face.\n"
            + $"  the floor : the feet are cut at y={footY:F2} — the room floor y={floorY:F2} minus "
            + $"{FootSinkMeters * 1000f:F0} mm ({FootSinkMeters * scale:F1} world units). SOURCE: "
            + $"{floorSource}. The player's own tracking floor is y={seat.FloorPosition.y:F2}, i.e. "
            + $"{playerAboveRoomFloor * 1000f:F0} mm ABOVE the room floor — the two planes this room "
            + "has always disagreed on, and the reason the legs are stood on the ROOM's one: they are "
            + "meant to be seen reaching the ground. RESIDUAL: the plane is exact, but both rooms "
            + "have a gentle floor relief outside their dead-flat play disc (BuildEnvironmentRooms: "
            + "ForestY is identically 0 inside r=1.7 authored m and ramps over 1.7..4.6; CellarFloorY "
            + $"is +/-6 mm authored), which at this table's corner radius is about +/-19 mm (swamp) "
            + $"and +/-5 mm (cellar) perceived. The {FootSinkMeters * 1000f:F0} mm sink swallows it "
            + "downward on purpose: a sunk foot reads as soft ground, a floating one reads as a bug.\n"
            + $"  heights   : tabletop top {(top.max.y - floorY) / scale:F3} m above the room floor "
            + $"and {(top.max.y - seat.FloorPosition.y) / scale:F3} m above the player's; parchment "
            + $"top {topAbovePlayerFloor:F3} m above the player's floor — CROSS-CHECK, that last one "
            + $"must read {MapRoomSeat.TableTopHeightMeters:F3}, and if it does not then the rig scale "
            + "used here and the one the seat was solved with disagree and every metre in this line "
            + $"is worth nothing. Rig scale {scale:F2} world units per real metre.\n"
            + $"  mesh      : {_verts.Count} verts, {_tris.Count / 3} tris, signed volume "
            + $"{volumeCubicMetres:F5} m^3, {disagreeing} triangle(s) disagreeing with their own "
            + $"normal — winding gate {(woundRight ? "PASSED" : "FAILED")}.\n"
            + "  the player: NOT MOVED and not moveable from here — this class reads the seat, the "
            + "table and the room and writes to none of them.\n"
            + "  DISPROOF  : if the legs look like doll furniture or like pillars, the metre column "
            + "above is wrong while the world-unit column looks fine — that is a rig-scale slip, not "
            + "an art problem. If they float or sink, compare 'the floor' line's two planes. If they "
            + "are the wrong wood, read 'material' — it names the exact material object taken off the "
            + "table. If they are inside-out or half-missing, the winding gate line says so. If they "
            + "appear where they should not, the 'gate' line says which style was read.\n"
            + $"  ONE OPEN RISK, stated rather than assumed away: the legs carry the tabletop's "
            + $"material but stand on the MOD layer ({VRLayers.ModLayer}) while the tabletop itself "
            + "is on layer 0, because every other prop this room builds goes through VRLayers.Apply "
            + "and that is what the head camera's mask is composed for. Same camera, same forward "
            + "pass, same material — so the SHADING recipe is identical — but a realtime light whose "
            + "own culling mask excludes the mod layer would light the two differently. If the next "
            + "photo shows legs of visibly the wrong BRIGHTNESS against a table of the right wood, "
            + "that is this and not the material; the fix is the layer, not the shader.");
    }
}
