# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop, multiplayer-capable.** Current build:
  **`NetProtocol.ModBuild = 106`** — it carries TWO rounds, the unbumped `76daf29` and the
  slot-overlay unification, and is waiting on its first hardware run.
- **Last update:** 2026-08-11

---

## 0. READ THIS FIRST (successor briefing)

This file is the handover. Everything below is written for someone who has never seen the
project before and has to be productive in the first ten minutes.

### The one-paragraph description

A BepInEx 5 + HarmonyX mod that turns *Gloomhaven Digital* (Unity 2021.3.5f1, Mono, net472)
into a room-scale VR game on a Quest 3 over Virtual Desktop (VDXR), MultiPass stereo, 90 Hz
(11.1 ms). The board becomes a physical diorama on a table; the player's hand holds a real
card fan; the game's own 2D UI is *converted* into world-space panels docked on a wooden
control board. **Multiplayer is supported and is a first-class constraint, not an
afterthought** (see §3).

### The user

Frederik (`frederik@lissek.info`). **Always answer in German.** Worker/agent prompts may be
English. He tests on real hardware every round and reports numbered findings; each round is
"read the reports → dispatch workers → review their diffs → merge → bump → push → write him a
German report".

### Where the truth lives

| Question | Authority |
|---|---|
| What is patched, by whom | `docs/PATCH-INVENTORY.md` (**generated** — `scripts/patch-inventory.sh generate`) |
| Why a patch exists, what it costs | `docs/PATCH-NOTES.md` (hand prose, not checked) |
| Architecture, module boundaries | `.planning/ARCHITECTURE.md` |
| Refactor rules, invariants | `.planning/refactor/CHARTER.md`, `INVARIANTS-*.md` |
| Frame-order contracts | `.planning/refactor/FRAME-ORDER.lock` |
| Performance measurements | `.planning/perf/FINDINGS.md`, `.planning/perf-zoomed-out.md` |
| Multiplayer design | `.planning/multiplayer/PLAN.md`, `PLAN2.md`, `RESEARCH.md` |
| Parked/abandoned work | `.planning/static-batching-removed.md`, `.planning/wall-fade-stereo-rivalry.md` |
| Anything user-facing | `src/GloomhavenVR/Core/Loc.cs` (+ `Loc.Config*.cs`) |
| Every shipped default | `src/GloomhavenVR/Defaults/*.cs`, one annotated line each |

**The code is the documentation.** This project deliberately writes long block comments that
state the user report verbatim, the root cause, the evidence and the rejected alternatives.
When you change something, write that comment — several rounds have been saved by a comment
that said "do NOT swap this back, here is why".

---

## 1. Hard rules (violating any of these is a defect, not a style choice)

1. **Mod-side only.** Never modify game data. `ressources/` is a READ-ONLY symlink to the
   installed game (`Managed/*.dll`). Never `git add` it or `libs/`. Decompiled reference
   sources are under `decompiled/` (and `decompiled-demeo/`), also never edited.
2. **`NetProtocol.ModBuild` +1 on every build handed to another player.** It is the
   multiplayer handshake key; mismatched peers get a blocking dialog. Write a full build note
   next to it — the note history in `NetProtocol.cs` is the project's real changelog.
3. **Wire:** magic `GVR1`, `Version` byte stays **3**, all changes **additive TLV** only, old
   readers skip unknown records by length. **Card identity NEVER goes on the wire, ever** —
   reveals go only through `Net/RevealGate.cs`. Records 1–17 and 22–29 are used; 30+ free. The
   extras flag byte is FULL and record 4 has no free bits: claim a NEW record rather than
   squeezing (the reservation comment in `NetProtocol.cs` records a past double-claim
   incident).
4. **Lights are never hidden or written by any visibility system. Figures are never touched by
   any wall/visibility system.**
5. **Everything that fades or moves does so WITH the animation.** Popping is unacceptable.
   Exception granted by the user for one case only: the decision area's *collapse* is instant.
6. **The user's approved look outranks geometric or technical correctness.**
7. **Localisation:** every user-facing string via `Core/Loc.cs`, German **and** English.
8. **Config:** every default on one annotated line in `src/GloomhavenVR/Defaults/` with a
   `// => [Section] Key` comment. A new KEY must end in a unit word `ConfigSteps` recognises —
   an unrecognised suffix silently falls back to "a fiftieth of the shipped default" and
   produces an unusable stepper. This has been user-reported **twice**; there is now a test
   (`tests/GloomhavenVR.WireTests/ConfigStepVectors.cs`) that pins it.
9. **Never mention remaining context or advise a fresh session** (the readout is a harness
   bug). Do not talk about token budgets.

## 2. The five gates — every change must pass all of them

