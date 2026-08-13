using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// FIGURE RESCALE — keep the SIMULATED parts of a figure (capes, cloaks, tabards: everything a
/// <c>UnityEngine.Cloth</c> drives) in step with a size the mod authors on the figure's root.
///
/// <para>USER REPORT (hardware test, ModBuild 137, verbatim): "4) Beim Skallieren der Figuren
/// skallieren nicht alle Assets richtig mit. zB die Klammotten bzw Umhänge verändern zwar auch ihre
/// größe aber nicht richtig mit der Figur mit. Alle Teile der Figur sollen korrekt
/// mitskallieren."</para>
///
/// <para>ROOT CAUSE — PROVEN FROM THE DECOMPILED GAME. Gloomhaven's character models carry Unity's
/// native <c>Cloth</c> component (PhysX cloth); there is no custom spring/jiggle/dynamic-bone script
/// anywhere in the game. <c>ActorBehaviour.SetActor</c> (decompiled/GH.Runtime/ActorBehaviour.cs:122)
/// caches them ONCE, at actor creation:
/// <code>actorBehaviour.m_Clothes = actorBehaviour.m_Animator.gameObject.GetComponentsInChildren&lt;Cloth&gt;();</code>
/// and the game never touches them again except to flip <c>enabled</c> and to write
/// <c>clothSolverFrequency</c> (ActorBehaviour.cs:128, PhysicsController.cs:53). Repo-wide the game
/// NEVER writes a figure's <c>localScale</c> — the only <c>localScale</c> write in GH.Runtime is
/// <c>ObjectPool.cs:471</c> on cards — so the mod's scale is uncontested but also never re-baked by
/// anyone. Nothing on the figure path ever reads <c>lossyScale</c> or <c>localScale</c>
/// (grepped: <c>lossyScale</c> appears 7 times in the whole game, none of them on a figure).</para>
///
/// <para>A skinned mesh scales with its root exactly, which is why the BODY is always right. A Cloth
/// does not: the solver's spatial constants are absolute distances, not fractions of the model —
/// the per-vertex <c>coefficients</c> (<c>maxDistance</c> = how far a vertex may travel from its
/// skinned position, <c>collisionSphereDistance</c> = surface-penetration allowance) are metres
/// baked against the mesh at AUTHORING scale, as are <c>selfCollisionDistance</c>,
/// <c>sleepThreshold</c> and the external accelerations. They were seeded when the actor spawned, at
/// the figure's board size, and the mod's rescale happens strictly later (grab → stretch gesture →
/// glide home), so the cape ends up simulating against a body it no longer fits: it renders at the
/// new size (it is still skinned) while its freedom of movement, its collision offsets and its
/// settling stay sized for the old body — "verändert zwar auch ihre größe aber nicht richtig mit
/// der Figur mit".</para>
///
/// <para>THE FIX, in the game's own idiom. While the mod is CHANGING a figure's size the cloth
/// simulation is faded OUT: a disabled Cloth means the SkinnedMeshRenderer skins the cape normally,
/// and a skinned mesh scales with the root EXACTLY — that half of the fix is airtight and is
/// literally what the user asked for ("Alle Teile der Figur sollen korrekt mitskallieren"). It is
/// also the game's own answer to "this figure just moved discontinuously": ActorBehaviour.cs:249
/// disables every <c>m_Clothes</c> entry on a forced position change and ActorBehaviour.cs:556
/// re-enables them two LateUpdates later. When the size SETTLES (a few quiet frames — the stretch
/// gesture and the remote ease both asymptote, hence the RELATIVE epsilon) the per-vertex
/// coefficients are rewritten as <c>pristine × factor</c> and the sim is faded back in, so the
/// re-seeded cloth hangs and swings in proportion to the size the figure is actually at. Returning
/// to the board size restores the pristine array verbatim — the scaling is always computed from the
/// authored snapshot, never incrementally, so it cannot drift.</para>
///
/// <para>CONFIDENCE, stated honestly (CHARTER: separate what is read from source from what is
/// inferred). PROVEN: the components exist and are Unity <c>Cloth</c>; the game caches them at
/// spawn and never re-seeds them; the game never scales figures; a disabled Cloth scales perfectly;
/// <c>SetEnabledFading</c>, <c>ClearTransformMotion</c> and <c>get/set_coefficients</c> all exist in
/// this build's <c>UnityEngine.ClothModule.dll</c> (verified against ressources/Managed) and the
/// game calls none of them, so nothing fights us. INFERRED: that the coefficients are metres
/// against world space rather than fractions of the transform, and that re-enabling re-seeds the
/// solver at the current transform. Both are the documented behaviour of Unity's Cloth and the
/// standard workaround for it, but neither is readable from the game's source. The suspend half
/// does not depend on either inference; only the coefficient rescale does, and if that inference is
/// wrong the visible consequence is a cape that swings a little too freely or too stiffly at an
/// unusual size — never the reported defect, which the suspend alone already removes.</para>
///
/// <para>REJECTED ALTERNATIVES.
/// (a) <c>ActorBehaviour.ForceSetLocoIntermediateTarget</c> does exactly the disable/re-enable dance
/// and is public — but it also overwrites <c>m_LocoIntermediateTarget</c>, i.e. it would teleport
/// the figure's locomotion goal as a side effect of a cosmetic resize. Rejected: never drive game
/// state to get a presentation effect.
/// (b) Hard <c>enabled = false/true</c> without the fade: that is the game's own pattern, but the
/// game uses it for a teleport where a pop is invisible anyway. Here the player is watching the
/// figure in their hand, and rule 5 ("everything moves WITH the animation, popping is
/// unacceptable") applies. <c>SetEnabledFading</c> blends the simulation in and out instead; the
/// hard write is kept only as the backstop below, for the case where the fade does not take.
/// (c) Rescaling the coefficients every frame while the gesture runs: <c>Cloth.coefficients</c>
/// allocates and uploads a per-vertex array on every get AND set, so a continuous stretch would
/// upload the whole constraint set ~90×/s per figure. The suspend already makes the transient
/// frames correct, so the upload happens once, on settle.
/// (d) Doing nothing and letting the cloth "catch up": it cannot. Nothing in the game ever re-seeds
/// a Cloth after spawn (proven above), so the mismatch is permanent for the life of the figure.</para>
///
/// <para>WHAT IS NOT HANDLED, deliberately, and how you will see it. Two other classes of sub-object
/// do not follow a figure rescale, and the FIGURE SCALE diagnostic below COUNTS them rather than
/// changing them, so the next hardware round can say whether they matter:
/// (1) <c>ParticleSystem</c>s whose <c>main.scalingMode</c> is <c>Local</c> ignore the root's scale
/// by design. Figures do spawn them at runtime (<c>ActorBehaviour.cs:427/433</c> parents the
/// invisibility idle + smoke under <c>m_Animator.transform</c>). Flipping them to
/// <c>Hierarchy</c> would fix the size but is an unreported, un-tested change to authored VFX, so
/// it is reported, not made.
/// (2) The figure's WORLDSPACE HEALTH/CONDITION PANEL is not under the figure at all:
/// <c>ActorBehaviour.CreateWorldSpaceGUIElements</c> (ActorBehaviour.cs:238-239) reparents it to
/// <c>WorldspaceUITools.Instance.WorldspaceGUIPrefabLevel</c>, so no root scale can ever reach it.
/// That surface belongs to the WorldUI lane, not to this one.</para>
///
/// <para>LATE AND RESPAWNED PARTS ARE COVERED. The subtree is re-scanned on EVERY suspend, never
/// once per figure, so anything that appears after the size was set is picked up: the model subtree
/// itself is asynchronous (<c>CharacterManager.InitialiseCharacterAsync</c> → Addressables
/// <c>InstantiateAsync</c>, GH.Runtime/CharacterManager.cs:187 — the Cloths materialise frames after
/// the root), weapons are attached to bones afterwards (<c>InitialiseCharacterStepTwo</c> →
/// <c>DoEquip</c>, CharacterManager.cs:295), and <c>ChangeModelSMB</c> deinitialises the character
/// and re-creates it wholesale mid-animation. A part born INSIDE the root is scaled by the root
/// transform for free; only a part born with its own simulation needs this pass, and a Cloth born
/// while the figure is already at its new size is seeded there and is correct without us — it is
/// still captured, so the trip BACK to board size restores it too.</para>
///
/// <para>MULTIPLAYER. Figure size is not a wire field of its own and does not need to become one.
/// The holder's side writes <c>heldLocalScale × stretch</c> (FigureGrabbable) and the peer's side
/// RECONSTRUCTS the same size locally from data it already has — the holder's rig scale off the rig
/// packet times the manual stretch factor from extension record 30 (<c>NetFigures.EaseSlot</c>). So
/// both machines end up writing a scale onto the same figure, and both call <see cref="Note"/> with
/// the same factor: the cape is corrected identically on every client, which is what the standing
/// 1:1 ruling requires. Everything here is local presentation — zero wire bytes, and a strict no-op
/// offline and for any figure nobody is resizing.</para>
/// </summary>
internal static class FigureCloth
{
    /// <summary>Quiet frames a size must hold before the simulation is faded back in. Three frames
    /// is ~33 ms at 90 Hz — under the perception threshold, and long enough that neither the stretch
    /// gesture's exponential smoothing nor the remote slot's Lerp can be mistaken for "settled" while
    /// it is still visibly moving.</summary>
    private const int SettleFrames = 3;

