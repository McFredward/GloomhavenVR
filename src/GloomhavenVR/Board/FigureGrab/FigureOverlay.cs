using System;
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
                // current world transform — POSITION, ROTATION **AND SCALE**. Until ModBuild 336
                // the scale was simply never set, so the clone inherited the container's and any
                // part whose own lossyScale differed drew its glow at the wrong SIZE: too small to
                // notice on a mini, and not at all subtle on a 198x diorama. The boss carries
                // exactly such parts (geo_bendyband (1), WP_Scoundrel_Dart, WP_Dummy) while the
                // small drakes' renderers all sit under one uniformly scaled m_AnimatedGameObject,
                // which is the shape of a defect that only ever shows on one figure.
                // <see cref="FigureHighlight.CloneOne"/> already did this; this copy did not.
                // A rare bone-driven static part that MOVES during the hover is still an accepted
                // edge case — the hover is short and the part is small.
                var go = new GameObject("VROverlay");
                go.transform.SetParent(container, worldPositionStays: false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                MatchCloneWorldScale(go.transform, container, r.transform);
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
    /// TASK #3 helper: build a self-contained, translucent ghost of the figure's visual subtree at
    /// world pose <paramref name="worldPos"/>/<paramref name="worldRot"/>/<paramref name="worldScale"/>.
    /// The subtree is Instantiated and stripped of game logic (Cloth/Collider/Rigidbody and ALL
    /// scripts) and of VFX (task #3: ParticleSystems, trails, and any renderer on a distort/FX
    /// shader such as <c>Amp_CharDistort_Low</c> — these previously kept simulating and were
    /// re-tinted into a fog/mist blob at the home cell); every remaining renderer is re-tinted with
    /// <paramref name="ghostMat"/>. Task #4: the <c>Animator</c> is KEPT (root motion disabled,
    /// events muted since their script receivers are stripped) so the ghost plays the same idle in
    /// place at the home pose. <paramref name="preserveOriginal"/> (task #2, the actor's
    /// <c>m_Hilight</c> selection ring): the matching subtree in the ghost keeps its ORIGINAL
    /// materials so the ring at the home cell looks exactly like the game's own. Returns the ghost
    /// root, or null if the subtree is missing. Caller owns the object and destroys it on release.
    /// </summary>
    internal static GameObject? BuildFrozenGhost(GameObject animatedRoot, Vector3 worldPos, Quaternion worldRot,
        Vector3 worldScale, Material ghostMat, out string report, Transform? preserveOriginal = null)
    {
        report = "no source subtree";
        if (animatedRoot == null)
            return null;

        // MEASURE THE SOURCE BEFORE TOUCHING IT — the ghost's own numbers mean nothing without the
        // figure they are supposed to reproduce (see MeasureClones for why this round measures the
        // COPIES and not the originals).
        bool haveSource = CombinedBounds(animatedRoot.transform, out Bounds sourceBounds,
                                         out int sourceRenderers);
        var vfxKilled = new List<string>(4);
        var kept = new List<string>(8);

        GameObject ghost = Object.Instantiate(animatedRoot);
        ghost.name = "VRFigureGhost";
        ghost.transform.SetParent(null, worldPositionStays: false);
        ghost.transform.SetPositionAndRotation(worldPos, worldRot);
        ghost.transform.localScale = worldScale;

        // Task #2 — locate the ghost's twin of the live selection ring BEFORE any stripping (the
        // mapping walks sibling-index chains, which Instantiate preserves exactly).
        Transform? ringTwin = preserveOriginal != null
            ? FindTwin(animatedRoot.transform, preserveOriginal, ghost.transform)
            : null;

        // TASK #4 — keep the Animator so the ghost plays the same idle clip in place. The game
        // scripts that normally zero/drive the animated root are stripped below, so root motion is
        // disabled (the pose must stay put at the home cell); animation events are muted because
        // their MonoBehaviour receivers are gone; always-animate so an offscreen home cell never
        // freezes the ghost mid-pose.
        foreach (Animator a in ghost.GetComponentsInChildren<Animator>(true))
        {
            if (a == null)
                continue;
            a.applyRootMotion = false;
            a.fireEvents = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        // MOD-OWNED SUBTREES GO FIRST (ModBuild 335). Since the ghost is cloned from the ACTOR
        // ROOT rather than m_AnimatedGameObject (see FigureGhosts.GhostSource), the walk can now
        // reach our OWN objects: the additive highlight overlay ("VRFigureHighlight") is parented
        // under that very root, as is the reach volume ("VR_FigureReach"). Ghosting our own glow
        // would leave a second, brighter copy standing at the home cell. Nothing the game ships
        // under an actor starts with these two letters (they are HE_, MO_, WP_, C_*_JNT, Base,
        // Actor(Clone)) — the same prefix rule FigureHighlight uses to avoid overlaying itself.
        StripModOwned(ghost.transform);

        foreach (Cloth c in ghost.GetComponentsInChildren<Cloth>(true))
            if (c != null) Object.Destroy(c);
        foreach (Collider col in ghost.GetComponentsInChildren<Collider>(true))
            if (col != null) Object.Destroy(col);
        foreach (Rigidbody rb in ghost.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) Object.Destroy(rb);
        // TASK #3 — kill every particle system (fog/mist). The system must be destroyed BEFORE its
        // ParticleSystemRenderer (RequireComponent dependency); the renderer falls in the renderer
        // sweep below. Stop+clear immediately so nothing emits during the deferred-Destroy frame.
        foreach (ParticleSystem ps in ghost.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null)
                continue;
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Object.Destroy(ps);
        }
        foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            mb.enabled = false;       // stop it ticking before the deferred Destroy lands
            Object.Destroy(mb);
        }

        // Re-tint every KEPT renderer to the translucent ghost material (one shared instance);
        // destroy VFX renderers outright (task #3). The ring twin keeps its original materials.
        var tint = new List<Renderer>(8);
        foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer || HasVfxShader(r))
            {
                // RECORD WHAT WAS KILLED AND WHY. HasVfxShader is a NAME match on the shader
                // ("Distort"/"Particle"/"Fog"/"FX"), so a character whose BODY is drawn with such a
                // shader loses its body to this branch and still returns a non-null ghost built out
                // of whatever props remain — a ghost that "spawned" and cannot be seen. That is a
                // hypothesis this list is here to confirm or kill, not an established cause.
                if (vfxKilled.Count < 8)
                    vfxKilled.Add($"'{r.name}' ({r.GetType().Name}, shader "
                                  + $"'{FirstShaderName(r)}')");
                r.enabled = false;    // instant off; the Destroy itself is deferred
                Object.Destroy(r);
                continue;
            }
            // NAME WHAT SURVIVED, with the shader it arrived on. "The ghost has 4 renderers" cannot
            // tell a ghost of a dragon from a ghost of the dart in its claw; "MO_Elder_Drake_body on
            // Amp_CharShader_Low, WP_Dummy on ..." can.
            if (kept.Count < 8)
                kept.Add($"'{r.name}' ({r.GetType().Name} on '{FirstShaderName(r)}')");
            if (ringTwin != null && (r.transform == ringTwin || r.transform.IsChildOf(ringTwin)))
            {
                tint.Add(r);          // counts as visual content, but keeps the game's ring look
                continue;
            }
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

        int vfxDestroyed = vfxKilled.Count;
        string vfxSays = vfxDestroyed == 0
            ? "no renderer was destroyed as VFX"
            : $"{vfxDestroyed} renderer(s) destroyed as VFX (particle/trail/line, or a material whose "
              + $"shader name contains Distort/Particle/Fog/FX): {string.Join(", ", vfxKilled)} — if "
              + "the figure's own BODY is in that list, this rule is why the ghost is invisible";

        if (tint.Count == 0)
        {
            report = $"NOTHING LEFT TO TINT — the source subtree '{animatedRoot.name}' offered "
                     + $"{sourceRenderers} renderer(s) and {vfxSays}. No ghost.";
            Object.Destroy(ghost);
            return null;
        }
        // Own the ghost material's lifetime: Unity does NOT destroy materials with their GameObject,
        // so without this the tint material would leak every time a ghost is torn down.
        ghost.AddComponent<OverlayMaterialOwner>().Init(ghostMat);

        report = MeasureRenderers(tint, "GHOST", haveSource, sourceBounds, sourceRenderers)
                 + $" Ghost root at world ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}), local "
                 + $"scale ({worldScale.x:F3}, {worldScale.y:F3}, {worldScale.z:F3}); the live figure "
                 + $"stands at ({animatedRoot.transform.position.x:F2}, "
                 + $"{animatedRoot.transform.position.y:F2}, {animatedRoot.transform.position.z:F2}), "
                 + $"{Vector3.Distance(worldPos, animatedRoot.transform.position):F2} wu away. "
                 + $"{vfxSays}. Tinted {tint.Count} renderer(s)"
                 + (preserveOriginal != null ? " (the selection ring keeps its own materials)" : "")
                 + $"; ghost material shader '{(ghostMat != null && ghostMat.shader != null ? ghostMat.shader.name : "<none>")}' "
                 + $"alpha {(ghostMat != null ? ghostMat.color.a : 0f):F2}. KEPT: "
                 + (kept.Count == 0 ? "nothing" : string.Join(", ", kept))
                 + (tint.Count > kept.Count ? $" (+{tint.Count - kept.Count} more not named)" : "")
                 + ".";
        return ghost;
    }

    /// <summary>Match <paramref name="clone"/>'s WORLD scale to <paramref name="source"/>'s while it
    /// hangs under <paramref name="container"/> — the term the MeshRenderer clone path forgot until
    /// ModBuild 336. Degenerate container axes fall back to 1 rather than dividing by zero.</summary>
    internal static void MatchCloneWorldScale(Transform clone, Transform container, Transform source)
    {
        Vector3 parentLossy = container.lossyScale;
        Vector3 want = source.lossyScale;
        clone.localScale = new Vector3(
            Mathf.Abs(parentLossy.x) > 1e-6f ? want.x / parentLossy.x : 1f,
            Mathf.Abs(parentLossy.y) > 1e-6f ? want.y / parentLossy.y : 1f,
            Mathf.Abs(parentLossy.z) > 1e-6f ? want.z / parentLossy.z : 1f);
    }

    /// <summary>Combined world <see cref="Renderer.bounds"/> of every ENABLED mesh/skinned renderer
    /// under <paramref name="root"/> — the same surface-geometry filter the overlay itself clones,
    /// so the two boxes are comparable. False when there is nothing to measure.</summary>
    internal static bool CombinedBounds(Transform root, out Bounds bounds, out int count)
    {
        bounds = default;
        count = 0;
        if (root == null)
            return false;
        Renderer[] all = root.GetComponentsInChildren<Renderer>(includeInactive: false);
        foreach (Renderer r in all)
        {
            if (r == null || !r.enabled)
                continue;
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
                continue;
            if (count == 0)
                bounds = r.bounds;
            else
                bounds.Encapsulate(r.bounds);
            count++;
        }
        return count > 0;
    }

    /// <summary>
    /// MEASURE THE COPIES, NOT WHAT THEY WERE COPIED FROM.
    ///
    /// <para>Three hardware rounds were spent on an overlay that "fires" and cannot be seen, against
    /// a report built from <c>r.bounds</c> where <c>r</c> was the ORIGINAL renderer. That report said
    /// "3 renderer(s) cloned from the ACTOR ROOT, spanning world y -1.18..5.66" — a true sentence
    /// about the figure and a sentence about nothing at all about the glow. It could not say where
    /// the copies ended up, whether they were enabled, what layer they landed on, whether the head
    /// camera renders that layer, or what shader they carry. The complaint is "I cannot see it"; the
    /// instrument answered "I chose three renderers".</para>
    ///
    /// <para>This measures the CLONES under <paramref name="container"/> and compares their combined
    /// world box against the originals' (<paramref name="sourceBounds"/>), saying plainly when the
    /// two disagree. <see cref="OverlayVisibilityProbe"/> then answers the one question no
    /// build-time measurement can — whether anything actually drew them.</para>
    /// </summary>
    internal static string MeasureClones(Transform container, string what, bool haveSource,
                                         Bounds sourceBounds, int sourceRenderers)
    {
        var made = new List<Renderer>(16);
        if (container != null)
        {
            foreach (Renderer r in container.GetComponentsInChildren<Renderer>(includeInactive: true))
                if (r != null)
                    made.Add(r);
        }
        if (made.Count == 0)
            return $"{what}: NOT ONE CLONE RENDERER EXISTS under '"
                   + (container != null ? container.name : "<null>") + "'.";
        return MeasureRenderers(made, what, haveSource, sourceBounds, sourceRenderers);
    }

    /// <summary>
    /// <see cref="MeasureClones"/> over a list the caller already holds. The ghost path needs this
    /// overload rather than a fresh subtree walk: <c>Object.Destroy</c> is deferred to the end of
    /// the frame, so the renderers <see cref="BuildFrozenGhost"/> has just destroyed are all still
    /// reachable from the clone this instant, and a walk would census them as part of the ghost.
    /// </summary>
    internal static string MeasureRenderers(List<Renderer> made, string what, bool haveSource,
                                            Bounds sourceBounds, int sourceRenderers)
    {
        if (made.Count == 0)
            return $"{what}: NOT ONE RENDERER SURVIVED.";

        int enabled = 0, active = 0, layerMask = 0;
        var layers = new List<int>(4);
        var shaders = new List<string>(4);
        Bounds box = default;
        int boxed = 0;
        for (int i = 0; i < made.Count; i++)
        {
            Renderer r = made[i];
            bool drawable = r.enabled && r.gameObject.activeInHierarchy;
            if (r.enabled) enabled++;
            if (r.gameObject.activeInHierarchy) active++;
            int layer = r.gameObject.layer;
            layerMask |= 1 << layer;
            if (!layers.Contains(layer)) layers.Add(layer);
            string sh = FirstShaderName(r);
            if (!shaders.Contains(sh)) shaders.Add(sh);
            // ONLY DRAWABLE CLONES SHAPE THE BOX. A renderer on a deactivated object keeps whatever
            // bounds it last had — often the origin — and letting one into the union would inflate
            // the box to reach across the map and report a DISAGREE that is an artefact of the
            // instrument. The counts above still see every clone, drawable or not.
            if (!drawable)
                continue;
            if (boxed == 0) box = r.bounds; else box.Encapsulate(r.bounds);
            boxed++;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        string maskSays;
        if (head == null)
        {
            maskSays = "there is NO head camera to compare against (VRRigDriver.HeadCamera is null)";
        }
        else
        {
            int missing = layerMask & ~head.cullingMask;
            maskSays = missing == 0
                ? $"every one of those layers is INSIDE the head camera's culling mask 0x{head.cullingMask:X8}"
                : $"layer(s) with mask 0x{missing:X8} are OUTSIDE the head camera's culling mask "
                  + $"0x{head.cullingMask:X8} — the head camera cannot draw them at all";
        }

        var sb = new System.Text.StringBuilder(512);
        sb.Append(what).Append(": ").Append(made.Count).Append(" clone renderer(s), ")
          .Append(enabled).Append(" with Renderer.enabled=true, ").Append(active)
          .Append(" on active objects; layer(s) ");
        for (int i = 0; i < layers.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            string name = LayerMask.LayerToName(layers[i]);
            sb.Append(layers[i]).Append(string.IsNullOrEmpty(name) ? " (unnamed)" : $" '{name}'");
        }
        sb.Append(" — ").Append(maskSays).Append("; shader(s) ").Append(string.Join(", ", shaders));
        if (boxed == 0)
        {
            sb.Append("; NOT ONE of them is drawable (enabled on an active object), so they have no "
                      + "box and nothing can have been drawn.");
            return sb.ToString();
        }
        sb.Append("; the ").Append(boxed).Append(" drawable CLONE(s) span world ").Append(Describe(box));

        if (!haveSource)
        {
            sb.Append(". The originals could not be measured, so there is nothing to compare against.");
        }
        else
        {
            float centre = Vector3.Distance(box.center, sourceBounds.center);
            float srcMax = Mathf.Max(sourceBounds.size.x, Mathf.Max(sourceBounds.size.y, sourceBounds.size.z));
            float cloneMax = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
            float ratio = srcMax > 1e-4f ? cloneMax / srcMax : 0f;
            bool agree = centre <= 0.02f * Mathf.Max(srcMax, 1e-3f) && ratio > 0.9f && ratio < 1.1f;
            sb.Append(", against ").Append(sourceRenderers).Append(" ORIGINAL renderer(s) spanning ")
              .Append(Describe(sourceBounds)).Append(" — they ")
              .Append(agree ? "AGREE" : "DISAGREE")
              .Append(" (centres ").Append((centre * 1000f).ToString("F0"))
              .Append(" mm-world apart, longest extent x").Append(ratio.ToString("F2")).Append(')');
            if (!agree)
                sb.Append(". A copy that does not sit where the figure sits, or is not the size of "
                          + "the figure, is exactly the shape of an overlay that fires and cannot "
                          + "be seen");
            sb.Append('.');
        }
        return sb.ToString();
    }

    private static string Describe(Bounds b) =>
        $"[centre ({b.center.x:F2}, {b.center.y:F2}, {b.center.z:F2}) size ({b.size.x:F2}, "
        + $"{b.size.y:F2}, {b.size.z:F2}), y {b.min.y:F2}..{b.max.y:F2}]";

    /// <summary>The renderer's first non-null material's shader name, or a reason there is none.</summary>
    internal static string FirstShaderName(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].shader != null)
                return mats[i].shader.name;
        return mats.Length == 0 ? "<no materials>" : "<null material>";
    }

    /// <summary>
    /// Destroy every mod-owned child under <paramref name="root"/> (name starts with "VR"), depth
    /// first so a nested one cannot be orphaned. Called on a FRESH clone only — it must never run
    /// against a live figure.
    /// </summary>
    private static void StripModOwned(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;
            if (child.name.StartsWith("VR", StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false); // instant off; the Destroy itself is deferred
                Object.Destroy(child.gameObject);
                continue;
            }
            StripModOwned(child);
        }
    }

    /// <summary>Task #3: true when any of the renderer's ORIGINAL materials uses a VFX-family
    /// shader (distort/particle/fog — the figures carry e.g. <c>Amp_CharDistort_Low</c> for such
    /// effects). Those must be destroyed, not re-tinted, or they render as a mist blob.</summary>
    private static bool HasVfxShader(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null || m.shader == null)
                continue;
            string shaderName = m.shader.name;
            if (shaderName.Contains("Distort") || shaderName.Contains("Particle")
                || shaderName.Contains("Fog") || shaderName.Contains("FX"))
                return true;
        }
        return false;
    }

    /// <summary>Map <paramref name="source"/> (a descendant of <paramref name="sourceRoot"/>) to its
    /// structural twin under <paramref name="cloneRoot"/> via the sibling-index chain — Instantiate
    /// preserves child order exactly. Null when source is not a descendant or the chain breaks.</summary>
    private static Transform? FindTwin(Transform sourceRoot, Transform source, Transform cloneRoot)
    {
        var chain = new List<int>(8);
        Transform? t = source;
        while (t != null && t != sourceRoot)
        {
            chain.Add(t.GetSiblingIndex());
            t = t.parent;
        }
        if (t == null)
            return null; // not under the animated root
        Transform twin = cloneRoot;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i] >= twin.childCount)
                return null;
            twin = twin.GetChild(chain[i]);
        }
        return twin;
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
    // Task #5a: slowed from 1.6 Hz to less than half — a calm breathing pulse instead of a strobe.
    // Intensity range (Floor/Ceil) deliberately unchanged.
    private const float PulseHz = 0.7f;
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

