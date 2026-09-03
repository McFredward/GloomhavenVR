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
///
/// <para><b>AND THAT SENTENCE IS ONLY HALF TRUE — the half that broke (ModBuild 360).</b> "The
/// figure already wrote depth in its opaque pass" holds for the PRE-GRAB HIGHLIGHT, which re-draws
/// a live, opaque, still-present figure. It is FALSE for the FROZEN HOME GHOST, whose whole premise
/// is that the figure has been carried into the hand: the ghost stands alone at the home cell with
/// nothing opaque behind it, so between them the two passes contribute NOTHING to the depth buffer.
/// User, 2026-09-03, verbatim: "Die Infotafeln und die Geister der Figuren die aktuell in der Hand
/// gehalten werden, respektieren die richtige Perspektive nicht. Wie alles soll auch hier die
/// Perspektive eingehalten werden, ist die Geisterfigur im Vordergrund, soll sie auch im
/// Vordergrund angezeigt werden." The arithmetic behind it: a converted panel rides the distance
/// ladder at <c>sortingOrder</c> 100+ while a ghost renderer sits at 0, and Unity resolves
/// transparents by sortingLayer → sortingOrder → renderQueue → distance — so the panel draws after
/// the ghost at EVERY distance and every angle, and with no depth to fail against it simply paints
/// over it. See <see cref="GhostDepthQueue"/> for the fix, which is the one ModBuild 352 already
/// shipped for the ghost HAND.</para>
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

        // ModBuild 339 — TWO TERMS AN OVERLAY MUST NOT INHERIT FROM THE MESH IT COPIES.
        //
        // 1. THE SOURCE MESH'S VERTEX COLOURS. The Overlay fragment is `_MainTex * _Color *
        //    vertexColor`, which is correct for a widget whose mesh was authored FOR this shader
        //    and wrong for an overlay, which re-draws somebody else's mesh. A character mesh that
        //    bakes a mask into its COLOR stream (black rgb, or alpha 0) multiplies an additive glow
        //    to zero and an alpha ghost to nothing — a clone that exists, is enabled, is on a drawn
        //    layer, sits exactly on the figure and is reported visible by every camera, and paints
        //    no pixels. That is the ModBuild 338 boss report word for word, whose own verdict was
        //    "the defect is in what they look like (blend, tint, depth, or being inside the
        //    figure)". This is the "tint" term, and it is the only one of the four that can also
        //    explain the GHOST — which stands ALONE at the home cell with no figure in front of it,
        //    so depth cannot be why that one is invisible.
        // 2. THE DEPTH EQUALITY. The glow re-draws the same triangles the figure already drew, so
        //    its ZTest LEqual is an equality between two DIFFERENT vertex programs' arithmetic. A
        //    small negative polygon offset settles it toward the camera. It CANNOT pierce a wall
        //    (a wall in front is many depth units nearer; this moves the fragment by about one),
        //    which is the standing constraint on this material.
        //
        // BOTH ARE HasProperty-GUARDED, so a player still running the ModBuild 338 bundle silently
        // gets the old behaviour instead of a pink material. Both shader properties default to the
        // OLD behaviour, so no other user of GloomhavenVR/Overlay changes.
        if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", 0f);
        if (m.HasProperty("_OffsetFactor")) m.SetFloat("_OffsetFactor", -1f);
        if (m.HasProperty("_OffsetUnits")) m.SetFloat("_OffsetUnits", -1f);
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
                CopyBlendShapeWeights(smr, clone);
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
        // ModBuild 360 — the renderers that may carry a DEPTH PREPASS, and the ones that may not
        // with the reason. See ArmGhostDepthPrepass.
        var solid = new List<Renderer>(8);
        var depthExcluded = new List<string>(4);

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

        // ModBuild 366 — JUDGE THE SURFACE RULE BEFORE ACTING ON IT, over exactly the set the
        // tint loop below will reach. Two exclusions from the candidate set, both so the guard's
        // arithmetic means what it says: the VFX branch destroys its renderers whatever this rule
        // says, and the preserved selection RING is game art we deliberately keep on its own
        // materials (an overlay/ZTest-Always material sits well above the opaque ceiling, so
        // counting it would push a healthy figure into "most of this art is transparent").
        var surfaceCandidates = new List<Renderer>(8);
        foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.gameObject.activeInHierarchy)
                continue;
            if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer || HasVfxShader(r))
                continue;
            if (ringTwin != null && (r.transform == ringTwin || r.transform.IsChildOf(ringTwin)))
                continue;
            surfaceCandidates.Add(r);
        }
        SurfaceVerdict surfaces = JudgeSurfaces(surfaceCandidates);
        var surfaceKilled = new List<string>(4);

        // Re-tint every KEPT renderer to the translucent ghost material (one shared instance);
        // destroy VFX renderers outright (task #3). The ring twin keeps its original materials.
        var tint = new List<Renderer>(8);
        int inactiveSkipped = 0;
        foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            // A RENDERER ON AN INACTIVE OBJECT IS NOT GHOST CONTENT (ModBuild 366). It draws
            // nothing, so tinting it is a no-op the player can never see — but it is counted, it
            // is given a depth-prepass twin, and it lands in the census. That is not hypothetical:
            // StripModOwned SetActive(false)s the mod's OWN highlight overlay and Object.Destroy is
            // deferred to end of frame, so the 2026-09-03 log's 'OneHexObstacle' ghost reported
            // "26 clone renderer(s), 26 with Renderer.enabled=true, 13 on active objects" — 13 real
            // renderers and 13 dead 'VROverlay' clones of our own glow. A census that doubles the
            // count cannot answer "how many renderers does this prop kind contribute".
            if (!r.gameObject.activeInHierarchy)
            {
                inactiveSkipped++;
                continue;
            }
            if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer || HasVfxShader(r))
            {
                // RECORD WHAT WAS KILLED AND WHY. HasVfxShader is a NAME match on the shader
                // ("Distort"/"Particle"/"Fog"/"FX"), so a character whose BODY is drawn with such a
                // shader loses its body to this branch and still returns a non-null ghost built out
                // of whatever props remain — a ghost that "spawned" and cannot be seen.
                //
                // THAT HYPOTHESIS IS NOW DEAD, AND THIS LIST IS WHAT KILLED IT (ModBuild 338 log,
                // ElderDrakeID): all 8 entries are ParticleSystemRenderers — BitsEffect, Fog,
                // Initial (1), Cloud (2), twice over — and the body, 'MO_ElderDrake_MESH'
                // (SkinnedMeshRenderer on 'Amp_Char_Shader'), is in the KEPT list, tinted, enabled,
                // and is the ONE drawable clone in that ghost. The rule is not eating the boss.
                // The list stays because it is the only thing that can say so.
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
            //
            // ModBuild 339 adds the mesh's own properties (DescribeSource), because 338 proved the
            // body survives and is still invisible — so the remaining term is what the copy LOOKS
            // like, and the first thing to ask a mesh that renders nothing under an unlit
            // `_MainTex * _Color * vertexColor` shader is whether it carries a COLOR stream at all.
            if (kept.Count < 6)
                kept.Add(DescribeSource(r) + $" on '{FirstShaderName(r)}'");
            if (ringTwin != null && (r.transform == ringTwin || r.transform.IsChildOf(ringTwin)))
            {
                tint.Add(r);          // counts as visual content, but keeps the game's ring look
                if (depthExcluded.Count < 4)
                    depthExcluded.Add($"'{r.name}' (the preserved selection RING — hollow "
                                      + "soft-falloff art, a depth stamp would be a solid disc)");
                continue;
            }
            // THE SURFACE RULE (ModBuild 366), applied AFTER the ring exemption so preserved game
            // art can never be destroyed by it. A ghost tints rather than re-draws, but the effect
            // is identical: a decal box or a beam card that draws nothing on its own becomes a
            // solid translucent slab standing at the home cell. The 2026-09-03 log proves the ghost
            // carries the same defect as the hover glow — 'DECAL_BloodSplat_Proj_PR' is in that
            // obstacle ghost's DEPTH PREPASS list, i.e. it was kept, tinted AND given a depth stamp,
            // while the same line says "no renderer was destroyed as VFX".
            if (surfaces.Enforce && !DrawsOwnSurface(r, out string surfaceWhy))
            {
                if (surfaceKilled.Count < 6)
                    surfaceKilled.Add(DescribeSource(r) + " — " + surfaceWhy);
                r.enabled = false;    // instant off; the Destroy itself is deferred
                Object.Destroy(r);
                continue;
            }
            // Asked BEFORE the tint replaces the materials: it is the SOURCE art that decides
            // whether this mesh's geometry is its silhouette.
            string renderType = SourceRenderType(r);
            if (IsAlphaCutSilhouette(renderType))
            {
                if (depthExcluded.Count < 4)
                    depthExcluded.Add($"'{r.name}' (RenderType '{renderType}' — the alpha carries "
                                      + "the silhouette, not the geometry)");
            }
            else
            {
                solid.Add(r);
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

        // The surface rule's verdict for THIS ghost — the field the 2026-09-03 rectangle report is
        // decided by. It is not marked HW-VERIFY here because it is not a call site: it rides the
        // caller's already-marked VRLog.Note ghost line (PropGhosts.NotifyHeld /
        // FigureGhosts.NotifyHeld), which is where the tier is enforced.
        string surfaceSays = surfaces.Headline
            + (surfaceKilled.Count == 0
                ? string.Empty
                : $" DESTROYED AS NON-SURFACE: {string.Join("; ", surfaceKilled)}"
                  + (surfaces.Dropped > surfaceKilled.Count
                      ? $" (+{surfaces.Dropped - surfaceKilled.Count} more)" : string.Empty))
            + (inactiveSkipped == 0
                ? string.Empty
                : $" {inactiveSkipped} renderer(s) on INACTIVE objects were skipped outright (they "
                  + "draw nothing; before ModBuild 366 they were counted, tinted and depth-stamped, "
                  + "which is why an obstacle ghost reported 26 renderers for 13 real ones).");

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
                 + $"{vfxSays}. {surfaceSays} Tinted {tint.Count} renderer(s)"
                 + (preserveOriginal != null ? " (the selection ring keeps its own materials)" : "")
                 + $"; ghost material shader '{(ghostMat != null && ghostMat.shader != null ? ghostMat.shader.name : "<none>")}' "
                 + $"alpha {(ghostMat != null ? ghostMat.color.a : 0f):F2}. KEPT: "
                 + (kept.Count == 0 ? "nothing" : string.Join(", ", kept))
                 + (tint.Count > kept.Count ? $" (+{tint.Count - kept.Count} more not named)" : "")
                 + ". "
                 // Armed AFTER the census above, so the twins can never be counted as ghost
                 // content: MeasureRenderers and OverlayVisibilityProbe both work off `tint`, and a
                 // colour-free depth stamp is not a thing anyone can see.
                 + ArmGhostDepthPrepass(ghost, solid, depthExcluded);
        return ghost;
    }

    /// <summary>
    /// Copy every blend-shape weight from <paramref name="source"/> onto <paramref name="clone"/>
    /// and return how many were non-zero.
    ///
    /// <para>WHY (ModBuild 339). A skinned clone shares the ORIGINAL's bones, so skinning puts its
    /// vertices exactly where the figure's are — but blend-shape weights live on the RENDERER, not
    /// on the bones, and a fresh <c>SkinnedMeshRenderer</c> starts every one of them at zero. A
    /// figure whose idle drives a shape would therefore be overlaid by a copy of itself in a
    /// DIFFERENT pose: sunk inside the body, where <c>ZTest LEqual</c> rejects it and the glow
    /// vanishes without any of the other measurements noticing (the reported bounds come from
    /// <c>localBounds</c>, which is copied, so the two boxes still "AGREE").</para>
    ///
    /// <para>This is a ONE-SHOT copy taken when the overlay is built. If a clip animates a shape
    /// DURING the hover the clone will drift; the count returned is reported so the log says
    /// whether this figure has any animated shape at all before anybody writes a per-frame sync.
    /// </para>
    /// </summary>
    internal static int CopyBlendShapeWeights(SkinnedMeshRenderer source, SkinnedMeshRenderer clone)
    {
        Mesh? mesh = source.sharedMesh;
        if (mesh == null)
            return 0;
        int shapes = mesh.blendShapeCount;
        int nonZero = 0;
        for (int i = 0; i < shapes; i++)
        {
            float w = source.GetBlendShapeWeight(i);
            if (Mathf.Abs(w) > 1e-4f)
                nonZero++;
            clone.SetBlendShapeWeight(i, w);
        }
        return nonZero;
    }

    /// <summary>
    /// NAME THE RENDERER AND THE THREE PROPERTIES OF ITS MESH THAT CAN MAKE AN OVERLAY OF IT
    /// INVISIBLE OR WRONG (ModBuild 339).
    ///
    /// <para>The ModBuild 338 census could say "3 clone renderer(s) … shader GloomhavenVR/Overlay …
    /// they AGREE" and still not say WHICH three, so the two amber strokes the player photographed
    /// at the boss's snout could not be named from the log at all — the combined box is dominated
    /// by the body and hides a 5 cm band entirely. It names, per source renderer: the object, the
    /// renderer kind, sub-mesh count, vertex count, whether the mesh carries a COLOR stream (a
    /// black or zero-alpha one multiplies this shader's output to nothing — see
    /// <see cref="MakeOverlayMaterial"/>), the blend shapes and how many are non-zero, and its own
    /// world box, which is what separates a dragon from a band hanging off it.</para>
    /// </summary>
    internal static string DescribeSource(Renderer r)
    {
        Mesh? mesh = SourceMesh(r);
        Bounds b = r.bounds;
        var sb = new System.Text.StringBuilder(160);
        sb.Append('\'').Append(r.name).Append("' (").Append(r.GetType().Name);
        if (mesh == null)
        {
            sb.Append(", NO MESH");
        }
        else
        {
            sb.Append(", ").Append(mesh.subMeshCount).Append(" sub-mesh(es), ")
              .Append(mesh.vertexCount).Append(" vert(s), ")
              .Append(mesh.HasVertexAttribute(VertexAttribute.Color)
                  ? "HAS a vertex-colour stream"
                  : "no vertex-colour stream");
            if (mesh.blendShapeCount > 0 && r is SkinnedMeshRenderer smr)
            {
                int nonZero = 0;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    if (Mathf.Abs(smr.GetBlendShapeWeight(i)) > 1e-4f)
                        nonZero++;
                sb.Append(", ").Append(mesh.blendShapeCount).Append(" blend shape(s), ")
                  .Append(nonZero).Append(" non-zero");
            }
            else
            {
                sb.Append(", no blend shapes");
            }
        }
        sb.Append(", box size (").Append(b.size.x.ToString("F2")).Append(", ")
          .Append(b.size.y.ToString("F2")).Append(", ").Append(b.size.z.ToString("F2"))
          .Append("), y ").Append(b.min.y.ToString("F2")).Append("..")
          .Append(b.max.y.ToString("F2"));
        // ModBuild 366 — THE THREE FIELDS THAT NAME A TALL THIN QUAD. Everything above describes
        // the mesh; none of it separates "the prop" from "a beam card standing in the prop". The
        // SHAPE (aspect) says a renderer is a sliver; the SHADER and the QUEUE say why it is
        // invisible in the game and a solid rectangle in an unlit re-draw. See the surface rule.
        sb.Append(", ").Append(DescribeAspect(b))
          .Append(", ").Append(DescribeShading(r)).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// The renderer's world box as a SHAPE rather than three numbers: the ratio of its longest
    /// extent to its shortest, plus a word for the degenerate cases.
    ///
    /// <para>ModBuild 366. The user photographed "so ein Rechteck was da drin steckt und nicht
    /// hingehört" — a narrow, tall, upright translucent rectangle standing in the middle of an
    /// otherwise correct hover glow. Every census field the mod had could describe that renderer
    /// and none of them could SORT it: 'box size (0.05, 6.30, 0.40)' is a sentence a human has to
    /// divide before it says "sliver". This does the division.</para>
    /// </summary>
    private static string DescribeAspect(Bounds b)
    {
        IsSliver(b, out string word, out float ratio);
        return float.IsPositiveInfinity(ratio)
            ? $"aspect long/short = infinite ({word})"
            : ratio <= 0f ? "aspect n/a (empty box)"
            : $"aspect long/short = {ratio:F1} ({word})";
    }

    /// <summary>Aspect ratio above which a box counts as a card rather than an object. Eight,
    /// because the rectangle the user photographed measures roughly 40 px across against 550 px
    /// tall in a 3840-wide frame and every real prop part named in the same session's census is
    /// under 4:1.</summary>
    private const float SliverAspect = 8f;

    /// <summary>
    /// TRUE when <paramref name="b"/> is a card rather than an object — flat in one axis, or long
    /// and thin past <see cref="SliverAspect"/>.
    ///
    /// <para>Used for one thing only: to make sure a sliver that the surface rule KEPT is still
    /// named in the census even when the named list is full. "A truncated list is not absence" —
    /// a tall thin quad that falls off the end of an ellipsis is exactly the renderer a round is
    /// being spent to find.</para>
    /// </summary>
    internal static bool IsSliver(Bounds b, out string shape, out float ratio)
    {
        Vector3 s = b.size;
        float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        float shortest = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        if (longest <= 1e-5f)
        {
            shape = "empty box";
            ratio = 0f;
            return false;
        }
        ratio = shortest > 1e-5f ? longest / shortest : float.PositiveInfinity;
        if (shortest <= 1e-3f * longest)
        {
            shape = "FLAT — one axis is zero, i.e. a PLANE";
            return true;
        }
        if (ratio >= SliverAspect)
        {
            shape = "SLIVER — a tall/thin card, the shape of a beam or a decal edge";
            return true;
        }
        shape = "compact";
        return false;
    }

    /// <summary>
    /// What the renderer's SOURCE material declares about itself: shader, <c>RenderType</c> tag
    /// and render QUEUE. The queue is the term the surface rule decides on
    /// (<see cref="DrawsOwnSurface"/>), so a census that reports the verdict without it cannot be
    /// checked; the shader name is what a human recognises ('Custom/MeshDecal', 'LightShaftShd').
    /// </summary>
    internal static string DescribeShading(Renderer r)
    {
        string type = SourceRenderType(r);
        int lowest = int.MaxValue;
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].renderQueue < lowest)
                lowest = mats[i].renderQueue;
        return $"shader '{FirstShaderName(r)}', RenderType "
               + (string.IsNullOrEmpty(type) ? "<none>" : $"'{type}'")
               + ", queue " + (lowest == int.MaxValue ? "<none>" : lowest.ToString());
    }

    /// <summary>
    /// The mesh a renderer would be CLONED FROM — <c>sharedMesh</c> for a skinned renderer, the
    /// sibling <c>MeshFilter</c>'s for a mesh renderer, null for anything else (which is exactly
    /// the set <see cref="FigureHighlight"/>'s clone step refuses). One accessor, because two
    /// callers now decide things with it — <see cref="DescribeSource"/> writes it into a log line,
    /// and <see cref="VertexCount"/> feeds the miniature verdict, which can CHANGE what is cloned.
    /// A second copy of this expression is a second place for the two to disagree.
    /// </summary>
    internal static Mesh? SourceMesh(Renderer r) =>
        r is SkinnedMeshRenderer s ? s.sharedMesh
      : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh
      : null;

    /// <summary>
    /// How much geometry a renderer would contribute to an overlay: its source mesh's vertex
    /// count, or 0 when it has no clonable mesh at all.
    ///
    /// <para>This is the size term the miniature verdict compares the KEPT set against the DROPPED
    /// set with (<see cref="FigureHighlight.Judge"/>). Vertex count, not bounds volume, because a
    /// bounding box cannot separate a 60-vertex band that happens to hang across the width of a
    /// dragon's snout from a wing; and not renderer COUNT, because one skinned body mesh against
    /// two bands is 1-against-2 and would read as a minority.</para>
    /// </summary>
    internal static int VertexCount(Renderer r)
    {
        Mesh? mesh = SourceMesh(r);
        return mesh != null ? mesh.vertexCount : 0;
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

    /// <summary>
    /// Render queue of the frozen ghost's colour-free DEPTH PREPASS.
    ///
    /// <para><b>WHY A GHOST NEEDS ONE AT ALL.</b> See the class doc: a frozen ghost is an
    /// alpha-blended clone with <c>ZWrite 0</c> standing where an opaque figure USED to be, so it
    /// writes no depth, and a converted world-space panel — <c>sortingOrder</c> 148…276 against the
    /// ghost's 0 — always draws after it and paints over it. This is the identical defect ModBuild
    /// 352 fixed for the ghost HAND ("Die Geisterhand respektiert die Perspektive nicht"), and the
    /// remedy is copied from <c>Hands/HandGhost</c> deliberately: give the GHOST depth so the
    /// panel's own <c>ZTest LEqual</c> resolves the two PER PIXEL.</para>
    ///
    /// <para><b>WHY BACK FACES ONLY</b> (<c>_Cull Front</c>, from HandGhost's argument, which is the
    /// part that matters more than the code): an alpha-blended surface with <c>ZWrite</c> simply
    /// turned back on self-occludes in triangle order, so a limb randomly blocks the torso behind
    /// it. The prepass instead stamps the ghost's FAR shell, which is farther than every visible
    /// ghost fragment — so the ghost's own front+back double layer draws exactly as it ships — and
    /// still nearer than anything genuinely behind the ghost.</para>
    ///
    /// <para><b>WHY 3999 AND NOT HANDGHOST'S 3099.</b> A hand ghost is held in front of the face; a
    /// figure ghost stands ON THE BOARD, in the middle of everything the game draws transparently
    /// there — the hex star under its feet, hover outlines, AoE tints, and its own preserved
    /// selection ring, all at ~3000 with <c>sortingOrder</c> 0. A prepass at 2999 would draw before
    /// all of those and start depth-rejecting them, which is a look change nobody asked for. At
    /// 3999 the stamp lands AFTER every one of them (they are already rasterised and untouched) and
    /// after the ghost's own visible pass at <c>RenderQueue.Transparent</c>, so the shipped ghost is
    /// byte-for-byte what it was — and it still lands BEFORE every converted panel, because a panel
    /// wins on <c>sortingOrder</c> (100+) whatever queue it is on. That last clause is not a
    /// theory: it is exactly what made HandGhost's 3099 work on hardware.</para>
    /// </summary>
    private const int GhostDepthQueue = 3999;

    /// <summary>Name of a depth-prepass twin object. Shared with
    /// <see cref="OverlayVisibilityProbe"/>, which must EXCLUDE them: that probe answers "could
    /// this have been seen?", and a colour-free depth stamp is a renderer that is always frustum-
    /// visible and never draws a pixel — counting it would inflate the one number the probe
    /// exists to report. It also starts with "VR" so <see cref="StripModOwned"/> would remove it
    /// from any ghost ever cloned from a ghost.</summary>
    internal const string DepthTwinName = "VRGhostDepth";

    /// <summary>
    /// A material that draws NOTHING and writes DEPTH — <c>Blend Zero One</c> (the destination is
    /// returned unchanged, so the colour buffer cannot tell this pass ran), <c>ZWrite 1</c>,
    /// <c>ZTest LEqual</c>, <c>_Cull Front</c>, at <see cref="GhostDepthQueue"/>. The SAME bundled
    /// <c>GloomhavenVR/Overlay</c> shader the ghost already wears, which exposes all four as
    /// properties (that is the reason the shader exists); resolved through
    /// <c>Core.BundleShaders</c> like every other bundled shader, never <c>Shader.Find</c>.
    /// </summary>
    private static Material? MakeGhostDepthMaterial()
    {
        Shader? s = PlayTray.OverlayShader();
        if (s == null)
            return null;
        var m = new Material(s) { name = "GloomhavenVR figure ghost (depth prepass)" };
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.Zero);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 1);
        if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        if (m.HasProperty("_Cull")) m.SetInt("_Cull", (int)CullMode.Front);
        // No polygon offset here: the prepass is not re-drawing somebody else's triangles against
        // an existing depth value, it IS the depth value. A bias would only mis-place it.
        if (m.HasProperty("_OffsetFactor")) m.SetFloat("_OffsetFactor", 0f);
        if (m.HasProperty("_OffsetUnits")) m.SetFloat("_OffsetUnits", 0f);
        m.renderQueue = GhostDepthQueue;
        return m;
    }

    /// <summary>
    /// Give every SOLID ghost renderer a depth twin.
    ///
    /// <para><b>THE CONSTRAINT THAT MADE THIS MORE THAN A COPY.</b> HandGhost attaches its prepass
    /// as a SURPLUS MATERIAL on the same renderer, and can only do so for a single-material
    /// renderer: with more than one submesh Unity applies a surplus material to the LAST submesh
    /// only, which stamps a PARTIAL silhouette — worse than none, because a half-covered figure
    /// reads as a glitch. Figure ghosts routinely have multi-submesh <c>SkinnedMeshRenderer</c>s
    /// (<see cref="BuildFrozenGhost"/> counts submeshes and fills the array), so a literal copy of
    /// HandGhost would arm on few of them or none. The fix is not to append a material but to add a
    /// SEPARATE renderer that draws the WHOLE mesh — every submesh — with the depth material: a
    /// child of the source renderer's own transform at identity local TRS, sharing the source mesh
    /// and, for a skinned source, the same bones/rootBone/blend-shape weights, so it deforms in
    /// lock-step with the ghost's idle. Full silhouette, every submesh, no reliance on Unity's
    /// surplus-material rule at all.</para>
    ///
    /// <para><b>WHAT IS NOT ARMED.</b> Anything already excluded as VFX (destroyed upstream); the
    /// preserved selection RING, which keeps the game's own materials and is exactly the hollow,
    /// soft-falloff art <c>Cards/Art/CardGlow</c> refused a depth fix for — its ZWrite would stamp
    /// a solid disc where the art is a thin glowing outline; and any renderer whose SOURCE material
    /// declares a cutout <c>RenderType</c>, where the alpha channel carries the silhouette and the
    /// geometry does not (hair cards, foliage planes). A solid character or prop mesh has none of
    /// those problems, which is why <c>CardGlow</c>'s objection does not transfer to it.</para>
    ///
    /// <para>Returns the census sentence for the caller's report — armed vs candidates, named, so
    /// "the prepass is on" is a number in the hardware log and not an assumption.</para>
    /// </summary>
    private static string ArmGhostDepthPrepass(GameObject ghost, List<Renderer> solid,
                                               List<string> excluded)
    {
        string skipped = excluded.Count == 0
            ? string.Empty
            : $" Not armed: {string.Join(", ", excluded)}.";

        if (solid.Count == 0)
            return "DEPTH PREPASS: 0 armed — no solid renderer survived to carry one, so a panel "
                   + "drawn after this ghost still has nothing to fail its ZTest against."
                   + skipped;

        Material? depth = MakeGhostDepthMaterial();
        if (depth == null)
            return $"DEPTH PREPASS: 0/{solid.Count} armed — the bundle has no GloomhavenVR/Overlay "
                   + "shader, so the ghost cannot write depth and a converted panel keeps painting "
                   + "over it." + skipped;

        int armed = 0;
        var names = new List<string>(4);
        for (int i = 0; i < solid.Count; i++)
        {
            Renderer r = solid[i];
            if (r == null)
                continue;
            Mesh? mesh = SourceMesh(r);
            if (mesh == null)
                continue;
            int subs = Mathf.Max(1, mesh.subMeshCount);

            var go = new GameObject(DepthTwinName);
            // Child of the SOURCE renderer's transform at identity local TRS: the twin inherits the
            // exact world matrix in every case (bone-driven part, static part, the ghost root
            // itself) with no special-casing, and it is destroyed with the ghost.
            go.transform.SetParent(r.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Renderer twin;
            if (r is SkinnedMeshRenderer smr)
            {
                var c = go.AddComponent<SkinnedMeshRenderer>();
                c.sharedMesh = mesh;
                c.bones = smr.bones;              // the ghost's OWN cloned bones — same deformation
                c.rootBone = smr.rootBone;
                c.localBounds = smr.localBounds;
                c.quality = smr.quality;
                c.updateWhenOffscreen = smr.updateWhenOffscreen;
                CopyBlendShapeWeights(smr, c);
                twin = c;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                twin = go.AddComponent<MeshRenderer>();
            }

            twin.sharedMaterials = FillMaterials(subs, depth);
            twin.shadowCastingMode = ShadowCastingMode.Off;
            twin.receiveShadows = false;
            // Sort with the renderer it stands in for: the prepass has to precede the PANEL, and
            // that is decided by sortingOrder long before renderQueue is consulted.
            twin.sortingLayerID = r.sortingLayerID;
            twin.sortingOrder = r.sortingOrder;
            armed++;
            if (names.Count < 4)
                names.Add($"'{r.name}' ({subs} submesh(es), sortingOrder {r.sortingOrder})");
        }

        if (armed == 0)
        {
            Object.Destroy(depth);
            return $"DEPTH PREPASS: 0/{solid.Count} armed — every candidate turned out to have no "
                   + "readable mesh." + skipped;
        }

        // Own the depth material's lifetime the same way the tint material's is owned: Unity does
        // not destroy materials with their GameObject.
        ghost.AddComponent<OverlayMaterialOwner>().Init(depth);
        return $"DEPTH PREPASS: {armed}/{solid.Count} renderer(s) armed at queue {GhostDepthQueue} "
               + $"(colour-free, ZWrite 1, Cull Front — the ghost's FAR shell), on a separate "
               + $"full-mesh twin per renderer so EVERY submesh is covered: "
               + $"{string.Join(", ", names)}"
               + (armed > names.Count ? $" (+{armed - names.Count} more)" : string.Empty)
               + ". Without this a world-space panel drawn after the ghost (sortingOrder 100+ vs 0) "
               + "has no depth to fail against and paints over it."
               + skipped;
    }

    // ---- THE SURFACE RULE (ModBuild 366) --------------------------------------------------------
    //
    // User, 2026-09-03, verbatim: "Ich habe elemente entdeckt die beim drüber fahren mit der Hand
    // kein richtiges overlay haben wie es sein sollte, siehe rechteck-highlighting.jpg. Dort
    // erkennt man zusätzlich zu dem korrekten Highlighting noch so ein Rechteck was da drin steckt
    // und nicht hingehört."
    //
    // WHAT A RE-DRAW ASSUMES. Both overlays copy somebody else's mesh and paint it with an unlit
    // `_MainTex * _Color` material. That is a picture OF the object only while the object's own
    // material also draws that mesh's SURFACE. It is false for the whole family of renderers whose
    // geometry is a VOLUME and whose visible pixels are computed from something else:
    //
    //   * a MESH DECAL — `Custom/MeshDecal` at queue 2600 in this game — is a box that reconstructs
    //     world position from the depth buffer and paints only where the box intersects the scene.
    //     Its own faces are never drawn. Re-drawn unlit, the BOX becomes a solid slab.
    //   * a light shaft / beam / glow card — `LightShaftShd` at queue 3000, y 0.7..7.0 — is a tall
    //     narrow quad whose look is a soft gradient with near-zero alpha at the edges. Re-drawn
    //     unlit and additive it is a hard, opaque rectangle standing on end.
    //
    // Both are in this scenario and at least one of them is INSIDE a prop subtree: the 2026-09-03
    // log names 'DECAL_BloodSplat_Proj_PR' (Custom/MeshDecal, q2600) in the DEPTH PREPASS list of
    // the 'OneHexObstacle' home ghost — i.e. it was cloned, kept and tinted — while the same
    // session's FLOOR CENSUS lines carry both it and 'LightShaft_Prefab' (LightShaftShd, q3000)
    // against ordinary prop geometry that sits at queue 2000 ('Amp_Basic', 'Amp_Basic_N_MRAO') and
    // the obstacle's own alpha-cut meshes at RenderType 'TransparentCutout' (queue 2450).
    //
    // THE PROPERTY, AND WHY THIS ONE. Unity's render QUEUE is the engine's own declaration of that
    // distinction and it is carried by the MATERIAL, so it survives a renamed prefab, a renamed
    // shader and the next scenario's art. Everything at or below <see cref="OpaqueQueueCeiling"/>
    // (2500 — the top of the AlphaTest band) draws its own surface; everything above it is a
    // transparent/effect pass. The alternatives were rejected on the evidence, not on taste:
    //   * a prefab-name blacklist dies the first time the art changes;
    //   * "degenerate bounds (a plane)" is FALSIFIED here — the mesh decal is a box 0.9 wu deep —
    //     and would eat a genuinely thin real prop (a plank, a blade);
    //   * "disabled or edge-on before the clone" cannot be it: CollectCandidates already drops
    //     `!r.enabled`, and the census says every one of that obstacle's 26 renderers was enabled;
    //   * "no _MainTex" is false for a decal, which projects a texture;
    //   * a `Projector` COMPONENT catches nothing — this game draws its decals as mesh renderers;
    //   * widening HasVfxShader's Distort/Particle/Fog/FX name match is a name blacklist by
    //     another name, and it already fails on both 'Custom/MeshDecal' and 'LightShaftShd'.
    //
    // A renderer is dropped only when EVERY one of its materials is above the ceiling: a mixed
    // mesh (opaque body + a glass submesh) still draws a surface and still gets its glow.

    /// <summary>The highest render queue at which a material is still drawing the mesh's OWN
    /// surface: 2500, the top of Unity's AlphaTest band. Above it live the transparent and effect
    /// passes whose geometry is a volume, not a picture. See the surface-rule note above.</summary>
    internal const int OpaqueQueueCeiling = 2500;

    /// <summary>
    /// TRUE when at least one of the renderer's CURRENT materials draws the mesh's own surface,
    /// i.e. sits at or below <see cref="OpaqueQueueCeiling"/>. <paramref name="why"/> names the
    /// queue and shader that decided it, for the census.
    ///
    /// <para>Never false on ignorance: a renderer with no materials, or with only null ones, is
    /// KEPT — the glow exists so the player can see what he is about to grab, and refusing to draw
    /// a renderer we could not measure is the wrong side to fail on.</para>
    /// </summary>
    internal static bool DrawsOwnSurface(Renderer r, out string why)
    {
        Material[] mats = r.sharedMaterials;
        int lowest = int.MaxValue;
        string shader = "<none>";
        int judged = 0;
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null)
                continue;
            judged++;
            int q = m.renderQueue;
            if (q < lowest)
            {
                lowest = q;
                shader = m.shader != null ? m.shader.name : "<null shader>";
            }
        }
        if (judged == 0)
        {
            why = "no material to judge — KEPT rather than dropped on ignorance";
            return true;
        }
        if (lowest <= OpaqueQueueCeiling)
        {
            why = $"queue {lowest} on '{shader}'";
            return true;
        }
        why = $"queue {lowest} on '{shader}' — above the {OpaqueQueueCeiling} opaque ceiling, so "
              + "this renderer's geometry is a VOLUME (decal box, beam/shaft card, glow plane) and "
              + "an unlit re-draw of it is a solid rectangle";
        return false;
    }

    /// <summary>
    /// THE VERDICT ON THE SURFACE RULE, and the guard that can switch it off.
    ///
    /// <para>Same shape (and same reason) as <c>FigureHighlight.Judge</c>'s ModBuild 341 guard:
    /// REFUSE the exclusion when it would drop at least as many VERTICES as it keeps. A prop whose
    /// art is genuinely transparent end to end — an ice crystal, a spirit — must not lose its glow
    /// altogether, because a weak glow is a usability regression and the glow exists so the player
    /// can see what he is about to grab. Vertex count rather than renderer count for the reason
    /// <see cref="VertexCount"/> gives: one body mesh against two effect cards is 1-against-2 and
    /// would read as a minority.</para>
    /// </summary>
    internal readonly struct SurfaceVerdict
    {
        internal SurfaceVerdict(bool enforce, int dropped, int keptVerts, int droppedVerts,
                                string dropList)
        {
            Enforce = enforce;
            Dropped = dropped;
            KeptVerts = keptVerts;
            DroppedVerts = droppedVerts;
            DropList = dropList;
        }

        /// <summary>Whether the caller must actually skip the non-surface renderers.</summary>
        internal bool Enforce { get; }

        internal int Dropped { get; }
        internal int KeptVerts { get; }
        internal int DroppedVerts { get; }

        /// <summary>Every renderer the rule names, with the queue/shader that decided it and its
        /// own box — the field a hardware log is read for. NOT truncated: a tall thin quad that
        /// falls off the end of an ellipsis is exactly the renderer this rule exists to name.</summary>
        internal string DropList { get; }

        internal string Headline =>
            Dropped == 0
                ? "SURFACE RULE: nothing dropped — every renderer draws its own surface (all at or "
                  + $"below queue {OpaqueQueueCeiling})."
                : Enforce
                    ? $"SURFACE RULE: {Dropped} renderer(s) dropped as NON-SURFACE ({DroppedVerts} "
                      + $"vert(s) against {KeptVerts} kept) — {DropList}"
                    : $"SURFACE RULE REFUSED: it would have dropped {Dropped} renderer(s) carrying "
                      + $"{DroppedVerts} vert(s) against only {KeptVerts} kept, i.e. most of this "
                      + "prop's art IS transparent, so nothing was dropped and the glow stays whole "
                      + $"— {DropList}";
    }

    /// <summary>Judge <paramref name="candidates"/> against the surface rule. Pure: writes
    /// nothing, and the caller decides what to do with <see cref="SurfaceVerdict.Enforce"/>.</summary>
    internal static SurfaceVerdict JudgeSurfaces(List<Renderer> candidates)
    {
        int dropped = 0, keptVerts = 0, droppedVerts = 0;
        var names = new List<string>(4);
        for (int i = 0; i < candidates.Count; i++)
        {
            Renderer r = candidates[i];
            if (r == null)
                continue;
            int verts = VertexCount(r);
            if (DrawsOwnSurface(r, out string why))
            {
                keptVerts += verts;
                continue;
            }
            dropped++;
            droppedVerts += verts;
            names.Add(DescribeSource(r) + " — " + why);
        }
        bool enforce = dropped > 0 && droppedVerts < keptVerts;
        return new SurfaceVerdict(enforce, dropped, keptVerts, droppedVerts,
                                  names.Count == 0 ? "nothing" : string.Join("; ", names));
    }

    /// <summary>The first non-empty <c>RenderType</c> tag on a renderer's CURRENT materials —
    /// asked BEFORE the ghost tint replaces them, because it is the SOURCE art's answer we need.
    /// Empty when nothing declares one (the game's Amp character shaders are the common case).</summary>
    private static string SourceRenderType(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null)
                continue;
            string tag = m.GetTag("RenderType", searchFallbacks: true, defaultValue: string.Empty);
            if (!string.IsNullOrEmpty(tag))
                return tag;
        }
        return string.Empty;
    }

    /// <summary>True when a renderer's silhouette lives in its ALPHA rather than in its geometry
    /// (hair cards, foliage planes) — stamping depth for one would occlude with the bounding
    /// quads, not with the shape the player sees. This is <c>CardGlow</c>'s objection, applied to
    /// the only ghost renderers it actually transfers to.</summary>
    private static bool IsAlphaCutSilhouette(string renderType) =>
        renderType.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0
        || renderType.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0
        || renderType.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0
        || renderType.IndexOf("TreeLeaf", StringComparison.OrdinalIgnoreCase) >= 0;

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
        int depthTwins = 0;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r == null)
                continue;
            // EXCLUDE the depth-prepass twins (ModBuild 360). They are colour-free by
            // construction (Blend Zero One), so they are always frustum-visible and can never
            // contribute a pixel — counting them here would raise the "N of M visible" figure this
            // probe exists to report without a single extra pixel being drawn. See
            // FigureOverlay.DepthTwinName.
            if (string.Equals(r.gameObject.name, FigureOverlay.DepthTwinName, StringComparison.Ordinal))
            {
                depthTwins++;
                continue;
            }
            made.Add(r);
        }

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
            + "true here is a floor and not a promise. "
            + (depthTwins > 0
                ? $"{depthTwins} colour-free DEPTH-PREPASS twin(s) are present and deliberately "
                  + "excluded from every count above — they draw no pixel, so counting them would "
                  + "inflate the verdict. "
                : string.Empty)
            + "Reported at most "
            + $"{ReportsPerKey} time(s) per figure per session.");
    }
}
