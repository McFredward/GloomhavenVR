// GloomhavenVR companion project — control-board (PlayTray) asset assembler.
//
// Batch:
//   Unity -batchmode -projectPath <this> -buildTarget Win64
//         -executeMethod GloomhavenVR.BoardBuilder.Build -logFile board-build.log
//   (run WITH graphics on an X server if you want the preview render; add -nographics
//    to skip the render and only assemble + bundle.)
//
// What it does, deterministically (no hand-authoring in the GUI):
//  1. Imports PlayTray_prepped.fbx (embedded textures), extracts + remaps them,
//     marks the normal map, and sets metres scale.
//  2. Builds a material on the bundled GloomhavenVR/BoardLit shader (albedo + normal).
//  3. Assembles Assets/Bundle/Table/PlayTray.prefab: a "PlayTray" root with the model
//     oriented to the bundle contract — board in local XY, decorated face toward -Z,
//     body z>0 — computed FROM THE ANCHOR AXES (Slot1/Slot2 span the long axis, the
//     rest pads span the short axis, their cross product is the decorated normal), so
//     it is correct regardless of FBX axis quirks. Verifies the SEVEN named anchors:
//     the four frame anchors plus the three button seats ButtonSeat1/2/3, whose legacy
//     spelling ConfirmButton/UndoButton still resolves (see AnchorNames below). Also
//     MEASURES each button recess and each rest pad off the mesh and writes
//     SeatExtent1/2/3 + RestExtentShort/Long into the prefab, which is what lets the
//     mod fit its keycaps to the board they sit on AND keep them inside those seats.
//  4. Optionally renders a viewer-side preview PNG (skipped under -nographics).
//  5. Builds gloomhavenvr.bundle via AssetsBuilder.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class BoardBuilder
    {
        private const string Table = "Assets/Bundle/Table";
        private const string ShaderName = "GloomhavenVR/BoardLit";
        private const string PreviewPng = "board-unity-preview.png";

        // One entry per switchable control board. The runtime picks a prefab by name
        // (CardsConfig.ControlBoard: Oak/Steel/Bronze -> these prefab paths). Each board
        // ships its own prepped FBX + loose albedo/normal PNGs (extracted from the source
        // GLB). All three carry the SAME named anchor set, so the assembly below is
        // board-agnostic. Board A (oak) keeps its original unsuffixed asset names.
        private struct BoardDef
        {
            public string Fbx, Mat, Prefab, Albedo, Normal;
        }

        private static readonly BoardDef[] Boards =
        {
            new BoardDef { Fbx = Table + "/PlayTray_prepped.fbx",   Mat = Table + "/PlayTray.mat",
                           Prefab = Table + "/PlayTray.prefab",     Albedo = Table + "/PlayTray_albedo.png",
                           Normal = Table + "/PlayTray_normal.png" },
            new BoardDef { Fbx = Table + "/PlayTray_9capjqp6.fbx",  Mat = Table + "/PlayTray_9capjqp6.mat",
                           Prefab = Table + "/PlayTray_9capjqp6.prefab", Albedo = Table + "/PlayTray_9capjqp6_albedo.png",
                           Normal = Table + "/PlayTray_9capjqp6_normal.png" },
            new BoardDef { Fbx = Table + "/PlayTray_16vm268h.fbx",  Mat = Table + "/PlayTray_16vm268h.mat",
                           Prefab = Table + "/PlayTray_16vm268h.prefab", Albedo = Table + "/PlayTray_16vm268h_albedo.png",
                           Normal = Table + "/PlayTray_16vm268h_normal.png" },
        };

        // ---- THE ANCHOR NAME TABLE ----------------------------------------------------------
        // A DELIBERATE COPY of src/GloomhavenVR/Cards/BoardAnchors.cs. This file compiles into the
        // Unity companion project, a different assembly that cannot reference the mod, so the table
        // cannot be shared the way the mod's three readers share it. Keep the two in step: the
        // resolver in BoardAnchors.cs is the one the runtime obeys, this one only decides what the
        // assembler VERIFIES and PROJECTS.

        /// The four anchors the board's ORIENTATION FRAME is derived from. Slot1->Slot2 is the long
        /// axis, ShortRestToken->LongRestToken the short one, their cross product the decorated-face
        /// normal. The derivation below reads these four BY NAME and nothing else, so it is
        /// unaffected by how many button seats a board carries.
        private static readonly string[] FrameAnchorNames =
            { "Slot1", "Slot2", "ShortRestToken", "LongRestToken" };

        /// Accepted names per BUTTON SEAT, most preferred first. The boards carry THREE physical
        /// button recesses (user, 2026-08: "3 statt 2 Slots fuer die buttons"); the legacy
        /// ConfirmButton/UndoButton spelling stays accepted forever so an un-regenerated FBX still
        /// assembles, and seat 3 is additive — no old board has one.
        private static readonly string[][] SeatAliases =
        {
            new[] { "ButtonSeat1", "ConfirmButton" },
            new[] { "ButtonSeat2", "UndoButton" },
            new[] { "ButtonSeat3", "SkipButton" },
        };

        /// Every name an anchor empty may carry — what the scan below matches against.
        private static readonly string[] AnchorNames =
            FrameAnchorNames.Concat(SeatAliases.SelectMany(a => a)).ToArray();

        public static void Build()
        {
            try
            {
                AssetDatabase.Refresh();
                GameObject lastPrefab = null;
                foreach (var board in Boards)
                {
                    if (AssetImporter.GetAtPath(board.Fbx) == null)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Board FBX '{board.Fbx}' not in project — skipping (bundle will omit this board).");
                        continue;
                    }
                    Debug.Log($"[GloomhavenVR] === Assembling board: {board.Prefab} ===");
                    ImportModel(board);
                    Material mat = BuildMaterial(board);
                    lastPrefab = AssemblePrefab(board, mat);
                }
                if (lastPrefab != null) TryRenderPreview(lastPrefab);
                AssetsBuilder.BuildAll(); // exits the editor (0/1)
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GloomhavenVR] BoardBuilder FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ImportModel(BoardDef board)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(board.Fbx);
            if (importer == null)
                throw new FileNotFoundException($"Model importer not found for {board.Fbx} — is the FBX in the project?");
            importer.useFileScale = true;          // FBX is authored in metres
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = true;            // prefab/bundle mesh access
            // We supply our own material (BoardLit) + loose PNG textures, so the FBX's
            // embedded materials/textures are not imported (avoids a stray .fbm blob).
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            // The albedo/normal are loose PNGs next to the FBX (extracted from the GLB).
            // Mark the normal map so the shader's tangent-space unpack is correct.
            var nti = (TextureImporter)AssetImporter.GetAtPath(board.Normal);
            if (nti != null && nti.textureType != TextureImporterType.NormalMap)
            {
                nti.textureType = TextureImporterType.NormalMap;
                nti.SaveAndReimport();
            }
        }

        private static Material BuildMaterial(BoardDef board)
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(board.Albedo);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(board.Normal);
            if (albedo == null)
                Debug.LogWarning("[GloomhavenVR] Albedo not found at " + board.Albedo + " — board will be untextured tint.");

            var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(board.Prefab) };
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            if (normal != null) mat.SetTexture("_BumpMap", normal);
            // WATERTIGHT FIX — render the board double-sided (Cull Off). The AI board mesh is
            // fragmented (1064 shells, 20 268 non-manifold edges), so with the default Back
            // culling its many small holes reveal the CULLED interior and, in MR passthrough,
            // the bright green background shows through (offscreen render: ~140 see-through px
            // across 5 POVs). Every hole here has a wall behind it, so Cull Off — with BoardLit's
            // VFACE two-sided lighting flipping the normal so that inner wall is lit, not black —
            // fills every hole with board surface instead of background (verified: 0 see-through
            // px, all 5 POVs). Purely a render-state change: the mesh, the six anchors, the
            // recessed functional face and the MeshCollider the mod raycasts are all untouched.
            mat.SetFloat("_Cull", 0f); // 0 = CullMode.Off
            AssetDatabase.CreateAsset(mat, board.Mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Material built: albedo={(albedo ? albedo.name : "none")}, normal={(normal ? normal.name : "none")}");
            return mat;
        }

        private static GameObject AssemblePrefab(BoardDef board, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(board.Fbx);
            if (model == null) throw new System.Exception($"Failed to load model at {board.Fbx}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Board";

            // --- resolve the anchors (any depth, by name; seats by alias) ---
            // `found` is keyed by the name as AUTHORED; `anchors` by the CANONICAL name, so every
            // step below (orientation, centring, projection, logging) speaks one vocabulary whether
            // the FBX says ButtonSeat1 or ConfirmButton.
            var found = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(AnchorNames, t.name) >= 0 && !found.ContainsKey(t.name))
                    found[t.name] = t;

            var anchors = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (string n in FrameAnchorNames)
            {
                if (found.TryGetValue(n, out Transform ft)) anchors[n] = ft;
                else Debug.LogWarning($"[GloomhavenVR] Frame anchor '{n}' MISSING from the model — orientation, centring and projection all fall back (see below).");
            }

            // The three button seats, in seat order. A null seat is not fatal: seats 0/1 get a
            // procedural anchor at runtime, and seat 2 simply does not exist on a board the mesh
            // lane has not regenerated (every board shipped so far).
            var seats = new Transform[SeatAliases.Length];
            for (int i = 0; i < SeatAliases.Length; i++)
            {
                foreach (string alias in SeatAliases[i])
                {
                    if (!found.TryGetValue(alias, out Transform st)) continue;
                    seats[i] = st;
                    anchors[SeatAliases[i][0]] = st;   // canonical key
                    if (alias != SeatAliases[i][0])
                        Debug.Log($"[GloomhavenVR] Button seat {i} resolved under the LEGACY name '{alias}' (canonical: '{SeatAliases[i][0]}').");
                    break;
                }
                if (seats[i] == null)
                    Debug.LogWarning($"[GloomhavenVR] Button seat {i} MISSING — the model carries none of {string.Join("/", SeatAliases[i])}. "
                                     + (i < 2 ? "The mod will synthesize a procedural anchor for it."
                                              : "The board will run as a TWO-seat one; regenerate the FBX with a third button recess to give it three."));
            }
            int seatCount = seats.Count(t => t != null);
            Debug.Log($"[GloomhavenVR] Anchors resolved: {anchors.Count} of {FrameAnchorNames.Length + SeatAliases.Length} "
                      + $"({FrameAnchorNames.Count(n => anchors.ContainsKey(n))} frame + {seatCount} button seat(s)).");

            // --- deterministic orientation from the anchor frame ---
            // Slot1->Slot2 = board long (X) axis; rest pads span the short (Y) axis;
            // their cross product is the DECORATED-face normal (from the prep, +Z in
            // Blender). Contract target: long->+X, decorated normal->-Z (toward the
            // viewer, HMD on -Z). A pure rotation then sends the short axis to -Y.
            if (anchors.ContainsKey("Slot1") && anchors.ContainsKey("Slot2")
                && anchors.ContainsKey("ShortRestToken") && anchors.ContainsKey("LongRestToken"))
            {
                Vector3 u = (anchors["Slot2"].position - anchors["Slot1"].position).normalized;      // long axis
                Vector3 s = (anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized; // short axis
                Vector3 n = Vector3.Cross(u, s).normalized;                                           // decorated normal
                // Re-orthogonalize s against u (guard against non-perpendicular anchors).
                Vector3 v = Vector3.Cross(n, u).normalized;

                // n = cross(u,v) points out the UNDECORATED back (verified by render):
                // the decorated face (slots/pads) is -n, and the contract wants it
                // toward -Z (the viewer). So map n -> +Z, which puts -n (decorated) -> -Z.
                // (u -> +X keeps the rest zone left / buttons right; v -> +Y then follows
                // as a proper rotation.)
                var src = new Matrix4x4();
                src.SetColumn(0, u); src.SetColumn(1, v); src.SetColumn(2, n); src.SetColumn(3, new Vector4(0,0,0,1));
                var dst = new Matrix4x4();
                dst.SetColumn(0, Vector3.right); dst.SetColumn(1, Vector3.up); dst.SetColumn(2, Vector3.forward);
                dst.SetColumn(3, new Vector4(0,0,0,1));
                Quaternion rot = (dst * src.transpose).rotation;
                inst.transform.rotation = rot;

                // GUARANTEE the anchor plane is the local XY plane with the decorated
                // face toward -Z (the viewer). The matrix above can leave a residual 90°
                // (the FBX Y-up import lands the plane in XZ, normal along Y) which made
                // cards stand UPRIGHT on the board instead of lying flat. Snap the
                // decorated-back normal exactly onto +Z; the shortest-arc rotation keeps
                // the long axis (Slot1->Slot2) in place.
                Vector3 nCur = Vector3.Cross(
                    anchors["Slot2"].position - anchors["Slot1"].position,
                    anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized;
                inst.transform.rotation = Quaternion.FromToRotation(nCur, Vector3.forward) * inst.transform.rotation;
            }
            else
            {
                Debug.LogWarning("[GloomhavenVR] Slot/rest anchors missing — falling back to Euler(90,0,0) orientation.");
                inst.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // --- centre the anchor plane at local z=0, body toward +z, XY-centred ---
            // COUNT-INDEPENDENT ON PURPOSE — see AnchorPlaneCentre. The old form averaged EVERY
            // resolved anchor, so adding a third button recess would have moved the prefab origin
            // (and with it the whole board relative to every _root-anchored mount) by a few
            // millimetres, silently, as a side effect of an unrelated addition.
            Vector3 planeCentre = AnchorPlaneCentre(anchors, seats);
            inst.transform.position -= planeCentre; // anchor-plane centre -> origin

            // If the solid body ended up in FRONT of the cards (min z < 0 well past the
            // anchor plane), flip 180° about Y so the body sits behind (z>0) and the
            // decorated face still points -Z.
            Bounds b = LocalBounds(inst);
            if (b.center.z < -0.002f)
            {
                inst.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * inst.transform.rotation;
                inst.transform.position = Vector3.zero;
                inst.transform.position -= AnchorPlaneCentre(anchors, seats);
                b = LocalBounds(inst);
                Debug.Log("[GloomhavenVR] Body was in front of the cards — flipped 180° about Y.");
            }

            // --- board collider (DEFECT 2) ---
            // The bundle visual ships NO collider, so the mod's index-finger laser
            // passed straight THROUGH the board. Give the mesh a MeshCollider
            // (non-convex: it must follow the RECESSED functional face, not a filled
            // hull) so the laser (Collider.Raycast — geometric, layer-independent)
            // stops on the real surface; it also lets us project the anchors onto that
            // surface just below. Static prop → non-convex is correct.
            var meshColliders = new System.Collections.Generic.List<MeshCollider>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                meshColliders.Add(mc);
            }
            Debug.Log($"[GloomhavenVR] Board MeshCollider(s) added: {meshColliders.Count}.");

            // --- seat the anchors ON the functional recess surface (DEFECT 1) ---
            // The bundle anchors sit ~2 cm BEHIND the recessed functional face (toward
            // the compass back), so cards/buttons parked at an anchor landed deep and
            // were only visible from the back. For each anchor, raycast from just in
            // FRONT of the functional face back toward the board and move the anchor
            // onto the first surface hit (a hair proud) — so every attached element
            // (cards, rest tokens, CONFIRM/UNDO) seats IN its recess / on its pad,
            // flush on the player-facing face. Generalises to any board with anchors.
            if (meshColliders.Count > 0 && anchors.ContainsKey("Slot1") && anchors.ContainsKey("Slot2")
                && anchors.ContainsKey("ShortRestToken") && anchors.ContainsKey("LongRestToken"))
            {
                Physics.SyncTransforms(); // ensure the just-added colliders match the posed transforms
                // Functional-face outward normal in WORLD space from the CURRENT anchor
                // frame (robust to the orientation/flip branches above): n=cross(long,
                // short) is the UNDECORATED back normal, so -n points out the functional
                // face (toward the player).
                Vector3 backN = Vector3.Cross(
                    anchors["Slot2"].position - anchors["Slot1"].position,
                    anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized;
                Vector3 outN = -backN;
                const float standoff = 0.15f;    // start well in front of the face
                const float proud = 0.0005f;      // seat a hair above the recess floor
                // Projection is a SMALL refinement that seats an anchor onto its recess
                // floor (e.g. cards sink into a slot). It is only trustworthy as a small
                // correction: the prep already places every anchor on the decorated-face
                // plane, so a big move means the ray hit an unrelated raised ornament or a
                // deep back protrusion (the 16vm268h board is a chunky 3D object, not a
                // flat plate) — that would float the element centimetres proud. Reject any
                // move beyond MaxProject and keep the authored face-plane position instead.
                const float maxProject = 0.045f; // accept up to 45 mm (board A ≤28 mm, 9capjqp6 slots ~25 mm)
                // EVERY AUTHORED ANCHOR EMPTY, aliases included — `found`, not `anchors`. The
                // re-authored FBXes ship both spellings of every seat at bit-identical positions so
                // one asset works with every DLL; projecting only the canonical one would have left
                // ConfirmButton/UndoButton a few millimetres off the recess floor, so an older DLL
                // (which resolves only those names) would seat its keycaps proud of the seat the
                // new DLL seats them in. Same anchor, same treatment.
                foreach (var kv in found)
                {
                    string name = kv.Key;
                    Transform a = kv.Value;
                    var ray = new Ray(a.position + outN * standoff, backN); // shoot back toward the board
                    float best = float.PositiveInfinity;
                    Vector3 bestPt = default;
                    foreach (var mc in meshColliders)
                        if (mc.Raycast(ray, out RaycastHit hit, standoff * 2f) && hit.distance < best)
                        { best = hit.distance; bestPt = hit.point; }
                    if (float.IsInfinity(best))
                    {
                        Debug.LogWarning($"[GloomhavenVR] Anchor '{name}' did not project (no mesh hit) — kept on the authored face plane.");
                        continue;
                    }
                    Vector3 target = bestPt + outN * proud;
                    float move = (target - a.position).magnitude;
                    if (move > maxProject)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Anchor '{name}' projection {move * 1000f:F1} mm exceeds {maxProject * 1000f:F0} mm (irregular geometry) — kept on the authored face plane.");
                        continue;
                    }
                    a.position = target;
                    Debug.Log($"[GloomhavenVR]   anchor {name} projected: moved {move * 1000f:F1} mm onto the functional face.");
                }
            }

            // --- MEASURE each button recess off the mesh (for the mod's cap fit) ---
            // The mod sizes its keycaps from ONE global tuned pair ([BoardButtons] Width/Height,
            // 0.063 x 0.065 m — the bound default, which is what his cfg holds) that the user dialled
            // in himself, but the three boards cut their button recesses at three different sizes and
            // the 65 mm height fits none of them. Lowering
            // the global would make every board wear the smallest board's cap and would re-seat a
            // hand-tuned value as a side effect of an asset change; so the cap is FITTED per board
            // instead, and the fit needs to know how big this board's recess actually is.
            //
            // WHY MEASURED HERE rather than baked as three pairs of constants in the mod: the
            // numbers are a property of the FBX, and the FBX changes without the code. The figures
            // this round was handed for the three recesses turned out to describe the top RIM while
            // a keycap sits on the FLOOR (Bronze: 71.6 mm quoted, 61.2 mm of actual floor) — a
            // second-hand geometry number went stale before it was written down. The ray-caster
            // that projects the anchors is already here and already pointed at the right surface.
            //
            // EXPECTED OUTPUT, so this instrument can be checked rather than believed. Measured on
            // the three committed FBXes with the identical walk (Blender, 0.25 mm plateau
            // tolerance, bisected): button seats Oak 0.0746 x 0.0643, Steel 0.0810 x 0.0701,
            // Bronze 0.0612 x 0.0519 m; rest pads Oak 0.0816, Steel 0.0817, Bronze 0.0681 m square.
            // If the log below disagrees with those, THIS CODE is wrong, not the boards.
            var seatHalf = new Vector2[SeatAliases.Length];
            // …AND THE TWO REST PADS, by the same walk. The rest discs sit in an authored pad exactly
            // the way the keycaps sit in an authored recess, and RestButtonOffset_{board} carries the
            // very same mirror compensation the button offsets do (Steel -0.44, Bronze -0.445). One
            // measurement mechanism, one clamp, one argument. Measured floors on the three committed
            // FBXes: Oak 81.6, Steel 81.7, Bronze 68.1 mm — against tuned disc diameters of 91 / 71 /
            // 71 mm, so Oak's disc overhangs its pad by 4.7 mm a side and Bronze's by 1.5 mm.
            var restHalf = new System.Collections.Generic.Dictionary<string, Vector2>();
            if (meshColliders.Count > 0 && anchors.ContainsKey("Slot1") && anchors.ContainsKey("Slot2")
                && anchors.ContainsKey("ShortRestToken") && anchors.ContainsKey("LongRestToken"))
            {
                Physics.SyncTransforms();
                Vector3 backN2 = Vector3.Cross(
                    anchors["Slot2"].position - anchors["Slot1"].position,
                    anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized;
                Vector3 outN2 = -backN2;
                Vector3 uAxis = (anchors["Slot2"].position - anchors["Slot1"].position).normalized;
                Vector3 vAxis = Vector3.Cross(backN2, uAxis).normalized;
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    Vector3 c = seats[i].position;
                    // THE FLOOR REFERENCE IS THE SURFACE UNDER THE SEAT, NOT THE ANCHOR'S OWN DEPTH.
                    // The projection above deliberately parks each anchor `proud` (0.5 mm) ABOVE the
                    // face it landed on, and it also SKIPS anchors whose projection was out of band —
                    // so the anchor's depth is 0.5 mm off the floor at best and arbitrary at worst.
                    // Reading the surface back at the seat centre makes this measurement independent
                    // of what the projection did, which is the whole point of measuring.
                    float? floorAt = SurfaceDepth(meshColliders, c, outN2, backN2);
                    if (floorAt == null)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Button seat {i} recess floor did not raycast — NOT written.");
                        continue;
                    }
                    float floor = floorAt.Value;
                    float hx = Mathf.Min(FloorRun(meshColliders, c, uAxis, outN2, backN2, floor),
                                         FloorRun(meshColliders, c, -uAxis, outN2, backN2, floor));
                    float hy = Mathf.Min(FloorRun(meshColliders, c, vAxis, outN2, backN2, floor),
                                         FloorRun(meshColliders, c, -vAxis, outN2, backN2, floor));
                    if (hx < 0.005f || hy < 0.005f || hx > 0.100f || hy > 0.100f)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Button seat {i} recess measured "
                            + $"{hx * 2000f:F1} x {hy * 2000f:F1} mm — out of band, NOT written. The mod "
                            + "will keep the tuned [BoardButtons] cap size on this board.");
                        continue;
                    }
                    seatHalf[i] = new Vector2(hx, hy);
                    Debug.Log($"[GloomhavenVR]   button seat {i} recess floor {hx * 2000f:F1} x {hy * 2000f:F1} mm.");
                }

                foreach (var pad in new[] { ("ShortRestToken", "RestExtentShort"),
                                            ("LongRestToken", "RestExtentLong") })
                {
                    if (!anchors.TryGetValue(pad.Item1, out Transform pt)) continue;
                    Vector3 pc = pt.position;
                    float? padFloor = SurfaceDepth(meshColliders, pc, outN2, backN2);
                    if (padFloor == null)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Rest pad '{pad.Item1}' floor did not raycast — NOT written.");
                        continue;
                    }
                    float px = Mathf.Min(FloorRun(meshColliders, pc, uAxis, outN2, backN2, padFloor.Value),
                                         FloorRun(meshColliders, pc, -uAxis, outN2, backN2, padFloor.Value));
                    float py = Mathf.Min(FloorRun(meshColliders, pc, vAxis, outN2, backN2, padFloor.Value),
                                         FloorRun(meshColliders, pc, -vAxis, outN2, backN2, padFloor.Value));
                    if (px < 0.005f || py < 0.005f || px > 0.100f || py > 0.100f)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Rest pad '{pad.Item1}' measured "
                            + $"{px * 2000f:F1} x {py * 2000f:F1} mm — out of band, NOT written. The mod will "
                            + "keep the tuned RestButtonDiameter and leave its offset unclamped.");
                        continue;
                    }
                    restHalf[pad.Item2] = new Vector2(px, py);
                    Debug.Log($"[GloomhavenVR]   rest pad {pad.Item1} floor {px * 2000f:F1} x {py * 2000f:F1} mm.");
                }
            }
            else
            {
                Debug.LogWarning("[GloomhavenVR] Seat recesses NOT measured (no collider or no frame anchors) — "
                                 + "the mod will keep the tuned [BoardButtons] cap size on this board.");
            }

            // --- material onto every renderer ---
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            // --- wrap under the contract root "PlayTray" ---
            var root = new GameObject("PlayTray");
            inst.transform.SetParent(root.transform, worldPositionStays: true);

            // --- carry the measurement into the prefab ---
            // SeatExtentN's localPosition is NOT a position: its x and y are the recess's HALF-WIDTH
            // and HALF-HEIGHT in metres (x along the board's long axis, y along its short one, which
            // is the basis the keycap's rectSize is expressed in on both the local board and a
            // peer's mirrored copy). An empty is the carrier because the mod cannot ship a
            // MonoBehaviour into this bundle — the mod assembly is not part of the Unity project —
            // and because a child of the prefab can never go out of sync with the board it
            // describes. `root` is at the origin with an identity rotation here, so these two
            // numbers land in the prefab exactly as measured.
            for (int i = 0; i < seatHalf.Length; i++)
            {
                if (seatHalf[i] == Vector2.zero) continue;
                var se = new GameObject($"SeatExtent{i + 1}").transform;
                se.SetParent(root.transform, false);
                se.localPosition = new Vector3(seatHalf[i].x, seatHalf[i].y, 0f);
            }
            foreach (var kv in restHalf)
            {
                var re = new GameObject(kv.Key).transform;
                re.SetParent(root.transform, false);
                re.localPosition = new Vector3(kv.Value.x, kv.Value.y, 0f);
            }

            // Log the resolved geometry so orientation is verifiable from the log alone.
            Debug.Log($"[GloomhavenVR] Board bounds (local): center={b.center}, size={b.size}");
            foreach (var kv in anchors)
                Debug.Log($"[GloomhavenVR]   anchor {kv.Key} local = {root.transform.InverseTransformPoint(kv.Value.position)}");

            var saved = PrefabUtility.SaveAsPrefabAsset(root, board.Prefab, out bool ok);
            if (!ok) throw new System.Exception($"SaveAsPrefabAsset failed for {board.Prefab}");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Prefab written: {board.Prefab}");
            return saved;
        }

        /// <summary>
        /// The point moved to the prefab origin: the mean of a FIXED SIX-TERM SET — the four frame
        /// anchors plus the FIRST and LAST button seat.
        ///
        /// <para>WHY NOT "the mean of every anchor", which is what this used to be. That form makes
        /// the prefab origin a function of HOW MANY anchors the FBX happens to carry, so the moment a
        /// board gains a third button recess the whole mesh — and every anchor on it, and therefore
        /// every card, keycap and rest disc parked on one — shifts a few millimetres against the
        /// board root and against the mounts that are direct root children (DecisionMount, PileMount,
        /// the handle bar, the native-widget docks at the hardcoded ButtonZoneX). That is a silent
        /// retune of hand-dialled per-board geometry caused by an addition that has nothing to do
        /// with it, and it would have been invisible in the log.</para>
        ///
        /// <para>WHY NOT "the four frame anchors" either, which looks like the tidy answer: those four
        /// sit on the card slots and the rest pads, all on ONE side of the board's long axis, so on
        /// the oak board their centroid is 126 mm off the six-anchor one. Every board in the shipped
        /// bundle was assembled against the six-term value; reproducing it exactly for a two-seat
        /// board is the requirement, and taking the first and last seat does that by construction
        /// (with two seats, first and last ARE the two, and the mean of six terms is the mean of the
        /// six anchors). With three seats it reads the button column's outer pair — the same
        /// quantity, measured the same way.</para>
        ///
        /// <para>Degrades to the mean of whatever resolved when a term is missing, so a half-authored
        /// FBX still assembles instead of throwing.</para>
        /// </summary>
        private static Vector3 AnchorPlaneCentre(
            System.Collections.Generic.Dictionary<string, Transform> anchors, Transform[] seats)
        {
            var terms = new System.Collections.Generic.List<Vector3>(6);
            foreach (string n in FrameAnchorNames)
                if (anchors.TryGetValue(n, out Transform t)) terms.Add(t.position);

            Transform first = seats.FirstOrDefault(t => t != null);
            Transform last = seats.LastOrDefault(t => t != null);
            if (first != null) terms.Add(first.position);
            if (last != null) terms.Add(last.position);

            if (terms.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (Vector3 v in terms) sum += v;
            return sum / terms.Count;
        }

        /// <summary>
        /// The functional-face surface depth (along <paramref name="outN"/>) directly at
        /// <paramref name="p"/>, or null if the ray misses the board — the same standoff-and-shoot-back
        /// ray the anchor projection uses, so it reads the same surface.
        /// </summary>
        private static float? SurfaceDepth(System.Collections.Generic.List<MeshCollider> colliders,
                                           Vector3 p, Vector3 outN, Vector3 backN)
        {
            const float standoff = 0.15f;
            var ray = new Ray(p + outN * standoff, backN);
            float best = float.PositiveInfinity;
            Vector3 bestPt = default;
            foreach (var mc in colliders)
                if (mc.Raycast(ray, out RaycastHit hit, standoff * 2f) && hit.distance < best)
                { best = hit.distance; bestPt = hit.point; }
            return float.IsInfinity(best) ? (float?)null : Vector3.Dot(bestPt, outN);
        }

        /// <summary>
        /// How far the RECESS FLOOR runs from <paramref name="centre"/> in direction
        /// <paramref name="dir"/>, in metres: walk outward sampling the surface (the same
        /// standoff-and-shoot-back ray the anchor projection uses) and stop where the surface leaves
        /// the floor plane, then bisect.
        ///
        /// <para>The FLOOR, deliberately, and not the rim. A keycap is a solid block that rests in
        /// the well; a cap wider than the floor has its lower corners inside the chamfered wall.
        /// Measuring at the rim instead reports 2-10 mm more room than a cap can actually use
        /// (Bronze: 71.6 mm at the rim, 61.2 mm of floor), which is the difference between a cap
        /// that seats and one that perches.</para>
        /// </summary>
        private static float FloorRun(System.Collections.Generic.List<MeshCollider> colliders,
                                      Vector3 centre, Vector3 dir, Vector3 outN, Vector3 backN,
                                      float floorDepth)
        {
            const float tol = 0.00025f;   // 0.25 mm — the authored floors are flat to far better
            const float step = 0.0005f;   // 0.5 mm coarse walk
            const float limit = 0.080f;   // no button recess on a 0.32 m board is wider than this

            bool OnFloor(float d)
            {
                float? z = SurfaceDepth(colliders, centre + dir * d, outN, backN);
                return z != null && Mathf.Abs(z.Value - floorDepth) <= tol;
            }

            float lo = 0f, hi = 0f;
            bool bounded = false;
            while (hi < limit)
            {
                hi += step;
                if (!OnFloor(hi)) { bounded = true; break; }
                lo = hi;
            }
            // Never found a wall inside the limit: the ray is not reading a recess at all (a flat
            // pad, a board with no seat cut, a degenerate normal). Report 0 so the caller's band
            // check rejects it and the mod keeps the tuned cap size, rather than returning `limit`
            // and quietly declaring an 160 mm seat.
            if (!bounded)
                return 0f;
            for (int i = 0; i < 18; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (OnFloor(mid)) lo = mid; else hi = mid;
            }
            return lo;
        }

        private static Bounds LocalBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            // world bounds -> local (go has the rotation applied; approximate with world here)
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center = go.transform.InverseTransformPoint(b.center);
            return b;
        }

        private static void TryRenderPreview(GameObject prefab)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[GloomhavenVR] No graphics device (-nographics) — skipping preview render.");
                return;
            }
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                // Tilt like the mod does (TrayTilt ~30°, -Z up toward the viewer).
                inst.transform.rotation = Quaternion.Euler(-60f, 0f, 0f);
                inst.transform.position = Vector3.zero;

                var lightGo = new GameObject("L"); var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.45f);

                var camGo = new GameObject("C"); var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.14f, 0.14f, 0.16f);
                cam.transform.position = new Vector3(0f, 0.05f, -0.75f); // viewer side (-Z)
                cam.transform.LookAt(new Vector3(0f, 0f, 0.05f));
                cam.fieldOfView = 40f;

                var rt = new RenderTexture(900, 700, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply();
                File.WriteAllBytes(PreviewPng, tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null;
                Object.DestroyImmediate(inst); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
                Debug.Log($"[GloomhavenVR] Preview render written: {Path.GetFullPath(PreviewPng)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GloomhavenVR] Preview render skipped ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
