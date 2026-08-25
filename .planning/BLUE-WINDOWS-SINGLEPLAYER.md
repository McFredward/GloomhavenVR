# Blue windows: the scenario spawn height, and no blue bars in singleplayer

Two halves of one report (2026-08-25, `spawn_scenario.jpg`, ModBuild 289 `LogOutput.log`):

> "Das 'blaue' Multiplayer Fenster ist IN dem Spielfeld gespawned … es muss viel höher spawnen
> damit es über dem Spielfeld schwebt. Weiterhin bin ich im Singleplayer, ich möchte dass es keine
> 'blauen' Fenster im Singleplayer gibt."

---

## HALF 1 — the spawn height

### The cause was the FRAME, not the number, and the log had already said so

The scenario shared anchor was NOT missing a clearance. It had one, and its own falsifier printed
it as correct:

```
SHARED WINDOW ANCHOR APPLIED (RE-PLACE …) — 'Story Window' … HOME 0 above the play field …
FRAME=BOARD (PlayTray.Current.Root 'GloomhavenVR.PlayTray', extents from
PlayTray.MeasureBoardLocalExtents) … pos (0.0000,0.6100,0.0000) … board top edge 0.1600 …
margin 0.30 … the BOTTOM EDGE sits +0.300 board-local above the top edge
```

`+0.300` was true. It was 0.300 above the top edge **of the player's card tray**.
`Cards.PlayTray` is "the control board" — the chest-height, tilted, *hand-carryable* card desk
(`PlayTray.1.Core.cs` class doc). It is not das Spielfeld. In this very log the tray was in his
hand at that moment (`FIXIERT, HELD — the hand is carrying it`), so the anchor followed it down:

| term | value |
|---|---|
| tray root world pos | `(1.17, −1.41, −1.49)` wu |
| tray-local anchor pos | `(0, 0.610, 0)`, tray world scale 6.317 |
| resulting window centre | `(1.66, 2.39, −2.37)` wu |
| play surface (orbit-focus plane) | `0.00` wu |
| diorama scale | `9.57` wu/m |
| **bottom edge above the play surface** | **+0.169 m** — down among the figures |

A margin in that frame cannot be tuned into a clearance over the play field, because the frame is
attached to the player's hand. So this is a frame change.

### The new rule, with every term named

Frame: **the seat anchor** — `PanelLayout.TryGetAnchor` = `CameraController.FocusPoint` (the orbit
focus, which `VRRigDriver` parks the rig at, which `PanelLayout` calls "table center", which
`ModalFallback.TryGetBoardPlaneY` calls the board plane) plus the **cached seat yaw**, with real
metres obtained by dividing by `PanelLayout.WorldScale`.

```
bar          = ScenarioWindowBoardClearanceMeters   (dial, default 0.60 m, code floor 0.30 m)
bottom edge  = bar + GrabBarDropMeters (0.018 m)
centre       = bottom edge + this window's OWN half-height (halfSize.y / WorldScale)
world pos    = FocusPoint + seatYaw * (lateral, centre, 0) * WorldScale
world rot    = seatYaw          (yaw-only; +Z away from the reader = faces the reader)
world scale  = PanelLayout.WorldScale
```

**The clearance I chose: 0.60 m of grab-bar height above the play surface.** Why that number and
not an invention:

* It is the height the user has **already accepted in the other room** —
  `MapRoomWindowBarHeightMeters = 0.60 m`, "Die neue Position von den remote-Fenster gefällt mir".
  Same quantity, same definition (measured at the bar), so a window now hovers the same way in
  both rooms.
* Measured against the mod's own standing estimate of how far the board's furniture reaches above
  the orbit-focus plane — `ModalFallback.BoardTopClearanceMeters = 0.30 m`, the constant every
  ordinary modal spawn has been floored by since Request B — it leaves **0.30 m of clear air under
  the bar**. That is the "schwebt" half; the first 0.30 m is merely "not inside the scenery".
* Against his own log it is **4x** what he photographed (bar at +0.151 m → +0.60 m).

