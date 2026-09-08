# Invariant registry — the 2026-08 split targets

> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** Corrections carry
> **[verified 2026-09-08]**. Two of this file's statements had gone stale and both were the kind a
> successor acts on: the config-key census number in §8, and §9's claim that `CardFan` and
> `CardsGameApi` had never been read. Everything else was re-read and left alone.
>
> Companion to `PLAN-2026-08.md` phase 4 and `CHARTER.md`. The four 2026-07 registries
> (`INVARIANTS-Cards.md`, `-Hands-Board-Core.md`, `-Net-Rig.md`, `-WorldUI.md`) describe the mod as
> it stood at 182 files. This one covers the types phase 4 will actually cut apart, and nothing
> else.
>
> **Symbol names only, never line numbers** — the files are being edited concurrently.
>
> **An entry is a veto, not a note.** If a proposed change makes the `Breaks if:` line true, the
> change is Tier 3 and out of scope.
>
> Confidence: **high** = a code comment or commit message states the cause explicitly ·
> **medium** = inferred from the diff or surrounding code · **low** = guess.

---

## 0. Why this registry is narrower than the plan said, and why that is right

`PLAN-2026-08.md` §2 phase 2 says "invariant registry for the 311 uncovered files". Writing that
was a mistake in the plan and this is the correction.

The registry exists for exactly one reason: so that the person about to touch a symbol reads the
bug that put it there **before** they touch it. It therefore only has to cover **what phases 3–5
will actually touch**. A registry entry for a file nobody edits protects nothing, goes stale in
two builds like every audit does, and costs the time that should have gone into the files that
are about to be cut.

There is a second reason the wide scope was wrong, and it is the stronger one: **the mod already
documents itself at the site, densely.** A sweep of the tree finds 4 373 `ModBuild NNN`
references, 1 762 "deliberately/on purpose" statements and 1 370 quotes from user reports, almost
all of them in comments sitting on the symbol they describe. Copying those into another document
produces a second copy that can disagree with the first. What a reader genuinely cannot get by
reading one file is:

1. **cross-file couplings** — "X must hold while Y runs", where X and Y are in different files;
2. **rejected alternatives** — what was tried, shipped and reverted, so it is not re-proposed;
3. **what a FILE SPLIT specifically breaks** — which is the operation phase 4 performs, and which
   no in-source comment is written to anticipate.

This document is those three, for the split targets. Everything else stays where it is.

**Not covered here, deliberately:** `PresenceState` (§7), the already-split god types the plan
declines to re-cut, and the 311 files no phase touches.

---

## 1. `EnvSound` / `EnvSoundBank` — Core/Sound

The largest single-part type in the mod (4 973 + 3 896 lines). It is already cut into `// ---- `
sections, so the split is mechanical — which is precisely why the three rules below matter, since
each of them survives a careless split looking untouched.

### 1.1 Bed construction order is a wire into the audio, not a formality

- **Where:** the bed factory in `EnvSound` that ends `v.Source.time = clip.length * (0.3819660f * Beds.Count % 1f)`, and every caller that adds a bed.
- **Rule:** each bed starts playing at a **different** point in its buffer, and the offset is derived from `Beds.Count` **at the moment that bed is constructed**.
- **Why:** the flame, the draught and the leaves deliberately share ONE noise clip, and so have the three fire sites since ModBuild 152. Started at the same instant they play the identical sample stream — perfectly correlated, summing coherently to about **+10 dB instead of the +5 dB of independent noise**, and the three collapse into one audible source coming from three places at once. `0.3819660` is not a magic number: it is 1 − 1/φ = 1/φ² = 0.381966…, an irrational fraction of the clip, which decorrelates them completely for one float write.
- **Breaks if:** a split reorders the bed-construction calls, or "tidies" the offset to a constant, an index the caller passes, or `Beds.Count` read after the loop. Any of those either re-correlates the buffers or silently changes every existing offset. **The count is read at construction time on purpose.**
- **Confidence:** high — the comment states the mechanism and the decibel figures.

### 1.2 `MaxEmitterGain` has exactly one exception and it is a written permission

- **Where:** `EnvSound.MaxEmitterGain`, `EnvSound.ShelfImpactGain`.
- **Rule:** no emitter exceeds `MaxEmitterGain` (0.16) except the shelf impact, which carries the user's verbatim permission at ModBuild 149 ("ich gebe dir hierbei eine Ausnahmegenehmigung … einen lauten Knall Sound einzubauen in dem Moment in das Regal den Boden berührt").
- **Breaks if:** a de-duplication pass merges the two gain constants into one, or moves `ShelfImpactGain` away from the comment that carries the permission. The exception is scoped to **one bang at one instant**; a shared constant silently extends it to every cue.
- **Confidence:** high — the permission is quoted at the constant.

