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
/// <para><b>ROUND FIVE, 2026-09-05: THE HUSH FIRED AND THE SHIMMER SURVIVED IT. WHAT FOLLOWS IS
/// WHY, AND IT IS THE WHOLE REASON THIS FILE GREW.</b> The user, verbatim: <i>"Die
/// Spiel-Highlighting Animation von Fallen und Truhen (dieser weiße Schimmer) ist immer noch auch
/// auf dem Asset sichtbar wenn es in der Hand ist. Das soll nicht der Fall sein."</i> The ModBuild
/// 447 logs from both machines say the suppression genuinely ran — <c>HELD-PROP ANIMATION HUSH for
/// 'BearTrap' Trap</c> reports <c>1 animator switched off</c> and <c>1 Outlinable switched off</c>
/// on the host and the same on the peer. So the hush is silencing something that is not what the
/// eye sees, which is a shape this project has written down twice: <i>"a fix passed its own green
/// readings and changed nothing — the instruments measured the bookkeeping"</i>.</para>
///
/// <para><b>THE 447 LOG ALSO RETIRED THE OLD "NOTHING WAS ANIMATING IN EITHER WINDOW" VERDICT,
/// AND THE HEADER USED TO REPEAT IT AS IF IT WERE STILL TRUE.</b> On ModBuild 447 the same
/// instrument reads, for the trap: <c>'Trap_BearTrap_PR' clipRate hand=n/a home=0.202/s</c>,
/// <c>anyLayerRate home=0.201/s</c>, <c>advancing hand=0/655 home=359/360</c>,
/// <c>layerCount hand=0 home=1</c>. Read it in order: the prop's animator runs a CONTINUOUS,
/// LOOPING clip while it stands on its hex — 359 of 360 home frames advancing, a ~5 s loop — and
/// in the hand it is stopped dead, which is this class doing its job (a disabled Animator reports
/// <c>layerCount 0</c>, which is why the hand column reads <c>n/a</c>). The old header's claim
/// that the animator was idle in BOTH windows was a reading of ModBuild 435/436 and it is no
/// longer the state of the evidence. The animator strand WORKS. It is simply not the shimmer.</para>
///
/// <para><b>AND THE MATERIAL READ-BACK THAT SAID "NOTHING MOVED" CANNOT CARRY THAT WEIGHT: IT IS
/// TRUNCATED, ON THIS EXACT SHADER, FOR THE SECOND TIME IN THIS PROJECT.</b> The A/B line's own
/// population clause reads <c>material(s) 1/1 on shader 'Amp_Char_Shader'; shader propert(y/ies)
/// 57 declared, 20 float/range/colour tracked</c> — <see cref="PropAnimWatch"/> samples the FIRST
/// TWENTY properties of a fifty-seven-property shader. ModBuild 151 lost a build to precisely
/// this: <i>"the census that was supposed to catch all this had a cap of 24 properties, while
/// Amp_Char_Shader declares exactly 24 interesting ones — so every dump was truncated precisely
/// where <c>_MOD_TINT</c> would have appeared"</i>. "Not one of the tracked slots moved" is
/// therefore not evidence that no material property moved. <see cref="EmitPostHushVerdict"/> reads
/// the whole table.</para>
///
/// <para><b>WHAT ACTUALLY PAINTS ON A HELD PROP THAT NO INSTRUMENT HAS EVER MEASURED AND NO
/// SUPPRESSION HAS EVER TOUCHED: LIGHTS.</b> The hush's two sweeps are TYPED on <c>Animator</c>
/// and <c>Outlinable</c>. The census's histogram is <c>GetComponentsInChildren&lt;MonoBehaviour&gt;</c>
/// — and <b><c>Light</c> derives from <c>Behaviour</c>, not from <c>MonoBehaviour</c></b>, so a
/// light is structurally invisible to it and appears only as the bare count at the end of the
/// line. That count is not zero: the 447 census reads <c>2 light(s)</c> under the held
/// <c>'BearTrap'</c> and <c>3 light(s)</c> under the held <c>'GoldPile'</c>, and the MonoBehaviour
/// histogram beside them names <c>RFX4_LightCurves x2</c> and <c>RFX4_LightCurves x3</c> — one
/// curve per light. <c>RFX4_LightCurves.Update</c> (decompiled :30-49) writes
/// <c>lightSource.intensity = LightCurve.Evaluate(t) * GraphIntensityMultiplier</c> EVERY FRAME
/// off <c>Time.time</c>, and re-seeds <c>startTime</c> for ever when its <c>IsLoop</c> is set. A
/// looping curve on a light's intensity IS a shimmer; it is not an animator, not an outline, not a
/// material property and not a renderer, which is exactly why four rounds of animator-and-material
/// measurement came back empty in BOTH windows.</para>
///
/// <para>This project has already paid for that blind spot once, on this codebase, in ModBuild
/// 151: <i>"A LIVE POINT LIGHT INSIDE THE CREATURE. 'LivingSpirit_Light (1)', Point, intensity
/// 20.00, range 1.0 m, parented in the prefab — a lamp centimetres from its own face. Strip's
/// sweep is GetComponentsInChildren&lt;MonoBehaviour&gt;() and Light derives from Behaviour, not
/// MonoBehaviour, so the sweep walked past it structurally."</i> The same sentence describes this
/// file before this build. And the geometry is the aggravating factor: a lamp that is unremarkable
/// on a prop lying on a hex a metre and a half away is a lamp held twenty centimetres from the eye
/// once the player picks the prop up — which is why the report is about the HAND and why nobody
/// has ever complained about it on the board.</para>
///
/// <para><b>WHAT IS SUPPRESSED (3), THEREFORE: EVERY <c>Behaviour</c>-DERIVED EMITTER —
/// <c>Light</c>, <c>Projector</c>, <c>LensFlare</c> — VIA <c>enabled</c>, ledgered per object.</b>
/// The class is taken whole rather than just <c>Light</c> because the type boundary is the defect:
/// naming one more type by hand is how the next one gets missed. None of the three is a renderer,
/// none carries game state, and each one only ever paints. <c>RFX4_LightCurves</c> is deliberately
/// NOT the thing switched off — disabling the WRITER freezes the intensity at whatever the curve
/// last wrote, which is a pinned shimmer rather than no shimmer; disabling the LIGHT removes the
/// picture whoever writes the number, and on release the light comes back at whatever value the
/// curve has meanwhile reached, which is exactly what the game would have had. Own the final
/// value, do not win a write war.</para>
///
/// <para><b>WHAT IS SUPPRESSED (4): PARTICLE SYSTEMS THAT WERE ACTUALLY PLAYING.</b>
/// <c>Stop(withChildren, StopEmittingAndClear)</c> on the ones found PLAYING at the walk, and
/// <c>Play</c> only on those same ones at the landing. Clearing rather than pausing is the point:
/// a paused system leaves its live particles hanging in the air, which is a frozen shimmer and not
/// an absent one. A system that was already stopped is never touched and never restarted, so the
/// restore cannot start an effect the game had finished with — and each one is re-checked at the
/// landing for still being a child of the visual, because these objects are POOLED
/// (<c>SpawnPFXOnEnable</c> hands them to <c>ObjectPool.Recycle</c>) and a recycled object may by
/// then belong to something else entirely.</para>
///
/// <para><b>THE PRE-STATE OF EVERY CLASS IS COUNTED SEPARATELY, AND THAT IS WHAT KEEPS THE NEXT
/// ROUND ABLE TO ATTRIBUTE.</b> Two new suppressions ship together here, which normally means a
/// round that cannot say which one worked. It is answerable anyway because
/// <see cref="Announce"/> prints, per class, how many objects were LIVE before this class wrote
/// anything: <c>N light(s) of which M were ENABLED</c>, <c>K particle system(s) of which J were
/// PLAYING</c>. A class whose pre-count is zero made no writes and cannot be why anything changed,
/// in either direction.</para>
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
/// <para><b>MULTIPLAYER — AND THE SENTENCE THAT USED TO STAND HERE WAS WRONG.</b> It read "a peer
/// does not render a held prop at all". A peer DOES: <c>NetProps</c> mirrors another player's held
/// prop into their hand (<c>[Props] HELD-PROP MIRROR</c>, 108 lines on the host and 135 on the
/// co-player in the 447 session) and it calls <see cref="Engage"/> at <c>NetProps.cs:288</c> and
/// <see cref="Release"/> at <c>NetProps.cs:550</c>, on the same <see cref="Live"/> ledger and the
/// same <see cref="Tick"/>. That is what makes this correction 1:1 by construction rather than by
/// promise: every suppression added to <see cref="Apply"/> reaches the mirrored copy on the peer's
/// machine through the SAME call, so a shimmer removed from the prop in my hand is removed from
/// the prop the peer sees in my hand, with no second code path to keep in step and no wire field.
/// Nothing here goes on the wire and no wire field is needed. Every field written is a per-client
/// rendering switch: whether Unity evaluates an animator, whether an outline pass walks a
/// component, whether a lamp is on, where Unity thinks a skinned mesh is. None of them changes the prop's transform, the
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

    // ---- the POST-HUSH verdict (round five) ---------------------------------------------------
    //
    // The grab-edge census says what was suppressed. It cannot say whether the PICTURE stopped,
    // and the whole finding of round five is that the census was green while the user still saw
    // the shimmer. So a second window opens AFTER the hush has written everything it is going to
    // write, and reports what is still moving and what is still painting. A line that only says
    // "I hushed it" is worth nothing here; this one is worth something only because it observes
    // the prop afterwards.

    /// <summary>Frames the post-hush window samples — four seconds at 90 Hz, deliberately the same
    /// length as <see cref="PropAnimWatch"/>'s HOME window. The trap's own clip loops at
    /// ~0.20 normalized/s on its hex (ModBuild 447), i.e. a five-second period, so a window much
    /// shorter than this could miss a whole cycle and report an honest zero that reads exactly like
    /// a stopped animation. A hold that ends sooner emits what it has, with the frame count.</summary>
    private const int VerdictFrames = 360;

    /// <summary>How many post-hush verdicts a session prints IN TOTAL, at most one per prop kind.</summary>
    private const int VerdictBudget = 3;

    /// <summary>Materials whose FULL property table is read back per frame.</summary>
    private const int VerdictMatCap = 4;

    /// <summary>Properties tracked per material. Sixty-four, not twenty, and the number is the
    /// finding: <c>PropAnimWatch</c> tracks 20 of the 57 <c>Amp_Char_Shader</c> declares, and
    /// ModBuild 151 already lost a build to a 24-property cap on a shader with 24 interesting
    /// properties. The declared count is printed beside the tracked count either way, so a
    /// truncation is visible rather than silent.</summary>
    private const int VerdictPropCap = 64;

    /// <summary>How many MOVING properties / lights the verdict names before it stops naming
    /// them. The totals are printed either way.</summary>
    private const int VerdictListCap = 6;

    /// <summary>Animators, lights, particle systems and renderers sampled per frame in the
    /// post-hush window. A prop carries a handful of each; the caps exist so a pathological
    /// prefab cannot turn a per-frame sampler into a stall, and every one of them is printed as
    /// found-vs-sampled.</summary>
    private const int VerdictObjCap = 12;

    /// <summary>A property (or a light's intensity) counts as MOVING when it changes by more than
    /// this between two consecutive frames. Small enough to catch a slow breathe, large enough
    /// that float noise in a value nothing writes does not read as an animation.</summary>
    private const float MoveEpsilon = 1e-4f;

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

        /// <summary>Every <c>Behaviour</c>-derived EMITTER switched off — <c>Light</c>,
        /// <c>Projector</c>, <c>LensFlare</c> — and the <c>enabled</c> value each one had. Held as
        /// <c>Behaviour</c> on purpose: the type boundary is the defect this strand exists for, and
        /// a per-type list is a fourth type waiting to be forgotten.</summary>
        internal readonly List<Behaviour> Emitters = new(8);
        internal readonly List<bool> EmitterEnabled0 = new(8);

        /// <summary>Particle systems this class STOPPED, i.e. only the ones that were PLAYING when
        /// it walked. A system that was already stopped is not in here and is never restarted.</summary>
        internal readonly List<ParticleSystem> Particles = new(8);

        /// <summary>Animators deliberately left running because their controller carries a
        /// <see cref="DelayedDeactivatePropAnimSMB"/>, which drives a rules message rather than a
        /// look. Counted, never written.</summary>
        internal int AnimatorsLeftForRules;

        /// <summary>How many of each set were ALREADY in the state this class wants. A suppression
        /// that changed nothing is a finding, not a non-event: it means the thing was not on in
        /// the first place, which narrows the search for whatever the flash actually is.</summary>
        internal int AnimatorsAlreadyOff, OutlinesAlreadyOff, SkinsAlready;

        /// <summary>THE PRE-STATE, PER CLASS, AND IT IS WHAT MAKES TWO SUPPRESSIONS IN ONE ROUND
        /// ATTRIBUTABLE. How many objects of each new class existed under the visual and how many
        /// of them were LIVE before this class wrote anything. A class whose live count is zero
        /// made no writes at all and cannot be why anything got better OR worse.</summary>
        internal int LightsFound, LightsOn0, ProjectorsFound, ProjectorsOn0, FlaresFound, FlaresOn0;
        internal int ParticlesFound, ParticlesPlaying0;

        /// <summary>How many emitters and particle systems were found in a state this class did
        /// NOT leave them in when <see cref="Restore"/> ran — i.e. somebody else wrote them during
        /// the hold. Zero is the expected reading and a non-zero one is the falsifier for the
        /// restore: it means the value handed back is being handed back over a foreign write.</summary>
        internal int RestoreForeignEmitters, RestoreForeignParticles, RestoreDead;

        internal int NextScanFrame;
        internal int Rescans;

        /// <summary>Label the belt was engaged with, so the restore line can name the prop without
        /// the caller having to pass it a second time.</summary>
        internal string Label = string.Empty;
    }

    private static readonly List<Belt> Live = new(2);
    private static readonly List<Belt> Pool = new(2);
    private static int _logsLeft = LogBudget;

    /// <summary>Prop kinds that have already printed their census. Held as the label strings the
    /// caller passes, which is what the log line is anchored on.</summary>
    private static readonly List<string> KindsDone = new(KindCap);

    /// <summary>How many restore lines a session prints IN TOTAL, at most one per prop kind.</summary>
    private const int RestoreLogBudget = 3;

    private static int _restoreLogsLeft = RestoreLogBudget;
    private static readonly List<string> RestoreKindsDone = new(KindCap);

    // ---- the POST-HUSH verdict's own state ----------------------------------------------------
    //
    // AT MOST ONE BELT IS EVER ARMED. Two props in two hands would otherwise need two of every
    // buffer below, and the question ("what is still painting on a hushed prop?") is answered by
    // one prop as well as by two. Everything here is static and fixed-size, so the per-frame path
    // allocates nothing.

    private static int _verdictsLeft = VerdictBudget;
    private static readonly List<string> VerdictKindsDone = new(KindCap);

    private static Belt? _vBelt;
    private static string _vLabel = string.Empty;
    private static int _vFrames;
    private static int _vEndFrame;

    private static Animator[] _vAnimators = System.Array.Empty<Animator>();
    private static Outlinable[] _vOutlines = System.Array.Empty<Outlinable>();
    private static UnityEngine.Light[] _vLights = System.Array.Empty<UnityEngine.Light>();
    private static ParticleSystem[] _vParticles = System.Array.Empty<ParticleSystem>();
    private static Renderer[] _vRenderers = System.Array.Empty<Renderer>();
    private static int _vFoundAnimators, _vFoundOutlines, _vFoundLights, _vFoundParticles, _vFoundRenderers;

    /// <summary>The materials whose whole property table is read back, and the table itself. The
    /// table is resolved ONCE when the window arms — <c>Shader.GetPropertyName</c> allocates a
    /// string, and doing that per frame would be an instrument that costs more than the thing it
    /// measures.</summary>
    private static readonly Material[] VMats = new Material[VerdictMatCap];
    private static int _vMatCount;
    private static readonly int[] VPropId = new int[VerdictPropCap];
    private static readonly string[] VPropName = new string[VerdictPropCap];
    private static readonly bool[] VPropIsColor = new bool[VerdictPropCap];
    private static int _vPropCount, _vPropDeclared;
    private static string _vShader = "<none>";

    private static readonly float[] VPrev = new float[VerdictMatCap * VerdictPropCap];
    private static readonly bool[] VSeen = new bool[VerdictMatCap * VerdictPropCap];
    private static readonly int[] VMoves = new int[VerdictMatCap * VerdictPropCap];
    private static readonly float[] VLo = new float[VerdictMatCap * VerdictPropCap];
    private static readonly float[] VHi = new float[VerdictMatCap * VerdictPropCap];

    private static readonly float[] VLightPrev = new float[VerdictObjCap];
    private static readonly int[] VLightMoves = new int[VerdictObjCap];
    private static readonly float[] VLightLo = new float[VerdictObjCap];
    private static readonly float[] VLightHi = new float[VerdictObjCap];
    private static readonly float[] VAnimPrev = new float[VerdictObjCap];
    private static readonly int[] VAnimMoves = new int[VerdictObjCap];

    // Aggregates over the window: the WORST (highest) reading of each, because the question is
    // "did anything paint at all", and a maximum answers that where a mean would hide one frame
    // of it. A field that stays 0 across the whole window is the falsifier for its own strand.
    private static int _vAnimOnMax, _vOutOnMax, _vLightOnMax, _vEmitterOnMax;
    private static int _vPartPlayingMax, _vPartAliveMax;
    private static int _vRendDrawMax, _vRendVisMax;
    private static float _vLightIntensityMax;

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
        b.Label = label;
        b.NextScanFrame = Time.frameCount + RescanFrames;
        Live.Add(b);
        Apply(b);
        Announce(b, label);
        ArmVerdict(b, label);
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
                // objects that held the replaced values are gone with it. The verdict still
                // reports, because "the prop was destroyed mid-hold" is an answer and a silently
                // dropped window looks exactly like a window that never armed.
                CloseVerdict(b, "the prop was destroyed mid-hold");
                Retire(i);
                continue;
            }
            if (Time.frameCount >= b.NextScanFrame)
            {
                b.NextScanFrame = Time.frameCount + RescanFrames;
                b.Rescans++;
                Apply(b);
            }

            // THE POST-HUSH WINDOW. At most ONE belt is ever armed, so this is a reference compare
            // for every other held prop and for every frame once the window has closed.
            if (ReferenceEquals(b, _vBelt))
                SampleVerdict(b);
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
            // The verdict is emitted BEFORE the restore: it is a report about what the prop looked
            // like WHILE it was hushed, and one frame of it is still true here. Then the restore
            // runs, and the restore line reports what it found — including whether anything had
            // been written by somebody else during the hold.
            CloseVerdict(Live[i], "the prop was put down");
            Restore(Live[i]);
            AnnounceRestore(Live[i]);
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
            CloseVerdict(Live[i], "every hold was released at once (scenario change, dial off or uninstall)");
            Restore(Live[i]);
            AnnounceRestore(Live[i]);
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
        _verdictsLeft = VerdictBudget;
        VerdictKindsDone.Clear();
        _restoreLogsLeft = RestoreLogBudget;
        RestoreKindsDone.Clear();
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

        // ---- STRAND 3: EVERY Behaviour-DERIVED EMITTER ----
        //
        // Light, Projector and LensFlare are the three components in Unity that PAINT without
        // being a Renderer. None of them is reachable from a GetComponentsInChildren<MonoBehaviour>
        // sweep — they derive from Behaviour — which is why the grab-edge census could report the
        // MonoBehaviour histogram in full and still never name the thing holding the lamp. Taken
        // as one class rather than three named types on purpose: the type boundary IS the defect.
        // The FOUND counts are the population of THIS walk (assigned, not accumulated — a rescan
        // re-counts the same objects); the ON counts accumulate, because an object that arrives
        // enabled on a later walk was genuinely live before this class wrote it.
        b.LightsFound = TakeEmitters<UnityEngine.Light>(b, go, ref b.LightsOn0);
        b.ProjectorsFound = TakeEmitters<Projector>(b, go, ref b.ProjectorsOn0);
        b.FlaresFound = TakeEmitters<LensFlare>(b, go, ref b.FlaresOn0);

        // ---- STRAND 4: PARTICLE SYSTEMS THAT WERE ACTUALLY PLAYING ----
        //
        // Stop-and-CLEAR, never Pause: a paused system leaves its live particles hanging in the
        // air, which is a frozen shimmer rather than an absent one. Only the ones found playing go
        // in the ledger, so the restore can never start an effect the game had already finished.
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        b.ParticlesFound = ParticleScratch.Count;
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p == null || Contains(b.Particles, p) || !p.isPlaying)
                continue;
            b.ParticlesPlaying0++;
            b.Particles.Add(p);
            p.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ParticleScratch.Clear();
    }

    /// <summary>Switch off every <typeparamref name="T"/> under <paramref name="go"/> that is not
    /// already in the ledger, remembering the <c>enabled</c> value each one had. Returns how many
    /// were FOUND and adds how many were LIVE to <paramref name="liveBefore"/> — the pre-state
    /// counts that let a round with two new suppressions still say which one had anything to
    /// do.</summary>
    private static int TakeEmitters<T>(Belt b, GameObject go, ref int liveBefore) where T : Behaviour
    {
        // The ARRAY overload rather than the shared-list one: a List<T> scratch cannot be shared
        // across three different T, and this runs at the grab and then once every RescanFrames —
        // never on the per-frame path the class doc's cost note is about.
        T[] found = go.GetComponentsInChildren<T>(includeInactive: true);
        for (int i = 0; i < found.Length; i++)
        {
            T e = found[i];
            if (e == null || Contains(b.Emitters, e))
                continue;
            b.Emitters.Add(e);
            b.EmitterEnabled0.Add(e.enabled);
            if (!e.enabled)
                continue;
            liveBefore++;
            e.enabled = false;
        }
        return found.Length;
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

        // THE EMITTERS, AND THE FALSIFIER FOR THE RESTORE IS COUNTED HERE RATHER THAN ASSERTED.
        // This project has a recorded incident where a hide saved a FOREIGN mid-animation value
        // and then restored garbage over another system's restore. The guard against it is not a
        // comment: before writing the remembered value back, compare what is there NOW against
        // what this class LEFT there (false). A mismatch means somebody else wrote the object
        // during the hold and the value about to be handed back is being handed back over their
        // write. The count goes in the restore line; it is expected to be zero, and a non-zero
        // reading is the one that says this restore is not exact.
        for (int i = 0; i < b.Emitters.Count && i < b.EmitterEnabled0.Count; i++)
        {
            Behaviour e = b.Emitters[i];
            if (e == null)
            {
                b.RestoreDead++;
                continue;
            }
            if (e.enabled)
                b.RestoreForeignEmitters++;
            e.enabled = b.EmitterEnabled0[i];
        }

        // ONLY THE SYSTEMS THIS CLASS STOPPED, AND ONLY IF THEY ARE STILL THIS PROP'S. These
        // objects are POOLED — SpawnPFXOnEnable hands them to ObjectPool.Recycle — so a reference
        // taken at the grab can by the landing name an object that has been recycled into some
        // other effect entirely. Restarting THAT would be this lane spawning a foreign effect on
        // release. The parent test is what makes the restore safe, and the visual is checked first
        // because a destroyed prop has no transform to compare against.
        Transform? visual = b.Visual != null ? b.Visual.transform : null;
        for (int i = 0; i < b.Particles.Count; i++)
        {
            ParticleSystem p = b.Particles[i];
            if (p == null)
            {
                b.RestoreDead++;
                continue;
            }
            if (visual == null || !p.transform.IsChildOf(visual))
            {
                b.RestoreForeignParticles++;
                continue;
            }
            p.Play(withChildren: true);
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
        b.Emitters.Clear();
        b.EmitterEnabled0.Clear();
        b.Particles.Clear();
        b.AnimatorsLeftForRules = 0;
        b.AnimatorsAlreadyOff = 0;
        b.OutlinesAlreadyOff = 0;
        b.SkinsAlready = 0;
        b.LightsFound = b.LightsOn0 = 0;
        b.ProjectorsFound = b.ProjectorsOn0 = 0;
        b.FlaresFound = b.FlaresOn0 = 0;
        b.ParticlesFound = b.ParticlesPlaying0 = 0;
        b.RestoreForeignEmitters = b.RestoreForeignParticles = b.RestoreDead = 0;
        b.Label = string.Empty;
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

        // ROUND FIVE, AND THE PRE-COUNTS ARE WHAT MAKE TWO NEW SUPPRESSIONS ATTRIBUTABLE.
        sb.Append("SUPPRESSED — Behaviour-DERIVED EMITTERS (new 2026-09-05, and the reason is a "
                  + "TYPE boundary rather than a new idea): ").Append(b.LightsFound)
          .Append(" Light(s) of which ").Append(b.LightsOn0).Append(" were ENABLED, ")
          .Append(b.ProjectorsFound).Append(" Projector(s) of which ").Append(b.ProjectorsOn0)
          .Append(" were enabled, ").Append(b.FlaresFound).Append(" LensFlare(s) of which ")
          .Append(b.FlaresOn0).Append(" were enabled — all switched off for the hold, each "
                  + "restored to its own remembered value. Light derives from Behaviour and NOT "
                  + "from MonoBehaviour, so the histogram at the end of this very line cannot name "
                  + "one and the two typed sweeps above cannot reach one; ModBuild 151 already lost "
                  + "a round to that exact hole ('a live point light inside the creature, intensity "
                  + "20.00 at range 1.0 m'). A lamp that is unremarkable on a hex a metre and a half "
                  + "away is a lamp twenty centimetres from the eye once the prop is in a palm. "
                  + "RFX4_LightCurves — which the histogram below DOES name — writes light.intensity "
                  + "from an AnimationCurve every frame off Time.time and loops for ever, and this "
                  + "class switches the LIGHT off rather than that writer, because disabling the "
                  + "writer would pin the intensity at whatever it last wrote. ")
          .Append("SUPPRESSED — PARTICLE SYSTEMS: ").Append(b.ParticlesFound)
          .Append(" under the visual of which ").Append(b.ParticlesPlaying0)
          .Append(" were PLAYING and were stopped-and-CLEARED for the hold (a pause would leave "
                  + "the live particles hanging in the air, which is a frozen shimmer rather than "
                  + "an absent one); the ones already stopped are untouched and are never "
                  + "restarted. THE TWO PRE-COUNTS ABOVE ARE THE ATTRIBUTION: a class whose "
                  + "pre-count is 0 made no writes and cannot be why anything changed either way. ");

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

        sb.Append(". WHY THIS ENUMERATION EXISTS, AND THE PARAGRAPH THAT USED TO STAND HERE IS NOW ")
          .Append("WRONG: it said the prop's animator was IDLE IN BOTH WINDOWS, which was a "
                  + "reading of ModBuild 435/436. The 447 log says otherwise — 'Trap_BearTrap_PR' "
                  + "clipRate home=0.202/s, anyLayerRate home=0.201/s, advancing home=359/360, "
                  + "hand=0/655, layerCount hand=0 — i.e. the prop runs a CONTINUOUS ~5 s loop on "
                  + "its hex and this class stops it dead in the hand (a disabled Animator reports "
                  + "layerCount 0, which is why the hand column reads n/a and not because anything "
                  + "went unmeasured). The animator strand WORKS and it is not the shimmer, and the "
                  + "material read-back that agreed with it tracks only 20 of Amp_Char_Shader's 57 "
                  + "declared properties, which is the same truncation that cost ModBuild 151 a "
                  + "build on this same shader. Read the '] [Props] HELD-PROP PAINT AFTER HUSH' "
                  + "line for this prop: it is taken AFTER everything above was written, it reads "
                  + "the whole property table, and it counts the lights and particles this line "
                  + "only enumerates. ")
          .Append(_logsLeft).Append(" more prop hush line(s) this session, at most one per prop kind.");

        // HW-VERIFY: this line IS the round's deliverable — it is the first census anyone has taken
        // of what a held prop actually carries, and the standing question ("what is the flash, if
        // it is not the animator?") is answered by reading it. It must stay at a tier the DEFAULT
        // log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Has this prop KIND already printed its census? The roster is per SCENARIO and
    /// <see cref="Reset"/> clears it, because a new scenario is a new hardware question.</summary>
    private static bool KindSpent(string label) => Spent(KindsDone, label);

    // ---- the POST-HUSH verdict --------------------------------------------------------------------

    /// <summary>
    /// Arm the window that watches a prop AFTER it has been hushed. Pure instrument: it reads and
    /// it prints, and nothing outside this region reads a field it writes.
    ///
    /// <para><b>WHY IT EXISTS, IN ONE SENTENCE.</b> The grab-edge census said the hush fired and
    /// the user said the shimmer was still there, so the only reading worth having is one taken
    /// with the suppression already in place. A line that says "I hushed it" is worthless against
    /// that report; this one names, per class, what is still enabled, still playing and still
    /// moving on a prop this class has just silenced.</para>
    /// </summary>
    private static void ArmVerdict(Belt b, string label)
    {
        if (_vBelt != null || _verdictsLeft <= 0 || Spent(VerdictKindsDone, label))
            return;
        GameObject? go = b.Visual;
        if (go == null)
            return;

        _verdictsLeft--;
        if (VerdictKindsDone.Count < KindCap)
            VerdictKindsDone.Add(label);

        _vBelt = b;
        _vLabel = label;
        _vFrames = 0;
        _vEndFrame = Time.frameCount + VerdictFrames;

        _vAnimators = Take(go.GetComponentsInChildren<Animator>(true), out _vFoundAnimators);
        _vOutlines = Take(go.GetComponentsInChildren<Outlinable>(true), out _vFoundOutlines);
        _vLights = Take(go.GetComponentsInChildren<UnityEngine.Light>(true), out _vFoundLights);
        _vParticles = Take(go.GetComponentsInChildren<ParticleSystem>(true), out _vFoundParticles);
        _vRenderers = Take(go.GetComponentsInChildren<Renderer>(true), out _vFoundRenderers);

        _vAnimOnMax = _vOutOnMax = _vLightOnMax = _vEmitterOnMax = 0;
        _vPartPlayingMax = _vPartAliveMax = _vRendDrawMax = _vRendVisMax = 0;
        _vLightIntensityMax = 0f;
        for (int i = 0; i < VerdictObjCap; i++)
        {
            VLightMoves[i] = VAnimMoves[i] = 0;
            VLightLo[i] = float.MaxValue;
            VLightHi[i] = float.MinValue;
            VLightPrev[i] = float.NaN;
            VAnimPrev[i] = float.NaN;
        }
        for (int i = 0; i < VPrev.Length; i++)
        {
            VSeen[i] = false;
            VMoves[i] = 0;
            VLo[i] = float.MaxValue;
            VHi[i] = float.MinValue;
        }

        ResolveVerdictMaterials();
    }

    /// <summary>Cap an array at <see cref="VerdictObjCap"/>, reporting the population it came
    /// from. A truncated list is not absence and every one of these is printed as
    /// sampled-of-found.</summary>
    private static T[] Take<T>(T[] all, out int found)
    {
        found = all.Length;
        if (all.Length <= VerdictObjCap)
            return all;
        var cut = new T[VerdictObjCap];
        System.Array.Copy(all, cut, VerdictObjCap);
        return cut;
    }

    /// <summary>
    /// Resolve the materials and the property table read back each frame.
    ///
    /// <para><b><c>sharedMaterials</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a clone the first time it is touched, which is a permanent change to the scene
    /// made by an instrument. It is also unnecessary: once anything — an animation curve on
    /// <c>material._X</c>, an SMB writing through <c>.material</c> — has instanced a renderer's
    /// material, Unity stores that clone back into the renderer, and <c>sharedMaterials</c>
    /// afterwards returns the CLONE. So this reads the instanced values without ever creating
    /// one. (<c>PropAnimWatch</c>'s header lists "a write through .material is invisible to
    /// sharedMaterial" as a blind spot; it is not one, and the real blind spot was the twenty-slot
    /// cap next to it.)</para>
    ///
    /// <para>Both material SLOTS of every renderer are eligible, not just <c>sharedMaterial</c>
    /// (which is only the first): a chest whose glint lives on its second submesh would be
    /// invisible to a first-slot-only read.</para>
    /// </summary>
    private static void ResolveVerdictMaterials()
    {
        _vMatCount = 0;
        _vPropCount = 0;
        _vPropDeclared = 0;
        _vShader = "<none>";
        for (int i = 0; i < VerdictMatCap; i++)
            VMats[i] = null!;

        for (int r = 0; r < _vRenderers.Length && _vMatCount < VerdictMatCap; r++)
        {
            Renderer rend = _vRenderers[r];
            if (rend == null)
                continue;
            Material[] mats = rend.sharedMaterials;
            for (int m = 0; m < mats.Length && _vMatCount < VerdictMatCap; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;
                bool dup = false;
                for (int k = 0; k < _vMatCount; k++)
                    dup |= ReferenceEquals(VMats[k], mat);
                if (dup)
                    continue;
                VMats[_vMatCount++] = mat;
            }
        }

        if (_vMatCount == 0)
            return;

        Shader? shader = VMats[0] != null ? VMats[0].shader : null;
        if (shader == null)
            return;
        _vShader = shader.name;
        _vPropDeclared = shader.GetPropertyCount();
        for (int i = 0; i < _vPropDeclared && _vPropCount < VerdictPropCap; i++)
        {
            UnityEngine.Rendering.ShaderPropertyType t = shader.GetPropertyType(i);
            bool isColor = t == UnityEngine.Rendering.ShaderPropertyType.Color;
            if (!isColor && t != UnityEngine.Rendering.ShaderPropertyType.Float
                         && t != UnityEngine.Rendering.ShaderPropertyType.Range)
                continue;
            VPropId[_vPropCount] = shader.GetPropertyNameId(i);
            VPropName[_vPropCount] = shader.GetPropertyName(i);
            VPropIsColor[_vPropCount] = isColor;
            _vPropCount++;
        }
    }

    /// <summary>One frame of the post-hush window. No allocation: every buffer is static and the
    /// component sets were resolved when the window armed.</summary>
    private static void SampleVerdict(Belt b)
    {
        if (b.Visual == null)
            return;
        _vFrames++;

        int animOn = 0;
        for (int i = 0; i < _vAnimators.Length; i++)
        {
            Animator a = _vAnimators[i];
            if (a == null || !a.enabled)
                continue;
            animOn++;
            // A DISABLED ANIMATOR REPORTS layerCount 0 — that is why PropAnimWatch's hand column
            // reads n/a on this build and it is not a missing measurement. Only an animator that
            // is still ON can advance, and the only ones this class leaves on are the rules ones.
            if (a.layerCount <= 0)
                continue;
            float t = a.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (!float.IsNaN(VAnimPrev[i]) && Mathf.Abs(t - VAnimPrev[i]) > MoveEpsilon)
                VAnimMoves[i]++;
            VAnimPrev[i] = t;
        }
        if (animOn > _vAnimOnMax)
            _vAnimOnMax = animOn;

        int outOn = 0;
        for (int i = 0; i < _vOutlines.Length; i++)
        {
            Outlinable o = _vOutlines[i];
            if (o != null && o.enabled)
                outOn++;
        }
        if (outOn > _vOutOnMax)
            _vOutOnMax = outOn;

        int lightOn = 0;
        for (int i = 0; i < _vLights.Length; i++)
        {
            UnityEngine.Light l = _vLights[i];
            if (l == null)
                continue;
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                lightOn++;
                if (l.intensity > _vLightIntensityMax)
                    _vLightIntensityMax = l.intensity;
            }
            // The INTENSITY is sampled whether or not the lamp is switched on, and that is the
            // point: a curve still writing a rising number into a light this class has disabled is
            // the reading that says the WRITER survived the suppression and would re-light the
            // moment anything re-enabled the component.
            float v = l.intensity;
            if (!float.IsNaN(VLightPrev[i]) && Mathf.Abs(v - VLightPrev[i]) > MoveEpsilon)
                VLightMoves[i]++;
            VLightPrev[i] = v;
            if (v < VLightLo[i]) VLightLo[i] = v;
            if (v > VLightHi[i]) VLightHi[i] = v;
        }
        if (lightOn > _vLightOnMax)
            _vLightOnMax = lightOn;

        int emitterOn = 0;
        for (int i = 0; i < b.Emitters.Count; i++)
        {
            Behaviour e = b.Emitters[i];
            if (e != null && e.enabled)
                emitterOn++;
        }
        if (emitterOn > _vEmitterOnMax)
            _vEmitterOnMax = emitterOn;

        int playing = 0, alive = 0;
        for (int i = 0; i < _vParticles.Length; i++)
        {
            ParticleSystem p = _vParticles[i];
            if (p == null)
                continue;
            if (p.isPlaying)
                playing++;
            alive += p.particleCount;
        }
        if (playing > _vPartPlayingMax) _vPartPlayingMax = playing;
        if (alive > _vPartAliveMax) _vPartAliveMax = alive;

        int drawing = 0, visible = 0;
        for (int i = 0; i < _vRenderers.Length; i++)
        {
            Renderer r = _vRenderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            drawing++;
            if (r.isVisible)
                visible++;
        }
        if (drawing > _vRendDrawMax) _vRendDrawMax = drawing;
        if (visible > _vRendVisMax) _vRendVisMax = visible;

        for (int m = 0; m < _vMatCount; m++)
        {
            Material mat = VMats[m];
            if (mat == null)
                continue;
            for (int k = 0; k < _vPropCount; k++)
            {
                int slot = (m * VerdictPropCap) + k;
                int id = VPropId[k];
                if (!mat.HasProperty(id))
                    continue;
                float v;
                if (VPropIsColor[k])
                {
                    Color c = mat.GetColor(id);
                    v = (c.r + c.g + c.b) * (1f / 3f);
                }
                else
                {
                    v = mat.GetFloat(id);
                }
                if (VSeen[slot] && Mathf.Abs(v - VPrev[slot]) > MoveEpsilon)
                    VMoves[slot]++;
                VPrev[slot] = v;
                VSeen[slot] = true;
                if (v < VLo[slot]) VLo[slot] = v;
                if (v > VHi[slot]) VHi[slot] = v;
            }
        }

        if (Time.frameCount >= _vEndFrame)
            CloseVerdict(b, "the window ran to its full length with the prop still in the hand");
    }

    /// <summary>Close and emit the armed window, if <paramref name="b"/> is the belt that armed
    /// it. Idempotent and safe on any belt.</summary>
    private static void CloseVerdict(Belt b, string why)
    {
        if (!ReferenceEquals(b, _vBelt))
            return;
        _vBelt = null;
        if (_vFrames > 0)
            EmitPostHushVerdict(b, why);
    }

    /// <summary>
    /// THE LINE THIS ROUND EXISTS FOR. What a held prop still carries and still does AFTER the
    /// hush has written everything it writes.
    /// </summary>
    private static void EmitPostHushVerdict(Belt b, string why)
    {
        var sb = new System.Text.StringBuilder(3072);
        sb.Append("[Props] HELD-PROP PAINT AFTER HUSH for ").Append(_vLabel)
          .Append(" — ").Append(_vFrames).Append(" frame(s) sampled with the suppression ALREADY IN "
                  + "PLACE, closed because ").Append(why)
          .Append(". THIS IS THE FALSIFIER FOR THE WHOLE HUSH: ModBuild 445 shipped it, the "
                  + "grab-edge census printed green on both machines, and the user reported the "
                  + "white shimmer STILL ON the asset in his hand. A census of what was switched "
                  + "off cannot answer that; only a reading taken afterwards can. ");

        sb.Append("ANIMATORS: ").Append(_vAnimators.Length).Append(" sampled of ")
          .Append(_vFoundAnimators).Append(" found, at most ").Append(_vAnimOnMax)
          .Append(" enabled on any frame (the ").Append(b.AnimatorsLeftForRules)
          .Append(" left running for DelayedDeactivatePropAnimSMB are inside that number and are "
                  + "supposed to be), advancing on ").Append(MaxOf(VAnimMoves, _vAnimators.Length))
          .Append(" frame(s) at worst. ");

        sb.Append("OUTLINES: ").Append(_vOutlines.Length).Append(" sampled of ")
          .Append(_vFoundOutlines).Append(" found, at most ").Append(_vOutOnMax)
          .Append(" enabled on any frame. Outlinable.OnDisable removes the component from the "
                  + "static list the outline pass walks and UpdateVisibility refuses to re-add a "
                  + "disabled one (Outlinable.cs:236-245, 264-270), so a zero here means the "
                  + "outline genuinely did not draw and the shimmer is not the outline. ");

        sb.Append("LIGHTS — AND THIS IS THE CLASS NO ROUND HAS EVER MEASURED: ")
          .Append(_vLights.Length).Append(" sampled of ").Append(_vFoundLights)
          .Append(" found (").Append(b.LightsOn0)
          .Append(" of them were ENABLED before this build wrote anything — that pre-count is what "
                  + "says whether this strand had anything to do at all), at most ")
          .Append(_vLightOnMax).Append(" lit on any frame, brightest intensity seen while lit ")
          .Append(_vLightIntensityMax.ToString("0.###")).Append(". ")
          .Append("Light derives from Behaviour, NOT from MonoBehaviour, so every "
                  + "GetComponentsInChildren<MonoBehaviour>() census this project has ever run "
                  + "walked past one structurally — that is ModBuild 151's 'LivingSpirit_Light, "
                  + "Point, intensity 20.00, range 1.0 m' word for word. RFX4_LightCurves.Update "
                  + "writes light.intensity from an AnimationCurve every frame off Time.time and "
                  + "re-seeds itself for ever when IsLoop is set, which is a shimmer with no "
                  + "animator, no material property and no renderer in it. ");
        AppendMovers(sb, "INTENSITY STILL MOVING ON A HUSHED LIGHT (the writer survived the "
                          + "suppression even though the lamp is off)", VLightMoves, VLightLo, VLightHi,
                     _vLights.Length, _vLights.Length, null);

        sb.Append(" OTHER Behaviour-DERIVED EMITTERS: ").Append(b.ProjectorsFound)
          .Append(" Projector(s) (").Append(b.ProjectorsOn0).Append(" were on), ")
          .Append(b.FlaresFound).Append(" LensFlare(s) (").Append(b.FlaresOn0)
          .Append(" were on); ").Append(_vEmitterOnMax)
          .Append(" of the whole emitter ledger was enabled on the worst frame, and that number "
                  + "must be 0 — a non-zero reading means something re-enabled an emitter under a "
                  + "held prop and the 45-frame rescan does not re-suppress an object already in "
                  + "the ledger. ");

        sb.Append("PARTICLES: ").Append(_vParticles.Length).Append(" sampled of ")
          .Append(_vFoundParticles).Append(" found (").Append(b.ParticlesPlaying0)
          .Append(" were PLAYING before this build stopped them), at most ")
          .Append(_vPartPlayingMax).Append(" playing on any frame and at most ")
          .Append(_vPartAliveMax).Append(" live particle(s) on screen at once. ");

        sb.Append("RENDERERS: ").Append(_vRenderers.Length).Append(" sampled of ")
          .Append(_vFoundRenderers).Append(" found, at most ").Append(_vRendDrawMax)
          .Append(" drawing and ").Append(_vRendVisMax).Append(" reported isVisible. ");

        sb.Append("MATERIAL PROPERTIES, READ WHOLE THIS TIME: ").Append(_vMatCount)
          .Append(" material(s) on shader '").Append(_vShader).Append("', ").Append(_vPropDeclared)
          .Append(" propert(y/ies) declared and ").Append(_vPropCount)
          .Append(" float/range/colour tracked");
        if (_vPropCount >= VerdictPropCap && _vPropDeclared > _vPropCount)
            sb.Append(" — THE TABLE IS STILL TRUNCATED at this class's own cap of ")
              .Append(VerdictPropCap).Append(", raise it before believing a zero below");
        sb.Append(". PropAnimWatch tracks TWENTY of Amp_Char_Shader's fifty-seven, which is why "
                  + "four rounds read 'not one tracked slot moved' and meant nothing by it — "
                  + "ModBuild 151 lost a build to exactly this cap, on exactly this shader. ");
        AppendMovers(sb, "PROPERTIES THAT MOVED AFTER THE HUSH", VMoves, VLo, VHi,
                     _vMatCount * VerdictPropCap, _vMatCount * _vPropCount, VPropName);

        sb.Append(" HOW TO READ THIS, AND WHAT WOULD MEAN THE FIX IS INERT. The fix is WORKING if "
                  + "lights-lit-on-any-frame is 0 with a non-zero pre-count of enabled lights, "
                  + "particles-playing is 0 with a non-zero pre-count of playing systems, "
                  + "emitters-enabled is 0, outlines-enabled is 0, animators-enabled is no more "
                  + "than the rules exception, and no material property moved. The fix is INERT — "
                  + "and this is the reading that must not be mistaken for success — if EVERY "
                  + "pre-count is 0: nothing was on, nothing was written, and a shimmer the user "
                  + "still sees is then being painted by something that is NOT under this prop's "
                  + "subtree at all, which is the one place no instrument in this file can look. "
                  + "If instead a property moved or a light kept its intensity climbing, the name "
                  + "printed above IS the driver and no further search is needed. ")
          .Append(_verdictsLeft).Append(" more post-hush verdict(s) this session, at most one per prop kind.");

        // HW-VERIFY: this line is the round-five deliverable and the falsifier for the ModBuild 445
        // hush, which passed its own census while the defect stood. It reports what was observed
        // AFTER the suppression, per class, with the pre-state beside it so a fix that changed
        // nothing cannot read as a fix that worked. It must stay at a tier the DEFAULT log level
        // prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Name the entries that MOVED, with the range each one covered, capped and with the
    /// total stated. Used for both the light intensities and the material property table.</summary>
    private static void AppendMovers(System.Text.StringBuilder sb, string title, int[] moves,
        float[] lo, float[] hi, int scan, int total, string[]? names)
    {
        int moved = 0;
        for (int i = 0; i < scan && i < moves.Length; i++)
        {
            if (moves[i] > 0)
                moved++;
        }
        sb.Append(title).Append(": ").Append(moved);
        if (moved == 0)
        {
            sb.Append(" — NONE of the ").Append(total).Append(" slot(s) moved on any frame.");
            return;
        }
        sb.Append(" of ").Append(total).Append(" slot(s), naming up to ").Append(VerdictListCap)
          .Append(": ");
        int listed = 0;
        for (int i = 0; i < scan && i < moves.Length && listed < VerdictListCap; i++)
        {
            if (moves[i] <= 0)
                continue;
            if (listed > 0)
                sb.Append(", ");
            if (names != null)
                sb.Append(names[i % VerdictPropCap] ?? "<unnamed>").Append(" (mat")
                  .Append(i / VerdictPropCap).Append(')');
            else
                sb.Append('#').Append(i);
            sb.Append(" moved on ").Append(moves[i]).Append(" frame(s), ")
              .Append(lo[i].ToString("0.####")).Append("..").Append(hi[i].ToString("0.####"));
            listed++;
        }
        sb.Append(listed < moved ? ", and the rest are counted but not named." : ".");
    }

    private static int MaxOf(int[] values, int count)
    {
        int best = 0;
        for (int i = 0; i < count && i < values.Length; i++)
        {
            if (values[i] > best)
                best = values[i];
        }
        return best;
    }

    // ---- the restore, and its own falsifier -------------------------------------------------------

    /// <summary>
    /// Say what the restore handed back and — the part that matters — whether it handed anything
    /// back OVER somebody else's write. This project has a recorded incident in which a hide saved
    /// a foreign mid-animation value and restored garbage over another system's restore, so
    /// "the restore is exact" is not left as a claim in a comment: <see cref="Restore"/> compares
    /// what is there against what this class LEFT there before writing the remembered value, and
    /// the mismatch count is printed here.
    /// </summary>
    private static void AnnounceRestore(Belt b)
    {
        if (_restoreLogsLeft <= 0 || Spent(RestoreKindsDone, b.Label))
            return;
        _restoreLogsLeft--;
        if (RestoreKindsDone.Count < KindCap)
            RestoreKindsDone.Add(b.Label);

        var sb = new System.Text.StringBuilder(1024);
        sb.Append("[Props] HELD-PROP HUSH RESTORE for ").Append(b.Label).Append(" — handed back ")
          .Append(b.Animators.Count).Append(" animator(s), ").Append(b.Outlines.Count)
          .Append(" outline(s), ").Append(b.Emitters.Count)
          .Append(" emitter(s) (Light/Projector/LensFlare), ").Append(b.Skins.Count)
          .Append(" skinned renderer(s) and restarted ").Append(b.Particles.Count)
          .Append(" particle system(s) this class had stopped, each to the value IT had, object "
                  + "for object, after ").Append(b.Rescans).Append(" mid-hold rescan(s). ")
          .Append("THE FALSIFIER FOR THIS RESTORE: ").Append(b.RestoreForeignEmitters)
          .Append(" emitter(s) were found ENABLED at the landing although this class had switched "
                  + "them off — i.e. somebody else wrote them during the hold and the remembered "
                  + "value is being written over that; ").Append(b.RestoreForeignParticles)
          .Append(" particle system(s) were no longer children of this prop and were therefore NOT "
                  + "restarted (these objects are pooled through ObjectPool.Recycle, and "
                  + "restarting one that has been recycled into another effect would be this lane "
                  + "spawning a foreign effect on release); ").Append(b.RestoreDead)
          .Append(" object(s) had been destroyed under the ledger and were skipped rather than "
                  + "written. All three are expected to read 0; any of them above 0 is the reading "
                  + "that says the prop did NOT come back exactly as the game left it. ")
          .Append(_restoreLogsLeft).Append(" more restore line(s) this session, at most one per prop kind.");

        // HW-VERIFY: the standing requirement on this feature is that a prop put back down looks
        // exactly as it did before it was picked up. This is the only line that can say whether
        // that held, and it must stay at a tier the DEFAULT log level prints (Note/Alert/Error).
        // scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Has this prop KIND already spent its budget in <paramref name="roster"/>?</summary>
    private static bool Spent(List<string> roster, string label)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (string.Equals(roster[i], label, System.StringComparison.Ordinal))
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