It is behind a dial, the same kind the map-table clearance uses:
**`[WorldUI] ScenarioWindowBoardClearanceMeters`, default 0.60, range 0.3–1.5**, floored in code
at `ArcSeats.ScenarioWindowMinBarHeightMeters` = `BoardTopClearanceMeters` so no typed value can
put a bar into the scenery. (One new dial — declared to the integrator.)

### How the play surface is determined, honestly

**There is no measurement of the scenario diorama's rendered bounds anywhere in this mod.** Nothing
walks the dungeon's renderers the way `PlayTray.MeasureBoardLocalExtents` walks the tray's. What
the mod does have, and what four subsystems already use, is `CameraController.FocusPoint`:

* `Rig.VRRigDriver` builds the rig at it and orbits world-tilt/zoom about it,
* `WorldUI.PanelLayout.TryGetAnchor` uses it as "table center" and measures every slot height from
  it,
* `Rig.Comfort` measures `MinEyeAboveTableMeters` from it,
* `ModalFallback.TryGetBoardPlaneY` calls it the board plane and floors every modal spawn on it.

Its Y is **the plane the hexes lie in, not the top of what stands on them**. This build does not
pretend otherwise: the height is measured from that plane, and the falsifier prints the residual
over `BoardTopClearanceMeters`, which is the mod's standing estimate of the furniture's reach. If
that estimate is wrong, one number in one log line says so.

### The log prints the arithmetic, and reads the answer back off the RESULT

`SHARED WINDOW ANCHOR APPLIED` now carries, in this order: the play surface's world Y and the
scale; the seat yaw; the frame-local pose in **real metres** (the number that must match on two
clients); `bar + drop + own half-height = centre`; and then, **recomputed from `worldPos` and the
true `halfSize` rather than restated from the inputs**:

* `bottom edge ±x.xxx m` above the play surface,
* `GRAB BAR ±x.xxx m` above the play surface,
* `the bar stands ±x.xxx m clear of the board's own top edge`.

A negative number in either of the first two is this report and nothing else. No subtraction is
needed by the reader.

### Three things this also fixed, stated because they were silent

1. **The size.** The old path returned the *tray's* `lossyScale` (6.32) as the window's world
   scale, while every other scenario window is built at `PanelLayout.WorldScale` (9.57). The shared
   story box therefore shipped **34 % smaller** than its neighbours. It is now built at the same
   scale as everything else, which means **it will look about 1.5x bigger than in the screenshot**.
   That is intended, but it is a visible change the user did not ask for and may comment on.
2. **The half-height fed to the placement was measured at a different scale than the window was
   built at** (`PlaceAtHmd` measures `halfSize` at `PanelLayout.WorldScale × extraScale`, then
   builds the host at whatever scale the anchor returns). Same scale on both sides now, by
   construction.
3. **The spawn frame and the drag frame are the same frame for the first time.** The old line ended
   with a stated caveat: *"this kind's drag travels on record 19 (Net/RemoteStorySync), whose frame
   is the per-client SEAT ANCHOR rather than the board, so the anchor and the drag do not share a
   frame; that seam is not this build's to close."* `RemoteStorySync.TryToAnchor/TryToWorld` are
   `PanelLayout.TryGetAnchor` + `PanelLayout.WorldScale`, which is exactly what the anchor is now
   built from — so the caveat is deleted rather than re-printed, and the frame-local metres in the
   anchor line are directly comparable with the metres a peer receives.

### What is deliberately NOT in it

* **No depth term.** The window sits over the field's centre (depth 0 in the seat frame), because
  "über dem Spielfeld schwebt" says height and nothing about depth, and a depth constant would be
  a second invented number. Consequence, stated: looking straight down at the middle of the board
  puts the window in that line of sight. The drag still wins, and moving it back is one grab.
* **No top-edge ceiling** (the map room has `MapRoomWindowTopCeilingMeters`). Only
  `SharedWindowKind.ScenarioStory` can reach this path — the other three kinds are behind
  `MapRoomDriver.Active` — and the game's story box fits 233 px, ~0.36 m at the shipped scale, so
  the top lands well under eye level. A future second scenario kind that is tall would need one.

