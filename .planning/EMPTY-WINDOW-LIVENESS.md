# EMPTY WINDOW LIVENESS — the DARK verdict hides, it no longer tears down

ModBuild 291 (branch work; `NetProtocol.ModBuild` deliberately NOT bumped by this lane).
Files: `src/GloomhavenVR/WorldUI/ModalFallback.{3.WindowPanel,4.Tick,9.Spawn,10.CatchAll}.cs`,
`CanvasConversion.{3.Fit,4.Lifecycle,8.Order}.cs`, `GrabbableModal.cs` (comment only).

---

## 1. THE REPORT

> "Ich bin in die Karte gespawned dann ist das Fenster mit der Character-UI plötzlich einfach
> verschwunden, und war mehrere Sekunden lang verschwunden, bis es wieder aufgetaucht ist. Das soll
> nicht sein. Es darf erst gar nicht verschwinden. Was ist passiert?"

The window is `New Party display` (ID `PartyPanel`) — the map room's character screen.

## 2. THE TIMELINE, FROM HIS OWN LOG

`.planning/debug/LogOutput.log`, ModBuild 290, his hardware, one run. Frame numbers come from the
`[Perf] SPIKE` lines; the frame rate comes from the heartbeats (`Heartbeat #18: frames+871`, and
`VRHeartbeat.IntervalSeconds = 10f`, so ~87 fps).

| log line | what |
|---|---|
| `:4280` | `MODAL PRE-CONVERT BLACKOUT: 'New Party display' … switched off 13 own canvas(es) at frame 12954` |
| `:4282` | `MODAL FALLBACK: window 'New Party display' (ID PartyPanel) opened` — the map is still loading |
| `:4309` | `one-shot facing applied — yawed 54.8°` |
| `:4426` | `MODAL REVEAL … FORCED after 609 ms (deadline 600 ms; still waiting on first content fit)` |
| `:4457` | `FIXED FIT … the CHARACTER COLUMN renders 328x1080 px from (-984,-540) [83 graphic(s)]` |
| `:4469` | `MODAL LIVENESS ARMED … after 902 ms` — **by FIRST PAINT. It had content.** |
| `:4476` | `HIT RECT … DRAWN CONTENT 328x1080 px … from 85 visible graphic(s)` |
| ~+0.9 s | the content stops drawing |
| `:4480` | `Loading ended: backgroundLoadingPriority restored to BelowNormal` |
| `:4490` | `EMPTY GRAB BAR TAKEN OFF … 2 agreeing ink walk(s) … found ZERO drawn graphic(s)` |
| `:4497` | `Loading indicator hidden (loading ended, faded out)` |
| `:4501` | `PANEL SUPERSAMPLE stood down … 150.4 MB of render target was released` |
| `:4502` | `EMPTY WINDOW RELEASED … DARK … not one of 861 Graphic(s) … (dwell 2.0 s)` |
| `:4506` | `SPIKE frame 13264 … ModalFallback.Release 17.02 ms` |
| `:4516` | `SUB-VIEW REVIVAL: the liveness hold … is LIFTED because its OWN SUBTREE is MEASURED DRAWING` |
| `:4555` | `SPIKE frame 13584 … ModalFallback.Convert 46.03 ms` — the window is rebuilt from scratch |
| `:4545` | `one-shot facing applied — yawed 136.9°` — **and it came back somewhere else** |

Arithmetic: the float stood 3.9 s, first-painted at 0.9 s, went dark at ~1.8 s, hit the 2.0 s dwell
at ~3.9 s and was **torn down** — host, collider/raycaster, grab bar, arc slot, 150.4 MB of
supersample target. It was rebuilt 320 frames later, i.e. **~3.7 s**. That is his "mehrere
Sekunden". It also came back at a different yaw, because the rebuild re-ran the spawn placement.

Two independent instruments agree the window really was dark — the fit's own verdict (0 of 861
graphics) and the grab bar's ink walk (`2 agreeing ink walk(s) … ZERO drawn graphic(s)`). This was
not a measurement artefact. The party display genuinely stopped drawing for ~5.8 s in total while
the map room finished spawning.

Exactly two `EMPTY WINDOW RELEASED` lines exist in the whole session: `:4028` (`GONE — the game
UIWindow object was DESTROYED`, correct, untouched) and `:4502` (this one).

## 3. THE PREDICATE AS IT WAS

