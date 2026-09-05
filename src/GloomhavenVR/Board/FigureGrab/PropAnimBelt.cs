using System.Collections.Generic;
using EPOOutline;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A HELD PROP HOLDS STILL — the hush. <see cref="PropAnimWatch"/> is the instrument half and
/// writes nothing; this class writes and logs nothing that gates it.
///
/// <para><b>READ THIS FIRST: THIS FILE'S PURPOSE WAS INVERTED ON 2026-09-06, AND THE NAME IS A
/// LEFTOVER.</b> Until then this class existed to make a held chest KEEP animating — it wrote
/// <c>Animator.cullingMode = AlwaysAnimate</c> and re-ran the game's world-anchor feeders, and its
/// header argued at length that stopping the animation would be a workaround. That is no longer
/// what it does or what it is for. It now STOPS the animation for the length of the hold, on the
/// user's explicit instruction, and the paragraphs below say why that instruction is the right
/// call and not a retreat. The type is still called <c>PropAnimBelt</c> only because its call
/// sites live in <c>GrabbableProp.cs</c> and <c>PropGrab.cs</c>, which this lane does not own; read
/// "belt" as the belt that holds the thing STILL. The full four-round record is
/// <c>.planning/held-prop-flash-experiments.md</c> and it should be read before anything here is changed.</para>
///
/// <para><b>THE REPORT, 2026-09-06, verbatim.</b> <i>"Das Aufleuchten der Truhen und Fallen
/// funktioniert immer noch nicht richtig in der Hand. […] ich will erstmal, um es einfach zu
/// halten, diese Animation gar nicht mehr in der Hand haben stattdessen."</i> That is a decision,
/// not a bug report: four rounds failed to make the flash play correctly in a palm, so the flash
/// is to be absent from the palm instead. Simplicity over a correct animation, chosen explicitly.
/// A prop's attention-getting blink exists to draw the eye to a chest ON THE BOARD; a chest the
/// player is already holding in their hand needs no help being noticed.</para>
///
/// <para><b>AND THE HONEST PART: THIS MAY NOT BE ENOUGH, BECAUSE THE DRIVER IS STILL
/// UNIDENTIFIED.</b> The POST-BELT A/B (<c>[Props] HELD-PROP ANIMATION A/B for 'Chest' Chest</c>,
/// ModBuild 436 or later, and BOTH machines of a two-player session agree) measured the chest's one
/// controller-bearing animator in both windows and found it IDLE in both:
/// <c>clipRate hand=0.000/s home=0.000/s</c>, <c>anyLayerRate 0.000/s</c> across every layer,
/// <c>advancing 0/665</c> and <c>0/360</c>, not one of forty tracked material property slots
/// moving in either window, and <c>MATERIAL FLASH TERM (SpawnObjectAnimateMaterial_SMB): NONE on
/// this animator's controller</c>. The window the round measured never contained the flash. So
/// switching that animator off is a suppression of a thing that was already still, and if it is
/// the only suppression here then nothing visible will change. That is precisely why this class
/// now also silences the second named candidate, and why <see cref="Announce"/> ENUMERATES what
/// else is alive under the held prop instead of asserting a cause it cannot see.</para>
///
/// <para><b>THE SAME LOG ALSO UNDERCUT THE OLD BELT'S OWN MECHANISM, WHICH IS THE OTHER HALF OF
/// WHY IT IS GONE.</b> The belt DID apply — <c>cullingMode hand=AlwaysAnimate
/// home=CullUpdateTransforms</c>, <c>updateWhenOffscreen hand=3/3 home=0/3</c>, and at the grab
/// 3 skinned renderers were at <c>updateWhenOffscreen=false</c> with 1 of 3 animators not already
/// <c>AlwaysAnimate</c>, so its preconditions genuinely existed. But the term its argument rested
/// on read the SAME on both sides: <c>worst gap between a renderer's culling bounds centre and its
/// own transform hand=0.237 wu home=0.237 wu</c>. The reparent did not leave the bounds behind at
/// all, so the "Unity thinks the prop is still on its hex" story was never load-bearing.</para>
///
/// <para><b>WHAT IS SUPPRESSED (1): THE ANIMATOR, VIA <c>enabled</c>.</b> Every
/// <c>Animator</c> under the visual is switched off for the hold and switched back on with the
/// exact value it had, object for object. <c>Animator.cullingMode</c> is no longer written at all:
/// Unity does not evaluate a disabled animator, so the culling mode would decide nothing, and a
/// replaced value with no effect is one more thing that can be handed back wrong.</para>
///
/// <para><b>THE ONE ANIMATOR THIS REFUSES TO TOUCH, AND WHY IT IS NOT AN OVERSIGHT.</b> An
/// <c>Animator</c> whose controller carries a <see cref="DelayedDeactivatePropAnimSMB"/> is left
/// running and completely untouched. That state behaviour is NOT a look: its
/// <c>OnStateUpdate</c> counts a delay down and then calls <c>DeactivateProp</c>, which sends
/// <c>CDeactivatePropAnim_MessageData</c> into <c>ScenarioRuleClient.MessageHandler</c>
/// (DelayedDeactivatePropAnimSMB.cs:102-141) — a RULES message, for a sprung trap. It also holds
/// itself in a static list behind <c>DelayedDeactivationsAreInProgress()</c> (:144-151), which the
/// game polls to decide whether it may proceed. Freezing that animator mid-countdown would stall a
/// rules message and leave a global "still in progress" true for as long as the player keeps hold
/// of the prop — a phase deadline waiting to be missed, produced by a rendering lane writing game
/// state by accident. The user asked for the blink to stop, not for the trap to stop springing.
/// The count of animators skipped for this reason is in the census line, so a hold where it
/// mattered is visible in the log rather than inferred.</para>
///
/// <para><b>WHAT IS SUPPRESSED (2): <c>EPOOutline.Outlinable</c>, VIA THE COMPONENT'S OWN
/// <c>enabled</c> FLAG — AND THE FIELD CHOICE IS THE WHOLE POINT.</b> The game raises a glowing
/// silhouette around a hovered board object through
/// <c>WorldspaceUITools.EnableHoveredOutline</c> (decompiled WorldspaceUITools.cs:156-163), which
/// writes <c>outlinable.OutlineParameters.Enabled = true</c>. Writing <c>false</c> back into that
/// same property is the obvious move and it is the wrong one, for two independent reasons:</para>
/// <list type="number">
///   <item><b>It has nine other writers.</b> <c>OutlineParameters.Enabled</c> is assigned in NINE
///   places in <c>WorldspaceUITools</c> alone (:52, :71, :76, :147, :161, :172, :187, :198, :207)
///   — hover on, hover off, ability focus on and off, and a global <c>ActivateAllOutlines</c>
///   sweep. A single write at the grab edge would be re-stomped by the next hover the frame after,
///   and holding it down would be a per-frame write war against the game over a flag the game
///   believes it owns. This project has a standing ruling about that shape: concede the flag.</item>
///   <item><b>It only silences one of three.</b> An <c>Outlinable</c> carries THREE independent
///   parameter blocks — <c>OutlineParameters</c>, <c>FrontParameters</c> and
///   <c>BackParameters</c> (Outlinable.cs:160-184) — and clearing one leaves the other two free to
///   draw.</item>
/// </list>
///
/// <para>The component's own <c>enabled</c> flag has NEITHER problem. Grep the whole decompiled
/// tree for a write to an <c>Outlinable</c>'s <c>enabled</c> and nothing comes back: the game
/// never touches it, so there is no writer to fight. And it is the switch the outline system
/// itself is built on — <c>Outlinable.OnDisable</c> removes the component from the static
/// <c>outlinables</c> list the outline pass walks, and <c>UpdateVisibility</c>'s very first test is
/// <c>if (!enabled) { outlinables.Remove(this); return; }</c> (Outlinable.cs:236-245, 264-270). One
/// bool removes the object from the pass entirely, whatever any of the three parameter blocks say.
/// Handing the bool back re-runs the component's own <c>OnEnable</c>, which re-registers it at
/// whatever <c>OutlineParameters.Enabled</c> the GAME has meanwhile decided on. The restore is
/// therefore exact without this mod ever having read or written the game's own field.</para>
///
/// <para><b>WHETHER A HELD CHEST EVEN CARRIES ONE IS NOT KNOWN, AND IS NOT GUESSED.</b> No round
/// has ever counted <c>Outlinable</c> components on a prop; the only census this project has of
/// them is on CREATURES (Core/Haunt/HauntFigures.Clone.cs:101-118). If the count is zero this
/// strand makes zero writes and costs one list walk, and the census line says <c>0</c> — which is
/// itself the answer to a standing question. Do not read this file as a claim that the outline was
/// the flash.</para>
///
/// <para><b>WHAT IS DELIBERATELY STILL WRITTEN: <c>SkinnedMeshRenderer.updateWhenOffscreen</c>.</b>
/// This one survived the inversion because it was never an animation term. It decides whether a
/// skinned mesh is DRAWN AT ALL, by deciding whether Unity culls it against the pose it is in or
/// against authored <c>localBounds</c> carried by the root bone. Freezing the animator makes those
/// bounds MORE stale rather than less, so if anything the case for it is stronger now than it was
/// when the belt still ran the other way. A held prop that vanishes out of the player's hand
/// because a stale bounding box left the frustum would be a far worse defect than the blink this
/// change removes. It is written once per hold and handed back at the landing.</para>
///
/// <para><b>WHAT WAS REMOVED WITH THE INVERSION.</b> The world-anchor strand is gone. It re-ran
/// <c>ZephyrAnim.OnEnable</c> and <c>ObjectPosToMaterial.OnEnable</c> on a moving held prop by
/// toggling their <c>enabled</c> flags, so that a shader sweep anchored on the object's world
/// position would follow it into the palm. Its entire purpose was to make a sweep look RIGHT in
/// the hand; there is no longer a sweep to make look right, and it was the riskiest write in the
/// file (a component that threw out of its own <c>OnEnable</c> had to be caught and re-armed by
/// hand). The three feeder types are still COUNTED for the census — <c>PosToMat</c> especially,
/// because it pushes <c>_ObjPosY</c> into a material every single frame on its own
/// (decompiled PosToMat.cs:5-11) and is therefore a live per-frame material writer on a held prop
/// that no round has yet ruled in or out.</para>
///
/// <para><b>NOTHING HERE WRITES GAME STATE.</b> No <c>GameObject.activeSelf</c>, no
/// <c>Renderer.enabled</c>, no <c>Animator.speed</c>, no material property, no animator parameter,
/// and — see above — no animator that drives a rules message. Every field written is a per-client
/// rendering switch on this client's own copy of the prop.</para>
///
/// <para><b>EVERY SUPPRESSION HAS AN EXACT RESTORE, AND THAT IS THE LOAD-BEARING INVARIANT.</b>
/// A prop that came back out of a hand with its animation permanently off would be a worse defect
/// than the one being fixed. So: every replaced value is remembered per OBJECT in a parallel
/// ledger and never guessed or shared; <see cref="Restore"/> walks the ledgers and writes each one
/// back; both landing paths call <see cref="Release"/> (<c>GrabbableProp.FinishGlide</c> and
/// <c>GrabbableProp.Restore</c>); <see cref="ReleaseAll"/> covers a scenario change, the feature
/// dial going off and uninstall; <see cref="Reset"/> is <see cref="ReleaseAll"/> plus the log
/// budget; <see cref="Tick"/> runs ABOVE the feature gate for exactly this reason; and
/// <see cref="Engage"/> is idempotent per visual, so a re-grab during the release glide finds the
/// existing ledger and keeps it rather than overwriting the original values with the suppressed
/// ones — which would make the restore a no-op and strand the animator off for the session.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire and no wire field is needed. A peer does
/// not render a held prop at all — the prop is in THIS client's hand, on THIS client's copy, and a
/// peer's own game is animating its own copy on its own hex. Every field written is a per-client
/// rendering switch: whether Unity evaluates an animator, whether an outline pass walks a
/// component, where it thinks a skinned mesh is. None of them changes the prop's transform, the
/// rules state, or anything a peer could observe. The state that justifies the suppression (a prop
/// in a hand) is itself local, which is exactly why the correction is local too. The whole-board
/// opt-out remains <c>[Net] RemoteBoards</c>; there is no per-sub-feature sync setting and this
/// needs none.</para>
///
/// <para><b>COST.</b> Three <c>GetComponentsInChildren</c> walks per belt, then again at most every
/// <see cref="RescanFrames"/> frames, over a prop subtree of under two dozen nodes, for at most the
/// two props a pair of hands can hold. <see cref="Tick"/> is a <c>Count</c> compare when nothing is
/// held and one frame-counter compare per held prop otherwise. No allocation on the per-frame path.
/// </para>
/// </summary>
internal static class PropAnimBelt
{
    /// <summary>Frames between re-walks of a suppressed prop's subtree. Apparance content is
    /// destroyed and re-instantiated on a refresh (that is the whole of ModBuild 349), and a fresh
    /// animator arrives ENABLED — so a suppression written once at the grab can quietly stop
    /// covering the objects that are actually drawing. The hold also FREEZES
    /// <c>MonitorMovement</c> (GrabbableProp.FreezeApparance), which makes a rebuild mid-hold
    /// unlikely rather than impossible; half a second is cheap insurance against the case that
    /// survives.</summary>
    private const int RescanFrames = 45;

