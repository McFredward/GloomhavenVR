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
/// <para>SECOND USER REPORT (hardware test, ModBuild 283, verbatim): "Ich habe in meinem neusten
/// Test einen weiteres Performanceproblem festgestellt: Wenn ich die Figuren in meiner Hand größer
/// skaliere hat es immer angefangen zu hängen bzw. hatte ich kleinere Hänger. Versuch das
/// nachzuvollziehen. Logs liegen ab." — that report is about THIS file, and the mechanism is in
/// the PERFORMANCE section below. The size correction itself is not the problem and is preserved
/// exactly; what it used to be BOUGHT WITH is.</para>
///
/// <para>ROOT CAUSE OF THE SIZE DEFECT — PROVEN FROM THE DECOMPILED GAME. Gloomhaven's character
/// models carry Unity's native <c>Cloth</c> component (PhysX cloth); there is no custom
/// spring/jiggle/dynamic-bone script anywhere in the game. <c>ActorBehaviour.SetActor</c>
/// (decompiled/GH.Runtime/ActorBehaviour.cs:122) caches them ONCE, at actor creation:
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
/// baked against the mesh at AUTHORING scale. They were seeded when the actor spawned, at the
/// figure's board size, and the mod's rescale happens strictly later (grab → stretch gesture →
/// glide home), so the cape ends up simulating against a body it no longer fits: it renders at the
/// new size (it is still skinned) while its freedom of movement, its collision offsets and its
/// settling stay sized for the old body — "verändert zwar auch ihre größe aber nicht richtig mit
/// der Figur mit".</para>
///
/// <para>MEASURED, not asserted: that defect is real and this is its size. In a Unity 2021.3.5f1
/// standalone player, a 41×41 cloth sheet with an authored 0.05 m slack, left simulating with its
/// coefficients untouched while its root transform was scaled 1.345×, wandered up to 0.080 away
/// from where a DISABLED cloth on the same mesh in the same frame put the vertex — about 1.2× the
/// rescaled slack (0.0673), and oscillating rather than settling (0.40827 / 0.54187 / 0.52367 /
/// 0.41744 at +1/+2/+5/+15 frames against a flat 0.48790 for the disabled arm). See PERFORMANCE
/// below for the harness.</para>
///
/// <para>THE FIX. While the mod is CHANGING a figure's size the cloth simulation is PINNED: every
/// managed cloth's per-vertex <c>maxDistance</c> is driven to 0, which forces each particle onto
/// its skinned position, so the cape is a plain skinned mesh again — and a skinned mesh scales with
/// the root EXACTLY, which is literally what the user asked for ("Alle Teile der Figur sollen
/// korrekt mitskallieren"). When the size SETTLES (a few quiet frames — the stretch gesture and the
/// remote ease both asymptote, hence the RELATIVE epsilon) the pin is released back to
/// <c>pristine × factor</c>, so the re-seeded cloth hangs and swings in proportion to the size the
/// figure is actually at. Both directions are RAMPED over <see cref="FadeSeconds"/> rather than
/// snapped, because rule 5 ("everything moves WITH the animation, popping is unacceptable") applies
/// to a figure the player is holding in front of their face. Returning to the board size restores
/// the pristine array verbatim — the scaling is always computed from the authored snapshot, never
/// incrementally, so it cannot drift.</para>
///
/// <para>=== PERFORMANCE: WHY THE PIN AND NOT <c>SetEnabledFading</c> (ModBuild 283 report) ===</para>
///
/// <para>Builds 137-283 suspended the simulation by DISABLING the component:
/// <c>SetEnabledFading(false, …)</c> on the way out, <c>coefficients = pristine × factor</c> plus
/// <c>SetEnabledFading(true, …)</c> on the way back in. In his ModBuild 283 log that resume is nine
/// logged spike frames of ~180 ms each — <c>FigureGrab.HeldSize 2.046ms avg, worst 172.93ms,
/// 139.3ms/s, frames 2044</c>, in the one 30 s window whose view height drops to 9.1 (he is working
/// close-in with a figure) and whose <c>p99</c> is 178.39 ms against 23.70 and 24.52 in the windows
/// either side. Nine is only what was PRINTED: that window logged <c>spikes 104 (49 lines
/// rate-limited)</c>, and p99 = 178.39 over n = 2044 means about twenty frames sat above 178 ms.
/// Twenty × 171 ms is 3.4 s of the step's 4.18 s window total, which is the whole of it.</para>
///
/// <para>WHICH CALL COST IT — MEASURED, because the API docs do not say and this project has lost
/// rounds to instruments that were reasoned at instead of run. A Unity 2021.3.5f1 Linux standalone
/// player (the same editor the bundle is built with) timed each call on its own frame, with a NULL
/// control (an empty timed body) and a known-positive control (<c>AddComponent&lt;Cloth&gt;</c>,
/// which must cook the fabric from scratch). At 3721 cloth vertices:
/// <list type="bullet">
/// <item>NULL control ............................. 0.0001 ms  (the instrument floor)</item>
/// <item><c>coefficients = next</c> (enabled) ..... 0.0069 ms</item>
/// <item><c>coefficients = next</c> (disabled) .... 0.0013 ms</item>
/// <item><c>SetEnabledFading(false, .12)</c> ...... 0.0008 ms</item>
/// <item><c>enabled = false</c> .................. 0.0023 ms</item>
/// <item><c>ClearTransformMotion()</c> ............ 0.0006 ms</item>
/// <item><c>SetEnabledFading(TRUE, .12)</c> ...... 19.37 ms   ← the whole cost</item>
/// <item>known-positive: first cook .............. 19.2-20.3 ms</item>
/// </list>
/// It scales with vertex count — 2.8 / 8.0 / 18.6 / 34.7 ms at 441 / 1681 / 3721 / 6561 — i.e.
/// ~5.3 µs per cloth vertex, which puts his ~57 ms-per-cloth at roughly eleven thousand. The
/// mechanism is now named rather than guessed: <c>SetEnabledFading(false, …)</c> lets the component
/// go <c>enabled = false</c> on its own once the blend finishes (measured: the component reads
/// False 40 frames later without anyone writing it), so <c>SetEnabledFading(true, …)</c> is an
/// ENABLE TRANSITION and PhysX re-cooks the fabric on the main thread. The
/// <c>if (!c.enabled) c.enabled = true;</c> backstop that used to follow it measured 0.0031 ms —
/// it looked innocent only because the line above it had already paid.</para>
///
/// <para>THE PIN COSTS NOTHING AND IS THE SAME PICTURE. Same harness, four arms, all at the same
/// authored slack, all scaled 1.345× and sampled at +1/+2/+5/+15/+45/+90 frames, drift measured
/// against the authored mesh vertices (which for one identity bone ARE the skinned positions):
/// <list type="bullet">
/// <item>A cloth DISABLED across the scale (the shipped behaviour): 0.48790 at every sample</item>
/// <item>B cloth PINNED, <c>maxDistance = 0</c>: 0.48788 / 0.48793 / 0.48790 / 0.48790 / … </item>
/// <item>C as B plus <c>ClearTransformMotion()</c>: identical to B</item>
/// <item>D cloth SIMULATING, coefficients untouched: 0.40827 / 0.54187 / 0.52367 / 0.41744 / … </item>
/// </list>
/// A and B agree to five decimal places and D does not — so a pinned cloth is indistinguishable
/// from a disabled one across a transform scale, which is the entire property the disable was
/// bought for, and it costs a coefficients write instead of a fabric cook. The pin engages in ONE
/// frame (drift 0.04880 → 0.00121 at +1f, flat for the next 52), which is why it is RAMPED here
/// rather than written once.</para>
///
/// <para>THE RAMP IS AFFORDABLE, ALSO MEASURED. Rejected alternative (c) below used to say a
/// per-frame coefficients write was out of the question because the property "allocates and uploads
/// a per-vertex array on every get AND set". The upload is real; the cost estimate was not. With
/// the array REUSED (see <c>_scratch</c> per cloth) one full rebuild-and-upload frame measures
/// 0.028 / 0.059 / 0.104 ms at 1681 / 3721 / 6561 vertices — 0.084 / 0.177 / 0.312 ms per frame for
/// three cloths, for the ~11 frames of a 0.12 s ramp at 90 Hz. Against 19.37 ms in one frame, per
/// cloth, that is the trade this file now makes.</para>
///
/// <para>END TO END, THE THING ITSELF — because a call-by-call split is worst-case for one question
/// and best-case for the next, the algorithm in this file was transcribed into the same player and
/// run head to head against the one it replaces, on the same 3721-vertex cloth, driven by a
/// scripted stretch (exponential approach 1 → 1.345 over 45 frames, then 60 quiet), with a third
/// arm that simply DISABLES the cloth for the whole gesture as the definition of "correct":
/// <list type="table">
/// <item><term></term><description>....................... worst frame | total | uploads | ENABLE transitions | worst drift</description></item>
/// <item><term>REFERENCE (disabled)</term><description> .. 0.45 ms | 0.47 ms | 0 | 0 | 0.00000</description></item>
/// <item><term>OLD (137-283)</term><description> ......... 29.12 ms | 32.17 ms | 2 | 1 | 1.72018</description></item>
/// <item><term>NEW (this file)</term><description> ....... 0.97 ms | 3.77 ms | 27 | 0 | 0.06880</description></item>
/// </list>
/// The NEW arm's 0.97 ms is its COLD first upload; across the 43 frames it did any work at all the
/// distribution is p50 0.101 ms, p90 0.119 ms. It does thirteen times as many uploads and costs an
/// eighth as much in total, it causes ZERO enable transitions, and it holds the cape TWENTY-FIVE
/// TIMES closer to the skinned pose during the resize than the mechanism it replaces — the OLD
/// arm's 1.72 is the frame its re-cook lands on, where the cape is re-anchored in one step. Its
/// settled coefficients come out equal to <c>pristine × the factor that actually settled</c> to
/// within 1.0e-9. THE ABSOLUTE MILLISECONDS ARE A DESKTOP LINUX BOX, NOT HIS QUEST 3 OVER VIRTUAL
/// DESKTOP: read the RATIO, which is a property of the algorithm, and expect his numbers to be
/// larger on both sides. His capes are also bigger than this test's — 172.93 ms ÷ 3 cloths ÷
/// 5.3 µs/vertex puts them near eleven thousand, so scale both columns by about three.</para>
///
/// <para>WHAT WAS TRIED AND DOES NOT WORK, so it is not rediscovered: dropping only the
/// <c>enabled</c> writes and keeping <c>SetEnabledFading</c> in both directions. Measured at
/// 20.45 ms — unchanged — because the fade-out disables the component by itself and the fade-in is
/// still an enable transition. The <c>enabled</c> writes were never the cost; the FADE was.</para>
///
/// <para>UNITY'S "UNCONSTRAINED" SENTINEL IS <c>float.MaxValue</c>, NOT <c>Infinity</c>. Read back
/// from a freshly added Cloth in the same harness: every one of 1681 / 3721 / 6561 default
/// coefficients came back as 3.402823E+38 for both <c>maxDistance</c> and
/// <c>collisionSphereDistance</c>, and NONE as <c>Infinity</c>. Builds 137-283 guarded with
/// <c>float.IsInfinity(max) ? max : max * factor</c>, which therefore did not catch it and
/// multiplied <c>float.MaxValue</c> by the factor — overflowing to a real <c>+Infinity</c> on every
/// unpainted vertex. Both values mean "unconstrained" to the solver so nothing visibly broke, but
/// the guard was not doing what its comment said. <see cref="IsUnconstrained"/> now tests for both
/// and neither is ever multiplied.</para>
///
/// <para>CONFIDENCE, stated honestly (CHARTER: separate what is read from source from what is
/// inferred). PROVEN FROM SOURCE: the components exist and are Unity <c>Cloth</c>; the game caches
/// them at spawn and never re-seeds them; the game never scales figures. MEASURED IN A PLAYER, on
/// this Unity version, with controls: every millisecond and every drift number above. NOT MEASURED
/// AND HIS EYES ARE THE INSTRUMENT: how the ramp READS on a figure held at arm's length. It is a
/// constraint ramp, not the position-space blend <c>SetEnabledFading</c> did, and the two are not
/// the same animation even at the same 0.12 s. Nothing else in this file depends on that judgement
/// — if the ramp reads wrong the fix is <see cref="FadeSeconds"/>, not the mechanism.</para>
///
/// <para>REJECTED ALTERNATIVES.
/// (a) <c>ActorBehaviour.ForceSetLocoIntermediateTarget</c> does the disable/re-enable dance and is
/// public — but it also overwrites <c>m_LocoIntermediateTarget</c>, i.e. it would teleport the
/// figure's locomotion goal as a side effect of a cosmetic resize. Rejected: never drive game state
/// to get a presentation effect. (It would also buy the 19 ms cook.)
/// (b) Hard <c>enabled = false/true</c> without the fade: the game's own pattern, used for a
/// teleport where a pop is invisible anyway. Rejected before for the pop; rejected now for the cook
/// as well — <c>enabled = true</c> is the same enable transition.
/// (c) Rescaling the coefficients every frame while the gesture runs. REJECTED ON A COST ESTIMATE
/// THAT WAS WRONG (see THE RAMP IS AFFORDABLE) and now partly ADOPTED: the ramp frames do exactly
/// this. It is still not done for every frame of a long gesture — once the weight reaches its
/// target nothing is written at all — because there is no reason to, not because it is unaffordable.
/// (d) Doing nothing and letting the cloth "catch up": it cannot. Nothing in the game ever re-seeds
/// a Cloth after spawn (proven above), so the mismatch is permanent for the life of the figure.
/// (e) Re-seeding once per GESTURE instead of once per settle, or rate-limiting the re-seed. Both
/// bound how OFTEN the cost is paid and neither touches what ONE event costs, and one 172 ms frame
/// is already 15× the 11.11 ms budget. Rejected as a fix, unnecessary as a mitigation.</para>
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
/// <para>THE GAME'S OWN <c>enabled</c> WRITES ARE NOW HARMLESS AND ARE LEFT ALONE. Builds 137-283
/// carried a <c>HardDisable</c> backstop because <c>ActorBehaviour.LateUpdate</c>
/// (ActorBehaviour.cs:556) re-enables every <c>m_Clothes</c> entry after its forced-position window
/// and would otherwise switch the simulation back on mid-resize. This pass never disables anything,
/// so that write lands on an already-enabled component and changes nothing; and if the game
/// DISABLES a cloth first (ActorBehaviour.cs:249) our pin writes still land — a coefficients write
/// to a disabled cloth measured 0.0013 ms — and take effect when the game brings it back. The cook
/// the game pays there is the game's, on a frame it was already teleporting a figure.</para>
///
/// <para>MULTIPLAYER — UNCHANGED BY THIS PASS, AND DELIBERATELY SO. Figure size is not a wire field
/// of its own. The holder's side writes <c>heldLocalScale × stretch</c> (FigureGrabbable) and the
/// peer's side reconstructs the same size from the factor the holder MEASURED and sent on extension
/// record 30 (<c>NetFigures.EaseSlot</c>). Both machines call <see cref="Note"/> with the same
/// factor, and this pass did not touch <see cref="Note"/>'s contract, <see cref="FactorEpsilon"/>,
/// <see cref="SettleFrames"/> or anything else that decides WHEN a re-seed happens — only what a
/// re-seed DOES. So owner and peer still settle on the same frame-count rule from the same factor
/// stream, the cape is corrected identically on every client (the standing 1:1 ruling), and zero
/// wire bytes move. Strict no-op offline and for any figure nobody is resizing.</para>
/// </summary>
internal static class FigureCloth
{
    /// <summary>Quiet frames a size must hold before the simulation is released back to full. Three
    /// frames is ~33 ms at 90 Hz — under the perception threshold, and long enough that neither the
    /// stretch gesture's exponential smoothing nor the remote slot's Lerp can be mistaken for
    /// "settled" while it is still visibly moving.</summary>
    private const int SettleFrames = 3;

