# Build 615: native actor pose eligibility and safe offscreen transforms

Build 612's loaded Frame run spent about 2.06 ms/frame in ActorBars and skipped
zero of 121499 bone checks. Every detailed loop report had prepared bounds but
`sparseEligible=False`. Increasing the interval could not address that refusal.
The old stripped visual fixture proved skin-envelope mathematics; it did not
prove that a live publisher rig was eligible.

## Native admission

The blanket MonoBehaviour refusal is replaced by exact, individually audited
publisher types on the sampled skeleton's ancestors. An unknown subclass is not
admitted by inheritance. Native source references below are relative to the
read-only local `decompiled/` tree; these publisher files are not redistributed.

| Exact type | Native source behavior relevant to pose checks |
| --- | --- |
| CharacterManager | Equips separate children and changes native materials; no assignment to original skeleton bones (`GH.Runtime/CharacterManager.cs`). |
| ActorEvents | Queues continuation events; native Choreographer adds it to the Animator at actor construction, not in the body prefab (`GH.Runtime/Choreographer.cs`, lines 891/1074; `ActorEvents.cs`). |
| UnityGameEditorObject | Wrapper registry, pathing/coverage metadata and object layer; no original skeleton-bone writes (`GH.Runtime/UnityGameEditorObject.cs`). |
| VFXLookup | Effect metadata (`GH.Runtime/VFXLookup.cs`). |
| FootstepSound / AnimFXTrigger | Audio and separate spawned effects, not original bone assignments (same-named `GH.Runtime` sources). |
| EPOOutline.Outlinable / TargetStateListener | Outline targets and visibility notification (`GH.Runtime.FirstPass/EPOOutline/`). |
| EnemyShadowsDisabler / CharacterShadowsDisabler | Renderer/shadow settings (same-named `GH.Runtime` sources). |
| DetailsDisabler | Accepted only when every colocated provider is one of those exact audited shadow providers; arbitrary IDetailDisablerProvider callbacks remain unsafe. |
| AutomaticLOD | Only its native UnityLODGroup mode is admitted; other mesh/game-object modes remain conservative (`ThirdParty/AutomaticLOD.cs`). |
| DeathDissolve | Only without vertex animation and without membership in its live dissolve set (`GH.Runtime/DeathDissolve.cs`). |

The last two conditions use cached component identities and are checked live;
there is no repeated component/hierarchy inventory. Existing controller, skin
ownership, bone-parent, scale, rotation, transition, layer and blend restrictions
remain. Humanoid rigs and unknown procedural writers retain evaluated checks.

Another blanket refusal would result from auditing every state-machine callback
in a controller: unrelated attack/death callbacks coexist with idle states.
The production audit instead caches the actual current full-path state hash using
Unity 2021's `Animator.GetBehaviours(hash, layer)`. Only exact IdleSMB,
AnimationOffsetSMB, ToggleAlternativeIdleFxSMB and ProgressChoreographerSMB are
admitted. Their source changes native phase/speed, separate FX or continuation
events, not original bones. Unknown current-state callbacks retain full checks.

## Optional animation compromise

`[Optimize] OffscreenIdleAnimation` is wired by CoreModule with
`ScenarioIdleAnimationBudget.Install(host, enabled)` and restored at shutdown.
Its fresh Frame default is enabled; the ordinary PC default is disabled. Existing
saved values remain user-owned.

This narrowly changes an audited native body Animator originally using
AlwaysAnimate to CullUpdateTransforms only during a single original, event-free
idle loop. The idle-state vocabulary comes from Choreographer.IdleStates. Unity
continues the native state machine and root-motion computation while suppressing
offscreen retargeting, IK and bone-transform writes. See the
[Unity 2021.3 culling-mode documentation](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AnimatorCullingMode.CullUpdateTransforms.html).
Visible actors still evaluate normally. No Animator is disabled, manually stepped
or put into CullCompletely; no NPC/shared animation clock is changed.