/// <summary>
/// COULD THIS HAVE BEEN SEEN? — the one question no build-time measurement can answer.
///
/// <para><see cref="Renderer.isVisible"/> reports whether a camera drew the renderer on the LAST
/// completed frame, so asking it on the frame the clones are created always answers "no" and always
/// means nothing. This component waits two frames, reads the outcome, prints it once, and then
/// deletes itself: a probe that has answered is spent, and this project has already paid for one
/// that kept blitting for 44,200 ticks after it had said everything it had to say.</para>
///
/// <para>Capped at <see cref="ReportsPerKey"/> lines per subject per session (the key is the figure
/// plus which overlay), so a player hovering a mini for a minute cannot recreate the log flood
/// ModBuild 331 removed while still guaranteeing that the FIRST hover of every figure is on record.
/// </para>
/// </summary>
internal sealed class OverlayVisibilityProbe : MonoBehaviour
{
    private const int ReportsPerKey = 2;
    private const int FramesToWait = 2;

    private static readonly Dictionary<string, int> Reported = new();

    private string _key = "";
    private string _what = "";
    private int _frames;

    /// <summary>Arm a probe on <paramref name="host"/> unless this subject has already reported its
    /// quota. No-op on a null host.
    ///
    /// <para>THE QUOTA IS CLAIMED HERE, not when the line is printed. Two reasons, and one of them
    /// is a recorded lesson: a counter written inside the logging method is a load-bearing write
    /// hiding in a diagnostic (scripts/check-instrument-writes.py refuses it — deleting a spent
    /// <c>Log*</c> once nearly latched the wall fade off forever). The other is behavioural: two
    /// overlays armed on the same figure in the same frame would both read the old count and both
    /// report.</para></summary>
    internal static void Attach(GameObject? host, string key, string what)
    {
        if (host == null)
            return;
        int done = Reported.TryGetValue(key, out int already) ? already : 0;
        if (done >= ReportsPerKey)
            return;
        Reported[key] = done + 1;
        OverlayVisibilityProbe probe = host.AddComponent<OverlayVisibilityProbe>();
        probe._key = key;
        probe._what = what;
    }