`ModalFallback.TickWindowLiveness` (ModalFallback.9.Spawn.cs), per floated window, every 6 frames:

```
GONE  (Window/Target/Host destroyed)                → EmptyReleasePending = true, no dwell
DARK  (subtree inactive, or not one Graphic passes
       CanvasConversion.CountsAsFitContent and no
       Renderer is enabled)
      … not before the reveal completes
      … not before the rule is ARMED (first paint, or a 1.5 s bounded grace)
      … not while SubViewRevival.SelectionHoldsRelease
      … after an unbroken dwell of 2.0 s (6.0 s for a live scripted level message)
                                                    → EmptyReleasePending = true
```

`EmptyReleasePending` was read by the release loop in `ModalFallback.4.Tick.cs` as an unconditional
"release this": `Converted.RemoveAt`, `wp.Grab?.Destroy()`, `CanvasConversion.Release(wp.Panel)`,
the arc slot handed back, and the window enrolled in `EmptyHold` (or `Failed` if blocking) so the
convert loop would not immediately re-float it. `EmptyHeldNow` probed the held window at 6 Hz and
lifted the hold when it drew again; `SubViewRevival.ShouldReviveHost` was a same-tick fast path for
the same lift.

**The defect is not in any of those terms.** Every one of them measured correctly. The defect is
that the ACTION at the end of the chain was a teardown.

## 4. THE PREDICATE AS IT NOW IS

Same measurements, a different action, and one new exemption:

```
EXEMPT (ESC / Options family: ESCMenu, Options, OptionsSubmenu, ViceOptionsSubmenu)
                                                    → never judged at all; a dormant one is woken
GONE  (unchanged)                                   → EmptyReleasePending = true, no dwell

DARK, awake
      … same reveal / arm / selection guards
      … after an unbroken EmptyHideDwellSeconds = 0.35 s
        (ScriptedMessageDwellSeconds = 6.0 s still applies to a live scripted level message)
                                                    → GoDormant()

DARK, dormant, every 6 frames:
      re-apply the hide (idempotent) and re-assert the raycaster off
      wake if  MeasureDrawsSomething()               (the strict fit verdict), OR
               DrawsAnythingScriptSide()             (the script-side test — see below)
                                                    → WakeDormant()
      else if dormant for DormantReleaseSeconds = 45 s
                                                    → EmptyReleasePending = true (the old teardown)
```

`GoDormant` calls `CanvasConversion.SetPanelRenderVisible(panel, false)` and switches the host
`GraphicRaycaster` off. `WakeDormant` is the exact inverse, in one frame.

### What dormancy actually does, and why it is as invisible as a release

| | released (≤290) | dormant (291) |
|---|---|---|
| every Canvas + Renderer of the float | destroyed | disabled (`HideTree`, recorded for exact restore) |
| grab bar + its laser collider | destroyed | disabled — the holder is a registered *extra render root*, and `GrabbableModal.IPanelGrabOwner.GrabVisible` already requires `!RenderHidden` |
| MR backing plate (opaque, host-rect sized) | destroyed | `MrBacking.TickPanels` refuses to build one and deactivates an existing one on `RenderHidden` |
| supersample render target (150.4 MB here) | released | released — `PanelSupersample.Eligible` refuses a `RenderHidden` panel |
| laser / poke | gone | **cannot reach it**: `RayUguiDriver` and `PokeInteractor` both iterate `UguiPokeSurfaces.Surfaces` and skip any Canvas that is not `isActiveAndEnabled` |
| host `GraphicRaycaster` (mouse/EventSystem) | gone | disabled, and both writers that force it back on now skip a dormant panel (`ModalFallback.Tick` step 4, `CanvasConversion.Tick`'s `_lockDirty` edge), plus a change-gated re-assert on every dormant probe |
| arc seat, pose, host, grab frame, committed fit, draw-order rung | gone | **kept** |
| coming back | full convert (46 + 71 ms of frame spikes), 150 MB reallocation, fresh spawn yaw | one frame, same pose, same seat |

**The brief asked what I did about "it does keep a laser collider in the room". It does not.** That
was the brief's one factual error about the mechanism: there is no collider on the host at all (the
laser is a plane intersection against registered Canvases, gated on `isActiveAndEnabled`), and the
one real collider — the grab bar's — lives on a GameObject that the hide switches off. The argument
is already written out verbatim in `GrabbableModal`'s `GrabVisible` comment, which was added for the
reveal gate and applies unchanged here.