### 1.3 The wind-leak assertion tests the MODULATOR, never the source volume

- **Where:** `EnvSound.TickBeds`, the `IsWindClip && !_windLeakLogged && air <= 0f && _windGate <= 0f && m > WindRestFloor + 1e-4f` branch; `EnvSound.WindRestFloor`.
- **Rule:** the check compares the **modulator** `m` against `WindRestFloor`, not `v.Source.volume`, and fires **once per build** rather than per frame.
- **Why:** the source volume walks over about a tenth of a second, so testing it needs a real tolerance — "and a tolerance is a place for the next bug to live". `WindBed` returns the constant itself on that path, so the comparison is against a value this file wrote rather than an approximation. The epsilon exists only because the constant is a float literal.
- **This invariant has already gone stale once.** The bound moved at ModBuild 223 (the user narrowed the ruling: a very quiet draught is now wanted at rest) and the check did **not** move with it — it still read `m > 0f`. That is the failure mode to expect again.
- **Breaks if:** "simplifying" the condition to read `v.Source.volume`, dropping the epsilon as an unexplained fudge, or removing the `_windLeakLogged` latch to "see it every frame" — that is 90 lines a second in a log that has to stay readable.
- **Confidence:** high.

---

## 2. `WaterTerrainVR` — Core/Water

### 2.1 Material instances are OWNED, and the shared material is never written

- **Where:** `WaterTerrainVR.Driver`'s instance tracking and its explicit `Destroy` paths (renderer pruned, material replaced under us, config flip, uninstall, destroy), and the live-instance census.
- **Rule:** every material obtained from `Renderer.materials` is tracked and explicitly destroyed. `sharedMaterial` is **never** written.
- **Why:** an instance is a live `Object` Unity does not reliably collect when its renderer dies, and this content churns — **109 placement events in one logged session**. The census counts live instances precisely so a leak shows as a number that only climbs.
- **Breaks if:** a split separates the creation site from one of the destroy sites and the new file's author does not know the other four exist; or a "simplification" to `sharedMaterial` to avoid the instancing cost. The shared material is the game's.
- **Confidence:** high.

### 2.2 There is exactly one known foreign writer of these renderers

- **Where:** the per-frame reassert path in `WaterTerrainVR.Driver`.
- **Rule:** the reassert exists because `MaterialLoaderData.CheckAllMaterialLoaded` swaps `sharedMaterials` on an Addressables callback — "it is also the only unconditional `Renderer.enabled = true` in the whole game assembly". A swap drops our instance on the floor.
- **Breaks if:** the reassert is removed as redundant, or throttled to a cadence, on the reasoning that "nothing else touches these renderers". Something does, asynchronously, and it is named.
- **Confidence:** high.

### 2.3 The discovery sweep must never become per-frame

- **Where:** `WaterTerrainVR.Driver`'s discovery cadence constant.
- **Rule:** slow on purpose.
- **Why:** the mod has already paid for this class three times in one round — see `[[findobjectsoftype-is-the-default-suspect]]`; one `FindObjectOfType` per frame once owned 12.6 ms of an 11.11 ms budget.
- **Breaks if:** a split moves discovery into the tick body "so it is all in one place".
- **Confidence:** high.

---

## 3. `StoryComposite` — WorldUI/Composites

### 3.1 The picture is MEASURED, never moved

- **Where:** `StoryComposite._picture`, `_dockSize`, and the park path.
- **Rule:** the quest picture stays exactly where `UILoadoutQuestWindow` put it inside the host. The composite measures it and places the **dialog** relative to it.
- **Why:** ModBuild 236. Moving the picture is the tempting inverse and it is what shipped broken twice — the two ways being "0x0 px" and "NOT LOADED YET", which is why both appear in the same log line.
- **Breaks if:** a split gives the picture its own placement step, or a "symmetry" pass that moves both halves.
- **Confidence:** high.

### 3.2 The composite's Unpark must run before the modal release loop

- **Where:** `StoryComposite.Tick` called from `ModalFallback.TickWindowLiveness`.
- **Rule:** cross-file, per-frame, and **now locked** — `FRAME-ORDER ModalFallback.Tick`, phase 1.4.
- **Why:** the release loop calls `CanvasConversion.Release`, which destroys the host the story window was re-parented under; a subtree still parked under a destroyed host cannot be given back.
- **Breaks if:** moving the `StoryComposite.Tick` call anywhere later in the modal pipeline. The lock now refuses this, which is why it was written.
- **Confidence:** high.

