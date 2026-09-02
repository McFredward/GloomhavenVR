using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #2 — PRE-GRAB proximity highlight for a board figure: an ANIMATED additive glow overlaid
/// ON TOP OF the figure's own textures (NO size change) so the player can see which mini their hand
/// would pluck before they grab it.
///
/// The earlier build "popped" the figure 1.12× larger; the user did not want a scale change. This
/// implementation instead clones the figure's renderers (sharing the SAME bones, so the overlay
/// tracks the live idle animation) and re-draws them with the bundled <c>GloomhavenVR/Overlay</c>
/// shader in ADDITIVE blend with a warm amber tint — a candle-lit shimmer laid over the mini's own
/// meshes. It is OCCLUSION-CORRECT: <c>_ZTest = LEqual</c> + <c>_ZWrite = 0</c> means the figure's
/// own opaque depth hides the glow behind walls exactly like the mini (see <see cref="FigureOverlay"/>).
/// An <see cref="OverlayPulse"/> component animates the tint over <c>Time.unscaledTime</c> so it
/// visibly breathes.
///
/// Driven purely by the single-winner <c>OnGrabHighlight(hand, true/false)</c> callback (the driver
/// suppresses every non-winner, so this only ever engages on the one grab candidate). Snapshot-free:
/// the overlay is a separate throwaway object graph, so clearing it restores the figure byte-identical
/// (its own renderers/materials are never touched).
///
/// <para><b>WHY THE RENDERER SEARCH MOVED OFF <c>m_AnimatedGameObject</c> IN ModBuild 294.</b> The
/// user's ModBuild 293 report was "der Boss-Drache hat immer noch KEIN Highlighting … alle anderen
/// Figuren schon". The hardware log says the opposite of what that sounds like: the boss engaged
/// SIX times (<c>pre-grab highlight ENGAGED (Right near ElderDrakeID …)</c>) against six CLEARs,
/// exactly like every other figure, and each of those six lines ends "overlaid on the figure's own
/// meshes", i.e. <see cref="Apply"/> returned TRUE. The highlight is not failing to fire. Something
/// is being drawn and the player cannot see it.</para>
///
/// <para>The one term that could produce that and is boss-specific is WHICH SUBTREE was cloned.
/// Until now that was <c>ActorBehaviour.m_AnimatedGameObject</c>, and the decompiled game sets it to
/// <c>MF.GetGameObjectAnimator(root).gameObject</c> — <b>the FIRST Animator in the actor's subtree
/// that owns a runtimeAnimatorController</b> (decompiled MF.cs:135-146), in depth-first order. That
/// is not defined to be the character's own Animator, it is defined to be whichever one the walk
/// reaches first, and the boss is the one figure in the log whose subtree provably carries foreign
/// animated content: its FIGURE REACH line names a <c>WP_Scoundrel_Dart</c> and two <c>WP_Dummy</c>
/// objects hanging off it, and its ActorBars census counts 36 active renderers of which 31 are
/// neither mesh nor skinned mesh. Clone the wrong Animator's subtree and you get a true return
/// value, a real material, a real pulse — and a glow on something the size of a dart.</para>
///
/// <para><b>THAT IS A HYPOTHESIS, AND THIS CLASS NO LONGER DEPENDS ON IT.</b> The search now starts
/// at the ACTOR ROOT, which is the same subtree <c>ActorBars</c> and <c>FigureGrabDriver</c> already
/// measure the figure with, so the glow covers whatever those two call "the figure". And
/// <see cref="Apply"/> now returns a REPORT: how many renderers were cloned, what world box they
/// span, and how many of them lay under <c>m_AnimatedGameObject</c> and what box THOSE span. One
/// hardware line then settles the hypothesis instead of the next round guessing again — if the two
/// boxes differ on the boss and agree everywhere else, the paragraph above was right; if they agree
/// on the boss too, it was wrong and the cause is elsewhere, and the line says so either way.</para>
///
/// <para><b>AND IN ModBuild 336 THE REPORT MOVED ONTO THE COPIES.</b> ModBuild 335's hardware log
/// answered the boss with "3 renderer(s) cloned from the ACTOR ROOT, spanning world y -1.18..5.66"
/// — every number in it read off the ORIGINAL renderers. It could not say where the clones landed,
/// whether they were enabled, what layer they were on, whether the head camera renders that layer,
/// what shader they carry, or whether a camera drew them; the complaint it exists to answer is "I
/// cannot see it". The report is now <see cref="FigureOverlay.MeasureClones"/> over the container
/// this class just built, compared against the originals' combined box, and
/// <see cref="OverlayVisibilityProbe"/> adds the outcome — <see cref="Renderer.isVisible"/> two
/// frames later, once a camera has had a chance to cull them.</para>
/// </summary>
internal sealed class FigureHighlight
{
    // Warm amber-gold — the candle-lit-dungeon palette of Gloomhaven, added as light over the mini.
    private static readonly Color GlowTint = new Color(1.0f, 0.62f, 0.26f);