    /// <summary>Session reset (module shutdown) so a second scenario reports afresh.</summary>
    internal static void Reset() => Reported.Clear();

    private void LateUpdate()
    {
        if (++_frames < FramesToWait)
            return;
        Report();
        Object.Destroy(this); // spent
    }

    private void Report()
    {
        var made = new List<Renderer>(16);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(includeInactive: true))
            if (r != null)
                made.Add(r);

        int visible = 0, enabled = 0;
        Bounds box = default;
        int boxed = 0;
        for (int i = 0; i < made.Count; i++)
        {
            if (made[i].isVisible) visible++;
            if (made[i].enabled) enabled++;
            if (boxed == 0) box = made[i].bounds; else box.Encapsulate(made[i].bounds);
            boxed++;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        string headSays = head == null
            ? "there is no head camera (VRRigDriver.HeadCamera is null), so nothing here could have "
              + "drawn them"
            : $"head camera '{head.name}' is at ({head.transform.position.x:F2}, "
              + $"{head.transform.position.y:F2}, {head.transform.position.z:F2}), "
              + $"{(boxed > 0 ? Vector3.Distance(head.transform.position, box.center) : 0f):F2} wu "
              + $"from their centre, culling mask 0x{head.cullingMask:X8}, near/far "
              + $"{head.nearClipPlane:F2}/{head.farClipPlane:F0} wu";

        string verdict = made.Count == 0
            ? "there are no renderers left at all"
            : visible == 0
                ? "NOT ONE of them was drawn by ANY camera — whatever else is true, the player "
                  + "cannot be looking at this"
                : visible == made.Count
                    ? "ALL of them were drawn — if the player still reports nothing, the defect is "
                      + "in what they look like (blend, tint, depth, or being inside the figure), "
                      + "not in whether they exist"
                    : $"{visible} of {made.Count} were drawn, {made.Count - visible} were culled";

        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        Core.VRLog.Note("FigureGrab",
            $"{_what} SEEN? Two frames after it was built: {verdict}. {enabled} of {made.Count} "
            + $"renderer(s) have Renderer.enabled=true; combined world bounds "
            + (boxed > 0
                ? $"centre ({box.center.x:F2}, {box.center.y:F2}, {box.center.z:F2}) size "
                  + $"({box.size.x:F2}, {box.size.y:F2}, {box.size.z:F2})"
                : "<none>")
            + $"; {headSays}. Renderer.isVisible counts EVERY camera, not just the head one, so a "
            + "true here is a floor and not a promise. Reported at most "
            + $"{ReportsPerKey} time(s) per figure per session.");
    }
}
