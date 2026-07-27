# Phase 4 — Comfort & table-manipulation surface (`GloomhavenVR.Rig`)

> Owned by the Rig module (feat/comfort), updated by the Phase-5 integration pass
> (markers: **P5**). The **in-VR settings panel exists since P5**
> (`WorldUI/SettingsPanel.cs`) and binds exactly to `ComfortSettings` + the runtime
> ops below (open: 'SET' gear at the table edge, or hold the non-dominant A/X for
> `[SettingsPanel] ChordHoldSeconds`).

## 1. Design rules (read before integrating)

- **All comfort motion manipulates the RIG (inverse manipulation).** Game objects,
  the board, the game camera's *game-side* state — never touched. The P1 Harmony
  prefix-skips (`CameraController.LateUpdate` / `RefreshFocusPosition`) already park
  the game's camera code, so nothing fights the rig.
- **Grip contention — object grabs win.** A grip press only starts a world grab when
  that hand's `ProximityGrabber` neither `Held` nor `Highlighted` a grabbable at
  grip-down (frozen P2 state). A grip that grabbed a card is ignored by WorldGrab
  until physically released. Register your grabbables via `VRInteractables` as usual;
  no coordination code needed on your side.
- **Stick contention — `BoardTargeting` owns the thumbstick.** Phase-3a rotates AoE
  patterns with the stick, so snap/smooth turn is hard-disabled while
  `VRModeStateMachine.CurrentMode == VRMode.BoardTargeting` (and re-armed on exit, so
  leaving targeting can't fire a stale flick). Turning is also off in `Menu2D`.
  **Test #13: turning is ACTIVE in `ModalUI`** — nothing modal reads the stick, and
  floating dialogs must not freeze diorama movement. If a future mode needs the
  stick, extend the mode check in `Rig/SnapTurn.cs` — do not read the stick
  concurrently.
- **World grab availability** (ARCHITECTURE §8, updated test #13): **every scenario
  mode, `ModalUI` included** — a floating dialog (story box, help box, ESC menu)
  must not freeze the table; grab/rotate/zoom stay usable while it is open. Object
  grabs near the floating window still win via the grip-contention rule above.
  **P5: disabled in `Menu2D`** (the menu rig gives Menu2D a rig root, but there is
  no table; the dev proxy stays exempt so grab math is testable flat).
- **P5 — menu rig:** while VR runs and NO scenario camera exists, `VRRigDriver` head-
  tracks the menu camera (`Camera.main`, `[Rig] MenuRig`, default true) at 1:1 scale.
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
the main plugin config is frozen P2 surface; `WorldScaleBase` *wraps* the existing
`[Rig] WorldScale` entry so the panel has one binding surface).

```csharp
ComfortSettings.IsBound                       // bool
ComfortSettings.AnyChanged                    // event Action<string /*key*/> — panel dirty-marking

// each of these is a ComfortSetting<T>:
//   .Value (get/set — set persists), .DefaultValue, .Reset()
//   .Section / .Key / .Description            (panel labels)
//   .Entry                                    (ConfigEntry<T> — acceptable ranges for sliders)
//   event Action<T> Changed                   (fired on ANY writer: code, panel, file)
```

| Accessor | Type | Default | Meaning |
|---|---|---|---|
| `WorldScaleBase` | float | 0 (auto) | Base diorama scale (wraps `[Rig] WorldScale`; rig rebuild applies it) |
| `WorldGrabEnabled` | bool | true | Master switch for grip world grab |
| `VerticalDrag` | bool | false | One-grip drag may move the table vertically |
| `RotateEnabled` | bool | true | Two-grip yaw rotation |
| `ScaleEnabled` | bool | true | Two-grip pinch scale |
| `ScaleMin` / `ScaleMax` | float | 0.5 / 4.0 | Pinch-scale clamps, × base WorldScale |
| `Turn` | `TurnMode` | Snap | `Off / Snap / Smooth` |
| `SnapTurnDegrees` | float | 45 | Degrees per snap step (15–90) |
| `SmoothTurnSpeed` | float | 90 | Smooth turn °/s |
| `TurnHand` | `TurnHandChoice` | Dominant | `Dominant / Left / Right` (Dominant = `[Hands] PrimaryHand`) |
| `FreeMovement` | bool | **true** | Test #10: no positional clamps at all — drag the table anywhere, only the (widened) scale limits remain. Off restores the pre-test-#10 comfort clamps |
| `TableHeightOffset` | float | 0 | Extra eye height above table on recenter, real m (−0.4…0.6) |
| `RecenterHoldSeconds` | float | 1.0 | B+Y both-hands hold time; 0 disables the chord |
| `SavedScaleMultiplier` | float | 1.0 | Auto-persisted pinch-scale (written after each two-grip gesture, re-applied on rig build) |
| `DebugGizmos` | bool | false | `[Comfort] DebugGizmos` overlay |

`TableHeightOffset` changes **re-run recenter immediately** — a panel slider gets live
feedback for free.

#### SUPERSEDED — accessors this table used to list (none exist at HEAD)

Kept, marked, rather than deleted: these names still appear in older commits and notes.

| Named here before | Status at HEAD | Replaced by |
|---|---|---|
| `SeatedMode` | **gone.** `ComfortSettings.cs` says so in as many words: "the old seated preset is GONE (user: irrelevant — the world is freely draggable)". Only the standing preset constants remain (`StandingEyeHeightMeters`/`StandingEyeBackMeters`, 0.70/0.70) | `FreeMovement` + `TableHeightOffset` |
| `VignetteEnabled`, `VignetteStrength` | **gone**, together with the whole `ComfortVignette` type (see §3) | nothing — no comfort vignette ships |

## 3. Runtime ops (panel actions / other modules)

```csharp
GloomhavenVR.Rig.Comfort.RequestRecenter();          // same path as the B+Y chord; safe no-op without a rig
GloomhavenVR.Rig.Comfort.SetScaleMultiplier(float);  // panel slider: scales around the HMD, clamps, persists
GloomhavenVR.Rig.VRRigDriver.BaseWorldScale          // float — resolved base scale, 0 while no rig
GloomhavenVR.Rig.WorldGrab.Instance?.IsGrabbing / .IsTwoHand / .CurrentMultiplier
GloomhavenVR.Rig.WorldGrab.Instance?.IsHandGrabbing(VRHand)  // is this hand's grip consumed by world grab?
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
| One grip (empty hand), move | Drag table on the horizontal plane (`VerticalDrag` opt-in). 1.5 cm deadzone, exponential smoothing |
| Two grips, move apart/together | Pinch-scale, clamped `ScaleMin..ScaleMax` × base; haptic `ClickPulse` detent on both hands every 25% step; multiplier auto-persists on release |
| Two grips, swing around each other | Yaw the table around the point between the hands (2.5° deadzone) |
| Stick flick left/right (turn hand) | Snap turn ±`SnapTurnDegrees` around the HMD (0.7 engage / 0.3 re-arm), or smooth turn |
| Hold B+Y (both hands) `RecenterHoldSeconds` | Recenter: HMD placed at the configured standing/seated spot at the table edge |

Recenter button choice: the frozen P2 hand API exposes only A/X (`PrimaryButton`) and
B/Y (`SecondaryButton`); the OpenXR menu/system button is runtime-reserved and not
surfaced. The **both-hands B+Y hold** chord keeps single presses free for future
features and is hard to fire accidentally.

## 5. Dev harness

- `[Dev] Enabled` + `[Dev] SimulateHands` (F8): the comfort stack runs without an HMD
  against a hidden **dev rig proxy** (`GloomhavenVR.DevRigProxy`, scale 12 — matches
  the sim hands scale). Hold **G** (both sim hands grip) → two-hand gesture; the
  procedural hand sway feeds rotate/scale. Observe via `[Comfort] DebugGizmos`.
- **F11** (dev mode, no HMD): recenter.
- `[Comfort] DebugGizmos`: bottom-left overlay — rig pos/yaw/scale multiplier, grab
  state and per-hand grip ownership, snap-turn arming + contention, clamp status,
  recenter-chord progress, seated/height/persisted values.

## 6. Threading & perf

Everything here is main-thread MonoBehaviour `Update` work; no Harmony patches were
added in Phase 4. No per-frame allocations on the always-on path (the `DebugGizmos`
overlay allocates strings while visible, same policy as the P2 dev overlay). Config
writes happen only at gesture end / explicit sets, never per frame.
