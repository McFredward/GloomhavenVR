using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// ONE DRAWN GRAB BAR: a shaft that may be stretched along its axis and two end caps that never
/// are, built from <see cref="GrabBarMesh"/> and textured from <see cref="GrabBarTexture"/>.
///
/// <para><b>THE WHOLE CONTRACT IS <see cref="SetLength"/>.</b> Call sites used to write
/// <c>bar.localScale = new Vector3(length, thickness, thickness)</c> on a cube. A rod cannot take
/// that write: stretching along the axis leaves the shaft's circular cross-section circular but
/// smears the domed caps into ellipsoids and flattens the beaded rings. So the length is handed in
/// instead, and this class spends it — the shaft's X scale, and where the two caps sit. The
/// THICKNESS is fixed at build time and is never scaled, which is the point.</para>
///
/// <para><b>ONE MATERIAL, THREE RENDERERS, AND THAT IS WHY THE HIGHLIGHT STILL WORKS.</b>
/// <c>PanelGrabHandle.OnGrabHighlight</c> lights the bar with a single
/// <c>_bar.sharedMaterial.color</c> write. The three pieces here share ONE Material instance, so
/// that unchanged line still lights the whole rod and <c>PanelGrabHandle.Init(owner, renderer, …)</c>
/// keeps its signature — <see cref="Renderer"/> is the one it should be handed.
/// <see cref="Renderers"/> exists for the callers that must touch ALL of them: the window bar rides
/// <c>CanvasConversion.RegisterOrderFollower</c>, and registering one piece of three would sort the
/// shaft over the menu and leave both caps behind it.</para>
///
/// <para><b>THE BASE COLOUR IS NOW WHITE, AND THAT IS A REAL CHANGE FOR CALL SITES.</b> The colour
/// used to BE the bar — a flat brass <c>(0.62, 0.50, 0.28)</c>. It is now a TINT multiplied onto the
/// strip, so the resting value must be white or every rod is drawn through a brass filter. A call
/// site that keeps a "resting colour" must therefore rest at <see cref="RestingTint"/>, not at the
/// old brass. The window bar no longer keeps one at all: <c>PanelGrabHandle.Init</c> seeds its
/// highlight fallback from the MATERIAL of the renderer it is handed, which is this class's, so the
/// right answer arrives without anyone naming it — and the blue shared-window tint that used to
/// re-point it was dropped by the user on 2026-08-28 in favour of a corner network badge, taking
/// <c>GrabbableModal._barTint</c> and <c>PanelGrabHandle.SetBarBaseColor</c> with it. If the texture
/// is missing, <see cref="Build"/> falls back to the historic brass on an untextured material, so a
/// broken resource loses the wood grain, keeps a usable handle, and — through that same seed — keeps
/// a highlight that returns to brass rather than to white.</para>
///
/// <para><b>THE PALM GRIP SURFACES ARE NOT GEOMETRY AND STILL DO NOT FOLLOW IT.</b>
/// <c>PlayTray</c> keeps its 62 %-wide trigger, which the card dock-apron arbitration reads as
/// <c>_handleZone</c>, and <c>GrabbableModal</c> keeps the generous frame zone the near-hand grab
/// uses. Those are deliberately roomier than the bar and must stay that way — a hand reaching for a
/// handle should not have to be accurate.</para>
///
/// <para><b>THE LASER TARGET IS DIFFERENT, AND IT IS NOW ROUND.</b> That one is aimed, not reached
/// for, so it should sit where the rod actually is. <see cref="AttachLaserTarget"/> builds a
/// <c>CapsuleCollider</c> along the rod's axis and keeps it in step with
/// <see cref="SetLength"/>; the call site hands it to <c>PanelGrabHandle.SetBarCollider</c>, which
/// is the collider <c>RayGrabDriver</c> tests EXCLUSIVELY for a far-ray grab (the lost-menu fix).
/// It is OPT-IN: a caller that does not ask still gets no collider from this class at all.</para>
/// </summary>
internal sealed class GrabBarVisual
{
    /// <summary>The resting tint of a TEXTURED rod: white, because the colour now lives in the
    /// strip and this value multiplies it. See the class doc — call sites that used to rest at
    /// brass must rest here instead.</summary>
    internal static readonly Color RestingTint = Color.white;

    /// <summary>The flat brass both bars were tinted before they had a texture
    /// (<c>PlayTray.BuildHandle</c>, <c>GrabbableModal</c>'s since-deleted <c>PrivateBarColor</c> and
    /// the mirrored <c>RemoteBoardFurniture.HandleColor</c> all carried this same literal). Used ONLY as the
    /// fallback when the embedded strip could not be loaded, so a missing resource degrades to
    /// exactly the bar that shipped yesterday.</summary>
    internal static readonly Color UntexturedFallback = new(0.62f, 0.50f, 0.28f);

