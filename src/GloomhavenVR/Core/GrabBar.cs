using System.Collections.Generic;
using UnityEngine;

// FILE-SCOPED `namespace X;` IS C# 10 AND UNITY 2021.3 COMPILES C# 9 — so this ONE file in the
// mod uses a block namespace on purpose, and must keep using it. It is symlinked into the Unity
// companion project (unity/GloomhavenVR.Assets/Assets/Editor/GrabBarMeshLink.cs) so the preview
// station renders the SAME lathe code the game runs rather than a second copy of it, and a
// tidy-up to the file-scoped form the rest of src/ uses would break that project's compile with an
// error that names this file but not the reason. Everything else here is C# 9 or older for the
// same reason: no file-scoped namespace, no records, no global usings.
namespace GloomhavenVR.Core
{

    /// <summary>
    /// THE GRAB BAR'S GEOMETRY — a turned rod of ROUND cross-section, replacing the stretched unit
    /// cube that both grab bars have been since they were written.
    ///
    /// <para><b>WHAT WAS THERE AND WHY IT WENT.</b> Both handles built
    /// <c>GameObject.CreatePrimitive(PrimitiveType.Cube)</c> and scaled it to a long thin box —
    /// <c>Cards.PlayTray.BuildHandle</c> at <c>(BoardW·0.55, 0.024, 0.024)</c> and
    /// <c>WorldUI.GrabbableModal</c> at <c>(barWidth, BarThickness, BarThickness)</c> — both tinted the
    /// same flat brass <c>(0.62, 0.50, 0.28)</c>. The user's instruction (2026-08-28) was: real meshes
    /// and a real texture, a different bar for the windows than for the boards, and "statt ein langes
    /// Rechteck will ich es eher rund".</para>
    ///
    /// <para><b>WHY A LATHE AND NOT A STRETCHED CYLINDER.</b> The window bar's LENGTH is live: every
    /// <c>SyncBar</c> writes it from the measured ink of the window, so it can change on any frame.
    /// Rebuilding a mesh at that rate is out. But a rod cannot simply be scaled along its axis either —
    /// the shaft survives it (scaling a circular cross-section ALONG the axis leaves the circle a
    /// circle) while the domed end caps and the beaded rings would smear into ellipsoids. So the bar is
    /// built in THREE pieces: one shaft that may be scaled freely along X, and two end caps that never
    /// are. <see cref="GrabBarVisual.SetLength"/> is the whole contract, and it costs two transform
    /// writes and one scale write per call.</para>
    ///
    /// <para><b>ONE MATERIAL FOR THE THREE PIECES, DELIBERATELY.</b> <c>PanelGrabHandle.OnGrabHighlight</c>
    /// lights the bar by writing <c>_bar.sharedMaterial.color</c> — ONE renderer, one colour. Handing
    /// the three pieces of one bar a single shared Material instance means that unchanged one-line
    /// highlight still lights the whole rod, and <c>PanelGrabHandle.Init(owner, renderer, …)</c> keeps
    /// its signature. This is NOT the sharing that <c>GrabbableModal</c>'s own comment rejects: that one
    /// is about sharing a material ACROSS windows, where a single hover would have turned every floated
    /// bar gold. Sharing WITHIN one bar is exactly what makes the hover correct.</para>
    ///
    /// <para><b>THE TWO GRIP SURFACES ARE NOT GEOMETRY AND MUST NOT MOVE.</b> The visible bar is only
    /// half of what a call site owns. <c>GrabbableModal</c> hands the primitive's own
    /// <c>BoxCollider</c> to <c>PanelGrabHandle.SetBarCollider</c>, which is what the far RAY tests —
    /// the lost-menu fix — and <c>PlayTray</c> keeps a 62 %-wide trigger zone that the card dock-apron
    /// arbitration reads as <c>_handleZone</c>. Neither is drawn and neither may change shape because
    /// the drawing did. Every call site therefore keeps building its own collider exactly as before;
    /// this class draws, and draws only.</para>
    ///
    /// <para><b>FOUR STYLES, ONE SILHOUETTE.</b> Three boards (Oak / Steel / Bronze) each get a rod in
    /// their own material, and the windows get one quiet generic rod — the user's ruling, in his words:
    /// "Ich will insgesamt 4 verschiedene Stäbe. Jedes Board soll ein eigenes passendes haben und dann
    /// noch 'generische' für die Fenster." The SHAPE is shared across all four so they read as one
    /// family; only the texture differs. He rejected the bracketed variant ("Ich mag diese Konsolen
    /// nicht"), so nothing hangs below the rod and the profile stays a plain turned shaft with a slight
    /// swell and a domed cap behind two beaded rings at each end.</para>
    ///
    /// <para><b>MEASURED AGAINST THE SHIPPED BOARDS, NOT AGAINST A GUESS.</b> The four materials were
    /// matched to the three boards as <c>dev</c> actually ships them, rendered out of
    /// <c>Build/Bundles/gloomhavenvr.bundle</c> (70,204,340 B) through
    /// <c>unity/…/Assets/Editor/PreviewBoard.cs</c>'s PROJECT pass. That detour was not optional: the
    /// obvious reference to hand an artist is <c>board-unity-preview.png</c>, and the copy sitting in
    /// the working tree was rendered from the UNMERGED <c>boards-texture-rework</c> branch's own bundle
    /// (68,522,833 B) — a different artifact. Judging the colour off it would have been the same class
    /// of mistake as judging a shader off a Blender render, which is the reason PreviewBoard exists at
    /// all. Oak is honey-brown oak with visible grain, Steel a cool mottled blue-grey with no gold
    /// anywhere, Bronze gold-brass crowns over verdigris hollows.</para>
    /// </summary>
    internal static class GrabBarMesh
    {
        /// <summary>
        /// WHERE THE CAP BAND ENDS AND THE SHAFT BEGINS, in <c>u</c> — the strip layout's one source of
        /// truth, and it lives HERE rather than beside the texture loader for a reason worth stating:
        /// this file must compile against <c>UnityEngine</c> AND NOTHING ELSE, because it is symlinked
        /// into the Unity companion project so the preview station renders the SAME mesh code the game
        /// runs. Two copies of a lathe profile is how the picture you check stops being the picture you
        /// ship. <c>Core.GrabBarTexture</c> and <c>scripts/grabbar-strips.py</c> both read these two
        /// numbers from here instead of carrying their own.
        /// </summary>
        internal const float ShaftU0 = 0.10f;

