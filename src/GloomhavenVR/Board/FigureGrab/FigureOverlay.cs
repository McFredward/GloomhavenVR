using System.Collections.Generic;
using GloomhavenVR.Cards;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Shared factory for the two figure-grab overlays that both paint the bundled
/// <c>GloomhavenVR/Overlay</c> unlit shader (<c>_MainTex * _Color * vertexColor</c>) over a board
/// figure's OWN geometry:
///   • TASK #2 — an ADDITIVE, animated pre-grab highlight that RIDES the live (still-animating)
///     figure by cloning its renderers and sharing the SAME bones, so the shimmer stays registered
///     to the mesh as the idle clip plays (<see cref="CloneRenderersSharingBones"/>);
///   • TASK #3 — an ALPHA-blended translucent GHOST frozen at the figure's home board pose, built
///     as a self-contained snapshot clone of the visual subtree with all scripts/colliders stripped
///     (<see cref="BuildFrozenGhost"/>).
///
/// Both routes use <c>_ZTest = LEqual (4)</c> + <c>_ZWrite = 0</c>, which makes the overlay
/// OCCLUSION-CORRECT: the figure already wrote depth in its opaque pass, so the overlay is hidden by
/// walls/terrain exactly like the mini (verified shader fact — the game's Amp character shaders do
/// NOT expose <c>_EmissionColor</c>, so an emissive glow is a silent no-op; the bundled Overlay
/// shader is the tool that works).
/// </summary>
internal static class FigureOverlay
{
    /// <summary>
    /// A <c>GloomhavenVR/Overlay</c> material tinted <paramref name="color"/>. <paramref name="additive"/>
    /// selects additive (SrcOne/DstOne — task #2 glow) vs alpha (SrcAlpha/OneMinusSrcAlpha — task #3
    /// ghost) blend. Always <c>_ZTest = LEqual</c> + <c>_ZWrite = 0</c> (occlusion-correct) and drawn
    /// in the Transparent queue after the opaque figure. Null when the bundle lacks the shader
    /// (callers then skip the effect rather than falling back to a wall-piercing look).
    /// </summary>
    internal static Material? MakeOverlayMaterial(Color color, bool additive)
    {
        Shader? s = PlayTray.OverlayShader();
        if (s == null)
            return null;

        var m = new Material(s) { color = color };
        if (additive)
        {
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.One);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.One);
        }
        else
        {
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }
        if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)CompareFunction.LessEqual); // 4 — wall-occluded
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    /// <summary>
    /// TASK #2 helper: for every renderer under <paramref name="animatedRoot"/>, create a clone that
    /// re-draws the SAME mesh with <paramref name="overlayMat"/> on every sub-mesh. Skinned meshes
    /// reference the ORIGINAL bones/rootBone, so the clone deforms in lock-step with the live figure
    /// (no per-frame tracking needed). All clones are parented under <paramref name="container"/> so
    /// the caller destroys the whole overlay by destroying that one object. Returns the clone count.
    /// The container should NOT be a child of the figure's Animator object, so the game's own
    /// <c>GetComponentsInChildren&lt;Renderer&gt;</c> passes (e.g. jump-exit opacity, invisibility)
    /// never touch these extra renderers.
    /// </summary>
    internal static int CloneRenderersSharingBones(GameObject animatedRoot, Transform container, Material overlayMat)
    {
        int made = 0;
        Renderer[] renderers = animatedRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null)
                    continue;
                var go = new GameObject("VROverlay");
                go.transform.SetParent(container, worldPositionStays: false);
                var clone = go.AddComponent<SkinnedMeshRenderer>();
                clone.sharedMesh = smr.sharedMesh;
                clone.bones = smr.bones;            // share the LIVE bones → tracks animation
                clone.rootBone = smr.rootBone;
                clone.localBounds = smr.localBounds;
                clone.quality = smr.quality;
                clone.updateWhenOffscreen = smr.updateWhenOffscreen;
                clone.sharedMaterials = FillMaterials(smr.sharedMesh.subMeshCount, overlayMat);
                clone.shadowCastingMode = ShadowCastingMode.Off;
                clone.receiveShadows = false;
                made++;
            }
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            {
                // Static mesh part (e.g. weapon/prop). Parent under the container (so the whole
                // overlay is torn down by destroying that one object — no leak) and match the part's
                // current world transform. It rides the figure via the container (which sits on the
                // figure root); rare bone-driven static parts are an accepted edge case.
                var go = new GameObject("VROverlay");
                go.transform.SetParent(container, worldPositionStays: false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                var cf = go.AddComponent<MeshFilter>();
                cf.sharedMesh = mf.sharedMesh;
                var cr = go.AddComponent<MeshRenderer>();
                cr.sharedMaterials = FillMaterials(mf.sharedMesh.subMeshCount, overlayMat);
                cr.shadowCastingMode = ShadowCastingMode.Off;
                cr.receiveShadows = false;
                made++;
            }
        }
        return made;
    }

    /// <summary>
    /// TASK #3 helper: build a self-contained, FROZEN translucent snapshot of the figure's visual
    /// subtree at world pose <paramref name="worldPos"/>/<paramref name="worldRot"/>/
    /// <paramref name="worldScale"/>. The subtree is Instantiated (so bones keep their exact current
    /// local pose — a frozen snapshot with no Animator), then every non-visual component
    /// (Animator/Cloth/Collider/Rigidbody and ALL game scripts) is destroyed and every renderer is
    /// re-tinted with <paramref name="ghostMat"/>. Returns the ghost root, or null if the subtree is
    /// missing. Caller owns the object and destroys it on release.
    /// </summary>
    internal static GameObject? BuildFrozenGhost(GameObject animatedRoot, Vector3 worldPos, Quaternion worldRot,
        Vector3 worldScale, Material ghostMat)
    {
        if (animatedRoot == null)
            return null;

        GameObject ghost = Object.Instantiate(animatedRoot);
        ghost.name = "VRFigureGhost";
        ghost.transform.SetParent(null, worldPositionStays: false);
        ghost.transform.SetPositionAndRotation(worldPos, worldRot);
        ghost.transform.localScale = worldScale;

        // Strip everything that could animate, simulate, collide, or run game logic — leave ONLY the
        // Transform hierarchy + the mesh renderers so it is an inert frozen silhouette.
        foreach (Animator a in ghost.GetComponentsInChildren<Animator>(true))
            if (a != null) Object.Destroy(a);
        foreach (Cloth c in ghost.GetComponentsInChildren<Cloth>(true))
            if (c != null) Object.Destroy(c);
        foreach (Collider col in ghost.GetComponentsInChildren<Collider>(true))
            if (col != null) Object.Destroy(col);
        foreach (Rigidbody rb in ghost.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) Object.Destroy(rb);
        foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            mb.enabled = false;       // stop it ticking before the deferred Destroy lands
            Object.Destroy(mb);
        }

        // Re-tint every renderer to the translucent ghost material (one shared instance).
        var tint = new List<Renderer>(8);
        foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            int subs = 1;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                subs = smr.sharedMesh.subMeshCount;
            else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                subs = mf.sharedMesh.subMeshCount;
            else
                subs = Mathf.Max(1, r.sharedMaterials.Length);
            r.sharedMaterials = FillMaterials(subs, ghostMat);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            tint.Add(r);
        }

        if (tint.Count == 0)
        {
            Object.Destroy(ghost);
            return null;
        }
        // Own the ghost material's lifetime: Unity does NOT destroy materials with their GameObject,
        // so without this the tint material would leak every time a ghost is torn down.
        ghost.AddComponent<OverlayMaterialOwner>().Init(ghostMat);
        return ghost;
    }

    private static Material[] FillMaterials(int count, Material mat)
    {
        count = Mathf.Max(1, count);
        var arr = new Material[count];
        for (int i = 0; i < count; i++)
            arr[i] = mat;
        return arr;
    }
}

