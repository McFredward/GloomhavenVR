// GloomhavenVR companion project — THE IN-HAND CARD GRIP, rendered.
//
//   CARDGRIP_PREVIEW_OUT=/somewhere xvfb-run -a \
//     Unity -batchmode -projectPath <this> -buildTarget Win64 \
//           -executeMethod GloomhavenVR.CardGripPreview.RenderAll -logFile cardgrip-preview.log -quit
//   (needs a graphics device — do NOT pass -nographics.)
//
// TOUCH THE SYMLINK FIRST. ALWAYS:
//
//   touch -h Assets/Editor/CardGripPoseLink.cs
//
// UNITY DOES NOT REIMPORT A SYMLINKED SOURCE FILE when only its TARGET changes — the link's own
// mtime never moves, so the asset database sees nothing and rebuilds nothing, and the run compiles
// the PREVIOUS version of CardGripPose.cs while reporting complete success. That cost two full
// rounds on the grab rods (see PreviewGrabBar.cs, which carries the same warning for the same
// reason). The tell, if it happens again: the [CardGripPreview] POSE line below prints the OLD
// pitch and the OLD curls. That line exists for this.
//
// WHAT THIS IS FOR (user, 2026-08-29: "Modellier dafür für jede Hand auch eine ideale
// Greifposition, die die Karte physisch greift an dem unteren Rand der Karte ohne zu viel zu
// verdecken. Render dir die Ergebnisse um es zu checken."). Two claims have to be settled by
// LOOKING, because neither is decidable from the numbers:
//
//   1. do the fingers close on the card's BOTTOM EDGE — and the shipped card face has a border
//      there, so the strip the grip must stay inside is drawn on the preview card in a
//      contrasting colour; and
//   2. how much of the card does the hand COVER, from the side somebody is being shown it.
//
// So the face view is shot straight down the card's own face normal: it is literally the picture
// the other player gets.
//
// WHAT MAKES IT WORTH TRUSTING, AND WHERE IT STOPS.
//
//  * THE POSE IS THE SHIPPED POSE. Assets/Editor/CardGripPoseLink.cs is a SYMLINK to
//    src/GloomhavenVR/Cards/CardGripPose.cs, so the card below is placed by the very method the
//    game places it with — not by a re-typed copy. (That is also why that one file is written
//    against C# 9 with a block namespace: Unity 2021.3 cannot compile the file-scoped form.)
//  * THE HAND IS THE SHIPPED HAND. The three style prefabs are loaded through AssetDatabase and
//    drawn through their own materials, so the mesh, the weights and the shader are the game's.
//  * THE CURL ARITHMETIC IS REPRODUCED, NOT SHARED, and that is the one seam. FingerCurler.cs
//    cannot be symlinked in — it reaches HandsConfig, Defaults and BepInEx — so the three lines it
//    applies are restated in Curl() below against its DefaultFingerMaxAngles / DefaultThumbMaxAngles
//    (75/95/65 and 25/45/60). unity/hand-prep/curl_check.py restates the same three lines for the
//    same reason and says so. Consequence, stated so nobody trusts this further than it goes: a
//    player who has RETUNED [Hands] CurlProximal/Middle/Tip gets a different fist than this picture
//    shows. The curl VALUES (what the grip is) come from the symlinked file and cannot drift; only
//    the degrees-per-unit-curl (how far a full curl bends) is restated here.
//  * PREVIEWS COME OUT BRIGHTER THAN THE HEADSET. Relative and structural claims ("the fingers are
//    inside the grip band", "the middle finger is not behind the art") are settled here; absolute
//    levels are not.
//  * IT CANNOT SHOW THE MODE MACHINE. Whether the grip is entered, held and left correctly is
//    HeldCardGrip's business and only the rig can confirm it.

