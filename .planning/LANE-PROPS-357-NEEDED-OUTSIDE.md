# LANE PROPS-357 — changes needed in files this lane does not own

Base: `ee04024e` (ModBuild 357). Branch: `lane-props-357`.
Owner of this lane: `Board/FigureGrab/{GrabbableProp,PropGrab,PropLift,PropHeldPose,HeldProps,FigureStretch,FigureStretchMath,FigureGrabConfig}.cs`,
new files under `Board/FigureGrab/`, `Defaults/Defaults.Board.cs`, `Core/Loc/Loc.ConfigNames.cs`,
`Core/Loc/Loc.ConfigDescriptions.German.cs`, `tests/GloomhavenVR.WireTests/**`.

**Headline: NOTHING BELOW IS REQUIRED FOR THE BUILD TO WORK OR FOR ANY GATE TO PASS.**
Every gate is green on this branch with none of these applied. Items 1 and 2 are OPTIONAL
follow-ups; item 3 is a note for whoever next touches `FigureGrabbable.cs`.

---

## 0. What this lane did NOT need from outside, and why — read this first

The obvious way to give props the figures' resize gesture is to declare an interface on
`FigureGrabbable` and `GrabbableProp` and type `FigureStretch` on it. That would have been an edit
to `FigureGrabbable.cs`, which this lane does not own.

It was not necessary. `FigureStretch` consumes exactly **ten** members from `FigureGrabbable`
(`HeldBy`, `TryGetHeldCenter`, `Stretch`, `SetStretch`, `GetStretchFactorBounds`, `IsHeld`,
`Label`, `HeldRenderers`, `TotalHeldSizeRatio`, `NoteCaptureVolume`), and every one of them is
already `internal` on that class. So the abstraction was built from the OUTSIDE instead — a new
`Board/FigureGrab/StretchTarget.cs` with two adapter subclasses that forward to members that
already exist. `FigureGrabbable.cs` is byte-identical to `ee04024e` on this branch.

Likewise `FigureGrabDriver.cs`: its two `FigureStretch.Engaged(side)` gates (lines 913 and 1025)
did **not** need a prop twin, because a prop is not elected centrally. `GrabbableProp.AllowsHand`
IS the prop election, and the veto was added there, inside this lane's own file.

---

## 1. OPTIONAL — curate the four shared resize dials under "Map items in your hand"

