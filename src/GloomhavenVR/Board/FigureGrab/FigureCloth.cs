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
/// <para>FOURTH USER REPORT (hardware test, ModBuild 289, verbatim): "Ich habe dir ein Video
/// figure_scale_problem.mp4 angelegt in dem ich eine Figur einmal größer und wieder kleiner mache.
/// Achte auf die Umhänge. Beim größer machen werden sie steif und beim kleiner machen wird da ein
/// Polygon matsch draus. Sobald ich loslasse ist alles wieder ok, aber ich würde gerne das auch
/// während dem skallieren alles intakt bleibt, inklusive der Reaktion. Ist das möglich?" — both
/// halves of that are ONE mechanism with opposite sign and BOTH of them are the pin, which was not
/// where this lane expected to find them. The account, the arm table and the answer to "ist das
/// möglich" are on <see cref="StepLiveCook"/> and in .planning/CLOTH-DURING-SCALE.md. The short
/// version: while the size moves, the fabric is still baked at the size the figure last settled at
/// — too short a fabric on the way up gives a taut cape with no folds ("steif"), too long a fabric
/// on the way down gives a buckled one ("Polygon matsch").</para>
///
/// <para>FIFTH USER REPORT (hardware test, ModBuild 290, verbatim): "Das Problem mit dem
/// Skallieren von Figuren ist 1:1 noch genau gleich, ich sehe keinen Unterschied zu dem wie es im
/// Video ist." — and he is right, twice over. ModBuild 290 answered "inklusive der Reaktion" with
/// <b>NO</b>, in this comment, on the strength of "a cook is 25-165 ms on his rig". <b>THAT WAS
/// WRONG.</b> The counter ModBuild 290 shipped to check it says a cook of one of his capes costs
/// 1.3-2.5 ms, on his hardware and in the harness alike — see the block comment on
/// <see cref="LiveCookPeakMs"/>. And the remedy ModBuild 290 shipped in place of the answer was
/// measured only at 1.345x, a factor taken from one ModBuild 289 log; his gesture clamp is
/// [0.417 .. 2.503] and his ModBuild 290 log settles at 2.5x. At 2.5x that remedy does not help the
/// shrink and makes the GROW worse — see <see cref="ApplyStretch"/>. ModBuild 291 makes the fabric
/// FOLLOW the size instead (<see cref="StepLiveCook"/>), which is what the user asked for.</para>
///
/// <para>THIRD USER REPORT (hardware test, ModBuild 286, verbatim): "Regression beim Skallieren:
/// Die Klamotten skallieren leider nicht mehr richtig mit, siehe klamotten_problem.jpg. Wie man
/// hier sieht hängt der ursprüngliche Umhang jetzt tiefer und kann nicht mehr als Umhang bezeichnet
/// werden. Auch die physics sollen beim skallieren (und danach) erhalten bleiben." — that is the
/// ModBuild 285 change above, and the section THE 285 REGRESSION below is the whole account.</para>
///
/// <para>=== THE 285 REGRESSION: A FABRIC IS COOKED, AND 285 STOPPED COOKING IT ===</para>
///
/// <para>WHAT 285 VALIDATED AND WHAT IT DID NOT. It proved that a PINNED cloth is indistinguishable
/// from a DISABLED one across a transform scale (0.48788-0.48793 against 0.48790 flat, +1 to +90
/// frames). That is true and it still holds. It is also a measurement of the SUSPENSION and not of
/// the RESUMPTION: the state the user is looking at in klamotten_problem.jpg is the cape SETTLED
/// and SIMULATING at the new size, and no arm of that round ever entered it. A measurement of the
/// wrong state looks exactly like proof.</para>
///
/// <para>THE MECHANISM, MEASURED. PhysX bakes a cloth's FABRIC — its edge rest lengths — in WORLD
/// units when the component is enabled, and nothing re-derives them from a transform scale
/// afterwards. Builds 137-283 paid for a re-cook by accident, because their resume was an enable
/// transition; 285 removed the enable to remove the 172.93 ms frame, and removed the cook with it.
/// A cape on a figure held at 1.345x therefore simulates against rest lengths sized for 1x. Same
/// Unity 2021.3.5f1 Linux player, a 1681-vertex sheet pinned along one edge and draped under
/// gravity, every arm settled for 300 frames and every position divided by the root scale before
/// comparison, against a POSITIVE CONTROL that is a cloth BORN at 1.345 (fabric cooked at 1.345,
/// coefficients authored x 1.345) and a NULL CONTROL that is the reference arm run twice:
/// <list type="table">
/// <item><term></term><description>.................... mean edge / authored edge | worst vs POSITIVE | mean vs POSITIVE</description></item>
/// <item><term>NULL control</term><description> ....... identical to REF to five decimals, 0.00000 drift</description></item>
/// <item><term>POSITIVE (born at 1.345)</term><description> .. 1.019 | — | —</description></item>
/// <item><term>285 SHIPPED (no cook)</term><description> .... 0.793 | 0.271 m | 0.075 m</description></item>
/// <item><term>OLD 137-283 (cooked)</term><description> ..... 1.017 | 0.038 m | 0.015 m</description></item>
/// <item><term>THIS FILE (cooked on settle)</term><description> 1.021 | 0.040 m | 0.018 m</description></item>
/// </list>
/// 0.793 is 1/1.345 to within the reference arm's own 1.036 — i.e. the number IS the missing scale.
/// The fabric squeezes a cape that is a third larger than the fabric believes, which is what "hängt
/// jetzt tiefer und kann nicht mehr als Umhang bezeichnet werden" looks like as a number. The
/// re-cook was never pure cost.</para>
///
/// <para>THERE IS NO THRESHOLD BELOW WHICH SKIPPING THE COOK IS FREE, which is why the trigger is
/// the ordinary <see cref="FactorEpsilon"/> and not a coarser one. The same arms at S = 1.08 give
/// mean edge 0.941 against the positive control's 1.025, and at S = 1.04 they give 0.974 against
/// 1.029. The error saturates almost immediately rather than scaling with the mismatch, so a "only
/// re-cook for big changes" rule buys frames by shipping a visibly wrong cape.</para>
///
/// <para>TWO NO-COOK ALTERNATIVES WERE TRIED AND BOTH FAIL. <c>stretchingStiffness = 0</c> lets the
/// fabric stop enforcing its stale rest lengths, but the cloth then has nothing holding it together
/// at all: mean edge 1.308 with a most-deviating edge at 5.3x authored, i.e. it tears open instead
/// of bunching up. <c>useTethers = false</c> changes essentially nothing (0.938 against 0.941). Both
/// are one-line, zero-cost and both are worse than the defect.</para>
///
/// <para>THE ORDER OF THE RE-COOK IS THE WHOLE THING, and four arms died finding it. The settled
/// coefficients must go up BEFORE the component goes down. An enable transition performed while
/// every <c>maxDistance</c> is 0 does not cook (1.1-3.1 ms against 14-21 ms) and leaves the cloth
/// PERMANENTLY non-simulating — flat 0.00000 sag out to +300 frames, and no later coefficient write
/// revives it. That is true of <c>enabled = true</c> and of <c>SetEnabledFading(true, …)</c> alike,
/// so it is a property of the zero coefficients and not of which API is used. See
/// <see cref="PinFloorFraction"/>, which is the same finding turned into a guard against the enable
/// transitions this pass does NOT own.</para>
///
/// <para>WHAT A COOK COSTS NOW, and how often. 8 reps per cell, median, same player, with the same
/// NULL and known-positive controls: <c>enabled = false</c> 0.021-0.025 ms, and <c>enabled = true</c>
/// after a proper down 20.06 / 34.29 / 56.45 ms at 1681 / 3721 / 6561 vertices (the
/// <c>AddComponent&lt;Cloth&gt;</c> positive control reads 17.09 / 24.67 / 40.24 in the same run).
/// It is paid ONCE per settled size change per cloth, never during the gesture, and ONE CLOTH PER
/// FRAME. That last point is the one that matters to him: his ModBuild 283 worst frame was 172.93 ms
/// and it was three cloths cooking on the same frame; staggered, the same work is three frames of
/// about a third that each. <see cref="SettleFrames"/> went 3 → 12 for the same reason — a settle
/// used to cost 0.1 ms and now costs a cook, so it must not fire on a mid-gesture micro-pause.</para>
///
/// <para>HIS CONSTRAINT OUTRANKS THE HITCH AND IS WHY THIS TRADE IS MADE: "Auch die physics sollen
/// beim skallieren (und danach) erhalten bleiben." A cape that is cheap and wrong is a worse build
/// than a cape that is right and costs one frame at the end of a deliberate action. What 285 bought
/// is kept in full — nothing cooks while the size is MOVING, which is the window his "Hänger"
/// complaint was actually about.</para>
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
    /// <summary>Quiet frames a size must hold before the simulation is released back to full.
    ///
    /// <para>Was 3 through ModBuild 285, when a release was a coefficient upload and cost 0.1 ms. A
    /// release now ends in a FABRIC RE-COOK (see THE 285 REGRESSION), which costs 20-56 ms per
    /// cloth, so a settle that fires on a mid-gesture micro-pause is no longer free. 12 frames is
    /// ~0.13 s at 90 Hz: still well under the pause a player makes between two deliberate stretches,
    /// and long enough that the exponential smoothing here and the remote slot's Lerp cannot be
    /// mistaken for "settled". Nothing is visibly wrong while it waits — the cape is PINNED, which
    /// is exactly the state that scales with the root perfectly.</para></summary>
    private const int SettleFrames = 12;

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

    /// <summary>
    /// The pin's FLOOR, as a fraction of a cloth's own bounding extent — the smallest
    /// <c>maxDistance</c> this pass will ever write. It is not zero, and that is a bug fix.
    ///
    /// <para>MEASURED: a <see cref="Cloth"/> that goes through an ENABLE TRANSITION while every one
    /// of its <c>maxDistance</c> coefficients is exactly 0 never simulates again — it renders the
    /// skinned pose for the rest of the figure's life, and no later coefficient write revives it
    /// (settle trace flat at 0.00000 out to +300 frames; the enable itself costs 1.5-3 ms instead of
    /// the 14-21 ms a real cook costs, i.e. it does not even cook). The same run with the floor
    /// below at 1e-3 comes back alive and lands 0.0355 from the born-at-scale reference, which is
    /// better than the 137-283 path's own 0.0378.</para>
    ///
    /// <para>WE DO NOT OWN EVERY ENABLE TRANSITION, which is why this matters. The game's
    /// <c>ActorBehaviour.ForceSetLocoIntermediateTarget</c> (ActorBehaviour.cs:245-252) disables
    /// every <c>m_Clothes</c> entry and its <c>LateUpdate</c> (ActorBehaviour.cs:551-562) re-enables
    /// them two frames later. A figure being carried is exactly the figure that gets its locomotion
    /// target forced, so that pair lands INSIDE our pin window. With a zero pin that killed the
    /// cape permanently; with this floor the game's own re-enable cooks the fabric for us, correctly
    /// and for free.</para>
    ///
    /// <para>1e-3 of the cape's extent is sub-millimetre on a board figure, so the pin's fidelity is
    /// unchanged for every purpose the pin exists for.</para></summary>
    private const float PinFloorFraction = 1e-3f;

    // =============================================================================================
    // THE LIVE RE-COOK (ModBuild 291) — "inklusive der Reaktion", which ModBuild 290 answered NO.
    //
    // THE NO WAS WRONG AND IT WAS MY NO. ModBuild 290 wrote, in this file and to the user: "full
    // physics during the gesture needs the fabric to follow the scale, that needs a cook, and a
    // cook is 25-165 ms on his rig". That sentence came from one ModBuild 289 log's
    // FigureGrab.Cloth.Cook step line. The counter that shipped in ModBuild 290 to check it says
    // otherwise, in his own ModBuild 290 log, on his own hardware:
    //
    //     FigureGrab.ClothCooks     total 17
    //     FigureGrab.ClothCookVerts total 1754, worst frame 143      -> 103 particles per cook
    //
    // and in that same 30.0 s window FigureGrab.Cloth.Cook appears on NEITHER the [Perf] STEPS line
    // NOR the [Perf] STEPS TAIL line, whose floor is 1.0 ms/s. Seventeen cooks therefore cost under
    // 30 ms BETWEEN THEM: under 1.76 ms each. Re-measured in the harness at the sizes his capes
    // actually are (LiveCookBench, --bench=live, 9 reps, median, NULL 0.0009 ms):
    //
    //     81 particles  1.277 ms   (15.8 us/particle)
    //    144 particles  2.475 ms   (17.2 us/particle)
    //    441 particles  6.666 ms   (15.1 us/particle)
    //
    // A cook of one of HIS capes is one to two and a half milliseconds. It fits.
    //
    // WHAT THAT BUYS. The stale fabric is the whole defect — "steif" on the way up and "Polygon
    // matsch" on the way down are one mismatch with two signs. Re-cooking every few frames means
    // the fabric is never more than a few frames stale, so there is nothing to pin against and the
    // cape can simply simulate. Measured over the whole gesture at HIS 2.5x (ThrottleBench):
    // shrink incoherence mean 0.8503 (ModBuild 289) / 1.0445 (ModBuild 290 shipped) / 0.0001 at
    // period 6, and the settled cape is bit-identical in every arm.
    // =============================================================================================

    /// <summary>Cost model for one cook, in microseconds per cloth particle. MEASURED at 15.1-17.2
    /// us/particle over 81-441 particles and falling to 5.9 above that (LiveCookBench part 1);
    /// 20 is deliberately above the whole measured range, because this number decides how much
    /// work gets scheduled and the safe direction to be wrong in is "too expensive".</summary>
    private const float LiveCookMicrosecondsPerParticle = 20f;

    /// <summary>A single cook this expensive is never scheduled mid-gesture, whatever the period —
    /// the cook lands on ONE frame and no amortisation makes that frame cheaper. 4 ms is a third of
    /// the 11.11 ms budget and is the same cost this file ALREADY pays, once per cloth, on every
    /// settle. At <see cref="LiveCookMicrosecondsPerParticle"/> it admits cloths up to 200
    /// particles: his two test figures' capes are 82 and 143. A figure carrying anything bigger
    /// keeps the ModBuild 285 pin, unchanged.</summary>
    private const float LiveCookPeakMs = 4.0f;

    /// <summary>The AMORTISED budget: total cook cost across all of a figure's cloths, divided by
    /// the period, must not exceed this per frame. Half a millisecond of an 11.11 ms frame, and
    /// only while a gesture is actually running.</summary>
    private const float LiveCookBudgetMsPerFrame = 0.5f;

    /// <summary>Frames between two cooks of the SAME cloth. The floor is the fastest arm the
    /// harness actually measured (period 6: shrink incoherence mean 0.0001 at 2.5x) and the ceiling
    /// is the slowest one that still beats the pin by two orders of magnitude (period 30: 0.1674
    /// against the ModBuild 289 pin's 0.8503). Nothing outside that range has been measured, so
    /// nothing outside it is scheduled. The period is also floored at twice the cloth count, which
    /// is what keeps two cloths from being disabled on the same frame — the invariant
    /// <c>FigureGrab.ClothCooks</c>' `worst frame` reports.</summary>
    private const int LiveCookMinPeriod = 6;
    private const int LiveCookMaxPeriod = 30;

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

        /// <summary>Per managed cloth, its authored <c>stretchingStiffness</c>, captured with the
        /// coefficients. The pin ramps this to 0 alongside <c>maxDistance</c> and restores it
        /// verbatim at weight 1 — see THE PIN AND THE FABRIC FIGHT EACH OTHER ON THE WAY DOWN.
        /// Captured, never assumed: Unity's own default is 1 but a cape may be authored softer.
        /// </summary>
        public readonly List<float> PristineStretch = new(2);

        /// <summary>The ramp weight the stiffness on the cloths currently expresses. A separate
        /// latch from <see cref="Dirty"/> because the coefficient upload and the stiffness write
        /// are different costs (0.0069 ms against 0.004 ms) and the stiffness must also be
        /// restored on paths — the re-cook — where no coefficient upload happens.</summary>
        public float StretchWeight = 1f;

        /// <summary>The size <see cref="Note"/> last reported, relative to the board size.</summary>
        public float Factor = 1f;

        /// <summary>The size the coefficients currently express. Only advances on a settle, so a
        /// figure mid-gesture keeps ramping against the size it last held still at.</summary>
        public float SeededFactor = 1f;

        /// <summary>0 = pinned to the skinned pose (<c>maxDistance</c> at its floor, the cape is a
        /// plain skinned mesh), 1 = simulating at <see cref="SeededFactor"/>.</summary>
        public float Weight = 1f;

        /// <summary>PER MANAGED CLOTH, the size that cloth's FABRIC was last cooked at — a different
        /// thing from <see cref="SeededFactor"/>, which is the size the COEFFICIENTS express. PhysX
        /// bakes a cloth's edge rest lengths in world units when the component is enabled and never
        /// afterwards, so this is the number that decides whether a re-cook is owed.
        ///
        /// <para>PER CLOTH AND NOT PER FIGURE, which it was through ModBuild 289. One number was
        /// sound only while the stagger was all-or-nothing; <see cref="AbortCook"/> can now stop it
        /// halfway, and then "the size this figure's fabric is baked at" is not a single value. The
        /// harness caught it as a REGRESSION in the one state the user says is already fine: a
        /// gesture whose shrink began mid-stagger left cloth 0 cooked at 1.345 while the figure
        /// finished at 1, the figure-wide number still read 1, the release therefore decided no
        /// cook was owed, and the SETTLED cape came out at normal incoherence 0.278 against
        /// ModBuild 289's 0.047. Per cloth it is 0.047 again, and the skip below means the cloths
        /// that were already cooked correctly are not cooked a second time.</para></summary>
        public readonly List<float> CookedFactor = new(2);

        public int QuietFrames;
        public int IdleFrames;

        /// <summary>True while the target weight is 0 — i.e. a size change is in progress.</summary>
        public bool Suspended;

        /// <summary>The arrays on the cloths no longer match <see cref="Weight"/> /
        /// <see cref="SeededFactor"/> and must be re-uploaded this frame.</summary>
        public bool Dirty;

        /// <summary>True while the re-cook sequence is running. It owns every cloth write for as
        /// long as it lasts, so the ramp in <see cref="Advance"/> stands down.</summary>
        public bool Cooking;

        /// <summary>Which cloth the re-cook sequence is on. ONE cloth per frame, deliberately: his
        /// ModBuild 283 worst frame was 172.93 ms, which is three cloths cooking together. Staggered,
        /// the same work is three ordinary-sized frames instead of one triple-sized one.</summary>
        public int CookIndex;

        /// <summary>True once the current cloth's coefficients are up and the component is down, so
        /// the NEXT frame performs the enable that cooks. Two frames per cloth, because a
        /// <c>false</c>/<c>true</c> pair inside ONE frame does not cook at all — measured at 2.6-3.1
        /// ms against 14-21 ms, and it leaves the cloth in the permanently-dead state described on
        /// <see cref="PinFloorFraction"/>.</summary>
        public bool CookDown;

        /// <summary>How many cloths the sequence was started for. Frozen at the start so a cloth
        /// that appears mid-sequence (the subtree is asynchronous — see LATE AND RESPAWNED PARTS)
        /// is not cooked: it was born at the current size and its fabric is already right.</summary>
        public int CookCount;

        // ==================================================================================
        // THE LIVE RE-COOK — ModBuild 291. Read WHY on LiveCookPeakMs and StepLiveCook.
        // ==================================================================================

        /// <summary>Decided once per gesture, in <see cref="Suspend"/>: may THIS figure keep its
        /// fabric following the size instead of pinning? PER FIGURE and not per cloth, because
        /// <see cref="Weight"/> is one number for the whole figure and a half-pinned figure would
        /// need two of them. A figure with one cloth too big to cook mid-gesture keeps the ModBuild
        /// 285 pin for all of them.</summary>
        public bool LiveCookable;

        /// <summary>Frames between two cooks of the SAME cloth, derived from the particle counts in
        /// <see cref="Suspend"/>. Cloth <c>i</c> goes down on gesture frames where
        /// <c>(GestureFrames - 2*i) % LivePeriod == 0</c> and comes up on the next one, so no two
        /// cloths are ever disabled together.</summary>
        public int LivePeriod = LiveCookMinPeriod;

        /// <summary>Per managed cloth, whether it is between the two frames of a live cook — i.e.
        /// <c>enabled == false</c> and waiting to be brought back up. A disabled Cloth never
        /// simulates again unless somebody enables it, so every exit from the live path has to
        /// drain this.</summary>
        public readonly List<bool> LiveDown = new(2);

        /// <summary>Per managed cloth, the size its fabric was baked at by its last LIVE cook — a
        /// separate record from <see cref="CookedFactor"/>, which a live cook deliberately poisons
        /// with a sentinel so the settle always re-cooks (see <see cref="FinishLiveCook"/>). Without
        /// it there is no way to answer "has the body moved since this cloth was last baked?", and
        /// a figure the player is holding still would go on cooking on schedule for the whole
        /// twelve-frame settle window — two or three cooks bought for nothing, on every micro-pause
        /// of every gesture.</summary>
        public readonly List<float> LiveFactor = new(2);

        /// <summary>The collider arrays of the ONE cloth currently between the two frames of a live
        /// cook, stashed across its disable/enable pair. At most one cloth per figure is ever down,
        /// so one slot is enough.
        ///
        /// <para>WHY. <see cref="FigureClothHands"/> writes <c>sphereColliders</c> ONCE, when the
        /// free hand comes into reach, and thereafter only moves the probe transforms — so if an
        /// enable transition drops those arrays, the cape silently stops reacting to the player's
        /// other hand for the rest of that session, and nothing in either file would say so. That
        /// combination is not exotic: the free hand is the hand that runs the stretch gesture, so
        /// it is present during exactly the gestures this path exists for. Stashing and restoring
        /// is two array reads and two writes at 0.008-0.017 ms each (FigureClothHands measured
        /// them), against a cook of 1.3-2.5 ms — it costs nothing and it does not depend on knowing
        /// whether PhysX would have kept them.</para></summary>
        public ClothSphereColliderPair[]? LiveSpheres;
        public CapsuleCollider[]? LiveCapsules;

        // ==================================================================================
        // THE GESTURE RECORDER — ModBuild 291. Read WHY on LogGesture.
        // ==================================================================================

        /// <summary>How many <see cref="Cloth"/> components the last <see cref="Suspend"/> scan
        /// FOUND in the subtree, against <see cref="Cloths"/>.Count, which is how many it took
        /// responsibility for. A figure whose two numbers differ has cloths this file is not
        /// managing — <see cref="Suspend"/> skips any cloth that is not <c>enabled</c> — and that
        /// is the difference between "the remedy did nothing" and "the remedy was never near this
        /// cape", which a user report cannot tell apart and which need opposite responses.</summary>
        public int FoundCloths;

        /// <summary>The size this gesture started from, and the smallest and largest size it
        /// reached. EVERY arm table this lane has produced was measured at 1.345×, because that is
        /// the factor one ModBuild 289 log happened to settle at. His ModBuild 290 log settles at
        /// 2.5× and drives to the 2.503 end stop over and over. A stale-fabric error is a RATIO,
        /// so it grows with the factor, and a measurement at 1.345 is not evidence about 2.5.
        /// These three numbers exist so that substitution can never be made silently again.</summary>
        public float GestureFromFactor = 1f;
        public float GestureMinFactor = 1f;
        public float GestureMaxFactor = 1f;

        /// <summary>Frames this gesture spent with the size moving.</summary>
        public int GestureFrames;

        /// <summary>The lowest ramp weight the gesture actually reached. The pin and the stiffness
        /// ramp are both <c>× Weight</c>, so a gesture that never gets near 0 never applied either
        /// remedy at full strength — and "no improvement" from a remedy that never ran carries
        /// zero information.</summary>
        public float MinWeightReached = 1f;

        /// <summary>How many times <see cref="ApplyStretch"/> wrote during this gesture, and the
        /// lowest value it wrote to any cloth. Zero writes means the ramp never reached the
        /// cloths at all.</summary>
        public int StretchWrites;
        public float MinStretchWritten = float.NaN;
    }

    private static readonly Dictionary<int, Tracked> _tracked = new();
    private static readonly List<int> _scratch = new(4);

    /// <summary>Figure roots whose census line has been printed. PER FIGURE, not per session: the
    /// ModBuild 290 census fired once, on a figure with an 82-particle cape, and was then read as
    /// evidence about a session in which the counters say cloths up to 143 particles were cooked
    /// seventeen times. A latch that fires on the first sample it sees is not a census. Capped at
    /// <see cref="CensusCap"/> figures because the line is long and the walk is three passes over a
    /// subtree.</summary>
    private static readonly HashSet<int> _censused = new();
    private static bool _loggedEmpty;
    private static bool _countersRegistered;
    private static int _gestureLines;

    /// <summary>How many DIFFERENT figures get a full census line, and how many gestures get a
    /// <see cref="LogGesture"/> line, before both stand down for the session. Both are generous
    /// enough to cover a whole hardware test (his ModBuild 290 session contained eight gestures on
    /// two figures) and bounded so neither can flood a log.</summary>
    private const int CensusCap = 6;
    private const int GestureLineCap = 24;

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
                Suspend(t);   // resets the gesture recorder — it is the START EDGE of a gesture

            // The extent of the gesture, recorded as it happens. Suspend() has already reset these
            // to the current factor on the start edge, so the first sample never widens the range
            // by itself.
            if (factor < t.GestureMinFactor) t.GestureMinFactor = factor;
            if (factor > t.GestureMaxFactor) t.GestureMaxFactor = factor;
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
                t.GestureFrames++;
                if (t.QuietFrames >= SettleFrames)
                    Release(t);
            }
            else if (++t.IdleFrames > PruneFrames && t.Weight >= 1f && !t.Cooking)
            {
                // Nobody is authoring this figure's size any more AND the ramp has finished AND no
                // re-cook is in flight, so the cloths are sitting at their settled coefficients with
                // a fabric that matches them. Pruning mid-ramp would strand a cape pinned to its
                // skin for the life of the figure; pruning mid-cook would strand one DISABLED.
                _scratch.Add(kv.Key);
            }

            Advance(t);
            t.QuietFrames++;
        }

        for (int i = 0; i < _scratch.Count; i++)
            _tracked.Remove(_scratch[i]);
    }

    /// <summary>Forget everything (driver teardown / scene change). It touches the cloths in
    /// exactly ONE way: any cloth caught between the two frames of a live cook is brought back up
    /// first. Everything else is left alone, because the figures these cloths belong to are going
    /// away with the scene and a Unity-null component cannot be written to anyway — but a cloth
    /// that is <c>enabled == false</c> when we stop ticking is a cape that is a plain skinned mesh
    /// for the life of the figure, and the driver can be torn down by a config toggle without the
    /// scene going anywhere.</summary>
    internal static void Clear()
    {
        foreach (KeyValuePair<int, Tracked> kv in _tracked)
        {
            Tracked t = kv.Value;
            for (int i = 0; i < t.Cloths.Count; i++)
                FinishLiveCook(t, i);
        }
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

        // THE GESTURE RECORDER STARTS HERE, because this method IS the start edge of a gesture:
        // Note only calls it on the transition out of the settled state. Everything reset here is
        // read once, by LogGesture, on the matching Release.
        t.GestureFromFactor = t.SeededFactor;
        t.GestureMinFactor = t.Factor;
        t.GestureMaxFactor = t.Factor;
        t.GestureFrames = 0;
        t.MinWeightReached = t.Weight;
        t.StretchWrites = 0;
        t.MinStretchWritten = float.NaN;

        Cloth[] found = t.Root.GetComponentsInChildren<Cloth>(true);
        t.FoundCloths = found.Length;
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
            t.PristineStretch.Add(c.stretchingStiffness);
            // Seeded at the BOARD size, which is the same claim ModBuild 289's figure-wide field
            // made and is true of every cloth that existed while the figure sat on the board. A
            // cloth genuinely born at some other size costs one redundant cook the first time the
            // figure settles, exactly as it did before; deciding otherwise is a separate question
            // and it is not this build's.
            t.CookedFactor.Add(1f);
            t.LiveDown.Add(false);
            t.LiveFactor.Add(1f);
            // A cloth captured on a LATER suspend has never had a stiffness written to it, so the
            // change-latch must be invalidated or it would be left at its authored value for the
            // whole pin — the one cloth on the figure still fighting the pin.
            t.StretchWeight = -1f;
        }

        DecideLiveCook(t);

        if (!_countersRegistered && t.Cloths.Count > 0)
        {
            _countersRegistered = true;
            // Declared so they PRINT AT ZERO. A row that is omitted when the number is zero is
            // indistinguishable from an instrument that was never wired up, and both of these
            // exist to prove a bound rather than to report an amount: ClothSeeds says how many
            // coefficient uploads replaced the per-gesture cook, and ClothCooks says how many
            // fabric cooks are left and — in its `worst frame` field — that no two of them ever
            // land on the same frame.
            PerfMonitor.Register("FigureGrab.ClothSeeds");
            PerfMonitor.Register("FigureGrab.ClothCooks");
            // THE OPEN NUMBER OF ModBuild 289, turned into an instrument instead of an argument.
            // His log says FigureGrab.Cloth.Cook costs 32-53 ms on average and 167.51 ms at worst,
            // while the census printed 771 vertices for the widest cape on ONE figure. Re-measured
            // in the harness on this Unity version, a cook is 10-20 us per cloth vertex and NOTHING
            // about a Cloth's configuration multiplies it — not self-collision (1.4x), not virtual
            // particles (1.2x), not colliders (1.4x), not the raw mesh vertex count (1.00x, the
            // welded particle count is the driver). 167 ms at that rate is a cape of about eleven
            // thousand particles, which is not the figure the census happened to print. So COUNT
            // THE VERTICES AT THE COOK: `worst frame` then names the largest cape ever cooked in
            // the session, and total / ClothCooks its mean, in one line, without guessing.
            PerfMonitor.Register("FigureGrab.ClothCookVerts");
            PerfMonitor.Register("FigureGrab.ClothCookAborts");
        }
    }

    /// <summary>
    /// Release the pin at the size the figure has settled at.
    ///
    /// <para>If the fabric was cooked at a DIFFERENT size, this starts the re-cook sequence and
    /// <see cref="StepCook"/> owns the cloths until it finishes — there is no ramp in that case,
    /// because a cook re-anchors every particle on its skinned position and the cape drapes out
    /// from there on its own (measured: lowest y walks -0.013 → -0.242 over the settle, i.e. the
    /// cook IS the ramp). If the fabric already matches, the cheap ModBuild 285 ramp runs and
    /// nothing is cooked at all.</para>
    /// </summary>
    private static void Release(Tracked t)
    {
        float factor = t.Factor;
        // Snap a factor that is within the epsilon of the board size to EXACTLY 1, so returning a
        // figure home restores the authored array bit-for-bit rather than to within 0.2%.
        if (Mathf.Abs(factor - 1f) <= FactorEpsilon)
            factor = 1f;

        t.SeededFactor = factor;
        t.Suspended = false;

        // DRAIN THE LIVE PATH BEFORE THE SETTLE SEQUENCE TAKES OVER. A cloth caught between the two
        // frames of a live cook is `enabled == false`, and a disabled Cloth never simulates again
        // unless somebody enables it. StepCook would revive the ONE cloth it is pointed at and
        // leave every other one a plain skinned mesh for the life of the figure. Nothing else in
        // this file writes `enabled`.
        for (int i = 0; i < t.Cloths.Count; i++)
            FinishLiveCook(t, i);

        if (t.Cloths.Count > 0 && NeedsCook(t, factor))
        {
            t.Cooking = true;
            t.CookIndex = 0;
            t.CookDown = false;
            t.CookCount = t.Cloths.Count;
            t.Dirty = false;
        }
        else
        {
            t.Dirty = true;
        }

        LogGesture(t, factor);
        LogOnce(t, factor);
    }

    /// <summary>
    /// Re-cook ONE cloth's fabric at the size the figure settled at, two frames per cloth, one
    /// cloth per frame.
    ///
    /// <para>WHY A COOK IS OWED AT ALL — the ModBuild 285 regression, measured. PhysX bakes a
    /// cloth's edge rest lengths in WORLD units when the component is enabled, and nothing
    /// re-derives them from a transform scale afterwards. Builds 137-283 paid for a cook by
    /// accident (their resume was an enable transition); ModBuild 285 removed the enable to remove
    /// the 172.93 ms frame and removed the cook with it, so a figure held at 1.345× simulated
    /// against rest lengths sized for 1×. In the harness that reads as a settled cape whose edges
    /// are 0.793 of their authored length — the fabric squeezing a cape that is now a third larger
    /// than the fabric believes, which is "hängt jetzt tiefer und kann nicht mehr als Umhang
    /// bezeichnet werden".</para>
    ///
    /// <para>THE ORDER IS THE WHOLE THING, and it cost this lane four dead arms to find. The
    /// settled coefficients go up BEFORE the component goes down. An enable transition performed
    /// while <c>maxDistance</c> is 0 everywhere does not cook and leaves the cloth permanently
    /// non-simulating — see <see cref="PinFloorFraction"/>. Uploading first also means that if the
    /// game's own re-enable (ActorBehaviour.cs:551-562) lands between our two frames it cooks the
    /// RIGHT fabric, and our <c>enabled = true</c> is then a harmless no-op on an already-enabled
    /// component rather than a second cook.</para>
    /// </summary>
    private static void StepCook(Tracked t)
    {
        if (t.CookIndex >= t.CookCount || t.CookIndex >= t.Cloths.Count)
        {
            t.Cooking = false;
            t.Weight = 1f;      // a cook releases the pin outright; there is nothing left to ramp
            t.Dirty = false;
            return;
        }

        Cloth c = t.Cloths[t.CookIndex];
        if (c == null)
        {
            t.CookIndex++;
            t.CookDown = false;
            return;
        }

        if (!t.CookDown && !NeedsCookAt(t, t.CookIndex, t.SeededFactor))
        {
            // This cloth's fabric is already baked at the size the figure settled at — it was
            // cooked by an earlier sequence that AbortCook stood down before it reached the rest.
            // Skip it outright rather than paying 25-165 ms to bake the same numbers again.
            t.CookIndex++;
            return;
        }

        if (!t.CookDown)
        {
            if (BuildInto(t, t.CookIndex, 1f, t.SeededFactor))
            {
                c.coefficients = t.Scratch[t.CookIndex];
                PerfMonitor.Count("FigureGrab.ClothSeeds");
            }
            // The fabric must be cooked at the AUTHORED stiffness, not at the 0 the pin drove it to
            // — the whole point of the cook is to bake rest lengths the authored fabric will then
            // enforce. Restoring it here, on the frame the component goes down, means the enable on
            // the next frame sees the finished state whichever way it arrives (ours, or the game's
            // own LateUpdate re-enable landing between the two frames).
            //
            // ONE CLOTH, NOT ALL OF THEM, and that distinction is the whole reason this is not a
            // call to ApplyStretch. The sequence is one cloth per frame and a cook frame is 25-165
            // ms on his rig, so restoring every cloth's stiffness here would put the cloths that
            // have NOT been cooked yet back into the pin-versus-stale-fabric fight for the several
            // hundred milliseconds the rest of the sequence takes — which is exactly the buckling
            // this build exists to remove, reintroduced at the moment it is most visible.
            if (t.CookIndex < t.PristineStretch.Count)
                c.stretchingStiffness = t.PristineStretch[t.CookIndex];
            t.StretchWeight = -1f;   // the per-figure latch no longer describes every cloth
            c.enabled = false;
            t.CookDown = true;
            return;
        }

        int verts = t.Pristine.Count > t.CookIndex && t.Pristine[t.CookIndex] != null
            ? t.Pristine[t.CookIndex].Length : 0;
        using (PerfMonitor.Scope("FigureGrab.Cloth.Cook"))
            c.enabled = true;   // THE RE-COOK — 10-20 us per cloth vertex, re-measured for 290
        PerfMonitor.Count("FigureGrab.ClothCooks");
        PerfMonitor.Count("FigureGrab.ClothCookVerts", verts);
        c.ClearTransformMotion();
        if (t.CookIndex < t.CookedFactor.Count)
            t.CookedFactor[t.CookIndex] = t.SeededFactor;
        // The settle cook is a bake too, so the live path's "has the body moved since this cloth
        // was last baked?" record has to see it — otherwise the NEXT gesture compares against a
        // size from the gesture before last.
        if (t.CookIndex < t.LiveFactor.Count)
            t.LiveFactor[t.CookIndex] = t.SeededFactor;
        t.CookDown = false;
        t.CookIndex++;
    }

    /// <summary>
    /// Decide, once per gesture, whether this figure's fabric can follow the size instead of the
    /// cape being pinned — and at what period.
    ///
    /// <para>Two independent gates, because they answer different questions. The PEAK gate asks
    /// "does one cook fit in one frame?" and no amount of spacing changes its answer: a cook lands
    /// on a single frame. The AMORTISED gate asks "how often can this figure afford one?" and is
    /// what the period is for. A figure fails outright if the peak gate rejects its largest cloth,
    /// or if the period the amortised gate demands is longer than the longest one the harness
    /// measured — scheduling outside the measured range would be a guess, and the fallback is the
    /// ModBuild 285 pin, which is what ships today.</para>
    ///
    /// <para>PER FIGURE, not per cloth: <see cref="Tracked.Weight"/> is one number for the whole
    /// figure, so a figure cannot be half pinned and half live without a second weight. His two
    /// test figures carry capes of 82 and 143 particles and both qualify; the Savvas Icestorm in
    /// figure_scale_problem.mp4 reported 771 for its widest and does not.</para>
    /// </summary>
    private static void DecideLiveCook(Tracked t)
    {
        t.LiveCookable = false;
        t.LivePeriod = LiveCookMinPeriod;
        if (t.Cloths.Count == 0)
            return;

        int worst = 0;
        int total = 0;
        for (int i = 0; i < t.Pristine.Count; i++)
        {
            ClothSkinningCoefficient[] p = t.Pristine[i];
            int n = p != null ? p.Length : 0;
            total += n;
            if (n > worst) worst = n;
        }
        if (worst <= 0)
            return;

        float worstMs = worst * LiveCookMicrosecondsPerParticle / 1000f;
        if (worstMs > LiveCookPeakMs)
            return;   // one cook does not fit in one frame; no period fixes that

        float totalMs = total * LiveCookMicrosecondsPerParticle / 1000f;
        int needed = Mathf.CeilToInt(totalMs / LiveCookBudgetMsPerFrame);
        int period = Mathf.Max(needed, Mathf.Max(LiveCookMinPeriod, 2 * t.Cloths.Count));
        if (period > LiveCookMaxPeriod)
            return;   // affordable only at a spacing nothing has been measured at

        t.LivePeriod = period;
        t.LiveCookable = true;
    }

    /// <summary>
    /// Re-cook ONE cloth's fabric at the size the figure is at RIGHT NOW, on a fixed period, while
    /// the gesture is still running.
    ///
    /// <para>=== THIS IS THE ANSWER TO "INKLUSIVE DER REAKTION" AND ModBuild 290 SAID IT WAS
    /// IMPOSSIBLE ===</para>
    ///
    /// <para>PhysX bakes a cloth's fabric — its edge rest lengths — in world units at enable time
    /// and never re-derives it from a transform scale. Everything the user filmed follows from
    /// that one fact: while the size moves, the fabric is still baked at the size the figure last
    /// settled at, so growing leaves a cape too taut to fold ("steif") and shrinking leaves it with
    /// surplus length that buckles through itself ("Polygon matsch"). ModBuild 289 pinned the cape
    /// to hide it. ModBuild 290 additionally drove <c>stretchingStiffness</c> to 0 to stop the pin
    /// and the stale fabric arguing. Neither made the fabric right, because only a cook does that,
    /// and ModBuild 290 concluded a cook could not be afforded mid-gesture.</para>
    ///
    /// <para>THAT CONCLUSION WAS WRONG, and the counter ModBuild 290 shipped to test it is what
    /// says so — see the block comment on <see cref="LiveCookPeakMs"/> for the full arithmetic. A
    /// cook of one of his capes is 1.3-2.5 ms, not 25-165 ms.</para>
    ///
    /// <para>MEASURED END TO END (<c>ThrottleBench</c>, <c>--bench=throttle</c>): three cloths, the
    /// script grow 1 → 2.5 → pause → shrink 2.5 → 1 → release, normal incoherence sampled EVERY
    /// frame of both moving windows, on the same 29×29 sheet the ModBuild 290 tables used.</para>
    /// <list type="table">
    /// <item><term>arm</term><description> GROW worst/mean | SHRINK worst/mean | SETTLED</description></item>
    /// <item><term>ModBuild 289 (pin only)</term><description> 0.0000 / 0.0000 | 1.1085 / 0.8503 | 0.0000</description></item>
    /// <item><term>ModBuild 290 (shipped)</term><description> 0.3097 / 0.1975 | 1.2202 / 1.0445 | 0.0000</description></item>
    /// <item><term>NULL (290 run twice)</term><description> 0.3097 / 0.1975 | 1.2202 / 1.0445 | 0.0000</description></item>
    /// <item><term>live, period 6</term><description>  0.0000 / 0.0000 | <b>0.0053 / 0.0001</b> | 0.0000</description></item>
    /// <item><term>live, period 12</term><description> 0.0000 / 0.0000 | 0.0767 / 0.0055 | 0.0000</description></item>
    /// <item><term>live, period 30</term><description> 0.0000 / 0.0000 | 0.9735 / 0.1674 | 0.0000</description></item>
    /// </list>
    /// <para>The settled cape is 0.0000 in EVERY arm — the state the user says is already fine is
    /// untouched, which is the standing constraint on this lane. And the ModBuild 290 row is the
    /// other thing this table found: at 2.5× the stiffness remedy is not merely useless, it makes
    /// the GROW direction worse (0.0000 → 0.1975) and the shrink worse than doing nothing. See
    /// <see cref="ApplyStretch"/>.</para>
    ///
    /// <para>TWO FRAMES PER CLOTH, ONE CLOTH AT A TIME, exactly as <see cref="StepCook"/>: the
    /// coefficients go up and the component goes down on one frame, and the enable on the next is
    /// the cook. A <c>false</c>/<c>true</c> pair inside ONE frame does not cook at all. The phase
    /// offset of <c>2*i</c> against a period of at least <c>2 * cloths</c> is what keeps two cloths
    /// from being disabled together, which is the invariant <c>FigureGrab.ClothCooks</c>' `worst
    /// frame` reports and which must stay at 1.</para>
    ///
    /// <para>THE ONE-FRAME COST, named rather than hidden: a cloth is <c>enabled == false</c> for
    /// one frame of each cook and a disabled <c>SkinnedMeshRenderer</c> draws the bare skinned pose
    /// with no drape. The harness measured that as per-frame vertex motion ("jitter") and it does
    /// not dominate: worst-frame jitter over the shrink is 0.142 at period 6 against the ModBuild
    /// 289 pin's own 0.074, on a cape whose incoherence went from 0.85 to 0.0001. Longer periods
    /// are WORSE on this column, not better (0.28 at period 9, 0.38 at period 18), because a fabric
    /// left staler for longer has further to jump when it is finally corrected — which is why the
    /// period is floored at the fastest measured arm rather than the slowest.</para>
    /// </summary>
    private static void StepLiveCook(Tracked t)
    {
        // FINISH BEFORE STARTING, and the order is load-bearing. A cloth put down on the previous
        // frame must come up on this one; if a start ran first it could put the same cloth down
        // again and it would never be enabled at all.
        for (int i = 0; i < t.Cloths.Count; i++)
            FinishLiveCook(t, i);

        int period = t.LivePeriod > 0 ? t.LivePeriod : LiveCookMinPeriod;
        for (int i = 0; i < t.Cloths.Count && i < t.LiveDown.Count; i++)
        {
            if (t.LiveDown[i])
                continue;
            int phase = t.GestureFrames - 2 * i;
            if (phase < 0 || phase % period != 0)
                continue;

            Cloth c = t.Cloths[i];
            if (c == null || !c.enabled)
                continue;
            // Has the body moved since this cloth was last baked? A gesture stays Suspended for a
            // further SettleFrames after the player stops moving it, and cooking on schedule
            // through that window buys nothing at all.
            float last = i < t.LiveFactor.Count ? t.LiveFactor[i] : 1f;
            if (Mathf.Abs(t.Factor - last) <= last * FactorEpsilon)
                continue;

            // The coefficients go up at the size the body is at RIGHT NOW and at full slack — the
            // cape is not being pinned on this path, it is being allowed to simulate correctly.
            if (BuildInto(t, i, 1f, t.Factor))
            {
                c.coefficients = t.Scratch[i];
                PerfMonitor.Count("FigureGrab.ClothSeeds");
            }
            // Stashed BEFORE the disable — see Tracked.LiveSpheres for why this is not optional.
            t.LiveSpheres = c.sphereColliders;
            t.LiveCapsules = c.capsuleColliders;
            c.enabled = false;
            t.LiveDown[i] = true;
            return;   // at most one cloth goes down per frame
        }
    }

    /// <summary>Frame two of a live cook: the enable, which is the cook. Also the drain every exit
    /// from the live path has to call — a cloth left <c>enabled == false</c> is a cape that is a
    /// plain skinned mesh for the rest of the figure's life.</summary>
    private static void FinishLiveCook(Tracked t, int i)
    {
        if (i < 0 || i >= t.LiveDown.Count || !t.LiveDown[i])
            return;
        t.LiveDown[i] = false;

        Cloth? c = i < t.Cloths.Count ? t.Cloths[i] : null;
        if (c == null || c.enabled)
        {
            // Either the cloth went away, or somebody else enabled it between our two frames — the
            // game does exactly that (ActorBehaviour.cs:245-252 disables every m_Clothes entry and
            // its LateUpdate re-enables them two frames later), and that enable cooks the same
            // fabric ours would have. Nothing owed but the stash, which must not survive into the
            // NEXT cloth's cook and be written onto it.
            if (c != null && c.enabled)
            {
                if (t.LiveSpheres != null) c.sphereColliders = t.LiveSpheres;
                if (t.LiveCapsules != null) c.capsuleColliders = t.LiveCapsules;
            }
            t.LiveSpheres = null;
            t.LiveCapsules = null;
            return;
        }

        int verts = t.Pristine.Count > i && t.Pristine[i] != null ? t.Pristine[i].Length : 0;
        using (PerfMonitor.Scope("FigureGrab.Cloth.Cook"))
            c.enabled = true;
        PerfMonitor.Count("FigureGrab.ClothCooks");
        PerfMonitor.Count("FigureGrab.ClothCookVerts", verts);
        c.ClearTransformMotion();

        // Put the free hand's colliders back on the far side of the enable. Restoring what was
        // there rather than what we think should be there: these arrays belong to
        // FigureClothHands, and on a figure the free hand is not near they are simply the empty
        // arrays we read a frame ago.
        if (t.LiveSpheres != null)
            c.sphereColliders = t.LiveSpheres;
        if (t.LiveCapsules != null)
            c.capsuleColliders = t.LiveCapsules;
        t.LiveSpheres = null;
        t.LiveCapsules = null;

        // A SENTINEL, NOT THE FACTOR, and this is what keeps the SETTLED cape bit-identical to what
        // ModBuild 290 ships. A live cook bakes the fabric at whatever size the body happened to be
        // at on that frame, which is within a few frames of the final one but not equal to it. If
        // this recorded that size, NeedsCookAt would find it inside FactorEpsilon of the settle
        // factor, the settle sequence would skip the cloth, and the figure would come to rest on a
        // fabric baked at 2.4987 instead of at 2.5. 0 is never a legitimate factor (Note rejects
        // any factor <= 0), so it reads as "unknown" and every cloth is re-cooked on the settle at
        // exactly the size it settled at — which is precisely the sequence that runs today.
        if (i < t.CookedFactor.Count)
            t.CookedFactor[i] = 0f;
        // The HONEST record of what this cook baked, kept separately because CookedFactor is now
        // carrying a sentinel rather than a size. It is what the next scheduled slot compares
        // against to decide whether the body has moved at all.
        if (i < t.LiveFactor.Count)
            t.LiveFactor[i] = t.Factor;
    }

    /// <summary>Does ANY managed cloth's fabric disagree with the size the figure settled at? The
    /// per-figure form of <see cref="NeedsCookAt"/>; a figure with no cloths answers false.</summary>
    private static bool NeedsCook(Tracked t, float factor)
    {
        for (int i = 0; i < t.Cloths.Count; i++)
            if (NeedsCookAt(t, i, factor))
                return true;
        return false;
    }

    /// <summary>Does cloth <paramref name="i"/>'s fabric disagree with <paramref name="factor"/>?
    /// The comparison is RELATIVE and uses the same <see cref="FactorEpsilon"/> as everything else
    /// here, because there is no threshold below which skipping the cook is free — the error
    /// saturates almost at once rather than scaling with the mismatch (measured: mean edge 0.941
    /// against a positive control's 1.025 at a factor of only 1.08).</summary>
    private static bool NeedsCookAt(Tracked t, int i, float factor)
    {
        if (i < 0 || i >= t.CookedFactor.Count)
            return true;   // never seen — assume it owes one; the safe direction is to cook
        float cooked = t.CookedFactor[i];
        return Mathf.Abs(factor - cooked) > cooked * FactorEpsilon;
    }

    /// <summary>
    /// Stand the re-cook sequence down because the size started moving again, and go back to
    /// pinning.
    ///
    /// <para>WHY THIS EXISTS, and it is a bug fix rather than a tuning choice. Through ModBuild 289
    /// <see cref="Advance"/> handed every frame to <see cref="StepCook"/> for as long as
    /// <see cref="Tracked.Cooking"/> was set, and <see cref="Note"/> could set
    /// <see cref="Tracked.Suspended"/> underneath it. The ramp therefore never ran: a cloth the
    /// sequence had ALREADY cooked was left at its full settled coefficients — unpinned, simulating
    /// — while the player went on changing the size, which is precisely the state the round's
    /// measurements name as the mush (SHRINK / STALE: normal incoherence 0.377 against the positive
    /// control's 0.027). And because <see cref="Release"/> re-entered with
    /// <see cref="Tracked.CookIndex"/> back at 0, the sequence restarted from the first cloth, so
    /// the window lasted as long as the gesture did. A cook frame on his rig is 25-165 ms, so six
    /// of them is a third of a second of wall clock with no pin on the figure in his hand.</para>
    ///
    /// <para>The one cost is the enable below. A cloth caught between the two frames of its own
    /// cook is DOWN, and a disabled Cloth never simulates again unless somebody enables it —
    /// nothing else in this file writes <c>enabled</c>. That enable is a cook and it is paid, once
    /// per abort, because the alternative is a cape that is permanently a plain skinned mesh.</para>
    /// </summary>
    private static void AbortCook(Tracked t)
    {
        if (t.CookDown && t.CookIndex < t.Cloths.Count)
        {
            Cloth c = t.Cloths[t.CookIndex];
            if (c != null && !c.enabled)
            {
                int verts = t.Pristine.Count > t.CookIndex && t.Pristine[t.CookIndex] != null
                    ? t.Pristine[t.CookIndex].Length : 0;
                using (PerfMonitor.Scope("FigureGrab.Cloth.Cook"))
                    c.enabled = true;
                PerfMonitor.Count("FigureGrab.ClothCooks");
                PerfMonitor.Count("FigureGrab.ClothCookVerts", verts);
                c.ClearTransformMotion();
                // That enable DID bake this cloth's fabric, at SeededFactor, so its own record has
                // to say so — otherwise the skip in StepCook would cook it again on the next
                // settle, and worse, the record would be a lie about what the cloth is carrying.
                if (t.CookIndex < t.CookedFactor.Count)
                    t.CookedFactor[t.CookIndex] = t.SeededFactor;
                if (t.CookIndex < t.LiveFactor.Count)
                    t.LiveFactor[t.CookIndex] = t.SeededFactor;
            }
        }

        // Nothing else is written to CookedFactor here. It is PER CLOTH (see its declaration), so
        // the cloths this sequence reached already record the size they were baked at and the ones
        // it did not still record the size they were baked at before — which is precisely the
        // bookkeeping a half-finished stagger needs, and precisely what one figure-wide number
        // could not express.
        t.Cooking = false;
        t.CookDown = false;
        t.Dirty = true;   // the cloths carry full settled coefficients; the pin must be re-uploaded
        PerfMonitor.Count("FigureGrab.ClothCookAborts");
    }

    /// <summary>
    /// Write every managed cloth's <c>stretchingStiffness</c> as <c>authored × weight</c>, once,
    /// and only when the weight has actually moved.
    ///
    /// <para>=== THE PIN AND THE FABRIC FIGHT EACH OTHER ON THE WAY DOWN (ModBuild 290) ===</para>
    ///
    /// <para>USER REPORT (hardware test, ModBuild 289, verbatim): "Achte auf die Umhänge. Beim
    /// größer machen werden sie steif und beim kleiner machen wird da ein Polygon matsch draus.
    /// Sobald ich loslasse ist alles wieder ok, aber ich würde gerne das auch während dem
    /// skallieren alles intakt bleibt, inklusive der Reaktion."</para>
    ///
    /// <para>Both halves of that are ONE mechanism with opposite sign, and the pin is a party to
    /// both. <c>maxDistance</c> is a SOFT constraint that the solver satisfies alongside the
    /// fabric's stretching constraint, not instead of it. While the size moves, the fabric is still
    /// cooked at the size the figure last settled at:
    /// <list type="bullet">
    /// <item>GROWING, the rest lengths are too SHORT for the body. Pulling a taut sheet onto its
    /// skinned pose leaves it taut and flat — "steif". Nothing is buckled; there is simply no fold
    /// left anywhere in it.</item>
    /// <item>SHRINKING, the rest lengths are too LONG. The surplus length has to go somewhere, and
    /// what it does is buckle through the surface — "Polygon matsch".</item>
    /// </list>
    /// MEASURED, 29x29 = 841 vertices (his widest cape is 771), 300 settle frames, every arm
    /// normalised by the root scale, against a POSITIVE control born at the target scale and a NULL
    /// control that is the positive arm run twice (0.00000 drift). "Incoherence" is the mean
    /// <c>1 - dot</c> between neighbouring quad normals: a smooth drape is near 0 and a surface
    /// folded back through itself is not.
    /// <list type="table">
    /// <item><term>SHRINK 1.345 -> 1</term><description> ..... mean edge | incoherence | close non-neighbour pairs</description></item>
    /// <item><term>POSITIVE (born at 1)</term><description> .. 1.022 | 0.027 | 0</description></item>
    /// <item><term>re-cooked (the settle)</term><description> 1.022 | 0.027 | 0</description></item>
    /// <item><term>stale fabric, simulating</term><description> 1.333 | 0.377 | 0</description></item>
    /// <item><term>PINNED — ModBuild 289</term><description> . 1.286 | <b>0.459</b> | 0</description></item>
    /// <item><term>PINNED + bendingStiffness 0</term><description> 1.286 | 0.458 | 0</description></item>
    /// <item><term>PINNED + useTethers false</term><description> 1.286 | 0.459 | 0</description></item>
    /// <item><term>PINNED + <b>stretchingStiffness 0</b></term><description> 1.112 | <b>0.037</b> | 0</description></item>
    /// </list>
    /// The pin was the WORST arm on the way down — worse than not pinning at all — and taking the
    /// stretching constraint out of the argument moves it from 0.459 to 0.037, which is within 1.4x
    /// of the positive control's own 0.027. Bending and tethers are inert to three decimal places,
    /// so this is the stretching constraint specifically and not "the fabric" in general. The
    /// rendered wireframes say the same thing in one look: the ModBuild 289 pin on a shrink is a
    /// jagged band, and with this write it is a line.</para>
    ///
    /// <para>=== AND ALL OF THAT WAS MEASURED AT 1.345×, WHICH IS NOT A FACTOR HE USES. ModBuild 291
    /// WITHDRAWS THE RAMP. ===</para>
    ///
    /// <para>The table above is real and it reproduces exactly — <c>FactorArmsBench</c> re-measured
    /// it on the same mesh and got mean edge 1.1119 and incoherence 0.0372 for the last row, to four
    /// decimal places. It is also the ONLY factor it was ever measured at, because 1.345 is what one
    /// ModBuild 289 log happened to settle at. His stretch gesture clamps at [0.417 .. 2.503]; his
    /// ModBuild 290 log settles at <b>2.5×</b> and drives to the end stop five times in one session.
    /// The stale-fabric error is a RATIO between rest length and body, so it grows with the factor,
    /// and the remedy does not survive the growth. Same bench, same sheet, same arms:</para>
    /// <list type="table">
    /// <item><term>SHRINK, incoherence</term><description> ... PINNED (289) -> PIN+NOSTRETCH (290)</description></item>
    /// <item><term>1.345 -> 1</term><description> ......... 0.4688 -> <b>0.0372</b>   the remedy works, 12.6x</description></item>
    /// <item><term>1.7 -> 1</term><description> ........... 0.9680 -> <b>1.1862</b>   WORSE</description></item>
    /// <item><term>2.0 -> 1</term><description> ........... 1.1323 -> <b>1.2972</b>   WORSE</description></item>
    /// <item><term>2.5 -> 1  (his)</term><description> .... 1.1719 -> <b>1.2950</b>   WORSE</description></item>
    /// <item><term>3.5 -> 1</term><description> ........... 1.2660 -> 1.2904          WORSE</description></item>
    /// <item><term>GROW, incoherence</term><description> ..... PINNED (289) -> PIN+NOSTRETCH (290)</description></item>
    /// <item><term>1 -> 1.345</term><description> ......... 0.0000 -> 0.0000          neutral</description></item>
    /// <item><term>1 -> 1.7</term><description> ........... 0.0000 -> 0.0106          worse</description></item>
    /// <item><term>1 -> 2.0</term><description> ........... 0.0000 -> 0.1230          worse</description></item>
    /// <item><term>1 -> 2.5  (his)</term><description> .... 0.0000 -> <b>0.3142</b>   WORSE</description></item>
    /// <item><term>1 -> 3.5</term><description> ........... 0.0106 -> 0.4685          worse</description></item>
    /// </list>
    /// <para>The mean edge tells the same story more bluntly: on the shrink at 2.5 the pinned cape
    /// sits at 2.35× its authored edge and the no-stretch one at 3.39× with a worst edge of 7.09 and
    /// sixteen self-intersecting vertex pairs. With the fabric's stretching constraint gone there is
    /// nothing left to stop a cape that is now far too big for its body folding through itself —
    /// which is the ModBuild 286 failure, rediscovered at a larger factor. <b>The remedy inverts
    /// between 1.345 and 1.7, in BOTH directions.</b> "Das Problem ist 1:1 noch genau gleich" is what
    /// a 1.345-only fix looks like to somebody scaling to 2.5.</para>
    ///
    /// <para>SO THE RAMP IS GONE and this method is now always called with a weight of 1, i.e. it
    /// writes each cloth's authored value back. It is kept rather than deleted for two reasons: the
    /// restore is a real safety net (<see cref="StepCook"/> invalidates the latch, and a value left
    /// low by an older build or by the game would otherwise stand), and this comment is the record
    /// of a remedy that was measured, shipped, and withdrawn — a lever whose SIGN depends on the
    /// factor is not one to rediscover.</para>
    ///
    /// <para>WHAT REPLACES IT is not another way to make a stale fabric behave. It is
    /// <see cref="StepLiveCook"/>, which stops the fabric being stale.</para>
    ///
    /// <para>IT COSTS 0.004 ms AND IT DOES NOT COOK — measured on a live, enabled 841-vertex cloth,
    /// 8 reps, against the two anchors in the same run: a full coefficients upload is 0.004 ms and
    /// an enable transition is 10.8 ms. The component reads <c>enabled == true</c> afterwards.
    /// Writing the SAME value again is 0.003 ms, i.e. the setter is not the cost either way, but it
    /// is still change-latched because there is no reason to write it 90 times a second.</para>
    /// </summary>
    private static void ApplyStretch(Tracked t, float weight)
    {
        if (t.StretchWeight == weight)
            return;
        t.StretchWeight = weight;
        for (int i = 0; i < t.Cloths.Count && i < t.PristineStretch.Count; i++)
        {
            Cloth c = t.Cloths[i];
            if (c == null)
                continue;
            // At weight 1 the authored float is written back, not `authored * 1f` — the same
            // reasoning as the identity snap in Release: a figure back at its board size must end
            // up with exactly what the artist authored, not with it to within a rounding.
            float value = weight >= 1f ? t.PristineStretch[i] : t.PristineStretch[i] * weight;
            c.stretchingStiffness = value;

            // RECORDED, NOT LOGGED HERE. This method runs once per ramp step per cloth and a line
            // per write would be ninety a second; what a reader needs is one line per GESTURE
            // saying whether the ramp reached the cloths at all and what the lowest value it wrote
            // was — see LogGesture, which is where these two fields are read. The distinction that
            // matters and that a user report cannot make is between a remedy that never executed
            // and a remedy that executed and did nothing: `writes 0` is the first, and `writes N,
            // lowest 0.000, authored 0.000` is the second.
            t.StretchWrites++;
            if (float.IsNaN(t.MinStretchWritten) || value < t.MinStretchWritten)
                t.MinStretchWritten = value;
        }
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
        if (t.Cooking)
        {
            if (!t.Suspended)
            {
                // The re-cook sequence owns every cloth write while it runs, including the
                // coefficient upload, so the ramp stands down rather than fighting it for the
                // same array.
                StepCook(t);
                return;
            }

            // The size started moving again mid-sequence. Stand it down and fall through to the
            // ramp on THIS frame rather than the next — see AbortCook for what the frames in
            // between used to look like.
            AbortCook(t);
        }

        // THE LIVE PATH. While the size is moving and this figure's cloths are small enough to
        // re-cook inside a frame, the fabric follows the size instead of the cape being pinned —
        // see StepLiveCook. It owns every cloth write while it runs, exactly as the settle sequence
        // does, so the ramp below stands down rather than fighting it for the same array.
        if (t.Suspended && t.LiveCookable && t.Cloths.Count > 0)
        {
            // The pin is not in use on this path, so the weight is held AT 1 rather than ramped to
            // 0. Snapping rather than ramping is deliberate: the ramp exists to hide the pin
            // engaging, and there is no pin to hide.
            t.Weight = 1f;
            t.Dirty = false;
            ApplyStretch(t, 1f);   // authored stiffness, verbatim — the fabric is the thing working
            StepLiveCook(t);
            return;
        }

        float target = t.Suspended ? 0f : 1f;
        if (t.Weight != target)
        {
            float step = FadeSeconds > 0f ? Time.unscaledDeltaTime / FadeSeconds : 1f;
            float before = t.Weight;
            t.Weight = Mathf.MoveTowards(t.Weight, target, step);
            if (t.Weight != before)
                t.Dirty = true;
            // Recorded HERE and not in ApplyStretch, so a figure with no managed cloths still
            // reports whether its ramp completed — that is the case where the answer to "did the
            // remedy run" is "there was nothing for it to run on", and it must be distinguishable.
            if (t.Weight < t.MinWeightReached)
                t.MinWeightReached = t.Weight;
        }

        if (t.Cloths.Count == 0)
        {
            t.Dirty = false; // nothing simulated on this figure — the ramp still runs, it just
            return;          // has nowhere to land, and the flag must not latch true forever
        }

        // ALWAYS 1, i.e. always the authored value — the ModBuild 290 ramp is withdrawn and the
        // table that withdrew it is on ApplyStretch. Driving stretchingStiffness down with the pin
        // helps on a shrink at 1.345x (0.4688 -> 0.0372) and hurts at every larger factor measured,
        // in both directions, including the 2.5x his gesture actually reaches (grow 0.0000 ->
        // 0.3142, shrink 1.1719 -> 1.2950). This call stays because the RESTORE is load-bearing:
        // StepCook invalidates the latch, and a value left low by an older build must not stand.
        // Outside a change this is a float compare and a return.
        ApplyStretch(t, 1f);

        if (!t.Dirty)
            return;
        t.Dirty = false;

        bool settled = !t.Suspended && t.Weight >= 1f;
        using (PerfMonitor.Scope("FigureGrab.Cloth.Seed"))
        {
            for (int i = 0; i < t.Cloths.Count; i++)
            {
                Cloth c = t.Cloths[i];
                if (c == null || !BuildInto(t, i, t.Weight, t.SeededFactor))
                    continue;

                c.coefficients = t.Scratch[i];
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

    /// <summary>
    /// Rebuild cloth <paramref name="i"/>'s coefficient array into its own scratch buffer at one
    /// ramp <paramref name="weight"/> and one size <paramref name="factor"/>. Returns false when
    /// there is nothing to write (no painted constraints, or a capture that did not take), in which
    /// case the buffer is untouched and the caller must not upload it.
    ///
    /// <para>ONE COEFFICIENT AT ONE RAMP WEIGHT — the three cases, written as three loops.</para>
    ///
    /// <para>At weight 1 the authored value is written back verbatim when the vertex is
    /// unconstrained, and as <c>authored × factor</c> otherwise. That is the ModBuild 137
    /// correction, unchanged: <c>maxDistance</c> and <c>collisionSphereDistance</c> are absolute
    /// distances against the authored body, so they must follow the figure's size.</para>
    ///
    /// <para>At weight 0 everything goes to the PIN FLOOR, which pins every vertex onto its skinned
    /// position and makes the cape a plain skinned mesh — the ModBuild 137 guarantee, at the cost of
    /// an upload instead of a fabric cook. The floor is <see cref="PinFloorFraction"/> of the cape's
    /// extent and NOT zero; that distinction is a bug fix and its evidence is on the constant.</para>
    ///
    /// <para>In between, the value ramps linearly. An UNCONSTRAINED vertex (Unity writes
    /// <c>float.MaxValue</c> — see the class comment) cannot ramp from its own value:
    /// <c>float.MaxValue × weight</c> is still unconstrained for any weight above ~1e-30, and
    /// multiplying it by the factor overflows to a real infinity. It ramps from <c>bound</c>
    /// instead. The single discontinuity that leaves — <c>bound</c> just below weight 1 against
    /// <c>float.MaxValue</c> at weight 1 — is between two values that are both physically
    /// unconstrained for a cape.</para>
    ///
    /// <para>TWO THINGS ARE HAND-FLATTENED HERE AND BOTH WERE MEASURED, not assumed.
    /// (1) The weight branch is LOOP-INVARIANT, so it is taken once and not 2N times.
    /// (2) The unconstrained test is written INLINE rather than through
    /// <see cref="IsUnconstrained"/>: Mono does not inline it, and at 3721 vertices that is 7442
    /// calls per upload. Same gesture, same cloth, same harness: a per-vertex helper call for the
    /// whole value 0.49 ms per upload; the weight branch hoisted with the helper kept for the test
    /// 0.10 ms p50 / 1.98 ms worst; both flattened (this code) 0.101 ms p50, 0.119 ms p90, 0.97 ms
    /// cold — against 29.1 ms for ONE <c>SetEnabledFading(true)</c> in the same run.
    /// <c>&gt;= float.MaxValue</c> is exactly the positive half of <see cref="IsUnconstrained"/> —
    /// positive infinity satisfies it, and a NaN falls through to the multiply in both forms. The
    /// floor is applied with a compare-and-select for the same reason, not a <c>Mathf.Max</c>
    /// call.</para>
    /// </summary>
    private static bool BuildInto(Tracked t, int i, float weight, float factor)
    {
        ClothSkinningCoefficient[] pristine = t.Pristine[i];
        ClothSkinningCoefficient[] next = t.Scratch[i];
        if (pristine == null || next == null || pristine.Length == 0
            || next.Length != pristine.Length)
            return false;

        float bound = t.RampBound[i];
        float floor = bound * PinFloorFraction;

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
                next[v].maxDistance = floor;
                next[v].collisionSphereDistance = floor;
            }
        }
        else
        {
            float k = factor * weight;
            float ramped = bound * weight;
            if (ramped < floor) ramped = floor;
            for (int v = 0; v < pristine.Length; v++)
            {
                float m = pristine[v].maxDistance;
                float s = pristine[v].collisionSphereDistance;
                float mv = m >= float.MaxValue ? ramped : m * k;
                float sv = s >= float.MaxValue ? ramped : s * k;
                next[v].maxDistance = mv > floor ? mv : floor;
                next[v].collisionSphereDistance = sv > floor ? sv : floor;
            }
        }

        return true;
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
    /// ONE LINE PER GESTURE: did the ModBuild 290 remedy run on this figure, what did it write, and
    /// HOW BIG was the size change it was asked to cover.
    ///
    /// <para>=== WHY THIS EXISTS, and it is the most important thing ModBuild 291 adds ===</para>
    ///
    /// <para>ModBuild 290 shipped a change to <c>stretchingStiffness</c> with a measured 9.4×
    /// improvement on the shrink direction, and the user's verbatim reply after testing it was: "Das
    /// Problem mit dem Skallieren von Figuren ist 1:1 noch genau gleich, ich sehe keinen Unterschied
    /// zu dem wie es im Video ist." A fix that never executed and a fix that executed and did
    /// nothing look IDENTICAL from that sentence, and they need opposite responses. The shipped
    /// <see cref="ApplyStretch"/> logged nothing at all, so the ModBuild 290 log could not tell them
    /// apart — the whole round after it had to be reasoned from counters that happened to be
    /// adjacent.</para>
    ///
    /// <para>THE THREE THINGS THIS LINE SETTLES, each of which has cost this project builds:</para>
    /// <list type="number">
    /// <item><b>Whether the ramp reached the cloths.</b> <c>writes</c> is how many
    /// <c>stretchingStiffness</c> assignments the gesture made. Zero, with cloths present, means
    /// the remedy never ran and the user's report carries no information about it at all.</item>
    /// <item><b>What it actually wrote.</b> The remedy's lever is <c>authored × weight</c>. If the
    /// artist shipped the cape at <c>stretchingStiffness</c> 0 — or at 0.05 — then driving it to 0
    /// is a no-op or nearly one, the write is INERT ON THAT ASSET, and no amount of harness work on
    /// a procedurally-built sheet whose authored value is Unity's default 1 would ever have said
    /// so. <c>authored</c> and <c>lowest written</c> are printed side by side for exactly that
    /// comparison.</item>
    /// <item><b>The size of the gesture.</b> Every arm table this lane has produced was measured at
    /// 1.345×, because that is the factor one ModBuild 289 log happened to settle at. His ModBuild
    /// 290 log settles at 2.5× and drives to the 2.503 gesture clamp repeatedly. The stale-fabric
    /// error is a RATIO between the fabric's rest lengths and the body they are stretched over, so
    /// it grows with the factor: a table at 1.345 is not evidence about 2.5. <c>range</c> and
    /// <c>peak</c> are on the line so a report about 2.5 can never again be answered with a
    /// measurement at 1.345.</item>
    /// </list>
    ///
    /// <para>It also separates "this figure has no cloths" from "this figure has cloths and the
    /// ramp never reached them": <see cref="Tracked.FoundCloths"/> is what the subtree scan saw and
    /// <see cref="Tracked.Cloths"/>.Count is what this file took responsibility for, and
    /// <see cref="Suspend"/> declines any cloth that is not <c>enabled</c>. Two numbers, printed
    /// together, because a single "0 cloths" cannot say which.</para>
    ///
    /// <para>Capped at <see cref="GestureLineCap"/> lines per session. His whole ModBuild 290 test
    /// contained eight gestures.</para>
    /// </summary>
    private static void LogGesture(Tracked t, float factor)
    {
        if (t.Root == null || _gestureLines >= GestureLineCap)
            return;
        // A gesture that never moved the size is not a gesture; Release runs on every settle.
        if (Mathf.Abs(t.GestureMaxFactor - t.GestureMinFactor) <= FactorEpsilon
            && Mathf.Abs(factor - t.GestureFromFactor) <= FactorEpsilon)
            return;
        _gestureLines++;

        string cloths;
        if (t.Cloths.Count == 0)
        {
            cloths = t.FoundCloths == 0
                ? "MANAGED CLOTHS: 0 of 0 in the subtree — this figure carries no Cloth at all, so "
                  + "neither the pin nor the stiffness ramp has anything to act on and this gesture "
                  + "says NOTHING about either of them"
                : $"MANAGED CLOTHS: 0 of {t.FoundCloths} in the subtree — every Cloth on this figure "
                  + "was found DISABLED at the suspend scan and skipped (Suspend declines a cloth "
                  + "that is not enabled, because a non-simulating cloth already scales as a plain "
                  + "skinned mesh). THE RAMP NEVER REACHED THEM. If the user is complaining about a "
                  + "cape on THIS figure, that is the whole answer and no remedy tuning is relevant";
        }
        else
        {
            var sb = new System.Text.StringBuilder(160);
            sb.Append($"MANAGED CLOTHS: {t.Cloths.Count} of {t.FoundCloths} in the subtree [");
            for (int i = 0; i < t.Cloths.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                int n = i < t.Pristine.Count && t.Pristine[i] != null ? t.Pristine[i].Length : 0;
                float authored = i < t.PristineStretch.Count ? t.PristineStretch[i] : float.NaN;
                sb.Append($"c{i} {n}p authored stretchingStiffness {authored:0.####}");
            }
            sb.Append(']');
            cloths = sb.ToString();
        }

        string live = t.Cloths.Count == 0
            ? "LIVE RE-COOK: n/a (no managed cloths)"
            : t.LiveCookable
                ? $"LIVE RE-COOK: ON, one cloth re-cooked every {t.LivePeriod} frame(s) — the fabric "
                  + "FOLLOWED the size for this whole gesture and the pin was never engaged"
                : "LIVE RE-COOK: OFF — this figure's largest cape does not fit a cook inside one "
                  + $"frame at {LiveCookMicrosecondsPerParticle:0.#} us/particle against a "
                  + $"{LiveCookPeakMs:0.#} ms ceiling, OR the amortised budget demands a period past "
                  + $"{LiveCookMaxPeriod}. It kept the ModBuild 285 pin, which is where the "
                  + "'steif' / 'Polygon matsch' the user filmed comes from — so THIS figure is one "
                  + "the ModBuild 291 change does not reach, and that is a size question, not a "
                  + "mechanism one";

        VRLog.Info("FigureGrab",
            $"CLOTH GESTURE {t.Root.name}: {live}. size went {t.GestureFromFactor:0.###}× → {factor:0.###}× "
            + $"of board size, RANGE [{t.GestureMinFactor:0.###} .. {t.GestureMaxFactor:0.###}]×, "
            + $"over {t.GestureFrames} moving frame(s). "
            + $"PIN RAMP reached weight {t.MinWeightReached:0.###} (0 = fully pinned; a gesture that "
            + "never reaches 0 never applied the pin OR the stiffness remedy at full strength, and "
            + "'no improvement' from a remedy at partial strength carries no information). "
            + $"STRETCHINGSTIFFNESS: {t.StretchWrites} write(s) this gesture, lowest value written "
            + $"{(float.IsNaN(t.MinStretchWritten) ? -1f : t.MinStretchWritten):0.####}. {cloths}. "
            + "HOW TO READ THIS. The ModBuild 290 stiffness RAMP is withdrawn — it helps a shrink at "
            + "1.345× and hurts at every larger factor measured, in both directions (see "
            + "ApplyStretch) — so `lowest written` should now equal the AUTHORED value on every "
            + "gesture, and this line's job is to report what that authored value IS. It is the "
            + "lever any future stiffness remedy would have: an asset shipped at 0.05 cannot be "
            + "driven anywhere useful and no harness built on a procedural sheet (Unity's default "
            + "is 1) would ever have said so. `writes 0` with managed cloths present would mean the "
            + "restore never reached them at all. AND READ THE RANGE: every arm table before "
            + "ModBuild 291 was measured at 1.345×, a factor taken from one ModBuild 289 log; the "
            + "stale-fabric error is a RATIO and grows with the factor, so a peak of 2.5 here is "
            + "NOT the condition those tables describe. That substitution is the single mistake "
            + "that produced 'das Problem ist 1:1 noch genau gleich'.");
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
        if (Mathf.Abs(factor - 1f) <= FactorEpsilon || t.Root == null)
            return;

        // ONE LATCH PER POPULATION, NOT ONE PER SESSION. This was a single `_logged` flag through
        // ModBuild 286, and in his ModBuild 286 log it fired on the FIRST figure resized in the
        // session — which had zero enabled Cloths — and then never printed again. The line read
        // "0 simulated, 0 constrained vertices" on a session where FigureGrab.ClothSeeds counted
        // 984 uploads with a worst frame of 3, i.e. cloths were being managed the whole time on
        // OTHER figures. A census that latches on the first sample it sees is not a census; the
        // cloth-less case and the cloth-bearing case are different populations and each gets one
        // line. (See MEMORY: "a held instrument reads as dead", "a summary stat is not the field".)
        // ONE LATCH PER FIGURE FOR THE CLOTH-BEARING CASE (ModBuild 291). Per POPULATION was still
        // not enough, and his ModBuild 290 log is the proof: the one line it printed described a
        // figure whose widest cape is 82 particles, and it was then read — by me — as evidence
        // about a session in which FigureGrab.ClothCookVerts records seventeen cooks with a worst
        // frame of 143. It also happened to be the ONLY figure it described, while the log's own
        // grab lines name two (LivingBonesID and BanditGuardID). One sample is not a census of a
        // population, however carefully the population was defined.
        bool hasCloths = t.Cloths.Count > 0;
        if (hasCloths)
        {
            int rootId = t.Root.GetInstanceID();
            if (_censused.Contains(rootId) || _censused.Count >= CensusCap)
                return;
            _censused.Add(rootId);
        }
        else
        {
            if (_loggedEmpty)
                return;
            _loggedEmpty = true;
        }

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

        VRLog.Info("FigureGrab", DescribeConfig(t));
        VRLog.Info("FigureGrab",
            $"FIGURE SCALE {t.Root.name} settled at {factor:0.###}× of its board size — subtree: "
            + $"{transforms.Length} transforms, {renderers.Length} renderers ({skinned} skinned, which "
            + "follow the root scale exactly and need nothing). Cloth (capes/cloth — the reported "
            + $"parts): {t.Cloths.Count} simulated, {verts} constrained vertices across them "
            + $"(widest {widest}), of which {unconstrained} are authored UNCONSTRAINED "
            + "(float.MaxValue — those are ramped against the cape's own extent, never multiplied); "
            + $"{empty} cloth(s) have no painted constraints to rescale. The per-vertex "
            + "maxDistance/collisionSphereDistance are rescaled from the authored metres and the "
            + "simulation is PINNED to the skinned pose while the size MOVES; when it SETTLES each "
            + "cloth's fabric is re-cooked once, ONE CLOTH PER FRAME, because PhysX bakes edge rest "
            + "lengths in world units at enable time and never re-derives them from a transform "
            + "scale — without that cook a cape at 1.345x simulates against rest lengths sized for "
            + "1x and its edges settle at 0.793 of their authored length (ModBuild 285's regression: "
            + "\"hängt jetzt tiefer und kann nicht mehr als Umhang bezeichnet werden\"). Watch "
            + "[Perf] STEPS FigureGrab.Cloth.Seed / FigureGrab.Cloth.Cook and [Perf] COUNTS "
            + "FigureGrab.ClothSeeds / FigureGrab.ClothCooks — ClothCooks' `worst frame` must be 1, "
            + "which is the whole claim of the stagger; his ModBuild 283 worst frame was 172.93 ms "
            + "and that was three cloths cooking together. READ ClothCookVerts NEXT TO ClothCooks: "
            + "its `worst frame` is the LARGEST cape ever cooked in the session and total divided "
            + "by ClothCooks is the mean, which is the one number ModBuild 289 could not supply — "
            + "that log said Cloth.Cook cost 32-53 ms on average and 167.51 ms at worst while this "
            + "census, which fires once, happened to print a figure whose widest cape is 771. A "
            + "cook is 10-20 us per cloth vertex and NOTHING about a Cloth's configuration "
            + "multiplies that (self-collision 1.4x, virtual particles 1.2x, colliders 1.4x, raw "
            + "mesh vertex count 1.00x — the welded particle count IS the driver), so 167 ms is a "
            + "cape of roughly eleven thousand particles on some OTHER figure, and ClothCookVerts "
            + "says so or refutes it in one line. ClothCookAborts counts the gestures that resumed "
            + "while a stagger was still running. "
            + $"NOT HANDLED: {localScaled} of {particles.Length} ParticleSystem(s) use "
            + "ParticleSystemScalingMode.Local and therefore ignore the root scale by design "
            + "(reported, not changed — see FigureCloth); the figure's worldspace health/condition "
            + "panel is parented outside the figure root by the game "
            + "(ActorBehaviour.CreateWorldSpaceGUIElements) and no root scale can reach it.");
    }

    /// <summary>
    /// THE AUTHORED CONFIGURATION OF EVERY MANAGED CLOTH ON ONE FIGURE — every solver knob and the
    /// shape of the painted coefficient array, printed once per figure beside the census.
    ///
    /// <para>WHY. Every measurement behind ModBuild 290 was taken on a cloth this lane BUILT: a
    /// 29×29 procedural grid with a linear slack ramp, no colliders, and every solver property at
    /// Unity's default — which for <c>stretchingStiffness</c> is 1. A Gloomhaven cape is an
    /// authored asset and the mod cannot see the authoring. The remedy's whole lever is
    /// <c>authored stretchingStiffness × ramp weight</c>, so if the artist shipped the cape at 0.1
    /// the lever is a tenth of what the harness measured; if the game's own
    /// <c>clothSolverFrequency</c> write (ActorBehaviour.cs:126-128 sets
    /// <c>PhysicsController.SimplifyPhysicsRate</c> under <c>PlatformSetting.SimplifyPhysics</c>;
    /// PhysicsController.cs:53 otherwise restores 120) lands somewhere else, the solver is not the
    /// one the harness ran. None of that is guessable from here and all of it is one property read.
    /// </para>
    ///
    /// <para>The coefficient array gets its DISTRIBUTION and not a summary: how many vertices are
    /// hard-pinned at 0, how many are authored <c>float.MaxValue</c>, and the min/mean/max of the
    /// finite remainder in metres. A cape that is 90 % unconstrained is ramped against its own
    /// extent by <see cref="BuildInto"/> rather than multiplied by the factor, which is a
    /// completely different code path from the one every arm table exercised.</para>
    ///
    /// <para>Read once, per figure, on the same latch as the census — never per frame.</para>
    /// </summary>
    private static string DescribeConfig(Tracked t)
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("CLOTH CONFIG ").Append(t.Root != null ? t.Root.name : "<null>")
          .Append(" — the AUTHORED state of each managed cloth, which no harness on this project "
                  + "has ever had and every arm table has had to assume:");

        for (int i = 0; i < t.Cloths.Count; i++)
        {
            Cloth c = t.Cloths[i];
            if (c == null)
            {
                sb.Append($" | c{i} <destroyed>");
                continue;
            }

            int pinned = 0, free = 0, finite = 0;
            float mn = float.MaxValue, mx = 0f;
            double sum = 0d;
            ClothSkinningCoefficient[]? p = i < t.Pristine.Count ? t.Pristine[i] : null;
            if (p != null)
            {
                for (int v = 0; v < p.Length; v++)
                {
                    float m = p[v].maxDistance;
                    if (IsUnconstrained(m)) { free++; continue; }
                    if (m <= 0f) { pinned++; continue; }
                    finite++;
                    sum += m;
                    if (m < mn) mn = m;
                    if (m > mx) mx = m;
                }
            }
            float mean = finite > 0 ? (float)(sum / finite) : 0f;
            if (finite == 0) mn = 0f;

            sb.Append($" | c{i} '{c.name}' {(p != null ? p.Length : 0)}p")
              .Append($" stretchingStiffness {c.stretchingStiffness:0.####}")
              .Append($" bendingStiffness {c.bendingStiffness:0.####}")
              .Append($" useTethers {c.useTethers}")
              .Append($" useVirtualParticles {c.useVirtualParticles:0.###}")
              .Append($" selfCollisionDistance {c.selfCollisionDistance:0.####}")
              .Append($" selfCollisionStiffness {c.selfCollisionStiffness:0.###}")
              .Append($" solverFrequency {c.clothSolverFrequency:0.#}")
              .Append($" damping {c.damping:0.###}")
              .Append($" friction {c.friction:0.###}")
              .Append($" worldVelocityScale {c.worldVelocityScale:0.###}")
              .Append($" worldAccelerationScale {c.worldAccelerationScale:0.###}")
              .Append($" stiffnessFrequency {c.stiffnessFrequency:0.#}")
              .Append($" useGravity {c.useGravity}")
              .Append($" enabled {c.enabled}")
              .Append($" | coefficients: {pinned} hard-pinned at 0, {free} authored float.MaxValue, "
                      + $"{finite} finite (min {mn:0.####} m, mean {mean:0.####} m, max {mx:0.####} m)");
        }

        sb.Append(". THE ONE THAT DECIDES THE ModBuild 290 REMEDY IS `stretchingStiffness`: the ramp "
                  + "writes `authored × weight`, so an authored value at or near 0 makes that write "
                  + "a no-op no matter how correct the mechanism is. The one that decides whether "
                  + "the harness measured the right SOLVER is `solverFrequency` (the game writes it "
                  + "at ActorBehaviour.cs:128 and PhysicsController.cs:53). The one that decides "
                  + "whether the arm tables measured the right COEFFICIENT PATH is the float.MaxValue "
                  + "count: those vertices are ramped against the cape's own extent, not multiplied "
                  + "by the size factor, and every table so far was built on a cloth with none.");
        return sb.ToString();
    }
}