    /// <summary>How many census lines a session prints IN TOTAL.</summary>
    private const int LogBudget = 4;

    /// <summary>Prop KINDS the roster remembers. The census is the round's only new evidence and
    /// the report names two kinds — a chest AND a trap — so one budget spent entirely on chests
    /// would answer half the question. A kind that has already printed is skipped, which is what
    /// makes the total budget reach a second kind.</summary>
    private const int KindCap = 6;

    /// <summary>Component TYPE NAMES listed by the census before it stops naming them. The total
    /// distinct count is printed either way: a truncated list is not absence, and this project has
    /// already read an ellipsis as proof that something never appeared.</summary>
    private const int TypeListCap = 24;

    // ---- shared scratch, cleared per use (no per-frame allocation) -----------------------------
    private static readonly List<Animator> AnimScratch = new(8);
    private static readonly List<SkinnedMeshRenderer> SkinScratch = new(16);
    private static readonly List<Outlinable> OutlineScratch = new(8);
    private static readonly List<MonoBehaviour> BehaviourScratch = new(16);
    private static readonly List<ParticleSystem> ParticleScratch = new(8);
    private static readonly List<Renderer> RendScratch = new(16);

    /// <summary>Census-only scratch: distinct MonoBehaviour type names under the held prop and how
    /// many of each. Written and read by <see cref="Announce"/> alone.</summary>
    private static readonly List<string> TypeNames = new(32);
    private static readonly List<int> TypeCounts = new(32);

