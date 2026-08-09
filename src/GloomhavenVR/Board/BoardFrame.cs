using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Board;

/// <summary>
/// ONE thin closed STROKE on the control board's outer EDGE — a line drawn along the rim of the
/// asset, with nothing whatsoever drawn inside it and nothing floating beside it.
///
/// <para><b>USER RULING 2026-08-08, third round.</b> ModBuild 83's frame was the right OBJECT and
/// the wrong drawing. Three complaints, all structural, all answered below:
/// <list type="number">
/// <item>"Es soll wie jedes andere Element auch die Perspektive respektieren, manche Hintergründe
///   von Texten sind dadurch sichtbar." → DRAW ORDER, see THE PERSPECTIVE DEFECT.</item>
/// <item>"Es soll dezenter sein, der Strich ist mir zu dick und den schwarzen Rahmen braucht es
///   auch nicht um den Outline-Strich." → 8/12 mm bands → a 3/4 mm stroke, and the dark MR keyline
///   is deleted (both live in <see cref="FocusCue"/>). ONE band, ONE material, ONE submesh.</item>
/// <item>"Ich hätte es gerne wirklich am äußeren Rand des Assets aber eben nur am Rand und ohne
///   'Versprengung' wie es davor war." → CONTOUR + PLANE, see the next two sections.</item>
/// </list></para>
///
/// <para><b>THE PERSPECTIVE DEFECT, READ FROM SOURCE AND FROM THE HARDWARE LOG.</b> The frame's
/// pixels were never the problem: the material is <c>Sprites/Default</c>, which declares
/// <c>ZWrite Off</c> and NO <c>ZTest</c> — i.e. the default <c>ZTest LEqual</c> — in the
/// <c>Transparent</c> queue, so opaque world geometry in front of it always occluded it correctly.
/// The break was the DRAW ORDER against everything TRANSPARENT, and it was an omission rather than
/// a decision: Unity resolves transparent renderers by sortingLayer → sortingOrder → material
/// renderQueue → distance (this project's own finding, <c>RayInteractor</c> and
/// <c>CanvasConversion.8.Order</c>), and this renderer shipped at the default
/// <c>sortingOrder 0</c> while EVERY other transparent surface around it rides the mod's distance
/// ladder — converted panels at ≥ <c>PanelOrderBase</c> (100) and the control board's own
/// transparent furniture in the band just under the nearest panel in front of it (hardware log,
/// <c>FURNITURE ORDER: 'control board' … band 95..99</c>, later <c>239..243</c>). At order 0 the
/// stroke was painted BEFORE all of them, so a menu or a text plate that is spatially BEHIND the
/// board still painted over it, and — being the highest renderQueue at order 0 — the stroke in turn
/// painted over the game's own order-0 transparent plates that were in FRONT of it. Both directions
/// are the same bug, and the reported "text backgrounds become visible" is its visible half.</para>
///
/// <para>THE FIX IS THE LADDER'S OWN MEDICINE, not a queue tweak: the stroke is REGISTERED AS BOARD
/// FURNITURE (<see cref="Renderer"/> → <c>PlayTray.AdoptFurniture</c>, done by
/// <see cref="FocusDriver"/> for the local board), so it rides in the same distance-ranked band as
/// the status placard and the keycap labels and is ordered against every panel by measured eye
/// distance. The material's renderQueue is no longer written at all — it stays at the shader's own
/// <c>Transparent</c> (3000), the same queue as the rest of that band, so nothing inside the band
/// is decided by an artificial queue bump. A PEER's board has the same kind of group since
/// 2026-08-09 (<c>Net.BoardVisual.AdoptBoardOrder</c> — the remote board ranks against the panel
/// ladder as one cluster and orders its own plane by board-local depth), so the stroke is adopted
/// there too and needs no seat of its own on either board. Stated honestly: the local stroke draws
/// OVER the game's own order-0
/// transparent surfaces, which is the trade the whole furniture band already makes.</para>
///
/// <para><b>THE CONTOUR IS TRACED, NOT CIRCUMSCRIBED — AND THAT IS WHAT MOVED IT ONTO THE EDGE.</b>
/// The board is a flat-ish prop lying in its root's local XY plane with its depth on Z (the bundle
/// contract, <c>unity/board-prep/prepare_playtray.py</c>). ModBuild 83 took the CONVEX HULL of the
/// plan-view point cloud. The hull is exact at its own vertices and chords across every shallow
/// concavity BETWEEN them — and the shipped boards are barrel-sided rounded rectangles whose edges
/// bow slightly inward, so the chords stand off the visible rim everywhere. MEASURED OFFLINE for
/// this round (Blender, the three shipped assets, plan-view silhouette rasterised at 0.4 mm and the
/// distance from every point of the hull boundary to the nearest silhouette pixel):
/// <list type="bullet">
/// <item>oak (<c>PlayTray_prepped.fbx</c>, the DEFAULT board, footprint 0.639 × 0.318 m):
///   hull stand-off max <b>5.8 mm</b>, mean 3.4 mm, median 4.4 mm; 71 % of the perimeter over
///   2 mm out.</item>
/// <item>bronze (<c>PlayTray_16vm268h</c>, 0.640 × 0.218 m): max <b>6.4 mm</b>, mean 3.7 mm.</item>
/// <item>steel (<c>PlayTray_9capjqp6</c>, 0.640 × 0.369 m): max <b>9.5 mm</b>, mean 4.2 mm.</item>
/// </list>
/// On top of that ModBuild 83 added 6 mm of deliberate clear air, so the stroke's inner edge sat
/// 9–16 mm off the visible rim of a 0.64 m board. That IS the "nicht wirklich am äußeren Rand"
/// report, and no width change could have fixed it.</para>
///
/// <para>The contour is therefore now the SAME convex hull with every edge <b>SAGGED ONTO THE
/// MESH'S OWN BOUNDARY</b> (<see cref="Trace"/>): each hull edge is cut into 3 mm buckets, a bucket
/// keeps the least-inward mesh vertex that projects into it and lies within
/// <see cref="SagTrustLocal"/> of the edge, and the contour vertex is that bucket's midpoint pulled
/// in by exactly that much. This is not a new contour algorithm — it is the shipped hull, moved,
/// which is what keeps every property it was chosen for:
/// <list type="bullet">
/// <item><b>It is bounded on both sides by construction.</b> The contour can only move INWARD from
///   the hull, and never by more than 10 mm. It cannot wander into the board's interior art and it
///   cannot grow outward.</item>
/// <item><b>No interior geometry can be drawn.</b> Every recess, well, dial and bevel is far more
///   than 10 mm inside the hull and can never win a bucket. That is a construction, not a filter
///   that could miss a feature.</item>
/// <item><b>No "Versprengung" is expressible.</b> The output is one vertex per bucket walked once
///   around the hull — exactly ONE closed polygon. There is no second ring and no free vertex to
///   shed. Re-verified offline on all three shipped assets AFTER the filtering below: zero
///   self-intersections in the contour (635 / 658 / 555 vertices) and zero in both offset rings the
///   stroke is built from, at the shipped −1 mm bite and +2 mm outer radius.</item>
/// <item><b>It degrades to the hull, not to garbage.</b> A bucket with no evidence is interpolated
///   from its populated neighbours; a hull with no evidence at all, or a degenerate one, skips the
///   trace entirely. The log states which contour was used and the mean and max sag it applied.</item>
/// </list></para>
///
/// <para><b>THE SAG PROFILE IS FILTERED, AND ModBuild 84's WAS NOT — THE SECOND HALF OF
/// "zerstückelt".</b> ModBuild 84 took each bucket's least-inward vertex RAW and pinned sag 0 at
/// every hull VERTEX, on the argument that a hull vertex is itself a cloud point and so has no sag
/// of its own. True, and beside the point: the 56-vertex hull of a 1.9 m perimeter has a vertex
/// every ~34 mm, so the contour was forced back OUT to the hull 56 times per lap while sagging up to
/// 10 mm in between (hardware log: <c>sag mean 3.6 mm / max 9.95 mm</c> — the max is the trust band
/// itself, i.e. single vertices were dragging whole buckets to the limit). That is a scallop every
/// three and a half centimetres plus per-bucket spikes on top of it: exactly the ragged, wandering
/// line in the screenshot, and it also decided WHERE the buried ring managed to poke out.</para>
///
/// <para>The sag is now treated as what it is — a signal sampled around a closed loop — and filtered
/// as one:
/// <list type="number">
/// <item>gaps are filled CIRCULARLY between populated buckets, wrapping across hull vertices, with
///   no pin. A bucket with no evidence now reads what its real neighbours say instead of what the
///   hull says;</item>
/// <item>a <see cref="SagMedianWindow"/>-wide circular MEDIAN removes the single-vertex spikes. A
///   median cannot invent a value: its output is always one of the measurements;</item>
/// <item><see cref="SagSmoothPasses"/> passes of a <see cref="SagSmoothWindow"/>-wide circular box
///   blur (≈ a Gaussian over ±13 mm) take out what is left of the sampling noise.</item>
/// </list>
/// Every step is an average or a selection over values already inside [−<see cref="SagTrustLocal"/>,
/// 0], so the bound the whole construction rests on is preserved exactly: the contour still cannot
/// leave the hull's own 10 mm boundary band, cannot reach interior art, and is still ONE closed
/// polygon walked once around the hull. What changes is only how RAGGED it is allowed to be.</para>
///
/// <para>MEASURED RESULT of the FILTERING, offline on all three shipped assets — the contour's mean
/// |second difference|, which is the "zerstückelt" complaint expressed as a number, against the
/// stand-off it costs:
/// <list type="bullet">
/// <item>oak: raggedness <b>0.429 mm → 0.041 mm</b> mean (worst vertex 7.78 mm → 0.83 mm), while the
///   contour's distance to the mesh moves only 0.62 mm → 0.79 mm median;</item>
/// <item>steel: 0.505 → 0.044 mm mean (worst 7.25 → 0.77 mm), distance 0.73 → 0.90 mm;</item>
/// <item>bronze: 1.016 → 0.043 mm mean (worst 8.88 → 0.46 mm), distance 0.78 → 1.27 mm.</item>
/// </list>
/// A factor of ten to twenty-four smoother for a fraction of a millimetre of accuracy — and the p95
/// and max stand-off are IDENTICAL before and after, i.e. the filtering removed noise and not
/// signal.</para>
///
/// <para>MEASURED RESULT of the trace itself, same method as above (signed distance from the built
/// geometry to the rasterised silhouette; negative = on the board):
/// <list type="bullet">
/// <item>contour median −0.3 mm / −0.9 mm / −0.2 mm (oak / bronze / steel), i.e. it sits ON the
///   visible rim rather than 3.4–4.2 mm outside it;</item>
/// <item>with the shipped 1 mm bite and 3 mm stroke, the stroke's INNER edge is on the board for
///   93–99 % of the perimeter and its OUTER edge sits a median 1.5–2.0 mm off it. The stroke
///   straddles the rim. ModBuild 83's band started 9–16 mm outside it.</item>
/// </list>
/// The one number that makes this work on a DECIMATED mesh is <see cref="SagTrustLocal"/> — see its
/// own doc, and the 24 mm error it exists to prevent.</para>
///
/// <para>Also verified rather than assumed: none of the three shipped boards has a stray part —
/// every hull vertex of all three lies within 1.7 mm of the rasterised silhouette (oak 1.03 mm,
/// bronze 1.67 mm, steel 0.63 mm), and each board is a SINGLE mesh object, so there is no
/// decorative sub-mesh for the trace to ignore and nothing pushing the contour outward.</para>
///
/// <para><b>THE PLANE: THE DECORATED FACE IS THE BOARD'S MIN-z END, AND A "SANITY" CLAMP BURIED THE
/// STROKE 11.7 mm INSIDE THE BOARD.</b> ModBuild 84 got the DIRECTION right and the DISTANCE wrong,
/// and the hardware log states both halves outright:
/// <c>Board-local z spans −0.0332…0; the decorated face is the MIN-z end (derived from the four FBX
/// anchors), so the stroke plane is z −0.0215</c>. −0.0215 is not −0.0332 − 1.5 mm: it is the
/// ±<c>0.02</c> clamp that ModBuild 84 shipped as "insurance, not a working part" BINDING, on the
/// default board, on the first frame. The clamp was authored around an offline claim that the face
/// sits at board-local z ≈ 0; the runtime says the opposite, so the clamp did not protect anything —
/// it parked the ring 11.7 mm below the face, inside the opaque body. That, together with the sag
/// noise below, IS the user's "zerstückelt" (2026-08-08, <c>outline2.png</c>): buried geometry pokes
/// out only where the rim happens to sag away from it, so the line appears in patches on the near
/// edge and vanishes entirely on the far one.</para>
///
/// <para><b>THE FACING ITSELF WAS NEVER WRONG — ONLY THE CLAMP WAS.</b> Worth stating plainly,
/// because the obvious reading of "the log says MIN, the class doc said MAX" is that
/// <see cref="FaceSign"/> is broken, and chasing that is a dead end. <c>PlayTray.EnsureBuilt</c>
/// (PlayTray.1.Core.cs:461-470) builds the board face frame from the same four anchors,
/// <c>nF = (Slot2−Slot1) × (ShortRest−LongRest)</c>, and its own comment records that the choice was
/// VERIFIED BY OFFSCREEN RENDER: elements face the anchor's local −Z, forward = +nF, therefore
/// <b>−nF points out of the decorated face</b> ("do not flip to −nF: that hides the cards on the
/// decorative back"). The hardware log confirms it every session from the other side —
/// <c>ITEM2 diag — Slot0: readable(−Z) … vs head dot 0.66 (want &gt;0) → FACES the player</c>: the
/// direction the cards read along really does point at the player. <see cref="FaceSign"/> computes
/// exactly that direction, transported into board-root space by an ordinary rotation (which
/// preserves a cross product), and answers MIN-z. So MIN-z is right, the face sits at −0.0332, the
/// stroke belongs at −0.0347, and it was the ±20 mm clamp — nothing else — that moved it to −0.0215.
/// The class doc's "MAX-z" was an offline reconstruction of the import chain that never agreed with
/// the code's own log line, and the log line was the measurement.</para>
///
/// <para>The absolute clamp is therefore GONE. The sanity check it was meant to be is now a check on
/// the measured DEPTH (<see cref="MaxBoardDepthLocal"/>): that is the quantity which actually reveals
/// a mis-imported asset, and unlike a clamp on the plane's POSITION it cannot mistake a correctly
/// measured face for a mis-import and quietly bury the stroke inside the board.</para>
///
/// <para><b>THE PLANE IS FLAT AND SITS AT MID-THICKNESS — AND A PER-VERTEX DRAPE IS A TRIED AND
/// REJECTED IDEA, NOT AN UNCONSIDERED ONE.</b> USER RULING 2026-08-08, ModBuild 85 round:
/// "es ist jetzt aber aktuell so ein dünner Streifen der wellenförmig um das Controllboard drumrum
/// geht … Ich möchte, dass so ein outline Streifen etwa mittig in der Höhe/Dicke des Controllboards
/// angesiedelt ist und nicht solche Wellen macht, sondern gerade."</para>
///
/// <para>ModBuild 85 gave every contour vertex its OWN z — the most-proud mesh z in its bucket — to
/// drive the viewing parallax to zero. It did exactly that, and it bought a WAVE: its own log line
/// reads <c>the stroke DRAPES over z −0.0326…−0.0084</c>, i.e. the ring rode 24 mm up and down the
/// board's rim over a single lap. Reason it went wrong: the drape takes the most-proud z within the
/// trust band, which is a statement about where the RIM SURFACE is, not about where the SILHOUETTE
/// is, and those differ by centimetres wherever the board's art is deep. The parallax it removed was
/// worth far less than the wave it added.</para>
///
/// <para>Flat wins, and on the shipped boards it costs almost nothing, which is why mid-thickness is
/// the right constant rather than merely the requested one. MEASURED offline: the cross-section
/// within ±10 % of the depth around the MIDPOINT has the same plan footprint as the FULL silhouette —
/// oak 0.6389 × 0.3185 m vs. 0.6390 × 0.3185, steel 0.6400 × 0.3678 vs. 0.6400 × 0.3688. In other
/// words the widest part of the barrel IS the middle, so on the two plate-shaped boards the flat
/// mid-plane puts the line precisely on the widest rim, where its silhouette physically lives, with
/// no wave and no parallax. Bronze is the honest exception: a raked lectern 301 mm deep whose
/// horizontal slice at mid-height spans only 73 mm of its 218 mm plan height. There the mid-plane is
/// a real compromise — which is exactly why the contour is still traced from the FULL point cloud
/// rather than from a mid-thickness slice: the OUTLINE stays correct on every board even where the
/// plane can only be approximately right.</para>
///
/// <para>One honest consequence, unchanged from the drape: at a barrel-shaped edge the millimetre the
/// stroke bites INSIDE the contour is on surface that curves back toward the viewer, so that
/// millimetre is depth-occluded and the line reads as its outer ~2 mm there. That is the bite doing
/// its job — it exists so the line can never show a hairline of background between itself and the
/// board at any angle, not to be uniformly wide.</para>
///
/// <para><b>ONE MESH, ONE RENDERER, ONE MATERIAL, ONE SUBMESH.</b> The whole point cloud yields ONE
/// contour, which yields ONE <see cref="Mesh"/> on ONE GameObject with ONE material. The count is
/// structural, not a convention: <see cref="Build"/> has no loop that can emit a second stroke.</para>
///
/// <para><b>NO Z-FIGHTING WITH THE BOARD.</b> The stroke deliberately bites
/// <see cref="FocusCue.OutlineEdgeBiteLocal"/> (1 mm) INSIDE the contour so it touches the rim
/// everywhere — but it is drawn <see cref="ProudLocal"/> in FRONT of the board's own face plane and
/// writes no depth, so the bitten millimetre paints onto the rim instead of fighting it.</para>
///
/// <para><b>SCALE AND POSE, AND THE PINNED BOARD.</b> The stroke is a child of the board ROOT with
/// an IDENTITY local pose, and its geometry is baked in board-ROOT-local metres. A board move, tilt
/// or user RESIZE is a write to the root's own transform, which the stroke inherits for free — no
/// per-frame maths. Nothing here ever writes the tray's transform: adding a child leaves the ROOT's
/// world pose untouched, which is what keeps a FIXIERT (pinned, world-frozen) board legal under the
/// freeze sentinel in <c>PlayTray.2.Watchdog</c>.</para>
///
/// <para><b>THE MESH REPORTS THE CONTOUR'S BOUNDS, NOT THE STROKE'S.</b>
/// <c>PlayTray.MeasureBoardLocalExtents</c> walks every mesh renderer under the tray and prefers
/// <c>mesh.bounds</c>. An honest bound would make the board's measured top edge — and with it the
/// tooltip and enemy-reveal clearance — twitch by the stroke width every time the cue blinks on.
/// The mesh therefore reports the CONTOUR's bounding box, which is by construction no larger than
/// the board's own footprint. The box is a thin slab at the ring's own plane, i.e. the board's
/// mid-thickness — strictly inside the board's own z span. And it costs the measurement NOTHING
/// either way: <c>MeasureBoardLocalExtents</c> reads the x and y of the box's eight corners and never
/// its z, and this frame sits at an identity local pose under the very root it is measured in.</para>
///
/// <para><b>THE MATERIAL IS AN OWN INSTANCE.</b> One <see cref="Material"/> from
/// <see cref="BoardVisual.Unlit"/> (<c>Sprites/Default</c>: unlit, Cull Off — right for a flat ring
/// that must survive being looked at from behind — alpha-blended, no depth write, and its
/// renderQueue left exactly where the shader puts it). The board's own materials are read for
/// nothing and written never.</para>
///
/// <para>Degrades honestly: no bundled board (procedural fallback board — which is a genuine
/// RECTANGLE, so the caller's rectangle frame is its true contour, not a compromise), no readable
/// mesh, or a degenerate contour ⇒ <see cref="Build"/> returns null and the caller keeps
/// <see cref="WorldFrame"/>. Logged once with the reason.</para>
/// </summary>
internal sealed class BoardFrame
{
    /// <summary>The instantiated board prefab under a board root. Both the LOCAL board
    /// (<c>PlayTray.EnsureBuilt</c>) and a PEER's board (<c>RemoteTrayVisual.Build</c>) name their
    /// clone exactly this, which is why one builder serves both sides.</summary>
    private const string VisualChildName = "TrayVisual";