    /// <summary>Frames without a <see cref="Note"/> before a figure is forgotten. Well past
    /// <see cref="SettleFrames"/>, so a figure always finishes its resume before it is pruned.</summary>
    private const int PruneFrames = 30;

    /// <summary>A size change worth reacting to, RELATIVE (0.2%). Both writers approach their target
    /// asymptotically (smoothing here, Lerp on the peer), so an absolute epsilon would either never
    /// settle or would settle while the figure was still visibly growing.</summary>
    private const float FactorEpsilon = 0.002f;

    /// <summary>Blend time for <c>Cloth.SetEnabledFading</c> in both directions.</summary>
    private const float FadeSeconds = 0.12f;

    private sealed class Tracked
    {
        public GameObject Root = null!;

        /// <summary>The cloths this pass MANAGES: only those found ENABLED at capture. A cloth that
        /// was already disabled is not simulating, therefore already scales perfectly as a plain
        /// skinned mesh, and must not be switched on by us (it may be authored off, or the game may
        /// be inside its own two-frame teleport window — ActorBehaviour.cs:249).</summary>
        public readonly List<Cloth> Cloths = new(2);

        /// <summary>Per managed cloth, the coefficient array exactly as authored, captured the first
        /// time that cloth was seen. Every rescale is computed from THIS, never from the live array,
        /// so repeated resizes cannot compound.</summary>
        public readonly List<ClothSkinningCoefficient[]> Pristine = new(2);

