# Build618: original stat portraits and figure-cache attribution

The Build617 capture contains a 126.19 ms stat-panel scope, a new portrait mip
bake and prepared-ghost misses whose reasons were not recorded. The existing
loading preparation collected only imagery already assigned to an original UI
Image. A never-inspected enemy's addressable portrait was outside that closure.

`StatPortraitPreparation` reads the exact selectors in the shipped
`ActorStatPanel.InitializeCharacterCard`, `InitializeMonsterCard`,
`InitializeMonsterClass` and `InitializeHeroSummonCard`: player scenario-preview
art with the original hero fallback, enemy model/custom portrait, attached-prop
portrait override, and summon prefab/custom portrait. It visits the coordinator's
one original actor snapshot, including inactive actors, without a second scene
inventory, native Show, character selection, UI assignment or gameplay callback.
If native UIInfoTools is not ready at Begin, a single deferred discovery job stays
pending for at most 30 seconds and collects the same original snapshot when it
arrives. It neither inventories the scene again nor silently reports readiness.

Original references join `ScenarioCardPreparation`'s asynchronous queue while
ghosts are constructed. The stat queue later observes the same GUID pin owner
and prepares one original sprite/texture per loading tick. It directly uses
`CardFaceMipBake.ReplacementFor`, so panel preparation obeys `PanelMipBake` even
when ability-card `FaceMipBake` is off. No native `ReferenceToSprite.GetAsyncSprite`
or `ImageSpriteLoader` request is started or altered. Pending jobs have a bounded
30-second wait and retain the coordinator's 90-second fail-open cancellation;
reset drops only borrowed jobs, not shared live pins or mip textures.

Prepared ghosts retain every strict mesh, material, shader, render queue, bone,
hierarchy and LOD identity check. Mismatches still construct today's original
visual immediately for local and remote pickup. Debug now records the failed
identity category, capped at eight reports per scenario. This is attribution,
not an unproven relaxation of cache validity or a claim that all misses disappeared.
Debug subscopes distinguish original stat conversion, first mip scan and the
singleton-safe second-panel snapshot. Normal logging adds only bounded failures.

## Verification boundary

The focused Unity 2021.3.5 fixture runs the complete production portrait collector
and extracted stat queue against inert native-provider/pin boundaries with the
shipped field/method shapes. It exercises player fallback, custom monster,
attached object, summon, duplicate references, incomplete handles, mip-off and
reset lifetime, including late native UI readiness. Causal controls remove the
correct model, prop override, shared queue publication or deferred discovery.
Release compilation validates the selectors against
the actual game assemblies. The same fixture separately renders the shipped
SpittingDrake sleeping/flying rig and tests strict ghost reuse/invalidation.

These checks do not measure headset portrait latency or total frame time. The
next hardware run must establish whether the former live portrait readback is
now a shared-cache hit and identify any remaining cold conversion or ghost miss.
