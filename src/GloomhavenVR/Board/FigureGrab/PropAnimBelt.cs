using System.Collections.Generic;
using EPOOutline;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A HELD PROP HOLDS STILL — the hush. For the length of a hold this class switches off the
/// prop's own attention animation and everything else on it that paints, remembers the exact
/// value it replaced, and hands every one of them back when the prop is put down.
///
/// <para><b>WHY IT EXISTS.</b> The game plays an attention flash on chests and traps so a player
/// notices them on the board. In a VR hand the prop is twenty centimetres from the eye instead of
/// a metre and a half away on a hex, and four rounds failed to make that animation play correctly
/// in a palm. The user's call, verbatim: <i>"ich will erstmal, um es einfach zu halten, diese
/// Animation gar nicht mehr in der Hand haben stattdessen."</i> That is a decision, not a bug
/// report — a chest the player is already holding needs no help being noticed.</para>
///
/// <para><b>WHAT IT SUPPRESSES, AND THE PRE-COUNT IS PART OF EACH ONE.</b> Every strand records
/// how many objects of its class existed and how many were LIVE before it wrote anything, because
/// that pair is what makes several suppressions in one build attributable — a class whose live
/// count is zero made no writes and cannot be why anything changed in either direction.</para>
/// <list type="bullet">
/// <item><c>Animator.enabled = false</c>, preceded by <c>WriteDefaultValues()</c> so the frame it
/// freezes on is the resting one. An animator whose controller carries
/// <c>DelayedDeactivatePropAnimSMB</c> is never touched at all: that behaviour sends a rules
/// message for a sprung trap and holds a global "deactivations in progress" flag, and this lane
/// does not write game state.</item>
/// <item><c>EPOOutline.Outlinable.enabled = false</c> — the component's own flag and NOT
/// <c>OutlineParameters.Enabled</c>, because the game writes that property in nine places and it
/// is only one of the three parameter blocks an Outlinable can draw from. <c>OnDisable</c> removes
/// the component from the static list the outline pass walks, so it cannot be re-added by a
/// visibility event either.</item>
/// <item>Every <c>Behaviour</c>-derived emitter — <c>Light</c>, <c>Projector</c>,
/// <c>LensFlare</c> — taken as a CLASS rather than by naming <c>Light</c> alone, because the type
/// boundary is the defect: <c>Light</c> derives from <c>Behaviour</c> and not from
/// <c>MonoBehaviour</c>, and ModBuild 151 lost a build to exactly that hole. The LIGHT is switched
/// off rather than the curve that writes its intensity — disabling the writer would pin the value
/// at whatever it last wrote, which is a frozen shimmer rather than an absent one.</item>
/// <item>The prop's <c>ObjectOcclusionVolume</c>, through the same emitter ledger.
/// <c>OnDisable</c> IS <c>TilesOcclusionGenerator.RemoveObjectRenderer</c> and <c>OnEnable</c> IS
/// <c>AddObjectRenderer</c>, so the registration is taken through the game's own lifecycle and no
/// list is edited by hand.</item>
/// <item><c>SkinnedMeshRenderer.updateWhenOffscreen = true</c> is SET, not suppressed. It is not
/// an animation term: it keeps a held prop DRAWN when its stale root-bone bounds leave the
/// frustum, and a frozen animator makes those bounds staler still.</item>
/// </list>
///
/// <para><b>THE RESTORE IS THE POINT, AND IT IS GUARDED.</b> Every write is ledgered per object
/// and handed back to its own remembered value. <see cref="Restore"/> compares what is there NOW
/// against what this class LEFT there before writing the remembered value, and
/// <see cref="AnnounceRestore"/> prints the mismatch counts, all expected to read 0 — this project
/// has a recorded incident in which a hide saved a foreign mid-animation value and restored
/// garbage over another system's restore. A held prop is never scenery: nothing here touches
/// <c>Renderer.enabled</c>, <c>GameObject.activeSelf</c> or any layer.</para>
///
/// <para><b>MULTIPLAYER, BY CONSTRUCTION AND NOT BY A SECOND CODE PATH.</b> <c>NetProps</c> calls
/// <see cref="Engage"/> and <see cref="Release"/> for a REMOTE hold too, so a peer's mirrored copy
/// goes through the same <see cref="Apply"/>, the same ledger and the same restore. Every field
/// written here is a per-client rendering switch and none of them changes the prop's transform,
/// the rules state, or anything a peer could observe — which is why no wire field is needed and
/// why there is no per-sub-feature sync setting.</para>
///
/// <para><b>THE INSTRUMENT HALF IS TWO LINES.</b> <c>] [Props] HELD-PROP ANIMATION HUSH</c> at the
/// grab edge carries the pre-counts; <c>] [Props] HELD-PROP HOME TWIN</c> compares the held prop's
/// whole property table against another instance of the same kind still on its hex, on the same
/// tick. That comparison is the only reading in this file that asks whether the held prop's value
/// is the RIGHT one rather than whether it MOVED — nine rounds of "did anything move" all returned
/// zero, and a value latched wrong at the instant of the grab is exactly what that prints.</para>
///
/// <para><b>THE DEFECT IS NOT SOLVED AND THE HISTORY IS NOT IN THIS FILE.</b> Nine rounds are
/// recorded in <c>.planning/held-prop-flash-experiments.md</c>, including every strand and probe
/// deleted from this class and the reading that retired it. Read it before adding a strand here:
/// re-proposing a falsified one is the failure mode that document exists to prevent.</para>
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

    /// <summary>Frames the window samples — four seconds at 90 Hz. The trap's own clip loops at
    /// ~0.20 normalized/s on its hex (ModBuild 447), i.e. a five-second period, so a window much
    /// shorter than this could miss a whole cycle and report an honest zero that reads exactly like
    /// a stopped animation. A hold that ends sooner emits what it has, with the frame count.</summary>
    private const int VerdictFrames = 360;

    /// <summary>How many post-hush verdicts a session prints IN TOTAL, at most one per prop kind.</summary>
    private const int VerdictBudget = 3;

    /// <summary>Materials whose FULL property table is read back per frame. Sixteen, not four: the
    /// old line printed "N material(s) on shader 'X'" with no population beside N, so a prop
    /// carrying a fifth material read as a prop carrying four.</summary>
    private const int VerdictMatCap = 16;

    /// <summary>Properties tracked per material. Ninety-six, not twenty and not sixty-four, and the
    /// number is the finding: <c>PropAnimWatch</c> tracks 20 of the 57 <c>Amp_Char_Shader</c>
    /// declares, and ModBuild 151 already lost a build to a 24-property cap on a shader with 24
    /// interesting properties. The declared count is printed beside the tracked count per material
    /// either way, so a truncation is visible rather than silent.</summary>
    private const int VerdictPropCap = 96;

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
    private static readonly List<Renderer> RendScratch = new(16);

    /// <summary>The animators strand 6 is about to rewind and then stop. Held between the two
    /// halves of <see cref="Apply"/>'s animator pass so the property read-back either side of the
    /// rewind runs ONCE for the whole set rather than once per animator.</summary>
    private static readonly List<Animator> RewindScratch = new(4);

    /// <summary>Renderers snapshotted either side of the rewind, and their own scratch list so
    /// nothing shares a buffer with the sweep that called it.</summary>
    private const int RewindStateCap = 32;
    private static readonly Renderer[] RwRends = new Renderer[RewindStateCap];
    private static readonly bool[] RwRendOn = new bool[RewindStateCap];
    private static readonly bool[] RwActive = new bool[RewindStateCap];
    private static int _rwStateCount;
    private static readonly List<Renderer> RwRendScratch = new(16);


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

        /// <summary>STRAND 5 — the SCREEN-SPACE OCCLUSION REGISTRATION, and it is the first thing
        /// this class has ever switched off that does not paint from inside the prop at all.
        /// <c>ObjectOcclusionVolume.OnEnable</c> hands the prop's own <c>MeshRenderer</c> to
        /// <c>TilesOcclusionGenerator.s_Instance.AddObjectRenderer</c> — a list on a CAMERA,
        /// outside every subtree any instrument in this file has ever walked. Found and live
        /// counts, same attribution rule as the other classes.</summary>
        internal int OcclusionFound, OcclusionOn0;

        /// <summary>STRAND 6 — THE REWIND, and it is the first strand this class has ever aimed at
        /// its OWN side effect rather than at the game's. Switching an <c>Animator</c> off leaves
        /// every channel that clip drives LATCHED at whatever value it happened to hold on the
        /// frame of the grab; on a prop whose idle is a ~5 s attention flash that is a coin toss
        /// between "resting" and "at the peak of the flash", and the peak is what the user calls
        /// <i>weiß</i>. <see cref="AnimatorsRewound"/> is how many animators were taken back to
        /// their bound default values before being stopped; <see cref="RewindSkipped"/> is how many
        /// could not be (inactive object, or no controller to have defaults from).</summary>
        internal int AnimatorsRewound, RewindSkipped;

        /// <summary>THE PRE-COUNT FOR STRAND 6, AND IT IS WHAT MAKES IT ATTRIBUTABLE. How many
        /// (material, property) slots under the prop were READ across the rewind and how many
        /// actually CHANGED value because of it. Changed &gt; 0 says the freeze really was latching
        /// a non-default picture and this strand had work to do; changed == 0 says the animator's
        /// channels were already at rest at the grab, the strand wrote nothing visible, and it
        /// cannot be why anything got better OR worse.</summary>

        /// <summary>Renderer.enabled / GameObject.activeSelf flags read and CHANGED across the
        /// rewind. An AnimationClip can drive m_IsActive and m_Enabled, so a flash authored as
        /// "switch the glow mesh on" carries no material property at all and the (material,
        /// property) count beside this one cannot see it.</summary>
        internal int RewindStateRead, RewindStateChanged;

        /// <summary>The first frozen animator's normalizedTime on the frame this class froze
        /// it. The user's own trigger is a phase — "kurz nachdem der weisse flash auf allen
        /// Fallen kam" — so the phase at the grab is the axis his sentence is about, and it has
        /// never been recorded.</summary>
        internal float GrabPhase;
        internal int GrabPhaseCount;

        /// <summary>The properties the rewind moved, named with their before→after VALUES. Six
        /// rounds of this instrument reported MOVEMENT and never once a value, so a term latched
        /// at a wrong constant read exactly like a term that was correct. This is the value
        /// print.</summary>

        /// <summary>How many emitters, particle systems and animators were found in a state this
        /// class did NOT leave them in when <see cref="Restore"/> ran — i.e. somebody else wrote
        /// them during the hold. Zero is the expected reading and a non-zero one is the falsifier
        /// for the restore: it means the value handed back is being handed back over a foreign
        /// write.</summary>
        internal int RestoreForeignEmitters, RestoreForeignAnimators, RestoreDead;

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


    /// <summary>The materials whose whole property table is read back, and the table itself —
    /// float, range, int, colour, VECTOR and TEXTURE, and each material read through the property
    /// table of ITS OWN shader. The table is resolved ONCE when the window arms
    /// (<c>Shader.GetPropertyName</c> allocates a string, and doing that per frame would be an
    /// instrument that costs more than the thing it measures).</summary>
    private static readonly PropTable VTable = new(VerdictMatCap, VerdictPropCap);

    private static Renderer[] _vRenderers = System.Array.Empty<Renderer>();
    private static int _vFoundRenderers;

    /// <summary>The held prop's whole property table as of THIS frame. Not a history: the round
    /// nine comparison needs the current value to hold it against the twin's, and "did it move"
    /// has been asked and answered on three builds.</summary>
    private static readonly Vector4[] VNow = new Vector4[VerdictMatCap * VerdictPropCap];
    private static readonly bool[] VNowValid = new bool[VerdictMatCap * VerdictPropCap];




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
    /// (<c>GrabbableProp.FinishGlide</c> and <c>GrabbableProp.Restore</c>).</summary>
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
        RewindScratch.Clear();
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
                // NOT SWITCHED OFF HERE ANY MORE. An animator this class stops is first taken back
                // to its bound defaults (strand 6) and only then disabled, and the rewind has to be
                // measured across the whole set at once or the per-material read-back would run
                // once per animator. RewindAndStop does both, below.
                RewindScratch.Add(a);
        }
        AnimScratch.Clear();
        RewindAndStop(b, go);
        RewindScratch.Clear();

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

        // ---- STRAND 5: THE SCREEN-SPACE OCCLUSION REGISTRATION ----
        //
        // THE ONE PAINTER THAT IS NOT UNDER THE PROP. Five rounds measured the prop's subtree and
        // the sixth measured it AFTER the hush: over 358 frames on a held gold pile the animators,
        // outlines, lights and particles were all at zero, no material property moved, and the user
        // still reported the effect. A census rooted at the prop cannot answer that by construction.
        //
        // ObjectOcclusionVolume.OnEnable is `TilesOcclusionGenerator.s_Instance.AddObjectRenderer(
        // GetComponent<MeshRenderer>())` and OnDisable is the matching Remove. The generator is a
        // component ON A CAMERA holding a CommandBuffer at CameraEvent.BeforeGBuffer which draws
        // every registered renderer with m_OcclusionObjectMaterial into a QUARTER-RESOLUTION target,
        // blurs it twice and publishes it as the GLOBAL texture `_ObjectOcclusion` (plus
        // `_TilesOcclusionMap` and `_EnableOcclusionMap`). A global shader texture is invisible to
        // `material.Get*` read-back, it is not a Light, an Animator, a ParticleSystem or an
        // Outlinable, and it hangs off no object in the prop's hierarchy — so it is dark to every
        // instrument this file has ever shipped, and it is exactly the "screen-space occlusion term"
        // the ModBuild 448 HELD? line named as the next suspect and then could not test.
        //
        // WHY IT ONLY SHOWS IN A PALM. On its hex the prop's footprint in that map is small and
        // still, so the term it samples back is effectively constant. In a hand the prop fills a
        // large part of the eye and MOVES every frame, so its own blurred quarter-res silhouette
        // sweeps across it — a soft moving wash with no animator, no lamp and no material of its
        // own. Twenty centimetres from the eye that is the "highlighting/Licht-Effekt".
        //
        // TAKEN THROUGH THE GAME'S OWN LIFECYCLE, NOT BY EDITING ITS LIST. Disabling the component
        // makes OnDisable call RemoveObjectRenderer, which sets m_RenderersUpdated and has the
        // generator REBUILD its command buffer without this prop; restoring `enabled` runs OnEnable
        // and puts it back. So the ledger below is the whole of the change and the whole of the
        // undo, no game state is written, and a prop whose volume was ALREADY off is left alone.
        b.OcclusionFound = TakeEmitters<ObjectOcclusionVolume>(b, go, ref b.OcclusionOn0);
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

    // ---- STRAND 6: the rewind ---------------------------------------------------------------------

    /// <summary>
    /// TAKE EVERY ANIMATOR THIS CLASS IS ABOUT TO STOP BACK TO ITS BOUND DEFAULTS FIRST, THEN STOP
    /// IT — and measure what that changed.
    ///
    /// <para><b>WHY, AND IT IS THIS CLASS'S OWN SIDE EFFECT.</b> <c>Animator.enabled = false</c>
    /// does not undo a clip; it stops the clip WHERE IT IS. Every channel the clip drives —
    /// a material colour, an emissive boost, a dissolve amount, a renderer's own enabled flag, a
    /// bone — keeps the value it happened to hold on the frame of the grab, for the whole hold. The
    /// trap's idle is a single looping clip running at 0.202 normalized/s (ModBuild 447's A/B line,
    /// <c>advancing home=359/360</c>), i.e. a ~5 s cycle, and an attention flash is a short bright
    /// part of such a cycle. Grabbing during the bright part therefore LATCHES the bright part for
    /// as long as the prop is held, and grabbing anywhere else does not. That is
    /// "<i>manchmal</i> weiß" exactly, it is a state and not a movement, and it is invisible to
    /// every reading this file has ever taken because all of them measure whether something MOVED.
    /// A latched value does not move.</para>
    ///
    /// <para><b><c>WriteDefaultValues</c>, not <c>Play(state, layer, 0f)</c>.</b> Replaying a state
    /// from its start re-enters it, and re-entering a state fires every <c>StateMachineBehaviour</c>
    /// on it — on these props that is how <c>DelayedDeactivatePropAnimSMB</c> sends a rules message
    /// and raises a global "deactivations in progress" flag, and this lane does not write game
    /// state. <c>Animator.WriteDefaultValues</c> writes the values the animator recorded when it
    /// bound the controller, touches no state machine, fires no behaviour and no animation event,
    /// and does it for EVERY channel the controller animates — which matters because nothing in
    /// this file knows WHICH channel the flash lives in (the 448/450 read-backs say it is not any
    /// of the 48 float/range/colour properties, and the clip still advances on 359 of 360 home
    /// frames, so it is something else the clip drives).</para>
    ///
    /// <para><b>THE RESTORE IS THE GAME'S OWN WRITER.</b> <see cref="Restore"/> hands the
    /// <c>enabled</c> flag back and the animator resumes from the state it retained; on the first
    /// frame it evaluates, it drives every one of those channels itself, over our defaults. So the
    /// highlight comes back exactly as before by construction rather than by a remembered copy —
    /// there is no second snapshot to restore over somebody else's write, which is the recorded
    /// incident this project already paid for once. The falsifier is counted rather than asserted:
    /// <see cref="Belt.RestoreForeignAnimators"/> is how many animators were found ENABLED at the
    /// landing although this class had switched them off.</para>
    ///
    /// <para><b>Refused, not forced, on an animator that cannot have defaults:</b> an inactive
    /// object (the animator was never initialised) or a null controller. Those are counted in
    /// <see cref="Belt.RewindSkipped"/> and simply stopped the way ModBuild 445 stopped them.</para>
    /// </summary>
    private static void RewindAndStop(Belt b, GameObject go)
    {
        if (RewindScratch.Count == 0)
            return;

        // The table is resolved against the prop's CURRENT materials every time, because a rescan
        // can find an animator on a subtree that was inactive at the grab and whose materials were
        // not in the earlier table at all.
        SnapshotRewindState(go);

        int rewound = 0;
        for (int i = 0; i < RewindScratch.Count; i++)
        {
            Animator a = RewindScratch[i];
            if (a == null)
                continue;
            if (!a.gameObject.activeInHierarchy || a.runtimeAnimatorController == null)
            {
                b.RewindSkipped++;
                continue;
            }
            // THE PHASE AT THE GRAB, captured before anything is written and before the
            // animator is switched off (a disabled Animator reports layerCount 0, so this is
            // the only place it can be read at all).
            if (b.GrabPhaseCount == 0 && a.layerCount > 0)
                b.GrabPhase = a.GetCurrentAnimatorStateInfo(0).normalizedTime;
            b.GrabPhaseCount++;
            a.WriteDefaultValues();
            rewound++;
        }
        b.AnimatorsRewound += rewound;

        if (rewound > 0)
            MeasureRewindState(b);

        // Only now is the picture allowed to stop, so the frame that is frozen is the resting one.
        for (int i = 0; i < RewindScratch.Count; i++)
        {
            Animator a = RewindScratch[i];
            if (a != null)
                a.enabled = false;
        }
    }

    /// <summary>Remember every renderer flag the rewind could plausibly move, so the write can
    /// be measured on the class it may actually drive.</summary>
    private static void SnapshotRewindState(GameObject go)
    {
        _rwStateCount = 0;
        RwRendScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, RwRendScratch);
        for (int i = 0; i < RwRendScratch.Count && _rwStateCount < RewindStateCap; i++)
        {
            Renderer r = RwRendScratch[i];
            if (r == null)
                continue;
            RwRends[_rwStateCount] = r;
            RwRendOn[_rwStateCount] = r.enabled;
            RwActive[_rwStateCount] = r.gameObject.activeSelf;
            _rwStateCount++;
        }
        RwRendScratch.Clear();
    }

    /// <summary>Read those flags back. Pure instrument: it writes nothing but the two counts on
    /// the belt that the HOME TWIN line prints.</summary>
    private static void MeasureRewindState(Belt b)
    {
        for (int i = 0; i < _rwStateCount; i++)
        {
            Renderer r = RwRends[i];
            if (r == null)
                continue;
            b.RewindStateRead += 2;
            if (r.enabled != RwRendOn[i])
                b.RewindStateChanged++;
            if (r.gameObject.activeSelf != RwActive[i])
                b.RewindStateChanged++;
        }
        for (int i = 0; i < _rwStateCount; i++)
            RwRends[i] = null!;
        _rwStateCount = 0;
    }

    /// <summary>Did this slot change? Componentwise for a colour or a vector, EXACT for a texture
    /// (the value is a texture instance id and a "close" id means nothing), epsilon for a
    /// float.</summary>
    private static bool Moved(byte kind, Vector4 a, Vector4 c)
    {
        if (kind == PropTable.KindTexture)
            return a.x != c.x;
        if (kind == PropTable.KindFloat)
            return Mathf.Abs(a.x - c.x) > MoveEpsilon;
        return Mathf.Abs(a.x - c.x) > MoveEpsilon || Mathf.Abs(a.y - c.y) > MoveEpsilon
            || Mathf.Abs(a.z - c.z) > MoveEpsilon || Mathf.Abs(a.w - c.w) > MoveEpsilon;
    }

    /// <summary>A slot's value, printed the way its own class reads: one number for a float, four
    /// for a colour or a vector, a texture instance id for a texture.</summary>
    private static string Show(byte kind, Vector4 v)
    {
        if (kind == PropTable.KindTexture)
            return v.x == 0f ? "<no texture>" : $"tex#{(int)v.x}";
        if (kind == PropTable.KindFloat)
            return v.x.ToString("0.####");
        return $"({v.x:0.###},{v.y:0.###},{v.z:0.###},{v.w:0.###})";
    }

    /// <summary>A single comparable number per slot, for the min/max range the movement report
    /// prints: the value itself for a float, the mean of r/g/b for a colour, the magnitude for a
    /// vector, the instance id for a texture.</summary>
    private static float Fold(byte kind, Vector4 v)
    {
        if (kind == PropTable.KindColor)
            return (v.x + v.y + v.z) * (1f / 3f);
        if (kind == PropTable.KindVector)
            return Mathf.Sqrt((v.x * v.x) + (v.y * v.y) + (v.z * v.z) + (v.w * v.w));
        return v.x;
    }

    // ---- the property table -----------------------------------------------------------------------

    /// <summary>
    /// THE MATERIALS UNDER ONE PROP AND, PER MATERIAL, THE PROPERTY TABLE OF *ITS OWN* SHADER —
    /// float, range, int, colour, VECTOR and TEXTURE.
    ///
    /// <para><b>Two defects in the old read-back this type exists to remove.</b> (1) It built ONE
    /// property table, from <c>VMats[0]</c>'s shader, and then read every other material through
    /// it; every id that material's own shader does not declare was silently skipped by
    /// <c>HasProperty</c>, and the line still printed "N material(s) on shader 'X'" as though all
    /// of them were X. (2) It tracked float, range and colour only, so a VECTOR or a TEXTURE
    /// property could be rewritten every frame and the line would say "nothing moved" — which the
    /// ModBuild 450 line names as its own blind spot in so many words.</para>
    ///
    /// <para><b><c>sharedMaterials</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a clone the first time it is touched, which is a permanent change to the scene
    /// made by an instrument. Once anything has instanced a renderer's material Unity stores the
    /// clone back into the renderer, so <c>sharedMaterials</c> returns the instanced values without
    /// ever creating one.</para>
    /// </summary>
    private sealed class PropTable
    {
        internal const byte KindFloat = 0, KindColor = 1, KindVector = 2, KindTexture = 3;

        private readonly int _matCap;
        private readonly int _propCap;
        private readonly List<Renderer> _rends = new(16);

        internal readonly Material[] Mats;
        internal readonly int[] Id;
        internal readonly string[] Name;
        internal readonly byte[] Kind;

        /// <summary>Properties tracked for material <c>m</c>, and the shader it actually uses.</summary>
        internal readonly int[] PerMat;
        internal readonly string[] Shader;
        internal readonly int[] DeclaredPerMat;

        /// <summary>Distinct materials KEPT, and distinct materials FOUND. A truncated list is not
        /// an absence and both numbers are printed.</summary>
        internal int MatCount, MatFound;

        internal PropTable(int matCap, int propCap)
        {
            _matCap = matCap;
            _propCap = propCap;
            Mats = new Material[matCap];
            Shader = new string[matCap];
            PerMat = new int[matCap];
            DeclaredPerMat = new int[matCap];
            Id = new int[matCap * propCap];
            Name = new string[matCap * propCap];
            Kind = new byte[matCap * propCap];
        }

        internal int PropCap => _propCap;

        internal int Capacity => _matCap * _propCap;

        /// <summary>Total (material, property) slots this table populates.</summary>
        internal int Slots
        {
            get
            {
                int n = 0;
                for (int m = 0; m < MatCount; m++)
                    n += PerMat[m];
                return n;
            }
        }

        /// <summary>Properties declared across every material this table kept — the population the
        /// tracked count is a fraction OF.</summary>
        internal int Declared
        {
            get
            {
                int n = 0;
                for (int m = 0; m < MatCount; m++)
                    n += DeclaredPerMat[m];
                return n;
            }
        }

        /// <summary>True when a material's own table hit the per-material cap, so a zero below it
        /// is a truncation rather than an absence.</summary>
        internal bool Truncated
        {
            get
            {
                if (MatFound > MatCount)
                    return true;
                for (int m = 0; m < MatCount; m++)
                {
                    if (PerMat[m] >= _propCap && DeclaredPerMat[m] > PerMat[m])
                        return true;
                }
                return false;
            }
        }

        internal string ShaderOf(int mat) => mat >= 0 && mat < MatCount ? Shader[mat] : "<none>";

        /// <summary>Per-material "'shader' D declared / T tracked", so a line can never again claim
        /// one shader for materials that do not share one.</summary>
        internal string Describe()
        {
            var sb = new System.Text.StringBuilder(128);
            for (int m = 0; m < MatCount; m++)
            {
                if (m > 0)
                    sb.Append("; ");
                sb.Append("mat").Append(m).Append(" '").Append(Shader[m]).Append("' ")
                  .Append(DeclaredPerMat[m]).Append(" declared / ").Append(PerMat[m])
                  .Append(" tracked");
            }
            return sb.Length == 0 ? "no material" : sb.ToString();
        }

        internal void Resolve(GameObject go)
        {
            MatCount = 0;
            MatFound = 0;
            for (int i = 0; i < _matCap; i++)
            {
                Mats[i] = null!;
                Shader[i] = "<none>";
                PerMat[i] = 0;
                DeclaredPerMat[i] = 0;
            }

            _rends.Clear();
            go.GetComponentsInChildren(includeInactive: true, _rends);
            for (int r = 0; r < _rends.Count; r++)
            {
                Renderer rend = _rends[r];
                if (rend == null)
                    continue;
                Material[] mats = rend.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;
                    bool dup = false;
                    for (int k = 0; k < MatCount; k++)
                        dup |= ReferenceEquals(Mats[k], mat);
                    if (dup)
                        continue;
                    MatFound++;
                    if (MatCount >= _matCap)
                        continue;
                    Mats[MatCount++] = mat;
                }
            }
            _rends.Clear();

            for (int m = 0; m < MatCount; m++)
            {
                Material mat = Mats[m];
                Shader? sh = mat != null ? mat.shader : null;
                if (sh == null)
                    continue;
                Shader[m] = sh.name;
                int declared = sh.GetPropertyCount();
                DeclaredPerMat[m] = declared;
                int kept = 0;
                for (int i = 0; i < declared && kept < _propCap; i++)
                {
                    UnityEngine.Rendering.ShaderPropertyType t = sh.GetPropertyType(i);
                    byte kind;
                    switch (t)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color: kind = KindColor; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector: kind = KindVector; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture: kind = KindTexture; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                        case UnityEngine.Rendering.ShaderPropertyType.Int: kind = KindFloat; break;
                        default: continue;
                    }
                    int slot = (m * _propCap) + kept;
                    Id[slot] = sh.GetPropertyNameId(i);
                    Name[slot] = sh.GetPropertyName(i);
                    Kind[slot] = kind;
                    kept++;
                }
                PerMat[m] = kept;
            }
        }

        /// <summary>Read every slot's current value. Allocation-free: the caller owns both arrays
        /// and every getter returns a struct or a reference.</summary>
        internal void Sample(Vector4[] into, bool[] valid)
        {
            for (int slot = 0; slot < into.Length; slot++)
                valid[slot] = false;
            for (int m = 0; m < MatCount; m++)
            {
                Material mat = Mats[m];
                if (mat == null)
                    continue;
                int n = PerMat[m];
                for (int k = 0; k < n; k++)
                {
                    int slot = (m * _propCap) + k;
                    int id = Id[slot];
                    if (!mat.HasProperty(id))
                        continue;
                    Vector4 v;
                    switch (Kind[slot])
                    {
                        case KindColor:
                        {
                            Color c = mat.GetColor(id);
                            v = new Vector4(c.r, c.g, c.b, c.a);
                            break;
                        }
                        case KindVector:
                            v = mat.GetVector(id);
                            break;
                        case KindTexture:
                        {
                            Texture? tex = mat.GetTexture(id);
                            v = new Vector4(tex != null ? tex.GetInstanceID() : 0f, 0f, 0f, 0f);
                            break;
                        }
                        default:
                            v = new Vector4(mat.GetFloat(id), 0f, 0f, 0f);
                            break;
                    }
                    into[slot] = v;
                    valid[slot] = true;
                }
            }
        }
    }


    /// <summary>Give every replaced value back, object for object. A destroyed object is skipped
    /// rather than written — Unity's <c>!= null</c> answers that — and the lists are cleared by
    /// <see cref="Retire"/> whatever happens here.</summary>
    private static void Restore(Belt b)
    {
        for (int i = 0; i < b.Animators.Count && i < b.AnimEnabled0.Count; i++)
        {
            Animator a = b.Animators[i];
            if (a == null)
                continue;
            // THE FALSIFIER FOR STRAND 6'S RESTORE, counted the same way the emitters' is. This
            // class left every animator it wrote at enabled=false; finding one ENABLED means
            // somebody else turned it back on during the hold, and the value about to be handed
            // back is being handed back over their write. The rewind itself needs no restore — the
            // animator re-drives every channel it owns on the first frame it evaluates, so the
            // highlight returns because its own writer takes it back, not because we kept a copy.
            if (b.AnimEnabled0[i] && a.enabled)
                b.RestoreForeignAnimators++;
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
        b.AnimatorsLeftForRules = 0;
        b.AnimatorsAlreadyOff = 0;
        b.OutlinesAlreadyOff = 0;
        b.SkinsAlready = 0;
        b.LightsFound = b.LightsOn0 = 0;
        b.ProjectorsFound = b.ProjectorsOn0 = 0;
        b.FlaresFound = b.FlaresOn0 = 0;
        b.RestoreForeignEmitters = b.RestoreDead = 0;
        b.RestoreForeignAnimators = 0;
        b.AnimatorsRewound = b.RewindSkipped = 0;
        b.RewindStateRead = b.RewindStateChanged = 0;
        b.GrabPhase = 0f;
        b.GrabPhaseCount = 0;
        b.Label = string.Empty;
        b.Rescans = 0;
        b.NextScanFrame = 0;
        if (Pool.Count < 4)
            Pool.Add(b);
    }

    // ---- the census ------------------------------------------------------------------------------

    // ---- the POST-HUSH verdict --------------------------------------------------------------------

    /// <summary>Root-down hierarchy path, capped, so a foreign emitter is attributable to the
    /// system that owns it rather than to a bare object name.</summary>
    private static string Describe(Transform t)
    {
        string path = t.name;
        Transform? p = t.parent;
        for (int depth = 0; depth < 4 && p != null; depth++)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
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


    // ---- ROUND EIGHT: the roster, the per-frame property block, and the timeline -------------------


    // ---- ROUND NINE: THE HOME TWIN -----------------------------------------------------------------
    //
    // THE USER NAMED THE TRIGGER, AND IT TURNS A ROOM FULL OF ZEROES INTO A FINDING.
    //
    //   "Das weiße in der Hand tritt immer auf wenn ich die Falle/Truhe aufhebe kurz nachdem der
    //    weiße flash auf allen Fallen kam. Es hat also sehr sicher was damit zu tun - ist also
    //    abhängig zu welchem Zeitpunkt ich es aufhebe."
    //
    // A white flash runs across EVERY trap AT ONCE, and grabbing shortly after it ALWAYS leaves the
    // held one white. So the white is a value LATCHED AT THE INSTANT OF THE GRAB, and nothing during
    // the hold either sustains it or removes it. That is exactly what eight rounds have measured:
    // ModBuild 454 reads no property block on any of 360 frames, 0 keyword changes, a light-probe
    // luminance flat to four decimals, no reflection probe at all, and the 453 verdict beside it
    // reads 0 of 67 material slots moving. **Every instrument in this file asks whether something
    // MOVES. Not one has ever asked whether the value is the RIGHT one**, and a latched wrong value
    // is constant — which is what all of them print.
    //
    // THE MEASUREMENT IS THEREFORE A COMPARISON AND NOT A SERIES: read the whole property table of
    // the HELD prop and of another instance of the SAME PROP KIND still standing on its hex, IN THE
    // SAME FRAME. A slot that differs names the channel the flash lives in and hands over its
    // correct value at the same time. It is immune to phase (both are read on the same tick), immune
    // to pose (a material value has no view angle in it), and it is the one comparison eight rounds
    // have never taken.
    //
    // IT ALSO SETTLES THE SHARED-MATERIAL TENSION IN ONE FIELD. If every trap draws ONE material
    // asset, freezing our animator cannot stop that shared value from moving — yet the held table is
    // provably static, so the material must be instanced per prop. The twin's material instance ids
    // are compared against the held prop's and the answer is printed rather than argued.
    private const int TwinSlots = VerdictMatCap * VerdictPropCap;

    private static Renderer? _twinLead;
    private static Animator? _twinAnim;
    private static GameObject? _twinRoot;
    private static readonly Material[] TwinMats = new Material[VerdictMatCap];
    private static string _twinPath = string.Empty;
    private static int _twinScanned, _twinCandidates, _twinSharedMats, _twinInstancedMats;
    private static int _twinFramesAlive, _twinFramesDrawing, _twinAnimAdvancing, _twinAnimFrames;
    private static float _twinPhasePrev, _twinPhaseLo, _twinPhaseHi;

    private static readonly Vector4[] TwPrev = new Vector4[TwinSlots];
    private static readonly Vector4[] TwNow = new Vector4[TwinSlots];
    private static readonly bool[] TwValid = new bool[TwinSlots];
    private static readonly bool[] TwSeen = new bool[TwinSlots];
    private static readonly int[] TwMoves = new int[TwinSlots];
    private static readonly float[] TwLo = new float[TwinSlots];
    private static readonly float[] TwHi = new float[TwinSlots];
    private static readonly int[] DiffFrames = new int[TwinSlots];
    private static readonly Vector4[] DiffHeld = new Vector4[TwinSlots];
    private static readonly Vector4[] DiffHome = new Vector4[TwinSlots];

    private static readonly List<Renderer> TwinScratch = new(8);

    /// <summary>
    /// Find another instance of the same prop kind that is NOT in a hand, once, when the window
    /// arms.
    ///
    /// <para>Matched on the drawing renderer's OBJECT NAME and its SHADER, not on the material
    /// name: <c>Renderer.material</c> appends " (Instance)" to a clone, so a material-name compare
    /// silently refuses exactly the case this round exists to detect. One
    /// <c>FindObjectsOfType</c> at arm and never per frame: a per-frame scene sweep has cost
    /// this project two rounds and one 12.6 ms frame.</para>
    /// </summary>
    private static void FindHomeTwin(GameObject held)
    {
        _twinLead = null;
        _twinAnim = null;
        _twinRoot = null;
        _twinPath = string.Empty;
        _twinScanned = _twinCandidates = _twinSharedMats = _twinInstancedMats = 0;
        _twinFramesAlive = _twinFramesDrawing = _twinAnimAdvancing = _twinAnimFrames = 0;
        _twinPhasePrev = float.NaN;
        _twinPhaseLo = float.MaxValue;
        _twinPhaseHi = float.MinValue;
        for (int i = 0; i < VerdictMatCap; i++)
            TwinMats[i] = null!;
        for (int i = 0; i < TwinSlots; i++)
        {
            TwSeen[i] = false;
            TwMoves[i] = 0;
            DiffFrames[i] = 0;
            TwLo[i] = float.MaxValue;
            TwHi[i] = float.MinValue;
        }

        // The held prop's own drawing renderer is the thing to match. Take the first roster entry
        // that is drawing AND carries a material — roster entry [1] on the trap is the occlusion
        // volume's MeshRenderer with no material at all, and matching on that would find nothing.
        Renderer? lead = null;
        for (int i = 0; i < _rCount && lead == null; i++)
        {
            int src = RSrc[i];
            if (src < 0 || src >= _vRenderers.Length)
                continue;
            Renderer r = _vRenderers[src];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.sharedMaterial != null)
                lead = r;
        }
        if (lead == null)
        {
            // No drawing renderer with a material means no twin can be matched, and the graph arm
            // must be cleared with it: reporting a previous verdict's snapshot against this prop
            // would be a stale reading wearing a fresh one's clothes.
            _twinRoot = null;
            ArmTwinGraph();
            return;
        }

        string wantName = lead.gameObject.name;
        Shader? leadShader = lead.sharedMaterial != null ? lead.sharedMaterial.shader : null;
        string wantShader = leadShader != null ? leadShader.name : string.Empty;
        Transform heldT = held.transform;

        Renderer[] all = Object.FindObjectsOfType<Renderer>();
        _twinScanned = all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || ReferenceEquals(r, lead))
                continue;
            if (!r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            // NOT under the held prop, and not under any OTHER hand either: a second held prop is
            // hushed too and its table would be latched in the same way, which would make the
            // comparison agree for the wrong reason.
            if (r.transform.IsChildOf(heldT))
                continue;
            if (!string.Equals(r.gameObject.name, wantName, System.StringComparison.Ordinal))
                continue;
            Material? m = r.sharedMaterial;
            Shader? s = m != null ? m.shader : null;
            if (s == null || !string.Equals(s.name, wantShader, System.StringComparison.Ordinal))
                continue;
            _twinCandidates++;
            if (_twinLead != null)
                continue;
            bool hushed = false;
            for (int k = 0; k < Live.Count; k++)
            {
                GameObject? v = Live[k].Visual;
                if (v != null && r.transform.IsChildOf(v.transform))
                    hushed = true;
            }
            if (hushed)
                continue;
            _twinLead = r;
        }
        if (_twinLead == null)
        {
            _twinRoot = null;
            ArmTwinGraph();
            return;
        }

        _twinAnim = _twinLead.GetComponentInParent<Animator>();
        _twinRoot = _twinAnim != null ? _twinAnim.gameObject : _twinLead.gameObject;
        _twinPath = Describe(_twinLead.transform);

        // Bind the twin's materials to the HELD table's slot layout by SHADER, rather than
        // resolving a second table off the twin. Two independently resolved tables can order their
        // materials differently and a slot-by-slot compare across them would be comparing two
        // different properties while printing one name.
        TwinScratch.Clear();
        _twinRoot.GetComponentsInChildren(includeInactive: true, TwinScratch);
        for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
        {
            string want = VTable.ShaderOf(m);
            for (int r = 0; r < TwinScratch.Count && TwinMats[m] == null; r++)
            {
                Renderer rend = TwinScratch[r];
                if (rend == null)
                    continue;
                Material[] mats = rend.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    Material mat = mats[k];
                    Shader? sh = mat != null ? mat.shader : null;
                    if (sh == null || !string.Equals(sh.name, want, System.StringComparison.Ordinal))
                        continue;
                    TwinMats[m] = mat!;
                    break;
                }
            }
            Material held0 = VTable.Mats[m];
            if (TwinMats[m] == null || held0 == null)
                continue;
            if (ReferenceEquals(TwinMats[m], held0))
                _twinSharedMats++;
            else
                _twinInstancedMats++;
        }
        TwinScratch.Clear();
        ArmTwinGraph();
    }

    /// <summary>One frame of the comparison. Called from <see cref="SampleVerdict"/> immediately
    /// after the held table has been read into <c>VNow</c>, so both sides are the SAME TICK — which
    /// is the whole point, because the props are in phase with each other.</summary>
    private static void SampleTwin()
    {
        if (_twinLead == null)
            return;
        _twinFramesAlive++;
        if (_twinLead.enabled && _twinLead.gameObject.activeInHierarchy)
            _twinFramesDrawing++;

        if (_twinAnim != null && _twinAnim.enabled && _twinAnim.layerCount > 0)
        {
            _twinAnimFrames++;
            float t = _twinAnim.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (!float.IsNaN(_twinPhasePrev) && Mathf.Abs(t - _twinPhasePrev) > MoveEpsilon)
                _twinAnimAdvancing++;
            _twinPhasePrev = t;
            float frac = t - Mathf.Floor(t);
            if (frac < _twinPhaseLo)
                _twinPhaseLo = frac;
            if (frac > _twinPhaseHi)
                _twinPhaseHi = frac;
        }

        SampleTwinGraph();
        SampleLightingCompare();

        for (int slot = 0; slot < TwinSlots; slot++)
            TwValid[slot] = false;
        for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
        {
            Material mat = TwinMats[m];
            if (mat == null)
                continue;
            int n = VTable.PerMat[m];
            for (int k = 0; k < n; k++)
            {
                int slot = (m * VerdictPropCap) + k;
                int id = VTable.Id[slot];
                if (!mat.HasProperty(id))
                    continue;
                Vector4 v;
                switch (VTable.Kind[slot])
                {
                    case PropTable.KindColor:
                    {
                        Color c = mat.GetColor(id);
                        v = new Vector4(c.r, c.g, c.b, c.a);
                        break;
                    }
                    case PropTable.KindVector:
                        v = mat.GetVector(id);
                        break;
                    case PropTable.KindTexture:
                    {
                        Texture? tex = mat.GetTexture(id);
                        v = new Vector4(tex != null ? tex.GetInstanceID() : 0f, 0f, 0f, 0f);
                        break;
                    }
                    default:
                        v = new Vector4(mat.GetFloat(id), 0f, 0f, 0f);
                        break;
                }
                TwNow[slot] = v;
                TwValid[slot] = true;
            }
        }

        for (int slot = 0; slot < TwinSlots; slot++)
        {
            if (!TwValid[slot])
                continue;
            byte kind = VTable.Kind[slot];
            Vector4 home = TwNow[slot];
            if (TwSeen[slot] && Moved(kind, TwPrev[slot], home))
                TwMoves[slot]++;
            TwPrev[slot] = home;
            TwSeen[slot] = true;
            float f = Fold(kind, home);
            if (f < TwLo[slot])
                TwLo[slot] = f;
            if (f > TwHi[slot])
                TwHi[slot] = f;

            if (!VNowValid[slot] || !Moved(kind, VNow[slot], home))
                continue;
            DiffFrames[slot]++;
            DiffHeld[slot] = VNow[slot];
            DiffHome[slot] = home;
        }
    }

    /// <summary>The slots that MOVE on a prop of this kind standing on its hex. This is the flash
    /// itself, if the flash is a material property at all.</summary>
    private static void AppendTwinMovers(System.Text.StringBuilder sb)
    {
        int moved = 0, tracked = 0;
        for (int slot = 0; slot < TwinSlots; slot++)
        {
            if (!TwSeen[slot])
                continue;
            tracked++;
            if (TwMoves[slot] > 0)
                moved++;
        }
        sb.Append("WHAT MOVES ON THE UNHELD TWIN — i.e. THE FLASH ITSELF: ").Append(moved)
          .Append(" of ").Append(tracked).Append(" tracked slot(s)");
        if (moved == 0)
        {
            sb.Append(" — NOTHING on a prop of this kind standing on its own hex moved a single "
                      + "material property on any sampled frame. That EXCLUDES the whole material "
                      + "class for the flash the user sees on every trap at once, and it does so "
                      + "with a population that is not hushed, not held and not frozen. ");
            return;
        }
        sb.Append(", naming up to ").Append(VerdictListCap).Append(": ");
        int listed = 0;
        for (int slot = 0; slot < TwinSlots && listed < VerdictListCap; slot++)
        {
            if (!TwSeen[slot] || TwMoves[slot] <= 0)
                continue;
            if (listed > 0)
                sb.Append("; ");
            sb.Append(VTable.Name[slot] ?? "<unnamed>").Append(" (mat")
              .Append(slot / VerdictPropCap).Append(' ').Append(VTable.ShaderOf(slot / VerdictPropCap))
              .Append(") moved on ").Append(TwMoves[slot]).Append(" frame(s), home range ")
              .Append(TwLo[slot].ToString("0.####")).Append("..").Append(TwHi[slot].ToString("0.####"))
              .Append(", HELD sits at ")
              .Append(Show(VTable.Kind[slot], VNowValid[slot] ? VNow[slot] : Vector4.zero));
            listed++;
        }
        sb.Append(listed < moved ? ", and the rest are counted but not named. " : ". ");
    }

    /// <summary>The slots where the held prop and the twin DISAGREE. Each one is a candidate
    /// channel with its own correct value beside it.</summary>
    private static void AppendTwinDiffs(System.Text.StringBuilder sb)
    {
        int differing = 0;
        for (int slot = 0; slot < TwinSlots; slot++)
        {
            if (DiffFrames[slot] > 0)
                differing++;
        }
        sb.Append("HELD vs HOME, SAME TICK: ").Append(differing).Append(" slot(s) EVER DIFFERED");
        if (differing == 0)
        {
            sb.Append(" — the held prop's material state is IDENTICAL to a prop of the same kind on "
                      + "its hex, on every sampled frame. If the user still saw white on this hold, "
                      + "the picture is not made of this prop's material values and no fix written "
                      + "on them can change it. ");
            return;
        }
        sb.Append(", naming up to ").Append(VerdictListCap).Append(" with BOTH values: ");
        int listed = 0;
        for (int slot = 0; slot < TwinSlots && listed < VerdictListCap; slot++)
        {
            if (DiffFrames[slot] <= 0)
                continue;
            if (listed > 0)
                sb.Append("; ");
            byte kind = VTable.Kind[slot];
            sb.Append(VTable.Name[slot] ?? "<unnamed>").Append(" (mat")
              .Append(slot / VerdictPropCap).Append(") differed on ").Append(DiffFrames[slot])
              .Append(" frame(s): HELD ").Append(Show(kind, DiffHeld[slot])).Append(" vs HOME ")
              .Append(Show(kind, DiffHome[slot])).Append(", home ranged ")
              .Append(TwLo[slot].ToString("0.####")).Append("..").Append(TwHi[slot].ToString("0.####"));
            listed++;
        }
        sb.Append(listed < differing ? ", and the rest are counted but not named. " : ". ");
    }

    /// <summary>
    /// The half of the rewind measurement that has never been taken: whether
    /// <c>WriteDefaultValues</c> changed a RENDERER or an OBJECT ACTIVE flag.
    ///
    /// <para>An <c>AnimationClip</c> can drive <c>GameObject.m_IsActive</c> and
    /// <c>Renderer.m_Enabled</c>, so an attention flash authored as "switch the glow mesh on for
    /// half a second" carries NO material property at all — and this file's rewind measurement has
    /// counted only (material, property) slots for two builds, which is why it reads
    /// <c>0 of 67 changed</c> either way. That zero has never distinguished "the clip was at rest"
    /// from "the clip does not drive a material".</para>
    /// </summary>
    private static void AppendTwinRewindState(System.Text.StringBuilder sb, Belt b)
    {
        sb.Append("THE REWIND'S OTHER HALF (new this build): across Animator.WriteDefaultValues, ")
          .Append(b.RewindStateRead)
          .Append(" renderer/object flag(s) were read and ").Append(b.RewindStateChanged)
          .Append(" CHANGED. An AnimationClip can drive GameObject.m_IsActive and "
                  + "Renderer.m_Enabled, so a flash authored as 'switch the glow mesh on' carries "
                  + "no material property at all — and the '0 of N (material, property) slot(s) "
                  + "changed' this file has printed for two builds could never tell that apart "
                  + "from a clip already at rest. A non-zero count here says the freeze was "
                  + "latching a RENDERER STATE and names a different fix from a material one. ");
    }

    // ---- the restore, and its own falsifier -------------------------------------------------------


    // ---- the reporting -----------------------------------------------------------------------------
    //
    // TWO LINES, AND THAT IS THE WHOLE INSTRUMENT SURFACE. Nine rounds left this class printing
    // five multi-kilobyte censuses whose questions were all answered; the answers live in
    // .planning/held-prop-flash-experiments.md and the code is not an archive. What survives is:
    // the grab-edge PRE-COUNTS, because they are what attributes a strand and what decides whether
    // a strand may be deleted; the RESTORE falsifier, because it guards a write; and the HOME TWIN
    // comparison, which is the only reading in this file that asks whether the held prop's value
    // is the RIGHT one rather than whether it MOVED.

    private const int RosterCap = 8;

    private static readonly string[] RName = new string[RosterCap];
    private static readonly string[] RShaders = new string[RosterCap];
    private static readonly bool[] RMine = new bool[RosterCap];
    private static readonly int[] RDrawFrames = new int[RosterCap];
    private static readonly int[] RVisFrames = new int[RosterCap];
    private static readonly int[] RDeadAtFrame = new int[RosterCap];

    /// <summary>The index in <c>_vRenderers</c> each roster entry was taken from. The roster SKIPS
    /// nulls, so the two arrays are only aligned when nothing was null at arm time; carrying the
    /// source index is what keeps a per-frame reading on the object it names.</summary>
    private static readonly int[] RSrc = new int[RosterCap];
    private static int _rCount, _rFound;

    /// <summary>THE POSE CONTROL. The photometry of the user's video measures a bright-pixel
    /// fraction inside a FIXED window while the prop is turned over in the hand, so a term that
    /// fades because the object ROTATES AWAY and a term that fades with the CLOCK draw the same
    /// curve. On ModBuild 454 the angle swept 52.4..130.7 deg while every value held still, which
    /// is what makes the video's curve a confound rather than a measurement — and what makes a
    /// HELD-vs-HOME difference readable as a real one.</summary>
    private static float _poseAngLo, _poseAngHi, _poseDistLo, _poseDistHi;

    /// <summary>
    /// Say what was suppressed and what was left alive, once per prop kind.
    ///
    /// <para>THE NUMBERS THAT MATTER HERE ARE THE PRE-COUNTS. "N of class X existed and M of them
    /// were LIVE before this class wrote anything" is what makes two suppressions in one round
    /// attributable, and it is the rule this file uses to decide whether a strand may be deleted:
    /// a class whose live count is zero on every prop kind ever measured has never written
    /// anything and cannot be why anything changed in either direction. Pure print — the state
    /// machine above has finished by the time this runs.</para>
    /// </summary>
    private static void Announce(Belt b, string label)
    {
        if (_logsLeft <= 0 || Spent(KindsDone, label))
            return;
        _logsLeft--;
        if (KindsDone.Count < KindCap)
            KindsDone.Add(label);

        GameObject? go = b.Visual;
        int renderers = 0, visible = 0;
        if (go != null)
        {
            RendScratch.Clear();
            go.GetComponentsInChildren(includeInactive: true, RendScratch);
            renderers = RendScratch.Count;
            for (int i = 0; i < RendScratch.Count; i++)
            {
                Renderer r = RendScratch[i];
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.isVisible)
                    visible++;
            }
            RendScratch.Clear();
        }

        var sb = new System.Text.StringBuilder(1024);
        sb.Append("[Props] HELD-PROP ANIMATION HUSH for ").Append(label)
          .Append(" — this mod suppresses a prop's own attention animation for the length of the "
                  + "hold, on the user's explicit instruction after four rounds failed to make it "
                  + "play right in a palm (.planning/held-prop-flash-experiments.md §6). EVERY "
                  + "FIGURE BELOW IS A PRE-COUNT: how many existed and how many were LIVE before "
                  + "this class wrote anything. A class whose live count is 0 made no writes and "
                  + "cannot be why anything changed either way — that is how this file decides "
                  + "which strands are real. ANIMATORS: ").Append(b.Animators.Count + b.AnimatorsAlreadyOff)
          .Append(" under the visual, ").Append(b.Animators.Count).Append(" switched off, ")
          .Append(b.AnimatorsAlreadyOff).Append(" already off, ").Append(b.AnimatorsLeftForRules)
          .Append(" LEFT RUNNING on purpose because their controller carries "
                  + "DelayedDeactivatePropAnimSMB, which sends a rules message for a sprung trap "
                  + "and holds a global 'deactivations in progress' flag — freezing that one would "
                  + "stall game state and this lane does not write game state. REWOUND FIRST: ")
          .Append(b.AnimatorsRewound)
          .Append(" taken back to their BOUND DEFAULT VALUES with Animator.WriteDefaultValues "
                  + "before being switched off, ").Append(b.RewindSkipped)
          .Append(" refused (inactive object or no controller). OUTLINES: ")
          .Append(b.Outlines.Count + b.OutlinesAlreadyOff).Append(" EPOOutline.Outlinable, ")
          .Append(b.Outlines.Count).Append(" switched off, ").Append(b.OutlinesAlreadyOff)
          .Append(" already off — the component's own enabled flag and NOT "
                  + "OutlineParameters.Enabled, because the game writes that property in nine "
                  + "places and it is only one of three parameter blocks an Outlinable can draw "
                  + "from. BEHAVIOUR-DERIVED EMITTERS (the class is taken WHOLE because the TYPE "
                  + "boundary is the defect — Light derives from Behaviour and NOT from "
                  + "MonoBehaviour, and ModBuild 151 lost a build to exactly that hole): ")
          .Append(b.LightsFound).Append(" Light(s) of which ").Append(b.LightsOn0)
          .Append(" were ENABLED, ").Append(b.ProjectorsFound).Append(" Projector(s) of which ")
          .Append(b.ProjectorsOn0).Append(" enabled, ").Append(b.FlaresFound)
          .Append(" LensFlare(s) of which ").Append(b.FlaresOn0)
          .Append(" enabled. SCREEN-SPACE OCCLUSION REGISTRATION: ").Append(b.OcclusionFound)
          .Append(" ObjectOcclusionVolume(s) of which ").Append(b.OcclusionOn0)
          .Append(" were ENABLED and are unregistered for the hold — ObjectOcclusionVolume's "
                  + "OnDisable IS TilesOcclusionGenerator.RemoveObjectRenderer, so this is taken "
                  + "through the game's own lifecycle and writes no game state. NOT SUPPRESSED — "
                  + "SkinnedMeshRenderer.updateWhenOffscreen: ").Append(b.Skins.Count)
          .Append(" set true, ").Append(b.SkinsAlready)
          .Append(" already true; that is not an animation term at all, it keeps a held prop DRAWN "
                  + "when its stale root-bone bounds leave the frustum. STILL ALIVE UNDER THIS "
                  + "PROP: ").Append(renderers).Append(" renderer(s) of which ").Append(visible)
          .Append(" reported isVisible (READ IT AS THE PREVIOUS FRAME'S ANSWER — isVisible is last "
                  + "frame's culling result and the prop has only just been reparented, so it "
                  + "describes the hex and not the hand). ").Append(_logsLeft)
          .Append(" more prop hush line(s) this session, at most one per prop kind.");

        // HW-VERIFY: the pre-counts on this line are what attribute every suppression this class
        // makes, and they are the evidence any future round needs to decide whether a strand is
        // real. It must stay at a tier the DEFAULT log level prints (Note/Alert/Error) —
        // scripts/check-hw-verify.py enforces the position of this marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>
    /// Arm the window that compares a hushed prop against one of its own kind still on its hex.
    /// Pure instrument: it reads and it prints, and nothing outside this region reads a field it
    /// writes.
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
        _vRenderers = Take(go.GetComponentsInChildren<Renderer>(true), out _vFoundRenderers);

        _poseAngLo = _poseDistLo = float.MaxValue;
        _poseAngHi = _poseDistHi = float.MinValue;

        ResolveVerdictMaterials();
        ArmRoster();
        // Round nine. After the roster, because the twin is matched against the held prop's first
        // DRAWING renderer that carries a material, which the roster has just resolved.
        FindHomeTwin(go);
    }

    /// <summary>
    /// Resolve the materials whose table is read back each frame.
    ///
    /// <para><b><c>sharedMaterials</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a clone the first time it is touched, which is a permanent change to the scene
    /// made by an instrument. It is also unnecessary: once anything has instanced a renderer's
    /// material, Unity stores that clone back into the renderer and <c>sharedMaterials</c>
    /// afterwards returns the CLONE.</para>
    /// </summary>
    private static void ResolveVerdictMaterials()
    {
        GameObject? go = _vBelt != null ? _vBelt.Visual : null;
        if (go != null)
            VTable.Resolve(go);
    }

    /// <summary>
    /// Name every renderer under the held prop, once.
    ///
    /// <para>Nine rounds printed COUNTS. "at most 1 drawing" of 3 renderers and "one material on
    /// GloomhavenVR/Overlay" are identity questions a count cannot answer, and the recorded lesson
    /// is "name the blocker, not the number". ModBuild 454 answered both from this list: the
    /// mod-owned overlay drew on 0 of 360 frames and was destroyed at frame 1, and the only
    /// renderer that draws is the game's own <c>Beartrap</c> on <c>Amp_Char_Shader</c>.</para>
    /// </summary>
    private static void ArmRoster()
    {
        _rCount = 0;
        _rFound = _vRenderers.Length;
        for (int i = 0; i < RosterCap; i++)
        {
            RName[i] = string.Empty;
            RShaders[i] = string.Empty;
            RMine[i] = false;
            RDrawFrames[i] = RVisFrames[i] = 0;
            RDeadAtFrame[i] = -1;
            RSrc[i] = -1;
        }
        for (int i = 0; i < _vRenderers.Length && _rCount < RosterCap; i++)
        {
            Renderer r = _vRenderers[i];
            if (r == null)
                continue;
            int k = _rCount++;
            RSrc[k] = i;
            RName[k] = Describe(r.transform) + " [" + r.GetType().Name + "]";
            Material[] mats = r.sharedMaterials;
            var names = new System.Text.StringBuilder(64);
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (m > 0)
                    names.Append(" + ");
                if (mat == null)
                {
                    names.Append("<null slot>");
                    continue;
                }
                Shader? sh = mat.shader;
                string shName = sh != null ? sh.name : "<no shader>";
                names.Append('\'').Append(mat.name).Append("' on ").Append(shName);
                // OURS or THEIRS. Every shader this mod bundles is namespaced GloomhavenVR/, so
                // the test is exact rather than a name guess.
                if (shName.StartsWith("GloomhavenVR/", System.StringComparison.Ordinal))
                    RMine[k] = true;
            }
            RShaders[k] = names.ToString();
        }
    }

    /// <summary>One frame of the window. No allocation: every buffer is static and the renderer
    /// set was resolved when the window armed.</summary>
    private static void SampleVerdict(Belt b)
    {
        if (b.Visual == null)
            return;
        _vFrames++;

        Renderer? lead = null;
        for (int i = 0; i < _rCount; i++)
        {
            int src = RSrc[i];
            Renderer r = src >= 0 && src < _vRenderers.Length ? _vRenderers[src] : null!;
            if (r == null)
            {
                if (RDeadAtFrame[i] < 0)
                    RDeadAtFrame[i] = _vFrames;
                continue;
            }
            if (r.enabled && r.gameObject.activeInHierarchy)
            {
                RDrawFrames[i]++;
                lead ??= r;
            }
            if (r.isVisible)
                RVisFrames[i]++;
        }

        _heldLead = lead;

        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null && lead != null)
        {
            Vector3 toProp = lead.bounds.center - head.transform.position;
            float dist = toProp.magnitude;
            if (dist < _poseDistLo) _poseDistLo = dist;
            if (dist > _poseDistHi) _poseDistHi = dist;
            if (dist > 1e-4f)
            {
                float ang = Vector3.Angle(lead.transform.forward, -toProp.normalized);
                if (ang < _poseAngLo) _poseAngLo = ang;
                if (ang > _poseAngHi) _poseAngHi = ang;
            }
        }

        VTable.Sample(VNow, VNowValid);
        // The twin is read HERE, immediately after VNow, so both sides of the comparison are the
        // same tick — every prop of a kind is in phase with every other, so a comparison taken a
        // frame apart would compare two different points of the same flash.
        SampleTwin();

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
            EmitHomeTwin(b, why);
    }

    /// <summary>Append the renderer roster and the pose control — the identity half, which has not
    /// answered, and the control that makes a difference readable.</summary>
    private static void AppendRoster(System.Text.StringBuilder sb)
    {
        sb.Append("ROSTER — EVERY RENDERER UNDER THE PROP BY HIERARCHY PATH, WITH ITS MATERIALS "
                  + "AND SHADERS: ").Append(_rCount).Append(" named of ").Append(_rFound)
          .Append(" sampled. ");
        for (int i = 0; i < _rCount; i++)
        {
            sb.Append('[').Append(i).Append("] '").Append(RName[i]).Append("' ")
              .Append(RMine[i]
                  ? "MOD-OWNED (a GloomhavenVR/ shader — this renderer is OURS)"
                  : "game-owned")
              .Append(", materials ").Append(RShaders[i].Length == 0 ? "<none>" : RShaders[i])
              .Append(", DRAWING on ").Append(RDrawFrames[i]).Append('/').Append(_vFrames)
              .Append(" frame(s), isVisible on ").Append(RVisFrames[i]);
            if (RDeadAtFrame[i] >= 0)
                sb.Append(", DESTROYED at frame ").Append(RDeadAtFrame[i]).Append(" of the window");
            sb.Append(". ");
        }
        sb.Append("A MOD-OWNED renderer with a non-zero DRAWING count is this mod painting the "
                  + "prop the player is holding — three incidents in this project's record are of "
                  + "that shape. THE POSE CONTROL: head-to-prop distance ranged ");
        if (_poseDistLo > _poseDistHi)
            sb.Append("<never sampled>");
        else
            sb.Append(_poseDistLo.ToString("0.###")).Append("..")
              .Append(_poseDistHi.ToString("0.###")).Append(" wu");
        sb.Append("; the angle between the drawing renderer's forward axis and the view ray ranged ");
        if (_poseAngLo > _poseAngHi)
            sb.Append("<never sampled>");
        else
            sb.Append(_poseAngLo.ToString("0.#")).Append("..").Append(_poseAngHi.ToString("0.#"))
              .Append(" deg");
        sb.Append(". A WIDE angle sweep with every value below flat means the decay in the user's "
                  + "video is the object TURNING and not anything this process writes — that is "
                  + "what ModBuild 454 read, and it is why a comparison replaced a series. ");
    }

    /// <summary>
    /// Say what the restore handed back and — the part that matters — whether it handed anything
    /// back OVER somebody else's write. This project has a recorded incident in which a hide saved
    /// a foreign mid-animation value and restored garbage over another system's restore, so
    /// "the restore is exact" is not left as a claim in a comment: <see cref="Restore"/> compares
    /// what is there against what this class LEFT there before writing the remembered value, and
    /// the three counts below are all expected to read 0.
    /// </summary>
    private static void AnnounceRestore(Belt b)
    {
        if (_restoreLogsLeft <= 0 || Spent(RestoreKindsDone, b.Label))
            return;
        _restoreLogsLeft--;
        if (RestoreKindsDone.Count < KindCap)
            RestoreKindsDone.Add(b.Label);

        var sb = new System.Text.StringBuilder(768);
        sb.Append("[Props] HELD-PROP HUSH RESTORE for ").Append(b.Label).Append(" — handed back ")
          .Append(b.Animators.Count).Append(" animator(s), ").Append(b.Outlines.Count)
          .Append(" outline(s), ").Append(b.Emitters.Count)
          .Append(" emitter(s) (Light/Projector/LensFlare and the ObjectOcclusionVolume "
                  + "registration, which share one ledger because 'switch a Behaviour off, "
                  + "remember what it was, write it back' is ONE restore and a second copy is a "
                  + "second place for the next fix to land on only one of), ").Append(b.Skins.Count)
          .Append(" skin(s). THE FALSIFIER, AND ALL THREE MUST READ 0: ")
          .Append(b.RestoreForeignAnimators)
          .Append(" animator(s) were found ENABLED although this class had switched them off, ")
          .Append(b.RestoreForeignEmitters)
          .Append(" emitter(s) likewise, and ").Append(b.RestoreDead)
          .Append(" object(s) under the ledger had been destroyed. A non-zero count says the prop "
                  + "did not come back exactly as the game left it, which is the recorded failure "
                  + "where a hide saved a foreign mid-animation value and restored garbage over "
                  + "another system's restore. ").Append(_restoreLogsLeft)
          .Append(" more restore line(s) this session, at most one per prop kind.");

        // HW-VERIFY: the standing requirement on this feature is that a prop put back down looks
        // exactly as it did before it was picked up, and these three counts are the only evidence
        // for it. It must stay at a tier the DEFAULT log level prints (Note/Alert/Error) —
        // scripts/check-hw-verify.py enforces the position of this marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>
    /// THE LINE THIS CLASS EXISTS TO PRINT: what the held prop is showing that a prop of the same
    /// kind on its hex is not, read on the same tick.
    /// </summary>
    private static void EmitHomeTwin(Belt b, string why)
    {
        var sb = new System.Text.StringBuilder(3072);
        sb.Append("[Props] HELD-PROP HOME TWIN for ").Append(_vLabel).Append(" — ").Append(_vFrames)
          .Append(" frame(s), closed because ").Append(why)
          .Append(". WHY: the user named the trigger — \"das weisse in der Hand tritt immer auf "
                  + "wenn ich die Falle aufhebe kurz nachdem der weisse flash auf ALLEN Fallen "
                  + "kam\" — so the white is a value LATCHED AT THE GRAB, and every instrument this "
                  + "file has ever shipped asks whether something MOVES rather than whether the "
                  + "value is the RIGHT one. A latched wrong value is constant, which is what all "
                  + "of them printed. This compares the held prop's whole property table against "
                  + "another instance of the same kind still on its hex, ON THE SAME TICK. ");

        sb.Append("GRAB PHASE (the animator's own normalizedTime on the frame this class froze it, "
                  + "which is the axis the user's sentence is about): ");
        if (b.GrabPhaseCount == 0)
            sb.Append("<no animator was frozen on this prop, so there was no phase to record>");
        else
            sb.Append(b.GrabPhaseCount).Append(" animator(s), first at normalizedTime ")
              .Append(b.GrabPhase.ToString("0.####")).Append(" (fraction of the loop ")
              .Append((b.GrabPhase - Mathf.Floor(b.GrabPhase)).ToString("0.###"))
              .Append("). Correlate this number ACROSS SESSIONS with whether the user reports "
                      + "white: a defect that clusters in one band of the loop is the latch, and "
                      + "one that does not is not.");
        sb.Append(' ');

        AppendRoster(sb);

        if (_twinLead == null)
        {
            sb.Append("NO HOME TWIN WAS FOUND. ").Append(_twinScanned)
              .Append(" renderer(s) scanned, ").Append(_twinCandidates)
              .Append(" matched the held prop's object name AND shader but none was usable (either "
                      + "there is only one instance of this kind in the scenario, or every other "
                      + "one is itself in a hand and therefore hushed and latched the same way). "
                      + "THIS IS A POPULATION FACT AND NOT A NULL READING: the comparison was not "
                      + "taken, so nothing here excludes anything. ");
            AppendTwinRewindState(sb, b);
            // HW-VERIFY: this branch says the comparison could NOT be taken, which must never be
            // mistaken for a clean reading. It must stay at a tier the DEFAULT log level prints.
            VRLog.Note("FigureGrab", sb.ToString());
            return;
        }

        sb.Append("HOME TWIN: '").Append(_twinPath).Append("', found among ").Append(_twinCandidates)
          .Append(" candidate(s) of ").Append(_twinScanned)
          .Append(" renderer(s) scanned, alive on ").Append(_twinFramesAlive)
          .Append(" frame(s) and drawing on ").Append(_twinFramesDrawing).Append(". ");

        sb.Append("SHARED OR INSTANCED, and this settles a tension nobody could resolve by "
                  + "argument: ").Append(_twinSharedMats)
          .Append(" of the held prop's material(s) are THE SAME ASSET as the twin's and ")
          .Append(_twinInstancedMats)
          .Append(" are per-prop instances. READ IT WITH THE MOVER COUNT BELOW: the game assigns "
                  + "props their materials with Renderer.sharedMaterials from an Addressables "
                  + "handle (decompiled GH.Runtime/MaterialLoaderData.cs:71), so SHARED is the "
                  + "expected answer — and a SHARED material whose table never moves while the "
                  + "user sees every trap flash at once means the flash is NOT a material property "
                  + "write at all, because such a write would land on every trap including this "
                  + "one. That is an exclusion, not a null reading. ");

        sb.Append("THE TWIN'S ANIMATOR: advancing on ").Append(_twinAnimAdvancing).Append(" of ")
          .Append(_twinAnimFrames).Append(" sampled frame(s)");
        if (_twinPhaseLo <= _twinPhaseHi)
            sb.Append(", loop fraction swept ").Append(_twinPhaseLo.ToString("0.###")).Append("..")
              .Append(_twinPhaseHi.ToString("0.###"));
        sb.Append(". ");

        AppendTwinMovers(sb);
        AppendTwinDiffs(sb);
        AppendTwinGraph(sb);
        AppendLightingCompare(sb);
        AppendTwinRewindState(sb, b);

        sb.Append("HOW TO READ IT. IT NAMES THE CHANNEL if any slot DIFFERS between held and home "
                  + "while the twin's own value MOVES on that same slot: that slot is the flash, "
                  + "the held prop is stuck at one point of it, and the home range printed beside "
                  + "it gives the value a fix must write. IT EXCLUDES THE WHOLE MATERIAL CLASS if "
                  + "NOTHING on the unheld twin moves either — the flash the user sees on every "
                  + "trap at once is then not a material property, and the next place to look is a "
                  + "renderer or child GameObject being ENABLED (an animator can drive m_IsActive "
                  + "and m_Enabled, which is what the rewind's other half above measures). IT IS "
                  + "INERT if no twin was found. NOTE THE TRAP IN THE OBVIOUS FIX: every trap is "
                  + "in PHASE, so at the instant of a grab the twin is bright too — copying the "
                  + "twin's CURRENT value would copy the flash. The value a fix must write is the "
                  + "twin's RESTING value, which is the end of the home range the held prop is NOT "
                  + "stuck at. ").Append(_verdictsLeft)
          .Append(" more comparison(s) this session, at most one per prop kind.");

        // HW-VERIFY: this is the only reading in this file that asks whether the held prop's value
        // is the RIGHT one rather than whether it MOVED, and it carries the value a remedy would
        // have to write. It must stay at a tier the DEFAULT log level prints (Note/Alert/Error) —
        // scripts/check-hw-verify.py enforces the position of this marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }




    // ---- ROUND TEN: THE TWIN'S OBJECT GRAPH, AND THE LIGHTING COMPARISON ----------------------------
    //
    // WHAT ModBuild 455 SETTLED, AND IT KILLED TWO ASSUMPTIONS THAT HAD BEEN LOAD-BEARING SINCE
    // ROUND SEVEN. Over 194 frames: the twin's material is THE SAME ASSET as the held prop's
    // (1 shared, 0 instanced), its animator ran a FULL loop (advancing 193 of 194, loop fraction
    // 0.001..0.997), and across that whole loop **0 of 57 material slots moved** while **0 slots
    // ever differed** between held and home.
    //
    //   1. THE WHOLE MATERIAL CLASS IS OUT. A shared material whose table never moves cannot carry
    //      a flash, and the held prop's material state is byte-for-byte a board prop's.
    //   2. **THE ~5 s IDLE LOOP IS NOT THE FLASH.** It advances through its entire range while
    //      nothing about the material changes, so it is driving BONES on a SkinnedMeshRenderer —
    //      a bear trap's jaws — not a brightness. Four rounds treated `clipRate 0.202/s` as the
    //      flash's clock. It is not. Whatever flashes every trap at once has a different cause and
    //      possibly a different period, and NO ROUND HAS EVER MEASURED THAT PERIOD.
    //
    // SO THIS ARM WATCHES THE OBJECT GRAPH OF A PROP THAT IS *NOT* HELD. If the flash is authored
    // as "switch the glow mesh on", the twin is the one place it is visible and the hushed prop in
    // the hand is precisely where it is not — and the previous build measured that class only on
    // the HELD prop, as a two-sample read across the rewind, on an object that was already frozen.
    // Per frame, on the twin: every renderer's enabled / activeInHierarchy / activeSelf, every
    // child object's activeSelf, the renderer and node COUNTS (so an object instantiated on the
    // flash is caught as well as one toggled), and each renderer's material count and material
    // instance id (a swapped sharedMaterials entry changes nothing in a property table). Every
    // change is named with its frame AND its timestamp, so the flash's own clock can be read off
    // the line instead of assumed.
    private const int TwinNodeCap = 40;
    private const int TwinRendCap = 16;
    private const int TwinEventCap = 24;

    private static readonly Transform[] TwNodes = new Transform[TwinNodeCap];
    private static readonly string[] TwNodeName = new string[TwinNodeCap];
    private static readonly bool[] TwNodeActive0 = new bool[TwinNodeCap];
    private static int _twNodeCount, _twNodeFound0, _twNodeCountMin, _twNodeCountMax;

    private static readonly Renderer[] TwRends = new Renderer[TwinRendCap];
    private static readonly string[] TwRendName = new string[TwinRendCap];
    private static readonly bool[] TwRendOn0 = new bool[TwinRendCap];
    private static readonly bool[] TwRendActive0 = new bool[TwinRendCap];
    private static readonly int[] TwRendMatCount0 = new int[TwinRendCap];
    private static readonly int[] TwRendMatId0 = new int[TwinRendCap];
    private static int _twRendCount, _twRendFound0, _twRendCountMin, _twRendCountMax;

    private static readonly List<string> TwEvents = new(TwinEventCap);
    private static int _twEventsTotal;
    private static float _twinT0;

    private static readonly List<Transform> TwNodeScratch = new(TwinNodeCap);
    private static readonly List<Renderer> TwRendScratch = new(TwinRendCap);

    /// <summary>The held prop's first DRAWING renderer this frame, so the lighting comparison can
    /// read both sides on the same tick.</summary>
    private static Renderer? _heldLead;

    // ---- the lighting comparison, DELIBERATELY RE-ADDED after the 2026-09-06 cleanup -------------
    //
    // The arm deleted there read the light probe DURING THE HOLD ONLY and reported it flat. **Flat
    // is not the same as CORRECT.** A prop carried to the eye leaves the probe volume it was
    // authored inside, and a constant-but-wrong ambient is exactly the shape every instrument in
    // this file is blind to — the same blindness §14.2 named for material values. Read as a
    // COMPARISON against the twin on the same tick it is a different measurement from the one that
    // was retired, and it costs one more column on a comparison that already exists.
    private const int LightProbeEvery = 5;
    private static int _litProbeAt, _litSamples;
    private static float _litHeldLo, _litHeldHi, _litTwinLo, _litTwinHi;
    private static float _litHeldLast, _litTwinLast, _litWorstDiff;
    private static int _reflHeldMax, _reflTwinMax;
    private static string _probeUsage = string.Empty;
    private static readonly List<UnityEngine.Rendering.ReflectionProbeBlendInfo> ReflScratch = new(4);

    /// <summary>Snapshot the twin's object graph when the window arms. Everything after this is a
    /// comparison against these values.</summary>
    private static void ArmTwinGraph()
    {
        _twNodeCount = _twRendCount = 0;
        _twNodeFound0 = _twRendFound0 = 0;
        _twEventsTotal = 0;
        TwEvents.Clear();
        _twinT0 = Time.unscaledTime;
        _litProbeAt = 0;
        _litSamples = 0;
        _litHeldLo = _litTwinLo = float.MaxValue;
        _litHeldHi = _litTwinHi = float.MinValue;
        _litHeldLast = _litTwinLast = float.NaN;
        _litWorstDiff = 0f;
        _reflHeldMax = _reflTwinMax = 0;
        _probeUsage = string.Empty;
        for (int i = 0; i < TwinNodeCap; i++)
            TwNodes[i] = null!;
        for (int i = 0; i < TwinRendCap; i++)
            TwRends[i] = null!;
        if (_twinRoot == null)
        {
            _twNodeCountMin = _twNodeCountMax = _twRendCountMin = _twRendCountMax = 0;
            return;
        }

        TwNodeScratch.Clear();
        _twinRoot.GetComponentsInChildren(includeInactive: true, TwNodeScratch);
        _twNodeFound0 = TwNodeScratch.Count;
        for (int i = 0; i < TwNodeScratch.Count && _twNodeCount < TwinNodeCap; i++)
        {
            Transform t = TwNodeScratch[i];
            if (t == null)
                continue;
            TwNodes[_twNodeCount] = t;
            TwNodeName[_twNodeCount] = t.name;
            TwNodeActive0[_twNodeCount] = t.gameObject.activeSelf;
            _twNodeCount++;
        }
        TwNodeScratch.Clear();

        TwRendScratch.Clear();
        _twinRoot.GetComponentsInChildren(includeInactive: true, TwRendScratch);
        _twRendFound0 = TwRendScratch.Count;
        for (int i = 0; i < TwRendScratch.Count && _twRendCount < TwinRendCap; i++)
        {
            Renderer r = TwRendScratch[i];
            if (r == null)
                continue;
            TwRends[_twRendCount] = r;
            TwRendName[_twRendCount] = r.gameObject.name + " [" + r.GetType().Name + "]";
            TwRendOn0[_twRendCount] = r.enabled;
            TwRendActive0[_twRendCount] = r.gameObject.activeInHierarchy;
            Material[] mats = r.sharedMaterials;
            TwRendMatCount0[_twRendCount] = mats.Length;
            TwRendMatId0[_twRendCount] = mats.Length > 0 && mats[0] != null ? mats[0].GetInstanceID() : 0;
            _twRendCount++;
        }
        TwRendScratch.Clear();

        _twNodeCountMin = _twNodeCountMax = _twNodeFound0;
        _twRendCountMin = _twRendCountMax = _twRendFound0;
    }

    /// <summary>Record one change, with the frame AND the clock, so a period can be read off the
    /// line rather than assumed. Capped, with the total kept beside it — a truncated list is not an
    /// absence.</summary>
    private static void TwEvent(string what)
    {
        _twEventsTotal++;
        if (TwEvents.Count >= TwinEventCap)
            return;
        TwEvents.Add($"[frame {_vFrames}, t+{(Time.unscaledTime - _twinT0):0.00}s, "
                     + $"abs {Time.unscaledTime:0.00}] {what}");
    }

    /// <summary>One frame of the twin's object graph. Allocation-free unless something actually
    /// changed, which is the point: a quiet prop costs two component walks over a subtree of a
    /// dozen nodes.</summary>
    private static void SampleTwinGraph()
    {
        if (_twinRoot == null)
            return;

        TwNodeScratch.Clear();
        _twinRoot.GetComponentsInChildren(includeInactive: true, TwNodeScratch);
        int nodes = TwNodeScratch.Count;
        TwNodeScratch.Clear();
        if (nodes < _twNodeCountMin)
        {
            _twNodeCountMin = nodes;
            TwEvent($"child object COUNT fell to {nodes} (was {_twNodeFound0} at arm)");
        }
        if (nodes > _twNodeCountMax)
        {
            _twNodeCountMax = nodes;
            TwEvent($"child object COUNT rose to {nodes} (was {_twNodeFound0} at arm) — something "
                    + "was INSTANTIATED under this prop");
        }

        TwRendScratch.Clear();
        _twinRoot.GetComponentsInChildren(includeInactive: true, TwRendScratch);
        int rends = TwRendScratch.Count;
        TwRendScratch.Clear();
        if (rends < _twRendCountMin)
        {
            _twRendCountMin = rends;
            TwEvent($"renderer COUNT fell to {rends} (was {_twRendFound0} at arm)");
        }
        if (rends > _twRendCountMax)
        {
            _twRendCountMax = rends;
            TwEvent($"renderer COUNT rose to {rends} (was {_twRendFound0} at arm) — a new RENDERER "
                    + "appeared under this prop");
        }

        for (int i = 0; i < _twNodeCount; i++)
        {
            Transform t = TwNodes[i];
            if (t == null)
                continue;
            bool active = t.gameObject.activeSelf;
            if (active == TwNodeActive0[i])
                continue;
            TwNodeActive0[i] = active;
            TwEvent($"'{TwNodeName[i]}'.activeSelf → {(active ? "TRUE" : "false")}");
        }

        for (int i = 0; i < _twRendCount; i++)
        {
            Renderer r = TwRends[i];
            if (r == null)
                continue;
            bool on = r.enabled;
            if (on != TwRendOn0[i])
            {
                TwRendOn0[i] = on;
                TwEvent($"'{TwRendName[i]}'.enabled → {(on ? "TRUE" : "false")}");
            }
            bool active = r.gameObject.activeInHierarchy;
            if (active != TwRendActive0[i])
            {
                TwRendActive0[i] = active;
                TwEvent($"'{TwRendName[i]}'.activeInHierarchy → {(active ? "TRUE" : "false")}");
            }
            Material[] mats = r.sharedMaterials;
            if (mats.Length != TwRendMatCount0[i])
            {
                TwEvent($"'{TwRendName[i]}' material COUNT {TwRendMatCount0[i]} → {mats.Length}");
                TwRendMatCount0[i] = mats.Length;
            }
            int id = mats.Length > 0 && mats[0] != null ? mats[0].GetInstanceID() : 0;
            if (id == TwRendMatId0[i])
                continue;
            TwEvent($"'{TwRendName[i]}' material 0 SWAPPED, instance id {TwRendMatId0[i]} → {id} — "
                    + "a swapped material changes nothing in a property table and is invisible to "
                    + "every read-back in this file");
            TwRendMatId0[i] = id;
        }
    }

    /// <summary>
    /// The per-position lighting the held prop receives against the one a prop of the same kind
    /// receives on its hex, ON THE SAME TICK. Flat is not the same as correct.
    /// </summary>
    private static void SampleLightingCompare()
    {
        if (Time.frameCount < _litProbeAt || _heldLead == null || _twinLead == null)
            return;
        _litProbeAt = Time.frameCount + LightProbeEvery;
        _litSamples++;
        if (_probeUsage.Length == 0)
            _probeUsage = _heldLead.lightProbeUsage + "/" + _heldLead.reflectionProbeUsage
                          + " held vs " + _twinLead.lightProbeUsage + "/"
                          + _twinLead.reflectionProbeUsage + " home";

        float held = ProbeLuminance(_heldLead);
        float twin = ProbeLuminance(_twinLead);
        _litHeldLast = held;
        _litTwinLast = twin;
        if (held < _litHeldLo) _litHeldLo = held;
        if (held > _litHeldHi) _litHeldHi = held;
        if (twin < _litTwinLo) _litTwinLo = twin;
        if (twin > _litTwinHi) _litTwinHi = twin;
        float diff = Mathf.Abs(held - twin);
        if (diff > _litWorstDiff)
            _litWorstDiff = diff;

        int heldProbes = CountReflectionProbes(_heldLead);
        int twinProbes = CountReflectionProbes(_twinLead);
        if (heldProbes > _reflHeldMax) _reflHeldMax = heldProbes;
        if (twinProbes > _reflTwinMax) _reflTwinMax = twinProbes;
    }

    /// <summary>Rec.709 luminance of the interpolated light probe's L0 (constant) band at a
    /// renderer's bounds centre — the ambient that surface actually receives, as one number.</summary>
    private static float ProbeLuminance(Renderer r)
    {
        LightProbes.GetInterpolatedProbe(r.bounds.center, r,
            out UnityEngine.Rendering.SphericalHarmonicsL2 sh);
        return (0.2126f * sh[0, 0]) + (0.7152f * sh[1, 0]) + (0.0722f * sh[2, 0]);
    }

    private static int CountReflectionProbes(Renderer r)
    {
        ReflScratch.Clear();
        r.GetClosestReflectionProbes(ReflScratch);
        int n = ReflScratch.Count;
        ReflScratch.Clear();
        return n;
    }

    /// <summary>The object-graph verdict: what changed on a prop that is NOT held, over a full
    /// loop, and when.</summary>
    private static void AppendTwinGraph(System.Text.StringBuilder sb)
    {
        sb.Append("THE TWIN'S OBJECT GRAPH — THE ONE CLASS NEVER MEASURED ON AN UNHELD PROP. "
                  + "ModBuild 455 excluded the whole material class (a SHARED material, 0 of 57 "
                  + "slots moving across a FULL loop, 0 slots ever differing), which also retires "
                  + "the assumption that the ~5 s idle clip IS the flash: it sweeps its entire "
                  + "range while nothing about the material changes, so it drives BONES — a bear "
                  + "trap's jaws — and not a brightness. If the flash is 'switch the glow mesh "
                  + "on', THIS is where it shows and a hushed prop in a hand is exactly where it "
                  + "does not. AT ARM: ").Append(_twNodeFound0).Append(" child object(s) (")
          .Append(_twNodeCount).Append(" tracked by identity) and ").Append(_twRendFound0)
          .Append(" renderer(s) (").Append(_twRendCount).Append(" tracked). COUNTS OVER THE WINDOW: "
                  + "objects ").Append(_twNodeCountMin).Append("..").Append(_twNodeCountMax)
          .Append(", renderers ").Append(_twRendCountMin).Append("..").Append(_twRendCountMax)
          .Append(". CHANGES: ").Append(_twEventsTotal);
        if (_twEventsTotal == 0)
        {
            sb.Append(" — NOTHING under an unheld prop of this kind was enabled, disabled, "
                      + "activated, deactivated, instantiated, destroyed or re-materialled on any "
                      + "sampled frame, across a full loop of its own animator. TAKEN WITH THE "
                      + "MATERIAL EXCLUSION ABOVE, NOTHING ABOUT THIS PROP'S OWN OBJECT GRAPH "
                      + "FLASHES, and the next round must measure the PICTURE rather than the "
                      + "state — a sampled read-back of the rendered pixels over the prop, held "
                      + "versus home. Do not invent a tenth state probe. ");
            return;
        }
        sb.Append(", naming up to ").Append(TwinEventCap).Append(" IN ORDER, each with its frame "
                  + "and its clock so the flash's OWN PERIOD can be read off this line instead of "
                  + "assumed (the ~5 s figure four rounds used is the idle clip's rate and is now "
                  + "known not to be the flash's): ").Append(string.Join("; ", TwEvents));
        if (_twEventsTotal > TwEvents.Count)
            sb.Append(", and ").Append(_twEventsTotal - TwEvents.Count)
              .Append(" more counted but not named");
        sb.Append(". THE INTERVAL BETWEEN REPEATS OF THE SAME EVENT IS THE FLASH'S PERIOD. Compare "
                  + "the absolute timestamps against another prop kind's line in the same session "
                  + "to say whether the props change SIMULTANEOUSLY, which is what the user's "
                  + "'auf allen Fallen' claims. ");
    }

    /// <summary>The lighting comparison: the same reading on both props, on the same tick.</summary>
    private static void AppendLightingCompare(System.Text.StringBuilder sb)
    {
        sb.Append("LIGHTING, HELD vs HOME ON THE SAME TICK — and this arm was deliberately "
                  + "RE-ADDED after being deleted, because the deleted one read the probe DURING "
                  + "THE HOLD ONLY and reported it flat, and FLAT IS NOT THE SAME AS CORRECT. A "
                  + "prop carried to the eye leaves the probe volume it was authored inside, and a "
                  + "constant-but-WRONG ambient is invisible to every 'did it move' reading in "
                  + "this file. ").Append(_litSamples).Append(" sample(s). ");
        if (_litSamples == 0 || float.IsNaN(_litHeldLast))
        {
            sb.Append("NOT TAKEN — no drawing renderer on one side or the other, so this excludes "
                      + "nothing. ");
            return;
        }
        sb.Append("INTERPOLATED LIGHT PROBE, Rec.709 luminance of the L0 band: HELD last ")
          .Append(_litHeldLast.ToString("0.####")).Append(" over ")
          .Append(_litHeldLo.ToString("0.####")).Append("..").Append(_litHeldHi.ToString("0.####"))
          .Append("; HOME last ").Append(_litTwinLast.ToString("0.####")).Append(" over ")
          .Append(_litTwinLo.ToString("0.####")).Append("..").Append(_litTwinHi.ToString("0.####"))
          .Append("; WORST DIFFERENCE on any sampled tick ").Append(_litWorstDiff.ToString("0.####"))
          .Append(". REFLECTION PROBES influencing the renderer: HELD at most ").Append(_reflHeldMax)
          .Append(", HOME at most ").Append(_reflTwinMax).Append(" (usage ")
          .Append(_probeUsage.Length == 0 ? "<unsampled>" : _probeUsage)
          .Append("). READ IT LIKE THIS: a LARGE worst-difference, or a HOME probe count above a "
                  + "HELD count of 0, says the prop in the hand is lit by something the same prop "
                  + "on the board is not — the ivory is then LIGHTING and not paint, which is a "
                  + "different fix again and one no round has costed. A difference near zero says "
                  + "both props receive the same ambient and this arm excludes lighting too. ");
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
}