---

## 4. `MapTableLegs` — WorldUI/MapRoom

### 4.1 Copy how the tabletop is LIT; do not copy how it LIGHTS OTHERS

- **Where:** the prop-renderer setup in `MapTableLegs`; `receiveShadows`, `lightProbeUsage`, `reflectionProbeUsage` copied from the tabletop, `shadowCastingMode` deliberately left `Off`.
- **Rule:** **inputs** to lighting are copied, **outputs** are not.
- **Why:** two objects with one material under one light set still come out at different brightnesses if their probe usage differs — those decide how much light the surface collects. `shadowCastingMode` is an output: it decides what this prop does to the *game's* renderers, and this class's whole standing claim is that it changes nothing outside itself. Leaving it off also keeps the cost claim true (no second pass over the mesh).
- **Breaks if:** a "copy the renderer settings" helper extracted during a split that copies all of them for symmetry. That single extra field breaks the class's standing claim and adds a shadow pass.
- **Confidence:** high.

### 4.2 No collider, ever

- **Where:** the prop construction in `MapTableLegs`; stated twice in the file.
- **Rule:** the furniture carries no collider — "the laser's pick path must not start finding furniture".
- **Breaks if:** a shared builder extracted during the split that adds one because another caller wanted it.
- **Confidence:** high.

---

## 5. `RemoteBoardFurniture` — Net/Remote

### 5.1 The skip-seat extrapolation is added OUTSIDE the clamp

- **Where:** the skip-cap seat computation; `Cards.BoardAnchors.ClampSeatPose(...) + skipExtra`.
- **Rule:** `skipExtra` is added **after** the clamp, not inside it.
- **Why:** it is not a tuned nudge, it **is the seat** — the clamp bounds a cap inside a recess this board does not have. A board that supplies no `ButtonSeat3` also carries no `SeatExtent3`, so `SeatMinHalf` is null there and the clamp is inert anyway.
- **Breaks if:** a tidy-up that folds the offset into the clamp call "so every seat goes through one path". On a board that DOES have the recess, that clamps away the seat.
- **Confidence:** high.

### 5.2 Button colours ride as individual FLOAT channels, not as `Color`

- **Where:** the `[ButtonColors]` mirror fields in `RemoteBoardFurniture`.
- **Rule:** held as separate float channels on purpose — that is the form the wire carries.
- **Breaks if:** a "cleanup" that groups them into a `Color`, which then has to be decomposed at the only place it is used and re-introduces the packing question the wire already answered.
- **Confidence:** medium — the comment states the form is deliberate; the wire reason is inferred from the field ids.

---

## 6. `NetAvatarDriver` — Net/Avatar

### 6.1 `PileKindWireOrderGuard` is not dead code and not a curiosity

- **Where:** `NetAvatarDriver.PileKindWireOrderGuard`.
- **Rule:** **do not delete it, do not "fix" it, do not move it out of this file.**
- **What it is:** `private const int PileKindWireOrderGuard = 1 / (…comparisons… ? 1 : 0);` — a deliberate compile-time division by zero. `Cards.PileKind`'s member ORDER is a wire constant: `TickExtrasSend` casts it straight onto the extras packet, and every peer decodes it against `NetProtocol.PileBrowseKind*`. **Nothing in the compiler otherwise links `Cards/` to `Net/`.** A renumber or mid-list insertion over there corrupts every peer's browse fan with no error, no single-player symptom, and nothing wrong on the sender's own screen — a sender never parses its own packet.
- **Why it is in the registry:** it reads exactly like the kind of thing a hygiene pass deletes. It compiles to nothing, it is never referenced, and its expression looks like a mistake. It is the only mechanism that turns a silent multiplayer corruption into a build failure (`CS0020`).
- **Breaks if:** removing it as an unused private const; "simplifying" the expression; moving it to a file that does not see both `Cards.PileKind` and `NetProtocol`. Appending a FOURTH pile at the end is fine and the guard deliberately permits it.
- **Confidence:** high — the comment says all of this at the declaration.

---

## 7. `PresenceState` — Net · *and why it gets one paragraph*

The plan makes `PresenceState.Write` / `TryRead` the **first** phase-4 split, deliberately: at
nesting depth 7 they are the most tangled code in the mod, and they are also the only part of it
behind **151 307 byte-exact assertions**. A mistake there is caught by a test in eight seconds
rather than by the user's headset a week later.