---

## HALF 2 — no blue windows in singleplayer

### The predicate, and where it comes from

`FFSNetwork.IsOnline` — the game's own static, `BoltNetwork.IsRunning && !IsShuttingDown`
(decompiled `FFSNetwork.cs:25-33`). It is **the mod's established accessor**: `Net.RevealGate`,
`Net.InitiativeHoverSampler`, `Board.EnemyInfoPhaseSkip` and `WorldUI.WristHud` all read it
directly and unguarded; `VROptionsTab.Cheats` reads it behind a fail-closed try/catch.

Deliberately **not** `Net.INetTransport.IsOnline`: that resolves the same property through cached
reflection on every call, and this predicate is asked per floated window per frame. It is also
what keeps `WorldUI` from taking a dependency on `Net`, which is the rule stated at the top of
`SharedWindows.cs` — `FFSNetwork` is a game type, like the two story singletons beside it.

Cost: one static property read resolving to a static bool, i.e. the same order as the
`Singleton<T>.IsInitialized` test it joins. `IsShared` was reordered to ask it **first**, so a
singleplayer client no longer runs `KindOf`'s singleton compares per floated window per frame for
an answer already decided. Strictly less work than before.

### Where the gate sits

One place: the top of `SharedWindows.ParticipatesHere`, above the per-kind switch. Everything else
already funnels through it.

### Every behaviour the blue bar gates, and what each does in singleplayer

| # | behaviour | reached through | singleplayer now |
|---|---|---|---|
| 1 | **grab-bar tint** | `GrabbableModal.SyncSharedBarTint` → `KindOf` + `ParticipatesHere`, once per tick per floated window | private brass |
| 2 | **release re-face suppression** | `GrabbableModal.WantsReFaceOnRelease` → `_shared` (cached from the same predicate in the same tick) | re-faces on release, under the player's own `[WorldUI] WindowFacing` dial |
| 3 | **the three orientation modes** (`Always` / `Never` / `LaserOnly`) | the same `_shared` gate — the modes are documented "for LOCAL windows only" | all three apply to the story box exactly as to the merchant's window |
| 4 | **remote pose easing** (`_easing`, `PeerPlaced`) | `GrabbableModal.PlaceFrameAt` → `_shared` | never eased; nothing drives it offline anyway |
| 5 | **`PanelPoseWatch.peerOwned`** | `ModalFallback.9.Spawn.cs:1716` → `IsShared`, once per tick | false ⇒ the pose lock treats every window as locally owned, which is the private behaviour |
| 6 | **the shared spawn anchor** | `TrySharedWindowAnchor` → `ParticipatesHere` | refused ⇒ the ordinary head-relative spawn path runs, including its own board-plane floor. So in singleplayer the story window is placed like every other window, which is the second half of "sollen alle Fenster Singleplayer-Fenster sein und sich auch entsprechend verhalten" |
| 7 | **the raised send cadence** | `NetAvatarDriver` → `SharedWindows.AnyGrabbedHere` → `GrabbedHere` → `ParticipatesHere` | false; that path is already behind `transport.IsOnline` as well |

Items 2, 3 and 4 are the ones the brief flagged as possibly latched. They are **not** latched at
grab start or at conversion: `GrabbableModal._shared` is rewritten every tick in
`SyncSharedBarTint` (`ModalFallback.4.Tick.cs:2946`, for every converted panel), from the same
predicate the tint uses. So one predicate change moves all of them, live, with at most one frame of
staleness — the same staleness the bar colour already carried.

### The transition, both directions

* **offline → online.** Next tick: `_shared` becomes true, the bar turns blue, the re-face and the
  facing dial stop applying, the pose is published and applied. **No reopen, no restart.** A window
  that was already standing changes behaviour in place.
* **online → offline under a live grab.** `_shared` becomes false mid-carry; on release the window
  re-faces like a private one. That is correct — there is no room left to disagree with, and no
  pose is published because the send path is behind `transport.IsOnline` too. Any in-flight remote
  ease simply converges: nothing calls `PlaceFrameAt` again.