    /// <summary>One held prop's suppression: which objects were written, and the value each one
    /// had. Restoring walks these lists, so a value is never guessed and never shared between
    /// props.</summary>
    private sealed class Belt
    {
        internal GameObject? Visual;

        /// <summary>Animators switched off, and the <c>enabled</c> value each one had.</summary>
        internal readonly List<Animator> Animators = new(4);
        internal readonly List<bool> AnimEnabled0 = new(4);

        /// <summary>Outline components switched off, and the <c>enabled</c> value each one had.
        /// The GAME's <c>OutlineParameters.Enabled</c> is neither read nor written — see the type
        /// doc for why that field is the wrong one to take.</summary>
        internal readonly List<Outlinable> Outlines = new(4);
        internal readonly List<bool> OutlineEnabled0 = new(4);

        /// <summary>Skinned renderers made to cull against their real pose, and the
        /// <c>updateWhenOffscreen</c> value each one had. This is the one write that is NOT a
        /// suppression: it keeps a held prop DRAWN.</summary>
        internal readonly List<SkinnedMeshRenderer> Skins = new(8);
        internal readonly List<bool> SkinOffscreen0 = new(8);

        /// <summary>Animators deliberately left running because their controller carries a
        /// <see cref="DelayedDeactivatePropAnimSMB"/>, which drives a rules message rather than a
        /// look. Counted, never written.</summary>
        internal int AnimatorsLeftForRules;