    private const string Scope = "Core";

    /// <summary>Forwarded from <see cref="GrabBarMesh.NormalStrength"/> — declared there because
    /// the Unity preview symlinks that file and must shade the rods the same way.</summary>
    private const float NormalStrength = GrabBarMesh.NormalStrength;

    /// <summary>Forwarded from <see cref="GrabBarMesh.SpecStrength"/>.</summary>
    private const float SpecStrength = GrabBarMesh.SpecStrength;

    /// <summary>
    /// How much wider than the rod the laser capsule is.
    ///
    /// <para>The box it replaces was <c>BarColliderPad = 1.5</c> — half again as thick as the bar
    /// in BOTH cross-section axes, so a beam could grab a handle while visibly missing it by a
    /// third of its own width. 1.10 puts the target essentially on the rod, which is what was
    /// asked for, while leaving a little for the aim jitter of a hand-held controller at arm's
    /// length. That jitter is real and already conceded elsewhere in this codebase: card colliders
    /// are granted a minimum ANGULAR half-size against the beam for the same reason
    /// (<c>FanSweep</c>'s near-miss rescue). A capsule at 1.10 R still sits well inside the box it
    /// replaces, so nothing that was hittable before at the rod's own silhouette stops being
    /// hittable now.</para>
    /// </summary>
    private const float LaserPad = 1.10f;

    private readonly Transform _shaft;
    private readonly Transform _capA;
    private readonly Transform _capB;
    private readonly float _radius;

    /// <summary>The repeat count the shaft's current mesh was built for; −1 until the first
    /// <see cref="SetLength"/>. Compared against the quantised want, so a bar whose length wobbles
    /// inside one quantum never touches its MeshFilter.</summary>
    private float _tiles = -1f;

    private MeshFilter? _shaftFilter;

    private CapsuleCollider? _laserTarget;

    /// <summary>The round laser target, or null until <see cref="AttachLaserTarget"/> is called.
    /// Hand it to <c>PanelGrabHandle.SetBarCollider</c>.</summary>
    internal Collider? LaserTarget => _laserTarget;
    private readonly List<MeshRenderer> _renderers = new(3);

    /// <summary>The bar's own root. Call sites parent it and position it; they must NOT scale it
    /// non-uniformly (see <see cref="SetLength"/>). A uniform scale is fine and is what the
    /// two-hand resize already applies further up the chain.</summary>
    internal Transform Root { get; }

    /// <summary>The renderer to hand <c>PanelGrabHandle.Init</c> — the shaft's. Every piece shares
    /// its material, so tinting this one tints the rod.</summary>
    internal MeshRenderer Renderer => _renderers[0];

    /// <summary>All three renderers, for callers that must register each one (sorting order,
    /// render-root hides, layer sweeps).</summary>
    internal IReadOnlyList<MeshRenderer> Renderers => _renderers;

    /// <summary>The one Material the three pieces share.</summary>
    internal Material Material { get; }

    /// <summary>True when the embedded strip decoded and the rod is actually textured. False means
    /// the fallback brass is being drawn — worth a log line at the call site, not a crash.</summary>
    internal bool Textured { get; }

    private GrabBarVisual(Transform root, Transform shaft, Transform capA, Transform capB,
                          float radius, Material material, bool textured)
    {
        Root = root;
        _shaft = shaft;
        _capA = capA;
        _capB = capB;
        _radius = radius;
        Material = material;
        Textured = textured;
    }