* **The receive path is untouched.** `RemoteStorySync.Observe/Resolve` and
  `RemoteMapStory`'s appliers reach the window through `SharedWindows.TryGetGrab`, which has never
  consulted `ParticipatesHere` and still does not. A client that is online applies peer poses
  exactly as before — and by construction it cannot be offline while receiving one.
* **One consequence worth naming:** if the session comes up while the story window is already open,
  the window keeps the private pose it was spawned at (the anchor is spawn-only and was refused at
  the time). A later presence-regain refloat would seat it at the shared home. Both are correct;
  neither jumps a revealed window, because `TickPoseRePlaceOne` refuses to move one.

### The reading of "singleplayer" this commits to — and it is a decision

**Singleplayer = not connected to a session** (Bolt not running). A one-player online session — a
host alone in a lobby — counts as **multiplayer** and gets blue bars.

* The user's own words are a session-state change: *"Wechselt der Spieler von Singleplayer zum
  Multiplayer"*. `FFSNetwork.IsOnline` is exactly that change and nothing else.
* The alternative fails on its own terms. While hosting alone this client's records really are
  being published; and the moment somebody joins mid-scenario the bar would have to be right
  *already*. A bar that turns blue on a stranger's join is a second, later surprise, and the first
  pose that peer receives would be against a window this client had been re-facing on release.
* It is also the only reading that is cheap enough for a per-frame predicate.

**The log says which state it observed, so a hardware test can settle it with no code change.**
`SHARED WINDOW SESSION GATE` prints on the transition edge only (change-gated on an int, one
compare in the steady state) and carries `FFSNetwork.IsOnline`, `IsHost`, `IsClient` **and the
player count from `FFSNet.PlayerRegistry.AllPlayers`**. A count of `1` beside `ONLINE` is the
"connected but alone" case *observed* rather than assumed — if the user then says those bars should
have been brass, the change is one clause in `SessionIsOnline`.

---

## WHAT I COULD NOT VERIFY WITHOUT A SECOND CLIENT

1. **That two clients compute the same frame-local anchor pose.** The anchor is now expressed in
   the same frame and units record 19 already travels in, so it *should* be identical to the
   millimetre — but both terms of that frame are per-client objects: `CameraController.FocusPoint`
   is each client's own parked orbit focus, and the seat yaw is each client's own recenter. In the
   shipped scenario both are derived from the board, so they should agree; nothing in a
   single-client run can show that. **The test is one grep:** diff the `pos (x,y,z)` on the two
   clients' `SHARED WINDOW ANCHOR APPLIED` lines. Equal ⇒ 1:1. Different ⇒ the frame is the
   suspect, not this file.
2. **That the window's own half-height does not diverge.** It is the one term two clients can
   legitimately disagree about (different window-size dials), exactly as ModBuild 244 stated for
   the table. The line prints it.
3. **That the bar turns blue live on join.** I traced the whole chain (`ParticipatesHere` →
   `SyncSharedBarTint` → `_shared` → tint / re-face / easing) and it is re-read every tick, but the
   join edge itself has not been executed. The `SHARED WINDOW SESSION GATE` line is the falsifier:
   it must appear at the moment of connect, and a `SHARED WINDOW BAR` line naming the story box
   must follow it within a frame.
4. **That a peer's pose still lands after the gate.** The receive path was not touched and cannot
   run offline, but "not touched" is an argument, not a measurement.
5. **Whether 0.60 m is what he means by "viel höher".** It is 4x what he photographed and the
   height he approved in the map room, but it is his eye that decides. The dial exists so the next
   round is a cfg value and not a build.
6. **The exact top of the scenario diorama.** No instrument in this mod measures it. If a scenario's
   walls reach higher than `BoardTopClearanceMeters` (0.30 m) above the orbit-focus plane, the
   `clear of the board's own top edge` field in the new line will still read positive while the bar
   is behind a wall. That field is the place to look, and the fix would be a real measurement of
   the play field's renderers — which would be a new instrument, not a new number.
