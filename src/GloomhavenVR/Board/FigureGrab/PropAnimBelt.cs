using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A HELD PROP KEEPS ANIMATING — the belt, and the mechanism half of the 2026-09-03 / 2026-09-05
/// report. <see cref="PropAnimWatch"/> is the instrument half and writes nothing; this class
/// writes and logs nothing that gates it.
///
/// <para><b>THE REPORT (2026-09-05), verbatim.</b> <i>"Eine Truhe oder Falle hat diese
/// weiß-Blink-Animation, um besser auf sie aufmerksam zu machen. Wenn man das Prop in der Hand
/// hat, ist diese Animation viel langsamer und ploppt auch manchmal mitten drin einfach weg — ich
/// will, dass die Animation da genau gleich ungestört weiter abspielt, wie sie es macht, wenn sie
/// nicht in der Hand ist."</i> Two symptoms: a RATE that fell, and a play that STOPS part way.</para>
///
/// <para><b>WHAT THIS PUTS BACK, AND WHY IT IS NOT A WORKAROUND.</b> Unity decides whether to
/// evaluate an <c>Animator</c> and whether to skin a <c>SkinnedMeshRenderer</c> from CULLING, and
/// culling is answered from the renderer's BOUNDS. Two of the game's own defaults make that answer
/// wrong for a prop the mod has lifted into a palm:</para>
/// <list type="number">
///   <item><b><c>Animator.cullingMode</c>, and this is the measured one.</b> The ModBuild 435
///   hardware log read the chest's one controller-bearing animator at <c>CullUpdateTransforms</c>
///   in BOTH windows (<c>[Props] HELD-PROP ANIMATION A/B</c>). In that mode Unity withholds
///   retargeting, IK and <b>the write of transforms</b> on every frame on which none of the
///   animator's renderers is visible, while the state machine keeps advancing — so the clip's
///   clock runs on and the picture does not follow it. <b>A player can hold a chest and look
///   somewhere else.</b> That is an ordinary second of VR and it is a state the flat game could
///   not produce at all: there, a prop is on a hex under a camera that looks down on the whole
///   room, and "the thing being animated is on screen" was true by construction. Fewer written
///   frames read as "viel langsamer"; a run of them in the middle of a clip reads as "ploppt
///   mitten drin einfach weg".</item>
///   <item><b><c>SkinnedMeshRenderer.updateWhenOffscreen</c> — the braces, not the belt.</b> The
///   chest's two drawables are SkinnedMeshRenderers (the ghost and pre-grab-highlight censuses
///   name them: <c>'Chest_Crypt_Base_02'</c>, <c>'Chest_Crypt_Lid_02'</c>,
///   <c>Amp_Char_Shader</c>), and every skinned character in this game ships with
///   <c>updateWhenOffscreen = false</c> (FigureGrabbable.cs:1514). With it false a skinned
///   renderer is culled against its authored <c>localBounds</c> carried by the ROOT BONE rather
///   than against the pose it is actually drawn in — the same reason
///   <c>Core: culling-cannot-see-vertex-shaders</c> exists. Whether a reparent into a palm at
///   3.648x actually leaves those bounds behind is NOT measured (the root bone travels with the
///   prop, so they may well follow), and this file does not claim it does. What is certain is that
///   the term above is answered from visibility and visibility is answered from bounds, so making
///   the bounds truthful for the length of the hold removes the one remaining way the first item
///   could come back. The instrument reports the gap between each renderer's bounds centre and its
///   own transform in both windows, so the next log settles it either way.</item>
/// </list>
///
/// <para><b>SO THE BELT IS THE DOOR BELT, ON A HELD PROP.</b> <c>[Compat] DoorAnimateOffscreen</c>
/// (Core/Environment/DoorOpenWatch.cs) already ships exactly this for scenario doors, for exactly
/// this reason — the flat game's camera looks down on the whole room, so a door it opens is on
/// screen while its clip plays, and in VR it is very often behind you. A prop in your hand is the
/// same broken precondition arrived at from the other direction: the flat game never moved a prop
/// off its hex at all. The belt adds no behaviour; it restores an assumption the game was written
/// under, for as long as the mod is holding the thing that breaks it.</para>
///
/// <para><b>IT CANNOT FIGHT A WRITER, AND THAT IS MEASURED, NOT ASSUMED.</b> The decompiled game
/// tree contains ZERO occurrences of <c>cullingMode</c> and ZERO of <c>updateWhenOffscreen</c> —
/// grep the whole of <c>decompiled/</c> for either token and nothing comes back. There is nobody
/// to concede a flag to and no number to own in a <c>LateUpdate</c>: the belt is written once when
/// the prop enters a hand, re-walked on a slow cadence only so a subtree Apparance re-instantiated
/// under us is belted too, and every replaced value is handed back object-for-object when the prop
/// lands. Contrast the field this project DOES have to fight for — a held prop's local TRS — which
/// is re-asserted every frame by <c>GrabbableProp.ApplyHeldPose</c> because the pose genuinely has
/// other writers.</para>
///
/// <para><b>STRAND 2 — THE WORLD ANCHOR, AND WHY IT IS THE GAME'S OWN CALL AND NOT OUR WRITE.</b>
/// The game feeds its Amplify prop/character shaders the object's WORLD POSITION through a shader
/// uniform, and two of the three feeders push it ONCE and then never again while the object moves:
/// <c>ObjectPosToMaterial</c> writes <c>materialProperty</c> (default <c>"_ObjPos"</c>) only in
/// <c>OnEnable</c> (decompiled ObjectPosToMaterial.cs:15-25, and its non-projector branch resolves
/// <c>GetComponent&lt;SkinnedMeshRenderer&gt;()</c> — which is exactly what a chest's drawables
/// are), and <c>ZephyrAnim</c> re-pushes from <c>Update</c> only when its serialized
/// <c>moveUpdate</c> bool is set (ZephyrAnim.cs:41-55), so with that bool off its
/// <c>OnEnable</c> write is the only one there will ever be. The third, <c>PosToMat</c>, pushes
/// <c>"_ObjPosY"</c> every frame (PosToMat.cs:5-11) and needs no help.</para>
///
/// <para>Nothing in the flat game ever moves a prop off its hex, so "written once at spawn" was
/// always true there. This mod moves it into a palm. A shader term anchored at a uniform that
/// still names the HEX, while the mesh it shades is a metre away and 3.648x bigger, is a
/// ready-made account of both halves of the report at once: a sweep anchored off the object
/// arrives late and crosses slowly, and it ends before it has crossed — "viel langsamer" and
/// "ploppt mitten drin einfach weg". It is NOT yet measured on this prop, which is why the fix
/// below is self-gating and why <see cref="PropAnimWatch"/> now reads these uniforms back.</para>
///
/// <para><b>The re-push is the component's own <c>OnEnable</c>, re-run.</b> While the prop is off
/// its hex and has moved since the last push, each <c>ZephyrAnim</c> / <c>ObjectPosToMaterial</c>
/// in its subtree is toggled <c>enabled = false; enabled = true</c>, and the GAME's own
/// <c>OnEnable</c> then writes the GAME's own property name, off the GAME's own serialized fields
/// (<c>renderObjs</c>, <c>getForward</c>, <c>directionProperty</c>, <c>rendererTypeProjector</c> —
/// all private, none of which this mod has to know or guess). We write one bool on a purely
/// visual helper; every material write is the game's. There is no writer to fight, because the
/// only other caller of that code is the same component's own <c>Update</c>, which pushes the
/// identical value from the identical transform when it pushes at all. If a prop carries none of
/// these components this strand makes ZERO writes and costs one <c>List.Count</c> compare.</para>
///
/// <para><b>WHAT THIS DELIBERATELY DOES NOT DO.</b> It does not write <c>Animator.speed</c>. The
/// two prop SMBs latch <c>animator.speed = Timekeeper.instance.m_GlobalClock.timeScale</c> once in
/// <c>OnStateEnter</c> and never refresh it (SpawnObjectAnimateMaterial_SMB.cs:24,
/// DelayedDeactivatePropAnimSMB.cs:58), which is a real way to strand an animator at a quarter
/// speed or at zero — but the ModBuild 435 log measured <c>speed hand=1 home=1</c> and
/// <c>globalClock.timeScale hand=1 home=1</c> on the chest that produced the report, so that latch
/// was NOT what he saw and a re-assert for it would be a remedy for a cause the round already
/// killed. <see cref="PropAnimWatch"/> keeps measuring both terms; if a later log shows
/// <c>speed</c> stranded below the clock, the unlatch belt is one line and
/// <c>DoorOpenWatch</c>'s UNLATCH is the shape to copy.</para>
///
/// <para>It also does not touch <c>GameObject.activeSelf</c> or <c>Renderer.enabled</c>. If the
/// game deactivates a held prop for real — a trap springing, a chest looted — that is a game
/// decision about game state and the instrument REPORTS it (<c>STOP VERDICT</c> separates
/// "the object went inactive" from "the picture froze"). Suppressing it would be writing game
/// state, which this lane does not do.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire and no wire field is needed. Both
/// writes are per-client rendering hints on this client's own copy of the prop: they change WHEN
/// Unity evaluates an animator and WHERE it thinks a skinned mesh is, never what the animator
/// plays, never the prop's transform, never the rules state. A peer's own game already animates
/// its own copy of that chest on its own hex, and a peer WITHOUT the mod is not holding anything
/// at all — the state that breaks the precondition (a prop in a hand) is itself local, which is
/// exactly why the correction is local too. The whole-board opt-out remains
/// <c>[Net] RemoteBoards</c>; there is no per-sub-feature sync setting and this needs none.</para>
///
/// <para><b>COST.</b> One <c>GetComponentsInChildren</c> pair into shared scratch lists per belt,
/// then again at most every <see cref="RescanFrames"/> frames, over a prop subtree of under two
/// dozen nodes, for at most the two props a pair of hands can hold. <see cref="Tick"/> is a
/// <c>Count</c> compare when nothing is held and one frame-counter compare per held prop
/// otherwise. No allocation on the per-frame path.</para>
/// </summary>
internal static class PropAnimBelt
{
    /// <summary>Frames between re-walks of a belted prop's subtree. Apparance content is destroyed
    /// and re-instantiated on a refresh (that is the whole of ModBuild 349), and a fresh animator
    /// arrives at the engine default — so a belt written once at the grab can quietly stop covering
    /// the objects that are actually drawing. The hold also FREEZES <c>MonitorMovement</c>
    /// (GrabbableProp.FreezeApparance), which makes a rebuild mid-hold unlikely rather than
    /// impossible; half a second is cheap insurance against the case that survives.</summary>
    private const int RescanFrames = 45;