```
./scripts/build.sh                        # 0 errors (6 pre-existing nullability warnings are OK)
bash scripts/wire-tests.sh                # currently 1441 assertions
./scripts/refactor-guard.sh check --summary
python3 scripts/rebase-defaults.py check
python3 scripts/check-wire-coverage.py
```

Reading `refactor-guard`: only the **five gate lines** matter (patch surface, frame order,
mirrors, remote defaults, bundle). Its **exit code is 1 whenever the compiled form differs
from the stored baseline**, which is normal after any change — that is a report, not a gate.
The baseline lives in gitignored `.planning/refactor/.guard/` and can be regenerated from a
clean tree with `./scripts/refactor-guard.sh baseline`.

`refactor-guard` hard-fails if `docs/PATCH-INVENTORY.md` is stale. Fix with
`bash scripts/patch-inventory.sh generate`.

**Bundles are built ONLY with `/home/claw/unity-2021.3.5`** — not `unity-2021.3`. The wrong
editor produces a bundle that silently loads nothing.

## 3. The multiplayer 1:1 ruling (user, verbatim, standing)

> "generell gilt die Regel, das man alle Interaktionen, Animationen und Anzeigen des
> Controllboards in MP auch synchronisieren soll. Die einzige Ausnahme ist hier die geheime
> Quest des characters und während der Auswahlphase die tatsächlichen Oberseiten der Karten.
> Ansonsten soll alles 1:1 übertragen werden."

Narrowed and extended since:
- Card **fronts** of a remote player are visible in every phase **except** card selection.
- The **initiative NUMBER** stays masked to "?" during selection.
- The personal quest / battle goal is never mirrored.
- **The tuning guarantee:** "Ändert ein Spieler also die Positionen für sich selber, so sollen
  alle anderen diese Position bei seinem board auch sehen." Board-affecting dials therefore
  ride **record 28**, which is range-paged (a full lap is a snapshot; convergence ≤ 400 ms at
  two pages). `scripts/check-wire-coverage.py` fails the build when a board-affecting dial has
  neither a record-28 field nor an annotated EXEMPT.

**The trap to avoid** (it has been hit): a wire field with *no consumer*. `check-wire-coverage`
would call it covered while the peer still sees no difference. See the `[Cards]
FanCloseDuration` note in that script.

## 4. Where the project stands — recent rounds

Newest first. Each entry names the *root cause*, because that is what generalises.

- **ModBuild 106** — the two blinking slot rectangles and the card that lands in them were sized by
  two unrelated numbers with no dial between them: the card took `[Cards] SlotCardFill` = 1.45, the
  overlays took CODE LITERALS off the card metric (teal wanted-pulse 1.36, gold snap glow 1.24), so
  the card overhung the rectangle that had just marked its spot by 6.6 % (119.7 mm inside 112.3 mm,
  board metres, shipped defaults). The dial the user found, `[Cards] ActiveCardScale_*`, sizes the
  ACTIVE PILE via `ActivePileViewer`, which does not exist during selection — hence "no effect". The
  POSITION half of the coupling already existed (`SlotOverlayOffset`/`Spacing` feed both the glows
  and `SlotHomeOffsetFor`); only SIZE was missing. Now one per-board dial,
  `[Cards] SlotOverlayScale_{board}`, seeded with SlotCardFill's 1.45: the card is untouched, the
  teal grew to meet it exactly (user: "exakt ausfüllen"), the gold keeps its shipped 0.912 ratio so
  it still reads inside the teal when both show. `SlotCardFill` retired (ConfirmUndoSize pattern) —
  no migration marker, the successor key is new. Wire: field **171** (FACTOR range), needed because
  the peer's glows are not cards — `RemoteBoardFurniture` builds them from the recess metric × a
  factor, and record 11 only carries the finished card WIDTH. Ratio is now one shared constant
  `PlayTray.SnapGlowRatio` instead of a literal on each side. Side finding: `Fill` is in no
  `ConfigSteps` unit row, so the retired dial had been stepping off its own magnitude all along —
  the successor's `Scale` ending fixes that and a step vector pins it.
- **`76daf29`** (folded into the 106 bump) — quest text flickered because ONE empty poll blanked it: the goal
  resolves through `CardsGameApi.ActiveHand()`, which is null while the game re-binds a pooled
  hand, so "no goal" and "ask again in a moment" were indistinguishable. Now told apart at the
  source. Figure-grab highlight flashed because a bare radius is a step function and the hand
  was drifting *on* the boundary (log: 39 → 38 → 37 mm against a 40 mm radius) — now
  enter/exit hysteresis plus a 6-frame dwell.
