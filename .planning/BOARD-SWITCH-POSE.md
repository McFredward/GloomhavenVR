# Board switch: same place, same size

> "Wenn man das Board wechselt, soll das neue Board an der exakt selben Stelle in der exakt selben
> Größe auftauchen. Bei tests hat es seine größe geändert. Fix das." — user, 2026-08-25

Position was already preserved. **Size was not**: the board came back at about a third.

---

## 1. The measurement

`.planning/debug/LogOutput.log`, ModBuild 289, his hardware. One board switch in the whole session,
`Oak → Steel`, log line 7637. The `[Cards] BOARD ANCHOR` instrument brackets it:

| | line | world scale | own localScale | parent chain | apparent size | per unit |
|---|---|---|---|---|---|---|
| before | 7582 | 6.865 | 0.223 | ×30.85 | 45.9 cm | 206.39 cm |
| after  | 7677 | 2.129 | 0.223 | × 9.57 | 14.2 cm |  64.00 cm |
| then   | 7687 | 2.690 | 0.281 | × 9.57 | 18.0 cm |  64.00 cm |

Read the columns, not the story:

* **`localScale` survived bit-for-bit.** 0.223 → 0.223. Nothing wrote the board's own scale.
* **The parent frame did not.** 30.85 → 9.57, a factor of **3.224** — which is exactly
  6.865 / 2.129, and exactly 206.39 / 64.00. One number explains all three columns.
* **The third row is a consequence, not a second defect.** 14.2 cm is below the 18 cm apparent
  minimum, so `ClampApparentSize` pushed the board back up to 18.0 cm (log line 7678, `BOARD SIZE
  PUSHED by the MINIMUM`). Its thresholds are correct and are not touched by this fix. Once the
  size is preserved the board never goes under the minimum and the push does not fire at all.

So the player saw **two** wrong sizes in sequence: 14.2 cm, then 18.0 cm. Neither is 45.9 cm.

## 2. The mechanism

`TryCapturePose` captured **`position` and `rotation` in WORLD space but `localScale` in LOCAL
space.** A local scale is not a size until you say what it is local *to*, and between the capture
and the restore the thing it was local to is replaced:

1. `CardsDriver.4.Rebuild.cs:RebuildBoard()` captures pos/rot/localScale.
2. `PlayTray.Destroy()` `DestroyImmediate`s `_pinRoot` — the world-static `GloomhavenVR.TrayPin`
   holder that a PINNED board hangs from. **The frame the captured 0.223 meant is now gone.**
3. `EnsureBuilt` creates the fresh root under `_anchorParent`, the rig anchor — lossyScale ×9.57.
4. `RestorePose` writes 0.223 into that frame. 0.223 × 9.57 = 2.13. Position, being world-space,
   lands correctly; size, being local-space, does not.
5. `ApplyFollowMode` then creates a **fresh** holder and seeds
   `_pinRoot.localScale = scaleRef.lossyScale.x` from the **live rig** — 9.57 — and re-parents with
   `worldPositionStays: true`, which faithfully preserves the wrong world scale it was handed.

### Why the old holder held 30.85 while the rig held 9.57

Because it is supposed to. `PlayTray.2.Watchdog.cs:SyncPinHolder()` carries a comment block with no
code under it whose *absence of code is the invariant*: the holder scale is written **once, at pin
time, and never again**. An earlier cut re-asserted it from the live rig every frame, and that was
the tester-reported bug — *"world zoom zooms the pinned control board too — that must not happen"*.
A pinned board must not ride the world-grab zoom.

So a pinned board's holder legitimately carries whatever rig scale was live **when the player
pinned**, and the rig scale that happens to be live **at switch time** is an unrelated number.
`ApplyFollowMode` seeding from the live rig is right at pin time and wrong at switch time.

## 3. The fix

**Carry the frame across with the pose.** Not a new number — the same numbers, told what they mean.

