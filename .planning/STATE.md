# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop, multiplayer-capable.** Current build:
  **`NetProtocol.ModBuild = 138`**, awaiting its hardware run (MP test still outstanding). Rounds are run as parallel agents on
  disjoint file sets; every diff reviewed before merge, cross-file changes applied by the integrator.
- **Last update:** 2026-08-14

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

- **ModBuild 145** — the shelf riders and the fire, the two things 144 owed. No wire change.
  **Bundle 65,793,726 bytes.**
  **Shelf riders** (a bug the user ruled on). `GhvrShelfTip` moved into its own header and **the
  shelf became a rider like the rest**, so there is no privileged copy of the curve for a rider to
  drift from. Each rider is authored where it stands, so `vertex − hinge` already exists in its own
  object space; only the HINGE travels, through the same door `ApplyRig` uses for the candle
  positions, with a build gate asserting it survives the round trip to under a millimetre. The
  flame rotates rigidly, bends back about its own origin, lags along the true tangential velocity,
  and GOES OUT a quarter-turn into the fall. Not riding, stated plainly: the spark emitters
  (Shuriken simulates in world space; the bundle has no scripts) and the wash on the wall.
  **Fire, third attempt — and the failure was finally MEASURED**: the sprite was the CANDLE flame
  texture (one laminar teardrop, which enlarged is a big candle) and the "turbulence" ran at
  0.63–1.03 Hz where real fire is 3–8. GENERAL RULE: when two rounds of tuning fail to change a
  reading, measure the primitive instead of tuning the parameters. New atlas (wide holed bed, torn
  tongues, ragged puff), 4.6 Hz, pieces that DETACH and die. The forest burns for the first time.
  **Fire+Light gives the fire a SMOKE PLUME** — smoke is only visible when lit, so the physics that
  would have made that pair the loser is what makes it work.

  **NOT MERGED, and waiting in a worktree:** the ten non-fire element pairs and the 64-subset
  arbitration. The lane finished and is sound, but its base predates the fire lane and a `--3way`
  apply half-landed (shaders calling `GhvrPair*` helpers that had not arrived). Backed out cleanly
  rather than shipping a broken bundle. **Two findings from it that matter regardless:**
  (a) the weighted-blend composition rule — `value = (base + Σ wᵢ·targetᵢ)/(1 + Σ wᵢ)` with
  `wᵢ = aᵢ·bᵢ·authority` — which is what makes any of the 64 subsets compose without summing
  contradictory targets into mush; (b) **the forest has no light source that Dark spares**, so
  every subset containing Dark reads mostly as "dark" there, where the cellar keeps its candles by
  the user's own ruling. That asymmetry needs a ruling. (c) Shuriken emitters run with
  `useAutoRandomSeed` ON, which makes previews incomparable across bakes AND is a multiplayer
  determinism hole — pinning it is a real fix, not just tooling.

- **ModBuild 144** — TWENTY of 22 reported items, plus environment SOUND. No wire change.
  **NOT IN THIS BUILD: the two fire items** (the cellar fire still reads as candle flames; the
  forest still has no burning trees). The lane was stopped before it landed. The RECEIVING half of
  the fire wash did land — `EnvRoom`/`EnvGround` carry `_FirePos0/1/2`, `_FireCol`, `_FireRate` —
  and sits unread and black until a sending half arrives.
  **KNOWN BUG shipped in 144:** the candles and the fire standing ON the bookshelf do not tip with
  it (their wax, flame and halo are separate meshes/shaders). The user has ruled it must be fixed.
  **Bundle 65,681,683 bytes.**
  **"Keine 2D-Pappaufsteller" — said seven times in one message.** Every apparition is now real
  geometry, posed at bake time from the MakeHuman CC0 base mesh by a new pipeline script. Assets
  were searched first, per the user's standing instruction: Smithsonian Open Access IS usable
  (unauthenticated CC0 OBJ/glTF) and was rejected on non-technical grounds — its human forms are
  identifiable historical people, and **an apparition you can recognise is a statue, not a
  fright.** GENERAL RULE that came out of this: the earlier "no rigged FBX, so build it yourself"
  finding was about ANIMATION, not geometry — apparitions that barely move need no skeleton, and a
  600k-triangle museum scan is good SOURCE for a decimation step this project already has.
  **Three apparitions were invisible, and the cause generalises**: they were authored as "a hole in
  the scene" — coverage with almost no light. That works against a lit window or a candle-lit wall
  and is nothing against a wood whose 95th percentile is 0.005. **A hole cut in nothing is
  nothing.** Value plans must name the background they are cut against.
  **THE PUDDLE HAS BEEN INVISIBLE SINCE MODBUILD 134** — `PuddleMesh` wound face-down against
  `EnvPuddle`'s `Cull Back`; ~600 triangles discarded from every camera above the floor. It hid for
  ten builds because the moonbeam's pool lands 35 cm away and a bright wet patch in roughly the
  right place was taken for it; the tell was ModBuild 143's own note that tripling the ripple
  "moved a measured maximum of 3/255". **Fourth instance of the winding bug class.** Found by a
  LOCATE pass — paint it magenta, count pixels with `Cull Back` (0) vs `Cull Off` (134,000).
  **Ice could not have been tuned into ice**: two of its three terms were wrong in principle. A
  sheet of ice is NOT a better mirror than water — it is a rough scattering dielectric, duller and
  broader, and bright from straight above where water is a black hole. Sharpening the lobe made a
  stiller puddle, which is exactly what the user reported.
  **Moss stopped being a stain** by getting the five things a stain cannot have: micro-relief with
  an analytic gradient, its own normal that DESTROYS the one underneath, two greens mixed by the
  relief, a contact lip, and gloss only on the thin frontier. Two failed bakes on the way are
  instructive: an analytic pattern at frond scale has no mip chain (moiré), and four fixed axes are
  quasi-periodic (honeycomb) — fixed by warping the lattice with the patch field itself.
  **`EnvGround` had NO element response at all** — it belonged to another lane the round the
  channel landed, so the forest floor, which is what "die Lichtung" mostly IS, never read Light or
  Dark. Measured 1.00× before, 1.71×/0.60× now. In the cellar the candles are provably untouched
  (+1.3% / −1.1%) while the moon moves 2.09×/0.45×.
  **Environment sound (new)** — spatialised, synthesized (BBC's library is RemArc, NOT
  redistributable in a public mod), switchable, on the shared clock. It exposed that **there was no
  AudioListener on the VR head**: the ear sat at the parked flat-screen camera, so every positional
  game sound has come from the wrong place. The room runs at 21–26× scale and Unity's rolloff is in
  WORLD units, so all distances are stored in PERCEIVED metres with exactly one conversion site.
  **The frequency dial and multiplayer** — worth carrying because the user asked: no value "wins"
  and none has to. The dial is a monotone gate on a setting-independent hash, so a player at 0.9
  sees a strict SUPERSET of a player at 0.4 — same apparition, same place, same second, the lower
  setting simply misses some. A host-wins rule would need a wire record AND would override a
  comfort setting the player chose.