    /// <summary>The six FBX anchor empties. Everything the MOD parks on the board — cards, slot
    /// frames and highlights, the Confirm/Undo/rest keycaps — hangs under one of these. A card in a
    /// slot is not part of the board's contour and must not push the stroke outward.</summary>
    private static readonly string[] AnchorNames =
        { "Slot1", "Slot2", "ShortRestToken", "LongRestToken", "ConfirmButton", "UndoButton" };

    /// <summary>Name prefix of every mod-built GameObject — a second, cheap exclusion for anything
    /// the mod adds under the visual that is not below an anchor.</summary>
    private const string ModNamePrefix = "GloomhavenVR.";

    private const string FrameName = "GloomhavenVR.BoardFrame";

    /// <summary>Length of one SAG BUCKET along a hull edge (board-local metres). 3 mm: the sag
    /// profile of a modelled board edge varies over centimetres, so this samples it several times
    /// over, and the whole contour lands at ~550–660 vertices — one ~1 300-triangle static ring,
    /// built once per board.</summary>
    private const float SagBucketLocal = 0.003f;

    /// <summary>
    /// How far INSIDE a hull edge a mesh vertex may still be taken as evidence of where the real
    /// boundary runs (board-local metres). This is the one number that makes the sag trustworthy on
    /// a DECIMATED mesh: the shipped boards are 20 k-triangle decimations, so a straight run of rim
    /// can be one long triangle edge with no vertex on it for 80 mm at a stretch, and a bucket there
    /// contains only INTERIOR vertices. Ignoring anything deeper than 10 mm means such a bucket
    /// reports "no evidence" and is interpolated from its neighbours — which on a straight run is
    /// the hull line, i.e. exactly right — instead of being dragged 25 mm into the board by a vertex
    /// that has nothing to do with the boundary. Measured: with this band the traced contour lands
    /// within 0.9 mm (median) of the rasterised silhouette on all three shipped boards; without it
    /// the worst error was 24 mm.
    /// </summary>
    private const float SagTrustLocal = 0.010f;