### The new predicate: `DrawsAnythingScriptSide`

A dormant float is render-hidden, so every Canvas over it is disabled — and uGUI does not service a
disabled canvas. `CountsAsFitContent` and `DrawsAnythingLoose` both read
`CanvasRenderer.GetInheritedAlpha()` and `CanvasRenderer.cull`, which are maintained by that
servicing. `CanvasConversion.VerifyOneShotFit` says so in its own words and answers it with a
`LayoutRebuilder.ForceRebuildLayoutImmediate` + `Canvas.ForceUpdateCanvases` flush — which over a
2115-transform party display, several times a second, forever, is not a price a liveness probe may
pay.

**A wake test built on a value the hide itself can freeze is the settle gate that never opens, which
this project has already shipped once.** So the wake asks the script side and nothing else: active
GameObject, enabled `Graphic`, own `color.a` above the fit's own floor, the `CanvasGroup` chain up to
the window root above the same floor (honouring `ignoreParentGroups`), non-degenerate rect, plus the
usual `Renderer.enabled` arm for 3D previews. Every term is written by the game from its own Update
and readable with nothing rendering.

The floor is `CanvasConversion.FitMinAlpha`, promoted from `private` to `internal` and **shared, not
copied** — its value is untouched. It is deliberately the looser test, exactly as
`DrawsAnythingLoose` documents for itself: it can only ever cause a WAKE, a wrong wake costs one
frame and the now-trustworthy strict test puts it back to sleep after 0.35 s, and a wrong "still
dark" costs the stranded window this whole round is about. `WindowPanel.DormantCycles` counts the
flaps and the census prints them, so a bar that is too short is visible in the log rather than felt
on the headset. The strict test is still asked FIRST and its "yes" is trusted; the log line names
which of the two woke the window, so the next hardware run tells us whether the strict test survives
the hide at all.

Note on self-measurement: `ReassertStickyVisible` writes `alpha = 1` on the *window root's*
CanvasGroup for a sticky window the game hid. That cannot fake a wake — the script-side test is
per-graphic and every graphic must clear the floor on its own `color.a` as well, so a forced root
alpha is one factor of a product, not the answer.

### What was NOT changed, and why

* **`RefuseEmptyFloat` (ModBuild 226, the reveal edge) still releases outright.** Its own comment
  rules against exactly what this round does: *"Hiding the bar and leaving the panel alive would
  leave an invisible thing holding a map-room window slot and a draw-order rung — a worse bug than
  the visible one, and explicitly ruled out."* That ruling is about a window **born empty**, which
  never had content and has no reason to be expected back. This round is about a window that HAD
  content and lost it, where the invisible thing holding the seat is precisely what makes the return
  in-place. `DormantReleaseSeconds = 45 s` bounds the ruling's concern rather than overturning it.
* **The `GONE` shape.** Untouched, including for the exempt menu family: a destroyed options window
  has no content to come back to and leaving its chrome standing would strand the menu the ruling
  protects.
* **`ScriptedMessageDwellSeconds = 6 s`.** Kept. The DurabilityPanel argument behind it is about a
  tutorial box whose dismiss chain must not be lost, and the mod's own hide is what would blank the
  box the player has to click — so the "cheap to undo" argument does not reach it.