    /// <summary>How many belts print their line. Two is enough to see that the second reads the
    /// same as the first; past that the belt is silent and still runs.</summary>
    private const int LogBudget = 2;

    /// <summary>Movement (squared world units) below which the world anchor is left alone — 1 mm.
    /// A prop resting in a still hand must not re-run two <c>OnEnable</c>s ninety times a second.</summary>
    private const float AnchorEpsilonSq = 1e-6f;

    // ---- shared scratch, cleared per use (no per-frame allocation) -----------------------------
    private static readonly List<Animator> AnimScratch = new(8);
    private static readonly List<SkinnedMeshRenderer> SkinScratch = new(16);
    private static readonly List<MonoBehaviour> BehaviourScratch = new(8);

    /// <summary>One held prop's belt: which objects were written, and the value each one had.
    /// Restoring walks these lists, so a value is never guessed and never shared between props.</summary>
    private sealed class Belt
    {
        internal GameObject? Visual;
        internal readonly List<Animator> Animators = new(4);
        internal readonly List<AnimatorCullingMode> AnimatorMode0 = new(4);
        internal readonly List<SkinnedMeshRenderer> Skins = new(8);
        internal readonly List<bool> SkinOffscreen0 = new(8);
        internal int NextScanFrame;
        /// <summary>STRAND 2: the game's own world-anchor feeders under this prop, and the world
        /// position at which each was last made to push. Two lists rather than one typed list
        /// because <c>ZephyrAnim</c> and <c>ObjectPosToMaterial</c> share no base type but
        /// <c>MonoBehaviour</c>; <c>PosToMat</c> is deliberately absent — it pushes every frame on
        /// its own (decompiled PosToMat.cs:5-11) and toggling it would be pure cost.</summary>
        internal readonly List<MonoBehaviour> Anchors = new(4);
        internal Vector3 AnchorPushedAt;
        internal bool AnchorPushed;
        /// <summary>How many of the belted animators were ALREADY <c>AlwaysAnimate</c>, and how
        /// many skins already had <c>updateWhenOffscreen</c>. A belt that changed nothing is a
        /// finding: it means neither default was in the way on this prop.</summary>
        internal int AnimatorsAlready, SkinsAlready;
        internal int Rescans;
    }