    /// <summary>Hard cap on the buckets of one hull edge, so a pathological hull cannot allocate
    /// without bound. 512 × 3 mm = 1.5 m, longer than any shipped board's whole perimeter.</summary>
    private const int MaxBucketsPerEdge = 512;

    /// <summary>Width (in buckets, odd) of the circular MEDIAN that runs over the sag profile before
    /// it is smoothed. 5 buckets = 15 mm: wide enough that a single stray mesh vertex dragging one
    /// bucket to the trust limit is outvoted, narrow enough that a real 15 mm feature of the rim
    /// survives. A median only ever outputs a value it was given, so it cannot push the contour
    /// outside the band the trace guarantees.</summary>
    private const int SagMedianWindow = 5;

    /// <summary>Width (in buckets, odd) of the circular box blur applied after the median.
    /// 9 buckets = 27 mm.</summary>
    private const int SagSmoothWindow = 9;

    /// <summary>How often the box blur is repeated. Two passes of a box are a good Gaussian
    /// (σ ≈ 3.7 buckets ≈ 11 mm) at a fraction of the cost, and the result is C¹-smooth to the eye
    /// at the ~550-vertex resolution this contour is built at.</summary>
    private const int SagSmoothPasses = 2;

    /// <summary>How far the stroke's plane is nudged toward the DECORATED FACE from the board's
    /// mid-thickness, in board-local metres, along the direction <see cref="FaceSign"/> resolves.
    /// 1.5 mm, and it is anti-z-fighting insurance rather than a placement: at mid-thickness the
    /// board's own surface passes exactly through the contour, so a ring sitting at precisely that z
    /// would be coplanar with it and could speckle on depth-buffer precision. A millimetre and a half
    /// toward the viewer settles that and moves the line by a fraction of a millimetre in plan (the
    /// barrel's radius changes by well under 0.1 mm over 1.5 mm of depth).</summary>
    private const float ProudLocal = 0.0015f;