    /// <summary>Frames without a <see cref="Note"/> before a figure is forgotten. Well past
    /// <see cref="SettleFrames"/>, so a figure always finishes its release before it is pruned.</summary>
    private const int PruneFrames = 30;

    /// <summary>A size change worth reacting to, RELATIVE (0.2%). Both writers approach their target
    /// asymptotically (smoothing here, Lerp on the peer), so an absolute epsilon would either never
    /// settle or would settle while the figure was still visibly growing.</summary>
    private const float FactorEpsilon = 0.002f;

    /// <summary>Ramp time for the pin, in both directions. Was the blend time for
    /// <c>Cloth.SetEnabledFading</c>; kept at the same 0.12 s so the timing the player sees is
    /// unchanged even though the mechanism underneath is not.</summary>
    private const float FadeSeconds = 0.12f;

    /// <summary>Unity's "unconstrained" sentinel in a <see cref="ClothSkinningCoefficient"/> is
    /// <c>float.MaxValue</c> — MEASURED, see the class comment; the pre-284 <c>IsInfinity</c> guard
    /// did not catch it and overflowed it to a real infinity. <c>Infinity</c> is accepted too
    /// because an array that has already been through that overflow can hold one.</summary>
    private static bool IsUnconstrained(float v) => v >= float.MaxValue || float.IsInfinity(v);

