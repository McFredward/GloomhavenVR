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
        // ---------------------------------------------------------------------------------------
        //  THE PROPORTIONS BELOW ARE MEASURED OFF THE APPROVED DESIGN SHEET, NOT ESTIMATED.
        //
        //  Four passes of eyeballing this against the sheet produced four different wrong rods —
        //  a tube, a lens, a rod with knobs thinner than its own belly, and one whose bead band
        //  disappeared into the shaft. So the sheet was measured instead: the rod's mask was
        //  reduced to its principal axis and its width sampled perpendicular to that axis along
        //  its length (scripts are throwaway; the numbers are not). Against rod_oak.png:
        //
        //      length / max width ............ 9.85 : 1   (max width is the KNOB, not the shaft)
        //      knob / max width .............. 1.000      the knob IS the widest part of the rod
        //      knob / neck behind it ......... 1.480
        //      shaft end / shaft belly ....... 0.89       a GENTLE taper, not the 0.72 of pass 4
        //      cap, tip to neck .............. 6.4 %      of total length, per end
        //      => length / shaft diameter .... 12.5 : 1
        //
        //  The sheet is a three-quarter view, so along-axis distances are foreshortened and the far
        //  end reads smaller than the near one. RATIOS BETWEEN WIDTHS survive that; the length
        //  aspect does not survive it cleanly and is the softest number here. Everything else is
        //  taken as read.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The rod's nominal (mid-length) radius in metres, and the value both call sites should
        /// pass. 0.014 puts the board bar at 12.5 : 1 against its 0.352 m length, which is the
        /// sheet's proportion; the bars shipped 0.012 and read visibly more slender than the
        /// design. The user approved the change ("mach alle so nah wie möglich an den Entwurf")
        /// after it was raised as a deliberate ergonomics dial rather than a cosmetic one.
        ///
        /// <para>THE GRIP SURFACES DO NOT FOLLOW IT. <c>PlayTray</c>'s trigger zone is 0.05 m tall
        /// and <c>GrabbableModal</c>'s bar collider carries its own pad; both have room for a
        /// 28 mm rod and neither may be re-derived from this. What the bar LOOKS like must not
        /// change where it can be grabbed from.</para>
        /// </summary>
        internal const float DefaultRadius = 0.014f;

        /// <summary>
        /// WHERE THE CAP BAND ENDS AND THE SHAFT BEGINS, in <c>u</c> — the strip layout's one source of
        /// truth, and it lives HERE rather than beside the texture loader for a reason worth stating:
        /// this file must compile against <c>UnityEngine</c> AND NOTHING ELSE, because it is symlinked
        /// into the Unity companion project so the preview station renders the SAME mesh code the game
        /// runs. Two copies of a lathe profile is how the picture you check stops being the picture you
        /// ship. <c>Core.GrabBarTexture</c> and <c>scripts/grabbar-strips.py</c> both read these two
        /// numbers from here instead of carrying their own.
        ///
        /// <para><b>WHY 0.18 AND NOT THE PROPORTIONAL 0.10.</b> A cap is 2.0 R of a 25 R rod, about
        /// 8 % of its length, so splitting the strip by LENGTH would give it a tenth. But the cap
        /// is where nearly all the ornament is — the knob's tarnish and pitting and the whole
        /// beaded collar — while the shaft is a long, slow, mostly uniform run. At 0.10 the cap got
        /// 102 of 1024 texels and the generated 1536-wide source had to be squeezed 15x into it,
        /// which survives as fine noise and loses every chip and every tarnish pool: the knobs read
        /// as clean plastic next to shafts full of wear, which is exactly what the user reported.
        /// Texels are spent where the DETAIL is, not where the metres are.</para>
        ///
        /// <para>Changing this one number moves the mesh's UVs and the compositor's band boundary
        /// together, because both read it from here — which is the whole reason it lives in one
        /// place.</para>
        /// </summary>
        internal const float ShaftU0 = 0.18f;

        /// <summary>Where the shaft ends and the far CAP band begins, in <c>u</c>. See
        /// <see cref="ShaftU0"/>.</summary>
        internal const float ShaftU1 = 0.82f;

        /// <summary>
        /// Radial segments around the rod. TWENTY-FOUR, up from the sixteen the first cut used:
        /// the beads and the neck are small-radius features, and at sixteen their silhouette read
        /// as a polygon rather than a turned edge — which is half of why the first rods looked
        /// machined out of a cube next to the design sheet. The whole bar is ~2,600 triangles,
        /// still noise next to anything else on the board.
        /// </summary>
        private const int RadialSegments = 24;

        /// <summary>Axial segments along the SHAFT — enough to carry the taper smoothly.</summary>
        private const int ShaftSegments = 28;

        /// <summary>
        /// THE SHAFT TAPERS, and this is the single biggest difference between the first rods and
        /// the design the user approved. The first shaft was a near-constant tube with a 6 % swell;
        /// the sheet shows a rod that is FULL in the middle and visibly slimmer at both ends, with
        /// a waist before each cap. This is the radius at the shaft's two ends, as a fraction of
        /// the nominal (mid-length) radius.
        ///
        /// <para>It is a RADIUS profile, so it survives the axial scale
        /// <see cref="GrabBarVisual.SetLength"/> applies — a longer bar keeps the same silhouette
        /// stretched, which is what the sheet shows at every length it was drawn at.</para>
        /// </summary>
        private const float ShaftEndRadius = 0.89f;

        /// <summary>
        /// How much of each END of the shaft the taper occupies, as a fraction of its length. The
        /// middle <c>1 - 2 x</c> of the rod stays at FULL radius.
        ///
        /// <para>THE SECOND ATTEMPT GOT THIS WRONG IN THE OPPOSITE DIRECTION and it is worth
        /// recording. Replacing the near-constant first tube with a <c>sin^0.75</c> belly across the
        /// whole length turned the rod into a lens — fat in the middle, pinched at both ends, a
        /// rugby ball rather than a handle. The sheet shows something else: a rod that reads as one
        /// even thickness for most of its run and eases down only in the last stretch before each
        /// knob. Tapering over the outer 18 % a side gives that.</para>
        /// </summary>
        private const float TaperSpan = 0.30f;

        /// <summary>Length of ONE end cap as a multiple of the nominal radius. The cap holds the
        /// dome, the bead band and the neck; at the shipped 12 mm radius this is 20.4 mm.</summary>
        internal const float CapLengthInRadii = 2.00f;

        /// <summary>
        /// Hemisphere radius of the end knob, in nominal radii. It must stand PROUD of the shaft's
        /// tapered end (<see cref="ShaftEndRadius"/>) — that overhang is what makes it read as a
        /// knob the hand stops against rather than as a rounded-off end. The second attempt set it
        /// to 0.80 against a 0.68 end and the knobs came out THINNER than the shaft's belly; the
        /// third put it at 0.98 against a 0.86 end, which is only a 14 % overhang and the bead band
        /// vanished INTO the shaft. The sheet's knob is roughly a third wider than the rod beside
        /// it, which is what 1.02 against a 0.72 shaft end gives.
        /// </summary>
        private const float DomeRadius = 1.28f;

        /// <summary>Rings used for the knob's arc.</summary>
        private const int DomeSegments = 12;

        /// <summary>
        /// How far the knob's arc sweeps, in degrees. PAST 90 on purpose: a bare quarter arc ends
        /// at its widest point, so it reads as a rounded-off stump, while sweeping a little past
        /// the equator tucks the profile back in and the knob reads as a BALL — which is what the
        /// sheet draws and what the first three attempts all missed.
        /// </summary>
        private const float DomeSweepDegrees = 96f;

        /// <summary>
        /// HOW MANY BEADS SIT BEHIND THE KNOB. The sheet shows a tight band of many FINE rings; the
        /// first cut had three fat ones spread over most of the cap and they rendered as stacked
        /// plates. Five over half a radius is the sheet's density.
        /// </summary>
        private const int BeadCount = 5;

        /// <summary>Axial length of one bead, in nominal radii.</summary>
        private const float BeadLength = 0.07f;

        /// <summary>Radius a bead falls to between crowns, in nominal radii.</summary>
        private const float BeadRoot = 0.94f;

        /// <summary>How far a bead's crown rises above <see cref="BeadRoot"/>.</summary>
        private const float BeadRise = 0.13f;

        /// <summary>Points across ONE bead. Four makes each bead a real arc; the first cut used a
        /// rise/crown/fall triple and every bead came out a flat disc.</summary>
        private const int BeadSegments = 4;

        /// <summary>Where the bead band starts, in nominal radii from the dome's tip.</summary>
        private const float BeadBandStart = 1.44f;

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
                // Distance from the NEARER end, normalised against the taper span, then
                // smoothstepped so the shoulder is a curve and not a crease.
                float edge = Mathf.Clamp01(Mathf.Min(t, 1f - t) / TaperSpan);
                float ease = edge * edge * (3f - 2f * edge);
                profile.Add(new Vector3(
                    t - 0.5f,
                    radius * (ShaftEndRadius + (1f - ShaftEndRadius) * ease),
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

            // THE CAP PROFILE, outside-in: a small hemispherical knob, a collar, a tight band of
            // fine beads, a waisted neck, then a flare that meets the shaft's tapered end. Every
            // part is generated as a real arc rather than listed as corner points — that is what
            // separates a turned edge from a stack of discs, and the first cut got it wrong in
            // exactly that way.
            var profile = new List<Vector2>();   // (x, r) in nominal radii

            // The knob: a true hemisphere, so the pole sits exactly at the tip.
            for (int d = 0; d <= DomeSegments; d++)
            {
                float ang = (float)d / DomeSegments * DomeSweepDegrees * Mathf.Deg2Rad;
                profile.Add(new Vector2(DomeRadius * (1f - Mathf.Cos(ang)),
                                        DomeRadius * Mathf.Sin(ang)));
            }

            // The collar the beads sit against.
            profile.Add(new Vector2(BeadBandStart, BeadRoot + BeadRise * 0.55f));

            // The bead band. Each bead is a half-sine in radius, so consecutive beads meet at
            // BeadRoot and the band reads as beading rather than as ribs.
            for (int b = 0; b < BeadCount; b++)
            {
                float x0 = BeadBandStart + b * BeadLength;
                for (int k = 1; k <= BeadSegments; k++)
                {
                    float t = (float)k / BeadSegments;
                    profile.Add(new Vector2(x0 + t * BeadLength,
                                            BeadRoot + BeadRise * Mathf.Sin(Mathf.PI * t)));
                }
            }

            // The waist, then the flare into the shaft's tapered end. The waist sits just under
            // the bead root so the band reads as applied to the rod rather than cut from it.
            float bandEnd = BeadBandStart + BeadCount * BeadLength;
            profile.Add(new Vector2(bandEnd + 0.07f, 0.86f));
            profile.Add(new Vector2(CapLengthInRadii - 0.07f, ShaftEndRadius - 0.02f));
            profile.Add(new Vector2(CapLengthInRadii, ShaftEndRadius));

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
                    // WINDING — checked by hand, not assumed, because this is the eighth mesh in
                    // this project to be built wound against the side it is seen from and the
                    // first seven all shipped.
                    //
                    // Take a unit cylinder quad: ring i at x=0, ring i+1 at x=1, r=1, seg s at
                    // angle 0 and s+1 at a small e. Then i0=(0,1,0), i1=(0,1,e), i2=(1,1,0), and
                    // the ORIGINAL order (i0,i2,i1) gives (i2-i0)x(i1-i0) = (0,-e,0) — pointing
                    // INWARD at a point whose outward direction is +Y. Every face on every rod was
                    // therefore back-facing.
                    //
                    // IT WAS NEARLY INVISIBLE, which is why it needs the note. Back-culling a tube
                    // removes the near wall and leaves the far wall's interior, and a textured tube
                    // looks much the same either way — so the shaft read as correct from every
                    // angle. It only showed where a dome faces the camera head-on: the knob
                    // vanished and you looked straight through the bead ring into the shaft. The
                    // WINDOW rod hid it completely, because GloomhavenVR/Overlay defaults to
                    // _Cull = 0 (two-sided) and drew the inside faces anyway.
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i1); tris.Add(i3); tris.Add(i2);
                }
            }
        }

        /// <summary>How hard the generated normal map bites. The maps are derived from the
        /// albedo's own luminance rather than sculpted, so this stays moderate: past ~1.5 the wood
        /// grain reads as carving rather than as grain. Lives here, in the file the Unity preview
        /// symlinks, so the station and the game shade alike.</summary>
        internal const float NormalStrength = 1.35f;

        /// <summary>Feeds <c>GloomhavenVR/BoardLit</c>'s <c>_SpecStrength</c>. It MUST be non-zero:
        /// that shader's whole specular block is gated behind <c>_SpecStrength &gt; 0</c> and the
        /// property defaults to 0, so leaving it alone is the same as shipping no MRS map — which
        /// is exactly why the first rods rendered matte. The board's own material ships 0.85; the
        /// rods sit at the same value so a rod and the board it is bolted to catch the light
        /// alike.</summary>
        internal const float SpecStrength = 0.85f;

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