    /// <summary>
    /// Build a rod under <paramref name="parent"/>.
    /// </summary>
    /// <param name="style">Which of the four strips to wear. <see cref="GrabBarStyle.Generic"/> is
    /// member 0, so a caller that does not say gets the neutral window rod rather than somebody
    /// else's board material.</param>
    /// <param name="radius">Rod radius in LOCAL metres — pass
    /// <see cref="GrabBarMesh.DefaultRadius"/> (0.014). This doc used to say 0.012, "so the bars
    /// stay exactly as thick as they were", and that stopped being true when the profile was
    /// measured against the design sheet: 0.014 is what 12.5 : 1 costs at the board bar's 0.352 m,
    /// and 0.012 read visibly more slender than the sheet. A call site following the old prose
    /// would have shipped one bar at the wrong thickness and the other at the right one — the
    /// "comment asserting a parity the code does not have" pattern, caught by a lane rather than by
    /// a checker.</param>
    /// <param name="overlay">TRUE for a rod drawn over a converted UI canvas (the window bars):
    /// uses <c>GloomhavenVR/Overlay</c>, the only shader that exposes <c>_ZTest</c>/<c>_ZWrite</c>,
    /// which is why those widgets stopped being occluded by the board. Overlay is UNLIT, and the
    /// window strip therefore carries its roundness baked into v — see
    /// <c>scripts/grabbar-strips.py</c>. FALSE for a rod in the world (the board bars): uses
    /// <c>GloomhavenVR/BoardLit</c> and is lit for real, which is why the board strips must stay
    /// pure albedo.</param>
    internal static GrabBarVisual Build(Transform parent, string name, GrabBarStyle style,
                                        float radius, bool overlay)
    {
        var rootGo = new GameObject(name);
        Transform root = rootGo.transform;
        root.SetParent(parent, worldPositionStays: false);

        GrabBarTexture.Maps maps = GrabBarTexture.Get(style);
        Material material = BuildMaterial(maps, overlay);

        Mesh shaftMesh = GrabBarMesh.Shaft(radius, 1f);
        Mesh capMesh = GrabBarMesh.Cap(radius);

        var bar = new GrabBarVisual(
            root,
            Piece(root, "Shaft", shaftMesh, material),
            Piece(root, "CapA", capMesh, material),
            Piece(root, "CapB", capMesh, material),
            radius, material, maps.Albedo != null);

        bar._shaftFilter = bar._shaft.GetComponent<MeshFilter>();
        bar._renderers.Add(bar._shaft.GetComponent<MeshRenderer>());
        bar._renderers.Add(bar._capA.GetComponent<MeshRenderer>());
        bar._renderers.Add(bar._capB.GetComponent<MeshRenderer>());

        // The caps are the SAME mesh, the RIGHT one turned to face the other way — which way round
        // that is, and where each sits, is GrabBarMesh.Pose's business and not this file's. It was
        // written out here once, wrongly, and the preview station could not catch it because the
        // preview had its own identical copy of the same error.
        bar._capA.localRotation = Quaternion.identity;
        bar._capB.localRotation = GrabBarMesh.RightCapRotation;

        return bar;
    }

