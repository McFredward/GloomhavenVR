# Phase 4 — Comfort & table-manipulation surface (`GloomhavenVR.Rig`)

> Owned by the Rig module (feat/comfort), updated by the Phase-5 integration pass
> (markers: **P5**). The in-VR settings surface binds exactly to `ComfortSettings` +
> the runtime ops below. It is **no longer a mod-owned panel**: `WorldUI/SettingsPanel.*.cs`
> and its 'SET' gear are DELETED, and the settings live in the game's own options
> window as the **VR options tab** (`WorldUI/Options/VROptionsTab.*.cs` +
> `ConfigCatalog.cs`) — curated Komfort rows plus the generic config browser under
> Debug ▸ *Alle Einstellungen*. `[SettingsPanel] ChordHoldSeconds` went with the panel:
> the non-dominant A/X button now carries `[WorldUI] ManualScreenChordSeconds`
> (default **2 s**) for the modal-escape and flat-screen-rescue holds, and a **tap**
> under 0.35 s toggles the game OPTIONS window (`WorldUI/Grab/NonDominantHold.cs`,
> `WorldUI/Options/OptionsToggle.cs`).

## 1. Design rules (read before integrating)

- **All comfort motion manipulates the RIG (inverse manipulation).** Game objects,
  the board, the game camera's *game-side* state — never touched. The P1 Harmony
  prefix-skips (`CameraController.LateUpdate` / `RefreshFocusPosition`) already park
  the game's camera code, so nothing fights the rig.
- **The world grab is on the THUMBSTICK CLICK, not the grip (P8 rebind).**
  `Rig/WorldGrab.cs`: *"click the THUMBSTICK to manipulate the diorama … the grip now
  grabs board figures, so the world drag lives on the stick-push button
  (`primary2DAxisClick`)."*
- **Stick contention — click vs axis, and they never fight.** The stick CLICK is a
  distinct button from the stick AXIS: `SnapTurn` and AoE targeting read the axis
  (`hand.Thumbstick.x`), world grab reads the click.
- **Object contention — INVERTED from the old grip rule: a stick-click starts a world
  grab REGARDLESS of what the hand holds.** That is the point of the rebind. Figure
  and card grabs live on the grip/trigger (`ProximityGrabber`) and never contend for
  the stick, so world locomotion stays available WHILE a mini or a card is in hand —
  the held object is parented to the hand and simply rides the rig as the world moves.
  Register your grabbables via `VRInteractables` as usual; no coordination code needed
  on your side. (The pre-P8 rule this bullet replaces — "a grip press only starts a
  world grab when that hand's grabber neither `Held` nor `Highlighted` a grabbable at
  grip-down" — is gone with the grip binding. Do not reintroduce it against the
  stick.)