* `TryCapturePose` now also snapshots, via `CaptureSwitchFrame()`: whether the board was PINNED, the
  **holder instance** (identity, not just scale), the **holder's scale**, the parent chain's
  lossyScale, and the resulting **world scale**. Signature unchanged, so the two call sites in
  `CardsDriver.2.Update.cs` (which this lane does not own) are unaffected and get the fix for free.
* `RestorePose` runs three steps in **one synchronous call**:
  1. `TryRestoreCapturedPinFrame()` re-creates the holder with the **captured** scale and re-parents
     the fresh root onto it. If the captured holder instance is still alive and still the parent,
     it does nothing — writing its scale there would *be* the live rescale `SyncPinHolder` forbids.
  2. Solve the local scale for whatever frame the root is actually in. Frame intact ⇒ the captured
     `localScale` is written back **bit-exact**. Frame changed ⇒ `localScale × (capturedParent /
     liveParent)`, which holds the world size. Parent scale degenerate ⇒ **no divide at all**:
     write verbatim and say so in the log line, which becomes the lead.
  3. Write pos/rot/scale, then the existing `ApplyFollowMode` tail — now unreachable on the switch
     path, because step 1 already satisfies its `_root.parent != _pinRoot` guard.
* `EnsurePinRoot()` in `PlayTray.1.Core.cs` is now the single place the holder object is created.
  It deliberately does **not** set the scale: its two callers need different answers, and folding
  the scale in is how the two cases got confused in the first place.
* `DiscardCapturedPose()` — one capture, one restore. Called at the end of every `RestorePose` and
  from the `hand == null` rebuild abort, so a captured frame can never be consumed by a later,
  unrelated restore (session resume, carried-pose rebuild).

### Both invariants, and why

**(a) Apparent size identical across the switch.** All five quantities are now identical on both
sides — world position, world scale, own `localScale`, holder scale, and per-unit — so the apparent
size is identical by construction, not by tolerance. In the common case it is bit-exact: nothing is
divided, the captured `localScale` is simply written back into the frame it came from.

**(b) The pinned board still does not ride a world-grab zoom.** The re-established holder is
world-parented (not under the rig), its scale is written once at restore and never again, and
`SyncPinHolder` still never rescales it. A subsequent zoom rescales the rig, which the holder is not
under, so nothing beneath it moves or resizes. The pre-fix code was the one that violated this —
it re-seeded the fresh holder **from the live rig**, i.e. it re-introduced exactly the coupling the
2026-08-07 fix removed, once per board switch.

**FOLGEN (follow) mode is structurally immune, and the code says so with an early return.** A
following board hangs directly off `_anchorParent`, the rig-space anchor, and `EnsureBuilt`
re-parents the fresh root onto the **very same transform** the old one hung from. Its `localScale`
therefore denotes the same size on both sides with no help from anyone, and there is no holder to
lose. The early return is not an untested branch — it is a branch that cannot arise — and it is
written explicitly so nobody later "fixes" it by manufacturing a pin for a board the player asked
to follow him.

### Why the holder is restored and the localScale is NOT merely re-solved

The brief offered these as equivalent alternatives. **They are not.** Re-solving `localScale =
capturedWorldScale / liveParentScale` against a rig-seeded holder gives the right *world* size and
the **wrong `localScale`**: 6.865 / 9.57 = **0.717** instead of 0.223. And `localScale` is
load-bearing config state, not a free variable —

* `PlaceAtHead` writes `_root.localScale = Vector3.one * ComputeBoardScale(board)`, i.e.
  `ClampedTrayScale × BoardScale_{board}`;
* `PersistPoseToConfig` reads it straight back out into `TrayScale` / `BoardScale_{board}` on grab
  release.

So a board left at 0.717 would, on the next grab-release, persist a board **3.22× too large** into
his hand-tuned config, and the next `PlaceAtHead` (lost-board recovery, session start) would rebuild
it at that size. It would also leave the holder tracking a rig scale the player never pinned at, so
the *next* switch would repeat the problem from a new base. Restoring the holder is the only option
that leaves all five quantities identical.

## 4. The instrument