**File:** `src/GloomhavenVR/WorldUI/Options/VROptionsTab.4.Curated.cs` (WorldUI, not this lane's).

**Why it is optional and not a defect today.** No new config key was added by this lane. The prop
resize is governed by the four EXISTING `[FigureGrab]` keys — `StretchReachMillimeters`,
`StretchScaleMin`, `StretchScaleMax`, `StretchLimits` — whose captions and German help text this
lane widened to say they cover figures AND map items. `python3 scripts/check-options-coverage.py`
passes unchanged (the `("FigureGrab", "Stretch")` family has zero curated members, so check 4 does
not fire).

**Why you might still want it.** Those four keys are reachable only through *Erweitert* today, and
the section right beside them — "Map items in your hand" — is where a player who just discovered
they can resize a chest will look. The section's own comment states the invariant it keeps:
*"every `[FigureGrab]` key whose leading word is `Prop` is on this heading"*. These four are not
`Prop`-prefixed, so they do not violate it; they are simply not there.

**⚠ If you add them, add ALL FOUR OR NONE.** Curating even one gives the
`("FigureGrab", "Stretch")` family a curated member, which makes the other three SPLIT-FAMILY
failures under `check-options-coverage.py` check 4.

Insert after the existing `PropHeldUprightAtGrab` row in the "Map items in your hand" section:

```csharp
                        // The RESIZE gesture's four dials. They are the FIGURES' keys, shared
                        // rather than mirrored, because every one of them is size-neutral by
                        // construction — Min/Max are factors of each object's OWN board size, and
                        // the reach is measured from its own surface with a ceiling that already
                        // scales with the held size (GrabbableProp's "THE MAP-ITEM STRETCH"
                        // doc block has the full argument). They appear here as well as under
                        // figures so a player looking for "how big may a chest get" finds them
                        // beside the rest of the map-item rows.
                        new("FigureGrab", "StretchReachMillimeters", "",
                            "Figure/map item: resize reach (mm)", "Figur/Map-Item: Greifweite Größe (mm)"),
                        new("FigureGrab", "StretchScaleMin", "",
                            "Figure/map item: min size in hand", "Figur/Map-Item: Mindestgröße in Hand"),
                        new("FigureGrab", "StretchScaleMax", "",
                            "Figure/map item: max size in hand", "Figur/Map-Item: Maximalgröße in Hand"),
                        new("FigureGrab", "StretchLimits", "",
                            "Figure/map item: size limits on/off", "Figur/Map-Item: Größen-Grenzen an/aus"),
```

The captions above are already the exact strings this lane put into
`Core/Loc/Loc.ConfigNames.cs`, so the two agree.

**If you do this, `VROptionsTab.8.Dependencies.cs` already has the grey-out rules** — lines 228-229
map `StretchScaleMin`/`Max` to `StretchLimits`. No change needed there.

---

## 2. OPTIONAL — separate `PropStretch*` dials, if the user asks for them

This lane deliberately SHARED the figures' four resize dials rather than mirroring them, and the
reasoning is written out in full in `GrabbableProp.cs` under "THE DIALS ARE THE FIGURES', AND THAT
IS A DECISION, NOT AN OMISSION". In one sentence: the ModBuild 350 held-POSE keys were mirrored
because those numbers are ABSOLUTE GEOMETRY (metres out of the palm, degrees of pitch) and a
hex-sized box does not want a 30 mm miniature's offsets — whereas these four are RATIOS of each
object's own board size plus a reach measured from its own surface, so the correct value is
provably the same number for a chest and for a mini.

If the user asks for them anyway ("ich will das separat einstellen können"), here is the complete
patch. **Note the ordering constraint: steps (c) and (d) must land in the SAME commit**, because a
`PropStretch*` key without its curated row is a `("FigureGrab", "Prop")` SPLIT FAMILY failure —
that family is fully curated today.

(a) `src/GloomhavenVR/Defaults/Defaults.Board.cs`, in the `PropHeldPose` block:

```csharp
    internal const float PropStretchReachMillimeters = 80f;  // => [FigureGrab] PropStretchReachMillimeters
    internal const float PropStretchScaleMin = 0.5f;         // => [FigureGrab] PropStretchScaleMin
    internal const float PropStretchScaleMax = 3f;           // => [FigureGrab] PropStretchScaleMax
    internal const bool PropStretchLimits = true;            // => [FigureGrab] PropStretchLimits
```

(b) `src/GloomhavenVR/Board/FigureGrab/PropHeldPose.cs` — bind them there (same file, same
`[FigureGrab]` section, same `Val(...)` accessor style), then in
`GrabbableProp.GetStretchFactorBounds` and `ApplyGrabTimeStretchClamp` replace the four
`FigureGrabConfig.Stretch*` reads with the prop accessors, and in
`FigureStretch.TickHand`/`TickActive` read the reach through the target rather than through
`FigureGrabConfig` (add an eleventh member `float CaptureReachRealMeters` to `StretchTarget`).
Both files are this lane's; only (c) is outside.

(c) `VROptionsTab.4.Curated.cs` — four rows in "Map items in your hand", exactly as in item 1 but
with the `PropStretch*` keys and the `"Map item: …"` / `"Map-Item: …"` captions the other eight
rows use.

(d) `VROptionsTab.8.Dependencies.cs` — two lines beside the existing pair:

```csharp
        ["FigureGrab/PropStretchScaleMin"] = new("FigureGrab", "PropStretchLimits", On),
        ["FigureGrab/PropStretchScaleMax"] = new("FigureGrab", "PropStretchLimits", On),
```

(e) `Loc.ConfigNames.cs` + `Loc.ConfigDescriptions.German.cs` — this lane's files; and revert the
four widened captions back to "Figure: …" / "Figur: …" once props have their own.

---

## 3. NOTE — the interface `StretchTarget` should eventually become

`src/GloomhavenVR/Board/FigureGrab/StretchTarget.cs` is an adapter, and it says in its own doc why
it is one rather than an interface. Whoever next has `FigureGrabbable.cs` open for another reason
may collapse it: declare

```csharp
internal interface IHeldStretchable
{
    bool IsHeld { get; }
    string Label { get; }
    float Stretch { get; }
    void SetStretch(float factor);
    void GetStretchFactorBounds(out float min, out float max);
    bool TryGetHeldCenter(out Vector3 world);
    Renderer[]? HeldRenderers();
    float TotalHeldSizeRatio { get; }
    void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters);
}
```

and add `, IHeldStretchable` to both `FigureGrabbable`'s and `GrabbableProp`'s declarations. Every
member already exists on both with exactly these signatures — `FigureGrabbable.cs` needs no body
change at all, only the interface in its declaration list. `StretchTarget` then reduces to its
static `HeldBy(side)` dispatch and the two `HeldBy` calls it wraps, and the four cached adapter
instances (and the `Owner` identity token they exist for) go away with it.

**Do not do this as a drive-by.** The adapters are allocation-free because they are re-pointed
rather than created; a naive interface version that returns `FigureGrabbable.HeldBy(side)` directly
is strictly better still (no wrapper at all), but the `Owner` re-check in
`FigureStretch.TickActive` must then be re-expressed as a direct reference comparison against the
grabbable, not deleted — it is what ends a gesture when the hand swaps objects mid-drag.

---

## 4. NOTE — `Board/Patches/HexHoverClear.HideStaleTooltips` still wins the info write war

Unchanged by this lane and unrelated to it, but it is the standing one-line fix named by the
`[Props] held-prop INFO CONCEDED` log line: return early while `HeldProps.Count > 0`. That file is
`Board/Patches/`, not WorldUI, so it may already be in another lane's scope.