        /// <summary>How many of each set were ALREADY in the state this class wants. A suppression
        /// that changed nothing is a finding, not a non-event: it means the thing was not on in
        /// the first place, which narrows the search for whatever the flash actually is.</summary>
        internal int AnimatorsAlreadyOff, OutlinesAlreadyOff, SkinsAlready;

        internal int NextScanFrame;
        internal int Rescans;
    }

    private static readonly List<Belt> Live = new(2);
    private static readonly List<Belt> Pool = new(2);
    private static int _logsLeft = LogBudget;

    /// <summary>Prop kinds that have already printed their census. Held as the label strings the
    /// caller passes, which is what the log line is anchored on.</summary>
    private static readonly List<string> KindsDone = new(KindCap);

    /// <summary>
    /// Suppress a prop entering a hand. Idempotent per visual: a re-grab during the release glide
    /// finds the existing ledger and keeps it, so the original values are never overwritten with
    /// the suppressed ones (which would make the restore a no-op and strand the animator off for
    /// the rest of the session).
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
        Announce(b, label);
    }

    /// <summary>One frame: re-walk any suppressed prop whose cadence has come round. A
    /// <c>Count</c> compare when nothing is held, and one int compare per held prop otherwise.
    /// Called from <c>PropGrab.Tick</c> ABOVE the feature gate, for the reason the Apparance thaw
    /// is: a suppression written on a prop still in a hand when the dial goes off must still be
    /// handed back, and the gate's <c>ReleaseAll</c> runs on that same frame.</summary>
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
            if (Time.frameCount < b.NextScanFrame)
                continue;
            b.NextScanFrame = Time.frameCount + RescanFrames;
            b.Rescans++;
            Apply(b);
        }
    }

    /// <summary>Hand every replaced value back for one prop and drop the record. Idempotent, and
    /// safe on a prop that was never suppressed. Called from BOTH landing paths
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
            Restore(Live[i]);
            Retire(i);
            return;
        }
    }

    /// <summary>Hand everything back — a scenario change, the feature dial going off, uninstall.
    /// The suppression must never outlive the hold that justified it.</summary>
    internal static void ReleaseAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Restore(Live[i]);
            Retire(i);
        }
    }

    /// <summary>Re-arm the per-session census budget for a new scenario (a new scenario is a new
    /// hardware question), after handing every live suppression back.</summary>
    internal static void Reset()
    {
        ReleaseAll();
        _logsLeft = LogBudget;
        KindsDone.Clear();
    }

    // ---- the writes ------------------------------------------------------------------------------

    /// <summary>Walk the prop's subtree and suppress anything not already suppressed, remembering
    /// the value it replaced. Inactive objects are included on purpose: a subtree that is inactive
    /// now can be active two frames later (<c>ProceduralMapTile.ShowContent</c> deactivates whole
    /// generated subtrees), and the door belt's own account names covering only what is active
    /// right now as the way to arm the wrong subtree.</summary>
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

            // THE RULES EXCEPTION. Not in the ledger at all, so the restore cannot touch it either:
            // an animator we never wrote is an animator we must not write on the way out.
            if (DrivesRules(a))
            {
                b.AnimatorsLeftForRules++;
                continue;
            }

            b.Animators.Add(a);
            b.AnimEnabled0.Add(a.enabled);
            if (!a.enabled)
                b.AnimatorsAlreadyOff++;
            else
                a.enabled = false;
        }
        AnimScratch.Clear();

        OutlineScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, OutlineScratch);
        for (int i = 0; i < OutlineScratch.Count; i++)
        {
            Outlinable o = OutlineScratch[i];
            if (o == null || Contains(b.Outlines, o))
                continue;
            b.Outlines.Add(o);
            b.OutlineEnabled0.Add(o.enabled);
            if (!o.enabled)
                b.OutlinesAlreadyOff++;
            else
                o.enabled = false;   // Outlinable.OnDisable drops it from the pass's static list
        }
        OutlineScratch.Clear();

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
        SkinScratch.Clear();
    }

    /// <summary>Does this animator's controller carry the state behaviour that sends a rules
    /// message? <c>GetBehaviours</c> allocates, so this runs once per animator per walk — at the
    /// grab and then at most every <see cref="RescanFrames"/> frames — and never on the per-frame
    /// path. A null controller has no behaviours and Unity answers with an empty array.</summary>
    private static bool DrivesRules(Animator a)
    {
        DelayedDeactivatePropAnimSMB[] found = a.GetBehaviours<DelayedDeactivatePropAnimSMB>();
        return found != null && found.Length > 0;
    }

    /// <summary>Give every replaced value back, object for object. A destroyed object is skipped
    /// rather than written — Unity's <c>!= null</c> answers that — and the lists are cleared by
    /// <see cref="Retire"/> whatever happens here.</summary>
    private static void Restore(Belt b)
    {
        for (int i = 0; i < b.Animators.Count && i < b.AnimEnabled0.Count; i++)
        {
            Animator a = b.Animators[i];
            if (a != null)
                a.enabled = b.AnimEnabled0[i];
        }
        for (int i = 0; i < b.Outlines.Count && i < b.OutlineEnabled0.Count; i++)
        {
            Outlinable o = b.Outlines[i];
            if (o != null)
                o.enabled = b.OutlineEnabled0[i];
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
        b.AnimEnabled0.Clear();
        b.Outlines.Clear();
        b.OutlineEnabled0.Clear();
        b.Skins.Clear();
        b.SkinOffscreen0.Clear();
        b.AnimatorsLeftForRules = 0;
        b.AnimatorsAlreadyOff = 0;
        b.OutlinesAlreadyOff = 0;
        b.SkinsAlready = 0;
        b.Rescans = 0;
        b.NextScanFrame = 0;
        if (Pool.Count < 4)
            Pool.Add(b);
    }

    // ---- the census ------------------------------------------------------------------------------

    /// <summary>
    /// Say what was suppressed AND what was left alive, once per prop kind. Pure print — the state
    /// machine above has already finished by the time this runs, so gating or deleting this method
    /// changes nothing but the log (the shape <c>scripts/check-instrument-writes.py</c> exists to
    /// keep true).
    ///
    /// <para><b>THE SECOND HALF OF THIS LINE IS THE POINT OF THE WHOLE ROUND.</b> Four rounds
    /// measured one animator's layers and one material's property table, and all four found them
    /// still. Nobody has ever simply ASKED what components a held chest carries. This enumerates
    /// them — the distinct MonoBehaviour type names under the visual with counts, the particle
    /// systems and whether they are playing, the lights, the renderers, the three world-anchor
    /// feeders — so the next hardware log NAMES the candidate instead of re-measuring the window
    /// that four rounds have already proved empty.</para>
    /// </summary>
    private static void Announce(Belt b, string label)
    {
        if (_logsLeft <= 0 || KindSpent(label))
            return;
        _logsLeft--;
        if (KindsDone.Count < KindCap)
            KindsDone.Add(label);

        GameObject? go = b.Visual;
        if (go == null)
            return;

        int animOff = b.Animators.Count - b.AnimatorsAlreadyOff;
        int outlineOff = b.Outlines.Count - b.OutlinesAlreadyOff;
        int skinChanged = b.Skins.Count - b.SkinsAlready;

        // ---- what is still alive under the prop ----
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        int particles = ParticleScratch.Count;
        int particlesPlaying = 0;
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p != null && p.isPlaying)
                particlesPlaying++;
        }
        ParticleScratch.Clear();

        RendScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, RendScratch);
        int renderers = RendScratch.Count;
        int renderersVisible = 0;
        for (int i = 0; i < RendScratch.Count; i++)
        {
            Renderer r = RendScratch[i];
            if (r != null && r.isVisible)
                renderersVisible++;
        }
        RendScratch.Clear();

        int lights = go.GetComponentsInChildren<UnityEngine.Light>(true).Length;

        int zephyr = 0, objPos = 0, posToMat = 0, behaviours = 0;
        TypeNames.Clear();
        TypeCounts.Clear();
        BehaviourScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, BehaviourScratch);
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour c = BehaviourScratch[i];
            if (c == null)
                continue;
            behaviours++;
            if (c is ZephyrAnim)
                zephyr++;
            else if (c is ObjectPosToMaterial)
                objPos++;
            else if (c is PosToMat)
                posToMat++;
            Tally(c.GetType().Name);
        }
        BehaviourScratch.Clear();

        int distinctTypes = TypeNames.Count;

        var sb = new System.Text.StringBuilder(2048);
        sb.Append("[Props] HELD-PROP ANIMATION HUSH for ").Append(label).Append(" — this mod now ")
          .Append("SUPPRESSES the prop's own animation for the length of the hold instead of ")
          .Append("trying to keep it playing correctly, which is the user's explicit call after ")
          .Append("four rounds failed to make it play right in a palm (.planning/held-prop-flash-experiments.md). ")
          .Append("SUPPRESSED — ANIMATORS: ").Append(b.Animators.Count).Append(" under the visual, ")
          .Append(animOff).Append(" switched off, ").Append(b.AnimatorsAlreadyOff)
          .Append(" already off, ").Append(b.AnimatorsLeftForRules)
          .Append(" LEFT RUNNING on purpose because their controller carries ")
          .Append("DelayedDeactivatePropAnimSMB, which sends a rules message for a sprung trap and ")
          .Append("holds a global 'deactivations in progress' flag — freezing that one would stall ")
          .Append("game state, and this lane does not write game state. SUPPRESSED — OUTLINES: ")
          .Append(b.Outlines.Count).Append(" EPOOutline.Outlinable under the visual, ")
          .Append(outlineOff).Append(" switched off, ").Append(b.OutlinesAlreadyOff)
          .Append(" already off. That count is itself an ANSWER: no round has ever measured ")
          .Append("whether a held chest carries one, and the game raises a glowing silhouette ")
          .Append("through WorldspaceUITools.EnableHoveredOutline. The component's own enabled ")
          .Append("flag is taken and not OutlineParameters.Enabled, because the game writes that ")
          .Append("property in nine places and would stomp us, and because it is only one of the ")
          .Append("three parameter blocks an Outlinable can draw from. NOT SUPPRESSED — ")
          .Append("SkinnedMeshRenderer.updateWhenOffscreen: ").Append(b.Skins.Count)
          .Append(" skin(s), ").Append(skinChanged).Append(" set true, ").Append(b.SkinsAlready)
          .Append(" already true — that one is not an animation term at all, it keeps a held prop ")
          .Append("DRAWN when its stale root-bone bounds leave the frustum, and a frozen animator ")
          .Append("makes those bounds staler still. ");

        sb.Append("STILL ALIVE UNDER THIS PROP, AND THIS IS THE PART THE NEXT ROUND NEEDS: ")
          .Append(renderers).Append(" renderer(s) of which ").Append(renderersVisible)
          .Append(" reported isVisible (READ IT AS THE PREVIOUS FRAME'S ANSWER — isVisible is last "
                  + "frame's culling result and the prop has only just been reparented, so it "
                  + "describes the hex and not the hand); ")
          .Append(particles).Append(" particle system(s) of which ")
          .Append(particlesPlaying).Append(" playing; ").Append(lights).Append(" light(s); ")
          .Append("world-anchor feeders ZephyrAnim x").Append(zephyr)
          .Append(", ObjectPosToMaterial x").Append(objPos).Append(", PosToMat x").Append(posToMat)
          .Append(" (PosToMat pushes _ObjPosY into a material EVERY FRAME by itself, so a non-zero ")
          .Append("count there is a live per-frame material writer on a held prop that no round ")
          .Append("has ruled in or out); ").Append(behaviours).Append(" MonoBehaviour(s) in ")
          .Append(distinctTypes).Append(" distinct type(s)");

        // A TRUNCATED LIST IS NOT ABSENCE. Whether the list is complete is stated in words, not
        // left to an ellipsis: this project has already read "X never appears" off a capped census
        // and been wrong. The distinct count above is the whole population either way.
        int listed = distinctTypes < TypeListCap ? distinctTypes : TypeListCap;
        if (distinctTypes == 0)
        {
            sb.Append(" (none — this prop carries no MonoBehaviour at all, which is itself an "
                      + "answer: whatever the flash is, it is not a component on this object)");
        }
        else
        {
            sb.Append(listed == distinctTypes
                ? ", and ALL of them are named here: "
                : ", of which only the first " + listed + " are named here — THE LIST IS "
                  + "TRUNCATED and the distinct count above is the whole population: ");
            for (int i = 0; i < listed; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(TypeNames[i]).Append(" x").Append(TypeCounts[i]);
            }
        }

        sb.Append(". WHY THIS ENUMERATION EXISTS: the last A/B measured this prop's one ")
          .Append("controller-bearing animator in both the HAND and HOME windows and found it IDLE ")
          .Append("in BOTH — clipRate 0.000/s, anyLayerRate 0.000/s on every layer, advancing ")
          .Append("0/665 and 0/360, not one of forty material property slots moving, and no ")
          .Append("SpawnObjectAnimateMaterial_SMB on the controller at all. So the flash the user ")
          .Append("sees is NOT that animator, four rounds measured a window that never contained ")
          .Append("it, and switching that animator off may therefore change nothing visible. If ")
          .Append("the flash survives this build, the driver is one of the names listed above — ")
          .Append("read the list, do not re-measure the animator. ")
          .Append(_logsLeft).Append(" more prop hush line(s) this session, at most one per prop kind.");

        // HW-VERIFY: this line IS the round's deliverable — it is the first census anyone has taken
        // of what a held prop actually carries, and the standing question ("what is the flash, if
        // it is not the animator?") is answered by reading it. It must stay at a tier the DEFAULT
        // log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Has this prop KIND already printed its census? The roster is per SCENARIO and
    /// <see cref="Reset"/> clears it, because a new scenario is a new hardware question.</summary>
    private static bool KindSpent(string label)
    {
        for (int i = 0; i < KindsDone.Count; i++)
        {
            if (string.Equals(KindsDone[i], label, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Count one component type name into the census histogram. Linear over a list that
    /// is at most a few dozen entries long and lives for the length of one log line.</summary>
    private static void Tally(string typeName)
    {
        for (int i = 0; i < TypeNames.Count; i++)
        {
            if (string.Equals(TypeNames[i], typeName, System.StringComparison.Ordinal))
            {
                TypeCounts[i]++;
                return;
            }
        }
        TypeNames.Add(typeName);
        TypeCounts.Add(1);
    }
}
