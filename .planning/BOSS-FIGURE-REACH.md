# The boss dragon: a health bar through its face, and a figure that is only grabbable below it

Report (ModBuild 290, hardware, `Drachen.jpg` + `.planning/debug/second_logs/LogOutput.log`):

> a) Die Health-Bar von allen Drachen ist falsch … bei dem Boss-Drachen sind sie mitten in ihm
>    statt darüber wie bei den anderen Gegnern.
> b) Wenn ich den Drachen nehmen will kommt kein Overlay/Highlight angezeigt …
> c) Ich kann die Figur zwar in die Hand nehmen, muss es dann aber am unteren Bereich tun …

Three actors are involved and the log names them: `ElderDrakeID` (the boss in the middle),
`RendingDrakeEliteID` and `SpittingDrakeID` (the two small drakes).

---

## Summary of what the measurements say

| symptom | cause | confidence |
|---|---|---|
| (a) bar through the boss | `ComputeAnchorOffsetWU` returned its **vanilla fallback** — the bounds rule never produced an answer. **NOT the 6 wu ceiling.** | measured, triangulated from two independent sources; see below |
| (b) no highlight | **not a highlight defect.** The log shows the boss highlighting and being grabbed a dozen times. It is the same refusal as (c). | measured (log) |
| (c) only the lower half grabs | consistent with the game's single pick collider under-covering the boss — **unverified**, no instrument existed | code-reading + inference only |

(b) and (c) **share a cause**. (a) does **not** share it: it is a separate defect in a separate
subsystem that happens to produce a similar height. See "Do (a) and (c) share a height?" below.

---

## (a) The bar — the number, and why the 6 wu ceiling is refuted

The brief's leading hypothesis was the hard ceiling in

```csharp
return Mathf.Clamp(offset, 0.05f, Mathf.Min(fallback + height, 6f));
```

whose comment asserts "the tallest boss mini is ~5 wu". **That is not what happened here.**

### The measurement

Two independent sources, one from the log and one from the screenshot.

**From the log** — `FigureStretch.CaptureDistanceReal` printed the boss's own mesh bound while it
was held in the hand:

```
STRETCH capture bounds clamped on ElderDrakeID: renderer 'MO_ElderDrake_MESH' implies a figure
radius of 0.99 m real (> 0.6 m sanity ceiling at this hold's total size 1.198×)
```

with `[Size] ElderDrakeID boardWorld=1 heldWorld=1` (held at board world size) and
`PICK VOLUME: … (rig world scale 9.57)`. That "radius" is
`|root − bounds.center| + bounds.extents.magnitude`, measured from the actor ROOT, so in world
units it is `0.99 × 9.57 = 9.47 wu`. Writing the AABB as `y ∈ [−u, +v]` relative to the root,
`|centre.y| + |extents| ≥ max(u, v)`, therefore

> **the boss's mesh AABB reaches at most 9.47 wu above its base.**

**From the screenshot** — pixel-measured on the full-resolution image (3840×2160), all three
points on the same figure at essentially the same depth, so pixels per world unit is constant
along that vertical:

| feature | image row | px above the feet |
|---|---|---|
| feet / floor | 2075 | 0 |
| **the health bar** | 1660 | **415** |
| head crest (top of the skull) | ≈ 1377 | ≈ 698 |
| left wing tip (the highest drawn point) | 431 | 1644 |

`bar / wing-tip = 415 / 1644 = 0.2524`. The wing tip is inside the mesh AABB, so
`wing-tip ≤ 9.47 wu`, hence

> **the shipped anchor offset is at most 0.2524 × 9.47 = 2.39 wu, and ≈ 2.3 wu if the AABB top is
> the wing tip (which for a wing-dominated skinned mesh it will be).**

Cross-check: `[WallSegmentFade] BOARD VOLUME … y 0.00..4.66 … MEDIAN wall crest 4.66 wu` gives an
independent px↔wu scale from the room's masonry (≈ 177 px per wu at the dragon's depth), which
puts the bar at **2.34 wu** and the wing tip at **9.29 wu** — inside the 9.47 bound, from a
completely different pair of instruments. The two derivations agree.

### What that rules out

At an anchor of ~2.3 wu:

* **the 6 wu hard ceiling is refuted.** A bar clamped at 6 wu would sit 1062 px above the feet, at
  image row ≈ 1013. It is at 1660. To make 6 wu look like 415 px the figure's tallest mesh
  renderer would have to reach 23.4 wu (13.6 hexes) above its base, against a measured bound of
  9.47.
* **no successful measurement produced it either.** The raw offset is
  `(maxY − track.y) + 0.12·height`, and `maxY − track.y ≥ 9.3` (the wing tip is drawn above the
  track point). Nothing in the successful branch can yield 2.3.