    private sealed class Tracked
    {
        public GameObject Root = null!;

        /// <summary>The cloths this pass MANAGES: only those found ENABLED at capture. A cloth that
        /// was already disabled is not simulating, therefore already scales perfectly as a plain
        /// skinned mesh, and must not be pinned by us (it may be authored off, or the game may be
        /// inside its own forced-position window — ActorBehaviour.cs:249).</summary>
        public readonly List<Cloth> Cloths = new(2);

        /// <summary>Per managed cloth, the coefficient array exactly as authored, captured the first
        /// time that cloth was seen. Every write is computed from THIS, never from the live array,
        /// so repeated resizes cannot compound.</summary>
        public readonly List<ClothSkinningCoefficient[]> Pristine = new(2);

        /// <summary>Per managed cloth, the buffer every write is built into. REUSED: allocating a
        /// fresh array per ramp frame is the one part of the ramp that is not free (0.16 ms per
        /// rebuild at 3721 vertices measured against 0.007 ms for the upload itself).</summary>
        public readonly List<ClothSkinningCoefficient[]> Scratch = new(2);

        /// <summary>Per managed cloth, the finite metre bound an UNCONSTRAINED vertex ramps against
        /// — the cape's own bounding extent, i.e. "a vertex may not travel further than the cape is
        /// big". <c>float.MaxValue</c> cannot be ramped multiplicatively, and there is no authored
        /// number to ramp it from; this is the only fabricated value in the file and it is only ever
        /// used BELOW weight 1. At weight 1 the authored sentinel is written back verbatim.</summary>
        public readonly List<float> RampBound = new(2);