* **`SubViewRevival`.** Untouched (not this lane's file, and it needs nothing). With dormancy the
  host never enters `EmptyHold`, so `ShouldReviveHost` simply stops being reached for this shape; it
  remains correct for the backstop release. Its *job* is done one step earlier instead:
  `AncestorWillBeFloated` now wakes a dormant host in the SAME tick when a nested sub-view is about
  to be floated and the host's own subtree is drawing — `[[parent-wins-needs-a-real-parent]]` applied
  to a host that is present but invisible.
* **`ModBuild`.** Not bumped, per the lane's instructions.

### A candidate I measured and rejected: "is a load in flight"

It was the obvious discriminator — he had just spawned into the map, and there is a
`Loading indicator hidden (loading ended, faded out)` five lines before the release. **His log
falsifies it as the fix.** The load ENDED at `:4480`/`:4497`, *before* the release at `:4502`, while
the content did not return until ~3.7 s after it. A load term would have restarted the same 2 s dwell
at the load edge and torn the same window down ~2 s later — still ~1.7 s early. It is "raise the
dwell" wearing a state flag's clothes: it makes the same wrong decision later instead of making it
right. The brief warned against exactly this and the warning applies to the state-flag form too.

### `EmptyDwellSeconds = 2f` is retired with a tombstone

Not because the number was wrong — it was measured (the unlock flow blanks its own popup for a ~1 s
camera focus between two announcements, `UIUnlockLocationFlowManager.cs:150-158`) — but because the
irreversible action it guarded no longer exists. The two numbers that replace it say which is which:
`EmptyHideDwellSeconds = 0.35 s` (reversible, and SHORTER on purpose, because 2 s of it is 2 s of the
empty frame ModBuild 230 forbids) and `DormantReleaseSeconds = 45 s` (irreversible, and far LONGER
than 2 s for exactly the old reason). The three prose references to the old constant
(`WindowPanel.EmptySince`, `GrabbableModal`'s ink block, the rule's own guard list) were corrected so
nobody re-derives a 2 s bar on the hide path.

## 5. HOW EACH RULING IS SATISFIED

**ModBuild 230 — "Es darf niemals leere Fenster geben - verschwindet das Objekt das in dem Fenster
dargestellt wird, soll auch das Fenster verschwinden."**

Satisfied *better* than before. What the player could see of a dark float was the MR backing plate,
the grab bar and the host's own frame; all three are gone the moment it is hidden, and the hide now
happens after 0.35 s instead of 2.0 s — **1.65 s sooner than the shipped build**. The window
disappears when its content disappears. Nothing on the screen, nothing clickable, nothing pokeable,
nothing grabbable, and the render target is released. The ruling says the *window* must disappear; it
never said the float must be destroyed, and destroying it is the only thing that cost him the
seconds.

**ModBuild 291 — "Es darf erst gar nicht verschwinden."**

Satisfied to the extent physically available. The content genuinely was not drawing for ~5.8 s —
that is the game's doing during a map spawn and no presentation rule can invent pixels for it. What
this round removes is everything the MOD added on top:

* the return is one frame instead of a 46 ms + 71 ms rebuild;
* the window returns **where it was**, in the seat it never gave up, at the scale and fit it had —
  no re-placement, no new spawn yaw (54.8° → 136.9° in the shipped log);
* the wake latency is ≤6 frames (~0.07 s) after the content returns, against the shipped
  `EmptyHold` 6 Hz probe plus a full re-convert;
* the same-tick wake in `AncestorWillBeFloated` removes the "a sub-view opened inside an invisible
  parent" window entirely.

Replayed against his own timeline: content dark at t≈1.8 s → hidden at t≈2.15 s (the empty frame
leaves 1.65 s earlier than it did) → visible again at t≈7.6 s, in place, in one frame. The shipped
build showed an empty frame until t≈3.9 s and then nothing at all until t≈7.6 s, followed by a
rebuild that moved the window.

## 6. THE OPTIONS / ESC FAMILY (the second job)

The other lane named this subsystem as the most likely remaining route to an unopenable pause menu,
via two mechanisms. Both were checked against the shipped code and the report's log.

**(a) The liveness rule — CHANGED.** `IsMenuFamilyWindow` (`ESCMenu`, `Options`, `OptionsSubmenu`,
`ViceOptionsSubmenu` — the same four IDs `IsFullScreenMenu` and `WantsTransparentBackground` already
treat as one family) is now consulted *ahead of the arm as well as ahead of the dwell*, so a member
is never armed and no later edit to the dwell arithmetic can put it back in scope. The `GONE` shape
still applies. The menu-spawned confirmation boxes are deliberately **not** in the set: a
confirmation that has finished should hide like anything else, and it is not the recovery path the
ruling protects. The census prints the exempt population so the exemption cannot silently stop
existing.

This is insurance, not a bug fix, and the log says so: `UI Scenario Esc Menu` (`:2208`) and
`UI Options Window_unified` (`:2277`) both armed by FIRST PAINT and neither ever went dark. There is
no evidence this family has ever been released by the rule. The exemption removes a route nobody has
walked, which is the right price for a ruling with the word MUSS in it.

**(b) `MODAL PRE-CONVERT BLACKOUT` — NOTHING NEEDED, and here is the evidence.** It cannot strand a
window:

* `TickPreConvertHide()` is the **first** statement of `ModalFallback.Tick()`, before any step that
  can throw (`ModalFallback.4.Tick.cs`, immediately after `EnterPhase(PhasePreConvertHide)`).
* It force-restores on every non-conversion outcome — globally off / screen style / Menu2D / VR off,
  no room to float in, the game closed it, already floated, conversion failed — and unconditionally
  after `PreConvertHideMaxFrames = 8` frames (~0.1 s at 87 fps), with a `Warn` naming the case.
* `TryConvertWindow` releases it as its *first* act, so the conversion's own hide records the true
  enabled state.
* `ReleaseAllPreConvertHide` covers module shutdown and VR off.
* It only ever disables `Canvas.enabled` on canvases it recorded as enabled, and restores one for
  one.

The only way it could strand a menu is `ModalFallback.Tick` never running again, and in that world
the mod has already stopped floating anything. I changed nothing here.

## 7. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Every item below is a claim this lane makes that only a headset run can settle. All of them are
instrumented — the log line to grep is named.

1. **Whether the strict test survives the hide at all.** The central unknown. If
   `CanvasRenderer.GetInheritedAlpha()` / `cull` freeze under a disabled canvas as the fit's own
   comment implies, every wake will come from the script-side arm; if they do not, the strict test
   will do it. Both work; which one fires is a fact about Unity I could not measure here.
   **Grep `EMPTY WINDOW BACK` and read the clause after the dash.** If it always says "SCRIPT-SIDE",
   the strict test is dead weight on this path and should be dropped from the dormant probe.
2. **Whether 0.35 s is the right hide dwell.** It is two liveness strides at the measured 87 fps.
   A window that legitimately blinks its content faster than that will now flap (hide/show) instead
   of standing empty. **Grep `MODAL LIVENESS CENSUS` and read the FLAPPER count**, and grep
   `EMPTY WINDOW HIDDEN` for `dormancy #2` or higher on one float. A rising count means the number
   is too short for some window — and the fix is that window's dwell, not a blanket 2 s.
3. **Whether the wake is really one frame to the eye.** The panel is visible again in the frame
   `WakeDormant` runs, but `PanelSupersample` re-engages on its own schedule afterwards, so the
   window may be visible at un-supersampled sharpness for a moment and then re-allocate its render
   target (a `PANEL SUPERSAMPLE engaged` line and a possible frame spike). That is strictly better
   than the shipped 46 + 71 ms convert, but it is not literally free and I could not time it.
4. **The cost of the dormant probe.** Per dormant float per 6 frames: one `HideTree` re-apply
   (Canvas + Renderer walk), one strict measure (Graphic walk + `CountsAsFitContent`) and, when that
   says dark, one script-side walk with a CanvasGroup chain per graphic. On the party display that is
   861 graphics over 2115 transforms. **The census prints `us/frame` and the walk count** — that line
   is the falsifier. If it moves materially, the script-side walk should memoise the CanvasGroup
   chain per parent instead of walking it per graphic.
5. **The 45 s backstop has never fired anywhere.** No session in hand keeps a window dark that long.
   Its release path is the old, exercised one, but the *number* is a judgement, not a measurement.
6. **The `_lockDirty` and step-4 raycaster skips** are reasoned from source, not observed. The
   change-gated re-assert in the dormant probe is the net under both. **Grep for a dormant window
   ever receiving input** — there is no positive instrument for this; the honest statement is that
   three writers were found and all three now agree, and a fourth would be invisible to me.
7. **Multiplayer.** Dormancy writes nothing to the game and nothing to the wire (same as the release
   it replaces: no `Hide`, no `Escape`, no `CanvasGroup` write, no state). `RemoteMapStory`'s
   `ReportSharedStory` reads `!panel.RenderHidden` as "floated" for a **diagnostic verdict line**
   only, so a dormant shared story window will report "not floated" in that line. That is accurate
   but newly reachable; it is a log verdict, not behaviour, and the file is another lane's.
8. **A dormant window is still in `Converted`**, so every per-tick consumer of that list sees it.
   I walked the ones that write (raycaster, order ladder, MR backing, supersample, grab, fit) and
   each either already reads `RenderHidden` or now skips dormant panels. A consumer I did not find
   would see an "alive" window that draws nothing — which is exactly what the shipped reveal gate
   already presents for up to 0.6 s on every float, so the shape is not new.
