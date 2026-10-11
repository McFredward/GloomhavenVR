# Pre-1.1.0 integration review

Reviewed published `v1.0.8` (`4640fff2f`) against development `ce637a1dc`
(ModBuild 665) on 2026-10-11. The review completed before the bounded repairs
were started. This is a development review, not authorization to publish a
release or a claim that every headset/game scenario has been exercised.

Three independent initialized worktrees reviewed networking, native game flow,
and compatibility. The integrator additionally checked NPC transaction handoff,
confirmation lifetime, visibility, scene teardown, and the paired Build 665
logs. The parallel NPC 666 summon/ring/hover work is retained as a separate
integration dependency. Quest standalone work remains outside this task.

## Confirmed findings

| Finding | Consequence | Repair boundary |
| --- | --- | --- |
| NPC shared presentation election retains a receiver-local cached owner | Simultaneous visitors can keep different shared original widgets even after receiving the same visitor set | Deterministic live visitor election; actual host grants retain precedence; independent personal fans/purses remain independent |
| Map hover transitions omit the original native current-state eligibility | Clearing an old hover can exit a story or private-quest state prematurely, including releasing its original input locks | Preserve the original Loadout/LocationHover/WorldMap transition predicate; still deliver ordinary pointer cleanup |
| Frame self-update relaunch drops the original VR opt-in argument | Successful file update can restart the original flat Steam entry | Preserve the existing EXE/working-directory/argument restart path in the running Proton context; reject unsafe argument loss for Frame |

The map defect predates 1.0.8 but remains within this release-wide continuation
review. The other findings are independently reproduced from current production
methods; automated evidence does not substitute for the final headset outcome.

## Multiplayer and visual parity

- Reviewed stable resident animation authors, actual per-NPC host grants,
  simultaneous visits at different stands, disconnect/reconnect cleanup,
  original-catalog identity, first-picture admission, reliable controls and
  bounded sampled motion. The confirmed shared-widget election defect is
  separate from native gameplay authorization and permanent animation authors.
- Original callbacks retain gameplay authority. An unmodded host cannot grant
  a mod NPC transaction: the existing usable original-window fallback remains.
  No native transport/game-action semantics are changed by these repairs.
- Shared cabinet contents, original UI, offered and held cards, purses,
  confirmations, flights, poses and audio remain public in the 3D map. Scenario
  ability-selection secrecy remains remote-only; local controlled cards never
  inherit observer secrecy.
- The only applicable town presentation exceptions are visitor-local pre-drop
  card/purse guides and client-local repeating ring-spin/offered-card-hover
  phase. Card facing, original enhancement overlays, actual flights, state,
  direction, speed, amplitude and waveform remain shared contracts. This review
  introduces no new exception.
- Reviewed original rule foldout/remote geometry, wrist-board owner settings,
  held-figure action release, health-bar depth/pose, character selection and
  assignment, quest proposal/ready/departure, story/reward/retirement callbacks,
  and new-save/non-immersive tutorial continuation. No other concrete new defect
  was established in these reviewed paths; this is not an exhaustive hardware
  guarantee.

## Compatibility and restoration

The released 1.0.8 verifier accepts the new files under `BepInEx/`, including
all indexed split figure banks. Current download/extraction byte counts use
`long`, and the existing archive limits accommodate the current package.
Frame setup preserves the original flat Steam entry. Fresh PC/Frame defaults
and explicit profiles retain saved configuration and personal calibration.
Optional visual budgets restore their owned original meshes/renderers/LOD/
cloth; native game actions and authoritative actor identity remain intact.
Unneeded camera sinks retain projection readers and independently consumed
render targets. The updater restart defect above is the confirmed exception.

The paired logs contain a caught enhancement-holder cleanup exception during
shutdown. The quiet-controller `finally` restores source/mask ownership and
clears the proxy island even when that native callback throws. This is recorded
as a bounded shutdown anomaly, not described as a demonstrated live deadlock.
Likewise, three remote map-hover null-reference warnings have no throwing-stage
stack; the navigation repair is source-proven independently, without assigning
those warnings an unproved exact cause.

## Evidence ledger

Worker reports provide exact scope, reproduction boundaries and validation:

- [Network review](RELEASE110-NET-REVIEW.md)
- [Flow review](RELEASE110-FLOW-REVIEW.md)
- [Compatibility review](RELEASE110-COMPAT-REVIEW.md)

The final candidate is ModBuild **667**, retaining the parallel NPC 666 source
(`9eddd479f`) and adding only the four production types required for the three
findings. Wire version stays **3**, TLV **118** remains next free. No new visual
exception, native gameplay action, configuration key or Harmony patch is added.

Fresh integrated evidence in `.planning/debug/release110/integration/`:

- Map navigation: **205 assertions**, both compiling causal gate-removal
  controls rejected. Existing worker map-flow evidence remains **2,012
  assertions and nine controls**; unchanged map-flow code was not rerun.
- Shared NPC presentation: **39 assertions**, all five causal controls rejected,
  including restored sticky/age ranking and ignored reliable host grant.
  Worker grant evidence remains **49 assertions and two controls** for unchanged
  complete grant sources. The actual Unity shared-workspace subset is recorded
  at `shared/run-3gmlla_u`: production and all **nine causal controls** pass on
  the composed candidate, retaining native workspace source, independent visitor
  content and cabinet readiness. This is a focused Unity pass, not headset proof.
- Update archive/restart: **61 assertions** and the compiling original-flat-
  restart control rejected. Windows/ordinary Steam behavior remains covered.
- All **16 source suites** pass. Strict **Debug and Release** builds have zero
  errors/warnings. Direct complete golden vectors pass **300,510 assertions**.
  These are direct goldens, not a new complete `wire-tests.sh` umbrella pass.
- Documentation pairs and committed game-compatible bundles/index pass.
  Surface comparison is unchanged: **666 configuration keys, 235 patches,
  4,795 log tokens**, zero removals/additions.
- Runner inventory remains exact: **215 local / 98 CI / 16 source** suites.
  The three new bounded regressions are registered for both local and CI.
  Runner unit tests pass with their two pre-existing explicit skips.
- The private compiled snapshot uses the actual guard's unchanged snapshot
  function against `ce637a1dc`. It records **19 changed types, one added,
  none removed**: the four review repair types, known NPC 666 codec/effect
  types and `TownOfferedHover`, NetProtocol, seven solely inlined ModBuild
  changes, and the expected worker GitBranch stamp. No unrelated production
  behavior change was found in the compiled comparison.

The first integrated partial run retained two passing regression scopes and one
failed updater *control*: its override property was not connected to a Compile
item, so it correctly refused to count the unchanged positive as a detected
mutation. The test project now binds that source property, and only this failed
scope was rerun successfully. The original result is preserved; it is not
rewritten into a green complete report. Direct goldens initially could not start
their apphost without `DOTNET_ROOT`; the built executable was then run with the
installed runtime and completed every assertion. Neither setup failure is
presented as a production bug.

The retained NPC 666 integration supplies composed coverage of its **212**
local suites: the initial complete attempt records **208 passes and four
fixture-binding failures**. The two handoff bindings pass in
`npc666/repair-handoffs/results.json`; the historical native-paint and physical-
geometry scopes pass in `npc666/repair-historical/results.json`. Those repairs
bind real native fields/named constants and explicitly fail if newer lifecycle
routes enter an older control; they do not weaken the original assertions.
The current compiled production source is unchanged by these fixture repairs.
The final NPC dependency is `6da6bf452`; its full evidence ledger is
[NPC-666-REVIEW.md](NPC-666-REVIEW.md).
The initial complete result remains at
`test-runs/20261011-013017-847b99bd/results.json` in the NPC 666 integration's
retained debug evidence. The three newly added review regression scopes bring
the composed local inventory to **215**, without claiming a fresh single-run
215-suite umbrella pass. The earlier final-capture/loading/visibility and NPC
ring/summon/hover proofs remain inherited where their source is unchanged.

Previously passing checks for unchanged areas are inherited explicitly. A
focused pass is not described as a new complete-gate pass.
No actual Steam Frame update restart, four-player game, or complete campaign was
run during this source review. In particular, the retained Proton child process
and real Steam playtime/achievement behavior after an updater restart need a
Frame hardware check; argument preservation itself is production-code proven.