    /// <summary>
    /// Mod-owned objects are named with this prefix (<c>VROverlay</c>, <c>VRFigureHighlight</c>,
    /// <c>VR_FigureReach</c>). Cloning the figure from its ROOT means the walk can now reach our own
    /// previous overlay, and an overlay of an overlay doubles every frame it is re-applied. Nothing
    /// the game ships under an actor starts with these two letters (they are <c>HE_</c>, <c>MO_</c>,
    /// <c>WP_</c>, <c>C_*_JNT</c>, <c>Base</c>, <c>Actor(Clone)</c>).
    /// </summary>
    private const string ModOwnedPrefix = "VR";

    private static readonly List<Renderer> Scratch = new(32);

    private GameObject? _overlayRoot;

    /// <summary>True while the highlight overlay exists.</summary>
    public bool Active => _overlayRoot != null;

    /// <summary>
    /// Build the animated additive overlay over the figure at <paramref name="figureRoot"/>. No-op
    /// if already active, or if the bundled Overlay shader / any renderer is unavailable (returns
    /// false). The overlay container is parented under <paramref name="figureRoot"/> so the game's
    /// own renderer sweeps (jump-exit opacity, invisibility) never enumerate the extra passes.
    ///
    /// <para><paramref name="animatedRoot"/> is no longer what is cloned — it is measured ALONGSIDE
    /// the clone so the report can say whether the game's own <c>m_AnimatedGameObject</c> is the
    /// figure at all. <paramref name="excludeSubtree"/> is the actor's selection ring
    /// (<c>m_Hilight</c>), which hangs off the actor root, draws <c>ZTest Always</c> and is not part
    /// of the miniature: gilding it would change how every figure looks, not just the boss.</para>
    ///
    /// <para>Returns true when at least one renderer was cloned; <paramref name="report"/> is always
    /// written and is what the caller logs.</para>
    /// </summary>
    public bool Apply(GameObject figureRoot, GameObject? animatedRoot, Transform? excludeSubtree,
                      string label, out string report)
    {
        report = string.Empty;
        if (Active)
            return true;
        if (figureRoot == null)
        {
            report = "no figure root";
            return false;
        }

        Material? mat = FigureOverlay.MakeOverlayMaterial(GlowTint, additive: true);
        if (mat == null)
        {
            report = "the bundle is missing the GloomhavenVR/Overlay shader";
            return false; // skip rather than pierce walls
        }

        var root = new GameObject("VRFigureHighlight");
        root.transform.SetParent(figureRoot.transform, worldPositionStays: false);

        Scratch.Clear();
        figureRoot.GetComponentsInChildren(includeInactive: false, Scratch);
        int onActiveObjects = Scratch.Count;

        int cloned = 0;
        int underAnimated = 0;
        int skippedModOwned = 0;
        int skippedRing = 0;
        int skippedDisabled = 0;
        int skippedKind = 0;
        float animMinY = float.MaxValue, animMaxY = float.MinValue;
        Bounds originals = default;
        Transform? animatedT = animatedRoot != null ? animatedRoot.transform : null;

        for (int i = 0; i < Scratch.Count; i++)
        {
            Renderer r = Scratch[i];
            if (r == null)
                continue;
            // Only SURFACE geometry, the same filter ActorBars and FigureGrabDriver use: a particle
            // or trail renderer is an effect volume, and an additive clone of one is a smear.
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
            {
                skippedKind++;
                continue;
            }
            if (IsUnder(r.transform, root.transform) || HasModOwnedAncestor(r.transform, figureRoot.transform))
            {
                skippedModOwned++;
                continue;
            }
            if (excludeSubtree != null && IsUnder(r.transform, excludeSubtree))
            {
                skippedRing++;
                continue;
            }
            // A renderer the game has switched OFF must not acquire a glowing double. This is not
            // hypothetical: the async material loader disables the component while materials stream
            // (MaterialLoaderData.LoadMaterials), and a sheathed weapon is disabled outright. The
            // pre-ModBuild-294 code walked with includeInactive:TRUE and never checked `enabled`,
            // so every hidden prop on the figure got a visible amber ghost of itself in mid-air.
            if (!r.enabled)
            {
                skippedDisabled++;
                continue;
            }

            if (!CloneOne(r, root.transform, mat))
                continue;

            // The ORIGINAL's box, accumulated only so the report has something to compare the
            // CLONES against. It is never the answer on its own — see the report below.
            Bounds b = r.bounds;
            if (cloned == 0) originals = b; else originals.Encapsulate(b);
            cloned++;

            if (animatedT != null && IsUnder(r.transform, animatedT))
            {
                underAnimated++;
                if (b.min.y < animMinY) animMinY = b.min.y;
                if (b.max.y > animMaxY) animMaxY = b.max.y;
            }
        }
        Scratch.Clear();

        if (cloned == 0)
        {
            Object.Destroy(root);
            Object.Destroy(mat);
            report = $"NOTHING TO GLOW — {onActiveObjects} renderer(s) on active objects under the "
                     + $"actor root, {skippedKind} non-mesh, {skippedDisabled} with "
                     + $"Renderer.enabled=false, {skippedRing} on the selection ring, "
                     + $"{skippedModOwned} mod-owned";
            return false;
        }

        OverlayPulse pulse = root.AddComponent<OverlayPulse>();
        pulse.Init(mat, GlowTint); // pulse owns + destroys the material
        _overlayRoot = root;

        // THE ONE LINE THAT MAKES AN INVISIBLE HIGHLIGHT ANSWERABLE. Before ModBuild 294 the caller
        // logged a boolean, and a boolean cannot tell "the glow covers the dragon" from "the glow
        // covers a dart hanging off the dragon" — which is exactly the pair the ModBuild 293 log
        // could not separate for ElderDrakeID.
        //
        // ...AND FROM ModBuild 336 IT MEASURES THE CLONES. The 294 report was still built from
        // `r.bounds` on the ORIGINAL renderers, so "3 renderer(s) cloned from the ACTOR ROOT,
        // spanning world y -1.18..5.66" described the renderers it had copied FROM. It said nothing
        // about where the copies landed, whether they were enabled, what layer they were on,
        // whether the head camera renders that layer, or what shader they carry — and the complaint
        // it was written to answer is "I cannot see it". FigureOverlay.MeasureClones now walks the
        // container that was just built, and OverlayVisibilityProbe reports two frames later
        // whether anything actually drew it.
        string animatedSays = animatedT == null
            ? "the actor has NO m_AnimatedGameObject"
            : underAnimated == 0
                ? $"NONE of them lie under m_AnimatedGameObject '{animatedRoot!.name}' — the game's "
                  + "own animator object is not where this figure's meshes are, which is precisely "
                  + "the shape of a highlight that fires and cannot be seen"
                : underAnimated == cloned
                    ? $"ALL of them lie under m_AnimatedGameObject '{animatedRoot!.name}', so the "
                      + "pre-ModBuild-294 search would have produced the same overlay"
                    : $"{underAnimated} of them lie under m_AnimatedGameObject "
                      + $"'{animatedRoot!.name}' spanning world y {animMinY:F2}..{animMaxY:F2} — the "
                      + $"pre-ModBuild-294 search would have glowed only that part";

        report = FigureOverlay.MeasureClones(root.transform, "GLOW", cloned > 0, originals, cloned)
                 + $" Cloned from the ACTOR ROOT; {animatedSays}. Skipped: "
                 + $"{skippedKind} non-mesh, {skippedDisabled} with Renderer.enabled=false, "
                 + $"{skippedRing} on the selection ring, {skippedModOwned} mod-owned, of "
                 + $"{onActiveObjects} renderer(s) on active objects.";
        OverlayVisibilityProbe.Attach(root, $"{label}/glow", $"GLOW on {label}");
        return true;
    }

