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
/// strip, so the resting value must be white or every rod is drawn through a brass filter. Call
/// sites that keep a "resting colour" (<c>GrabbableModal._barTint</c>,
/// <c>PanelGrabHandle.SetBarBaseColor</c>) must therefore rest at <see cref="RestingTint"/>, not at
/// the old brass. If the texture is missing, <see cref="Build"/> falls back to the historic brass on
/// an untextured material, so a broken resource loses the wood grain and keeps a usable handle.</para>
///
/// <para><b>NO COLLIDER IS CREATED HERE, EVER.</b> Both call sites own grip surfaces that are not
/// geometry and must not follow it: <c>GrabbableModal</c> hands a <c>BoxCollider</c> to
/// <c>PanelGrabHandle.SetBarCollider</c> as the far RAY's only target (the lost-menu fix), and
/// <c>PlayTray</c> keeps a 62 %-wide trigger the card dock-apron arbitration reads as
/// <c>_handleZone</c>. Changing what the bar LOOKS like must not change where it can be grabbed
/// from, so this class draws and does nothing else.</para>
/// </summary>
internal sealed class GrabBarVisual
{
    /// <summary>The resting tint of a TEXTURED rod: white, because the colour now lives in the
    /// strip and this value multiplies it. See the class doc — call sites that used to rest at
    /// brass must rest here instead.</summary>
    internal static readonly Color RestingTint = Color.white;

    /// <summary>The flat brass both bars were tinted before they had a texture
    /// (<c>PlayTray.BuildHandle</c>, <c>GrabbableModal.PrivateBarColor</c> and the mirrored
    /// <c>RemoteBoardFurniture.HandleColor</c> all carried this same literal). Used ONLY as the
    /// fallback when the embedded strip could not be loaded, so a missing resource degrades to
    /// exactly the bar that shipped yesterday.</summary>
    internal static readonly Color UntexturedFallback = new(0.62f, 0.50f, 0.28f);

    private const string Scope = "Core";

    /// <summary>Forwarded from <see cref="GrabBarMesh.NormalStrength"/> — declared there because
    /// the Unity preview symlinks that file and must shade the rods the same way.</summary>
    private const float NormalStrength = GrabBarMesh.NormalStrength;

    /// <summary>Forwarded from <see cref="GrabBarMesh.SpecStrength"/>.</summary>
    private const float SpecStrength = GrabBarMesh.SpecStrength;

    private readonly Transform _shaft;
    private readonly Transform _capA;
    private readonly Transform _capB;
    private readonly float _radius;
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
    /// <param name="radius">Rod radius in LOCAL metres. The two bars shipped 0.024 m thick, so
    /// 0.012 keeps them exactly as thick as they were.</param>
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

        Mesh shaftMesh = GrabBarMesh.Shaft(radius);
        Mesh capMesh = GrabBarMesh.Cap(radius);

        var bar = new GrabBarVisual(
            root,
            Piece(root, "Shaft", shaftMesh, material),
            Piece(root, "CapA", capMesh, material),
            Piece(root, "CapB", capMesh, material),
            radius, material, maps.Albedo != null);

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
    /// Lay the rod out for a total end-to-end length, in the root's local metres.
    ///
    /// <para>Cheap enough for a per-frame caller — <c>GrabbableModal.SyncBar</c> is one — because it
    /// is three transform writes and no allocation. The shaft is scaled ONLY along X, which a
    /// circular cross-section is indifferent to; the caps are moved, never scaled.</para>
    ///
    /// <para>A length below two caps would put the caps through each other. Rather than draw a knot,
    /// the shaft collapses to nothing and the two caps meet at the middle, which is the honest
    /// picture of "this bar is as short as this bar gets" and is what the window's own
    /// <c>MinBarWidth</c> floor already prevents in practice.</para>
    /// </summary>
    internal void SetLength(float length)
    {
        GrabBarMesh.Pose(length, _radius, out float shaft, out Vector3 left, out Vector3 right);
        _shaft.localScale = new Vector3(shaft, 1f, 1f);
        _capA.localPosition = left;
        _capB.localPosition = right;
    }
}