    /// <summary>
    /// Largest board DEPTH (board-local metres) this class will trace a stroke for — the sanity
    /// check that replaces ModBuild 84's absolute z clamp, and the one the clamp should always have
    /// been. MEASURED on the three shipped FBXs rather than assumed, because the assumption is the
    /// trap here: oak is 33.2 mm deep, steel 63.8 mm, and BRONZE IS 301.1 mm — a deep-bodied board,
    /// not a plate, and any "a board is a few centimetres thick" number would have refused it. 0.5 m
    /// is past every shipped asset and still catches the failure this exists for: a model imported
    /// with its thickness on the wrong axis, where the "depth" is a whole board WIDTH. Unlike a clamp
    /// on the plane's POSITION it cannot mistake a correctly measured face for a mis-import and
    /// quietly bury the stroke inside the board. Exceeding it falls back to the rectangle frame with
    /// the reason logged.
    /// </summary>
    private const float MaxBoardDepthLocal = 0.5f;

    /// <summary>Contour edges shorter than this (board-local metres) are collapsed. Adjacent bins
    /// can land on the same modelled corner; the miter of a sub-millimetre edge is numerically
    /// worthless and would put a spike on the stroke.</summary>
    private const float MinEdgeLocal = 0.0008f;

    /// <summary>Floor on the miter's cosine when offsetting a contour vertex, so a pathologically
    /// sharp vertex cannot grow a spike. 0.25 caps the outward step at 4× the offset.</summary>
    private const float MinMiterCos = 0.25f;

    /// <summary>Board-local metres past the AUTHORED plate that a vertex may sit and still count
    /// toward the contour. Mirrors <c>PlayTray</c>'s own <c>BoardExtentSanityMargin</c> and exists
    /// for the same reason: one stray vertex must never drag the stroke metres into the scene. Wide
    /// enough that the tallest shipped board (steel, half-height 0.184 m vs. the authored 0.16 m)
    /// clears it comfortably.</summary>
    private const float FootprintSanityMargin = 0.35f;

    /// <summary>Least vertices a usable contour may have.</summary>
    private const int MinContourVertices = 3;

    /// <summary>The closed contour in board-ROOT-local metres, plan view. The ring is FLAT — its one
    /// z is <see cref="_planeZ"/> (see THE PLANE).</summary>
    private readonly Vector2[] _contour;

    /// <summary>The single z the whole ring sits at: the board's mid-thickness, nudged
    /// <see cref="ProudLocal"/> toward the decorated face.</summary>
    private readonly float _planeZ;
    private readonly float _faceSign;
    private readonly Bounds _bounds;
    private readonly GameObject _go;
    private readonly MeshFilter _filter;
    private readonly MeshRenderer _renderer;
    private readonly Material _rimMaterial;
    private readonly Mesh _mesh;

    /// <summary>Stroke radii the mesh is currently baked at, in board-local microns.
    /// <see cref="int.MinValue"/> = never baked.</summary>
    private int _bakedBite = int.MinValue;
    private int _bakedRim = int.MinValue;
    private bool _shown;

    private static bool _loggedBuilt;
    private static bool _loggedFallback;

    private BoardFrame(Vector2[] contour, float planeZ, float faceSign, Bounds bounds, GameObject go,
                       MeshFilter filter, MeshRenderer renderer, Material rim, Mesh mesh)
    {
        _contour = contour;
        _planeZ = planeZ;
        _faceSign = faceSign;
        _bounds = bounds;
        _go = go;
        _filter = filter;
        _renderer = renderer;
        _rimMaterial = rim;
        _mesh = mesh;
    }

    /// <summary>The stroke's own renderer, for the caller that must seat it on a draw-order ladder
    /// (the LOCAL board registers it as furniture — see THE PERSPECTIVE DEFECT in the class doc).
    /// Null-safe for a torn-down frame.</summary>
    internal MeshRenderer? Renderer => _renderer != null ? _renderer : null;

    /// <summary>The stroke's GameObject, for the same caller. Null once destroyed.</summary>
    internal GameObject? RootObject => _go != null ? _go : null;

    // -------------------------------------------------------------------------------- building --