One line per switch — not per frame — replacing the old bare `Control board switch: preserved the
previous board's world pose`:

```
[Cards] BOARD SWITCH POSE: world pos preserved at (18.57, 9.60, 18.66). SIZE — captured world
scale 6.865, restored world scale 6.865, own localScale 0.223, pin holder ×30.848 at capture,
RE-ESTABLISHED from the capture (NOT re-seeded from the live rig — that is the fix). delta
+0.000 % — PRESERVED (the bar is 0.1 %). Frame: parent frame intact at ×30.848 — localScale
written back bit-exact. …
```

It prints the captured world scale, the captured holder scale, the restored world scale and the
delta in per cent, and names which of the four frame clauses ran. The next log answers "did the size
survive" with a number, with no arithmetic across two `BOARD ANCHOR` samples. Against the 289 log
the same line would have read `delta -68.991 % — NOT PRESERVED`.

## 5. Ordering: no intermediate size can render

The holder re-establish, the pose write and the re-pin all happen inside the one synchronous
`RestorePose` call, which `Rebuild()` issues immediately after `EnsureBuilt` in the same stack —
no yield, no coroutine, no frame boundary between the fresh root appearing and its final pose being
written. `ClampApparentSize` runs from the per-frame watchdog (`TickLostWatchdog`) and therefore
cannot observe anything but the finished state.

The 14.2 cm → 18.0 cm push in the 289 log was **not** a transient the ordering let through: it was
the push correctly reacting to a wrong size that had already shipped and would have stayed. With the
frame carried across there is no under-minimum size for it to see and it does not fire. **No clamp
threshold was changed.**

## 6. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Stated plainly. Everything below is reasoning from source and from one log, not measurement.

1. **That the fix works on his rig.** Not observed. It is verified by arithmetic against the 289 log
   and by the compiler. The `BOARD SWITCH POSE` line is the falsifier: **switch the board while
   FIXIERT and grep it — a preserved size prints a delta under 0.1 %.** If it prints a larger delta,
   the `Frame:` clause in the same line says which of the four branches ran and the pin-holder
   figures say whether the holder came back.
2. **That the push no longer fires after a switch.** Predicted, not observed. Falsifier: no
   `BOARD SIZE PUSHED` line within a second of the switch.
3. **Invariant (b) after a switch** — that the board still ignores a world-grab zoom once it has
   been switched. This is the one that matters most and it is code-reading only. It needs an
   explicit test: pin the board, switch it, **then** world-grab zoom, and check `BOARD ANCHOR`
   reports `world scale ×1.00000` with `push did not fire`.
4. **FOLGEN-mode switch.** The 289 log's board was FIXIERT throughout; a following board was never
   switched in it. The immunity argument is from reading `EnsureBuilt`'s re-parent, not from a
   measurement.
5. **The follow-toggled-across-a-switch case** (captured FOLGEN, restored FIXIERT, or the reverse).
   Handled by the frame-solve branch, exercised by no log I have.
6. **Multiplayer.** No wire change and no new field — `NetAvatarDriver` samples `board.lossyScale.x`,
   which is precisely the quantity this fix makes correct, so a peer should now see the switched
   board at the sender's real size instead of a third of it. Not tested against a peer.
7. **The degenerate-parent branch.** Unreachable in any log I have; its correctness is by
   inspection. It deliberately does not divide, and it labels itself as the lead if it ever runs.

## 7. A note on the worktree base

This lane's worktree branched from `065fbda3` — roughly ModBuild 240, **1153 insertions behind
`origin/dev` in the four owned files alone**. In that base, `ClampApparentSize` was still gated to
FOLLOW mode and the per-unit divisor was still the pin holder in FIXIERT; both were changed by the
2026-08-25 ruling that ships in 289. Patching at the stale base and merging would have **reverted
the push and the divisor change**. The worktree was reset onto `origin/dev` (`ba918fe5`, ModBuild
289) before any edit, so this branch's diff is against the code the measurement was taken from and
touches nothing outside the four owned files.