That is why this section is short. The field semantics are documented per-field at the site
("a POSITION, never an identity"; which bits are reserved; which record ids stay free), the record
layout is documented as an ASCII map above the serializer, and the wire tests hold the rest. The
one thing worth stating that a splitter could still get wrong:

- **Where:** `PresenceSerializer.Write`, `PresenceSerializer.TryRead`.
- **Rule:** the two are a mirrored pair, and **a change made in lockstep is invisible to a round trip.** Extracting a record's write into a helper and its read into the matching helper is safe; changing what either writes is not, and the round-trip test will not see it.
- **Breaks if:** relying on "the wire tests passed" after editing both sides. `scripts/check-wire-coverage.py` and the byte-vector assertions — not the round trip — are what actually pin the format.
- **Confidence:** high — `refactor-guard.sh`'s own header states this blind spot.

---

## 8. The `Bind` monoliths — every module

- **Where:** `CardsConfig.Bind` (966 code lines), `WorldUIConfig.Bind` (448), `WallSegmentFade.Bind` (312), `PerfConfig.Bind` (292), `FigureGrabConfig.Bind` (221), `HandsConfig.Bind` (208), and the other 18.
- **Rule:** **the order of `Bind` calls is the order of sections and keys in the player's generated `.cfg` file.** It is not an implementation detail.
- **Breaks if:** splitting per section in any order other than the existing one, or sorting the calls "for readability". The player's tuned file is rewritten, and his hand-tuned values are what every recent hardware round was measured against — see `[[tuned-cfg-drops-are-current]]`.
- **How to verify a split:** guard empty **and** `check-surface.py` config-key census unchanged **and** a before/after diff of a freshly generated `.cfg`. The first two alone do not see order.
  **[verified 2026-09-08]** The number in the original sentence was **385**; the census reports
  **625 config keys** (plus 150 Harmony patches and 4 698 log tokens) at `49ceab21`. **Do not
  hard-code it.** The verification is *unchanged across your commit*, which is what
  `check-surface.py diff` asserts against the stored `surface.json` — a literal in this document
  is stale the next time anyone binds a key, and quoting one turns a working check into a
  false alarm. `CardsConfig.Bind` is likewise no longer 966 lines; `CardsConfig.cs` alone is 2 172.
  Take the shape of the rule from here and every number from the tool.
- **Confidence:** high.

---

## 9. Still uncovered, and named so nobody assumes otherwise

~~`CardFan` and `CardsGameApi` are phase-4 split targets and have **no entry here yet**. Both are
densely documented at the site; neither has been read end-to-end for cross-file couplings. They
must be read before they are cut, and this section is the reminder that they were not.~~

**[verified 2026-09-08] THEY HAVE NOW BEEN READ, AND THEY WERE DELIBERATELY NOT SPLIT.** The
reminder above worked: the 2026-09 cards lane read both for structure and wrote the entries this
section demanded, **before** deciding. The entries live in
**`.planning/refactor-2026-09/REVIEW-cards.md` §3.1 (`CardsGameApi`) and §3.2 (`CardFan`)**, in
this registry's format. Read them there before cutting either file; they are not duplicated here
because a registry entry copied into two places goes stale in one of them.

**Neither file was split, and the reason is the finding.** Both are pinned by **file-scoped
gates**, and a split that moves a pinned symbol out of the scoped file does not fail loudly in
both cases:

- `CardsGameApi` — `scripts/check-mirrors.sh` PART 3 scopes the rules-engine-busy subset guard to
  **the file** `Cards/CardsGameApi.cs`. A file *in* scope that stops reading the trigger term is
  simply skipped, so moving `RulesEngineBusy` into a new part **passes the gate** and silently
  takes the expression out of coverage. **A split that silently narrows a gate is worse than a
  long file.** If it is ever split, the PART 3 scope entry changes in the same commit.
- `CardFan` — `check-mirrors.sh` pins seven constants by **file and name**
  (`GazeBiasDeadzoneDeg`, `GazeBiasReleaseDeg`, `GazeBiasFullDeg`, `GazeBiasMaxYawDeg`,
  `GazeBiasGain`, `GazeBiasSmoothing`, `ZStagger`) against `Net/Remote/RemoteHandFan.cs`. Here the
  gate **does** fail loudly on a move, so a split is safe — the pins simply have to move in the
  same commit.

Sizes at `49ceab21`, for scale: `Cards/CardsGameApi.cs` **4 280 lines**, `Cards/CardFan.cs`
**2 889** — both single files, both grown since the 2026-08 plan proposed cutting them
(`PLAN-2026-08.md` listed 3 348 and 2 805). **Growth is not by itself an argument for the split**;
the gate-scoping problem above is unchanged by it.
