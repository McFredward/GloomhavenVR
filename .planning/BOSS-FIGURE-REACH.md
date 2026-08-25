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