/// <summary>
/// TASK #2 animator: shimmers an additive overlay material over time so the pre-grab highlight
/// visibly pulses (intensity sine on <c>Time.unscaledTime</c>, so it keeps breathing while the game
/// is paused) plus a slow <c>_MainTex</c> scroll for extra life. Lives on the overlay container and
/// owns the material, destroying it in <see cref="OnDestroy"/> so nothing leaks per hover.
/// </summary>
internal sealed class OverlayPulse : MonoBehaviour
{
    private Material? _mat;
    private Color _base;
    private const float PulseHz = 1.6f;
    private const float Floor = 0.45f; // dimmest intensity
    private const float Ceil = 1.0f;   // brightest intensity

    internal void Init(Material mat, Color baseColor)
    {
        _mat = mat;
        _base = baseColor;
    }

    private void Update()
    {
        if (_mat == null)
            return;
        float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * Mathf.PI * 2f);
        float k = Mathf.Lerp(Floor, Ceil, s);
        _mat.color = _base * k;
        // Subtle scroll so the additive pass shimmers even on a flat white _MainTex-lit mesh.
        if (_mat.HasProperty("_MainTex"))
            _mat.mainTextureOffset = new Vector2(0f, Time.unscaledTime * 0.15f % 1f);
    }

    private void OnDestroy()
    {
        if (_mat != null)
        {
            Object.Destroy(_mat);
            _mat = null;
        }
    }
}

/// <summary>
/// Frees an overlay material when its host object is destroyed (Unity does not destroy materials with
/// their GameObject). Used by the frozen ghost, whose material has no other owner.
/// </summary>
internal sealed class OverlayMaterialOwner : MonoBehaviour
{
    private Material? _mat;

    internal void Init(Material mat) => _mat = mat;

    private void OnDestroy()
    {
        if (_mat != null)
        {
            Object.Destroy(_mat);
            _mat = null;
        }
    }
}
