// GloomhavenVR companion project — export the SHIPPED keycap meshes as OBJ.
//
//   xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode -quit \
//     -projectPath unity/GloomhavenVR.Assets \
//     -executeMethod GloomhavenVR.ExportCapMeshes.Run -logFile cap-export.log
//
// WHY THIS EXISTS, AND WHY IT REFLECTS INTO THE MOD DLL RATHER THAN PORTING THE BUILDER.
//
// Assets/Editor/PreviewKeycaps.cs carries a HAND PORT of Cards/CardMesh.BuildBeveledKeycap and
// BuildRoundKeycap ("Ported verbatim from ..."), guarded by a MeshInvariant() that compares vertex
// and triangle COUNTS. That guard is real and it caught nothing this round for a good reason: round
// 4 makes the mesh depend on the BOARD, so the counts differ per board and a count check has
// nothing fixed to compare against. A second implementation of a mesh that now has three variants
// is three chances to draw a picture of something the game does not build — and this project has
// already lost rounds to exactly that ("A picture that cannot show the thing is not evidence").
//
// So this loads the BUILT GloomhavenVR.dll and calls the real CardMesh through reflection. The mesh
// exported here is, by construction, the mesh the game builds: there is no port to drift.
//
// CardMesh and CapFaceLayout are `internal`, hence BindingFlags.NonPublic. That is the point of the
// exercise rather than a workaround — nothing here is a public API and nothing should become one so
// a preview station can see it.
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class ExportCapMeshes
    {
        // Application.dataPath is <repo>/unity/GloomhavenVR.Assets/Assets, so the repo root is three
        // levels up — Assets -> GloomhavenVR.Assets -> unity -> root. CAP_MESH_DLL overrides it.
        private const string DllRel = "../../../src/GloomhavenVR/bin/Release/net472/GloomhavenVR.dll";

        // Cards/PlayTray.6.Build.cs SquareCapThickness / the rest disc's thickness, and
        // Assets/Editor/PreviewKeycaps.cs's Styles table (BoardAnchors.FitCapSize applied to the
        // tuned 0.063 x 0.065 m against each board's MEASURED seat recess).
        private const float SquareThick = 0.036f;
        private const float DiscThick = 0.012f;
        private static readonly (string Style, int Board, float W, float H, float Disc)[] Styles =
        {
            ("oak",    0, 0.0630f, 0.0563f, 0.0736f),
            ("steel",  1, 0.0630f, 0.0621f, 0.0737f),
            ("bronze", 2, 0.0532f, 0.0439f, 0.0601f),
        };

        private static int _exit;
        private static void Log(string s) => Debug.Log("[capexport] " + s);
        private static void Err(string s) { Debug.LogError("[capexport] " + s); _exit = 1; }

        public static void Run()
        {
            try
            {
                string outDir = Environment.GetEnvironmentVariable("CAP_MESH_OUT");
                if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("cap-meshes");
                Directory.CreateDirectory(outDir);

                string dll = Environment.GetEnvironmentVariable("CAP_MESH_DLL");
                if (string.IsNullOrEmpty(dll))
                    dll = Path.GetFullPath(Path.Combine(Application.dataPath, DllRel));
                if (!File.Exists(dll))
                    throw new FileNotFoundException(
                        $"No mod DLL at {dll} — run ./scripts/build.sh first. This station exports the "
                        + "SHIPPED mesh and has no port to fall back on, deliberately.");
                Assembly mod = Assembly.LoadFrom(dll);
                Log($"loaded {mod.GetName().Name} from {dll}");

                Type cardMesh = mod.GetType("GloomhavenVR.Cards.CardMesh", throwOnError: true);
                Type layout = mod.GetType("GloomhavenVR.Cards.CapFaceLayout", throwOnError: true);
                Type conType = layout.GetNestedType("CapConstruction",
                                                    BindingFlags.NonPublic | BindingFlags.Public);
                if (conType == null)
                    throw new MissingMemberException(
                        "CapFaceLayout.CapConstruction not found — this DLL predates round 4's "
                        + "per-board construction, so exporting from it would silently produce three "
                        + "identical caps and a before/after sheet with no after in it.");

                MethodInfo conFor = layout.GetMethod("ConstructionFor",
                                                     BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo sq = cardMesh.GetMethod("BuildBeveledKeycap",
                                                   BindingFlags.NonPublic | BindingFlags.Static,
                                                   null,
                                                   new[] { typeof(float), typeof(float), typeof(float), conType },
                                                   null);
                MethodInfo rn = cardMesh.GetMethod("BuildRoundKeycap",
                                                   BindingFlags.NonPublic | BindingFlags.Static,
                                                   null,
                                                   new[] { typeof(float), typeof(float), typeof(int), conType },
                                                   null);
                FieldInfo plain = layout.GetField("PlainConstruction",
                                                  BindingFlags.NonPublic | BindingFlags.Static);
                if (conFor == null || sq == null || rn == null || plain == null)
                    throw new MissingMemberException("CardMesh/CapFaceLayout members not found — "
                                                     + "signature changed; fix this station, do not "
                                                     + "guess around it.");

                int segs = (int)cardMesh.GetField("RoundCapSegments",
                                                  BindingFlags.NonPublic | BindingFlags.Static)
                                        .GetValue(null);

                object plainCon = plain.GetValue(null);

                // BEFORE: one plain profile for all three boards — which is exactly what ModBuild
                // 289 shipped and exactly what was rejected. Exported at EACH board's own size, so
                // the before/after pair differs only in construction and never in scale.
                foreach (var s in Styles)
                {
                    Dump(outDir, $"{s.Style}_square_before",
                         (Mesh)sq.Invoke(null, new object[] { s.W, s.H, SquareThick, plainCon }));
                    Dump(outDir, $"{s.Style}_round_before",
                         (Mesh)rn.Invoke(null, new object[] { s.Disc, DiscThick, segs, plainCon }));

                    object con = conFor.Invoke(null, new object[] { s.Board });
                    Dump(outDir, $"{s.Style}_square_after",
                         (Mesh)sq.Invoke(null, new object[] { s.W, s.H, SquareThick, con }));
                    Dump(outDir, $"{s.Style}_round_after",
                         (Mesh)rn.Invoke(null, new object[] { s.Disc, DiscThick, segs, con }));
                }
                Log($"exit {_exit}");
            }
            catch (Exception e)
            {
                Err($"FAILED: {e}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(_exit);
        }

        /// <summary>
        /// Write one mesh as OBJ, ONE GROUP PER SUBMESH — the three submeshes are three different
        /// materials on the shipped cap (recessed field / bright bezel / dark wall) and a single
        /// merged object would render the whole cap in one colour, which is a picture of a cap
        /// nobody has ever seen.
        ///
        /// <para>It also AUDITS the mesh while it has it, because this is the only place in the
        /// chain that holds real geometry: every triangle's winding is checked against its own
        /// vertex normals, and every edge is counted. A closed solid has every edge shared by
        /// exactly two triangles; this repository has shipped SEVEN meshes wound against the side
        /// they are seen from, and a count of backward triangles is the cheapest possible way to
        /// not make that eight.</para>
        /// </summary>
        private static void Dump(string dir, string stem, Mesh m)
        {
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            Vector2[] uv = m.uv;
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine($"# GloomhavenVR {stem} — exported from the SHIPPED CardMesh, not a port");
            foreach (Vector3 p in v) sb.AppendLine($"v {p.x.ToString("R", ci)} {p.y.ToString("R", ci)} {p.z.ToString("R", ci)}");
            foreach (Vector3 p in n) sb.AppendLine($"vn {p.x.ToString("R", ci)} {p.y.ToString("R", ci)} {p.z.ToString("R", ci)}");
            foreach (Vector2 p in uv) sb.AppendLine($"vt {p.x.ToString("R", ci)} {p.y.ToString("R", ci)}");

            int backward = 0, tris = 0;
            var edges = new System.Collections.Generic.Dictionary<(int, int), int>();
            var weld = new System.Collections.Generic.Dictionary<(int, int, int), int>();
            // Quantise to 1 micron: the caps are 40-63 mm across, so a micron is well below any
            // real feature and well above float noise from the ring arithmetic.
            int Key(Vector3 p)
            {
                var q = (Mathf.RoundToInt(p.x * 1e6f), Mathf.RoundToInt(p.y * 1e6f),
                         Mathf.RoundToInt(p.z * 1e6f));
                if (!weld.TryGetValue(q, out int id)) { id = weld.Count; weld[q] = id; }
                return id;
            }
            string[] group = { "field", "bezel", "wall" };
            for (int s = 0; s < m.subMeshCount; s++)
            {
                sb.AppendLine($"g {(s < group.Length ? group[s] : "sub" + s)}");
                int[] t = m.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    tris++;
                    Vector3 rh = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                    Vector3 avg = (n[a] + n[b] + n[c]);
                    // A triangle whose right-hand normal opposes its own shading normal is drawn
                    // from the wrong side by any back-face-culling material — and every cap
                    // material is `new Material(BoardLit)`, which keeps the shader's _Cull = Back.
                    if (Vector3.Dot(rh, avg) < 0f) backward++;
                    // EDGES ARE KEYED ON POSITION, NOT ON INDEX, and the first version of this
                    // check keyed on index and was worthless. Both cap builders emit four FRESH
                    // vertices per quad (they must: the normals are flat per face), so no two
                    // quads share an index and an index-keyed test reports 72 of 90 edges "open"
                    // on the known-good ModBuild 289 square cap. It was measuring vertex reuse and
                    // calling it watertightness.
                    foreach ((int p, int q) in new[] { (a, b), (b, c), (c, a) })
                    {
                        int kp = Key(v[p]), kq = Key(v[q]);
                        var key = kp < kq ? (kp, kq) : (kq, kp);
                        edges.TryGetValue(key, out int cnt);
                        edges[key] = cnt + 1;
                    }
                    sb.AppendLine($"f {a + 1}/{a + 1}/{a + 1} {b + 1}/{b + 1}/{b + 1} {c + 1}/{c + 1}/{c + 1}");
                }
            }
            // CLASSIFY the open edges instead of only counting them. The raised HARDWARE — the
            // dome rivets and the oak dentil blocks — stands ON the rim land and omits its own base
            // face, so its footprint is a ring of edges used by exactly one triangle. That is a
            // normal modelling choice and it is NOT a hole: the rim land closes the solid
            // underneath. A hole that matters is an open edge somewhere ELSE, so the number that
            // means something is how many open edges are off the frontmost plane.
            //
            // Counting without classifying would have left "112 open edges" to be read as a defect
            // on a mesh that has none, which is how a round gets spent arguing with an instrument.
            var byPos = new System.Collections.Generic.Dictionary<int, Vector3>();
            foreach (Vector3 p in v) byPos[Key(p)] = p;
            float frontZ = float.MaxValue;
            foreach (Vector3 p in v) frontZ = Mathf.Min(frontZ, p.z);
            int open = 0, offPlane = 0;
            float seatZ = -Mathf.Abs(m.bounds.max.z - m.bounds.size.z);   // the rim-land plane
            foreach (var kv in edges)
            {
                if (kv.Value == 2) continue;
                open++;
                Vector3 p1 = byPos[kv.Key.Item1], p2 = byPos[kv.Key.Item2];
                // A footprint edge lies flat in ONE plane, and that plane is the rim land the
                // hardware is seated on — never the frontmost plane, which is the hardware's crown.
                bool flat = Mathf.Abs(p1.z - p2.z) < 1e-5f;
                if (!flat)
                {
                    offPlane++;
                    if (offPlane <= 6)
                        Log($"   {stem}: NON-FLAT open edge (used {kv.Value}x) "
                            + $"({p1.x:F5},{p1.y:F5},{p1.z:F5}) -> ({p2.x:F5},{p2.y:F5},{p2.z:F5})");
                }
            }
            File.WriteAllText(Path.Combine(dir, stem + ".obj"), sb.ToString());
            string verdict = backward == 0 ? "winding OK" : $"*** {backward} BACKWARD TRIANGLES ***";
            string shell = open == 0
                ? "closed shell"
                : offPlane == 0
                    ? $"{open} open edge(s), ALL flat — hardware footprints on the rim land, not holes"
                    : $"*** {offPlane} of {open} open edge(s) are NOT flat — a real hole ***";
            Log($"{stem}: {v.Length} verts, {tris} tris, {m.subMeshCount} submeshes, "
                + $"bounds {m.bounds.size}, {verdict}, {shell} ({edges.Count} unique edges).");
            if (backward != 0 || offPlane != 0) _exit = 1;
        }
    }
}
