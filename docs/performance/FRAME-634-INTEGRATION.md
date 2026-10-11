# Frame follow-up integration: defaults, smoke and terrain CPU budget

The supplied Build633 run still takes 98–103 ms per fully loaded three-room
application frame, including 61–68 ms of measured outer mod work. Terrain
pre-cull alone takes about 38 ms. This follow-up reduces that substitution work
and fixes two ineffective quality paths. It does not establish playable headset
performance. Native game/render/driver work also needs reductions; the remaining
frame interval is not an independently measured native CPU or GPU busy time.

The original dev integration base is `8b1c0a0487fad648d94e461bf1603cb0137ae13f`.
Implementation and combined validation use the isolated
`work/frame634-hotpaths-20261006` worktree. The main agent owns integration,
the build-number increment and publishing to dev. There are no wire, gameplay,
NPC or native-template changes here. The separate Quest branch/archive is untouched;
its existing shared-default selector can receive these defaults through integration.

## Delivered behavior

- Fresh Frame configuration and the explicit Standalone graphics action request
  eye scale **0.80**. Fresh PC and the other three graphics actions remain **1.00**.
  Normal BepInEx Bind preserves saved values; selecting a profile remains an
  explicit reset of its graphics choices.
- A previously accepted viewport can recover from a native reset even when the
  saved request has not changed. Existing quiet/wrong-time/deferred guards remain;
  automatic repair is bounded to once per second and stops after API refusal.
  It never requests a live eye-texture allocation. Multiple independent XR displays
  remain outside the existing one-display model and supplied hardware evidence.
- Existing scenario ambient-effects density now recognizes numbered native torch
  families and decorative emitters beneath prop/animated-door containers. At zero
  it masks their decorative flame, spark, smoke and distortion renderer roles.
  Native Light objects, door geometry/controllers and combat/actor/UI effects remain.
  Callback-sensitive emitters keep simulating; safe emitters pause without Stop,
  Clear or child pausing. This fixes coverage, not the unknown black-smoke shader cause.
- The new live `[Optimize] ScenarioTerrainCameraSourceLimitCount` bounds expensive
  source checks per camera invocation, including refused candidates. Fresh Frame
  and Standalone use **64**; fresh PC uses **0**, meaning unlimited. Overflow
  retains original native 3D geometry and shading. The trade is lower substitute
  CPU maintenance against potentially greater native rendering cost. Both eyes
  retain native fallback; no room is hidden. The row and help are localized.
- The existing shared-read toggle controls immutable type/name classification,
  common-owner reads within one synchronous pass and conservative frustum checks.
  Current component/parent/material/mesh/MPB state is still checked per evaluated
  source and eye. Frustum checks keep both eyes and authored culling overrides;
  invalid projections fail open. Exact originals remain native clone sources.
- Cheap terrain shading skips inactive native dissolve texture/noise work while
  retaining the LOW/HIGH authored clip equations, including cutoffs above one.
  Active continuous fade is unchanged. Actual Frame GPU savings are unmeasured.

Quality snapshots now include the configured camera limit. Debug counters report
actual evaluated candidates, surviving substitutes and native budget/frustum
fallbacks. These are source operations/leases, not independent GPU draw timings.
No new frequent normal-level diagnostic stream is introduced.

See [the raw-row hardware audit](FRAME-634-RUN-AUDIT.md),
[smoke coverage and scope](FRAME-634-SMOKE.md) and
[terrain work and causal tests](FRAME-634-TERRAIN.md).

## Combined validation and asset delivery

The combined tree passes all **14 source suites**, strict Release and Debug
solution builds with **zero warnings/errors**, the five bilingual document pairs,
and **296,631 complete golden-vector assertions**. The golden binary stage runs
directly with the project's runtime shim and repository argument. This is not a
new complete 146-local-suite pass: unrelated NPC632 evidence is inherited, as the
maintainer explicitly requested for this bounded follow-up.

The combined production checks rerun actual Unity environment code (**11,428
assertions**) and terrain code (**316 assertions**). Worker evidence for the same
owned source includes eleven selected smoke causal controls and all forty-six
terrain causal controls, including actual GL pixel comparisons for active and
inactive HIGH/LOW clip paths. Final RenderQuality evidence has **75 assertions,
20 causal controls, two startup controls and 14 source bindings**. The actual
profile action passes **265 boundary assertions** and four UI-action bindings.
The profile fixture does not emulate actual BepInEx persisted files; preservation
relies on the existing Bind contract. Source hashes bind the reused evidence.

Canonical compiled633-to-candidate comparison has 1,226 to 1,227 decompiled C#
type files: twelve intended behavior/config/UI/diagnostic changes, BuildInfo's
worktree branch metadata, one new ambient helper and no removals. Existing
OpenXRBootstrap/VRRigDriver and all multiplayer runtime types compare unchanged.
Hash/timestamp masking uses the existing snapshot procedure; the branch difference
is explicitly reviewed rather than hidden.

The separate Unity2021.3.5f1 Windows64 environment bundle is **56,732,489 bytes**,
SHA-256 `e48e5f00bb978347434c4e895fd03662c469f3d53f9aed803619c676ea7251d9`.
The bank receipt is updated. Two sequential same-worktree builds are byte-identical;
this is not fresh-machine reproducibility. Game-exact Unity loading checks all
3,170 immutable streams, 1,139 originals and the production decoder. Null-graphics
loading proves asset/decoder delivery, not rendered HMD pixels. Install the updated
bundle together with the integrated assembly; the main/town bundles are unchanged.
The first internal Unity pack log was overwritten by the second pack; both wrapper
logs and hashes remain, and that limitation is recorded in the receipt.

Source-bound run manifests, compiled differences, failed attempts and final logs
are archived under the integration checkout's
`.planning/debug/frame634-review-20261006T183809Z/proof/` before owned worktree
cleanup. Supplied hardware inputs and their original manifest remain separate.

## Next hardware comparison and native workload

In the same fully loaded three-room view, compare limit64 with limit0, keeping eye
scale0.80 and all other choices constant. Use settled measurement windows and the
new candidate/fallback counters, terrain scope and whole-frame interval. Inspect
all rooms, both eyes, near walls and continuous wall fade. If greater native
rendering outweighs reduced CPU work, a higher limit or zero remains available.
Verify ambient density0 removes the photographed puffs and density100 restores
the native optional effects. A paired multiplayer run needs matching new builds;
the supplied PC631/remote629 captures cannot establish current multiplayer cost.

The next concrete native-render reduction is an independently configurable cheap
material path retaining original static geometry, without broad room combining.
The incremental census still contains 730 submitted Amp_Basic_N_MRAO slots;
that population establishes an investigation target, not its GPU milliseconds or
eligibility. First measure bounded Debug refusal reasons for floor/structural
materials, native fade/MPB ownership and shader/identity proof. An audited variant
could remove normal/MRAO/detail/specular work while retaining original texture,
tint, projection, live property blocks and exact native clip/fade behavior.
Its ownership must preserve cloning and immediate native writes; a persistent
replacement of native template material arrays is unacceptable. Do not simply
relax the existing native-wall-fade veto: historical floors carry live fades,
and earlier broad combining lost rooms.

After that, measure static scenery shadow/probe costs before adding independent
owned/restored quality compromises. Zero explicit instance admissions and the
menu's zero-light census do not prove profitable scenario shadows or instancing.
Continue finished scenery-mask maintenance analysis (about 4.9 ms in this run).
All such further compromises remain configurable. Neither this change nor the
automated tests certify three-room playability or multiplayer smoothness.