    /// <summary>
    /// Build the stroke for the board asset under <paramref name="boardRoot"/> (the tray root
    /// locally, the remote board root for a peer). Null when there is no board ASSET to trace — the
    /// procedural fallback board, a peer still on the flat fallback, or a bundle whose meshes are not
    /// Read/Write enabled. The caller then keeps <see cref="WorldFrame"/>.
    /// </summary>
    internal static BoardFrame? Build(Transform? boardRoot, string label)
    {
        if (boardRoot == null)
            return null;
        Transform? visual = boardRoot.Find(VisualChildName);
        if (visual == null)
            return null; // procedural / flat fallback board — a rectangle IS its true contour

        var cloud = new List<Vector3>(4096);
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        int layer = boardRoot.gameObject.layer;
        int skippedUnreadable = 0;
        int meshes = 0;
        float xLimit = PlayTray.BoardHalfWidthLocal + FootprintSanityMargin;
        float yLimit = PlayTray.BoardTopLocalY + FootprintSanityMargin;
        Matrix4x4 worldToBoard = boardRoot.worldToLocalMatrix;

        foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>(includeInactive: true))
        {
            if (mf == null || mf.GetComponent<MeshRenderer>() == null)
                continue;
            if (mf.gameObject.name.StartsWith(ModNamePrefix, System.StringComparison.Ordinal))
                continue;
            if (UnderAnchor(mf.transform, visual))
                continue;
            Mesh? mesh = mf.sharedMesh;
            if (mesh == null)
                continue;
            if (!mesh.isReadable)
            {
                skippedUnreadable++;
                continue;
            }

            Matrix4x4 toBoard = worldToBoard * mf.transform.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toBoard.MultiplyPoint3x4(verts[i]);
                // One absurd vertex must never define the contour (PlayTray's own extent rule).
                if (!(Mathf.Abs(p.x) <= xLimit) || !(Mathf.Abs(p.y) <= yLimit))
                    continue;
                cloud.Add(p);
                if (p.z < minZ)
                    minZ = p.z;
                if (p.z > maxZ)
                    maxZ = p.z;
            }
            layer = mf.gameObject.layer; // ride the board's own (mod) layer, as its renderers do
            meshes++;
        }

        if (cloud.Count < MinContourVertices)
        {
            Fallback(label, skippedUnreadable > 0
                ? $"{skippedUnreadable} board mesh(es) are not Read/Write enabled, so their vertices "
                  + "cannot be read to trace a contour (re-import the FBX with isReadable)"
                : "the board asset exposes no mesh renderer of its own");
            return null;
        }

        // WHICH WAY THE BOARD FACES. +1 = the decorated face is the board's MAX-z end, −1 = its
        // MIN-z end (every shipped prefab, runtime-verified). See THE PLANE in the class doc.
        float faceSign = FaceSign(visual, boardRoot, out bool faceFromAnchors);
        float facePlane = faceSign > 0f ? maxZ : minZ;
        float depth = maxZ - minZ;
        if (!(depth <= MaxBoardDepthLocal))
        {
            Fallback(label, $"the board asset measures {depth * 1000f:0.#} mm deep in board-local z "
                            + $"(z {minZ:0.####}…{maxZ:0.####}), more than the "
                            + $"{MaxBoardDepthLocal * 1000f:0} mm this class will trace — that is a "
                            + "mis-imported asset (thickness on the wrong axis), not a board");
            return null;
        }

        List<Vector2> hull = Hull(cloud);
        Vector2[]? traced = Simplify(Trace(cloud, hull, out float meanSag, out float maxSag,
                                           out int gapBuckets));
        Vector2[]? contour = traced ?? Simplify(hull);
        bool usedTrace = traced != null;
        if (contour == null)
        {
            Fallback(label, $"the board's {cloud.Count} projected vertices collapse to a degenerate "
                            + "contour (fewer than three distinct boundary points) — there is no "
                            + "footprint to trace");
            return null;
        }

        // ONE FLAT PLANE at the board's mid-thickness, nudged toward the decorated face — see
        // THE PLANE. Not the face plane (ModBuild 84 clamped that one INTO the body) and not a
        // per-vertex drape (ModBuild 85 — that is what waved).
        float planeZ = (minZ + maxZ) * 0.5f + faceSign * ProudLocal;

        // The REPORTED bounds are the contour's, never the stroke's — see the class doc.
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < contour.Length; i++)
        {
            min = Vector2.Min(min, contour[i]);
            max = Vector2.Max(max, contour[i]);
        }

        var bounds = new Bounds(
            new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, planeZ),
            new Vector3(max.x - min.x, max.y - min.y, 0.001f));

        var go = new GameObject(FrameName);
        Transform t = go.transform;
        // IDENTITY local pose under the board ROOT: the stroke inherits the board's live pose, tilt
        // and user scale with no per-frame maths, and dies with the board. The ROOT's own transform
        // is never written — that is what keeps a pinned board's freeze sentinel quiet.
        t.SetParent(boardRoot, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        go.layer = layer;

        var mesh2 = new Mesh { name = "GloomhavenVR.BoardFrameRing" };
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh2;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        // sortingOrder is NOT set here: it is the caller's ladder seat — the control board's
        // distance-ranked furniture band locally, the peer board's own cluster remotely (both
        // adopt this renderer). The material's renderQueue is NOT written either —
        // Sprites/Default's own Transparent (3000) is exactly the queue the rest of that band uses.
        Material rim = BoardVisual.Unlit(Color.white);
        mr.sharedMaterial = rim;
        go.SetActive(false);

        var frame = new BoardFrame(contour, planeZ, faceSign, bounds, go, filter, mr, rim, mesh2);

        if (!_loggedBuilt)
        {
            _loggedBuilt = true;
            VRLog.Info("Board", $"Focus stroke: ONE closed contour stroke built for '{label}' — "
                                + $"{contour.Length} contour vertices ({(usedTrace ? "hull SAGGED onto the mesh boundary, then MEDIAN+SMOOTHED" : "CONVEX-HULL fallback")}"
                                + $", {hull.Count} hull edges, sag mean {meanSag * 1000f:0.##} mm / max "
                                + $"{maxSag * 1000f:0.##} mm, {gapBuckets} bucket(s) had no evidence and "
                                + $"were filled circularly) from {meshes} board mesh(es) and "
                                + $"{cloud.Count} projected points. Board-local z spans "
                                + $"{minZ:0.####}…{maxZ:0.####} (depth {depth * 1000f:0.#} mm); the "
                                + $"decorated face is the {(faceSign > 0f ? "MAX" : "MIN")}-z end "
                                + $"({(faceFromAnchors ? "derived from the four FBX anchors" : "ANCHORS NOT FOUND — assumed")}) "
                                + $"at z {facePlane:0.####}, and the ring is ONE FLAT PLANE at the board's "
                                + $"MID-THICKNESS, z {planeZ:0.####} (user: 'etwa mittig in der Höhe/Dicke "
                                + "… nicht solche Wellen … sondern gerade'). No absolute clamp (ModBuild "
                                + "84's ±20 mm clamp bound here and buried the ring 11.7 mm inside the "
                                + "board) and no per-vertex drape (ModBuild 85's — that is what waved). "
                                + "Footprint "
                                + $"{(max.x - min.x):0.###} × {(max.y - min.y):0.###} m. Exactly 1 ring "
                                + "mesh on 1 renderer with 1 material and 1 submesh; NO geometry is "
                                + "emitted for any interior feature (the contour never leaves the "
                                + "hull's own boundary band), and no dark keyline (user: 'den "
                                + "schwarzen Rahmen braucht es auch nicht').");
        }
        return frame;
    }

    /// <summary>True when <paramref name="t"/> hangs below one of the six FBX anchors (i.e. it is
    /// mod cargo parked ON the board, not the board). Walks up to <paramref name="stop"/>, which is
    /// the prefab root, so the loop always terminates.</summary>
    private static bool UnderAnchor(Transform t, Transform stop)
    {
        for (Transform? p = t; p != null && p != stop; p = p.parent)
        {
            for (int i = 0; i < AnchorNames.Length; i++)
            {
                if (p.name == AnchorNames[i])
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// +1 when the board's DECORATED face is its MAX-z end in board-ROOT-local space, −1 when it is
    /// the MIN-z end. Derived exactly the way <c>PlayTray</c> derives the board face frame it seats
    /// every card and keycap with: <c>n = (Slot2−Slot1) × (ShortRest−LongRest)</c> is the board's
    /// normal and −n points OUT of the decorated face. Only the sign of that direction's z matters
    /// here, because the stroke is a flat ring in the board-root XY plane.
    ///
    /// <para>Every shipped prefab answers +1 (measured offline: the assets occupy z ∈ [−depth, 0]
    /// with the decorated face at 0), which is also the assumption when the anchors are missing —
    /// a board without them cannot seat a card either, so it is broken well before this. The log
    /// says which of the two happened.</para>
    /// </summary>
    private static float FaceSign(Transform visual, Transform boardRoot, out bool fromAnchors)
    {
        fromAnchors = false;
        Transform? s1 = FindAnchor(visual, "Slot1");
        Transform? s2 = FindAnchor(visual, "Slot2");
        Transform? shortRest = FindAnchor(visual, "ShortRestToken");
        Transform? longRest = FindAnchor(visual, "LongRestToken");
        if (s1 == null || s2 == null || shortRest == null || longRest == null)
            return 1f;

        Vector3 a = boardRoot.InverseTransformPoint(s1.position);
        Vector3 b = boardRoot.InverseTransformPoint(s2.position);
        Vector3 c = boardRoot.InverseTransformPoint(shortRest.position);
        Vector3 d = boardRoot.InverseTransformPoint(longRest.position);
        Vector3 outward = -Vector3.Cross(b - a, c - d);
        if (Mathf.Abs(outward.z) < 1e-6f)
            return 1f; // anchors are degenerate/coplanar with z — no usable statement
        fromAnchors = true;
        return outward.z > 0f ? 1f : -1f;
    }

    private static Transform? FindAnchor(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? hit = FindAnchor(root.GetChild(i), name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------- drawing --

    /// <summary>Show the stroke in <paramref name="tint"/>, or hide it when null.</summary>
    internal void Apply(Color? tint)
    {
        if (_go == null)
            return;
        if (tint == null)
        {
            Show(false);
            return;
        }

        Bake(FocusCue.OutlineEdgeBiteLocal, FocusCue.OutlineRimWidthLocal);
        if (_rimMaterial != null)
            _rimMaterial.color = tint.Value;
        Show(true);
    }

    internal void Destroy()
    {
        if (_go != null)
            Object.Destroy(_go);
        if (_mesh != null)
            Object.Destroy(_mesh);
        // OWN instance — nothing else wears it.
        if (_rimMaterial != null)
            Object.Destroy(_rimMaterial);
    }

    private void Show(bool shown)
    {
        if (_shown == shown)
            return;
        _shown = shown;
        if (_go != null)
            _go.SetActive(shown);
    }

    // ----------------------------------------------------------------------------- ring geometry --

    /// <summary>
    /// Rebuild the ring for the given radii (board-local metres): the stroke spans
    /// −<paramref name="bite"/> … −<paramref name="bite"/> + <paramref name="rim"/> measured
    /// outward from the contour, i.e. it starts <paramref name="bite"/> INSIDE the board's own edge.
    /// A no-op unless a width actually changed, which outside the MR toggle it never does.
    /// </summary>
    private void Bake(float bite, float rim)
    {
        int biteU = Mathf.RoundToInt(bite * 1e6f);
        int rimU = Mathf.RoundToInt(rim * 1e6f);
        if (biteU == _bakedBite && rimU == _bakedRim)
            return;
        _bakedBite = biteU;
        _bakedRim = rimU;

        int n = _contour.Length;
        Vector2[] cIn = Offset(_contour, -bite);
        Vector2[] cOut = Offset(_contour, -bite + rim);

        int vertexCount = 2 * n;
        var verts = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var colors = new Color32[vertexCount];
        var white = new Color32(255, 255, 255, 255);
        // The unlit shader multiplies by the vertex COLOUR channel; a mesh without one feeds it an
        // undefined value (black on some drivers), which is how an "invisible" stroke happens.
        var faceNormal = new Vector3(0f, 0f, _faceSign); // out of the board's decorated face

        int v = 0;
        int rimBase = Fill(verts, normals, colors, ref v, cIn, cOut, _planeZ, faceNormal, white);

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.normals = normals;
        _mesh.colors32 = colors;
        _mesh.subMeshCount = 1;
        _mesh.SetTriangles(Ring(rimBase, n), 0, calculateBounds: false);
        // The contour's bounds, never the stroke's — see the class doc (measured board extents must
        // not twitch by the stroke width every time the cue blinks on).
        _mesh.bounds = _bounds;

        if (_filter.sharedMesh != _mesh)
            _filter.sharedMesh = _mesh;
    }

    /// <summary>Append the 2 × <c>n</c> vertices of one band (inner ring then outer ring) and return
    /// the index the band starts at. ONE z for every vertex — the ring is planar by construction,
    /// which is the whole point of THE PLANE: a flat ring cannot wave.</summary>
    private static int Fill(Vector3[] verts, Vector3[] normals, Color32[] colors, ref int v,
                            Vector2[] inner, Vector2[] outer, float z, Vector3 normal, Color32 color)
    {
        int start = v;
        int n = inner.Length;
        for (int i = 0; i < n; i++)
        {
            verts[v] = new Vector3(inner[i].x, inner[i].y, z);
            normals[v] = normal;
            colors[v] = color;
            v++;
        }
        for (int i = 0; i < n; i++)
        {
            verts[v] = new Vector3(outer[i].x, outer[i].y, z);
            normals[v] = normal;
            colors[v] = color;
            v++;
        }
        return start;
    }

    private static int[] Ring(int baseIndex, int n)
    {
        var tris = new int[n * 6];
        Stitch(tris, 0, baseIndex, n);
        return tris;
    }

    /// <summary>Stitch a closed band: quad i joins inner[i], outer[i], outer[i+1], inner[i+1].
    /// The material is Cull Off, so the winding is correctness rather than necessity.</summary>
    private static void Stitch(int[] tris, int at, int baseIndex, int n)
    {
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = baseIndex + i;          // inner i
            int b = baseIndex + j;          // inner i+1
            int c = baseIndex + n + i;      // outer i
            int d = baseIndex + n + j;      // outer i+1
            tris[at++] = a; tris[at++] = d; tris[at++] = c;
            tris[at++] = a; tris[at++] = b; tris[at++] = d;
        }
    }

    // --------------------------------------------------------------------------- contour maths --

    /// <summary>
    /// The traced outer boundary of <paramref name="cloud"/> in counter-clockwise order: the convex
    /// <paramref name="hull"/> with every edge SAGGED onto the mesh's own boundary.
    ///
    /// <para>Each hull edge is cut into <see cref="SagBucketLocal"/>-long buckets. A bucket keeps
    /// the LEAST-INWARD cloud point that projects into it and lies within
    /// <see cref="SagTrustLocal"/> of the edge; that point's inward distance is the bucket's sag,
    /// and the contour vertex is the bucket's midpoint pulled in by it. Buckets with no such point
    /// are linearly interpolated between their populated neighbours, with sag 0 pinned at both hull
    /// VERTICES — which is exact, because a hull vertex IS a cloud point and therefore has no sag.
    /// On a long straight rim run (where a decimated mesh has no vertex to offer) that interpolation
    /// reproduces the hull edge, which is the correct answer there.</para>
    ///
    /// <para>Every property the shipped convex hull was chosen for survives, because this IS the
    /// hull, moved:
    /// <list type="bullet">
    /// <item>the contour can only ever move INWARD from the hull, and never further than
    ///   <see cref="SagTrustLocal"/> — it is bounded on both sides by construction, so it cannot
    ///   wander into the board's interior art;</item>
    /// <item>no interior feature can be drawn: a recess, well or dial is metres — at minimum, more
    ///   than 10 mm — inside the hull and can never win a bucket;</item>
    /// <item>the output is ONE closed polygon walked once around the hull, so no fragment
    ///   ("Versprengung") is expressible.</item>
    /// </list>
    /// Null when the hull is degenerate; the caller then uses the hull itself.</para>
    ///
    /// <para>Cost: one pass over the cloud per hull edge (~1.2 M point tests on a shipped board,
    /// tens of milliseconds once, on the frame the board is built).</para>
    /// </summary>
    private static List<Vector2>? Trace(List<Vector3> cloud, List<Vector2> hull,
                                        out float meanSag, out float maxSag, out int gapBuckets)
    {
        meanSag = 0f;
        maxSag = 0f;
        gapBuckets = 0;
        int h = hull.Count;
        if (h < MinContourVertices)
            return null;

        Vector3[] pts = cloud.ToArray(); // array indexing: this is the hot loop of the whole build

        // ONE profile around the WHOLE closed hull, not one per edge: every filter below wraps, so
        // a hull vertex is no longer a seam the contour is pinned back to (see THE SAG PROFILE).
        var mid = new List<Vector2>(1024);
        var nrm = new List<Vector2>(1024);
        var sag = new List<float>(1024);
        var has = new List<bool>(1024);

        var sagE = new float[MaxBucketsPerEdge];
        var hasE = new bool[MaxBucketsPerEdge];

        for (int i = 0; i < h; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % h];
            Vector2 e = b - a;
            float len = e.magnitude;
            if (len < 1e-6f)
                continue;
            Vector2 dir = e / len;
            var normal = new Vector2(dir.y, -dir.x); // outward for a CCW hull
            int m = Mathf.Clamp(Mathf.RoundToInt(len / SagBucketLocal), 1, MaxBucketsPerEdge);
            for (int k = 0; k < m; k++)
            {
                sagE[k] = 0f;
                hasE[k] = false;
            }

            for (int p = 0; p < pts.Length; p++)
            {
                float dx = pts[p].x - a.x;
                float dy = pts[p].y - a.y;
                float s = dx * normal.x + dy * normal.y; // <= 0 inside a supporting line
                if (s > 0f || s < -SagTrustLocal)
                    continue;
                float t = dx * dir.x + dy * dir.y;
                if (t < 0f || t > len)
                    continue;
                int k = (int)(t / len * m);
                if (k >= m)
                    k = m - 1;
                // The cloud carries z only so the caller can measure the board's DEPTH and its
                // mid-thickness; the contour itself is a plan-view statement (THE PLANE).
                if (!hasE[k] || s > sagE[k])
                {
                    hasE[k] = true;
                    sagE[k] = s;
                }
            }

            for (int k = 0; k < m; k++)
            {
                mid.Add(a + dir * ((k + 0.5f) / m * len));
                nrm.Add(normal);
                sag.Add(sagE[k]);
                has.Add(hasE[k]);
            }
        }

        int count = mid.Count;
        if (count < MinContourVertices)
            return null;
        for (int k = 0; k < count; k++)
        {
            if (!has[k])
                gapBuckets++;
        }
        if (gapBuckets == count)
            return null; // no evidence anywhere — the hull IS the answer, the caller uses it

        FillCircular(sag, has);
        MedianCircular(sag, SagMedianWindow);
        for (int pass = 0; pass < SagSmoothPasses; pass++)
            SmoothCircular(sag, SagSmoothWindow);

        var contour = new List<Vector2>(count);
        double sagSum = 0d;
        for (int k = 0; k < count; k++)
        {
            // Belt on the construction's own bound: every filter above is a selection or an average
            // over values already in [−SagTrustLocal, 0], so this cannot bite — it just makes the
            // guarantee unconditional rather than an argument about the filters.
            float s = Mathf.Clamp(sag[k], -SagTrustLocal, 0f);
            contour.Add(new Vector2(mid[k].x + nrm[k].x * s, mid[k].y + nrm[k].y * s));
            sagSum += -s;
            if (-s > maxSag)
                maxSag = -s;
        }

        meanSag = (float)(sagSum / count);
        return contour;
    }

    /// <summary>
    /// Fill every entry of a CLOSED profile whose <paramref name="has"/> flag is false by linear
    /// interpolation between the nearest populated entries on either side, wrapping around the end.
    /// No value is pinned at a hull vertex: that pin is exactly what scalloped ModBuild 84's
    /// contour (see THE SAG PROFILE in the class doc). Requires at least one populated entry, which
    /// the caller checks.
    /// </summary>
    private static void FillCircular(List<float> values, List<bool> has)
    {
        int n = values.Count;
        int first = -1;
        for (int i = 0; i < n; i++)
        {
            if (has[i])
            {
                first = i;
                break;
            }
        }
        if (first < 0)
            return;

        int populated = 0;
        for (int i = 0; i < n; i++)
        {
            if (has[i])
                populated++;
        }
        if (populated == 1)
        {
            // Nothing to interpolate BETWEEN: one measurement is the only statement there is.
            float only = values[first];
            for (int i = 0; i < n; i++)
                values[i] = only;
            return;
        }

        int prev = first;
        // Walk the whole ring once starting from a populated entry, so every gap is bracketed.
        for (int step = 1; step <= n; step++)
        {
            int i = (first + step) % n;
            if (!has[i] && step < n)
                continue;
            int at = has[i] ? i : first;
            int span = ((at - prev) % n + n) % n;
            if (span > 1)
            {
                float from = values[prev];
                float to = values[at];
                for (int g = 1; g < span; g++)
                    values[(prev + g) % n] = Mathf.Lerp(from, to, (float)g / span);
            }
            prev = at;
        }
    }

    /// <summary>Circular median filter of odd width <paramref name="window"/> — the spike killer.
    /// A median only ever outputs one of its inputs, so it cannot move the profile out of the band
    /// the trace guarantees.</summary>
    private static void MedianCircular(List<float> values, int window)
    {
        int n = values.Count;
        if (window < 3 || n < window)
            return;
        int half = window / 2;
        var src = values.ToArray();
        var win = new float[window];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < window; k++)
                win[k] = src[((i + k - half) % n + n) % n];
            System.Array.Sort(win);
            values[i] = win[half];
        }
    }

    /// <summary>One pass of a circular box blur of odd width <paramref name="window"/>. Repeated
    /// <see cref="SagSmoothPasses"/> times this approximates a Gaussian; being an average of values
    /// inside the band, its output is inside the band too.</summary>
    private static void SmoothCircular(List<float> values, int window)
    {
        int n = values.Count;
        if (window < 3 || n < window)
            return;
        int half = window / 2;
        var src = values.ToArray();
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            for (int k = -half; k <= half; k++)
                sum += src[((i + k) % n + n) % n];
            values[i] = sum / window;
        }
    }


    /// <summary>
    /// The convex hull of <paramref name="cloud"/> in counter-clockwise order (Andrew's monotone
    /// chain), preceded by an Akl–Toussaint octagon reject that typically discards well over 90 % of
    /// the points before the sort. Kept as the RADIAL TRACE's fallback and as its area reference.
    /// </summary>
    private static List<Vector2> Hull(List<Vector3> cloud)
    {
        List<Vector2> pts = Octagon(cloud);
        pts.Sort(static (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var hull = new List<Vector2>(pts.Count + 1);
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            for (int k = 0; k < pts.Count; k++)
            {
                Vector2 p = pts[pass == 0 ? k : pts.Count - 1 - k];
                while (hull.Count - start >= 2
                       && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1); // the pass' last point opens the next pass
        }
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// <summary>Akl–Toussaint: keep only the points OUTSIDE the octagon spanned by the extremes of
    /// x, y, x+y and x−y. Everything discarded is strictly interior and provably cannot be on the
    /// hull.</summary>
    private static List<Vector2> Octagon(List<Vector3> cloud)
    {
        Vector2 xMin = cloud[0], xMax = cloud[0], yMin = cloud[0], yMax = cloud[0];
        Vector2 sMin = cloud[0], sMax = cloud[0], dMin = cloud[0], dMax = cloud[0];
        for (int i = 1; i < cloud.Count; i++)
        {
            Vector2 p = cloud[i];
            if (p.x < xMin.x) xMin = p;
            if (p.x > xMax.x) xMax = p;
            if (p.y < yMin.y) yMin = p;
            if (p.y > yMax.y) yMax = p;
            if (p.x + p.y < sMin.x + sMin.y) sMin = p;
            if (p.x + p.y > sMax.x + sMax.y) sMax = p;
            if (p.x - p.y < dMin.x - dMin.y) dMin = p;
            if (p.x - p.y > dMax.x - dMax.y) dMax = p;
        }

        // The eight extremes in CCW order — right, up-right, up, up-left, left, down-left, down,
        // down-right — i.e. the point each of those eight directions maximises. Repeated or
        // collinear entries (a cloud with no diagonal extreme of its own) collapse harmlessly: the
        // inside test below simply never rejects across a degenerate edge.
        Vector2[] oct = { xMax, sMax, yMax, dMin, xMin, sMin, yMin, dMax };

        var kept = new List<Vector2>(256);
        for (int i = 0; i < cloud.Count; i++)
        {
            if (!InsideConvex(oct, cloud[i]))
                kept.Add(cloud[i]);
        }
        for (int i = 0; i < oct.Length; i++)
            kept.Add(oct[i]); // the octagon's own corners ARE hull points — never drop them
        return kept;
    }

    /// <summary>True when <paramref name="p"/> is inside the CCW convex polygon
    /// <paramref name="poly"/>. Degenerate (repeated) polygon vertices simply never reject.</summary>
    private static bool InsideConvex(Vector2[] poly, Vector2 p)
    {
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Length];
            if (Cross(a, b, p) < 0f)
                return false;
        }
        return true;
    }

    /// <summary>Drop contour vertices closer than <see cref="MinEdgeLocal"/> to the one before them,
    /// so no miter is computed from a sub-millimetre edge. Null when fewer than three survive or the
    /// input is null.</summary>
    private static Vector2[]? Simplify(List<Vector2>? contour)
    {
        if (contour == null || contour.Count < MinContourVertices)
            return null;
        float minSq = MinEdgeLocal * MinEdgeLocal;
        var kept = new List<Vector2>(contour.Count);
        for (int i = 0; i < contour.Count; i++)
        {
            if (kept.Count == 0 || (contour[i] - kept[kept.Count - 1]).sqrMagnitude >= minSq)
                kept.Add(contour[i]);
        }
        while (kept.Count > MinContourVertices
               && (kept[0] - kept[kept.Count - 1]).sqrMagnitude < minSq)
        {
            kept.RemoveAt(kept.Count - 1);
        }
        return kept.Count >= MinContourVertices ? kept.ToArray() : null;
    }

    /// <summary>
    /// Push a CCW contour outward by <paramref name="d"/> board-local metres along each vertex'
    /// miter (a NEGATIVE <paramref name="d"/> pulls it inward, which is how the stroke bites onto
    /// the rim). Exact for the straight runs; at a corner the miter cuts the true offset's circular
    /// arc, which on a five-hundred-vertex contour turning a fraction of a degree at a time is a
    /// sub-hundredth-of-a-millimetre difference. <see cref="MinMiterCos"/> caps a pathologically
    /// sharp vertex so a spike can never grow out of the stroke.
    /// </summary>
    private static Vector2[] Offset(Vector2[] contour, float d)
    {
        int n = contour.Length;
        var edgeNormal = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 e = contour[(i + 1) % n] - contour[i];
            float len = e.magnitude;
            // A zero-length edge cannot happen after Simplify; fall back to the previous normal.
            edgeNormal[i] = len > 1e-9f ? new Vector2(e.y / len, -e.x / len)
                                        : (i > 0 ? edgeNormal[i - 1] : Vector2.up);
        }

        var outContour = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = edgeNormal[(i - 1 + n) % n];
            Vector2 next = edgeNormal[i];
            Vector2 miter = prev + next;
            float len = miter.magnitude;
            if (len < 1e-9f)
            {
                outContour[i] = contour[i] + next * d;
                continue;
            }
            miter /= len;
            float cos = Mathf.Max(Vector2.Dot(miter, next), MinMiterCos);
            outContour[i] = contour[i] + miter * (d / cos);
        }
        return outContour;
    }

    private static void Fallback(string label, string why)
    {
        if (_loggedFallback)
            return;
        _loggedFallback = true;
        VRLog.Warn("Board", $"Focus stroke for '{label}' falls back to the RECTANGLE frame: {why}. "
                            + "The cue still works; it just circumscribes the board instead of "
                            + "tracing its edge.");
    }
}