- **ModBuild 105 `e4ccf8c`** — a peer's board was being **described** instead of shown. The
  decision row crossed as a label string; the receiver rebuilt flat plates. Fixed by realising
  the game already builds the whole widget tree on every client (`TakeDamagePanel` is a
  per-client Singleton, `ShowOtherPlayer`), so only ROLES need to travel — new record 29. Also:
  a peer's board sat on a static sub-ladder (0/4/8) below the panel ladder's base (100), so it
  could never rank against anything; card aliasing was a 1 s rescan timer, not a slow bake; the
  refusal sound was keyed on ownership after free focus had made "look at another player's
  character" a success.
- **ModBuild 104 `0064307`** — performance. `FindObjectsOfType<T>()` is O(*every loaded
  object*); three independent measurements price ONE call at 10–15 ms in a big room, and the
  mod made ~7 per second (~89 ms/s, delivered as 20–100 ms hitches). Replaced by
  `Core/SceneRegistry.cs`. The perf instrument also gained a **zoom axis** (`FRAME`/`SPLIT`
  now bucket a window's frames into near/middle/far thirds).
- **ModBuild 103 `06cb184`** — six reports, three of which were one defect class (a
  transparent deciding its paint order from something that moves). Includes the **invisible
  buttons** root cause, found after three failed rounds: the cap FACE is multiplied by the
  player's `[ButtonColors]` tint, the WELL it sits in is not, so at tint 0.5 a disabled face
  (0.105) is darker than its own hole (0.15) — pure arithmetic, hence a steady state.
- **ModBuild 102 `f200f7f`** — the wrist HUD. A uGUI canvas is read from its **-Z** side; an
  identity base therefore aimed the readable face out of the back of the hand while the gate
  revealed it from the palm, so the player saw the mirrored back face.
- **ModBuild 101 `a1623e1`** — `ConfigSteps` resolver: a key ending in a bare axis letter never
  matched a unit word, so its step fell to a fiftieth of the shipped default (0.05 mm per
  press). 73 of 351 dials changed step.

## 5. Open items — the successor's queue

### 5a. Committed to the user, not yet done

*(Empty. The slot-overlay unification that stood here shipped in ModBuild 106 — see §4. Note for
next time: "overlay" in the user's vocabulary means the two BLINKING RECTANGLES on the board where
a hand card may be laid, not the active pile off its right edge. The predecessor's entry here read
it the other way and that cost a clarification round.)*

### 5b. Awaiting the next hardware session (diagnostics are already in place)

- **The glasses among the symbols** (`[WorldUI] USE BARS: bonus-bar split KEPT …`). The
  predicate `CardsGameApi.BonusIsPlaceable` is a four-way conjunction that **fails open**, which
  is also the shape a host/client divergence takes. The new line names WHICH condition kept each
  row — compare the host's and the peer's logs for the same bonus.
- **Fog-tile shimmer**: grep `UNSEEN TILE ORDER` and read the **apply count**. A rewrite is the
  only event that driver has which can change anything on screen, so "the tiles moved" and "the
  count did not move" cannot both be true.
- **Perf**: the next capture should run with `[Optimize] QuietDiagnostics = true` (about a third
  of the ModBuild 104 saving is behind that switch) and `[Perf] SceneProfile = true` (feeds the
  zoom clause). Then read the new `ZOOM` clause on `[Perf] SPLIT`.
- **Figure pick radius**: `[FigureGrab] PickRadiusMillimeters` (5…130; **130 restores the old
  palm-wide reach exactly**). At the user's zoom 40 mm ≈ 1.1 hex widths — if the flashing
  persists after the hysteresis, the radius itself is the next lever.

### 5c. Declined by the user — do not re-propose without new information

The zoomed-out overview cannot reach 90 Hz without changing what the head camera declares
visible. The user was presented with the options and chose **none of them**:
- narrowing the head camera's culling mask (`[Optimize] HeadMaskFromScenarioCamera`),
- distance-culling VFX/particles,
- animation LOD for distant figures,
- bar material de-duplication (small but real look risk).

Expected outcome of what WAS built: close-in should reach a stable 90 Hz; the far overview
should be a **hitch-free 45**, not 90. Say so plainly rather than implying more.

### 5d. Parked by user ruling

- **Per-eye wall-fade stereo rivalry** — proven unfixable on the game's masonry shader without
  losing the dissolve (`.planning/wall-fade-stereo-rivalry.md`).
- **Static batching ("Bündelung")** — tried and completely removed; Apparance reveal-clones of
  batched sources are born without material slots (`.planning/static-batching-removed.md`).

### 5e. Known gaps, carried

- `Net/BoardVisual.cs`'s sub-ladder was fixed in 105, but mod-owned free plates near a peer's
  board (fan hints, ping labels) are still at order 0 and lose to it.
- 16 PENDING wire-coverage debts (`check-wire-coverage.py` lists them with reasons).
- `Selectable` sprite-swap transitions write `Image.overrideSprite`, which the mip bake has
  never covered — hovering an action half can still sample a mipless state sprite.
- A receiver whose own take-damage prompt is docked falls back to plates for that window
  (self-healing, logged).

## 6. Working practice that actually matters

### Your role: INTEGRATOR (user ruling, 2026-08-11, standing)

> "Ich will, dass du hauptsächlich dafür da bist die Ergebnisse der agenten zu mergen und deine
> Aufgaben an spezialisierte Agenten abgibst die auf feature branches arbeiten. So sollst du deine
> Anstrengen auch parallelisieren. In Begründeten Außnahmefällen kannst du auch mal direkt etwas
> implementieren, sonst an Implementierungsagenten auslagern."

Implementation goes to specialised agents on feature branches, in parallel. Implementing yourself
is the exception and needs a stated reason. **"The files are coupled" is not one** — that was the
argument used in ModBuild 106 and the user rejected it, because coupling is something the
integrator designs away *before* dispatching:

1. **Land the shared contract first.** Any symbol several workers need (a config accessor, a Tune
   id, an interface) is a thin slice you write and commit to `main` yourself BEFORE fanning out.
   Minutes of work, and every worker then compiles on its own.
2. **Split by FILE OWNERSHIP, not by topic** — name each worker's exclusive file set in its brief.
   Overlapping FILES cause collisions; overlapping subject matter does not.
3. Typical lanes for a board feature: local/render · net mirror · localisation + options UI ·
   tests + checkers.
4. **Always pass `isolation: worktree`** — without it they collide in the shared checkout.
5. Brief every worker with: the hard rules (§1), the five gates (§2), "do NOT bump ModBuild",
   "do NOT commit or push", "never `git stash`", and the exact user report **verbatim in German**.
6. **You** review every diff, merge, bump ModBuild, write the build note, push, and report.

Worker first command: `bash scripts/worktree-setup.sh` (links `ressources`, `libs`,
`.planning/debug/default` and the refactor-guard baseline).

### Hazards that have actually bitten (all of these cost a round)

1. **`git stash` is NOT worktree-isolated.** `refs/stash` is shared by every worktree; two
   agents stashing concurrently pop each other's work. Forbid it in worker briefs.
2. **The shell's working directory persists between tool calls**, and `cd`-ing into a worktree
   to inspect a diff leaves you there. Two commits landed on a worker branch instead of `main`
   this way. **Use absolute paths, and verify with `git log --oneline -1 origin/main` after
   every push.**
3. **`git apply` is atomic.** It prints "Applied patch to X cleanly" per file and then rolls
   *everything* back if a later file conflicts. A green build afterwards proves nothing — a new
   file compiles on its own. Verify a merged symbol with `grep`.
4. **`grep -c` returning 0 exits non-zero** and breaks `&&` chains. Use `;`.
5. Bash heredoc + Python triple-quote collide — write the script to the scratchpad first.

### Logs

The user drops hardware logs in `.planning/debug/` (gitignored); a peer's logs go to
`.planning/debug/remote/`. Tuned config snapshots land in `.planning/debug/default/` and are
taken over with `python3 scripts/rebase-defaults.py apply`. **Those cfg values are always tuned
against the NEWEST build — take them over verbatim, never re-express them for a frame change
that shipped in the same round.** (Learned the hard way; see the do-not-swap note in
`WristHud.ApplyPose`.)

Always check the build stamp at the top of a dropped log before reasoning about it:
`[Core] GloomhavenVR ModBuild N` and `v0.1.0 build <sha> [main]`.

### Reporting to the user

German, prose, no bullet-point dumps. State the **root cause**, separate what was **proven from
the log/source** from what is **inferred**, and say plainly what you did *not* do and why. He
values a stated limitation far above a smoothed-over one, and he has corrected over-confident
claims more than once.

---

## Standing technical decisions

- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs; OpenXR 1.10.0 + XR Management 4.5.0;
  MultiPass.
- Never patch `ScenarioRuleLibrary`/Bolt; commit through UI seams only.
- License GPL-3.0 (LCVR/RepoXR pattern reuse, credited); SteamVR hands BSD-3.
- Module config: `dev.gloomhavenvr.<module>.cfg` via `ModuleConfig.Create`.
- Distance ladder (`WorldUI/CanvasConversion.8.Order.cs`): base 100, step 16, nearer = higher
  order; `OrderAboveDistance(eyeDistance, lift)` is the seam for non-panel plates.
- `HandRig.Wrist` is **not** in `HandRig.Root`'s frame: `Anchor_Wrist` carries +90° about X, so
  wrist +Y is along the fingers and wrist +Z is out of the palm. A uGUI canvas parented there is
  read from its **-Z** side.
