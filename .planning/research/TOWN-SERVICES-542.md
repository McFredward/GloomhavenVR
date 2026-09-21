# Build 542: persistent town residents and physical merchant cards

## Hardware evidence and scope

The supplied local `LogOutput.log` identifies build 541 / assembly 1.0.7.0. It records
merchant, temple and merchant opening, without a town-service fallback or error-level
entry. The supplied screenshots show floor clearance, damaged facial geometry and
the flat merchant catalog. Peer logs still identify build 500 and cannot establish
current multiplayer appearance. Source changes below address the reported presentation;
a successful automated check does not establish headset quality.

## Permanent residents and native visits

All three NPCs remain in the active 3D map when immersive town services are enabled.
An available resident replaces its physical map button. Point/trigger or fingertip
interaction enters the same guarded native destination path; native locks and quest
commitment remain authoritative. Decorative loading never gates game continuation:
the original button remains available until its replacement is ready. The resident
collider participates in early pointer arbitration, including when visiting is locked,
so UI or map icons behind a resident receive neither hover nor click.

Closing a visit leaves the NPC standing. Disabling the option restores original
windows and map buttons; actual remote visits retain their existing shared appearance.
Three resident poses, scales, visibility and animation clocks use additive presence
record 79. The lowest fresh enabled peer authors the shared positions and clocks;
leave, timeout and opt-out elect a replacement without opening native controllers.
Observers never replace the author's placement with their own environment preference.

The record is 1 byte while inactive or 115 bytes while active, excluding its 2-byte
TLV header. The unchanged v3 snapshot maximum grows from 6869 to 6986 actual bytes;
The 7243-byte allocation preserves 257 spare bytes; actual snapshots remain below the
existing 7168-byte reassembly ceiling. The record includes the author's separate
actor and furniture floor adjustments; observers never resample their own terrain.
No card identity, purchase, donation or enhancement authority moves to this channel.

## Floor, lighting and original decoration

[Setting evidence](TOWN-542-SETTING.md) records the actual floor-mesh interpolation,
placement, original Addressables asset provenance and light ownership. The initial
station entrance waits for optional decoration loading to settle, then fades its
NPC, furniture and original props together. Optional load failures are bounded
warnings; the native service remains accessible.

The town shader receives environment/practical lighting instead of a fixed studio
fill. Original lanterns/candles, coins, books and vessels are reconstructed as
rendering data without any native gameplay scripts. Owned materials, load handles
and lights are released with their station. No original game asset is repackaged.

## Physical merchant inspection

[Card implementation and tests](TOWN-542-CARDS.md) covers the raised item cards,
original native faces, physical item-card bodies, presentation and return motion.
Picking up or dropping an item card does not select, buy or equip it. Explicit native
selection/purchase controls retain all permissions and confirmations. The same face
and body move into the hand and return to their original slot, locally and remotely.
There is no second held-card publication. Physical body fades match the face's
opening alpha; every inert clone registers for late original silhouette updates.

## Validation record

- Strict integration Release: zero warnings/errors.
- Golden wire vectors: 257025 assertions (255887 previous +1138 resident vectors).
  Golden bytes, absent records, truncation, numeric validity, malformed quaternion,
  atomic capacity failure and complete snapshot budget are covered.
- Floor/station: 1581 geometry, 56 lifecycle and 243 ground-contact assertions,
  13 compiled negative controls, including author handover, original upper-edge
  preservation and owned-resource cleanup.
- Resident lifecycle/authority: 135 production assertions and five compiled negative
  controls, including missing assets, opt-out, expiry and viewer environment changes.
- Card body: 15 real-Unity assertions and four compiled negative controls.
- NPC input: 89 real-Unity assertions and four compiled negative controls, including
  actual Collider rays and the production early RayInteractor/RayUgui election.
- Stand light ownership: 624 production assertions and five compiled negative
  controls. Exact object ownership excludes only the new town lights from native
  flicker damping; native lights with the same names/layer retain existing behavior.
- Catalog: 409 Unity assertions and 19 negative controls; interactions: 901 and 31.
- Additional merchant workspaces: 1580 Unity assertions and eight negative controls.
  Full-sized counters remain in the clearing; held/returning cards defer relocation.
  [Workspace evidence](TOWN-542-WORKSPACES.md) records footprint bounds and synchronized
  opacity/input behavior.
- Full mirror/render suite passes, including physical body and held-duplicate mutations.
- Complete guard: 14 source checks and all 49 local suites pass (419.5 seconds),
  plus 257025 wire assertions. Exit 1 reports only the expected old-baseline compiled
  difference: 106 changed, 105 added/removed, zero order-only moves. Strict Release zero warnings/errors;
  bilingual docs and whitespace checks pass. Evidence: `/tmp/town542-final-guard.log`
  and `.planning/debug/test-runs/20260921-153333-2134e554/results.json`.
  Asset-specific final checks follow.
- Facial mesh assembly, visual review and matching town bundle are still in progress
  at this integration checkpoint. This checkpoint is not a hardware handoff.

## Hardware checklist

1. Open Campaign and Guildmaster maps in forest, cellar and MR. Check all three NPCs
   and furniture at floor level, scenery clearance, lighting and original decoration.
2. Approach every face and view front/profile/three-quarter views. Check eyebrows,
   eyelids, lips, neck, collar and hood edges, including idle/greeting motion.
3. Open services by pointing or touch. Confirm buttons behind NPCs never react.
   Close/reopen visits; the three residents must remain. Check native locked services.
4. Pick up merchant cards with either hand, turn and release them above/away from
   the counter. There must be no duplicate or unintended purchase. Check unaffordable
   items, filters, paging, character switches and native buy/sell confirmation.
5. With two matching builds, compare permanent NPC positions/clocks and held card
   faces/bodies/returns. Visit concurrently, leave/rejoin and change the option.
6. Disable immersive town services while a service is open. Confirm the original
   window, controls and native selection return, then re-enable repeatedly.