    private static Transform Piece(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    private static Material BuildMaterial(GrabBarTexture.Maps maps, bool overlay)
    {
        // Both shaders come through PlayTray's accessors, which go through Core.BundleShaders — a
        // bare Shader.Find on a "GloomhavenVR/*" name fails the build gate, and for good reason:
        // Shader.Find only sees shaders something has already LOADED, which has cost this project
        // two whole builds.
        Shader? shader = overlay ? Cards.PlayTray.OverlayShader() : Cards.PlayTray.BoardLitShader();
        shader ??= Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
                   ?? Shader.Find("Sprites/Default");

        if (shader == null)
        {
            VRLog.Warn(Scope, "Grab bar: no usable shader at all; the rod will not draw.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        var material = new Material(shader);
        if (maps.Albedo != null)
        {
            material.color = RestingTint;
            if (material.HasProperty("_MainTex"))
                material.mainTexture = maps.Albedo;
        }
        else
        {
            // No strip: keep yesterday's bar rather than a white one.
            material.color = UntexturedFallback;
        }

        if (overlay && material.HasProperty("_ZWrite"))
        {
            // Item 3, unchanged from the cube: force ZWrite ON so the handle draws SOLID and
            // occludes the menu behind it, while ZTest stays at the default LEqual so a hand held
            // physically in front still occludes the handle.
            material.SetInt("_ZWrite", 1);
        }

        // ---- WHERE THE MATERIAL DEPTH COMES FROM, and why the first rods had none ------------
        // The first cut bound the albedo and nothing else. GloomhavenVR/BoardLit gates its whole
        // specular block behind `if (_SpecStrength > 0.0)` and _SpecStrength DEFAULTS TO ZERO, so
        // the branch never executed: the rods rendered matte, with no sheen along the crown and no
        // darkening in the crevices, and read as plastic next to the design sheet. The shader's own
        // header says what it wants — "the AI-authored albedo already carries baked detail; this
        // adds just enough normal-mapped shape so the brass/woodgrain reads" — and it wants three
        // things, not one.
        //
        // The window rod passes none of this: Overlay is unlit and samples _MainTex * _Color only,
        // which is exactly why its strip carries a baked body gradient and a baked highlight
        // instead (see scripts/grabbar-strips.py).
        if (maps.Normal != null && material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", maps.Normal);
            if (material.HasProperty("_NormalStrength"))
                material.SetFloat("_NormalStrength", NormalStrength);
        }
        if (maps.Mrs != null && material.HasProperty("_MRSMap"))
        {
            material.SetTexture("_MRSMap", maps.Mrs);
            if (material.HasProperty("_SpecStrength"))
                material.SetFloat("_SpecStrength", SpecStrength);
        }
        return material;
    }

    /// <summary>
    /// Give this rod a ROUND laser target: a capsule down its axis, sized from the rod itself.
    ///
    /// <para>A capsule rather than a box because a rod IS a capsule — round in cross-section, domed
    /// at both ends. It is also analytic, so it costs a ray-capsule test rather than a mesh
    /// traversal, and it needs no second mesh to keep in step with the tiling one.</para>
    ///
    /// <para>WHAT IT DELIBERATELY DOES NOT MATCH. The rod's silhouette is not a constant radius:
    /// the shaft tapers to <c>0.89 R</c> at its ends and the knobs swell to <c>1.28 R</c>. One
    /// capsule cannot be both. It is sized on the SHAFT, which is the whole graspable run and the
    /// part a player actually aims at; the knobs then stand slightly proud of it. Sizing it on the
    /// knobs instead would have made the target 28 % fatter than the rod along its entire length —
    /// which is the very complaint this replaces, just with a rounder cross-section.</para>
    ///
    /// <para>Idempotent: calling it twice returns the same collider.</para>
    /// </summary>
    internal Collider AttachLaserTarget()
    {
        if (_laserTarget != null)
            return _laserTarget;

        _laserTarget = Root.gameObject.AddComponent<CapsuleCollider>();
        _laserTarget.direction = 0;                 // along X, the rod's own axis
        _laserTarget.isTrigger = true;              // as the box it replaces was
        _laserTarget.radius = _radius * LaserPad;
        // Unity's capsule height INCLUDES the two hemispherical ends, so this is the rod's full
        // end-to-end length. Below 2 R it degenerates to a sphere, which is the honest shape of a
        // bar that short anyway.
        _laserTarget.height = Mathf.Max(_lastLength, _radius * 2f * LaserPad);
        return _laserTarget;
    }

    /// <summary>The length <see cref="SetLength"/> was last given, so a target attached AFTER the
    /// first layout is still the right size.</summary>
    private float _lastLength;

    /// <summary>
    /// Lay the rod out for a total end-to-end length, in the root's local metres.
    ///
    /// <para>Cheap enough for a per-frame caller — <c>GrabbableModal.SyncBar</c> is one. In the
    /// common case it is three transform writes and no allocation: the shaft is scaled ONLY along
    /// X, which a circular cross-section is indifferent to, and the caps are moved, never
    /// scaled.</para>
    ///
    /// <para><b>AND THE TEXTURE DOES NOT STRETCH WITH IT.</b> The shaft repeats its band
    /// <c>length / TileLength</c> times, so a bar twice as long shows twice as much pattern rather
    /// than the same pattern pulled to twice the size. The count is quantised
    /// (<see cref="GrabBarMesh.QuantiseTiles"/>) and the mesh only swapped when that quantised
    /// value actually moves — a window whose ink wobbles by a millimetre re-scales a transform and
    /// touches nothing else. The meshes are cached by count, so the swap is a dictionary hit after
    /// the first time any bar in the process has asked for it.</para>
    ///
    /// <para>A length below two caps would put the caps through each other. Rather than draw a
    /// knot, the shaft collapses to nothing and the two caps meet at the middle, which is the
    /// honest picture of "this bar is as short as this bar gets".</para>
    ///
    /// <para>THIS DOC USED TO CLAIM the window's own <c>MinBarWidth</c> floor "already prevents"
    /// that in practice. It did not: the floor was 0.040 m and the rod's minimum drawn length is
    /// <c>2 x CapLengthInRadii x DefaultRadius</c> = 0.056 m, so a bar clamped to the floor was
    /// drawn ~40 % wider than it asked for and its laser capsule came out shorter than the rod.
    /// The floor has been raised to 0.06; the claim is true now, which it was not when it was
    /// written. Caught by a lane reading the arithmetic, not by a checker — nothing in this
    /// codebase compares a floor in one frame against a minimum in another.</para>
    /// </summary>
    internal void SetLength(float length)
    {
        GrabBarMesh.Pose(length, _radius, out float shaft, out Vector3 left, out Vector3 right);

        float want = GrabBarMesh.QuantiseTiles(shaft / GrabBarMesh.TileLength);
        if (_shaftFilter != null && !Mathf.Approximately(want, _tiles))
        {
            _shaftFilter.sharedMesh = GrabBarMesh.Shaft(_radius, want);
            _tiles = want;
        }

        _shaft.localScale = new Vector3(shaft, 1f, 1f);
        _capA.localPosition = left;
        _capB.localPosition = right;

        _lastLength = length;
        if (_laserTarget != null)
            _laserTarget.height = Mathf.Max(length, _radius * 2f * LaserPad);
    }
}