    /// <summary>Destroy the overlay (idempotent). Restores the figure byte-identical — its own
    /// renderers and materials were never modified.</summary>
    public void Clear()
    {
        if (_overlayRoot != null)
        {
            Object.Destroy(_overlayRoot); // OverlayPulse.OnDestroy frees the material
            _overlayRoot = null;
        }
    }

    /// <summary>
    /// Re-draw one renderer's mesh with <paramref name="overlayMat"/> under
    /// <paramref name="container"/>. A skinned clone references the ORIGINAL bones and rootBone, so
    /// it deforms in lock-step with the live figure and needs no per-frame tracking; its bounds are
    /// therefore identical to the original's and it is culled with it.
    ///
    /// <para>This duplicates <see cref="FigureOverlay.CloneRenderersSharingBones"/>'s per-renderer
    /// body rather than calling it, because that method takes a subtree ROOT and this class now has
    /// to filter the subtree renderer by renderer (mod-owned objects, the selection ring, disabled
    /// components). The ghost path in <see cref="FigureOverlay"/> is untouched.</para>
    /// </summary>
    private static bool CloneOne(Renderer r, Transform container, Material overlayMat)
    {
        if (r is SkinnedMeshRenderer smr)
        {
            if (smr.sharedMesh == null)
                return false;
            var go = new GameObject("VROverlay");
            go.transform.SetParent(container, worldPositionStays: false);
            var clone = go.AddComponent<SkinnedMeshRenderer>();
            clone.sharedMesh = smr.sharedMesh;
            clone.bones = smr.bones;            // share the LIVE bones → tracks animation
            clone.rootBone = smr.rootBone;
            clone.localBounds = smr.localBounds;
            clone.quality = smr.quality;
            clone.updateWhenOffscreen = smr.updateWhenOffscreen;
            clone.sharedMaterials = Fill(smr.sharedMesh.subMeshCount, overlayMat);
            clone.shadowCastingMode = ShadowCastingMode.Off;
            clone.receiveShadows = false;
            return true;
        }

        if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
        {
            // Static mesh part (a weapon, a shield, a prop). Parent under the container so the whole
            // overlay is torn down by destroying that one object, and match the part's current world
            // transform. It rides the figure via the container; a bone-driven static part that moves
            // during the hover is an accepted edge case (the hover is short and the part is small).
            var go = new GameObject("VROverlay");
            go.transform.SetParent(container, worldPositionStays: false);
            go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            // MATCH THE PART'S WORLD SCALE, which the pre-ModBuild-294 code left at the container's.
            // The container hangs off the actor root, so a prop with any local scale of its own drew
            // its glow at the wrong size — invisible on a mini, and on the 198x diorama not. The
            // arithmetic moved into FigureOverlay in ModBuild 336, when the ghost path was found to
            // be missing this same term; one copy now, so the next fix cannot land on one of two.
            FigureOverlay.MatchCloneWorldScale(go.transform, container, r.transform);
            var cf = go.AddComponent<MeshFilter>();
            cf.sharedMesh = mf.sharedMesh;
            var cr = go.AddComponent<MeshRenderer>();
            cr.sharedMaterials = Fill(mf.sharedMesh.subMeshCount, overlayMat);
            cr.shadowCastingMode = ShadowCastingMode.Off;
            cr.receiveShadows = false;
            return true;
        }
        return false;
    }

    private static Material[] Fill(int count, Material m)
    {
        var mats = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = m;
        return mats;
    }

    /// <summary>True when <paramref name="t"/> is <paramref name="ancestor"/> or sits under it.</summary>
    private static bool IsUnder(Transform t, Transform ancestor)
    {
        for (Transform? c = t; c != null; c = c.parent)
            if (ReferenceEquals(c, ancestor))
                return true;
        return false;
    }

    /// <summary>
    /// True when any transform between <paramref name="t"/> and <paramref name="stopAt"/>
    /// (inclusive of t, exclusive of stopAt) is a mod-owned object. See <see cref="ModOwnedPrefix"/>.
    /// </summary>
    private static bool HasModOwnedAncestor(Transform t, Transform stopAt)
    {
        for (Transform? c = t; c != null && !ReferenceEquals(c, stopAt); c = c.parent)
            if (c.name.StartsWith(ModOwnedPrefix, System.StringComparison.Ordinal))
                return true;
        return false;
    }
}