Holding, native motion, transitions, eventful/non-idle states, controller changes,
option-off and actor release restore the owned original mode. Harmony prefixes
on the actual publisher MF.AnimatorPlay and actor locomotion commands restore
before native action dispatch. Missing action hooks fail open to original
evaluation. A foreign mode change is retained. Existing actor-bar adoption
provides the registry; there is no additional scene scan.
Native prefix entrypoints isolate optional-owner exceptions, disable the owner
and restore live records without blocking MF/locomotion continuation. A single
bounded lifecycle report describes a failure. A real Harmony fault injection
verifies that the actual MF call, state-exit event and subsequent action still run.

The **original Drake body prefab already authors CullUpdateTransforms**. This
option leaves that authored mode untouched and may provide no further saving
for it. Build 612's 99 AlwaysAnimate census was scene-wide, not a count of eligible
body Animators. Do not attribute all of those Animators to this optimization.

## Evidence and limits

`scripts/check-native-actor-audit-runtime.py` imports the real GH.Runtime,
GH.Runtime.FirstPass and ThirdParty assemblies as Unity MonoScripts. UnityPy
extracts original script identities and conditional fields from both the Drake
body and its outer gameplay wrapper. The native fixture uses those exact classes,
the original avatar/bones/clips and real ActorEvents injected as in Choreographer.
It does not boot a complete scenario or the full native SaveData-dependent graph;
a controlled graph uses original clips, native state names, and actual publisher
alternative-idle/continuation callbacks. This is stronger than a lookalike
CharacterManager stub, but it is not a headset scene test.

The fixture isolates unrelated player-only input/debugger/outline-camera bootstrap
callbacks in its Editor project. It does not replace actor types, actor callback
implementations, native animation evaluation or original asset data. The original
refs remain read-only. InputSystem's player DLL otherwise throws on Editor input
updates; dynamic Assembly.Load alone also fails to register script callbacks, so
these dependencies must be imported as real Unity plugins.

The positive test admits ten original body/wrapper scripts plus actual runtime
actor/event components, tracks 117 original bones and skips 100 repeated sleeping
loop checks. Real Unity frames prove that CullUpdateTransforms advances state
time while leaving an invisible original wing pose unchanged; the AlwaysAnimate
control continues writing that pose. Real MF.AnimatorPlay restores before WakeUp,
the native state-exit continuation reaches ActorEvents, and an event-bearing clip
retains original evaluation and delivers its callback. Causal controls cover
mandatory type refusal, unknown subclasses/providers/state callbacks, active
dissolve, CullCompletely, holding, native action restoration, foreign edits and
eventful loops.

The existing actor-bar proof retains 1615 runtime assertions against original
sleeping/flying Drake and CaveBear skins: full-cycle stable ceilings, native wake
transitions, transformed ancestry, real derivative ownership and pooled remote
reuse. Its Editor microbenchmark was approximately 34 us/bar with full checks
versus 10 us/bar with sparse checks. That is a fixture measurement, not an isolated
Steam Frame frame-time gain. Detailed receipts and source hashes are retained at
`.planning/debug/frame615-review/actors/`; generated build caches may be removed.

## Next Frame readback

Bars.PoseEligible and Bars.PoseUnsafeRig partition captured-pose populations;
Bars.PoseChecks and Bars.LoopSkips count actual matrix checks/shortcut reads.
The bounded Debug loop payload includes effective interval and component refusals.
Zero values are registered, avoiding a misleading missing counter.

Figure.IdleTracked counts registered bodies. Figure.IdleOriginalAlways and
Figure.IdleAuthoredCull partition their two relevant authored modes;
Figure.IdleTransformCull counts modes currently changed by this owner, including
visible actors for which Unity naturally still evaluates transforms. It is not a
count of actually invisible bodies or a millisecond saving. The remaining native
Animator modes do not enter those two authored-mode counters. Counter groups
describe different populations and must not be added together.

Verify nonzero eligible/skip values during ordinary loaded play, original stable
sleep/flight bar heights, immediate wake/action/held-body behavior and actual
authored modes. Further savings depend on those real populations and eye
visibility, rather than merely the requested settings or smaller meshes.
