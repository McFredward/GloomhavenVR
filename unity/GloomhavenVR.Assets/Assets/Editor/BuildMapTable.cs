// GloomhavenVR companion project — THE 3D WORLD MAP'S TABLE (.planning/worldmap-3d.md §5 Phase 2).
//
// USER, verbatim (2026-08): "Die world map steht auf einem Tisch mit Bänken an
// beinden Enden." — so: one table, a bench at EACH END, at a size real people
// stand and sit around, with the campaign map lying on the top.
//
// ======================================================================= WHY IT IS BUILT AND NOT DOWNLOADED
// The plan (§8) offered two routes: build the table procedurally out of the CC0
// `dark_wooden_planks` set that is already imported for the cellar's ceiling and
// timbers, or import Poly Haven's `wooden_picnic_table` (CC0, a photoscanned
// table with attached benches — geometrically the request, exactly).
//
// This is the procedural one, and the reason is the project's own standing art
// ruling: game-asset environments were abandoned by user ruling and the
// replacement is CUSTOM STYLE-MATCHING ASSETS. The two rooms this table has to
// stand in are a medieval stone cellar and a night swamp. A picnic table reads
// *rustic garden* — attached benches on a cross-brace, weathered softwood, a
// silhouette from a park — and it would be the only object in either room that
// came from somewhere else and looked it. Building it here also makes every
// dimension a tunable rather than a download, costs no new bundle bytes and no
// new licence work (NOTHING was downloaded for this file), and lets the table
// take the same shader, the same light rig, the same element response and the
// same build gates as the rooms it stands in.
//
// ======================================================================= WHY IT IS NOT IN A ROOM
// `AssertPlaySpaceClear` (BuildEnvironmentRooms.cs) FAILS THE BUILD if standing
// geometry intrudes on the play space, and `PlaySpaceCarpetR` = 1.70 m reserves
// a hard clearance directly under the floating board. This table's own footprint
// radius is ~1.55 m — i.e. it lies ENTIRELY inside that reserved disc, and it is
// 0.75 m tall, so it is not a "carpet" either. Baked into `RoomGeo` it would not
// merely be rejected, it is the exact shape that gate exists to reject.
//
// That is the design speaking, not a bug: the gate encodes "the board floats over
// an empty clearing you stand in", and the map phase is the opposite picture —
// THE TABLE IS THE FLOOR THE MAP STANDS ON. So the resolution is modelled, per
// plan §4.2: the table is a STANDALONE PREFAB under Assets/Bundle/Table/, loaded
// at runtime the way PlayTray.prefab already is, and `AssertPlaySpaceClear` never
// sees it. Both room bakes stay byte-identical; nothing in the 19 000-line room
// builder changed for this feature.
//
// AND THE EXEMPTION IS GUARDED RATHER THAN COMMENTED (`AssertNotInAnyRoom`,
// `AssertWhyItCannotStandInARoom` below): the bake fails if this prefab ever ends
// up referenced by a room prefab, and it re-measures the table against
// PlaySpaceCarpetR itself so the one documented exception cannot quietly stop
// being true.
//
// ======================================================================= THE BOARDS ARE THE PHOTOGRAPH'S BOARDS
// The wood is `dark_wooden_planks_alb/_nrm` (Poly Haven, CC0, already in
// Imported/Textures for the cellar ceiling). Its planks run along U, and the nine
// plank seams in the 2048² albedo were MEASURED rather than guessed (row-mean
// against a ±60-row median baseline; each seam is 30-48 grey levels below its
// neighbourhood, spacing 211-244 px). `Seam` below is that measurement.
//
// Every part of this table is then cut TO those boards:
//   * a part is either a whole number of the photograph's boards wide, with its
//     two long edges exactly on two measured seams, or narrow enough to sit
//     strictly INSIDE one board with no seam crossing it;
//   * so the table top is not five plank meshes with a plank texture on them
//     (two plankings that would disagree at every joint), it is one slab whose
//     UV span is exactly boards 1..5 of the photograph — the seams you see in it
//     are the photograph's own, and its two long edges are board edges.
// `AssertBoardAligned` fails the build for any face that breaks that rule, which
// is what makes the board widths below DERIVED (`Bands(...)`) instead of typed.
//
// ======================================================================= ELEMENTS
// The table participates, and it participates for free: it is drawn by
// GloomhavenVR/EnvRoom, whose element response reads the two GLOBALS the mod
// publishes (`_GhvrElemA/_GhvrElemB`, ElementMood.cs). See THE ELEMENT FRAME
// below for what that means for all 64 subsets and why the middle of the top
// stays readable at every one of them.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class MapTableBuilder
    {
        private const string AssetDir = "Assets/Bundle/Table";
        internal const string PrefabPath = AssetDir + "/MapTable.prefab";
        private const string MeshPath = AssetDir + "/MapTable.asset";
        private const string MatPath = AssetDir + "/MapTable.mat";
        private const string TexBase = "dark_wooden_planks";

        // ------------------------------------------------------------ THE WOOD
        /// <summary>Metres one texture tile covers. Everything about the board
        /// widths follows from this one number and the measured seams: a board is
        /// `Seam[k+1]-Seam[k]` of a tile, i.e. 0.185-0.214 m at 1.80 m/tile, which
        /// is what sawn boards actually vary like.</summary>
        private const float TileM = 1.80f;

        /// <summary>The nine plank seams of dark_wooden_planks_alb.jpg, in Unity
        /// UV v (0 at the bottom of the image), ascending. Measured, not authored:
        ///     rows (from the top of the 2048² albedo) 117, 328, 551, 778, 1022,
        ///     1256, 1484, 1703, 1932  ->  v = 1 - row/2048.
        /// Re-derive with:
        ///     row mean luminance, minus the median over a ±60-row window, peaks
        ///     above 6 grey levels, nearest peaks merged within 30 rows.
        /// If the texture is ever re-imported at a different crop this table is
        /// the first thing that has to be re-measured — and AssertBoardAligned
        /// will say so, loudly, because the parts would stop landing on seams.
        /// </summary>
        private static readonly float[] Seam =
        {
            1f - 1932f / 2048f, 1f - 1703f / 2048f, 1f - 1484f / 2048f,
            1f - 1256f / 2048f, 1f - 1022f / 2048f, 1f -  778f / 2048f,
            1f -  551f / 2048f, 1f -  328f / 2048f, 1f -  117f / 2048f,
        };

        /// <summary>The v span of boards <paramref name="lo"/>..<paramref name="hi"/>
        /// inclusive — i.e. from seam `lo` to seam `hi+1`.</summary>
        private static (float v0, float v1) Bands(int lo, int hi) => (Seam[lo], Seam[hi + 1]);

        /// <summary>Metres a board span is wide. THE ONLY way a width is written
        /// down in this file: no dimension across the grain is a typed number, so
        /// geometry and texture cannot drift apart.</summary>
        private static float Wide(int lo, int hi)
        {
            var (a, b) = Bands(lo, hi);
            return (b - a) * TileM;
        }

        /// <summary>A v start for a part that is NARROWER than one board: centred
        /// inside board <paramref name="band"/> so no seam can cross it.</summary>
        private static float Inside(int band, float sizeM)
        {
            var (a, b) = Bands(band, band);
            float span = sizeM / TileM;
            if (span > b - a)
                throw new Exception($"MapTable: a {sizeM:F3} m face was asked to sit inside board "
                    + $"{band}, which is only {(b - a) * TileM:F3} m wide. Give it a whole board "
                    + "span instead — a face that overhangs its board draws a plank seam across "
                    + "the middle of a rail.");
            return a + 0.5f * ((b - a) - span);
        }

        // -------------------------------------------------------- THE FURNITURE
        // Every number here is a MEASURED piece of furniture or a span of the
        // photograph's boards, in the spirit of the cellar's stool (user, cellar
        // 12: "als ob es ein Hocker für eine Maus ist" — the fix was to scale it
        // to a measured 0.45 m seat instead of to a number that looked right).
        //
        //   0.75 m table top      the standard dining/refectory height; the same
        //                         number the cellar bake already prints the stool
        //                         against ("a real stool and a real table are 0.45
        //                         and 0.75, i.e. 60 %").
        //   0.45 m bench seat     the same measured seat height as that stool, so
        //                         the two pieces of furniture this mod has built
        //                         agree with each other.
        //   1.80 x 1.01 m top     a four-place refectory top. Long enough that two
        //                         people fit on each end bench without touching,
        //                         short enough that the far end of the map is
        //                         within a lean of either bench.
        //   0.13 m bench gap      between the table's end and the bench's near
        //                         edge: knees clear the top, and a STANDING player
        //                         (the normal case in VR) has somewhere to put his
        //                         feet without stepping on a bench.
        private const float TopY = 0.75f;      // top SURFACE, metres over the foot plane
        private const float TopT = 0.055f;     // plank thickness
        private const float TopL = 1.80f;      // along the grain (X) = one tile
        private static readonly float TopW = Wide(1, 5);   // 1.0125 m — boards 1..5
        private const float SeatY = 0.45f;     // bench seat SURFACE
        private const float SeatT = 0.045f;
        private const float SeatLen = 1.30f;   // seats two, at 0.65 m each
        private static readonly float SeatD = Wide(6, 7);  // 0.3815 m — boards 6..7
        private const float BenchGap = 0.13f;
        private static readonly float BenchX = TopL * 0.5f + BenchGap + SeatD * 0.5f;
        private const float TrestleX = 0.58f;  // 0.32 m of overhang past each trestle

        /// <summary>An overlap, and it is deliberate. Parts INTERPENETRATE by 5 mm
        /// rather than touching exactly, because the closed-and-outward gate welds
        /// vertices at 1e-5 m: two boxes that share a face share vertices, their
        /// shared edges cancel in the pairing, and a perfectly good solid reports
        /// itself open. 5 mm of interpenetration is invisible and cannot weld.
        /// </summary>
        private const float Weld = 0.005f;

        // ============================================================== the build
        [MenuItem("GloomhavenVR/Build Map Table")]
        public static void BuildFromMenu() { Build(); AssetDatabase.SaveAssets(); }

        public static void Build()
        {
            Directory.CreateDirectory(AssetDir);
            var a = new EnvRoomBuilder.Acc();
            BuildTop(a);
            BuildTrestle(a, +TrestleX);
            BuildTrestle(a, -TrestleX);
            BuildStretcher(a);
            BuildBench(a, +BenchX);
            BuildBench(a, -BenchX);

            // ---- THE WINDING GATE, on the room builder's own instrument -------
            // Seven meshes in this project have shipped wound against the side
            // they are seen from and one was invisible for ten builds, so this is
            // not optional for anything new. AssertClosedAndOutward proves itself
            // on THIS mesh first (it reverses every triangle and requires the
            // check to throw), then checks it: closed (every edge paired), outward
            // (positive signed volume) and every triangle agreeing with its own
            // vertex normal.
            EnvRoomBuilder.AssertClosedAndOutward(a, "MapTable");

            var mesh = a.Build("MapTable");
            EnvironmentsBuilder.SaveMesh(MeshPath, mesh);
            AssetDatabase.SaveAssets();
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath)
                        ?? throw new Exception("MapTable: the mesh asset did not save.");
            // TANGENTS, EXPLICITLY. SaveMesh's re-author path copies vertices,
            // normals, uvs and colours into the existing asset but NOT tangents,
            // so a rebuild after a dimension change would leave this prop's normal
            // mapping keyed to geometry that no longer exists. Every face here is
            // normal-mapped wood, so that is not cosmetic — and tangents are a
            // pure function of the positions and uvs, so recomputing them costs
            // nothing and cannot be wrong.
            saved.RecalculateTangents();
            EditorUtility.SetDirty(saved);
            AssetDatabase.SaveAssets();

            var mat = BuildMaterial(saved);
            var root = Assemble(saved, mat);
            try
            {
                // Same walk every renderer in both rooms takes: the shader must
                // have a VertexMovers row, and the mesh's culling box must contain
                // everything that shader's VERTEX PROGRAM can reach.
                EnvRoomBuilder.ApplyDisplacedBounds("MapTable", root.transform);
                AssertWhyItCannotStandInARoom(saved);
                LogTable(saved, root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new Exception("SaveAsPrefabAsset failed for MapTable");
                Debug.Log("[GloomhavenVR][Env] Prefab written: " + PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }

            AssetDatabase.SaveAssets();
            AssertNotInAnyRoom();
        }

        // ================================================================= parts
        // Axis convention for every part: GRAIN is the axis the planks run along
        // (u), ACROSS is the axis the board widths are counted on (v on the two
        // big faces), THICK is what is left (v on the edge and end faces).
        private static void BuildTop(EnvRoomBuilder.Acc a)
        {
            var (v0, _) = Bands(1, 5);
            // One slab, and the planks in it are the photograph's. Its two long
            // edges land exactly on seams, so the top does not end in the middle
            // of a board.
            Board(a, "Top", new Vector3(0f, TopY - TopT * 0.5f, 0f),
                  grain: Axis.X, len: TopL,
                  across: Axis.Z, wide: TopW, vAcross: v0,
                  thick: TopT, vThick: Inside(0, TopT));
        }

        private static void BuildTrestle(EnvRoomBuilder.Acc a, float x)
        {
            float sgn = Mathf.Sign(x);
            string tag = sgn > 0 ? "E" : "W";
            // The upright slab: a trestle end, grain running VERTICALLY the way a
            // standing board's does, three of the photograph's boards wide.
            float upW = Wide(3, 5);              // 0.6196 m
            const float upT = 0.075f;
            float upLo = 0.085f, upHi = TopY - TopT + Weld;
            Board(a, "Trestle" + tag, new Vector3(x, 0.5f * (upLo + upHi), 0f),
                  grain: Axis.Y, len: upHi - upLo,
                  across: Axis.Z, wide: upW, vAcross: Bands(3, 5).v0,
                  thick: upT, vThick: Inside(0, upT));

            // The foot it stands on, one board wide, lying across the room.
            float footW = Wide(3, 3);            // 0.2057 m along X
            const float footH = 0.09f;
            Board(a, "Foot" + tag, new Vector3(x, footH * 0.5f, 0f),
                  grain: Axis.Z, len: 0.88f,
                  across: Axis.X, wide: footW, vAcross: Bands(3, 3).v0,
                  thick: footH, vThick: Inside(0, footH));

            // ...and the cap the top rests on.
            float capW = Wide(2, 2);             // 0.2004 m along X
            const float capH = 0.06f;
            float capHi = TopY - TopT + Weld;
            Board(a, "Cap" + tag, new Vector3(x, capHi - capH * 0.5f, 0f),
                  grain: Axis.Z, len: 0.84f,
                  across: Axis.X, wide: capW, vAcross: Bands(2, 2).v0,
                  thick: capH, vThick: Inside(0, capH));
        }

        private static void BuildStretcher(EnvRoomBuilder.Acc a)
        {
            // The beam between the two trestles — the piece that makes a table
            // read as a trestle table rather than as a top on two boxes. Narrower
            // than a board, so it sits strictly inside one.
            const float w = 0.11f, h = 0.09f, y = 0.345f;
            Board(a, "Stretcher", new Vector3(0f, y, 0f),
                  grain: Axis.X, len: 2f * (TrestleX + 0.04f),
                  across: Axis.Z, wide: w, vAcross: Inside(1, w),
                  thick: h, vThick: Inside(0, h));
        }

        private static void BuildBench(EnvRoomBuilder.Acc a, float x)
        {
            string tag = x > 0 ? "E" : "W";
            // THE SEAT: two of the photograph's boards, edges on seams, at the
            // same measured 0.45 m the cellar's stool was rescaled to.
            Board(a, "Seat" + tag, new Vector3(x, SeatY - SeatT * 0.5f, 0f),
                  grain: Axis.Z, len: SeatLen,
                  across: Axis.X, wide: SeatD, vAcross: Bands(6, 7).v0,
                  thick: SeatT, vThick: Inside(0, SeatT));

            float legW = Wide(4, 4);             // 0.2145 m along X
            const float legT = 0.055f;
            float legHi = SeatY - SeatT + Weld;
            foreach (float z in new[] { -0.50f, 0.50f })
                Board(a, $"Leg{tag}{(z < 0 ? "S" : "N")}", new Vector3(x, legHi * 0.5f, z),
                      grain: Axis.Y, len: legHi,
                      across: Axis.X, wide: legW, vAcross: Bands(4, 4).v0,
                      thick: legT, vThick: Inside(0, legT));

            const float railW = 0.08f, railH = 0.075f;
            float railHi = SeatY - SeatT + Weld;
            Board(a, "Rail" + tag, new Vector3(x, railHi - railH * 0.5f, 0f),
                  grain: Axis.Z, len: 1.02f,
                  across: Axis.X, wide: railW, vAcross: Inside(1, railW),
                  thick: railH, vThick: Inside(0, railH));
        }

        // ============================================================= the boxes
        private enum Axis { X = 0, Y = 1, Z = 2 }

        private static Vector3 AxisDir(Axis a) =>
            a == Axis.X ? Vector3.right : a == Axis.Y ? Vector3.up : Vector3.forward;

        /// <summary>One board: a closed box, six faces, UVs that put the grain
        /// along <paramref name="grain"/> and the board widths along
        /// <paramref name="across"/>, and vertex colour carrying the baked shade.
        /// </summary>
        private static void Board(EnvRoomBuilder.Acc a, string what, Vector3 centre,
                                  Axis grain, float len, Axis across, float wide, float vAcross,
                                  float thick, float vThick)
        {
            var thickAxis = (Axis)(3 - (int)grain - (int)across);
            if (grain == across) throw new Exception($"MapTable {what}: grain and across axes are equal.");
            Vector3 g = AxisDir(grain), w = AxisDir(across), t = AxisDir(thickAxis);

            // The two v mappings this board uses, checked ONCE each against the
            // photograph's seams — see THE BOARDS ARE THE PHOTOGRAPH'S BOARDS.
            AssertBoardAligned(vAcross, vAcross + wide / TileM, $"{what} (across)");
            AssertBoardAligned(vThick, vThick + thick / TileM, $"{what} (edge)");

            float wMin = Vector3.Dot(centre, w) - wide * 0.5f;
            float tMin = Vector3.Dot(centre, t) - thick * 0.5f;
            // uv from the vertex's OWN position, so nothing about the winding fix
            // below can move a texel: u runs with the grain, v with the boards.
            Vector2 UVBig(Vector3 p) => new Vector2(Vector3.Dot(p, g) / TileM,
                                                    vAcross + (Vector3.Dot(p, w) - wMin) / TileM);
            Vector2 UVEdge(Vector3 p) => new Vector2(Vector3.Dot(p, g) / TileM,
                                                     vThick + (Vector3.Dot(p, t) - tMin) / TileM);
            Vector2 UVEnd(Vector3 p) => new Vector2(Vector3.Dot(p, w) / TileM,
                                                    vThick + (Vector3.Dot(p, t) - tMin) / TileM);

            Face(a, centre + t * (thick * 0.5f), g * (len * 0.5f), w * (wide * 0.5f), t, UVBig);
            Face(a, centre - t * (thick * 0.5f), g * (len * 0.5f), w * (wide * 0.5f), -t, UVBig);
            Face(a, centre + w * (wide * 0.5f), g * (len * 0.5f), t * (thick * 0.5f), w, UVEdge);
            Face(a, centre - w * (wide * 0.5f), g * (len * 0.5f), t * (thick * 0.5f), -w, UVEdge);
            Face(a, centre + g * (len * 0.5f), w * (wide * 0.5f), t * (thick * 0.5f), g, UVEnd);
            Face(a, centre - g * (len * 0.5f), w * (wide * 0.5f), t * (thick * 0.5f), -g, UVEnd);
        }

        /// <summary>One quad, wound so that it faces <paramref name="outward"/>.
        ///
        /// <para>The winding is not asserted here and then hoped for: the vertex
        /// order Acc.Quad expects gives a face normal of Cross(hv, hu), so if that
        /// disagrees with the outward direction the V half-axis is NEGATED, which
        /// flips the winding and leaves every vertex's uv untouched (uv is read
        /// from the position, not from the corner index). Both halves of the
        /// closed-and-outward gate — the edge pairing and the normal agreement —
        /// then have to pass anyway on the finished solid.</para></summary>
        private static void Face(EnvRoomBuilder.Acc a, Vector3 c, Vector3 hu, Vector3 hv,
                                 Vector3 outward, Func<Vector3, Vector2> uv)
        {
            if (Vector3.Dot(Vector3.Cross(hv, hu), outward) < 0f) hv = -hv;
            int b = a.Count;
            foreach (var p in new[] { c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv })
                a.Vert(p, outward, uv(p), Shade(p, outward));
            a.Quad(b);
        }

        // ------------------------------------------------------------ THE SHADE
        // Vertex colour is multiplied into the albedo by EnvRoom (_VCol), and it
        // is the whole of this prop's form-shading, because the key light is
        // STRAIGHT DOWN (see THE LIGHT RIG) and a vertical face therefore takes
        // no directional term at all. Three terms, all analytic:
        //   * height    a table is lit from above, so everything low is darker.
        //   * downface  an upward-facing normal sees the map and the ceiling; a
        //               downward-facing one sees the floor.
        //   * undertop  the volume enclosed by the top's own footprint is the
        //               darkest part of any table, and it is where the stretcher,
        //               the trestle caps and the inner faces of the uprights live.
        // The three floors (0.55 / 0.65 / 0.72, i.e. 0.40 where all three meet at
        // the foot of a trestle) were RAISED once after the first preview pass:
        // at 0.42/0.58/0.62 the trestles and the bench legs came out very nearly
        // black, and a player stands at this table with the legs at knee height.
        // Undersides are dark; they are not holes.
        private static Color Shade(Vector3 p, Vector3 n)
        {
            float ao = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p.y / 0.62f));
            if (n.y < -0.45f) ao *= 0.65f;
            if (Mathf.Abs(p.x) < TopL * 0.5f + 0.03f && Mathf.Abs(p.z) < TopW * 0.5f + 0.03f
                && p.y < TopY - TopT)
                ao *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((TopY - TopT - p.y) / 0.45f));
            return new Color(ao, ao, ao, 1f);
        }

        /// <summary>THE RULE THAT KEEPS THE MESH AND THE PHOTOGRAPH IN STEP, as a
        /// build failure. A face's v span must either land exactly on two of the
        /// measured seams (a whole number of the photograph's boards) or contain
        /// no seam at all (a rail cut from inside one board). Anything else draws
        /// a plank seam across a place where the geometry has no joint, or ends a
        /// board halfway through a plank — which is what "textured with planks"
        /// looks like when nobody checks.</summary>
        private static void AssertBoardAligned(float v0, float v1, string what)
        {
            const float Eps = 1e-4f;
            if (v0 < -Eps || v1 > 1f + Eps)
                throw new Exception($"MapTable {what}: v span {v0:F4}..{v1:F4} leaves the tile. Every "
                    + "face on this prop is authored inside one tile so the seam table can be "
                    + "consulted without worrying about the wrap.");
            var inside = Seam.Where(s => s > v0 + Eps && s < v1 - Eps).ToArray();
            if (inside.Length == 0) return;                                  // inside one board
            bool ends = Seam.Any(s => Mathf.Abs(s - v0) < Eps) && Seam.Any(s => Mathf.Abs(s - v1) < Eps);
            if (ends) return;                                                // whole boards
            throw new Exception($"MapTable {what}: its v span {v0:F4}..{v1:F4} has {inside.Length} "
                + "plank seam(s) of dark_wooden_planks running through it but does not start and end "
                + "on one. Either give it a whole number of boards (Wide/Bands) or make it narrow "
                + "enough to sit inside one (Inside) — a joint the geometry does not have is exactly "
                + "as visible as a joint the texture does not have.");
        }

        // ============================================================= material
        private static Material BuildMaterial(Mesh mesh)
        {
            var sh = Shader.Find("GloomhavenVR/EnvRoom")
                     ?? throw new Exception("Shader 'GloomhavenVR/EnvRoom' missing");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null)
            {
                m = new Material(sh) { name = "MapTable" };
                AssetDatabase.CreateAsset(m, MatPath);
            }
            else m.shader = sh;                       // re-authored every build

            m.SetTexture("_MainTex", EnvRoomBuilder.Imp(TexBase + "_alb"));
            m.SetTexture("_BumpMap", EnvRoomBuilder.Imp(TexBase + "_nrm"));
            m.SetFloat("_BumpScale", 0.9f);           // as the cellar uses this same set
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_VCol", 1f);                  // the baked shade above IS the form

            // ------------------------------------------------------ THE LIGHT RIG
            // A room's materials are lit by that room's rig (ApplyRig). This prop
            // has no room: it stands in the cellar, in the wood, in `Default`, in
            // `OffBlack` and — if the user rules for it — over passthrough, and it
            // is loaded as a standalone prefab, so its rig has to be
            // ROOM-AGNOSTIC. The picture it is authored to is the one the feature
            // itself provides: THE MAP LYING ON THE TABLE IS THE LIGHT IN THE
            // ROOM. Hence a soft hemisphere, a weak key straight down, and one
            // steady warm point light hanging where the parchment will hang.
            //
            // These five values are this file's tunables and the honest place for
            // a hardware round to start: a prop with its own baked rig cannot
            // match two differently-lit rooms exactly, and nothing but a headset
            // can say which way it is wrong.
            m.SetColor("_AmbUp", new Color(0.095f, 0.091f, 0.086f));
            m.SetColor("_AmbDown", new Color(0.032f, 0.030f, 0.028f));
            m.SetColor("_DirCol", new Color(0.160f, 0.145f, 0.120f));
            m.SetVector("_L0Pos", new Vector4(0f, TopY + 0.45f, 0f, 1f / 2.4f));
            m.SetColor("_L0Col", new Color(1.00f, 0.88f, 0.66f) * 1.35f);   // a = 0: it does NOT flicker
            m.SetFloat("_PtHard", 0f);                // the softest falloff the shader has
            for (int i = 1; i < 3; i++)
            {
                m.SetVector($"_L{i}Pos", new Vector4(0, 0, 0, 1));
                m.SetColor($"_L{i}Col", new Color(0, 0, 0, 0));
            }

            // ---- THE KEY POINTS STRAIGHT UP, AND THAT IS A GUARD ---------------
            // EnvRoom masks the LIGHT element's indoor brightening with the
            // cellar window's throw (GhvrMoonWindow), computed in the material's
            // OWN object space from _DirDir's horizontal bearing. This prop's
            // object space is the table's, not the cellar's, so any horizontal
            // bearing here would be asking a window in another frame of reference
            // where the light falls on a table that may be standing anywhere.
            // GhvrMoonWindow's own first line is the answer: a direction with no
            // horizontal component (|dir.xz| < 0.05) returns 0 — never a spurious
            // lift — and the table simply keeps the moonlight it has while Light
            // brightens what the window sees, which IS the ModBuild 151 ruling
            // ("mach ausschließlich das Licht aus dem Kellerfenster ... heller").
            // So the vertical key is not only the look; it is what makes the mask
            // provably inert. Asserted, because a later "let's tilt the key a
            // little" would silently switch a window mask on.
            var dir = new Vector4(0f, 1f, 0f, 0f);
            m.SetVector("_DirDir", dir);
            m.SetVector("_RimDir", dir);              // _RimCol is black: the rim is off
            if (new Vector2(dir.x, dir.z).magnitude >= 0.05f)
                throw new Exception("MapTable: _DirDir has a horizontal bearing. EnvRoom would then "
                    + "evaluate the CELLAR WINDOW's throw (GhvrMoonWindow) in the TABLE's object "
                    + "space and mask the Light element's lift with a rectangle that means nothing "
                    + "here. Keep the key vertical, or write the window's geometry for this prop.");

            // ------------------------------------------------- THE ELEMENT FRAME
            // "Every subset of the six elements must read sensibly on anything you
            // add to a room" — so this prop is IN, not inert, and the mechanism is
            // the one the rooms use: EnvRoom reads _GhvrElemA/_GhvrElemB, which
            // the mod publishes as globals, so a standalone prefab is reached by
            // them with no runtime plumbing at all. What the six do here:
            //   ICE    frost on the wood, exactly as on the cellar's timbers.
            //   FIRE   the warm rim, and the fire seats stay black (there is no
            //          fire standing on this table).
            //   LIGHT  lifts the moonlight it has; indoors that lift is masked to
            //          the window and this prop is not in it (above).
            //   DARK   takes it away, unmasked — a dark table is still a table.
            //   AIR    nothing. It moves cards and cloth (EnvRoomCutout), and this
            //          prop has neither — the same as every EnvRoom surface in
            //          both rooms.
            //   EARTH  nothing, for the same reason: since the painted moss was
            //          deleted, Earth reaches a room through growth CARDS only.
            // All four terms that do fire are independent products of their own
            // element's strength, so the 64 subsets are settled by the six singles
            // plus a mixture — which is what the preview series shoots.
            //
            // AND THE PERIPHERY IS THE TABLE'S OWN. GhvrRim ramps every element
            // from the middle of _ElemRad to its edge, and the standing ruling it
            // exists for is literally about this object: "an element at full
            // strength owns the walls and touches the middle of the table hardly
            // at all". Feeding it the table's own footprint radius makes frost
            // creep in from the benches and the ends of the top and leave the
            // middle — where the map is — clear.
            var vb = VertexBounds(mesh);
            float rad = new Vector2(Mathf.Max(Mathf.Abs(vb.min.x), Mathf.Abs(vb.max.x)),
                                    Mathf.Max(Mathf.Abs(vb.min.z), Mathf.Abs(vb.max.z))).magnitude;
            m.SetVector("_ElemCentre", Vector4.zero);   // the prefab IS built around its centre
            m.SetFloat("_ElemRad", rad);
            m.SetFloat("_ElemScl", 1f);                 // object units are metres at scale 1
            // _ElemFrost and _ElemWarm stay at the shader's authored defaults of
            // 1: that default is documented there as the value for "stone, bark,
            // wood — everything the elements should reach", and this is wood. A
            // number written here would be a second opinion about the same thing.

            EditorUtility.SetDirty(m);
            return m;
        }

        // ============================================================== assembly
        /// <summary>The prefab, and with it the RUNTIME CONTRACT — because none of
        /// the numbers above exist at runtime (this is an editor script), the mod
        /// reads the table's geometry off these transforms:
        /// <list type="bullet">
        /// <item>the ROOT's origin is the table's FOOT PLANE at its centre, so
        /// seating it is `position = floor point` with no offset to get wrong —
        /// and for `MapTableAnchor` (plan §4.2) floorY is exactly the root's y;</item>
        /// <item><c>TopAnchor</c> sits at the middle of the top SURFACE, +X along
        /// the table's length, +Z across it, so the map can be laid out in its
        /// local frame;</item>
        /// <item><c>Seat0..3</c> are the four bench places, each at the seat
        /// surface and facing the table.</item>
        /// </list></summary>
        private static GameObject Assemble(Mesh mesh, Material mat)
        {
            var root = new GameObject("MapTable");
            var geo = new GameObject("Geo");
            geo.transform.SetParent(root.transform, false);
            geo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = geo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var top = new GameObject("TopAnchor");
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = new Vector3(0f, TopY, 0f);

            // Two places per bench, 0.65 m apart: a bench that seats two is what
            // makes four seats out of "Bänken an beiden Enden".
            int n = 0;
            foreach (float x in new[] { BenchX, -BenchX })
                foreach (float z in new[] { 0.325f, -0.325f })
                {
                    var s = new GameObject("Seat" + n++);
                    s.transform.SetParent(root.transform, false);
                    s.transform.localPosition = new Vector3(x, SeatY, z);
                    // facing the table, i.e. along the bench's own normal — a
                    // person on a bench faces the table, not the table's centre.
                    s.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sign(x), 0f, 0f));
                }
            return root;
        }

        // ================================================================= gates
        private static Bounds VertexBounds(Mesh mesh)
        {
            var v = mesh.vertices;
            if (v == null || v.Length == 0) throw new Exception("MapTable: the mesh has no vertices.");
            var b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]);
            return b;
        }

        /// <summary>THE EXEMPTION, MEASURED. This prop is allowed to skip
        /// AssertPlaySpaceClear only because it is not in a room — and the reason
        /// it may never be put in one is that it would fail that gate outright.
        /// This re-derives that from the real mesh against the room builder's own
        /// PlaySpaceCarpetR, so the claim in the header cannot quietly stop being
        /// true (if the table were ever shrunk to something a clearing would
        /// tolerate, this fires and whoever did it has to come and read WHY IT IS
        /// NOT IN A ROOM before deciding).</summary>
        private static void AssertWhyItCannotStandInARoom(Mesh mesh)
        {
            var b = VertexBounds(mesh);
            float rad = new Vector2(Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)),
                                    Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z))).magnitude;
            float carpet = EnvRoomBuilder.PlaySpaceCarpetR;
            if (rad >= carpet)
                throw new Exception($"MapTable: its footprint radius is {rad:F2} m, which is OUTSIDE "
                    + $"the {carpet:F2} m clearance under the board — so the reasoning in WHY IT IS "
                    + "NOT IN A ROOM (the table lies entirely inside the disc the play-space gate "
                    + "reserves) no longer describes this table. Re-read that block before changing "
                    + "anything else.");
            Debug.Log($"[GloomhavenVR][Env] MapTable stays OUT of both rooms by construction: its "
                      + $"footprint radius is {rad:F2} m and it stands {b.max.y:F2} m tall, i.e. it "
                      + $"lies entirely inside the {carpet:F2} m clearance AssertPlaySpaceClear "
                      + "reserves under the floating board and is far too tall to be a carpet. Baked "
                      + "into RoomGeo it would fail the build; it ships as a standalone prefab and "
                      + "the mod seats it (plan §4.2).");
        }

        /// <summary>...and the other half of the same rule: nobody may have
        /// parented it into a room after all. A comment is not a guard.</summary>
        private static void AssertNotInAnyRoom()
        {
            var table = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                        ?? throw new Exception("MapTable: the prefab did not save.");
            foreach (var room in new[] { "Env_Cellar", "Env_Swamp" })
            {
                string path = $"Assets/Bundle/Environments/{room}.prefab";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var deps = EditorUtility.CollectDependencies(new UnityEngine.Object[] { go })
                    .Select(AssetDatabase.GetAssetPath).ToArray();
                if (deps.Contains(PrefabPath) || deps.Contains(MeshPath))
                    throw new Exception($"MapTable is referenced by {room}. It may never be part of a "
                        + "room prefab: AssertPlaySpaceClear would fail the build over it (see WHY IT "
                        + "IS NOT IN A ROOM), and a room that contains the table can no longer be "
                        + "placed around a table the mod seats itself.");
            }
            Debug.Log("[GloomhavenVR][Env] MapTable is referenced by neither room prefab — the "
                      + "play-space gate never sees it, and both room bakes are unchanged.");
        }

        private static void LogTable(Mesh mesh, GameObject root)
        {
            var b = VertexBounds(mesh);
            var sb = new System.Text.StringBuilder();
            sb.Append("[GloomhavenVR][Env] MAP TABLE — user: \"Die world map steht auf einem Tisch "
                + "mit Bänken an beinden Enden.\"\n");
            sb.Append($"    overall      {b.size.x:F3} x {b.size.z:F3} m on the floor, {b.size.y:F3} m tall "
                + $"({mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, 1 material)\n");
            sb.Append($"    top          {TopL:F3} x {TopW:F3} m at {TopY:F3} m, {TopT * 1000f:F0} mm thick — "
                + $"boards 1..5 of dark_wooden_planks, edges exactly on two measured seams\n");
            sb.Append($"    benches      2 x {SeatD:F3} x {SeatLen:F3} m at {SeatY:F3} m (the cellar stool's "
                + $"own measured seat height), {BenchGap:F2} m clear of each end of the top\n");
            sb.Append("    board widths ");
            for (int i = 0; i + 1 < Seam.Length; i++) sb.Append($"{Wide(i, i):F3} ");
            sb.Append($"m — MEASURED from the photograph, not authored\n");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root.transform && t.name != "Geo")
                    sb.Append($"    anchor       {t.name,-10} local {t.localPosition:F3} "
                              + $"forward {t.forward:F2}\n");
            Debug.Log(sb.ToString());
        }
    }
}