- **ModBuild 143** — seven reported items, all environment craft. No wire change.
  **Bundle 65,331,900 bytes.**
  **The rat: two one-line defects, both instructive.** Its entire albedo was a lerp between two
  greys 1.4 stops apart — measured on the shipped build at 43–53 luma, flat, over the whole animal,
  which is why it read as untextured. And "it shrinks instead of entering the hole" was literally
  `wp = lerp(P, wp, vis)`, a shrink written back when the hole was a black rectangle painted on the
  wall; the file's own header admitted it. Now a coat (pigment ramp + sin-free noise evaluated in
  the AUTHORED position so the fur does not swim as the body waves + bare-skin mask in the one free
  vertex channel) and a real entry down a bore derived from the route's own tangent — the south
  route arrives 60° off the wall normal, so the pocket is sheared to match. Hidden by stone after
  47 cm and PARKED there between crossings.
  **Real fire**: six seats, each with a cause in the frame. A burning crate is not a big candle —
  `EnvFlame` needed a bonfire branch (wide low bed, tearing tongues, temperature ramp), and the
  first bake proved it by rendering a row of tall candle flames on a crate.
  **Surface growth — Ice and Earth are ONE problem**: a coverage that advances and retreats through
  a fixed pattern, not a tint that fades. Per-pixel AFFINITY (noise in room metres + the normal
  map's own grain + where frost and moss really start: wall feet, mortar, shade, damp) with the
  element sliding a THRESHOLD down through it. GENERAL RULE, and the reason 142 failed: a global
  fade of a tint can never read as growth, however well tuned — the frontier has to be spatial.
  Grass/moss cards FOLD FLAT onto their own base edge at rest: zero area, no fragments.
  **Wind**: the prior art is unanimous — Crytek, Unity Tree Creator and SpeedTree all use the
  cubic-smoothed triangle wave, not `sin()` (no transcendentals, bounded time argument, |wave| ≤ 1
  by construction). Tip amplitude 4.5 cm chosen AGAINST the canopy shadow map's 5.5 cm texel, so
  the baked shadow cannot visibly disagree with the leaves moving under it.
  **Moon**: Dark slides Earth's umbra across the disc with a Danjon copper gradient — a SHADOW, not
  a body, because a body would have had to occult stars. Light swells it ×1.34 in RADIUS, not area,
  because the caller already multiplies 1.9 and 1.9 × 1.8 is a headlight. Both drive
  `GhvrMoonLight()`, which the room shaders multiply into their moon term, so the wood darkens
  BECAUSE the moon is being covered. **The cellar never sees the disc** — the lane rendered it and
  recorded the negative result; there, both elements arrive only as the beam's brightness.
  **AN INTEGRATION FAULT OF MY OWN MAKING**, found at the merge and worth carrying: two lanes built
  the two halves of the element channel in the same round, each declared the uniforms it needed,
  and each picked the obvious name — one made `GhvrElems` a struct, the other a function. It
  compiled everywhere EXCEPT in the one shader that wanted both headers. LESSON for splitting work
  by file ownership: a shared NAMESPACE is a shared file even when the files are disjoint. Name the
  contract in the brief, as I did for `GhvrMoonLight()` and `_GhvrHauntForce` — those two crossed
  lanes without a scratch.

- **ModBuild 142** — four reported items. No wire change. **Bundle 65,226,455 bytes.**
  **Element effects, the art half.** 140 shipped only the sensor, so the user saw nothing with Air
  up — and his log proved the sensing side was fine (`Air=Waning now 0.00 -> 0.40±0.12`, nothing
  reading it). All six now reach both rooms through nine `Env*` shaders and seven new emitters.
  **The zero state is proven bit-identical** (md5-equal renders for channel-off vs
  all-six-Strong-with-master-0 vs master-on-all-inert), which is what protects the levels tuned
  over ModBuild 133–139. **Light and Dark are not one axis**: ambient is lifted by Light only while
  Dark is down and crushed to 20% by Dark, while SOURCES are lifted by Light and HARDENED by Dark
  — both strong gives a black room with small fierce candle pools, not a grey average.
  **Horror redesign — a grammar problem, not a tuning one.** User verdict on 141: "weit entfernt
  von echtem Horror … eher lächerlich", and the render is literally an emoji. A handful of SDF
  primitives in a fragment shader can only produce A SILHOUETTE WITH FEATURES DRAWN ON IT, which is
  the grammar of a pictogram. What frightens is VALUE, and value needs a modelled surface with cast
  shadows — so the apparitions are baked from a CPU-rendered atlas (depth field, grazing key light,
  40-tap heightfield shadow march, cavity occlusion). GENERAL RULE: if a thing must look
  photographic, bake it; the fragment shader is where you spend ALU, not where you get craft.
  Two bugs found on the way, both would have shipped silently: `SaveMesh` drops UV1–UV3 when
  overwriting an existing asset (each re-bake kept the PREVIOUS catalogue's ids against the new
  positions — it looked exactly like a shader bug), and `floor()` on an interpolated integer picked
  the wrong atlas tile at 4.0 minus one ulp.
  **Shaft striping — the best diagnosis of the session.** The beams were combed into hard 5.5 cm
  vertical teeth. A shaft runs ALONG the light and both shadow-map axes are perpendicular to it, so
  the map coordinate is **exactly constant down a beam**: a vertical strip of blade reads ONE TEXEL
  COLUMN from canopy to floor, and one texel sideways swaps one of seven taps, which the bite ramp
  amplifies to 0.375 of full shadow held over the entire length. The floor never showed it because
  a floor moves in u AND v AND depth at once. **And my own verification was blind to it**: the
  probe walked the beam AXIS, the one direction in which the comb cannot appear, so the log
  reported a perfectly smooth profile while the picture showed a comb — both true. LESSON: a probe
  that samples along the axis of the thing it measures proves nothing about its cross-section.
  Fixed by splitting occluders by SCALE — trunks keep the crisp map and the bite ramp, needles get
  a separate 128² mass map (exact 4×4 area averages, bilinear, linear response, 64 KiB). Worst
  across-beam step 2.03% → 0.22% while the beam's own across-contrast ROSE 5.4% → 8.8%.
  "Shafts through trunks" is the necessary geometry of a beam parallel to the light (what shadows a
  point lies on that point's ray to the moon, which IS the beam further up) — not a bug; what was
  wrong is one shaft whose axis sat 0.21 m inside a trunk.
  **Test triggers** (new Advanced page): each element (Strong and Waning) and each apparition on
  demand. LOCAL ONLY — the element board is a multiplayer desync invariant, so the buttons override
  the PUBLISHED value, never the game's state.
  **Open:** the cellar's shelf face reads as a dark rounded blob in my own render — it is backlit by
  the candle behind it and shows no internal value. Needs re-siting or a key from the candle side.

- **ModBuild 141** — HORROR EASTER EGGS (user feature request), forest and cellar only. No wire
  change. **Bundle 65,265,924 bytes.**
  Twelve apparitions, six per room, plus the rat stopping to stare (6% of crossings, free). Slot
  beat 83 s, one per slot at most, shipped at half frequency → median 1.9 min apart, shortest
  quiet stretch 41 s.
  **The sync is the rat's schedule pattern generalised**, and that pattern is now the project's
  answer to "make it random but identical everywhere": cut time into slots off the SHARED clock,
  make every decision a hash of the slot index, and never let `Random`, per-client state, head
  input or `sin()` (vendor-divergent last bits) into it. Zero wire bytes, because the clock is
  already shared and element state is already replicated.
  **Nothing re-orients with the head** — the faces are world-fixed cards aimed at the BOARD, which
  is where the player is anyway. That is the trick that makes "it looks at you" legal under the
  permanent no-billboard ruling.
  Placement respects the play space (cellar ≥ 4.95 m from the board, forest ≥ 7.6 m); nothing
  flashes or lunges; longest event 8.6 s, shortest 0.34 s. Cost +12 tris and one draw call per
  room, no textures — five of six cards collapse to a point at any instant.
  **Elements FLAVOUR the haunt instead of fighting it**: Dark ×1.59 frequency and blacker, Light
  ×0.65 and sharper-edged with a dark contour (so a pale apparition cannot be washed out), Fire an
  unsteady warm rim, Ice ×1.35 duration / ×0.35 motion, Air drift, Earth sinks it. Read straight
  off `_GhvrElemA`/`_GhvrElemB`.
  **Assets: procedural SDF after searching six CC0 sources.** No CC0 face texture with an alpha
  channel exists; the only CC0 human faces are 100k–600k-triangle museum life masks that cannot
  smile, and the bundle forbids MonoBehaviours so nothing rigged could animate anyway. Drawn
  shapes morph (the grin widens while it watches) and stay crisp at 25 m.
  **Verified by rendering, not by intention** — `ENV_PREVIEW_OUT=… xvfb-run … RenderAll`, and the
  preview solves the SHIPPED schedule backwards to an instant the event is really running rather
  than forcing it with a debug constant. Two rounds of retuning came out of the renders: bodies at
  0.020 linear rendered as the brightest thing in frame (the wood's blacks are 0.002), and two
  SDFs had their heads swallowed by their own torsos.
  **Open risk for hardware:** the five dark cellar silhouettes are borderline — in my own render
  the window figure is barely separable from the wall, and a preview is BRIGHTER than the headset.
  The beam-dim (−45% measured) carries that event even if the figure does not. Knob is `_Rim` plus
  the per-card colours.

- **ModBuild 140** — five reported items + the sensing half of the element feature. No wire change.
  **Bundle 65,255,033 bytes — 140 NEEDS it.**
  **Focus pin.** The board waits for a hex pick from the acting character, but the VR focus could
  be moved to another one — and the confirm caps follow the FOCUS while the pick follows the
  ACTOR, so the player was left unable to confirm. `CharacterFocus.PinnedActor` requires all three
  of: a real targeting wait state read LIVE from `Choreographer.m_WaitState` (not the mod's
  event-driven mirror — a gate that takes a control away may not fire on a stale bit),
  `ThisPlayerHasTurnControl`, and `CurrentPlayerActor` mine and alive. Clearing the OVERRIDE (not
  the selection) is what returns the view, so no "nobody selected" state can appear. Use
  `CurrentPlayerActor`, never raw `m_CurrentActor`: during exactly these waits the game re-points
  it at the driven figure (`Choreographer.cs:4269, 9878, 10013`) and a summon there is a `CActor`.
  **Forest shafts — the third pass, and both earlier ones were wrong in ways worth carrying:**
  (1) a candidate search that maximises "how CLEAR is this beam" finds beams with nothing in them;
  the objective was the inverse of the goal. (2) **Alpha-test speckle averaged LINEARLY is a
  uniform dimming, not a shadow.** A fir crown is a sieve (23.6% of the atlas over cutoff), so a
  beam collects 0.2–0.5 coverage spread over metres — which is why "42% occluded" and "perfectly
  smooth beam" were both true, and why more throw could only have made it greyer. Fixed by a BITE
  RAMP (below a coverage threshold nothing casts, above it everything does; the PCF taps still
  average BEFORE the ramp so edges keep their penumbra) plus a minimum-visibility floor, which is
  what lets throw and bite be aggressive without extinguishing a beam. GENERAL RULE: when a
  visibility term is built from many small partial occlusions, average then THRESHOLD — a linear
  mean of speckle is grey.
  **Measure before tuning:** the lane computed EnvGround's own arithmetic and found the moon is
  **77% of the lit floor's brightness**, so `_DirScale` stayed at the user's approved 0.38. Lit vs
  shadowed now reads 2.04x (deepest 2.84x).
  **Forest ground props.** 'Roots' was a 2.4 × 2.7 m photoscan of forest floor **0.17 m thick** —
  a ground decal shipped as geometry, with no height at which it works; removed. The stumps' pale
  aprons were `GroundLift`'s 99.5th-percentile rule, right for a barrel and exactly wrong for a
  shell: new `Bed()` measures how far a prop's BEARING SURFACE stands over the ground under it and
  drops it, never lifts (Stump0 −18.7 cm, Stump1 −16.1, nine more 1.5–10.8 cm).
  **The rat was inside-out** — `AddTube` wound every triangle against the normal it handed the
  same vertex, so `Cull Back` discarded the near surface and drew the far one: you looked INTO the
  animal and saw its legs. **Third instance of this class of bug** (after the ModBuild 137 moon
  hull), so it now has a build gate — closed, outward, signed volume — which reports the old mesh
  as 82 unpaired edges and 282/282 triangles backwards. Its walk became a SCHEDULE hashed off the
  shared clock (2541/2541 crossings unique, 30% turn back, 32% sniff); `sin()` is banned from the
  hash because its last bits differ between GPU vendors and the schedule must be bit-identical.
  **Rat holes** were one flat black quad each; now really cut out of the wall through `WallMesh`'s
  own holes array, derived from the route endpoints, with an asymmetric arched mouth, a bent
  pocket, a ring overlapping the courses, and darkness as a vertex-colour gradient.
  **Element mood (new, sensing half).** Six elements polled — the game offers no event and nine
  methods mutate the board — smoothed and published on two global shader vectors. ZERO WIRE:
  element state is replicated by the game and desync-checked every round (`ScenarioState` codes
  117/118). Strong 1.0, Waning 0.4 BREATHING 0.28–0.52 on the shared clock (so the player can see
  an element is about to go out), Inert 0. It keeps sensing under MR **by ruling**: the MR rule is
  about GEOMETRY over passthrough, and a published number is not geometry — the "additive only,
  board-anchored" constraint belongs to the art that reads it.
  **Tooling discovered:** `EnvironmentsPreview.RenderAll` runs headless under `xvfb-run`, so
  environment lanes can render before/after and compare instead of shipping numbers. Previews are
  brighter than the headset, so only structural claims are settled that way.

- **ModBuild 139** — three reported items. No wire change. **Bundle 65,245,628 bytes — 139 NEEDS
  it** (the canopy shadow map is new bundle content and the cellar's meshes changed).
  **Turning is never blocked** — the ruling hardened from 138's "not another seat's targeting" to
  unconditional. 138 stopped one scope short: the acting player placing a waypoint is in
  `BoardTargeting` WITH turn control, so the 137/138 gate said "MINE" and stood turning down — but
  `AoeControl.CanRotate` declines there too (`CurrentDisplayState == MovementSelection`, and
  `WaitingForTileSelected` is an explicit refusal). Same mistake as on the remote peers, one scope
  smaller: the stick taken for a consumer that had declined it. Two changes, because "never" admits
  no exception — (a) `LocalTurnControl` asks the CONSUMER (`AoeControl.ClaimsStick`, computed from
  the gates `Tick` applies) instead of testing `VRMode`; (b) `AoeControl.ResolveRotationHand` moves
  pattern rotation to the hand `[Comfort] TurnHand` does NOT use, so the claim cannot land on the
  turn stick at all. `TurnMode=Off` frees it and rotation keeps the primary hand. `Flight`'s strafe
  test became hand-accurate (after the split, AoE and flight share the LEFT stick on the shipped
  defaults). GENERAL RULE: arbitrate a control against what really reads the axis this frame, never
  against a game STATE — a state is not a claim.
  **Forest — the trees cast shadows** (user: the shafts "clippen durch die Bäume"). No Unity Light
  exists in these prefabs to cast from, so it is baked DATA: an orthographic depth map along
  `MoonDir`, 512², 5.5 cm/texel, read by `EnvShaft` (blades) and `EnvGround` (the moon term only —
  ambient, all three point lights and the landing pool are separate addends and cannot be
  darkened). Three findings that each cost a pass and generalise:
  (1) **A depth, not a mask.** The shafts run UP through the canopy tear; orthographically a
  shaft's top and the boughs beside it are the SAME texel, so a mask blacks out exactly the thing
  ModBuild 137 exists to show. Only a depth answers "is the segment from here to the moon blocked".
  (2) **Alpha-cut cards are not solid.** Rasterised as opaque quads the crowns became a lid: 81.2%
  of texels occluded and all three shafts scoring 0% clear — it would have DELETED the shafts.
  Alpha-testing against the sprig atlas drops 2.4 M texel writes.
  (3) **Nearest is the wrong layer.** The nearest occluder in a texel is almost always the canopy
  20-30 m up-light, so the throw test measured the floor against the ROOF and the trunk four metres
  away never voted — 5.2% clearing shadow. The map now carries TWO 16-bit layers (nearest + deepest,
  hence RGBA32, 1024 KiB): the floor reads the deepest (exact for a floor), a blade takes the
  stronger of both. Clearing shadow 5.2% → 28.8%.
  **MAXIMUM THROW** (floor 9 m, blades 4 m, soft release) is a deliberate departure from physical
  exactness: exactly, nothing in a wood this dense is lit, and the authored fiction is the approved
  one. A blade is inside a trunk for ~1.2 m of its own length, so 4 m is "the tree this beam passes
  through" and 9 m is "the trunk whose shadow rakes the clearing".
  **Cellar — the box broken** (user: perfect 90° at every junction, cubes under the beams). The
  cause was in the wall code: `WallMesh`'s inward bulge TAPERS TO ZERO at every edge and hole rim,
  so the one place irregularity was wanted was the one place it was switched off. Now: rubble
  skirtings that heap and thin along each run (cleared at the stair and the rat's route, taken from
  `SnappedHole` and the rat's own Bézier), four corners each treated differently, a crumbling cove
  and three wall plates under the ceiling, hewn corbels instead of `BoxMesh` cubes, beams that sag
  and wander and are DERIVED from the two corbels under their own ends, and a plank plane that sags
  between them. The twelve transforms became TWO welded meshes — required, not an optimisation:
  the light rig bakes in OBJECT space, so twelve transforms sharing a material are all lit from the
  first one's position. −10 draw calls, −10 materials, +2346 tris. `PruneUnreferenced` deleted the
  14 now-unused beam/corbel assets, as designed.
  **Process note:** `scripts/build-bundles.sh` only PACKS. The environment prefabs (and any bake in
  them) come from a separate run — `-executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll`. A
  green bundle build that silently did nothing is what hid the first bad bake for a full pass.

- **ModBuild 138** — the big multiplayer round: 16 reported items. **Wire gains record 31.**
  Bundle 64,474,086 bytes — 138 NEEDS it. Root causes worth carrying:
  **Locomotion is local.** A peer's targeting froze everyone's snap-turn AND stick strafe:
  `SnapTurn.cs:88` / `Flight.cs:303` gated on `VRMode.BoardTargeting`, which
  `VRModeStateMachine` derives from `Choreographer` state that `ProcessMessage` sets on EVERY
  client (ownership is decided *after* the assignment). Matched log pairs from both machines on
  the same NetworkAction event prove it. New `Rig/LocalTurnControl` uses the game's own
  `ThisPlayerHasTurnControl` (true offline ⇒ SP bit-identical). RULE: another actor's turn may
  gate GAME actions, never the local player's locomotion or view.
  **Empty selection** was MP-only: `CardsHandManager.SwitchHand` has no ownership test, so the
  presented hand follows whoever acts; `CurrentHand()` correctly answers null for a foreign
  hand and `CharacterFocus` latched that as 'nobody' (and `Hide()` never clears `currentHand`,
  hence through the whole enemy phase). Selection floor added.
  **Capes**: Unity `Cloth` (no custom solver exists in the game), coefficients are absolute
  metres cached at spawn and never re-seeded; suspend during resize → rescale → resume.
  **Peer card fronts**: `FullAbilityCardAction`'s skin refs lack `[SerializeField]`, so
  `Instantiate` cannot copy them, the clone's `ApplyImage` early-returns, and a null sprite
  draws Unity's built-in WHITE texture. `SetSkin` replayed pre-activation. **Item chips**: the
  punch-out was fine — `CardH = CardW*1.15` was a guess for a card that measures 270x258.
  **The peer's 'box' was never the decision dock** (record 12 published nothing outside the
  damage prompt) — it is a use-bar slot tile, anonymous BY DESIGN. Resolved locally instead:
  the bars are per-client singletons the game raises from replicated messages. Same fact
  answers finding 8 — `CheckForInitiativeAdjustments` raises the bar on every client and gates
  only the ready button, so a teammate's client docked it on ITS board showing another
  character; the guard compared against the EXPLICIT focus override, null while following the
  game. Both halves closed (sender withholds, local dock refuses foreign-only bars).
  **The cap-state byte** was sampled, logged 46x and never assigned into `PresenceState` — one
  line; it made the pile cue dead and every mirrored skip cap paint through the DISABLED path.
  **Bar size dials** bounded an intermediate that is exactly 1.000 at the shipped zoom
  (his drop 3.368283 vs shipped 3.3683) — unreachable, removed; the band lives in code.
  **'50/50'**: `UISliderBar.amountTexts` is a LIST and the placed control also carries an
  inactive gamepad key-tip label; only ONE was bound. All are now.
  **Moonbeam**: hull wound against `Cull Front` (a debug pass measured ZERO hull pixels from
  inside) + a point sample on an axis singularity → analytic line integral, `t0 = max(0,…)` IS
  the inside case. **Sky yaw**: sky branch took the head yaw, room branch `boardYaw` — that
  difference WAS the moon/shaft mismatch and made two players see different moons.
  **MP env clock**: record 31 `[style][u32 ms]`, 7 bytes only while a shell stands, owner =
  lowest player id on the same style, followers walk 0.2 s/s. Style is a comparison key, never
  an instruction. Rat/drip/flicker/sway/shimmer and (integrator addition to both sky shaders)
  star rotation now share one epoch. NOT synced: shooting stars, fireflies, motes, fog.
  Flight max 3. Wire tests 1504 → 1520 (+16 for record 31's vectors).
  NOT DONE / next: the per-card gold pulse on usable item chips in the open fan is still
  unsynced; a deliberate PING (marker at a hex every peer sees) is written up as its own round.
- **ModBuild 137** — his tuned setup is the shipped default. 31 values taken over VERBATIM from
  the cfg drop via `python3 scripts/rebase-defaults.py apply` (standing rule: a dropped cfg is
  always against the NEWEST build — never re-express it, see memory tuned-cfg-drops-are-current).
  Shipped board style is now **Bronze**; the play tray moves to where he holds it (TrayForward
  0.585→0.679, TrayDown 0.094→0.127, TrayRight −0.264→−0.035, TrayPitch 33→42.4, TrayYaw
  −41→−10, TrayScale 0.57→2); BoardScale rebased on all three boards (Oak 0.924→0.554, Steel
  0.299→0.437, Bronze 0.4→0.437); the item-use slot gains per-board offsets on all three; the
  decision dock, pile, rest discs, slot overlays and active card follow; world scale
  SavedScaleMultiplier 2.82→3.37; RoundButtons Depth 0.009→0.015 and OffsetZ 0.025→0.005.
  Only `Defaults/{Cards,Rig,WorldUI}.cs` changed — no code, no wire, same bundle as 136.
  **`rebase-defaults.py check` exits 0 for the first time** — the reference cfg directory was
  missing until now, so that fifth gate had been reporting an environment gap in every round's
  worker reports. The 109 cfg keys with no Defaults line are correctly left alone; ~20 of them
  are the dials removed in 136's audit and are inert orphans by design.
- **ModBuild 136** — STACK TRACES RESTORED (the round's biggest win), settings audit round 2,
  moonlight instead of lasers, Off (black) environment, three reported bugs closed.
  **DIAGNOSTICS:** `GloomhavenShared.LogBuildInfo()` (decompiled GH.Shared:57-61) calls
  `Application.SetStackTraceLogType(..., None)` five times at boot including LogType.Exception —
  so EVERY exception in every log this project ever read was a bare unattributable one-liner
  (1172 in his run). `Core/ExceptionTraces.cs` restores ScriptOnly and re-asserts on scene load;
  Shutdown writes the game's authored value back. Traces land in Player.log only. **From now on
  exceptions are attributable — read Player.log stacks before theorising.** The restart NREs are
  game-side (they fire identically on quit-to-menu, which is no restart); the options-tab burst
  is one NRE per clone of the game's settings-row prefab (AdvancedIndex = 11 link rows → exactly
  1), harmless. Found while proving it: WorldUIModule's ~25-call shutdown chain ran UNGUARDED —
  one throw silently skipped every restore below it (MrBacking alphas, ActorBars, flat screen);
  that chain + Hands/VRHand/VRCard teardowns are now per-step TickGuard-isolated, order verbatim.
  Diagnostic `RESTART TEARDOWN` (released / already-destroyed census 2 frames later).
  **SETTINGS ROUND 2:** 20 dials removed, 16 clamped, method mirrored from round 1 (8594ebd).
  Decision test that settled every borderline: *does the flat game let you switch this display
  off?* → readouts REMOVED (initiative track, element board, objectives, stat panels, prop info,
  enemy reveal, actor bars, tooltips, action hints, hex outline+fill), mod-invented extras KEPT
  (wrist HUD, combat log). Also removed: ClickLatch (off = nothing clickable), ClickMode (only
  'execute' ever worked), ForceMouseMode, 2 map-composite switches (off = documented black
  screen), Keyboard (unfillable text field blocks campaign creation), TutorialVRAdapt (off = the
  known camera-step deadlock), MenuRig (off = no rig at all). Clamps where 0 deleted the thing
  (canvas scale, screen size/distance, card width, inspect scale, fan radius, rest discs,
  per-board furniture scales); BoardScale floored at the READ because the two-hand grab writes
  that key. No wire change (no removed key is in a board section), 1506 assertions unchanged.
  **BUGS:** rest keycaps now ride the game's own offer predicate (phase ==
  SelectAbilityCardsOrLongRest && !IsImprovedLongResting) instead of `can || selected` — the
  boots prompt kept 'Lange Rast' up because the selected flag survives until confirmation, and
  vanilla doesn't switch hands in that phase (hence "egal welchen Character"). Commit cap and
  item-use cap now share ONE derived seat: the Y offset was exactly half the tuned per-board
  spacing because the item cap counted as a third cluster member it is never co-visible with
  (5 mm Steel / 4 mm Oak / 0 Bronze — matching his report); the peer mirror's 52 mm error fixed
  with it. **[Sky] OffBlack** appended (never renumbering): hides the sphere, loads and spawns
  nothing, doesn't set `_active` so the far plane stays the game's, MR untouched. Perf audit:
  the mod never HIDES an environment, it destroys it — so the "disabled renderer still
  simulates" trap cannot apply; cellar's inactive GlowTemplate has no ParticleSystem; prefabs
  have 0 MonoBehaviours / 0 colliders. Diagnostic `SKY IDLE`. **CONTENT:** the five moonbeam
  slats become ONE analytic volume (EnvBeam) with NO faces — density from ray/axis distance,
  super-Gaussian section, path-length term so it brightens looking along it; hull sized where
  density is 1e-6 of peak. Pool re-lights the FLOOR'S OWN ALBEDO through an elliptical mask
  (mortar comes up cold) instead of a glow sprite; additive sill glows were tried and REJECTED
  (edge-on = one-pixel line = the laser failure again). Cobwebs from TextureCan others_0015
  (CC0; ambientCG/PolyHaven/Kenney carry none, the 4K pack forbids redistribution). Forest
  ground `_DirScale` 0.38 + the cold tint the trunks got in 133 (0.61 → 0.22 at centre) with the
  board's landing patch raised; `_VCol` debt paid with per-class floors so the canopy recedes
  instead of vanishing. Fog velocity curves unified (his log's warning spam).
  **TOOLING FINDING:** PreviewEnvironments rendered into an 8-bit LINEAR RT — first step after
  gamma is 18/255, so every dark gradient showed hard bands that do not exist on the headset.
  Now ARGBHalf. **Earlier rounds' previews under-reported darks and over-reported banding.**
  Bundle 64,473,235 bytes — 136 NEEDS it.
  MERGE NOTE: two lanes' stale-based patches reverted already-merged lanes this round; caught
  both times by the marker sweep. Restrict patches to owned paths AND grep every prior lane's
  marker after every apply — see memory git-worktree-merge-hazards.
- **ModBuild 135** — cellar reworked into a place, hex decal leak killed at the root, forest
  polish. FOREST APPROVED by the user this round ("gefällt mir schon sehr gut") — only the three
  requested fixes touched it (axe pose derived so it bites the stump centre, fireflies −40%,
  shooting stars at 40 m / 0.35× angular size). CELLAR: ranges 4.6/4.0/4.6 → 3.1/2.9/3.0 m plus
  a hardened falloff (`_PtHard`, default 0 so the forest is bit-identical) — north wall −4.8× at
  1.55 m, −870× at 3 m, table top unchanged. The flicker DID reach surfaces; it was invisible
  (±10%) and desynchronised — all flame cards shared one material at phase 0 while light slots
  ran 0/2.1/4.4. Now ±31%, `SlotPhase[]`/`SlotRate[]` the single source for flame, halo and
  puddle reflection. TWO more silent-default bugs of the `_RimDir` class: cellar `dirWorld` was
  hand-typed 21° off the visible moon (→ MoonDir), and SHARED MATERIALS across transforms baked
  object-space lighting as if every bar/candle stood where the first one does (welded to one
  mesh each). WINDOW: bars floated because they sat proud AND `WallMesh` quantises the hole to
  its 0.16 m grid → new `SnappedHole()` + a 0.34 m reveal (zero-thickness walls had no "inside"
  before); window moved west so the beam lands 3.95 m out, clear of the 3.25 m play radius; the
  beam is 5 EnvShaft slats, one per bar gap, so bar shadows are geometry. LIFE, script-free on
  one clock: EnvDrip (hang 1.55 s → gravity fall 0.813 s → 5 ballistic splash droplets) with
  EnvPuddle's ring train phase-shifted by exactly hang+fall; EnvCritter rat on a cubic Bézier
  routed THROUGH the moonbeam and candle pool (build assert: ≥3.25 m from centre, ≤0.12 m from
  the beam axis; it alone gets the shaft as a real light); cobwebs with `_Sway` (mip coverage
  preserved or thin threads vanish); blinking eyes on ONE shared material; unphased `_Gust` so
  all flames lean together with the dust. HEX DECAL round 2: intermittency REPRODUCED — a target
  without a stencil attachment degenerates `Comp Equal` to always-pass (FlatScreenStereo.2
  documents the mod hitting exactly that; XR eye textures aren't ours). Stencil COLLISION
  eliminated (poison draw setting all 255 bits changes nothing — the prepass rewrites over its
  own coverage). Second intermittency found: 133's band was along the view ray, vertical reach
  tol·sin(elev) — same tile painted at 57°, vanished at 8°. Now an object-space SLAB (y=0 to
  y=−tol, both ray∩plane of the same ray), roles swapped so the far bound is a literal ZTest
  GEqual needing NO stencil; Ref 0 keeps zero-reading targets passing; guards clamp non-positive
  /NaN tolerance. Verified across 16 harness scenarios incl. the no-stencil and poison cases.
  Bundle 64,152,301 bytes — 135 NEEDS it. Style labels now plain "Keller"/"Nachtwald".
  KNOWN DEBT: `EnvRoomCutout` receives `_VCol` but never applies it, so the forest canopy's
  baked depth fade does nothing — deliberately NOT fixed (it would visibly darken the approved
  room); decide next round. If the highlight ever draws THROUGH figures, that confirms the eye
  target lacks stencil and needs a C# fix.
- **ModBuild 134** — board floats small in a large place, sky fully procedural, night dark.
  PROPORTIONS: 133 normalized on the prefab's TOTAL renderer extent (forest 60.2 m incl. tree
  bands out to 28.5 m) → the CLEARING came out smaller than the 30.95 wu board and the tree ring
  stood inside the level (his log 537; the cellar only looked sane because total ≈ interior).
  Fix: prefabs carry a `PlaySpace` marker child (localScale.x = authored usable diameter; swamp
  9.0 m, cellar 6.5 m), taken and DESTROYED before any other pass sees it; runtime scales that
  to `PlaySpaceToBoardRatio` 4.5× the board world extent and drops the floor
  `FloatGapToBoardRatio` 0.75× below the board underside so the board HOVERS. Both ratios are
  board-proportional — an authored-metre value would break under zoom. Worked on his numbers:
  clearing 139 wu (perceived 9.4 m vs 2.1 m board), tree band at 14.9 m perceived, his real
  floor 2 cm under the new forest floor. No marker = old total basis + warn + FALLBACK note in
  the placement line. Far plane budgets the TOTAL art, not the play space. Sanity window and
  zero-re-seat both intact. SKY: astrophoto + its bake pipeline DELETED. Gradient (inverted —
  darkest at the horizon, which also killed the lifted ridge band) + 8404 Yale stars (cut to
  the catalogue's own V≤6.5 limit) + Milky Way in REAL galactic coordinates (IAU 1958; basis
  from NGP + centre, orthogonality 1.4e-6 and l(NCP) 122.93190 vs 122.93192 published — both
  HARD build failures) + sub-visual star dust (denser in the band — the MW *is* unresolved
  stars); one celestial frame, dome direction from the OBJECT-SPACE vertex (only space where
  dome and star geometry cannot slide); moon 3.27°→1.39°, occludes stars per-star in the vertex
  shader (no depth-buffer assumptions). DARKNESS: ambient ~3× down, depth fade 6.5–22 m →
  3–10.5 m, far fog α 0.055→0.012, lantern range halved, WHILE dirCol and _RimCol go UP —
  contrast not dimming. Bug found en route: `_RimDir` was never set, so the moonlit-side gate
  read EnvRoom's default and rimmed trunks all the way round. New build gate
  AssertPlaySpaceClear walks transformed vertices and FAILS on intruders (caught 6 props + the
  fog donut). Bundle 64,097,948 bytes (−12.3 MB) — 134 NEEDS it. UNVERIFIED: ratio 4.5 and gap
  0.75 by eye; `_MwGain` 0.040 (band soft by design); cellar window now a dark hole.
- **ModBuild 133** — BOARD-ANCHORED room (invariant at last), hex decal banded, creepy forest,
  real turning star sky. ANCHOR: his ruling "Verhältnis zum Raum drumrum MUSS fix ... nur drehen
  und kleiner/größer beim Zoomen" is incompatible with perceived-constant rooms. Room branch
  (RoomGeo + GroundFog/GroundFogFar/Fireflies) is derived ONCE from the board — 3.0× its world
  extent from the live hex tiles via ObjectCacheService (+half a hex per side, SpawnRing's own
  math), floor at the board underside (hex renderers AND SceneRegistry room-chunk volumes,
  since floor ART hangs off map tiles), yaw from the board hierarchy — then WORLD-FIXED forever
  (zero writes, zero re-seats: 131's teleport is structurally impossible). Invariance proof:
  all locomotion writes the rig (WorldGrab 362/401/403, Flight 213, SnapTurn 162), nothing
  moves the diorama. 130's miniature root cause found: it encapsulated DISABLED BoxColliders
  (zero bounds at origin) → 2.1 wu; now a perceived-extent window [0.2, 20] m refuses to place
  rather than ship nonsense. Sky branch unchanged (perceived-constant, RigPoseVersion re-seat).
  HEX DECAL: NOT a Projector (HexSelect_Control.HexProjector is a MeshRenderer decal box) —
  HexDecalStable's depth scheme was one-sided (LEqual rejects nearer, accepts ALL farther; the
  black sky hid it until the room floor arrived). New ColorMask-0 pre-pass exports P pushed
  back by _VRSurfaceTolerance (0.25 m) with ZTest GEqual + stencil bit 128; colour pass draws
  Comp Equal. Verified by standalone renders (leak cut at the board edge, 0 changed px for
  shared borders and figure occlusion). _CameraDepthTexture rejected: off by default
  (HeadDepthPrepass=false) and a full extra scene submission per eye under MultiPass.
  _VRStencilBit=0 = rebuild-free kill switch; all degenerate paths fail to the OLD behavior.
  Also: game's real projectors (DeathDissolve, RFX4) now ignore the mod layer, additively.
  FOREST (moor rejected): 106 procedural trunks in 4 depth bands to 28.5 m w/ photoscanned
  needle atlases, canopy torn toward the moon, 3 moonlight shafts, forest floor + trodden path,
  2 HorizontalBillboard mist layers, will-o'-wisps, far lantern, eyes at 12.5 m, story props.
  SKY: 5080 Yale Bright Star Catalogue stars (public domain; HYG rejected CC BY-SA, Shadertoy
  NC/SA) as per-star quads, B−V→temperature colours, celestial-pole rotation 1 turn/2880 s,
  extinction, rise/set, Rozenberg scintillation; photo dome suppresses its own star cores +
  haze veil + dither + wrapped clock. FLOATERS: Renderer.bounds is a transformed-AABB (399 mm
  phantom drop on the tilted tree), photoscan pivots arbitrary (40 cm) → transformed-vertex
  heights, pivot re-centring, ray-cast stacking, overhang = build error, baked contact pools.
  Bundle 76,384,749 bytes — 133 NEEDS it. UNVERIFIED: reversed-Z branch of the decal band,
  stencil bit 128 free on this rig, room-to-board ratio 3.0 by eye, preview gamma vs headset
  (rooms went darker this round).
- **ModBuild 132** — game-asset environments DELETED (user ruling 2026-08-13); custom photoscan
  rooms; teleport killed. THE TELEPORT ("darf unter keinen Umständen passien"): 131's
  scale-settle re-seat fired 5× in his log — re-seating a room the player stands IN reads as a
  player teleport + board displacement. MapGen deleted whole (3,450 lines); SkyAlternative back
  to the 128 FX-shell model (scenario gate, MR precedence, one world-anchored perceived-constant
  frame, recenter-chord-only re-seat). STANDING RULING: never re-seat an occupied room. Full
  saga + all Apparance/Addressables knowledge preserved in .planning/game-env-postmortem.md +
  memory game-env-rooms-abandoned. CUSTOM ROOMS (Poly Haven CC0 photoscans; ready-made CC rooms
  researched and rejected as stylized/low-poly/paid/login-gated): Env_Cellar = 10.5×9 m stone
  room, candle groups w/ animated flame cards, barred star window, stair alcove, barrels/table/
  shelf, 105k tris; Env_Swamp = 30 m moonlit clearing, mud/leaf heightfield, 3 ponds w/ moon
  glint, snags/logs/mossy rocks/standing stones/reeds, treeline berm, far fog ring
  (HorizontalBillboard), 150k tris. Five new baked-lighting shaders (EnvRoom/Cutout/Ground/
  Water/Flame) — ZERO real Lights, zero scripts in prefabs; closed opaque floors. Assets under
  Environments/Imported/ (~50 MB srcs, polyhaven_pipeline.py reproduces). Bundle 65,8xx,xxx
  bytes — 132 NEEDS it. KNOWN OPEN: perceived-constant model means extreme zoom-out can put the
  board below the cellar floor line (the old finding-3 tension, documented in the postmortem) —
  if reported again, solve in CONTENT (softer floor edge), never with re-seats. Cellar leans
  warm-amber; light constants in BuildEnvironmentRooms.cs rig blocks for one-line tuning.
- **ModBuild 131** — LIFE-SIZE single-room environment, alive, floor-tight. Same bundle as 130.
  130 verdict: 'Map ABHM' LOADED (dual route works) but as a 7 cm miniature next to the board —
  the 130 board-relative sizing was the bug (board extent is world-tiny at diorama zoom; log:
  5.8 wu at rig scale 85). DELETED; sizing is perceived again: room world scale = live rig
  scale, main room 11 REAL meters, player's floor point at room center, floor at real floor,
  world-frozen between seats (finding-3 ruling holds mid-gesture), RE-SEAT when a zoom settles
  beyond 1.4x (0.7 s settle timer; recenter-chord event class; board-appears probe re-centers
  SKY only). (a) ONE ROOM: preference lists = DLC_SC*_RM* single-room scenario maps (Cellar
  SC02/04/06_RM01, Swamp SC03_RM01/SC16/SC20_RM01; all in his MAP CATALOG census; Map A
  terminal), multi-tile arrivals CULLED to center tile; main-room measure fixed (renderer
  bounds ≤ map bounds — collider claimed 60.2 vs map 35.0). (b) ATMOSPHERE: particles/
  Animators/flames PRESERVED; LightFlicker kept + rebased per seat (its Start caches the
  STAGING pose — flames would teleport); DynamicAmbience SetLightLevel(1) then parked;
  StaticAmbience destroyed (writes RenderSettings globals — from source). (c) FLOORS never
  deleted (130 heal REMOVED StoneRooms.Floor.Tile = the see-through floor): 4-rank variant-
  stripped donor match across ALL loaded packets, clone-patch holes, re-enable reveal-disabled
  hex floors, 10x10 FLOOR GATE; RESOURCE TOPOLOGY census on first miss. UNVERIFIED: DLC room
  themes (inferred from scenario numbering), floor-band heuristic vs short props, room-board
  intersection when recentering far zoomed out.
- **ModBuild 130** — composites LOAD (dual route), board FIXED in the room, cellar night sky.
  129 verdicts from his log: staging invisible CONFIRMED, red cubes 0 CONFIRMED (heal found
  donors), but 'Map ABHM'/'Map DDM' "failed to load" → Map A rectangle again. REVISED root
  cause: the maps EXIST on his install (Player.log boot dump line 1490: 113 'Map *' prefabs
  in always_loaded_standalone) — the full-path Addressables key resolves only for
  single-letter maps on his catalog. Fix: per-candidate DUAL ROUTE — (1) the game's
  always-loaded asset store (AssetBundleManager._alwaysloadedHandles, session-lifetime,
  synchronous), (2) Addressables path key; preference lists Cellar ABHM→GI→A / Swamp
  DDM→LML→A (all verified in his dump); 'MAP CATALOG' census line on first activation.
  BOARD FIXED (his ruling overrules perceived-constant): frame split — ROOM branch (map +
  GroundFog + Fireflies by node name) world-FROZEN after placement, zero per-frame writes,
  zoom changes only perceived size; sized 2.75× board's larger extent, board center =
  main-room center, room floor = board underside (can never sink); SKY branch (StarDome,
  shooting stars, dust motes) keeps NotifyRigScaled pivot algebra; far plane = max of both
  budgets. Board drag needs no follow code (all locomotion writes the rig — WorldGrab 304/
  401-403, Flight, SnapTurn). CELLAR SKY: Env_Cellar gains the swamp's StarDome node (shared
  assets by GUID, 'StarDome' = node-name contract with src). Bundle 41,865,652 bytes — 130
  NEEDS it. UNVERIFIED: ABHM/DDM room shapes/themes (letter-count inference), always-loaded
  route on future game updates (census covers), room-floor-at-table-height look (new ruled
  behavior, his test judges).
- **ModBuild 129** — REAL authored maps, invisible staging, zero red cubes, astrophoto sky.
  128's rendering fix CONFIRMED on hardware (census: 680 drawing, T+3s survival) — remaining
  findings: staging visible (fixed −50 wu = 36 cm real at rig scale 137 — user watched the
  build as a miniature), Map A too generic ("Rechtecking ... kein interesannter Ort"), 9–19
  residual Red Cubes. REAL MAPS: authored multi-room composites Cellar='Map ABHM' (4 rooms) /
  SwampNight='Map DDM' (3 rooms), AUTHORED style axes kept (fill only Inherit/Default axes;
  swamp forces Tone=ForestMoonlight, logs overrides), main room → 10 m / map ≤ 24 m, placed by
  MAIN room center (bounds center can fall inside a wall); fallback composite→Map A→FX shell.
  STAGING: ≥ 50 REAL meters down AND beyond head-cam far plane + hidden layer runtime-verified
  vs Camera.allCameras every poll (head mask 0xFFFFFFFF is re-asserted per frame by its owner —
  carving rejected). RED CUBES: decompile — a loaded category list lacking a piece MINTS a
  session-poisoning null placeholder (terminal; RefreshResourceList purges Objects, not lists).
  Fix: warmup from the map's own effective styles + per-list placeholder purge + HEAL at settle
  (donor by piece-suffix via transform-derived procedure frame, alias injected into the game's
  own list) + no-donor removal (gap beats red box); census prints NAMES, 0 by construction.
  SKY (round 3, user: real + hi-res from the internet): 16k Rogland Clear Night (Poly Haven
  CC0) → 8192x2560 BC7 sky-band dome (float pipeline, TPDF dither vs banding, sRGB dark-end
  precision, npotScale assert), painted moon kept, twinkle reduced; bundle 41,863,274 bytes —
  129 NEEDS it. UNVERIFIED: composite catalog presence (log names what loaded), donor
  availability for Marsh floors/doors (worst case gaps), composite renderer count perf.
- **ModBuild 128** — the generated room RENDERS, the ally banner's real occluder, the painted sky.
  ROOM (was: gray floor, black/no walls): FOUR causes — (1) the 127 freeze (disable
  ApparanceEntity) let ApparanceEngine DESTROY all generated content one tick after placement
  (EntitiesGameTick ignores 'enabled'; CheckEntity→DestroyEntity wipes Generated-Content — the
  user saw the template skeleton); fix = empty m_GenerationTiers/m_GenerationRoot/m_Instances
  FIRST, then disable. (2) Apparance resource packets load on demand and a miss caches a
  session-poisoning 'Red Cube' fallback engine-globally by name; fix = WARMUP phase (pre-load
  style packets, purge via RefreshResourceList(clear_unused:true), settle refuses while
  fallbacks remain). (3) DynamicAmbience clones every light at intensity 0 — only the real
  scenario's UpdateAmbience blends tiles in; fix = SetLightLevel(1f), room lights masked to MOD
  LAYER ONLY (never restyles the board), ranges rescaled with room scale (Unity light range
  ignores transform scale), 2-point fallback rig. (4) WallSegmentFade adopted the room's doors
  (log 4253); fix = mod layer from birth + tile machinery components DESTROYED at finalize (no
  sweep can adopt). Diagnostics: grep 'ROOM CENSUS' (placement, lights, T+3s survival proof).
  BOARD CENTERED (finding 4): frame origin = ProceduralScenario tile-bounds center (ALL tiles
  incl. hidden — reveals never re-center), player-point fallback + 60-frame re-center probe.
  BANNER ROUND 4 ("unverändert"): all three prior mechanisms provably ran in the 127 log,
  pixel-identical cut ⇒ occluder is UI CLIPPING (the initiative ScrollRect viewport's
  game-owned prefab-level clipper; the ally banner is the only part crossing its top edge —
  enemy popups have no banner, hence immune). UnmaskedUiGraphics: maskable=false +
  RecalculateClipping() per shown-popup graphic (BOTH load-bearing in shipped uGUI 1.0.0),
  internal-clipper guard, restore at both doors; rounds 1–3 STAY. Proof: 'BANNER-CLIP DIAG'.
  PAINTED SKY (style round 2): dome repainted (milky-way band, 4200 PSF stars w/ halos,
  repainted moon + layered halo, comet streaks, bokeh fireflies, wispier fog — HorizontalBillboard
  held), 64x32 dome, slow vault drift; star tex BC7 2.1 MB VRAM (was 8.4), bundle 30,152,582
  bytes — 128 NEEDS it. UNVERIFIED ON HARDWARE: warmup token list is inferred (census names
  any residual fallback); room light budget vs pixelLightCount; board-center pop if the board
  appears after fallback placement.
- **ModBuild 127** — environments are SCENARIO-ONLY and built from the game's own art.
  SCOPE (user ruling: "Ich WILL garnicht das die Umgebung im Menu rendert - sondern nur im
  Szenario"): SkyAlternative gates on VRModeStateMachine.ScenarioBoardExists (Choreographer-
  alive, NOT the save-state phase which flips during loading); menu/world map untouched, leaving
  a scenario stands down next tick + cancels in-flight generation. GAME-BUILT ROOMS
  (SkyAlternative.MapGen.cs, new): Addressables 'Map A' (session-cached handle) staged 50 wu
  below the viewpoint in the ProcGen scene, ambience muted FIRST, styles written (Cellar =
  Dungeon/StoneRooms/Candlelight; SwampNight = Forest/Marsh/StillWaters/ForestMoonlight), walls
  populated, detail focus BORROWED onto the staging map for the ≤30 s build window (staging is
  outside detail range — round-3 evidence, not conditional), settle poll = renderer census
  stable + no busy entities (30 s timeout → FX shell only, one-shot warn), freeze = disable
  Apparance COMPONENTS never GameObjects (inactive entities destroy their native side), strip
  colliders, mod layer, normalize to ~9 real meters (n = 9/max(bounds.xz) at staging scale 1,
  clamp 0.02–10), floor = avg ProceduralMapTile height, center → frame origin (player inside).
  GameEnvProbe RETIRED (923 lines — mission complete; logic lives in MapGen). FX SHELLS rebuilt
  from own shaders, third-party low-poly art DELETED (folder 4.2 MB → 980 KB; bundle 29,639,808
  bytes — 127 NEEDS it): Swamp = star dome (baked moon+twinkle) + shooting stars (Stretch,
  cameraVelocityScale=0) + fireflies + ground fog as HORIZONTAL billboards (user fog ruling:
  never re-orient with head movement — PERMANENT CONSTRAINT in BuildEnvironments.cs); Cellar =
  dust motes + disabled GlowTemplate. UNVERIFIED ON HARDWARE: whether disabled components fully
  stop native re-tiering after placement; the 9 m normalization target may want tuning. First
  scenario start with a style selected is the real test — the engine dresses live, log records.
- **ModBuild 126** — environments become WORLD PLACES, the game-asset probe, ally banner round 3.
  FREE MOVEMENT: all locomotion writes RigRoot (Flight 213, SnapTurn 162, WorldGrab 304/362/401/
  403); the env now spawns world-anchored at the player's floor point + gaze yaw, re-seats on
  RigPoseVersion bumps, and mirrors rig-scale writes around the SAME pivot (NotifyRigScaled from
  WorldGrab/Comfort; algebraic no-drift proof in SkyAlternative) — fly/turn/drag/walk move
  THROUGH the room, zoom never changes its perceived size. STYLE PLAN: user wants Gloomhaven's
  own style, ideally game assets — investigation (.planning/game-env-assets.md) picked APPARANCE
  MICRO-GENERATION (Map A prefab + style enums Dungeon/StoneRooms/Candlelight resp. Forest/
  Marsh/StillWaters/ForestMoonlight); GameEnvProbe ships in 126: passive catalog/engine dumps
  every session + one-shot [Sky] EnvProbe menu experiment (detail-focus pointed at the probe map
  — distance-scaled synthesis would fake 'never built'; idempotent cleanup) → ENV PROBE VERDICT
  decides B vs scenario-time capture (C). Low-poly envs stay as placeholders. ALLY BANNER R3:
  round-2 lift WORKED (log 529); remaining occluder = WORLD DEPTH (the board's raised wooden
  rail in front of the canvas plane where the banner reaches up). Fix: OnTopUiGraphics — every
  Graphic in a SHOWN popup swaps to a session-cached ZTest-Always clone of its own material
  (ActorBars mechanism inverted, TMP via fontSharedMaterial, CardEffects subtrees excluded,
  reference-checked restore). NEXT after his log: read ENV PROBE VERDICT → build the styled
  environments via the winning approach.
- **ModBuild 125** — 3D ENVIRONMENTS (panoramas rejected + removed), mouse round 2 + INCIDENT
  RESTORE, ally banner. [Sky] Style = Default/Cellar/SwampNight ("Umgebung"): two bundled
  prefabs from CC0 Quaternius packs (Env_Cellar 53k tris — stone room, torches, chests, stairs
  into darkness; Env_Swamp 29k tris — 2200-star dome w/ baked moon + twinkle shader, moon-glint
  water, fog banks, fireflies, glowing mushrooms, shooting stars), ALL world-anchored (user
  rule), no bundle scripts, spawned under RigRoot at identity (stands still in real space,
  perceived-size-constant under zoom, zero per-frame writes), MR on = always off. Preview
  renders reviewed by builder AND integrator before shipping (6 iterations; renders in
  scratchpad env-previews/). Bundle rebuilt 30,060,345 bytes (panoramas deleted) — 125 NEEDS it.
  MOUSE: OS cursor hidden per frame (WarpCursorPosition moved the real cursor); virtual pointer
  parks at (-4096,-4096) unless the laser actively drives it. INCIDENT: the 8594ebd merge had
  silently reverted 123's MouseWorldSurfaceCut + grip fall-through (stale-base diff) — restored;
  hazard recorded in memory (restrict merge patches to owned paths; grep prior lanes' markers).
  ALLY BANNER: 'VERBÜNDETER' clip closed (branch lift + ancestor-chain flatten, both occluders).
- **ModBuild 124** — SKY ALTERNATIVES. [Sky] Style (Default/Night/Sunset/Cellar; DE Standard/
  Sternenhimmel/Abendrot/Gewölbekeller), curated in Grafik ▸ Darstellung, localized dropdown,
  live-switchable. Three CC0 Poly Haven panoramas (4096x2048) + GloomhavenVR/SkyPanoramic
  (view-direction equirect, non-occluding by construction) in the REBUILT 39 MB bundle
  (unity-2021.3.5; prebuilt/ updated — 124 NEEDS this bundle, old one = one-shot warn + game
  sky untouched). Runtime: SkyAlternative hides GH_SkySphere (renderer.enabled=false, the safe
  half of SkyBackdrop's trap), shows a mod-layer head-following inverted sphere; lazy bundle
  load, session-kept textures. MR precedence hard-wired: MR on stands the alternative down
  before the chroma sweep; dial re-applies on MR off. Local-only, nothing on the wire.
- **ModBuild 123** — the seven-item round after the border victory. MOUSE DEAD: game EventSystem
  pointer swept world UI with the head; MouseWorldSurfaceCut strips game-pointer hits on
  world-space root canvases at the shared source (EventSystem.RaycastAll postfix; mod pointer
  ids pass). GRIP FALL-THROUGH: trigger-only highlight no longer eats the grip — nearest
  GrabWithGrip target re-elected (tray bar beside a highlighted card). MENU: sliders show live
  values (donor caption + deferred-Destroy rebind bug), dependency layer folds children under
  parents (VROptionsTab.8), SyncPeerFades → Avatar & Mehrspieler. ELEVEN always-on dials removed
  (danger audit: PileViewer, ActivePile, Master, FlatScreen+AutoShow, UseBars, DoomPicker,
  DistributePanel, CatchAllModals, MenuPopupFloat, ManualScreenChord); three borderline
  questions queued for the user (ForceMouseMode, ClickLatch, the two map-fix diagnostics).
  HELD FIGURE: auto-release is PER-FIGURE (own bar/named wait/own turn/own animator leaves
  idle — Choreographer.IdleStates vocabulary, same predicate as the grab gate), global flows no
  longer end a hold, and every forced release takes the normal 0.28 s glide (instant-when-busy
  branch removed; authoritative-move release stays instant). Deadlock-safety argument in
  FigureBusy docs; watchdog stays.
- **ModBuild 122** — the border saga CLOSED (user on 121: "Großer Erfolg!"). Bottom fix: the
  contour derived from the v5 footprint (punched pixels ∩ frame-era CardOutline bands) — body
  stopped higher than the stock art draws. Capture now stamps the STOCK art's own alpha only
  (shadow trim / dark-border peel / OUTLINE CLIP deleted); CacheVersion 5→6 relearn; body and
  face coincide by construction. THE GREAT CLEANUP: net −7,200 LOC — deleted CardShapeMask (incl.
  Net call sites), CardDissolveFloor+dial, CardShaderProbe, CardBandPainter, CardBandPixelCapture,
  CardFaceCrop, CardOutline, punch/crop factories in CardFaceMipBake, FramePunch sweep + band
  diagnostics, cutout material bake, inert [Cards] GrabButton (+ dead ProximityGrabber branch +
  menu rows). KEPT: CardContour+AttachBody, stock-alpha capture v6, mip bake, emission floor,
  umber EdgeColor, SlotSeatLiner+mirror, FaceBlackout (stock-path). Tab captions wrap (explicit
  '&' breaks DE/EN; Erweitert chooser same). Saga history: build notes 105-121 + git. MP test of
  the shaped bodies still outstanding.
- **ModBuild 121** — border attempt 17: THE USER'S OWN DESIGN. His ruling: face must render
  completely correct again; the MESH must be punched out to the surface outline. (His "120" log
  was really a 119 run — the 120 deciders never executed; geometry moots the shader question.)
  ORDER A: punched/cropped sprite serving retired at the mint (PunchServingEnabled=false,
  CardFaceCrop.Enabled=false, restores via existing contracts) — stock art everywhere incl. the
  designed printed frame; the broken bottom dies with its cause; FramePunch sweep = outline
  derivation only; capture's OUTLINE CLIP now load-bearing. ORDER B: CardContour (marching
  squares 0.5 iso → closed-loop DP ≤120 verts → ear-clip front / mirrored back / extruded rim,
  bounds pinned to full box) + CardMesh.AttachBody serving shaped meshes to every registered
  body on contour-learn (rounded until then; warm cache = shaped from first draw); cutout
  baking retired (CutoutMaterialsEnabled=false); metrics consumers verified box-only. ALL SIX
  mirrors adopted AttachBody same build (RemoteHandFan/ItemFan/BrowserFan/CardFx/Avatar +
  AvatarMirror; two submeshes → back material twice; mirrors never destroy the shared cached
  meshes). If a band still shows on a seated tray card it can only be the deliberate LINER wood.
- **ModBuild 120** — border attempt 16: THE DIFFERENTIAL (instrumentation only, deliberately).
  119's partial success proved the painter chain (band = slab umber+light, opaque). The paradox:
  cutout texture alpha provably transparent in the bands + material state provably correct, yet
  the band renders. Code audit exonerates all local writers (emission floor touches only
  emission; _Cutoff 0.5; ConfigureCutout authoritative last on every path) → chief suspect: the
  game build's Standard shader may lack the _ALPHATEST_ON variant (EnableKeyword is a request;
  stripped variant never clips → slab draws its full envelope in EdgeColor = the measured band).
  Shipped deciders: CARD BAND DIFF (re-render with exactly one candidate suppressed per pass —
  Backing / edge submesh / liner / face canvas — per-strip verdict names the painter), CARD BAND
  TEX-VS-RENDER (live GPU texture alpha via Blit readback beside at-capture material state),
  CUTOUT CLIP PROBE (live edge material, UVs pinned to a verified alpha-0 texel → "clip
  EXECUTES" / "DOES NOT EXECUTE (variant stripped)"). If DOES NOT EXECUTE: fix lane = bundled
  clip-capable shader (BoardLit + clip(), bundle rebuild with /home/claw/unity-2021.3.5) or true
  mesh trimming to the outline. NO other changes in 120.
- **ModBuild 119** — border attempt 15: THE LIGHTING CONVICTION. Fourteen albedo/texture rounds
  were invisible because the card slab renders through stock 'Standard' in DARK scenes — albedo
  × ~0 light = black regardless of what we painted (karten4 band (4,4,3) ≈ 16 % of even the old
  EdgeColor's lit value). BoardLit (the board's own ambient-floored shader) provably cannot clip
  (bundle source: alpha hard-coded 1.0, no clip()) → fix is an EMISSION FLOOR
  (CardMesh.ApplyEmissionFloor: _EmissionMap = own albedo, tint × 1.0) on the shared card-body
  pairs + baked cutout textures; Net mirrors borrow the same instances → peers fixed free. Same
  floor on every Standard near-card surface (fallback board BoardLit-first, ItemsPile slab,
  PileViewer slabs, fallback caps). The 118 capture was BLIND (mask copied from game
  ScenarioCamera which excludes mod layer 27) → now HeadCamera mask ∪ card-subtree layers,
  centre-canary retry ~0, honest INSTRUMENT FAILURE verdict, and a mid-height RLE SCAN line
  (width + face-x + RGB per run) in every CARD BAND PIXELS log. Known caveat: a stripped
  _EMISSION shader variant would silently no-op — the scan line is the arbiter next run.
- **ModBuild 118** — border attempt 14 (two convictions + the pixel instrument), mirror ghost
  hands, trigger-only cards. THE BORDER: 117's liner was RIGHT in kind, WRONG in units — sized
  off the BASE card box while the seated card renders at the live SlotOverlayScale dial (~1.7 on
  his rig): ~1.10× coverage vs a ~1.30–1.40× well → "unverändert". Now seated-card ×
  SlotLinerSeatRatio (1.45) from the same dial, z from the tuning chain, on BOTH wire sides
  (RemoteBoardFurniture takes field 171 into the product; SlotLinerScale const deleted). Second
  conviction: the thin NEUTRAL near-black fan ring is CardMesh.EdgeColor (0.10,0.09,0.08) baked
  into every silhouette-surviving Edge texel → retinted warm umber. CardBandPixelCapture (new)
  renders the real card in situ at latch/re-arm and classifies band-strip means against known
  signatures — the remaining maroon PRINT band (91/264 punched-header + 27–28/36 unpunched
  action-default probes in the 117 log) gets convicted per placement in the next log. MIRROR:
  AvatarMirror read legacy single-side HandGhosts.LocalSide → now LocalLeft/Right per side
  (peers were already correct). TRIGGER-ONLY: grip proximity fallback removed for cards+figures
  (named throttled refusal); [Cards] GrabButton now INERT — retirement candidate next purge.
- **ModBuild 117** — the border SOLVED (attempt 13), the dead-dial purge, the menu restructure.
  THE BORDER: 116's CARD BAND PAINTER acquitted every card layer on the user's own rig while his
  screenshot showed the band — so the reach was wrong. Convicted: **the bundled tray's authored
  AO-dark RECESS FLOOR**, an ancestor mesh ~1.2x the card no card-root sweep could see; pixel
  proof warm-vs-cool (band r−b +7..+21 = tray wood; card layers −11..−18). Punch/crop/silhouette
  worked for rounds — every erased pixel just rendered recess-black, incl. the bottom "kaputt"
  (kept bright ornaments floating on black). The 116 log's 132 % slab spans were a world-AABB
  measurement artifact (tilted cards); the 111 "0.94 coincide" finding stands. Fix =
  **SlotSeatLiner** (opaque rounded CardMesh slab 1.78x card box, keycap grain wood, per recess;
  mirrored 1:1 on peer boards). Diagnostic upgraded: local-tight rects, subtree sweep with
  backdrop verdicts, throttled re-arm — a hand-fan residue would be NAMED in the next log.
  DEAD-DIAL PURGE: 48 keys removed (per-key re-verified; kept ScrollWithStickOnly — live reader
  UguiPointer.cs:505, the audit was wrong there — Experimental3DMap, WorldTilt family);
  ScreenLeftMirrorFallback description fixed (it gates the black-map probe). MENU RESTRUCTURE
  (all 20 integrator recommendations ruled in by the user): everyday tabs Komfort / Grafik /
  Brett & Karten / Tafeln / Avatar & Mehrspieler / Erweitert (ex-Debug); ~30 promotions;
  hand-built trees for the two oversized topics (VROptionsTab.7.TopicTrees.cs); GroupWordLabel
  localization; [Cards] CardSoundsEnabled master switch. Audit basis in `.planning/menu-audit/`.
  Wire tests now 1509.
- **ModBuild 116** — three lanes. BORDER ATTEMPT TWELVE: the 115 probe EXONERATED the card shader
  (verdict (a): alpha honored at rest) — eleven rounds edited layers that could not be painting the
  band. Convicted in source instead: **`FullAbilityCard.unfocusedMask`** — ShowCard loads the SAME
  background sprite into headerImage AND this full-card dimming copy at its own slightly different
  rect (the 115 two-placement warning), toggled by SetUnfocused, ACTIVE IN MULTIPLAYER for every
  presented character not under local control — and served the header-rect punch copy it erased
  ~2 % into the card bottom (the 115 "unterer Teil kaputt" regression). Fix: the mask is a sixth
  named plate (punched + cropped against ITS OWN rect); per-placement punch copies keyed
  (source, mapping); dissolve floor default 0 (probe: cures nothing; _Dissolve_VerticalGradient
  discards bottom-first — the other bottom suspect); NEW CardBandPainter latches a complete painter
  inventory (Graphics + Renderers + rest-output GPU probe per custom shader, incl. the never-probed
  120 %-of-face CardEffects fgFx 'UIFX_Overlay'). If the band survives 116: **read CARD BAND
  PAINTER — it names the painter.** STRETCH BOUNDS + INFO TOGGLE (items 2+3): total-based
  Min/Max with grab-time clamp, [FigureGrab] StretchLimits off-switch, [FigureGrab] HeldFigureInfo
  gating StatPanelSurface.ShowHeldFigure at the registration seam; all in VR settings, DE/EN.
  ALSO RUNNING: the VR options menu overhaul audit — five parallel read-only auditors writing
  `.planning/menu-audit/0*.md` (dead dials, NORMAL vs POWER audience, structure proposal); the
  synthesis + user decision list is the next deliverable after this round's report.
- **ModBuild 115** — two lanes. BORDER ATTEMPT ELEVEN, the first SHADER-side one: 114's log closed
  the texture-side case — the rect crop provably applied (294x450→291x436) and the user still saw
  an identical band, while CARD SHADER IDENTITY named the painter ('GUI/AbilityCard_Shd', a per-
  image material clone, no readable blend state). The user's own report is the tell: **the band
  turns transparent during the game's dissolve animation**, i.e. a clip of the shape
  clip(f(tex.a) − _Dissolve·..) that discards nothing at rest 0 and paints alpha-0 pixels opaque
  black — one mechanism explaining all nine texture rounds at once. Shipped: `CardShaderProbe`
  (once per session, renders the punch's exact alpha-0 pixels through a CLONE of the live material
  at _Dissolve 0 and 0.004 into an RT, reads pixels back, logs a CARD SHADER PROBE verdict:
  (a) alpha honored → another painter exists, (b) epsilon discards → floor proven by that log,
  (c) neither → shader replacement is the only road, (d) unproven; plus the full shader property
  table) and `CardDissolveFloor` (the fix: [Cards] DissolveFloorFraction = 0.004 held at rest,
  never lowering an animated value, on every card-FX material by SIGNATURE match _Dissolve +
  _PosAndBounds — ability faces via CardArtWatch, item cards via ItemsPile.ItemChip, remote fronts
  free because RemoteCardArt already rides CardArtWatch; full restore on yield/recycle). STRETCH
  FIXES from the 114 test: capture was a fixed 80 mm sphere around the mini's CENTRE, so a
  stretched mini sat outside its own zone and could not be shrunk — capture is now min
  closest-point over the held visual's renderer AABBs (cached per hold, 0.5 m sanity clamp,
  centre fallback), scaling with the figure by construction; d0/d stay centre-based on purpose.
  Plus the requested zone-entry haptic (HapticPreset.HoverTick, edge-triggered). If the border
  survives 115: **read CARD SHADER PROBE first** — verdict (a) means hunt the other painter,
  (c) means build a replacement shader; either way the guessing era is over. Wire tests 1529.
- **ModBuild 114** — two lanes. BORDER ATTEMPT TEN: heuristics retired after losing twice on the
  same two sprites — the five face layers resolve BY IDENTITY from CardEffects' serialized fields;
  a CARD SHADER IDENTITY line settles whether the custom card shader ignores alpha (the only
  theory left that explains nine invisible texture rounds at once); and the shader-agnostic rect
  crop removes the band's geometry entirely on local ability faces. HELD-FIGURE STRETCH: second
  hand + trigger near a held mini scales it ratio-based (s0 x d/d0), this-hold-only, wire record
  30 (first new record since the reservation note; additive, neutral-omitted, fail-closed decode),
  +40 wire vectors -> 1526. If the border survives 114: read CARD SHADER IDENTITY first — its
  verdict clause dictates whether the next move is shader-side or a sixth-layer hunt.
- **ModBuild 113** — the border, attempt NINE: the punch region is GEOMETRY now, not luma. 112's
  BAND INVENTORY named the culprits (punched background still 102/264 band probes — the luma-BFS is
  interrupted by decoration; the two ACTION-HALF plates 55/72, never punched). Screenshot profiling
  found the structural failure of every luma approach: content luma 55-65 vs threshold 48 vs frame
  <40 — a knife edge — while the gold TRIM sits at ~213. The card's true outline is now derived from
  the bright-trim contour once per kind, validated against the screenshot's band widths, and erased
  on EVERY layer mapping outside it. **The decisive find: the action halves fell at Image.Type.Simple
  — they are 9-SLICED Button plates, and the punch now replicates GenerateSlicedSprite's mapping.**
  First time in nine attempts they are touched. CacheVersion 4→5 (fourth load-bearing bump). If it
  fails: "CARD OUTLINE refused" = behaviour is exactly 112; otherwise BAND INVENTORY prints derived
  bands beside the culprit — disagreement indicts the derivation, agreement indicts an escaped
  consumer. Known pre-existing residual: hovered halves render Image.overrideSprite (§5e), uncovered
  by any bake.
- **ModBuild 112** — the border, attempt EIGHT: round 7's falsifier fired (ART RECT 100 % x
  100 % — no letterbox on his art), leaving the series' one POSITIVE measurement standing: the
  PEEL hit its depth cap twice (11/11, then 18/18 texels, mean luma 22). The band is a PRINTED
  near-black frame in the art's own opaque pixels — which is why mesh work was invisible (behind
  opaque art) and the verified stencil clip changed nothing (an opacity-captured outline INCLUDES
  the frame). Fix: pixel surgery on the mod-owned 'VR-mip' sprite copies the bake already swaps in
  — boundary-seeded BFS erosion with LEARNED depth (two guessed caps were both too small), alpha 0
  on the frame, gates logged. Punch runs BEFORE capture so the footprint is stamped from punched
  pixels — mesh and face agree by construction. CacheVersion 3→4 (third load-bearing bump). Round
  4 had declined exactly this as risky-against-speculation; the peel's measurements re-weighed it.
  **If it fails: grep CARD FRAME BAND INVENTORY — the culprit graphic is named, not guessed.**
- **ModBuild 111** — the black card border, attempt SEVEN, and **the lesson is about method, not
  about cards**. Six rounds reasoned about art that could not be read offline; the user supplied a
  SCREENSHOT and pixel measurement settled it in one pass. The band is TOP/BOTTOM (4.76 % / 6.6 %
  of the card height), not left/right (0.8 % / 2.4 %); its corner is a clean arc equal to
  `CardMesh.CornerRadius` and its colour a flat (4,4,3) identical across cards at different
  orientations — so it is the mod's MESH. Cause: slab and face rect coincide (round 5's "0.94
  refutation" stands), but the ability ART is poker-shaped and `Image.preserveAspect` letterboxes it
  to 90.54 % of the face rect's height, leaving 4.73 % dead top and bottom. Measured 4.76 %. The
  capture normalised by each candidate's LAYOUT rect, so the mask was stretched ~10 % vertically and
  declared the body "card" exactly where the art draws nothing — every one of six clips was correct
  and aimed 10 % away from the edge it sought. Fixed by replicating uGUI's `PreserveSpriteAspectRatio`
  (pivot re-anchoring included) from live values; nothing about size or proportions changes, which is
  what the user required ("Ich will es also so wie es jetzt ist … nur eben ohne die schwarzen
  Ränder"). `CacheVersion` 2→3, because a stale mask silently ships the previous round — that has
  already happened once. **Two integrator hypotheses were wrong in this series (sprite-less dark
  quads, and the aspect's axis); both were plausible, checkable and false. Measure before deducing.**
- **ModBuild 110** — attempt FIVE at the black card border, and the first that clips the layer the
  black is on. **The generalisable lesson: four attempts all acted on the card BODY, and the body was
  never what bounds the visible card.** The 109 log refuted the standing hypothesis with its own new
  diagnostic (exactly ONE sprite-less quad on a face, and it is WHITE) and reported a captured
  outline with real corner cuts (bbox 95.5 % of the face, 0.928 fill) — so if the clipped body bound
  the card, the cards would have stopped looking rectangular in attempt 2. They did not. Therefore
  the FACE paints over the body's whole footprint. `Cards/CardShapeMask.cs` now stencil-clips the
  adopted face itself. The reason that was not trivial: `MaskUtilities.FindRootSortOverrideCanvas`
  stops at `FullAbilityCard`'s own Canvas, whose sorting the game TOGGLES — so a mask above it can
  render a plain rectangle while every log line claims success, exactly how attempt 1 failed. It
  therefore neutralises `overrideSorting` on every canvas inside the face (recording each original,
  because a toggle can flip one after install) and VERIFIES itself by making the same two calls
  `MaskableGraphic` makes, refusing and removing itself if the stencil would not resolve.
  Integrator-verified against the local uGUI package: `Mask.IsRaycastLocationValid` filters by RECT
  only, so input is untouched, and `StencilMaterial` really does set `useAlphaClip`.
  A 0.94 coordinate-mismatch hypothesis (the integrator's) was raised and REFUTED by arithmetic —
  recorded as do-not-re-test.
- **ModBuild 109** — the card SILHOUETTE round. **The black card border took four attempts, and
  every wrong turn was a layer error**, which is why it is worth reading in full.
  Attempt 1 shipped an alpha clip that never executed (`isActiveAndEnabled` against cards adopting
  art under an inactive pool root; the rejecting branch logged nothing). Attempt 2 made it execute
  — and the border did not move. Attempt 3 found why, from the user's own sentence: during a
  character switch the loader takes the art down, nothing paints, and the border VANISHES — so the
  mesh behind it is already clipped, and **the remaining black is painted by the adopted uGUI FACE,
  not by the card body**. A clipped mesh cannot un-clip itself. The suspects are sprite-less
  `Image`s (22 enabled ones on a single face), which uGUI draws as plain colour quads — rectangles,
  incapable of carrying the card's shape. Attempt 4 mutes them in the same frame the art arrives,
  on the seam `CardFaceMipBake` already uses to swap sprites before a card's first drawn pixel.
  User then narrowed it: every placement, **no visible transition**, item cards, and every card a
  PEER draws. The transition is answered by not learning the shape in that session — the footprint
  is persisted to `BepInEx/config` and re-applied from inside the body's own material factory,
  before any renderer that will draw it has a material. Two uncovered local paths and two missed
  mirror sites were found; the peer FRONT was the bigger hole and needed no wire (the pump passes
  the CANVAS, whose GameObject carries no card component — the clone is its child).
  Also in 109: held figures keep the size they had at the grab (`TickHeldScale` pinned WORLD size,
  and the diorama zoom IS the rig scale the anchor hangs under — log: `boardWorld=1` at anchorScale
  41.368 vs 10.149); the slot a card lands in is ranked by the CARD, not the hand (an eligibility
  test had been used as a ranking key); and the boot spinner is the GAME's symbol (the previous
  round's "it is serialized inside the scene being loaded" was true of that OBJECT and wrong as a
  conclusion — a second copy lives in the Intro scene, switched on a frame before our arming edge).
- **ModBuild 108** — six reports, one round. **The generalisable lesson: three of the six were
  defects in a layer nobody had looked at, not in the layer the symptom pointed at.**
  (1) **THE DEADLOCK** (critical, user-flagged): lifting a figure mid-attack killed the turn
  machine permanently. No exception — a wait that never completes, hence nothing logged.
  `ActorBars` hides the figure's panel HOST while its mini is held; `AttackModBar`'s flow is a
  coroutine started ON that controller; Unity kills a coroutine when its GameObject is
  deactivated; `FinalizeFlow` (sole writer of `IsFlowActive = false`) is its last statement, so
  the flag latches; and `WaitingForPlayerIdle` waits on it with NO timeout. The find that shaped
  the fix: the game waits on the attack's **TARGET**, not the acting figure — a guard on "whose
  turn is it" would have missed it. Prevented structurally (`FigureBusy`), mechanism removed
  (`ActorBars` no longer hides a live-flow bar), watchdog behind both.
  (2) **MR plate flicker** on the pile captions — 107's per-registrant fix does not scale, because
  the order writer is usually not the registrant. `MrBacking` re-syncs plates phase-blind now.
  (3) **Pile symbols** — a second defect at the same place: `PileViewer.SetVisible` hit by the
  pooled-hand re-bind window. Hide debounced 2 frames. Closed rather than distinguished on a run.
  (4) **Card X spacing** — `HalfSelection` used the spread-free slot-home accessor (its only
  caller); a docked pair sat 5.5 mm wider, a 42 % change in the gap the eye judges. Accessor
  retired; the peer mirror fixed by converting through the board root (local slots carry
  `SlotScale`, remote prefab anchors do not).
  (5) **No turning while scrolling** — the axes never contended, the thumb did. Releases on the
  turn axis returning to rest, not on the scroll ending.
  (6) **Card silhouette** — the alpha clip had shipped in `6d7f1bb` and had NEVER RUN
  (`isActiveAndEnabled` against cards adopting art under an inactive pool root, rejecting branch
  logged nothing, retry budget burnt blind).
  Startup freeze: measured, not assumed — 21.3 ms of 2667 ms is the mod, 62 % is game-side YML
  parsing on the main thread. Spinner now covers the window; the mod's own 981 ms bundle inflate
  is prewarmed. See `.planning/startup-freeze.md`, incl. the bundle rebuild the user declined.
- **ModBuild 107** — two defects ModBuild 106 had already claimed to fix, both of which had been
  fixed ONE LAYER AWAY from where they live. That generalises, so it is the entry's headline.
  (1) The figure highlight is not raised by `FigureGrabDriver` at all — `ProximityGrabber` raises
  it, on the nearest grabbable inside its CARD-sized 13 cm palm reach. The driver's election is
  only a VETO, and the veto was distance-gated (`suppressed = inReach && !winner`), so a figure
  outside the sphere was actively written back to ALLOWED; the grabber reads those flags a frame
  late, so the figure CROSSING IN was momentarily the only allowed candidate and won "nearest" by
  default. One frame of amber at the far edge of the reach, walking figure to figure — "verschiedene
  Figuren", and "zu weit weg" because the leak fired at 130 mm, not at the 40 mm pick radius. Log:
  137 highlight lines against 9 elections. The 106 hysteresis/dwell stabilise the ELECTION and were
  never in this path. Veto is unconditional now and fails closed.
  (2) The quest text's one-frame blank was in the RENDER path; 106 had hardened the POLL path,
  which cannot produce a one-frame anything (the goal re-derives every 0.5 s). Two render paths
  were open and BOTH were closed rather than distinguished by a diagnostic on the next run: the
  label's draw order was written in Update *and* LateUpdate while its opaque MR plate copies it in
  Update only (plate paints over the glyphs in the frame between), and a single `!activeInHierarchy`
  frame on the tray mount takes the whole host panel down. Rank is Update-only now, guarded by a
  once-per-session assertion; the mount HIDE is debounced 2 frames, the SHOW is untouched.
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

### Never invite a test before the round is pushed (user ruling, 2026-08-11, standing)

> "Ich hatte aber gepulled und kompiliert. Sorge dafür das nach einer Runde wenn ich zum tsten
> aufgefordert bin die änderungen auch immer im master gepusht sind!"

He pulls and compiles from `main`; an open worker lane is invisible to him. So:

- **Do not name log strings to grep, expected visual changes, or things to watch for** for any
  item whose lane is not yet merged AND pushed. Describing what to look for reads as an
  invitation to test even without the words "please test" — that is exactly how this was broken.
- **End every round-closing report with two explicit lines:** the commit to test (verified with
  `git log --oneline -1 origin/main` *after* pushing), and which reported items are NOT in it.
- While lanes are still running, either say plainly "noch nicht testen" or scope the test to the
  merged items and name them.

Same economics as the test-confidence rule: a run whose outcome is already known costs him a
full game launch plus headset time.

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