        /// <summary>Where the shaft ends and the far CAP band begins, in <c>u</c>. See
        /// <see cref="ShaftU0"/>.</summary>
        internal const float ShaftU1 = 0.90f;

        /// <summary>Radial segments around the rod. Sixteen is the point where the silhouette of a
        /// 24 mm rod stops reading as faceted at arm's length in the headset; the whole bar is
        /// ~700 triangles at this count, which is noise next to anything else on the board.</summary>
        private const int RadialSegments = 16;

        /// <summary>Axial segments along the SHAFT. The shaft is not a plain tube — it carries the
        /// slight mid-length swell the design sheets show, so it needs enough rings to curve. The swell
        /// is a RADIUS profile, so it survives the axial scale that <see cref="GrabBarVisual.SetLength"/>
        /// applies; a shaft built as two rings would flatten into a cone the moment it stretched.</summary>
        private const int ShaftSegments = 20;

        /// <summary>How far the shaft's radius grows at mid-length, as a fraction of the nominal
        /// radius. Small on purpose: the sheets show a swell you feel rather than see, and anything
        /// larger starts to read as a club rather than a handle.</summary>
        private const float ShaftSwell = 0.06f;

        /// <summary>Length of ONE end cap as a multiple of the rod's radius. The cap holds the dome and
        /// both beaded rings, so it is the piece whose proportions carry the whole design; 2.2 R at the
        /// shipped 12 mm radius is 26.4 mm of cap at each end.</summary>
        internal const float CapLengthInRadii = 2.2f;

        /// <summary>
        /// The cap's lathe profile, as (axial position in radii from the OUTER tip, radius in radii).
        /// Read it outside-in: a dome, a neck, two beaded rings, then a shoulder that meets the shaft
        /// at full radius. The dome is emitted as real arc points rather than listed here so it stays
        /// smooth at any radial count.
        /// </summary>
        private static readonly Vector2[] CapProfileAfterDome =
        {
            new(1.05f, 0.62f),   // neck behind the dome
            new(1.15f, 0.80f),   // ring 1 rise
            new(1.28f, 0.80f),   // ring 1 crown
            new(1.38f, 0.62f),   // ring 1 fall
            new(1.50f, 0.78f),   // ring 2 rise
            new(1.62f, 0.78f),   // ring 2 crown
            new(1.72f, 0.60f),   // ring 2 fall
            new(1.90f, 0.88f),   // shoulder
            new(2.20f, 1.00f),   // meets the shaft
        };

        /// <summary>Arc points used for the cap's dome, from the tip to where the neck starts.</summary>
        private const int DomeSegments = 6;

        /// <summary>Radius the dome reaches before the neck, in radii.</summary>
        private const float DomeRadius = 0.92f;

        /// <summary>Axial length the dome occupies, in radii.</summary>
        private const float DomeLength = 0.90f;