using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class CardGripPreview
    {
        // FingerCurler.DefaultFingerMaxAngles / DefaultThumbMaxAngles — the degrees a FULL curl
        // bends each of a digit's three joints. Restated, not shared; see the header.
        private static readonly Vector3 FingerMaxAngles = new(75f, 95f, 65f);
        private static readonly Vector3 ThumbMaxAngles = new(25f, 45f, 60f);

        // Defaults.CardWidth (0.0635 m), CardsConfig.CardHeight's 63.5:88 aspect, and
        // Defaults.InspectScale (1.6) — the size a card really is while it is held.
        private const float CardWidth = 0.0635f;
        private const float CardHeight = CardWidth * (88f / 63.5f);
        private const float InspectScale = 1.6f;

        private const int Res = 900;
        private const int FaceTexPx = 256;

        // THE SHIPPED PER-STYLE VISUAL SCALE ([Hands] GloveScale/PlateScale/ArcaneScale;
        // Defaults.Plugin.cs 1.12 / 0.62 / 0.62). Without it the plate and arcane gauntlets render
        // at their raw authored size — 1.6x the glove — and the card looks small beside them, which
        // is a picture of nothing the player ever sees.
        //
        // THE CARD DOES NOT SHRINK WITH THE HAND, and that is not an omission here either: at
        // runtime HandVisuals.ApplyStyleScale scales the whole hand subtree and then COUNTER-scales
        // the three attachment sockets, so a card hanging off the grab anchor keeps its own world
        // size. Building the card's world pose from the anchor's position and ROTATION only — never
        // its scale — reproduces exactly that.
        // The tuple's index is also the Hands.HandStyle ordinal (0 Glove, 1 Plate, 2 Arcane), which
        // is what CardGripPose.ThumbCurlByStyle is indexed by. Kept in that order deliberately.
        private static readonly (string Style, string Left, string Right, float Scale)[] Hands =
        {
            ("glove",  "Assets/Bundle/Hands/VRHand_L.prefab",
                       "Assets/Bundle/Hands/VRHand_R.prefab", 1.12f),
            ("plate",  "Assets/Bundle/Hands/VRHandPlate_L.prefab",
                       "Assets/Bundle/Hands/VRHandPlate_R.prefab", 0.62f),
            ("arcane", "Assets/Bundle/Hands/VRHandArcane_L.prefab",
                       "Assets/Bundle/Hands/VRHandArcane_R.prefab", 0.62f),
        };

        // The four views, as directions in the CARD's own frame. The card's readable face points at
        // its -Z (the project-wide convention), so "face" looks back along +Z from in front of it.
        private static readonly (string Name, Vector3 CardDir)[] Views =
        {
            ("face",    new Vector3(0f, 0f, -1f)),
            ("back",    new Vector3(0f, 0f, 1f)),
            // Not EXACTLY edge-on: the card is two single-sided quads, so a camera dead on its X
            // axis sees nothing at all and the shot reads as "no card" rather than as a profile.
            // Nudged off the plane, the card is a sliver and the pinch is legible in profile —
            // which is the view that actually answers "do the fingers close ON it".
            ("edge",    new Vector3(1f, 0.05f, -0.24f)),
            ("quarter", new Vector3(-0.62f, 0.30f, -0.72f)),
        };

        /// <summary>
        /// THUMB-CURL OVERRIDE, for looking at a FAMILY rather than a value.
        ///
        /// <para>"The thumb looks unnatural" is not a claim any single number settles — the splay
        /// check says the flexion axis is fine, and the pose numbers say the sandwich is fine, and
        /// the thumb still reads wrong on two of the three gauntlets. The only instrument for that
        /// is a row of renders at different curls, side by side, so set CARDGRIP_THUMB to a value
        /// and the station poses the thumb at it instead of at the shipped
        /// <c>CardGripPose.Curls[0]</c>. Everything else — including the card, which is placed off
        /// the LIVE thumb — follows, so each frame in the row is a complete, self-consistent
        /// hold rather than the same card with a different thumb drawn near it.</para>
        ///
        /// <para>NaN (unset) means "use the shipped value", which is what every normal run does.</para>
        /// </summary>
        private static float ThumbOverride()
        {
            string v = System.Environment.GetEnvironmentVariable("CARDGRIP_THUMB");
            return !string.IsNullOrEmpty(v)
                   && float.TryParse(v, System.Globalization.NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out float f)
                ? f
                : float.NaN;
        }

        private static float Curl(int finger, int style)
        {
            float over = ThumbOverride();
            return finger == 0 && !float.IsNaN(over)
                ? over
                : Cards.CardGripPose.CurlFor(finger, style);
        }

        public static void RenderAll()
        {
            string outDir = System.Environment.GetEnvironmentVariable("CARDGRIP_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.GetFullPath(Path.Combine("..", "..", ".planning", "debug", "cardgrip"));
            Directory.CreateDirectory(outDir);
            Debug.Log("[CardGripPreview] out = " + outDir);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[CardGripPreview] POSE: pitch {0:F1} deg, grip fraction {1:F3}, thumb per style "
                + "(glove/plate/arcane) {2}, fingers I{3:F2} M{4:F2} R{5:F2} P{6:F2} — read from the "
                + "SYMLINKED CardGripPose.cs. If these are not the values you just edited, the "
                + "symlink was not touched.",
                Cards.CardGripPose.DefaultPitchDegrees, Cards.CardGripPose.GripFraction,
                string.Join(" / ", System.Array.ConvertAll(
                    Cards.CardGripPose.ThumbCurlByStyle,
                    v => v.ToString("F2", CultureInfo.InvariantCulture))),
                Curl(1, 0), Curl(2, 0), Curl(3, 0), Curl(4, 0)));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);

            Texture2D face = BuildFace();
            Texture2D back = BuildBack();
            try
            {
                for (int i = 0; i < Hands.Length; i++)
                {
                    var (style, left, right, scale) = Hands[i];
                    Shoot(style + "_left", left, scale, i, face, back, outDir);
                    Shoot(style + "_right", right, scale, i, face, back, outDir);
                }
            }
            finally
            {
                Object.DestroyImmediate(face);
                Object.DestroyImmediate(back);
            }
            Debug.Log("[CardGripPreview] done.");
        }

        private static void Shoot(string tag, string prefabPath, float styleScale, int style,
                                  Texture2D face, Texture2D back, string outDir)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[CardGripPreview] prefab not found: " + prefabPath);
                return;
            }

            GameObject hand = Object.Instantiate(prefab);
            GameObject card = null;
            var camGo = new GameObject("Cam");
            var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32);
            try
            {
                hand.transform.position = Vector3.zero;
                hand.transform.rotation = Quaternion.identity;
                hand.transform.localScale = Vector3.one * styleScale;

                Transform anchor = Find(hand.transform, "Anchor_Grab");
                if (anchor == null)
                {
                    Debug.LogError("[CardGripPreview] " + tag + ": no Anchor_Grab in the prefab.");
                    return;
                }

                // THE MODELLED GRIP, applied exactly as FingerCurler applies it.
                string[] digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
                for (int f = 0; f < digits.Length; f++)
                    Bend(hand.transform, digits[f], Curl(f, style),
                         f == 0 ? ThumbMaxAngles : FingerMaxAngles);

                // THE PINCH POINT, sampled off the POSED fingertips — the same midpoint the runtime
                // samples (HeldCardGrip.TryPose). Expressed with a ROTATION-ONLY inverse rather than
                // InverseTransformPoint on purpose: the runtime's anchor is a scale-normalized
                // socket, so anchor-local there IS real metres, and dividing this prefab's own
                // import scale back out here would silently change the units the pose is solved in.
                Transform thumbTip = Find(hand.transform, "Anchor_Thumb_Tip");
                Transform indexTip = Find(hand.transform, "Anchor_Index_Mid");
                if (thumbTip == null || indexTip == null)
                {
                    Debug.LogError("[CardGripPreview] " + tag + ": no thumb tip / index knuckle.");
                    return;
                }
                Vector3 pinchWorld = (thumbTip.position + indexTip.position) * 0.5f;
                Vector3 pinchLocal = Quaternion.Inverse(anchor.rotation)
                                     * (pinchWorld - anchor.position);

                // +1 right / -1 left, and MEASURED rather than taken from the file name: the
                // thumb's own lateral sign in the anchor frame is the fact the pose depends on, so
                // a prefab that disagrees with its name is named here instead of quietly rendering
                // the card's BACK.
                float thumbSide = tag.Contains("_right") ? 1f : -1f;
                float measuredSide = Mathf.Sign((Quaternion.Inverse(anchor.rotation)
                                                 * (thumbTip.position - anchor.position)).x);
                if (!Mathf.Approximately(thumbSide, measuredSide))
                    Debug.LogError("[CardGripPreview] " + tag + ": the thumb sits on the "
                        + (measuredSide > 0f ? "+X" : "-X") + " side of the grab anchor, which is "
                        + "not what this hand's name says. The card would show its BACK.");

                float cardH = CardHeight * InspectScale;
                Cards.CardGripPose.Solve(Cards.CardGripPose.DefaultPitchDegrees, thumbSide,
                                         pinchLocal, CardWidth * InspectScale, cardH,
                                         out Vector3 localPos, out Quaternion localRot);

                Quaternion cardRot = anchor.rotation * localRot;
                Vector3 cardPos = anchor.position + anchor.rotation * localPos;
                card = BuildCard(cardPos, cardRot, CardWidth * InspectScale, cardH, face, back);

                // MEASUREMENTS, not decoration. A scale surprise on an imported prefab shows up as
                // a pinch gap of the wrong order, and a grip that has crept off the card's bottom
                // edge shows up as a fraction outside [0, GripFraction].
                float gap = Vector3.Distance(thumbTip.position, indexTip.position);
                Vector3 pinchAxis = Quaternion.Inverse(anchor.rotation)
                                    * (indexTip.position - thumbTip.position).normalized;
                float axisVsNormal = Vector3.Angle(pinchAxis, localRot * Vector3.forward);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[CardGripPreview] {0}: thumb-index gap {1:F4} m, pinch anchor-local {2}, "
                    + "card {3:F4} x {4:F4} m at {5}, hand span {6:F4} m, pinch AXIS (anchor-local) "
                    + "{7} = {8:F1} deg off the card normal",
                    tag, gap, pinchLocal.ToString("F4"), CardWidth * InspectScale, cardH,
                    cardPos.ToString("F4"), Content(hand).size.magnitude,
                    pinchAxis.ToString("F3"), axisVsNormal));

                // CLIPPING, AS A NUMBER. The user's report on the first pose was "in deinen Bildern
                // clippen Finger durch die Karte", and an impression of a render is a bad instrument
                // for that: a finger can pass a millimetre behind the card and read as through it,
                // or pierce it where the shot cannot see. So every driven joint of every digit is
                // tested against the card RECTANGLE — signed distance to the plane, whether its
                // footprint lands inside the card at all, and which SIDE it is on (see Report).
                //
                // JOINTS, NOT THE MESH, and the difference is stated so nobody over-trusts this: the
                // skinned finger is a tube around the bone, so the test carries a radius rather than
                // pretending the bone is the finger. It cannot see a knuckle bulge. What it does see
                // is the class of defect that was there — a whole digit standing through the card.
                Report(tag, cardPos, cardRot, CardWidth * InspectScale, cardH,
                       hand.transform, thumbSide);

                Bounds b = Content(hand);
                b.Encapsulate(Content(card));

                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
                cam.orthographic = true;
                cam.orthographicSize = b.size.magnitude * 0.36f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.targetTexture = rt;

                foreach (var (name, cardDir) in Views)
                {
                    // The view directions are given in the CARD's frame, so the same four names
                    // mean the same four things on both hands however the wrist ended up.
                    Vector3 dir = (cardRot * cardDir.normalized).normalized;
                    camGo.transform.position = b.center + dir * 2f;
                    camGo.transform.LookAt(b.center, cardRot * Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    var shot = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
                    shot.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                    shot.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(outDir, tag + "_" + name + ".png"),
                                       shot.EncodeToPNG());
                    Object.DestroyImmediate(shot);
                }
                cam.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                if (card != null)
                    Object.DestroyImmediate(card);
                Object.DestroyImmediate(hand);
            }
        }

        /// <summary>
        /// THE SIDE TEST, and it is a different question from "how close is it".
        ///
        /// <para>The first version of this counted every joint within a fingertip's radius of the
        /// card plane as CLIPPING, and that was wrong in a way worth keeping: a card held between a
        /// thumb and a knuckle is SUPPOSED to have both of them within a fingertip's radius — that
        /// is what holding it means. On the arcane glove the two sit at exactly -8.8 and +8.8 mm, a
        /// 17.6 mm sandwich on a card, and the old count called that two clips. An instrument that
        /// reports a correct grip as a defect is an instrument that gets ignored.
        ///
        /// <para>What actually distinguishes a grip from a pass-through is the SIDE. In this hold
        /// the thumb belongs on the reader's side of the card and every finger joint behind it, so
        /// a joint on the WRONG side is the defect — the card has been laid through the hand rather
        /// than in it. Reported separately: THROUGH (wrong side, inside the card's rectangle) is a
        /// failure; GRAZING (right side but under 2 mm) is a warning that the sandwich has no
        /// clearance left; everything else is a hold.</para>
        /// </summary>
        private const float GrazeMillimetres = 2f;

        /// <summary>Per-joint side report; see <see cref="GrazeMillimetres"/> for what it decides
        /// and the call site for what it cannot see.</summary>
        private static void Report(string tag, Vector3 cardPos, Quaternion cardRot,
                                   float w, float h, Transform hand, float thumbSide)
        {
            string[] digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            string[] joints = { "Root", "Mid", "Tip" };
            Vector3 n = cardRot * Vector3.forward;      // card +Z, AWAY from the reader
            Vector3 right = cardRot * Vector3.right;
            Vector3 up = cardRot * Vector3.up;
            int through = 0, grazing = 0;
            float nearestThumb = float.PositiveInfinity, nearestFinger = float.PositiveInfinity;
            float thumbU = float.NaN, thumbV = float.NaN, thumbNear = float.PositiveInfinity;
            var bad = new System.Text.StringBuilder();
            foreach (string d in digits)
            {
                // The thumb lies on the READER's side (card -Z); every finger backs it (card +Z).
                float wantSign = d == "Thumb" ? -1f : 1f;
                foreach (string j in joints)
                {
                    Transform t = Find(hand, "Anchor_" + d + "_" + j);
                    if (t == null)
                        continue;
                    Vector3 v = t.position - cardPos;
                    float dist = Vector3.Dot(v, n) * 1000f;
                    float u = Vector3.Dot(v, right) / (w * 0.5f);
                    float q = Vector3.Dot(v, up) / (h * 0.5f);
                    if (Mathf.Abs(u) > 1f || Mathf.Abs(q) > 1f)
                        continue;                       // beside the card, not against it
                    string label = " " + d + "_" + j + "("
                        + dist.ToString("F1", CultureInfo.InvariantCulture) + "mm)";
                    if (Mathf.Sign(dist) != wantSign && Mathf.Abs(dist) > GrazeMillimetres)
                    {
                        through++;
                        bad.Append(label);
                    }
                    else if (Mathf.Abs(dist) <= GrazeMillimetres)
                    {
                        grazing++;
                        bad.Append(label);
                    }
                    if (d == "Thumb")
                    {
                        nearestThumb = Mathf.Min(nearestThumb, Mathf.Abs(dist));
                        // WHERE ON THE CARD the thumb lands, as a fraction of half-width/half-height
                        // from the centre. Dead centre at the bottom is where an ability card keeps
                        // its initiative number, so u near 0 is a covered number, not a tidy grip.
                        if (Mathf.Abs(dist) < thumbNear)
                        {
                            thumbNear = Mathf.Abs(dist);
                            thumbU = u;
                            thumbV = q;
                        }
                    }
                    else
                        nearestFinger = Mathf.Min(nearestFinger, Mathf.Abs(dist));
                }
            }
            // THE HEADLINE CLAIM CARRIES NO THRESHOLD. "THROUGH 0" is decided by the SIDE, which
            // needs no calibration, and the two clearances are printed as the millimetres they are
            // rather than as a count against a radius nobody has measured on these gauntlets. A
            // reader can then apply their own idea of how thick a finger is — which is the whole
            // difference between an instrument and an opinion with a number in it.
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[CardGripPreview] {0}: THROUGH {1}, GRAZING {2}{3} Clearance from the card plane: "
                + "nearest thumb joint {4:F1} mm (in front), nearest finger joint {5:F1} mm (behind); "
                + "a finger is roughly 9-13 mm thick, so both are touching it. THUMB LANDS at "
                + "u {6:F2} / v {7:F2} of the card's half-extents (0,0 = dead centre). "
                + "(thumb side {8:F0})",
                tag, through, grazing,
                bad.Length > 0 ? " —" + bad + "." : " — nothing passes through the card.",
                nearestThumb, nearestFinger, thumbU, thumbV, thumbSide));
        }

        /// <summary>FingerCurler's three lines, verbatim: each of a digit's joints takes its
        /// AUTHORED local rotation times Euler(maxAngle * curl, 0, 0) about local X.</summary>
        private static void Bend(Transform root, string digit, float curl, Vector3 max)
        {
            string[] joints = { "Root", "Mid", "Tip" };
            for (int j = 0; j < joints.Length; j++)
            {
                Transform t = Find(root, "Anchor_" + digit + "_" + joints[j]);
                if (t == null)
                    continue;
                t.localRotation = t.localRotation * Quaternion.Euler(max[j] * curl, 0f, 0f);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = Find(root.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        /// <summary>Two unlit quads back to back — a face and a back, so which side is being looked
        /// at is never a guess.</summary>
        private static GameObject BuildCard(Vector3 pos, Quaternion rot, float w, float h,
                                            Texture2D face, Texture2D back)
        {
            var root = new GameObject("HeldCard");
            root.transform.position = pos;
            root.transform.rotation = rot;
            Quad(root.transform, "Face", w, h, -0.0004f, face, flip: false);
            Quad(root.transform, "Back", w, h, 0.0004f, back, flip: true);
            return root;
        }

        private static void Quad(Transform parent, string name, float w, float h, float z,
                                 Texture2D tex, bool flip)
        {
            var mesh = new Mesh { name = name };
            float x = w * 0.5f, y = h * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f),
                new Vector3(x, y, 0f), new Vector3(-x, y, 0f),
            };
            // The readable face points at -Z, so its winding is the one that faces -Z. The back
            // quad mirrors it AND mirrors its UVs, or the back would read as a mirror of the front.
            mesh.triangles = flip ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv = flip
                ? new[] { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) }
                : new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = tex };
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// The preview card FACE. Not decoration — every mark on it is one of the two questions:
        /// the GRIP BAND across the bottom is the fraction of the card the fingers are supposed to
        /// stay inside (CardGripPose.GripFraction doubled, so the band's top edge is where "too far
        /// up the card" begins), and the big arrow says which way is up without any text.
        /// </summary>
        private static Texture2D BuildFace()
        {
            var tex = new Texture2D(FaceTexPx, FaceTexPx, TextureFormat.RGBA32, false);
            var parchment = new Color(0.90f, 0.86f, 0.74f);
            var ink = new Color(0.20f, 0.16f, 0.12f);
            var band = new Color(0.85f, 0.32f, 0.22f);
            var art = new Color(0.42f, 0.52f, 0.46f);
            int bandTop = Mathf.RoundToInt(FaceTexPx * Cards.CardGripPose.GripFraction * 2f);
            for (int y = 0; y < FaceTexPx; y++)
            {
                for (int x = 0; x < FaceTexPx; x++)
                {
                    float fx = x / (float)(FaceTexPx - 1), fy = y / (float)(FaceTexPx - 1);
                    Color c = parchment;
                    // The ART BOX: the region a hand must not cover. Anything of the hand seen
                    // inside this rectangle in the face view is the defect the user named.
                    if (fx > 0.12f && fx < 0.88f && fy > 0.16f && fy < 0.84f)
                        c = art;
                    if (y < bandTop)
                        c = band;                       // the grip band, on top of everything
                    if (x < 4 || y < 4 || x > FaceTexPx - 5 || y > FaceTexPx - 5)
                        c = ink;                        // border
                    // Arrow to the card TOP, drawn over the art box.
                    float ax = Mathf.Abs(fx - 0.5f);
                    if (fy > 0.60f && fy < 0.86f && ax < (0.86f - fy) * 1.1f)
                        c = ink;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return tex;
        }

        /// <summary>The preview card BACK — a flat dark red with a light lattice, so a view that
        /// has ended up behind the card is instantly recognisable as such.</summary>
        private static Texture2D BuildBack()
        {
            var tex = new Texture2D(FaceTexPx, FaceTexPx, TextureFormat.RGBA32, false);
            var deep = new Color(0.32f, 0.09f, 0.11f);
            var line = new Color(0.62f, 0.44f, 0.26f);
            for (int y = 0; y < FaceTexPx; y++)
            {
                for (int x = 0; x < FaceTexPx; x++)
                {
                    Color c = deep;
                    if ((x + y) % 24 < 3 || (x - y + FaceTexPx) % 24 < 3)
                        c = line;
                    if (x < 4 || y < 4 || x > FaceTexPx - 5 || y > FaceTexPx - 5)
                        c = line;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return tex;
        }

        /// <summary>The union of every enabled renderer under <paramref name="root"/>.</summary>
        private static Bounds Content(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.05f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