        public float Factor = 1f;
        public int QuietFrames;
        public int IdleFrames;
        public bool Suspended;
        public float FadeEnds;
    }

    private static readonly Dictionary<int, Tracked> _tracked = new();
    private static readonly List<int> _scratch = new(4);
    private static bool _logged;

    /// <summary>
    /// Report the size a figure is currently being rendered at, RELATIVE to its native board size
    /// (1 = untouched). Called from every site that authors a figure's scale — the local hold, the
    /// release glide and the remote mirror alike — and cheap enough to call every frame: a factor
    /// that has not moved is a dictionary lookup and a compare.
    /// </summary>
    internal static void Note(GameObject? root, float factor)
    {
        if (root == null || float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f)
            return;

        int id = root.GetInstanceID();
        if (!_tracked.TryGetValue(id, out Tracked t))
        {
            // A fresh entry starts at the NATIVE size (1), never at the factor it was first seen
            // at, so a figure that is ALREADY resized the first time we hear about it is corrected
            // too. That is not a corner case in multiplayer: a peer who grabbed and stretched a
            // mini before this client ever ticked their record arrives with a factor of 2 on its
            // very first frame, and seeding the baseline from it would silently declare that size
            // "unchanged" and leave the cape wrong for the whole hold.
            t = new Tracked { Root = root };
            _tracked[id] = t;
        }

        t.IdleFrames = 0;
        if (Mathf.Abs(factor - t.Factor) > t.Factor * FactorEpsilon)
        {
            t.Factor = factor;
            t.QuietFrames = 0;
            if (!t.Suspended)
                Suspend(t);
        }
    }

