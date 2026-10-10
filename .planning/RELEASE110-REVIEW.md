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

Final integrated check receipts will be recorded here after the bounded repairs
and the parallel NPC 666 integration complete. Previously passing checks for
unchanged areas are inherited explicitly; a focused pass is not described as a
new complete-gate pass. No actual Steam Frame update restart, four-player game,
or complete campaign was run during this source review.