        private static readonly Dictionary<int, Mesh> _shaftCache = new();
        private static readonly Dictionary<int, Mesh> _capCache = new();

        /// <summary>
        /// A UNIT-LENGTH shaft: a tube of the given radius running along +X from −0.5 to +0.5, carrying
        /// the mid-length swell. Scale it along X to the length you want — that is the whole reason it
        /// is unit length and the reason the caps are a separate mesh.
        ///
        /// <para>UV: <c>u</c> runs along the rod across the texture's SHAFT band
        /// (<see cref="ShaftU0"/> … <see cref="ShaftU1"/>), <c>v</c> runs
        /// around the circumference. The band is mapped ONCE rather than tiled, so a longer bar stretches
        /// its shaft texture instead of repeating it. That is a deliberate trade: wood grain and forged
        /// patina both run ALONG a turned rod, so stretching them is invisible at the 24 mm thickness
        /// these bars are drawn at, whereas tiling a sub-band of an atlas needs either a second material
        /// or a second texture — and a second material would break the one-write highlight above.</para>
        /// </summary>
        internal static Mesh Shaft(float radius)
        {
            int key = Mathf.RoundToInt(radius * 1e5f);
            if (_shaftCache.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var profile = new List<Vector3>();   // (x, r, u)
            for (int a = 0; a <= ShaftSegments; a++)
            {
                float t = (float)a / ShaftSegments;
                profile.Add(new Vector3(t - 0.5f,
                                        radius * (1f + ShaftSwell * Mathf.Sin(Mathf.PI * t)),
                                        Mathf.Lerp(ShaftU0, ShaftU1, t)));
            }
            Lathe(profile, verts, norms, uvs, tris);

            Mesh mesh = Finish("GrabBarShaft", verts, norms, uvs, tris);
            _shaftCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// ONE end cap, tip at local x = 0 and growing along +X to <c>CapLengthInRadii · radius</c>,
        /// where it meets the shaft at full radius. The caller places it: the LEFT cap is rotated 180°
        /// about Y, which is why the mesh is emitted once and never mirrored (a mirrored mesh would need
        /// its winding flipped — this project has shipped seven meshes wound against the side they are
        /// seen from, and the cheapest way not to ship an eighth is to not mirror geometry at all).
        ///
        /// <para>UV: <c>u</c> runs from the texture's outer edge inward across the CAP band, so the same
        /// strip serves both ends.</para>
        /// </summary>
        internal static Mesh Cap(float radius)
        {
            int key = Mathf.RoundToInt(radius * 1e5f);
            if (_capCache.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var profile = new List<Vector2>();
            // The dome, as a real quarter arc so it stays smooth: x sweeps 0…DomeLength while the
            // radius sweeps 0…DomeRadius on a sine, which puts the pole exactly at the tip.
            for (int d = 0; d <= DomeSegments; d++)
            {
                float t = (float)d / DomeSegments;
                profile.Add(new Vector2(DomeLength * (1f - Mathf.Cos(t * Mathf.PI * 0.5f)),
                                        DomeRadius * Mathf.Sin(t * Mathf.PI * 0.5f)));
            }
            profile.AddRange(CapProfileAfterDome);

            var lathed = new List<Vector3>();    // (x, r, u), in METRES
            for (int i = 0; i < profile.Count; i++)
            {
                float along = profile[i].x / CapLengthInRadii;          // 0 at the tip, 1 at the shaft
                lathed.Add(new Vector3(profile[i].x * radius, profile[i].y * radius,
                                       Mathf.Lerp(0f, ShaftU0, along)));
            }
            Lathe(lathed, verts, norms, uvs, tris);

            Mesh mesh = Finish("GrabBarCap", verts, norms, uvs, tris);
            _capCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Revolve a profile about the X axis into rings, taking the normals FROM THE PROFILE
        /// rather than assuming they are radial.
        ///
        /// <para><b>THIS IS THE DEFECT THE PREVIEW STATION CAUGHT, and it is worth writing down
        /// because it was invisible on one of the four rods.</b> The first cut wrote a purely radial
        /// normal <c>(0, cos, sin)</c> at every vertex. That is right for a CYLINDER and wrong
        /// everywhere else on a lathe: across the dome, the beads and the shoulder the radius
        /// changes along the axis, so the true normal tilts along X as well. The three LIT board
        /// rods rendered with black spikes around both caps — and the window rod looked perfect,
        /// because it draws through the UNLIT overlay shader and unlit geometry never consults a
        /// normal. A defect invisible on whichever rod you happen to look at first is exactly what
        /// rendering ALL of them is for.</para>
        ///
        /// <para>For a surface of revolution <c>S(t,θ) = (x(t), r(t)cosθ, r(t)sinθ)</c> the outward
        /// normal is <c>(-r', x'cosθ, x'sinθ)</c>. Central differences give <c>x'</c> and <c>r'</c>
        /// at the interior rings and one-sided differences at the two ends; at the dome's pole
        /// <c>r = 0</c> and the formula yields the axial normal on its own, with no special case.</para>
        ///
        /// <para>The seam vertex is DOUBLED (each ring emits <c>RadialSegments + 1</c> vertices) so
        /// <c>v</c> can run 0 → 1 without the last quad smearing the whole strip backwards. That is
        /// also why <c>Mesh.RecalculateNormals</c> is never called: it would weld the seam back
        /// together and undo it.</para>
        /// </summary>
        /// <param name="profile">(x, radius, u) per ring, in the mesh's own units, ordered along +X.</param>
        private static void Lathe(List<Vector3> profile, List<Vector3> verts, List<Vector3> norms,
                                  List<Vector2> uvs, List<int> tris)
        {
            int n = profile.Count;
            for (int i = 0; i < n; i++)
            {
                int prev = Mathf.Max(0, i - 1);
                int next = Mathf.Min(n - 1, i + 1);
                float dx = profile[next].x - profile[prev].x;
                float dr = profile[next].y - profile[prev].y;
                // Two coincident profile points would give a zero normal; fall back to radial,
                // which is correct for the only place that can happen — a flat run.
                if (Mathf.Abs(dx) < 1e-9f && Mathf.Abs(dr) < 1e-9f)
                    dx = 1f;

                float x = profile[i].x;
                float r = profile[i].y;
                float u = profile[i].z;
                for (int seg = 0; seg <= RadialSegments; seg++)
                {
                    float ang = (float)seg / RadialSegments * Mathf.PI * 2f;
                    float cy = Mathf.Cos(ang);
                    float sz = Mathf.Sin(ang);
                    verts.Add(new Vector3(x, cy * r, sz * r));
                    norms.Add(new Vector3(-dr, dx * cy, dx * sz).normalized);
                    uvs.Add(new Vector2(u, (float)seg / RadialSegments));
                }
            }

            int stride = RadialSegments + 1;
            for (int i = 0; i < n - 1; i++)
            {
                for (int seg = 0; seg < RadialSegments; seg++)
                {
                    int i0 = i * stride + seg;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }
        }

        /// <summary>
        /// WHERE THE THREE PIECES GO for a total end-to-end <paramref name="length"/>. Lives here,
        /// in the file the Unity preview symlinks, because the first version of this feature had
        /// the placement written out twice — once in <c>GrabBarVisual</c> and once in the preview —
        /// and BOTH copies were wrong in the same way, which is precisely why the render agreed
        /// with the runtime and neither caught it.
        ///
        /// <para><b>THE MISTAKE, recorded so it is not repeated.</b> The cap mesh has its dome TIP
        /// at local x = 0 and its shoulder — the end that meets the shaft — at x = capLength. The
        /// first placement rotated the LEFT cap and offset both by half the shaft. That put each
        /// cap's dome facing INWARD and its beaded shoulder facing OUT, so the rod rendered as a
        /// ball with the neck and both beads flaring past it like fins, and left a visible gap
        /// where the shoulder should have met the shaft. The mesh was never wrong: its bounds
        /// measured exactly 2.2 R by 2 R throughout. Only the pose was.</para>
        ///
        /// <para>So: the LEFT cap is UNROTATED and sits at −(shaft/2 + capLength), which puts its
        /// shoulder on the shaft's end and its tip outermost; the RIGHT cap is the same mesh turned
        /// 180° about Y at +(shaft/2 + capLength). Rotating rather than mirroring is deliberate —
        /// a mirrored mesh needs its winding flipped, and this project has already shipped seven
        /// meshes wound against the side they are seen from.</para>
        /// </summary>
        internal static void Pose(float length, float radius,
                                  out float shaftLength, out Vector3 leftPos, out Vector3 rightPos)
        {
            float capLength = radius * CapLengthInRadii;
            shaftLength = Mathf.Max(0f, length - 2f * capLength);
            float offset = shaftLength * 0.5f + capLength;
            leftPos = new Vector3(-offset, 0f, 0f);
            rightPos = new Vector3(offset, 0f, 0f);
        }

        /// <summary>The rotation the RIGHT cap takes; the left one takes identity. See
        /// <see cref="Pose"/>.</summary>
        internal static Quaternion RightCapRotation => Quaternion.Euler(0f, 180f, 0f);

        private static Mesh Finish(string name, List<Vector3> verts, List<Vector3> norms,
                                   List<Vector2> uvs, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