- **Turning is NEVER suppressed by mode** (user ruling, hardware ModBuild 138: *"Die
  drehung soll nie blockiert sein!"*). `SnapTurn` used to hard-disable turning
  throughout `VRMode.BoardTargeting` because "targeting owns the thumbstick"; the MODE
  turned out to be the wrong question — it is derived from the SHARED Choreographer
  wait-state (a peer's pending move froze everyone's turning) and it is also true
  where the AoE rotation it protected declines to run. `Rig/SnapTurn.cs` asks the
  CONSUMER instead (`LocalTurnControl.TargetingOwnsStick`), and since AoE rotation
  moved to the hand `[Comfort] TurnHand` does not use
  (`Board/AoeControl.ResolveRotationHand`) that answer is structurally "no". Turning
  is ACTIVE in `ModalUI` (test #13). What remains: `Menu2D` (non-dev-proxy) and the
  menu-scroll gate (`ScrollTurnGate`). **`Rig/SnapTurn.cs` is still the one place to
  extend** if a future consumer really needs this axis — extend it by teaching that
  consumer to answer `TargetingOwnsStick`, not by adding a mode test, and never read
  the stick concurrently.
- **World grab availability** (ARCHITECTURE §8, updated test #13): **every scenario
  mode, `ModalUI` included** — a floating dialog (story box, help box, ESC menu)
  must not freeze the table; grab/rotate/zoom stay usable while it is open.
  **Disabled only in `Menu2D`** (the menu rig gives Menu2D a rig root, but there is
  no table; the dev proxy stays exempt so grab math is testable flat). Since
  **ModBuild 178** that exclusion is exact rather than approximate: the 3D map room
  resolves to `TableIdle`, not `Menu2D`, so the map table grabs and two-hand zooms
  like any diorama and `Menu2D` again means only the flat 2D menu.
- **P5 — menu rig:** while VR runs and neither a scenario board nor the 3D map room
  exists, `VRRigDriver` head-tracks the menu camera (`Camera.main`) at 1:1 scale —
  **unconditionally**; the `[Rig] MenuRig` dial is DELETED (user ruling 2026-08-13:
  its OFF built no rig at all outside a scenario — no head tracking, no hand anchor
  and nothing for the floating 2D screen to hang on, i.e. a brick rather than a
  viewpoint choice).
  `RigRoot`/`HeadCamera` are non-null in `Menu2D` too; `BaseWorldScale` reads 1.
  Recenter (chord/panel/`RequestRecenter`) returns the head to the menu camera's
  authored vantage. Anything gating on "rig exists ⇒ scenario" must gate on
  `Choreographer.s_Choreographer`/mode instead (WorldGrab and SnapTurn already do).
- **Comfort guard:** after every rig manipulation `RigClamp.Apply` lifts the rig so the
  eyes keep ≥ 0.10 real m clearance above the table plane (orbit-focus plane) — scale
  or drag can never put the head under/inside the table.

## 2. `ComfortSettings` — config accessors + change events

Static class, bound by `RigModule.Init()` (VR **or** `[Dev] Enabled`), released in
`Shutdown()` — check `ComfortSettings.IsBound` before touching accessors. Entries live
in **`BepInEx/config/dev.gloomhavenvr.comfort.cfg`**, section `[Comfort]` (own file —
the main plugin config is frozen P2 surface).

```csharp
ComfortSettings.IsBound                       // bool
ComfortSettings.AnyChanged                    // event Action<string /*key*/> — panel dirty-marking

// each of these is a ComfortSetting<T>:
//   .Value (get/set — set persists), .DefaultValue, .Reset()
//   .Section / .Key / .Description            (panel labels)
//   .Entry                                    (ConfigEntry<T> — acceptable ranges for sliders)
//   event Action<T> Changed                   (fired on ANY writer: code, panel, file)
```

**Defaults below are the SHIPPED ones, from `src/GloomhavenVR/Defaults/Defaults.Rig.cs`
— that file is the default of record.** A number inside a `Clamped(...)` / range call
in `ComfortSettings.cs` is a pre-bind fallback, not a default; do not quote it.

| Accessor | Type | Default | Meaning |
|---|---|---|---|
| `WorldGrabEnabled` | bool | true | Master switch for the **stick-click** world grab (not the grip — the grip is figure-grab since P8) |
| `FreeMovement` | bool | **true** | Test #10: no positional clamps at all — drag the table anywhere, only the (widened) scale limits remain. Off restores the pre-test-#10 comfort clamps |
| `VerticalDrag` | bool | false | One-hand drag may move the table vertically (always on while `FreeMovement`) |
| `RotateEnabled` | bool | true | Two-hand (both sticks clicked) yaw rotation |
| `ScaleEnabled` | bool | true | Two-hand (both sticks clicked) pinch scale |
| `ScaleMin` / `ScaleMax` | float | 0.5 / 4.0 | Pinch-scale clamps, × base WorldScale |
| `Turn` | `TurnMode` | **Smooth** | `Off / Snap / Smooth`. Config KEY is `"TurnMode"`, not `"Turn"` — the accessor and the key differ |
| `SnapTurnDegrees` | float | 45 | Degrees per snap step (15–90) |
| `SmoothTurnSpeed` | float | 90 | Smooth turn °/s (30–270) |
| `TurnHand` | `TurnHandChoice` | **Right** | `Dominant / Left / Right` (Dominant = `[Hands] PrimaryHand`) |
| `FlightEnabled` | bool | true | Stick flight: push the flight hand's stick forward to fly, back to reverse, sideways to strafe level (`Rig/Flight.cs`) |
| `FlightDirection` | `FlightDirectionSource` | Head | What flight steers by: the HMD's forward (pitch included) or the dominant hand's aim ray |
| `FlightMaxSpeed` | float | 1.27367 | Speed at full deflection in APPARENT m/s (converted through the live rig scale, so it feels the same at every zoom). Range 0.2–3 — the ceiling is the user's ruling, and a declared range IS the in-VR slider |
| `FlightHand` | `TurnHandChoice` | **Left** | Which stick flies. Shipped opposite `TurnHand` on purpose, so both work at once |
| `TurnStickVertical` | bool | **false** | The TURN stick's forward axis as world up/down travel at `FlightMaxSpeed`. Pinned off: the user asked for it as an OPTION and its off state is the pre-feature behaviour |
| `LaserCarryReel` | bool | **true** | Wind a laser-held window closer/further with that hand's stick Y (`WorldUI.PanelGrabHandle.TickCarryReel`). On because the user asked for the BEHAVIOUR and named the switch only so it can be turned off |
| `LaserCarryReelSpeed` | float | 2.0 | Reel speed at full deflection, apparent m/s (0.25–6) |
| `RecenterHoldSeconds` | float | 1.0 | B+Y both-hands hold time; 0 disables the chord |
| `SavedScaleMultiplier` | float | **0.83443** | Auto-persisted pinch-scale (written after each two-hand gesture, re-applied on rig build). A one-time migration in `ComfortSettings.Bind` lifts an EXISTING config sitting at exactly 1.0 to 2.5 and marks itself done via `[Comfort] TableScaleDefault25Applied`; a fresh install starts at the default above |
| `DebugGizmos` | bool | false | `[Comfort] DebugGizmos` overlay |
| `KeepPlaceOnReorigin` | bool | true | Compensate a runtime re-origin (typical after an HMD doff/don) so the player stays where they were — `VRRigDriver.TickOriginGuard` |

No comfort entry feeds the recenter POSE any more. The seat is the bare standing preset
and nothing adds to it, so `RecenterHoldSeconds` — how long the chord is held — is the
only recenter-related setting left.

**The preset is `StandingEyeHeightMeters` = 0.30 and `StandingEyeBackMeters` = 0.70,
and the 0.30 is load-bearing.** Freeze both numbers; they are the contract, not a
tuning value. `TableHeightOffset` shipped at −0.40 (the very bottom of its range) and
the tuned cfg carried −0.40 too, so the seat every build actually produced was
0.70 + (−0.40) = **0.30 m**. Retiring the dial and leaving 0.70 behind would have
raised every recenter by 40 real centimetres — times the rig scale, very visible — as
a side effect of a cleanup nobody asked to change the view. The tuned addend is folded
into the constant instead: 0.30 m above the focus plane, 0.70 m back, is the
leaning-in-over-the-board view the dial was pinned to. **If you find 0.70/0.70 written
anywhere, it is the bug this paragraph exists to prevent.**

#### SUPERSEDED — accessors this table used to list (none exist at HEAD)

Kept, marked, rather than deleted: these names still appear in older commits and notes.

| Named here before | Status at HEAD | Replaced by |
|---|---|---|
| `WorldScaleBase` | **gone**, and so is the `[Rig] WorldScale` entry it wrapped — zero `Bind` calls for either (user ruling 2026-08, "Tischgröße" removed) | `SavedScaleMultiplier` + the two-hand pinch |
| `SeatedMode` | **gone.** `ComfortSettings.cs` says so in as many words: "the old seated preset is GONE (user: irrelevant — the world is freely draggable)". Only the standing preset constants remain (`StandingEyeHeightMeters`/`StandingEyeBackMeters`, **0.30 / 0.70** — see the paragraph above for why the height is 0.30) | `FreeMovement` + free locomotion |
| `TableHeightOffset` | **gone** (user ruling 2026-08: "durch das freie Bewegen braucht man das nicht mehr"). It was the last addend on the recenter eye height; that height is now the bare standing preset | `[Comfort] FlightEnabled` (stick flight, `Rig/Flight.cs`) + the world grab |
| `VignetteEnabled`, `VignetteStrength` | **gone**, together with the whole `ComfortVignette` type (see §3) | nothing — no comfort vignette ships |

## 3. Runtime ops (options-tab actions / other modules)

```csharp
GloomhavenVR.Rig.Comfort.RequestRecenter();          // same path as the B+Y chord; safe no-op without a rig
GloomhavenVR.Rig.Comfort.SetScaleMultiplier(float);  // scales around the HMD, clamps, persists — NO CALLER today
                                                     //   (the table-scale slider went with the settings panel and
                                                     //    VROptionsTab offers no replacement row; the pinch reaches
                                                     //    the same state. Kept: giving the row back is a product
                                                     //    decision, not a cleanup.)
GloomhavenVR.Rig.VRRigDriver.BaseWorldScale          // float — resolved base scale, 0 while no rig
GloomhavenVR.Rig.WorldGrab.Instance?.IsGrabbing / .IsTwoHand / .CurrentMultiplier
GloomhavenVR.Rig.WorldGrab.Instance?.IsHandGrabbing(VRHand)  // is this hand's STICK CLICK consumed by world grab?
```

**SUPERSEDED:** `ComfortVignette.NotifyMotion(float01)` and `ComfortVignette.Pulse()`
were listed here. The `ComfortVignette` **type no longer exists** — there is no comfort
vignette in the mod, and nothing replaced it. Do not call these; do not re-add them
without the hardware round that would justify a vignette.

P1 statics are unchanged: `VRRigDriver.RigRoot`, `VRRigDriver.HeadCamera` (frozen P2
§6). New statics (`BaseWorldScale`, `Instance`, `RequestRecenter`) are additive.

## 4. Interaction summary (what the player does)

| Gesture | Effect |
|---|---|
| One stick CLICKED (held down), move | Drag the table freely in all directions while `FreeMovement`; with it off, horizontal only unless `VerticalDrag`. 1.5 cm deadzone, exponential smoothing. Works with a mini or a card in that hand — the held object rides the rig |
| Both sticks clicked, move apart/together | Pinch-scale, clamped to the EFFECTIVE limits (at least 0.1×–12× of base while `FreeMovement`, else `ScaleMin..ScaleMax`); haptic `ClickPulse` detent on both hands every 25% step; multiplier auto-persists on release |
| Both sticks clicked, swing around each other | Yaw the table around the point between the hands (2.5° deadzone) |
| Grip near a board figure | Figure grab (P8, `Board/FigureGrab/*`) — the grip no longer moves the world |
| Stick flick left/right (turn hand) | Snap turn ±`SnapTurnDegrees` around the HMD (0.7 engage / 0.3 re-arm), or smooth turn |
| Stick forward/back (flight hand) | Stick flight at `FlightMaxSpeed` apparent m/s, steered by `FlightDirection` |
| Hold non-dominant A/X `[WorldUI] ManualScreenChordSeconds` | Close the top floating modal, else toggle the flat screen (the in-scenario self-rescue) |
| Tap non-dominant A/X (< 0.35 s) | Toggle the game OPTIONS window — where the VR options tab lives |
| Hold B+Y (both hands) `RecenterHoldSeconds` | Recenter: HMD placed at the standing preset (0.30 m up, 0.70 m back) at the table edge |

Recenter button choice: the frozen P2 hand API exposes only A/X (`PrimaryButton`) and
B/Y (`SecondaryButton`); the OpenXR menu/system button is runtime-reserved and not
surfaced. The **both-hands B+Y hold** chord keeps single presses free for future
features and is hard to fire accidentally.

## 5. Dev harness

- `[Dev] Enabled` + `[Dev] SimulateHands` (F8): the comfort stack runs without an HMD
  against a hidden **dev rig proxy** (`GloomhavenVR.DevRigProxy`, scale 12 — matches
  the sim hands scale). **T** = trigger, **G** = grip on both sim hands
  (`HandsDriver.AnimateSimulation`); the procedural hand sway feeds the gestures.
  Observe via `[Comfort] DebugGizmos`.
  **The world grab is no longer reachable from the harness.** `VRHand.ReadSimulated`
  pins `ThumbstickClick`/`ThumbstickClickDown`/`Up` to false and `Thumbstick` to zero,
  and since the P8 rebind `WorldGrab` engages on the stick click — so holding G, which
  used to produce the two-hand grip gesture, now drives the grabbers only. Turning and
  flight are unreachable for the same reason. Nothing in `src/` currently maps a key to
  either; test drag/rotate/scale on hardware, or add a sim binding first.
- **F11** (dev mode, no HMD): recenter.
- `[Comfort] DebugGizmos`: bottom-left overlay (`Rig/ComfortGizmos.cs`) — rig
  pos/yaw/scale multiplier, grab state and per-hand STICK ownership, snap-turn arming
  + contention, clamp status, recenter-chord progress, persisted values. The seated
  preset and the comfort vignette are gone, so nothing about either is shown.

## 6. Threading & perf

Everything here is main-thread MonoBehaviour `Update` work; no Harmony patches were
added in Phase 4. No per-frame allocations on the always-on path (the `DebugGizmos`
overlay allocates strings while visible, same policy as the P2 dev overlay). Config
writes happen only at gesture end / explicit sets, never per frame.