    /// <summary>
    /// Advance every tracked figure: fade the simulation back in once its size has held still, keep
    /// a suspended cloth suspended if the game re-enabled it under us, and forget figures nobody is
    /// resizing any more. Called once per frame from <c>FigureGrabbable.TickHeldScale</c>.
    ///
    /// <para>It is deliberately NOT a new step in <c>FigureGrabDriver.Update</c>'s locked frame
    /// order: it belongs to the same per-frame job as the size write it corrects, it must run in the
    /// same place (above the config gate, so a figure released by the gate on this frame still gets
    /// its cloth back), and adding a step would edit FRAME-ORDER.lock — a Tier 3 change this lane
    /// does not own. Strict no-op with nothing tracked.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_tracked.Count == 0)
            return;

        _scratch.Clear();
        foreach (KeyValuePair<int, Tracked> kv in _tracked)
        {
            Tracked t = kv.Value;
            if (t.Root == null)
            {
                _scratch.Add(kv.Key); // actor torn down / model swapped (ChangeModelSMB) / pooled
                continue;
            }

            if (t.Suspended)
            {
                if (t.QuietFrames >= SettleFrames)
                    Resume(t);
                else if (Time.unscaledTime >= t.FadeEnds)
                    HardDisable(t); // backstop: the fade has had its time, or the game re-enabled it
            }
            else if (++t.IdleFrames > PruneFrames)
            {
                _scratch.Add(kv.Key); // nobody is authoring this figure's size any more
            }

            t.QuietFrames++;
        }