        /// <summary>The size <see cref="Note"/> last reported, relative to the board size.</summary>
        public float Factor = 1f;

        /// <summary>The size the coefficients currently express. Only advances on a settle, so a
        /// figure mid-gesture keeps ramping against the size it last held still at.</summary>
        public float SeededFactor = 1f;

        /// <summary>0 = pinned to the skinned pose (<c>maxDistance = 0</c>, the cape is a plain
        /// skinned mesh), 1 = simulating at <see cref="SeededFactor"/>.</summary>
        public float Weight = 1f;

        public int QuietFrames;
        public int IdleFrames;

        /// <summary>True while the target weight is 0 — i.e. a size change is in progress.</summary>
        public bool Suspended;

        /// <summary>The arrays on the cloths no longer match <see cref="Weight"/> /
        /// <see cref="SeededFactor"/> and must be re-uploaded this frame.</summary>
        public bool Dirty;
    }

    private static readonly Dictionary<int, Tracked> _tracked = new();
    private static readonly List<int> _scratch = new(4);
    private static bool _logged;
    private static bool _countersRegistered;

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
    /// Advance every tracked figure: ramp the pin toward its target, release it once the size has
    /// held still, and forget figures nobody is resizing any more. Called once per frame from
    /// <c>FigureGrabbable.TickHeldScale</c>.
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
                    Release(t);
            }
            else if (++t.IdleFrames > PruneFrames && t.Weight >= 1f)
            {
                // Nobody is authoring this figure's size any more AND the ramp has finished, so the
                // cloths are sitting at their settled coefficients. Pruning mid-ramp would strand a
                // cape pinned to its skin for the life of the figure.
                _scratch.Add(kv.Key);
            }

            Advance(t);
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
    /// Begin pinning, and (re-)scan the subtree. The scan happens on every suspend, not once per
    /// figure, because a figure's visual subtree is asynchronous and can be replaced wholesale while
    /// it is on the board — see the class comment's LATE AND RESPAWNED PARTS. Nothing is written to
    /// a cloth here: <see cref="Advance"/> owns every upload, so there is exactly one place that
    /// touches <c>Cloth.coefficients</c>.
    /// </summary>
    private static void Suspend(Tracked t)
    {
        t.Suspended = true;

        Cloth[] found = t.Root.GetComponentsInChildren<Cloth>(true);
        for (int i = 0; i < found.Length; i++)
        {
            Cloth c = found[i];
            if (c == null || t.Cloths.Contains(c))
                continue;
            if (!c.enabled)
                continue; // not simulating → already scales as a plain skinned mesh; leave it alone

            ClothSkinningCoefficient[] pristine = c.coefficients;
            t.Cloths.Add(c);
            t.Pristine.Add(pristine);
            t.Scratch.Add(new ClothSkinningCoefficient[pristine.Length]);
            t.RampBound.Add(RampBoundOf(c));
        }

        if (!_countersRegistered && t.Cloths.Count > 0)
        {
            _countersRegistered = true;
            // Declared so it PRINTS AT ZERO. The whole ModBuild 284 claim is that this pass no
            // longer buys a PhysX fabric cook, and the counter that proves it is the one that says
            // how many coefficient uploads it does instead. A row that is omitted when the number
            // is zero is indistinguishable from an instrument that was never wired up.
            PerfMonitor.Register("FigureGrab.ClothSeeds");
        }
    }

    /// <summary>Release the pin at the size the figure has settled at. The ramp back to full
    /// simulation is <see cref="Advance"/>'s job; this only names the target.</summary>
    private static void Release(Tracked t)
    {
        float factor = t.Factor;
        // Snap a factor that is within the epsilon of the board size to EXACTLY 1, so returning a
        // figure home restores the authored array bit-for-bit rather than to within 0.2%.
        if (Mathf.Abs(factor - 1f) <= FactorEpsilon)
            factor = 1f;

        t.SeededFactor = factor;
        t.Suspended = false;
        t.Dirty = true;
        LogOnce(t, factor);
    }

    /// <summary>
    /// Move <see cref="Tracked.Weight"/> one frame toward its target and, if anything moved, rebuild
    /// and upload every managed cloth's coefficients. The ONLY place in this file that writes to a
    /// <see cref="Cloth"/> — and it never writes <c>enabled</c> and never calls
    /// <c>SetEnabledFading</c>, which is the entire point of ModBuild 284 (see PERFORMANCE).
    /// Steady state is a float compare and a return.
    /// </summary>
    private static void Advance(Tracked t)
    {
        float target = t.Suspended ? 0f : 1f;
        if (t.Weight != target)
        {
            float step = FadeSeconds > 0f ? Time.unscaledDeltaTime / FadeSeconds : 1f;
            float before = t.Weight;
            t.Weight = Mathf.MoveTowards(t.Weight, target, step);
            if (t.Weight != before)
                t.Dirty = true;
        }

        if (t.Cloths.Count == 0)
        {
            t.Dirty = false; // nothing simulated on this figure — the ramp still runs, it just
            return;          // has nowhere to land, and the flag must not latch true forever
        }
        if (!t.Dirty)
            return;
        t.Dirty = false;

        bool settled = !t.Suspended && t.Weight >= 1f;
        using (PerfMonitor.Scope("FigureGrab.Cloth.Seed"))
        {
            for (int i = 0; i < t.Cloths.Count; i++)
            {
                Cloth c = t.Cloths[i];
                if (c == null)
                    continue;

                ClothSkinningCoefficient[] pristine = t.Pristine[i];
                ClothSkinningCoefficient[] next = t.Scratch[i];
                if (pristine == null || next == null || pristine.Length == 0
                    || next.Length != pristine.Length)
                    continue; // no painted constraints, or a capture that did not take

                float factor = t.SeededFactor;
                float weight = t.Weight;
                float bound = t.RampBound[i];

                // ONE COEFFICIENT AT ONE RAMP WEIGHT — the three cases, written as three loops.
                //
                // At weight 1 the authored value is written back verbatim when the vertex is
                // unconstrained, and as `authored × factor` otherwise. That is the ModBuild 137
                // correction, unchanged: maxDistance and collisionSphereDistance are absolute
                // distances against the authored body, so they must follow the figure's size.
                //
                // At weight 0 everything is 0, which pins every vertex to its skinned position and
                // makes the cape a plain skinned mesh — the ModBuild 137 guarantee, at the cost of
                // an upload instead of a fabric cook.
                //
                // In between, the value ramps linearly. An UNCONSTRAINED vertex (Unity writes
                // float.MaxValue — see the class comment) cannot ramp from its own value:
                // float.MaxValue × weight is still unconstrained for any weight above ~1e-30, and
                // multiplying it by the factor overflows to a real infinity. It ramps from `bound`
                // instead. The single discontinuity that leaves — `bound` just below weight 1
                // against float.MaxValue at weight 1 — is between two values that are both
                // physically unconstrained for a cape.
                //
                // TWO THINGS ARE HAND-FLATTENED HERE AND BOTH WERE MEASURED, not assumed.
                // (1) The weight branch is LOOP-INVARIANT, so it is taken once and not 2N times.
                // (2) The unconstrained test is written INLINE rather than through
                //     <see cref="IsUnconstrained"/>: Mono does not inline it, and at 3721 vertices
                //     that is 7442 calls per upload. Same gesture, same cloth, same harness:
                //     a per-vertex helper call for the whole value ... 0.49 ms per upload
                //     the weight branch hoisted, helper kept for the test ... 0.10 ms p50 / 1.98 ms worst
                //     both flattened (this code) ................. 0.101 ms p50, 0.119 ms p90, 0.97 ms cold
                //     against 29.1 ms for ONE SetEnabledFading(true) in the same run.
                // `>= float.MaxValue` is exactly the positive half of IsUnconstrained — positive
                // infinity satisfies it, and a NaN falls through to the multiply in both forms.
                if (weight >= 1f)
                {
                    for (int v = 0; v < pristine.Length; v++)
                    {
                        float m = pristine[v].maxDistance;
                        float s = pristine[v].collisionSphereDistance;
                        next[v].maxDistance = m >= float.MaxValue ? m : m * factor;
                        next[v].collisionSphereDistance = s >= float.MaxValue ? s : s * factor;
                    }
                }
                else if (weight <= 0f)
                {
                    for (int v = 0; v < pristine.Length; v++)
                    {
                        next[v].maxDistance = 0f;
                        next[v].collisionSphereDistance = 0f;
                    }
                }
                else
                {
                    float k = factor * weight;
                    float ramped = bound * weight;
                    for (int v = 0; v < pristine.Length; v++)
                    {
                        float m = pristine[v].maxDistance;
                        float s = pristine[v].collisionSphereDistance;
                        next[v].maxDistance = m >= float.MaxValue ? ramped : m * k;
                        next[v].collisionSphereDistance = s >= float.MaxValue ? ramped : s * k;
                    }
                }

                c.coefficients = next;
                PerfMonitor.Count("FigureGrab.ClothSeeds");

                if (settled)
                {
                    // The resize is over. Clear the transform motion it accumulated so the cape is
                    // not whipped by a size change it should not read as travel. Measured at
                    // 0.0006 ms, and only on the frame the ramp completes.
                    c.ClearTransformMotion();
                }
            }
        }
    }

    /// <summary>The finite metre bound an unconstrained vertex ramps against: the cape's own
    /// bounding extent, read off the <see cref="SkinnedMeshRenderer"/> a <see cref="Cloth"/> is
    /// required to sit beside (same GameObject — this is not a subtree search, so it cannot answer
    /// "related to a mesh" when it was asked "IS this cloth's mesh"). 1 m if the mesh is not
    /// readable, which only affects the shape of a ramp and never a settled value.</summary>
    private static float RampBoundOf(Cloth c)
    {
        var smr = c.GetComponent<SkinnedMeshRenderer>();
        Mesh? mesh = smr != null ? smr.sharedMesh : null;
        if (mesh == null)
            return 1f;
        float extent = mesh.bounds.extents.magnitude;
        return extent > 0f && !float.IsNaN(extent) && !float.IsInfinity(extent) ? extent : 1f;
    }

    /// <summary>
    /// ONE line per session, on the first figure released at a size other than its board size — the
    /// census the next hardware round needs: what the subtree actually contains, how much of it this
    /// pass took responsibility for, HOW BIG the cloths are (the number that sets the cost of every
    /// coefficient upload, and the one this lane had to extrapolate from his spike times), how many
    /// of the authored coefficients are unconstrained (the case the pre-284 guard was silently
    /// overflowing), and exactly which sub-objects are known NOT to follow a figure rescale (see the
    /// class comment's WHAT IS NOT HANDLED). Counting walks the subtree three times, which is why it
    /// is once and never per resize.
    /// </summary>
    private static void LogOnce(Tracked t, float factor)
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

        int verts = 0;
        int widest = 0;
        int unconstrained = 0;
        int empty = 0;
        for (int i = 0; i < t.Pristine.Count; i++)
        {
            ClothSkinningCoefficient[] p = t.Pristine[i];
            if (p == null || p.Length == 0)
            {
                empty++;
                continue;
            }
            verts += p.Length;
            if (p.Length > widest) widest = p.Length;
            for (int v = 0; v < p.Length; v++)
                if (IsUnconstrained(p[v].maxDistance)) unconstrained++;
        }

        VRLog.Info("FigureGrab",
            $"FIGURE SCALE {t.Root.name} settled at {factor:0.###}× of its board size — subtree: "
            + $"{transforms.Length} transforms, {renderers.Length} renderers ({skinned} skinned, which "
            + "follow the root scale exactly and need nothing). Cloth (capes/cloth — the reported "
            + $"parts): {t.Cloths.Count} simulated, {verts} constrained vertices across them "
            + $"(widest {widest}), of which {unconstrained} are authored UNCONSTRAINED "
            + "(float.MaxValue — those are ramped against the cape's own extent, never multiplied); "
            + $"{empty} cloth(s) have no painted constraints to rescale. The per-vertex "
            + "maxDistance/collisionSphereDistance are rescaled from the authored metres and the "
            + "simulation is PINNED to the skinned pose while the size moves — ModBuild 284 replaced "
            + "Cloth.SetEnabledFading with that pin because the fade back IN is an enable transition "
            + "and PhysX re-cooks the fabric on the main thread (measured 19.37 ms at 3721 vertices; "
            + "his 283 log's worst FigureGrab.HeldSize frame was 172.93 ms). Watch [Perf] STEPS "
            + "FigureGrab.Cloth.Seed and [Perf] COUNTS FigureGrab.ClothSeeds for what it costs now. "
            + $"NOT HANDLED: {localScaled} of {particles.Length} ParticleSystem(s) use "
            + "ParticleSystemScalingMode.Local and therefore ignore the root scale by design "
            + "(reported, not changed — see FigureCloth); the figure's worldspace health/condition "
            + "panel is parented outside the figure root by the game "
            + "(ActorBehaviour.CreateWorldSpaceGUIElements) and no root scale can reach it.");
    }
}