* **the 0.05 floor is not it** (that would put the bar on the dragon's toes).

The only remaining value is `fallback = Mathf.Max(controller.m_WorldspaceOffsetY, 0.2f)` — the
game's own per-prefab `CharacterManager.Height`, passed through
`ActorBehaviour.CreateWorldSpaceGUIElements → WorldspacePanelUIController.Init(…, component.Height)`.
Its default is `1.8f`; ~2.3 for a boss prefab is an ordinary authored value.

> **Verdict (a): the bounds rule never ran for these figures. Every one of them fell through to the
> vanilla fixed height, which is right for a humanoid mini and lands mid-chest on a dragon.**

That also explains *"die Health-Bar von **allen** Drachen ist falsch"*: the two small drakes hover
and their bars likewise cross their bodies. The fallback is a per-prefab constant with no
relationship to a hovering, winged silhouette, so it misses all three the same way.

### Which of the fallback's five exits fired — NOT established

`MeasureAnchorOffsetWU` can return the fallback five ways: no object to track, no track point, no
`Renderer` at all on active objects, no *mesh* renderer surviving the static-batch / non-mesh
filters, or a degenerate height. Two of them are excluded by the fact that the bar *is* placed and
tracked at all (the placement pass calls the same `TryGetTrackPoint`). The remaining three are all
"the hierarchy was not the figure yet when we looked", and the game gives that a real mechanism:

* `CharacterManager.InitialiseCharacterAsync` instantiates the character's child prefab — the
  object that carries the mesh — through `Addressables.InstantiateAsync`, asynchronously.
* `MaterialLoaderData.LoadMaterials` sets **`Renderer.enabled = false`** and only restores it when
  the addressable material load completes. (Note this alone does *not* remove a renderer from
  `GetComponentsInChildren(includeInactive: false)`, which filters on GameObject active state, not
  on `Renderer.enabled` — so it is counted, not skipped, and the new instrument prints the count.)
* The mod's own `Compat/MaterialLoaderHeal` exists because the game leaves renderers
  foreign-disabled in this scenario; the same log contains it firing.

The bar is adopted the frame `WorldspacePanelUIController.Init` registers itself with
`WorldspaceUITools`, which is inside `ActorBehaviour.SetActor` — i.e. as early as it is possible to
be. **The measurement ran once, at the earliest possible moment, and the value was kept forever.**

### What shipped for (a)

1. **`BAR ANCHOR` — one line per adoption.** There was previously *no* instrument for bar
   placement at all: every `ActorBar` line in a hardware log is about draw order or size. The new
   line names the track mode and its world y, the renderer census (measured / on active objects /
   including inactive / static-batched / non-mesh / `Renderer.enabled=false`), the head joint's
   height above the track point, the bounds, the tallest contributing renderer **by name** with its
   `updateWhenOffscreen` flag, the raw offset broken into its two terms, **which clamp arm bound
   the result**, and the vanilla fallback for comparison — every length also in hex widths. A
   measurement that failed says so in words and names the exit it took.
2. **A bounded resample.** The anchor is re-measured up to 8 times at 0.5 s intervals after adopt
   and latches the moment two consecutive samples agree *and* the later one is a real measurement.
   A figure whose adopt-time reading was already right pays one extra renderer walk and changes by
   nothing; a figure that was measured mid-assembly corrects itself within half a second and
   writes a `BAR ANCHOR RESAMPLED` line saying so. The remedy and the instrument are the same
   thing, which is the point: this build cannot come back with "no improvement, no information".

### The prediction this build makes — read it before tuning anything

If the diagnosis above is right, the boss's `BAR ANCHOR RESAMPLED` line will read approximately:

```
height ≈ 9.3–9.5 wu (5.4–5.5 hex), TALLEST 'MO_ElderDrake_MESH',
raw offset = (top−track) ≈ 9.3 + 12% clearance ≈ 1.1 = ≈ 10.5 wu,
BOUND BY the 6.0 wu HARD CEILING  ⇒  6.00 wu
```

i.e. **the bar moves from 2.3 wu to 6.0 wu and the hard ceiling becomes the binding arm for the
first time.** That is above the dragon rather than through it — which is what the report asks for —
but the head crest is at ≈ 3.9 wu and the head joint lower still, so 6.0 wu will read as *high*.

**This is a deliberate, falsifiable step, not the finished answer.** The right next change is a
head-anchored rule (the bounding-box top *is* the head on a humanoid and is a wing tip on a
dragon; `m_HeadBonePoint` = `C_headSkel01_JNT` is present on every character and is now printed in
the same line), **not** a larger ceiling. I did not make that change in this build because it
rewrites the anchor rule for every figure in the game on the strength of one screenshot, and the
project's own history says a blind rule change costs more rounds than it saves. The head-joint
field is in the log line precisely so that decision needs no further build.

If instead the resample line never appears for the drakes, the timing family is dead and the
`BAR ANCHOR at ADOPT` line names which exit actually fired.

---

## (b) and (c) — one cause, and it is not the highlight

**(b) as reported is contradicted by the user's own log.** `ElderDrakeID` highlights and grabs
repeatedly in the ModBuild 290 session:

```
[FigureGrab] Right pinch candidate 'Actor(Clone)' at 30 mm real from the pinch point (radius 40 mm).
[FigureGrab] pre-grab highlight ENGAGED (Right near ElderDrakeID, 28 mm from the pinch point /
             81 mm from the palm …) — animated additive glow overlaid on the figure's own meshes
[FigureGrab] Right grabbed figure (ElderDrakeID); …
```

Twelve such engagements on `ElderDrakeID`, plus engagements on `RendingDrakeEliteID` and
`SpittingDrakeID`, at 0–30 mm from the pinch point. So:

* the highlight is **not** built from a renderer set that excludes the boss;
* the ModBuild 288 **walk-in stand-down is not it** either — it clears a *standing* glow on a mode
  edge and writes its own line ("walk-in mode engaged under a standing hover"), and that line
  never appears in this log;
* the bundle Overlay shader is present (the "UNAVAILABLE" branch never fires).

Read together with (c) — "I can grab it, but I have to do it at the lower part" — (b) and (c) are
**the same event seen twice**: a pinch aimed at the upper half elects nothing, so there is no
highlight *and* no grab; a pinch aimed low elects the figure and both work. One cause, two
symptoms, and the boundary the user reports (the health bar) is where the reachable volume stops.

### The mechanism, and what is NOT verified

`FigureGrabDriver` elects a figure by `collider.ClosestPoint(pinch)` against **one** collider:

```csharp
Collider? collider = interactable.GetComponent<Collider>();
if (collider == null)
    collider = interactable.GetComponentInChildren<Collider>();
```

— the game's authored mouse-pick collider on `CInteractableActor`, or the **first** collider found
below it. Nothing anywhere checks whether that volume covers the figure a VR player sees. On an
ordinary humanoid mini it does not matter; on a boss whose drawn body reaches 9.3 wu it decides
whether half of it is inert.

**I could not verify the boss's collider.** There is no game install on this machine, the prefab is
in an addressable bundle, and no log line has ever printed a figure's collider. That is exactly the
gap this build closes.

### What shipped for (b)/(c)

1. **`FIGURE REACH` — one line per figure adoption.** Collider type and name, its world y span and
   size, **how many colliders the figure carries** under its `CInteractableActor` and under the
   actor root (with "ONLY THE FIRST is used" stated in the line), the figure's rendered mesh bounds,
   and the **coverage**: what percentage of the rendered height the collider spans and how far
   below the rendered top its top sits. If the boss really does carry a base-sized capsule, that
   line says so in one reading — and if the game already ships more colliders, the round-two fix is
   exact and free instead of invented.
2. **`REACHED AND MISSED` — the trigger pull that elected nothing.** Throttled to one per second
   per hand (the same throttle the busy refusal uses), fired only on a trigger edge with no winner
   and no busy refusal. It names the nearest figure, its distance from the pinch against the pick
   radius, and — the field the report is about — **whether the pinch was above the top of that
   figure's collider, and by how much**. Until now "I reached for it and nothing happened" was the
   one outcome this subsystem could not describe: `LogElection` prints only winners.

I deliberately did **not** widen the pick radius or synthesise a reach volume. The recorded
rejection in `FigureGrabConfig` ("the leak fires on the palm reach, not at the 40 mm pick radius")
still stands, the card fan is measured in the same radius, and a mod-side volume built from
renderer bounds would inherit exactly the wing-inflated AABB that broke (a). Nothing here should be
built before the two lines above come back from hardware.

---

## Do (a) and (c) share a height?

The brief suggested they might share a **cause**. On the evidence they share only an approximate
**height**, and by coincidence:

* (a)'s height is `CharacterManager.Height` — a constant authored on the character prefab, ≈ 2.3 wu.
* (c)'s height is the top of the game's authored pick collider — unknown, but the user places it at
  the bar, ≈ 2.3 wu.

Both are per-prefab authored values scaled for an ordinary miniature, so they land near each other
without either causing the other. They are in different subsystems, computed from different inputs,
and fixing one will not move the other.

This is checkable next build without any new work: `BAR ANCHOR` prints `track y` and the offset
(so the bar's world y is `track.y + offset`), and `FIGURE REACH` prints the collider's world y span
directly. If the collider top and the bar land on the same world y, they are the same authored
number and worth one shared fix; if they merely land near each other, they are two.

---

## WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Everything in this section is code-reading, log-reading or photogrammetry — no VR hardware and no
game install were available to this lane.

* **The boss's collider.** Its type, size, position, and how many colliders the figure carries are
  entirely unverified. The whole of (c)'s mechanism — "the authored pick collider under-covers the
  boss" — is inference from the symptom plus the code path. It is the single most important thing
  the new `FIGURE REACH` line is there to settle, and it could still be wrong.
* **Which fallback exit (a) took.** I proved the *value* returned was the fallback; I did not prove
  *why*. The async-assembly explanation (child prefab + streamed materials) is a mechanism the
  decompiled sources definitely contain, but nothing shows it firing for these actors.
* **The screenshot measurements.** `bar / wing-tip = 0.2524` assumes constant pixels-per-world-unit
  along that vertical, i.e. that the feet, the bar and the wing tip are at the same depth. The wings
  lean, which biases the ratio *upward* — so `offset ≤ 2.39 wu` is safe as an upper bound, and the
  point estimate of ≈ 2.3 wu is softer than it looks. The project has been burned by exactly this
  before (`a-photo-length-needs-a-depth`). The bound is what refutes the 6 wu ceiling; the point
  estimate is what suggests the value is `CharacterManager.Height`, and only the bound is load-
  bearing.
* **`CharacterManager.Height` for these prefabs.** Not readable here — it is a serialized field in
  an asset bundle. ~2.3 is inferred from the image, not read.
* **The predicted post-fix placement (6.00 wu).** A prediction, not a measurement. If the resample
  does not fire, or fires with a different height, the prediction is simply wrong and the log says
  so.
* **The two small drakes.** I read their bars off the screenshot as also crossing their bodies and
  attribute that to the same fallback. Their `Height` values, their `Base` transforms (they hover —
  the base may be at ground level under a floating model) and their renderer sets are unmeasured.
  If the fallback theory is right the resample fixes all three; if a hovering figure's base is on
  the floor, the drakes may need a separate look even after the boss is right.
* **Nothing in this build was run.** It compiles clean and passes every gate; no line of the new
  instrumentation has ever executed.


---

# ROUND TWO — ModBuild 292: what the two instruments settled, and the two things they did not

Both ModBuild 291 instruments fired on his hardware (`.planning/debug/LogOutput.log`: 16
`BAR ANCHOR` lines, 6 `FIGURE REACH` lines, 192 `REACHED AND MISSED` lines). Read together they
settle (a) and (c) completely and refute part of round one's account of both — including two
things this round's own brief stated as established.

## The measurements, in full

`BAR ANCHOR`, every distinct reading in the session (world units above the track point, which is
the figure's `Base` at `y 0.00` for all five):

| figure | box y | box height | head joint | TALLEST | shipped 291 | arm |
|---|---|---|---|---|---|---|
| `BruteID` (adopt) | −0.36 .. 2.27 | 2.63 | 1.84 | `HE_Brute_Mesh` | 2.59 | none |
| `BruteID` (8 resamples) | −0.33 .. 2.23 | 2.55–2.57 | 1.69–1.71 | `HE_Brute_Mesh` / `LOD0` | 2.54–2.55 | none |
| `MindthiefID` | −0.20 .. 1.23 | 1.44 | 0.96 | `HE_Mindthief` | 1.41 | none |
| `SpittingDrakeID` (x2) | −0.04 .. 1.52 | 1.56 | **0.57** | `MO_Spitting_Drake_Mesh` | 1.70 | none |
| `RendingDrakeEliteID` | −0.06 .. 1.18 | 1.24 | 0.74 | `MO_Rending_Drake_Elite` | 1.33 | none |
| `ElderDrakeID` (adopt) | −0.27 .. 2.10 | 2.37 | 1.52 | `MO_ElderDrake_MESH` | 2.39 | none |
| `ElderDrakeID` (resampled) | **−1.29 .. 5.55** | **6.84** | **3.41** | `MO_ElderDrake_MESH` | **6.00** | **HARD 6.0** |

`FIGURE REACH`, all five:

| figure | pick collider | world y | size | colliders (under `CInteractableActor` / under actor root) | rendered body | coverage |
|---|---|---|---|---|---|---|
| `BruteID` | Capsule on `HE_Brute` | −0.05 .. 2.05 | 1.00×2.10×1.00 | 13 / 13 | −0.31 .. 2.25 | 82 % |
| `MindthiefID` | Capsule on `HE_Mindthief` | −0.15 .. 1.65 | 1.00×1.80×1.00 | 6 / 6 | −0.20 .. 1.23 | 96 % |
| `SpittingDrakeID` | Capsule on `Actor(Clone)` | 0.00 .. 2.00 | 1.00×2.00×1.00 | 1 / 3 | −0.04 .. 1.52 | 97 % |
| `RendingDrakeEliteID` | Capsule on `Actor(Clone)` | 0.00 .. 2.00 | 1.00×2.00×1.00 | 1 / 4 | −0.06 .. 1.18 | 95 % |
| `ElderDrakeID` | Capsule on `Actor(Clone)` | 0.00 .. 2.00 | 1.00×2.00×1.00 | 1 / 4 | −1.22 .. 5.50 | **30 %** |

---

## (a) — the resample works, the ceiling was a symptom, and the cause is a padded box

**Round one's two competing hypotheses are both dead.** The value 2.39 that produced "mitten in
ihm" was neither the vanilla fallback (which for this prefab is **3.50**, not the ≈2.3 the
photogrammetry suggested — round one's point estimate was wrong and its *bound* was what mattered)
nor the hard ceiling. It was a **real measurement of a figure that was not yet the figure**: at
adopt the boss's box read 2.37 wu tall with its head joint at 1.52; half a second later the same
box read 6.84 wu with the head joint at 3.41.

**The resample is the remedy and it stays.** It is what turned 2.39 into 6.00.

### The mechanism the resample is actually guarding against is NOT the one it was written for

Its doc comment attributed the race to Addressables material streaming
(`MaterialLoaderData.LoadMaterials` disabling renderers). The log refutes that attribution: the
renderer census is **bit-identical** across the two samples —

```
5 mesh renderer(s) measured of 36 on ACTIVE objects (48 incl. inactive),
0 static-batched and 31 non-mesh skipped, 2 measured with Renderer.enabled=false
```

— on both the ADOPT line and the RESAMPLED line. The same five renderers reported bounds 2.89×
apart. Nothing streamed in; the figure was **rescaled or re-posed** under a renderer set that never
changed. Corrected in the comment. A census that does not move is exactly the evidence that would
otherwise have been read as "nothing happened".

### THE REAL CAUSE OF THE REMAINING ERROR: the box is padded, and it admits it

`bounds y −1.29` was the field to read, and this round's brief asked what it is. **It is the box's
own admission of how loose it is.** The dragon is standing on a floor at `y 0.00`; 1.29 wu of its
box is below that floor, and there is no dragon there. Every figure in the log admits the same
thing in proportion: Brute −0.32, Mindthief −0.20, Spitting Drake −0.04, Rending Drake −0.06.

The mechanism is ordinary: `MO_ElderDrake_MESH` is a `SkinnedMeshRenderer` with
`updateWhenOffscreen=False`, so `Renderer.bounds` is its **authored/baked local bounds** transformed
by the root bone — a box sized to contain every pose in the clip set, plus whatever padding the
import produced. It is not the silhouette on screen this frame and was never claimed to be.

**So the fix is: subtract the box's measured error from the box's top.**

```
underhang  = max(0, baseY - boundsMin.y)     // provably empty box, below the floor
trustedTop = boundsMax.y - underhang         // the same padding taken off the top
top        = max(trustedTop, headJointY)     // never below the head
top        = min(top, boundsMax.y)           // never above the raw box
offset     = (top - trackY) + 0.12 * (top - trackY)
```

One assumption — that the padding is roughly symmetric — and it is the only correction available
that is itself **measured** rather than tuned: large exactly where the box is loose, vanishing
where it is tight.

### WHY NOT THE HEAD-ANCHORED RULE THE BRIEF ASKED FOR — the log refutes it

The brief proposed `min(headJoint + clearance, boundsTop + 12 %)` and predicted "on an ordinary
figure the head joint sits just below the bounds top so the result barely moves". That is true for
the two humanoids and **false for every drake in the log**:

| figure | head joint | box top | head as % of box top | brief's rule | shipped 291 | change |
|---|---|---|---|---|---|---|
| `BruteID` | 1.70 | 2.23 | 76 % | 2.01 | 2.54 | −21 % |
| `MindthiefID` | 0.96 | 1.23 | 78 % | 1.13 | 1.41 | −20 % |
| `SpittingDrakeID` | **0.57** | 1.52 | **37 %** | **0.76** | 1.70 | **−55 %** |
| `RendingDrakeEliteID` | 0.74 | 1.18 | 63 % | 0.89 | 1.33 | −33 % |
| `ElderDrakeID` | 3.41 | 5.55 | 61 % | 4.23 | 6.00 | −30 % |

A drake holds its head **down** and its back **up**. Anchoring the bar at the head joint buries the
two small drakes' bars **inside their own backs** — reproducing, on the figures the user *also*
called wrong ("die Health-Bar von **allen** Drachen ist falsch"), the exact defect being fixed on
the boss.

The head joint therefore survives as a **floor only**: whatever the box says, the answer is never
below the head. On all five figures in the log the floor does not bind; the log line now names
which term produced the top, so a build where it *does* bind says so.

Note that the two rules **agree on the boss to within 0.03 wu** (head rule 4.23, slack rule 4.26)
while only the slack rule leaves the small drakes alone. That convergence is the strongest argument
for either of them.

### How the head joint and the base are resolved — the brief asked, and the answer is not a guess

From `decompiled/GH.Runtime/WorldspaceDisplayPanelBase.cs:103-124`:

* the head joint is found **by name, once, in `Init`**: `FindInChildren("C_headSkel01_JNT")`. Not
  `Animator.GetBoneTransform`, not a serialized field.
* a character **without** that transform leaves `m_HeadBonePoint` null — *unless* the panel is set
  to `PoinToTrack.HeadBone`, in which case the game logs `Unable to find head bone on character`
  and **deactivates the panel**. So "no head joint" never means a bar in the wrong place: it means
  either the floor is absent and nothing else changes, or there is no bar at all.
* the base is found the same way, `FindInChildren("Base")`, and its absence **also deactivates the
  panel**. `m_BasePoint` is therefore never null on a bar that is drawing. The `baseKnown` guard in
  the new code is a belt, not a live branch, and when it does fire it returns the pre-fix answer
  exactly.
* `m_HeadBaseOffset` is captured **once** at `Init` as `head − base`. On a figure that rescales
  after `Init` — and this boss grew 2.89× half a second later — that offset is stale for the whole
  session. It feeds only the `HeadBoneStatic` track mode, which none of these five use.

### Before / after, every figure in the log

Anchor offset in world units above the track point:

| figure | ModBuild 291 | ModBuild 292 | change | binding arm after |
|---|---|---|---|---|
| `BruteID` | 2.54–2.59 | **2.14** | −0.40 (−16 %) | none |
| `MindthiefID` | 1.41–1.44 | **1.15** | −0.26 (−18 %) | none |
| `SpittingDrakeID` | 1.70 | **1.66** | −0.04 (−2 %) | none |
| `RendingDrakeEliteID` | 1.33 | **1.25** | −0.08 (−6 %) | none |
| `ElderDrakeID` (adopt) | 2.39 | 2.05 | −0.34 | none |
| `ElderDrakeID` (settled) | **6.00** | **4.77** | **−1.23 (−21 %)** | none |

**THE REGRESSION RISK, QUANTIFIED RATHER THAN WAVED AT.** The two small drakes move by 4 and 8
centimetres of board — invisible. The two heroes move by 16–18 %, and he has never complained about
them. What that move *is*: the Brute's bar sat 0.31 wu above a box top that overstates its head by
0.32, i.e. about 0.63 wu above the actual head; it now sits 0.23 wu above the corrected top. Both
readings are "above him", and the second is nearer what "darüber" means — but it is a visible change
to a figure nobody reported, and if he says the hero bars now sit too close, the answer is the
clearance fraction (12 %), not the correction.

### The 6.0 wu hard ceiling: kept, no longer binding, and its comment corrected

Its comment claimed "the tallest boss mini is ~5 wu". The log cuts both ways: **false** about the
box (6.84 wu) and very nearly **true** about the figure (corrected top 4.26, bar 4.77). It was
right about content and wrong about what it was measuring.

It is not deleted. Under the new rule the largest figure measured clears it by 1.23 wu, so what it
still guards is the case the underhang cannot see — a renderer whose bounds are garbage in a way
that does *not* show up below the base (a mesh folded into a foreign batch that survives the
`isPartOfStaticBatch` filter, a VFX mesh that is neither particle nor trail). A bar 6 wu up is
wrong; a bar 40 wu up is a bar the player never finds again. The line still names this arm when it
bites, and a hardware line naming it is now genuine news rather than the expected case.

### Two instrument defects found and fixed while reading the log

1. **The resample latch never fired for an animated figure.** `BruteID`'s eight samples read 2.59,
   2.55, 2.54, 2.55, 2.54, 2.55, 2.54 — skinned bounds breathe with the animation and
   `Mathf.Approximately` is a relative epsilon of ~1e-6, so the "two agreeing samples latch the
   budget to zero" rule never triggered. The Brute burned all eight samples and wrote **seven of
   the sixteen** `BAR ANCHOR` lines in the session. Replaced by a 2 %-of-the-anchor tolerance,
   compared against the *adopted* value so a slow drift still accumulates. The budget is now always
   spent (6 extra subtree walks per figure over 4 s) rather than latched early, which also removes
   the risk of freezing a wrong number on a figure that finishes assembling after its second
   sample.
2. **`CountAllRenderers` ran on every silent sample.** Its own doc comment said it was "walked only
   on a frame that is about to LOG"; it was inside an interpolated string built unconditionally, so
   every one of the eight samples paid a second full `includeInactive` subtree walk and a string
   build. The report is now generated only when the line is going to print.
3. **A new field that can falsify the whole correction in one reading:** the line now names the
   **LOWEST** renderer beside the TALLEST. If they are the same object, the underhang and the box
   top are one baked box and the symmetry argument holds. If they are different objects — a ground
   decal, a shadow blob, a socket mesh under the base — the underhang says nothing about the top
   and this correction is measuring the wrong thing. **Nothing in the ModBuild 291 log can tell
   those two apart**, and that is the single largest unverified assumption in this build.

---

## (c) — the game gives every monster the same 1×2×1 capsule, and the boss is 6.7 units tall

Fully attributed by the instrument. The three monsters carry the **identical** pick collider —
a `CapsuleCollider` on `Actor(Clone)`, world `y 0.00..2.00`, size `(1.00, 2.00, 1.00)`. It is not
authored per figure; it is the actor prefab's, one hex wide and two units tall whatever is standing
in it. Two of the three monsters are shorter than two units, so it fits them (95 %, 97 %). The boss
is 6.72 units of drawn dragon and **everything above y = 2.00 is inert** — which is the report
verbatim: *"muss es dann aber am unteren Bereich tun … über der Healthbar geht das nicht"*.

The two heroes carry per-bone collider sets (13 and 6) and their *first* collider is a well-fitted
capsule on the model root: 82 % and 96 %.

### WHAT THE FOUR EXTRA COLLIDERS ARE — I cannot tell you, and neither can the log

**This is where the brief was wrong, and it is the most important correction in this report.** The
brief says: *"The deciding field is the one the instrument was built to print: there are FOUR MORE
colliders under the actor root and only the first is used. Find out what they are."*

The instrument printed a **count**. It printed `1 collider(s) under its CInteractableActor and 4
under the actor root`, and nothing else in the log — not one line — says what those colliders are,
where they sit, how big they are, or whether they are triggers. A summary stat is not the field. So
"elect across all of them" would have been a fix built on two integers that cannot support it: `4`
is equally consistent with four body volumes and with four aggro triggers a metre wide, and
electing across an unknown trigger volume is how a figure becomes grabbable from a metre away.

The one thing the count *does* establish: they are **outside the `CInteractableActor` subtree
entirely** (1 under it, 4 under the root), which makes them likelier to belong to some other system
than to the miniature's body. Note also that the counts differ by species (Spitting Drake 3, the
other two 4) while their pick capsules are identical, which is not what a body-collider set looks
like.

`LogFigureReach` now **names every one of them** — type, object name, world y span, size, trigger
flag, enabled flag, and whether the pick search can even reach it — with a cap that states how many
it left out. One hardware line settles it next round.

### THE FIX: a mod-owned reach extension, applied only where the game's collider under-covers

Not a wider pick radius. `[FigureGrab] PickRadiusMillimeters` is untouched — the recorded rejection
stands, the leak fires at the palm reach, and the card fan is measured in the same number.

At adoption the driver measures the figure's rendered body, applies the **same** `FigureBody.
TrustedTopY` rule as the health bar (shared code, one rule, `FigureGrabbable.cs`), and compares it
against the game collider's top:

| figure | body y | collider top | trusted top | gap | 5 % of body | result |
|---|---|---|---|---|---|---|
| `BruteID` | −0.31 .. 2.25 | 2.05 | 1.99 | **−0.06** | 0.13 | no volume — untouched |
| `MindthiefID` | −0.20 .. 1.23 | 1.65 | 1.18 | **−0.47** | 0.07 | no volume — untouched |
| `SpittingDrakeID` | −0.04 .. 1.52 | 2.00 | 1.48 | **−0.52** | 0.08 | no volume — untouched |
| `RendingDrakeEliteID` | −0.06 .. 1.18 | 2.00 | 1.12 | **−0.88** | 0.06 | no volume — untouched |
| `ElderDrakeID` | −1.22 .. 5.50 | 2.00 | **4.28** | **+2.28** | 0.34 | **EXTEND to y 0.00..4.28** |

Only the boss gets a volume. The other four leave the method with no GameObject created and
`adopted.Collider` still pointing at the game's own collider — their behaviour is not merely
unchanged, it is **untouched**.

The volume is a `CapsuleCollider` on a mod-owned `VR_FigureReach` GameObject with:

* the game capsule's **own radius, axis and X/Z centre** — taller only, never wider. No figure
  becomes easier to grab from the side and none can steal an election from a neighbour.
* **Ignore Raycast layer (2)** + `isTrigger` — invisible to the flat game's mouse pick, to the VR
  laser and to every raycast either side runs, while still answering `Collider.ClosestPoint`, which
  does not consult layers. Same choice and same reasoning as `FigureClothHands`.
* **parented to the game collider's transform**, so it rides every move, turn and (uniform) rescale
  the figure makes with no per-frame writer — including `FigureStretch`'s held-size latch, which
  then scales the body and the volume by the same factor.
* **the game keeps its veto**: an extended figure whose *authored* collider the game disables is
  skipped by the election. The extension may add reach; it may not add permission.
* **re-measured 8 × 0.5 s after adoption** (never while held), for the same measured reason the bar
  resamples: this boss's own mesh bounds grew 2.89× half a second after `ActorBars` first measured
  it. A figure adopted mid-assembly must not keep a volume sized to whatever it briefly was. If the
  figure later shrinks below the game's collider the volume is destroyed and the election handed
  back.

**Why this and not `RegisterGrabbable` with both colliders:** `VRInteractables.RegisterGrabbable`
unregisters the target first, so one grabbable can hold exactly one entry. Extending only the
driver's own distance checks would have been a half-fix, because `ProximityGrabber.UpdateHighlight`
runs its own 0.13 m palm test against the **registered** collider — at the rig scale in this log
that is 1.24 wu, so a driver-only change would have opened the boss up to y ≈ 3.24 (below its head
joint at 3.41) and, worse, the ceiling would have moved with the zoom.

**Multiplayer:** the volume is local presentation, created and destroyed per client, and writes no
game state. Nothing about it is sent or received.

### THE `REACHED AND MISSED` PROBE FIRED 192 TIMES AND SAID NOTHING — the brief was wrong about this too

The brief says: *"`REACHED AND MISSED` fired 192 times in this session — it works; use it to check
your own fix."* It fired. It did not work.

* **Not one of the 192 lines names `ElderDrakeID`.** They name `MindthiefID` (93), `RendingDrake
  EliteID` (82) and `BruteID` (17).
* **The nearest figure was between 406 mm and 2 424 mm real** from the pinch point — ten to sixty
  times the 40 mm pick radius. There is not a single genuine near-miss in the whole session.
* **190 of the 192 print `ABOVE its top … a pinch above the collider can NEVER elect this figure`**
  — literally true and entirely irrelevant about a hand two metres away. The probe was asserting
  the round's leading hypothesis on evidence that only supported "nothing was near the hand". An
  instrument that agrees with you at two metres will agree with you about anything.

The probe fires on any trigger pull that elects nothing, and a player pressing the trigger for a
card, a panel or a teleport is not reaching for a miniature. Fixed by gating on the question the
probe is *for*: **did the hand reach into the figure the player can SEE?** — measured against the
figure's drawn surface (gating on the collider would re-import the very under-coverage being
diagnosed), with the palm reach as the bar. Every one of the 192 would have been suppressed; a
genuine reach for the boss's head measures 0. Suppressed pulls are **counted, not discarded**, and
the count rides the next line that is a real miss. The line also now prints the **lateral** distance
from the volume's axis (so "reached over the top of the mini" can be told apart from "was somewhere
else entirely and happened to be higher") and says whether the volume it is describing is the
game's or the mod's extension.

---

## Do (a) and (c) share a height? — settled: NO, and round one's coincidence story was also wrong

Round one guessed both were ≈2.3 wu by coincidence, one being `CharacterManager.Height` and the
other the collider top. The log says:

* the boss's vanilla fallback is **3.50**, not 2.3. It was never used — the bar was a real
  measurement of a half-built figure.
* the collider top is **2.00**, a prefab-wide constant shared by all three monsters.

Two different numbers from two different sources that happened to land near each other in one
screenshot. They are two defects, they have two fixes, and both are in this build.

---

## WHAT I COULD NOT VERIFY WITHOUT HARDWARE

No VR hardware and no game install were available to this lane. Everything below is code-reading,
log-reading or arithmetic.

* **THE SYMMETRY ASSUMPTION — the single biggest one in this build.** "The box is padded by the
  same amount above as below" is an *assumption*, supported by one fact (1.29 wu of the boss's box
  is under the floor it stands on) and by five figures whose corrected tops all land plausibly near
  their head joints. It could be wrong: if the box's underhang comes from a *different renderer*
  than its top — a ground decal, a shadow mesh, a socket under the base — then the correction is
  measuring one object's padding and applying it to another's. The new `LOWEST '<name>'` field is
  there to answer exactly that in one reading, and until a hardware log carries it, the correction
  is a well-motivated guess.
* **What is actually at `y 5.55` on the boss.** The brief states as fact that "the bounds top is the
  WINGS, not the head". **Nothing measures that.** The log gives box top 5.55, head joint 3.41 and
  box bottom 1.29 below the base — and no instrument identifies the geometry at any of those
  heights. Round one's screenshot ratio (head crest / wing tip = 0.425) is moreover *inconsistent*
  with the box top being the wing tip: it would put the head crest at 2.36, below the measured head
  joint at 3.41. Either the photogrammetry or the wing attribution is wrong, and the fix does not
  rest on either — it rests only on the underhang.
* **Whether 4.77 wu reads as "darüber".** It is 1.36 wu above the head joint and 0.51 above the
  trusted top. It is a prediction, not an observation. Equally: **nobody has confirmed that 6.00 wu
  read as "far over him"** — the brief states that as the current symptom, but ModBuild 291 shipped
  as an instrument build and no user verdict on its placement exists. If 6.00 was actually fine,
  this build makes the bar lower for no reason and the log line says exactly which term did it.
* **The two heroes' 16–18 % drop.** Quantified above, not observed. If he reports the hero bars now
  crowd the head, the dial is the 12 % clearance fraction and not the correction.
* **The four extra colliders.** Still unknown. The instrument now names them; the fix deliberately
  does not depend on the answer.
* **Whether the mod-owned volume disturbs anything.** Layer 2 + `isTrigger` is the same
  configuration `FigureClothHands` already ships without incident, and no `Rigidbody` is known to
  exist on the board, so no trigger event should be reachable — but no build with a collider
  parented under a game actor has ever run on his machine.
* **The laser far-grab path is NOT fixed.** `TryLaserGrab` uses a physics raycast, and the extension
  is invisible to raycasts by construction. Pointing the beam at the boss's head still elects
  nothing. That was not the reported symptom ("wenn ich den Drachen **in die Hand nehmen** will"),
  and making the volume raycastable would put it in front of the game's own mouse pick. If he asks
  for the beam to work on the boss's upper body, that is a separate, deliberate change.
* **Nothing in this build was run.** It compiles clean and passes every gate; not one line of the
  new code has ever executed.