        for (int i = 0; i < _scratch.Count; i++)
            _tracked.Remove(_scratch[i]);
    }

    /// <summary>Forget everything (driver teardown / scene change). Does not touch the cloths: the
    /// figures they belong to are going away with the scene, and a Unity-null component cannot be
    /// written to anyway.</summary>
    internal static void Clear()
    {
        _tracked.Clear();
        _scratch.Clear();
    }

    /// <summary>
    /// Fade the simulation out and (re-)scan the subtree. The scan happens on every suspend, not
    /// once per figure, because a figure's visual subtree is asynchronous and can be replaced
    /// wholesale while it is on the board — see the class comment's LATE AND RESPAWNED PARTS.
    /// </summary>
    private static void Suspend(Tracked t)
    {
        t.Suspended = true;
        t.FadeEnds = Time.unscaledTime + FadeSeconds;

        Cloth[] found = t.Root.GetComponentsInChildren<Cloth>(true);
        for (int i = 0; i < found.Length; i++)
        {
            Cloth c = found[i];
            if (c == null || t.Cloths.Contains(c))
                continue;
            if (!c.enabled)
                continue; // not simulating → already scales as a plain skinned mesh; leave it alone
            t.Cloths.Add(c);
            t.Pristine.Add(c.coefficients);
        }

        for (int i = 0; i < t.Cloths.Count; i++)
        {
            Cloth c = t.Cloths[i];
            if (c != null)
                c.SetEnabledFading(false, FadeSeconds);
        }
    }

    /// <summary>The backstop for <c>SetEnabledFading(false)</c>: once the blend has had its time the
    /// component must actually be off, both because the fade may not have taken and because the
    /// game's own LateUpdate re-enables every cloth unconditionally after its teleport window
    /// (ActorBehaviour.cs:556) and would otherwise switch the simulation back on mid-resize.</summary>
    private static void HardDisable(Tracked t)
    {
        for (int i = 0; i < t.Cloths.Count; i++)
        {
            Cloth c = t.Cloths[i];
            if (c != null && c.enabled)
                c.enabled = false;
        }
    }

    /// <summary>
    /// Re-seed every managed cloth at the size the figure has settled at: rewrite the per-vertex
    /// coefficients as <c>pristine × factor</c> (exactly the pristine array when the figure is back
    /// at board size), fade the simulation in, and clear the transform motion the resize
    /// accumulated so the cape is not whipped by a size change it should not read as travel.
    /// </summary>
    private static void Resume(Tracked t)
    {
        float factor = t.Factor;
        bool identity = Mathf.Abs(factor - 1f) <= FactorEpsilon;
        int reseeded = 0;
        int skipped = 0;

        for (int i = 0; i < t.Cloths.Count; i++)
        {
            Cloth c = t.Cloths[i];
            if (c == null)
            {
                skipped++;
                continue;
            }

            ClothSkinningCoefficient[] pristine = t.Pristine[i];
            if (pristine != null && pristine.Length > 0)
            {
                var next = new ClothSkinningCoefficient[pristine.Length];
                for (int v = 0; v < pristine.Length; v++)
                {
                    // maxDistance and collisionSphereDistance are absolute distances against the
                    // authored body. Infinity is Unity's "unconstrained" and must stay infinite;
                    // multiplying it is a NaN waiting to happen.
                    float max = pristine[v].maxDistance;
                    float sphere = pristine[v].collisionSphereDistance;
                    next[v].maxDistance = identity || float.IsInfinity(max) ? max : max * factor;
                    next[v].collisionSphereDistance =
                        identity || float.IsInfinity(sphere) ? sphere : sphere * factor;
                }
                c.coefficients = next;
                reseeded++;
            }
            else
            {
                skipped++; // no painted constraints — nothing sized to correct
            }

            c.SetEnabledFading(true, FadeSeconds);
            if (!c.enabled)
                c.enabled = true; // backstop, same reasoning as HardDisable
            c.ClearTransformMotion();
        }

        t.Suspended = false;
        t.FadeEnds = Time.unscaledTime + FadeSeconds;
        LogOnce(t, factor, reseeded, skipped);
    }

    /// <summary>
    /// ONE line per session, on the first figure whose cloth is re-seeded at a size other than its
    /// board size — the census the next hardware round needs: what the subtree actually contains,
    /// how much of it this pass took responsibility for, and exactly which sub-objects are known NOT
    /// to follow a figure rescale (see the class comment's WHAT IS NOT HANDLED). Counting walks the
    /// subtree three times, which is why it is once and never per resize.
    /// </summary>
    private static void LogOnce(Tracked t, float factor, int reseeded, int skipped)
    {
        if (_logged || Mathf.Abs(factor - 1f) <= FactorEpsilon || t.Root == null)
            return;
        _logged = true;

        Transform[] transforms = t.Root.GetComponentsInChildren<Transform>(true);
        Renderer[] renderers = t.Root.GetComponentsInChildren<Renderer>(true);
        int skinned = 0;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] is SkinnedMeshRenderer) skinned++;

        ParticleSystem[] particles = t.Root.GetComponentsInChildren<ParticleSystem>(true);
        int localScaled = 0;
        for (int i = 0; i < particles.Length; i++)
            if (particles[i] != null && particles[i].main.scalingMode == ParticleSystemScalingMode.Local)
                localScaled++;

        VRLog.Info("FigureGrab",
            $"FIGURE SCALE {t.Root.name} settled at {factor:0.###}× of its board size — subtree: "
            + $"{transforms.Length} transforms, {renderers.Length} renderers ({skinned} skinned, which "
            + "follow the root scale exactly and need nothing). Cloth (capes/cloth — the reported "
            + $"parts): {t.Cloths.Count} simulated, {reseeded} RE-INITIALISED at the new size "
            + $"(per-vertex maxDistance/collisionSphereDistance rescaled from the authored metres), "
            + $"{skipped} left alone (destroyed, or no painted constraints to rescale). NOT HANDLED: "
            + $"{localScaled} of {particles.Length} ParticleSystem(s) use ParticleSystemScalingMode."
            + "Local and therefore ignore the root scale by design (reported, not changed — see "
            + "FigureCloth); the figure's worldspace health/condition panel is parented outside the "
            + "figure root by the game (ActorBehaviour.CreateWorldSpaceGUIElements) and no root scale "
            + "can reach it.");
    }
}
