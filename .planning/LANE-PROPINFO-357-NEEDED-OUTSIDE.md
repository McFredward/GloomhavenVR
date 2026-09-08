# LANE PROPINFO (base ee04024e / ModBuild 357) — what this lane needs from files it does not own

> ### ✅ CLOSED — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> Items 1 and 2 were explicitly OPTIONAL and nothing depended on them. Item 3 — the
> `GrabbableProp` concession note that still prescribed a fix which had already shipped — **was
> addressed**: that `VRLog.Note` was re-worded at ModBuild 366 and now names the two remaining
> candidate writers instead (`Board/FigureGrab/GrabbableProp.cs:1304`). Nothing here is
> outstanding.

Owner of this lane: the held-prop info card (HALF A) and the held-figure/prop ghost depth
(HALF B). Files owned: `WorldUI/Surfaces/PropInfoSurface.cs`,
`WorldUI/Surfaces/StatPanelSurface.cs`, `WorldUI/Tooltips/HexHintFacing.cs`,
`Board/Patches/HexHoverClear.cs`, `Board/FigureGrab/FigureGhosts.cs`, `PropGhosts.cs`,
`FigureOverlay.cs`.

**Read this first: NOTHING BELOW IS REQUIRED FOR THE FIX TO WORK.** The held-prop dock resolves
its anchor from the shared `HeldProps` registry on its own and is live as shipped. Item 1 is a
fidelity upgrade; item 2 is a debt correction; item 3 is a comment that is now wrong. A remedy
gated behind a file this lane cannot land would have been a remedy that never runs, and this
project has already paid for one of those (`gated-remedy-never-ran`).

---

## 1. OPTIONAL — hand `PropInfoSurface` the exact prop visual (fidelity upgrade)

Today `PropInfoSurface.TryResolveHeldAnchor` finds the held prop by walking the holding hand's
`Rig.GrabAnchor` children and asking `HeldProps.OwnsRendererOf(child)`. That is exact whenever the
visual is a DIRECT child of the grab anchor — which is what `GrabbableProp.OnGrab` does today
(`t.SetParent(anchor, worldPositionStays: true)`). It degrades to the grab anchor itself (the hand,
a few centimetres off) if that ever stops being true, and the dock log line says which route ran.

The one-line hook that removes the walk entirely, mirroring `FigureGrabbable.cs:651-653`:

```diff
--- a/src/GloomhavenVR/Board/FigureGrab/GrabbableProp.cs
+++ b/src/GloomhavenVR/Board/FigureGrab/GrabbableProp.cs
@@ (in OnGrab, immediately after the ShowInfo() call at ~:383)
         ApplyHeldPose();
         Live.Add(this);
         ShowInfo();
+        // Dock that card BESIDE THE PROP, exactly as FigureGrabbable docks the stat card beside a
+        // held mini (user, 2026-09-03: "es soll sich wenn man es in der Hand hält genau so
+        // verhalten wie die Figur-Info neben der Figur"). Optional: PropInfoSurface resolves the
+        // same anchor from HeldProps on its own; this hands it the exact visual instead.
+        WorldUI.Surfaces.PropInfoSurface.ShowHeldProp(_visual.transform, hand.Side);
```

and the matching clear, in BOTH release paths — next to each existing `HeldProps.Remove(_prop);`
(`:624` and `:667`):

```diff
         HeldProps.Remove(_prop);
+        if (_holder != null)
+            WorldUI.Surfaces.PropInfoSurface.ClearHeldProp(_holder.Side);
```

(Place the clear BEFORE `_holder` is nulled, or capture the side first. `ClearHeldProp` is
idempotent and a no-op for a hand that never registered, so a missed call degrades to the
registry-driven route rather than to a stale dock — but a stale registration WOULD survive a
release, so if item 1 is landed at all, land both halves.)

Signatures already in place on this lane's side:

```csharp
internal static void ShowHeldProp(Transform? anchor, GloomhavenVR.Hands.HandSide holdingHand);
internal static void ClearHeldProp(GloomhavenVR.Hands.HandSide holdingHand);
```

## 2. OPTIONAL — a cleaner accessor on `HeldProps`

`HeldProps.Visuals` is private and there is no getter, which is the only reason this lane walks
the grab anchor's children. Three lines beside the existing `TryGetSlot` would replace it:

```diff
--- a/src/GloomhavenVR/Board/FigureGrab/HeldProps.cs
+++ b/src/GloomhavenVR/Board/FigureGrab/HeldProps.cs
@@ (after TryGetSlot, ~:144)
+    /// <summary>The VISUAL ROOT of the <paramref name="slot"/>-th still-held prop, in the same
+    /// GRAB ORDER as <see cref="TryGetSlot"/>. The transform an info card must RIDE — a captured
+    /// position would dock once and then stop following the hand.</summary>
+    internal static bool TryGetVisual(int slot, out GameObject visual)
+    {
+        visual = null!;
+        if (slot < 0 || slot >= Visuals.Count)
+            return false;
+        visual = Visuals[slot];
+        return visual != null;
+    }
```

If this lands, `PropInfoSurface.TryResolveHeldAnchor` can drop its child scan and read the visual
directly.

## 3. A COMMENT THAT IS NOW WRONG — `GrabbableProp.TickInfo`'s concession line (`:774-781`)

That `VRLog.Note` says:

> THE WRITER IS ALMOST CERTAINLY Board/Patches/HexHoverClear.HideStaleTooltips … The fix is one
> line there: return early while HeldProps.Count > 0.

**That fix has already been landed** — `HexHoverClear.HideStaleTooltips` has carried exactly that
early return since ModBuild 340 (verified at `ee04024e`, `HexHoverClear.cs:283`). The line is a
debt that outlived its truth (`a-debt-can-outlive-its-truth`): if the concession now fires, the
writer is somebody ELSE, and the two live candidates are

* the game's own `WorldspaceStarHexDisplay.ShowTooltipForTile`, which hides both info panels on
  every hover change (WSHD.cs:3574-3575) — reachable while a prop is held because only the
  HOLDING hand's ray goes off, so the OTHER hand's laser can still sweep the board; and
* a `UIWindow` fade that has not finished (`IsVisible` is alpha>0, so a fade-OUT still reads up).

Re-word the tail of that line to name those two instead of a fix that shipped three builds ago.
Do NOT change the line's TIER or its leading text (`promote the TIER, never re-word` applies to
the tier; this is a factual correction to the prescription, which is the part that is wrong).