    private static readonly List<Belt> Live = new(2);
    private static readonly List<Belt> Pool = new(2);
    private static int _logsLeft = LogBudget;
    private static bool _throwLogged;

    /// <summary>True while at least one prop is belted — one <c>Count</c> compare, so callers on
    /// the per-frame path can leave without touching anything else.</summary>
    internal static bool Any => Live.Count > 0;

    /// <summary>
    /// Belt a prop entering a hand. Idempotent per visual: a re-grab during the release glide
    /// finds the existing belt and keeps it, so the original values are never overwritten with the
    /// belted ones (which would make the restore a no-op and strand <c>AlwaysAnimate</c> on the
    /// prop for the rest of the session).
    /// </summary>
    internal static void Engage(GameObject? visual, string label)
    {
        if (visual == null)
            return;
        if (Find(visual) != null)
            return;

        Belt b = Rent();
        b.Visual = visual;
        b.NextScanFrame = Time.frameCount + RescanFrames;
        Live.Add(b);
        Apply(b);
        PushAnchors(b, force: true);
        Announce(b, label);
    }

    /// <summary>One frame: re-walk any belted prop whose cadence has come round. A <c>Count</c>
    /// compare when nothing is held, and one int compare per held prop otherwise. Called from
    /// <c>PropGrab.Tick</c> ABOVE the feature gate, for the reason the Apparance thaw is: a belt
    /// written on a prop still in a hand when the dial goes off must still be handed back, and the
    /// gate's <c>ReleaseAll</c> runs on that same frame.</summary>
    internal static void Tick()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Belt b = Live[i];
            if (b.Visual == null)
            {
                // The prop died under us (looted, broken, teardown). Nothing to hand back — the
                // objects that held the replaced values are gone with it.
                Retire(i);
                continue;
            }
            PushAnchors(b, force: false);
            if (Time.frameCount < b.NextScanFrame)
                continue;
            b.NextScanFrame = Time.frameCount + RescanFrames;
            b.Rescans++;
            Apply(b);
        }
    }

    /// <summary>Hand every replaced value back for one prop and drop the record. Idempotent, and
    /// safe on a prop that was never belted. Called from BOTH landing paths
    /// (<c>GrabbableProp.FinishGlide</c> and <c>GrabbableProp.Restore</c>), which is where
    /// <c>PropAnimWatch.NotifyLanded</c> already converges.</summary>
    internal static void Release(GameObject? visual)
    {
        if (visual == null)
            return;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Live[i].Visual, visual))
                continue;
            // The prop is back on its hex and the caller has already written the home TRS, so this
            // last push leaves the anchor naming the HEX again — exactly the value the game's own
            // OnEnable put there at spawn. Without it the uniform would be left holding the palm.
            PushAnchors(Live[i], force: true);
            Restore(Live[i]);
            Retire(i);
            return;
        }
    }

    /// <summary>Hand everything back — a scenario change, the feature dial going off, uninstall.
    /// The belt must never outlive the hold that justified it.</summary>
    internal static void ReleaseAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PushAnchors(Live[i], force: true);
            Restore(Live[i]);
            Retire(i);
        }
    }

    /// <summary>Re-arm the per-session log budget for a new scenario (a new scenario is a new
    /// hardware question), after handing every live belt back.</summary>
    internal static void Reset()
    {
        ReleaseAll();
        _logsLeft = LogBudget;
        _throwLogged = false;
    }

    // ---- the writes ------------------------------------------------------------------------------

    /// <summary>Walk the prop's subtree and belt anything not already belted, remembering the value
    /// it replaced. Inactive objects are included on purpose: a subtree that is inactive now can be
    /// active two frames later (<c>ProceduralMapTile.ShowContent</c> deactivates whole generated
    /// subtrees), and the door belt's own account names belting only what is active right now as
    /// the way to arm the wrong subtree.</summary>
    private static void Apply(Belt b)
    {
        GameObject? go = b.Visual;
        if (go == null)
            return;

        AnimScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, AnimScratch);
        for (int i = 0; i < AnimScratch.Count; i++)
        {
            Animator a = AnimScratch[i];
            if (a == null || Contains(b.Animators, a))
                continue;
            b.Animators.Add(a);
            b.AnimatorMode0.Add(a.cullingMode);
            if (a.cullingMode == AnimatorCullingMode.AlwaysAnimate)
                b.AnimatorsAlready++;
            else
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        SkinScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, SkinScratch);
        for (int i = 0; i < SkinScratch.Count; i++)
        {
            SkinnedMeshRenderer s = SkinScratch[i];
            if (s == null || Contains(b.Skins, s))
                continue;
            b.Skins.Add(s);
            b.SkinOffscreen0.Add(s.updateWhenOffscreen);
            if (s.updateWhenOffscreen)
                b.SkinsAlready++;
            else
                s.updateWhenOffscreen = true;
        }

        CollectAnchors(go, b);
    }

    /// <summary>Add every world-anchor feeder under the prop that is not already on the list. ONE
    /// subtree walk for both types — the list is append-only for the life of the hold, so a
    /// component collected on the first walk is not re-added by a rescan.</summary>
    private static void CollectAnchors(GameObject go, Belt b)
    {
        BehaviourScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, BehaviourScratch);
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour c = BehaviourScratch[i];
            if (c == null || !(c is ZephyrAnim || c is ObjectPosToMaterial))
                continue;
            if (Contains(b.Anchors, c))
                continue;
            b.Anchors.Add(c);
        }
        BehaviourScratch.Clear();
    }

    /// <summary>
    /// STRAND 2, once per frame while the prop is off its hex: if the visual has moved since the
    /// last push, re-run each feeder's own <c>OnEnable</c> by toggling <c>enabled</c>. The game
    /// writes the material; this writes one bool.
    ///
    /// <para>Gated on real movement (<see cref="AnchorEpsilonSq"/>) so a prop sitting still in a
    /// motionless hand costs one <c>sqrMagnitude</c> compare and no component writes at all. A
    /// component that throws out of its own <c>OnEnable</c> is dropped from the list: a feeder with
    /// no <c>Renderer</c> beside it would otherwise throw once a frame for the whole hold, which is
    /// the shape this project has already paid for (Core: "grep for throws first").</para>
    /// </summary>
    private static void PushAnchors(Belt b, bool force)
    {
        if (b.Anchors.Count == 0)
            return;
        GameObject? go = b.Visual;
        if (go == null)
            return;

        Vector3 now = go.transform.position;
        if (!force && b.AnchorPushed && (now - b.AnchorPushedAt).sqrMagnitude < AnchorEpsilonSq)
            return;
        b.AnchorPushedAt = now;
        b.AnchorPushed = true;

        for (int i = b.Anchors.Count - 1; i >= 0; i--)
        {
            MonoBehaviour c = b.Anchors[i];
            if (c == null)
            {
                b.Anchors.RemoveAt(i);
                continue;
            }
            if (!c.enabled || !c.gameObject.activeInHierarchy)
                continue;   // the GAME has this feeder switched off — that is its decision, not ours
            try
            {
                c.enabled = false;
                c.enabled = true;
            }
            catch (System.Exception e)
            {
                // LEAVING IT OFF WOULD BE THE REAL DAMAGE. The re-arm is a two-step write and the
                // second step is the one that can throw, so a bare `continue` here could hand the
                // game back a permanently disabled component — a write to game state, by accident,
                // on an error path. Put it back, then stop touching this one for the rest of the
                // hold: re-arming a thrower ninety times a second is worse than a stale uniform.
                try { c.enabled = true; }
                catch { /* the component or its object is going away; nothing left to restore */ }
                b.Anchors.RemoveAt(i);
                ReportAnchorThrow(c, e);
            }
        }
    }

    /// <summary>Give every replaced value back, object for object. A destroyed object is skipped
    /// rather than written — Unity's <c>!= null</c> answers that — and the lists are cleared by
    /// <see cref="Retire"/> whatever happens here.</summary>
    private static void Restore(Belt b)
    {
        for (int i = 0; i < b.Animators.Count && i < b.AnimatorMode0.Count; i++)
        {
            Animator a = b.Animators[i];
            if (a != null)
                a.cullingMode = b.AnimatorMode0[i];
        }
        for (int i = 0; i < b.Skins.Count && i < b.SkinOffscreen0.Count; i++)
        {
            SkinnedMeshRenderer s = b.Skins[i];
            if (s != null)
                s.updateWhenOffscreen = b.SkinOffscreen0[i];
        }
    }

    // ---- bookkeeping ------------------------------------------------------------------------------

    private static Belt? Find(GameObject visual)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            if (ReferenceEquals(Live[i].Visual, visual))
                return Live[i];
        }
        return null;
    }

    private static bool Contains<T>(List<T> list, T item) where T : Object
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return true;
        }
        return false;
    }

    private static Belt Rent()
    {
        int last = Pool.Count - 1;
        if (last < 0)
            return new Belt();
        Belt b = Pool[last];
        Pool.RemoveAt(last);
        return b;
    }

    private static void Retire(int index)
    {
        Belt b = Live[index];
        Live.RemoveAt(index);
        b.Visual = null;
        b.Animators.Clear();
        b.AnimatorMode0.Clear();
        b.Skins.Clear();
        b.SkinOffscreen0.Clear();
        b.Anchors.Clear();
        b.AnchorPushed = false;
        b.AnchorPushedAt = Vector3.zero;
        b.AnimatorsAlready = 0;
        b.SkinsAlready = 0;
        b.Rescans = 0;
        b.NextScanFrame = 0;
        if (Pool.Count < 4)
            Pool.Add(b);
    }

    /// <summary>Name a world-anchor feeder that threw out of its own <c>OnEnable</c>, ONCE per
    /// session. A subsystem that throws every frame and says nothing is the failure this project
    /// keeps paying for; one line naming the component and the exception type is what turns it into
    /// a lead. Pure print — the caller has already restored and dropped the component.</summary>
    private static void ReportAnchorThrow(MonoBehaviour c, System.Exception e)
    {
        if (_throwLogged)
            return;
        _throwLogged = true;
        VRLog.Note("FigureGrab",
            $"[Props] HELD-PROP ANIMATION BELT: the world-anchor feeder '{c.GetType().Name}' on "
            + $"'{c.name}' threw {e.GetType().Name} out of its own OnEnable when re-run, so its "
            + "enabled flag was put back and it is not re-run again for the rest of this hold. The "
            + "shader uniform it feeds therefore still names wherever the game last pushed it. "
            + "(1 such line per session.)");
    }

    /// <summary>Say what was belted, on the first two holds of a session. Pure print — the state
    /// machine above has already finished by the time this runs, so gating or deleting this method
    /// changes nothing but the log (the shape <c>scripts/check-instrument-writes.py</c> exists to
    /// keep true).</summary>
    private static void Announce(Belt b, string label)
    {
        if (_logsLeft <= 0)
            return;
        _logsLeft--;

        int animChanged = b.Animators.Count - b.AnimatorsAlready;
        int skinChanged = b.Skins.Count - b.SkinsAlready;

        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] HELD-PROP ANIMATION BELT engaged for {label} — the prop keeps animating in the "
            + "hand because Unity is no longer allowed to answer 'is it visible?' from bounds this "
            + $"mod just invalidated. ANIMATORS: {b.Animators.Count} under the visual, {animChanged} "
            + $"moved to AlwaysAnimate, {b.AnimatorsAlready} already there. SKINNED RENDERERS: "
            + $"{b.Skins.Count} under the visual, {skinChanged} moved to updateWhenOffscreen=true, "
            + $"{b.SkinsAlready} already there. WHY: Animator.cullingMode=CullUpdateTransforms — "
            + "which is what the ModBuild 435 log measured on this chest in BOTH windows — withholds "
            + "the transform write on every frame on which no renderer of that animator is visible, "
            + "while the state machine keeps advancing. A player can hold a chest and look somewhere "
            + "else; the flat game could not produce that state at all, because there a prop is on a "
            + "hex under a camera that sees the whole room. Fewer written frames read as 'viel "
            + "langsamer', a run of them mid-clip as 'ploppt mitten drin einfach weg'. "
            + "updateWhenOffscreen is the braces and not the belt: visibility is answered from a "
            + "skinned renderer's ROOT-BONE bounds rather than from the pose it is drawn in, and "
            + "whether a reparent at 3.648x actually leaves those bounds behind is NOT measured — "
            + "the A/B reports the bounds-vs-transform gap in both windows and settles it. NOBODY TO "
            + "FIGHT: the decompiled "
            + "game writes cullingMode in 0 places and updateWhenOffscreen in 0 places, so this is "
            + "written once per hold, re-walked every 45 frame(s) only in case Apparance rebuilt the "
            + "subtree, and every replaced value is handed back object-for-object when the prop "
            + "lands. NOT WRITTEN: Animator.speed (the ModBuild 435 log measured speed=1 and "
            + "globalClock.timeScale=1 in BOTH windows, so the SMB latch was not what he saw), "
            + "GameObject.activeSelf and Renderer.enabled (a prop the GAME switches off is a game "
            + "decision — the A/B's STOP VERDICT reports it instead of suppressing it). "
            + $"WORLD ANCHOR: {b.Anchors.Count} feeder(s) under the visual (ZephyrAnim / "
            + "ObjectPosToMaterial — the two that push the object's WORLD POSITION into a shader "
            + "uniform and then stop: ObjectPosToMaterial writes it only in OnEnable, ZephyrAnim "
            + "only from Update and only while its serialized moveUpdate bool is set). While the "
            + "prop is off its hex each one is re-run by toggling its own enabled flag, so the GAME "
            + "writes the material with the GAME's own property name and serialized fields and this "
            + "mod writes one bool; 0 feeder(s) means this strand made no writes at all. PosToMat is "
            + "deliberately not touched — it pushes _ObjPosY every frame by itself. "
            + $"({_logsLeft} more prop animation belt lines this session.)");
    }
}
