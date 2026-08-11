# Menu audit 04 — Board / FigureGrab / Hands / Net / stragglers

Auditor scope: FigureGrabConfig.cs, HandsConfig.cs, HexHighlightFix.cs, BoardConfig.cs, BoardModule.cs,
NetModule.cs, WristHud.cs, WorldUIModule.cs, ConfigCatalog.cs, plus the straggler sweep
(SelectionReadyHighlighter.cs, ActorBars.cs). Read-only; evidence is grep over full `src/`
(decompiled/ and tests/ excluded). Per-style families (`Glove/Plate/Arcane` × one key) are audited as
one row each — status/audience is identical across the three styles.

Curation reference: `src/GloomhavenVR/WorldUI/VROptionsTab.4.Curated.cs`.
Localized-name reference: `src/GloomhavenVR/Core/Loc.ConfigNames.cs`.

---

## src/GloomhavenVR/Board/FigureGrab/FigureGrabConfig.cs (file `dev.gloomhavenvr.figuregrab.cfg`, section [FigureGrab])

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| GrabFigures | ACTIVE | NORMAL | Gate read in FigureGrabbable.cs:329, FigureGrabDriver.cs:163; localized "Figuren greifen"; NOT yet curated |
| PickRadiusMillimeters | ACTIVE | NORMAL | Via PickRadiusRealMeters → FigureGrabDriver.cs:494; born from a user report ("versehentlich Figuren in die Hand"), player-worded loc name "Figur: Greifradius an der Hand (mm)" |
| StretchReachMillimeters | ACTIVE | POWER | Via StretchReachRealMeters → FigureStretch.cs:246; gesture capture radius, NO localized name (never menu-intended) |
| StretchScaleMin | ACTIVE | NORMAL | Confirmed (added 2026-08-11): via StretchScaleMinValue → FigureGrabbable.cs:698/731; localized "Figur: Mindestgröße in Hand" |
| StretchScaleMax | ACTIVE | NORMAL | Confirmed: via StretchScaleMaxValue → FigureGrabbable.cs:699/732; localized "Figur: Maximalgröße in Hand" |
| StretchLimits | ACTIVE | NORMAL | Confirmed: via StretchLimitsEnabled → FigureGrabbable.cs:696/724, FigureStretch.cs:272; the user-requested off switch, localized |
| HeldFigureInfo | ACTIVE | NORMAL | Confirmed: via HeldFigureInfoEnabled → StatPanelSurface.cs:236 + SettingChanged closes open panel; user-requested toggle, localized |
| HeldUpright | ACTIVE | NORMAL | Mode switch (deliberately global), read FigureGrabbable.cs:558/644; localized "Figur: aufrecht halten" |
| HeldUprightAtGrab | ACTIVE | NORMAL | Read FigureGrabbable.cs:601; one-shot upright-at-grab, localized "Figur: aufrecht greifen" |
| HeldScale | LEGACY-SEED | — | Read once as bind default of {Style}HeldScale (line 577) — and that successor is itself DEAD, so net effect none; marked "LEGACY — no effect" (menu drops it) |
| HeldOffsetForward / HeldOffsetUp / HeldOffsetSide | LEGACY-SEED | — | Read once each as bind default of the per-style successors (lines 560-567); marked "LEGACY — no effect" |
| HeldTiltDegrees / HeldFaceYawDegrees | LEGACY-SEED | — | Seed for {Style}HeldTiltDegrees/{Style}HeldFaceYawDegrees (themselves seeds); marked LEGACY |
| {Style}HeldOffsetSide / Up / Forward (×3 styles) | ACTIVE | POWER | Via ActiveHeldSide/Up/Forward → HeldOffsetFor → FigureGrabbable.cs:452/635, FigureGrabDriver.cs:488; m-scale pose calibration, live steppers |
| {Style}HeldTiltDegrees (×3) | LEGACY-SEED | — | Read once as bind default of {Style}HeldRotPitch (line 592); ActiveHeldTilt's fallback path is unreachable after Bind (RotPitch array always non-null) |
| {Style}HeldFaceYawDegrees (×3) | LEGACY-SEED | — | Seed of {Style}HeldRotYaw (line 596); same unreachable-fallback argument |
| {Style}HeldRollDegrees (×3) | LEGACY-SEED | — | Seed of {Style}HeldRotRoll (line 602); marked LEGACY at bind |
| {Style}HeldScale (×3) | DEAD | — | Marked "LEGACY — no effect"; only reader is retired accessor ActiveHeldScale, which has ZERO code callers (grep: only two comments in FigureGrabbable.cs). Held minis keep board size by user ruling |
| {Style}HeldRotPitch / RotYaw / RotRoll (×3 each) | ACTIVE | POWER | Via ActiveHeldTilt/FaceYaw/Roll → HeldUprightRotation/HeldPalmRotation → FigureGrabbable.cs:644-646; degree calibration, live |

## src/GloomhavenVR/Hands/HandsConfig.cs (file `dev.gloomhavenvr.hands.cfg`)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [Hands] TestFist | ACTIVE | POWER | Via TestFistActive → VRHand.cs:854/956/975; explicitly a DEBUG A/B lever |
| [Hands] CurlProximal / CurlMiddle / CurlTip | ACTIVE | POWER | Via FingerMaxAnglesSafe → FingerCurler.cs:162; per-joint fist angles |
| [Hands] CurlInputFullAt | ACTIVE | UNCERTAIN | Via CurlInputFullAtSafe → VRHand.cs:917; hardware grip-plateau remap, but arguably an accessibility/comfort dial (weak grip). See question list |
| [Hands] GlovePinkyCounterAbduction | ACTIVE | POWER | Via GlovePinkyCounterAbductionSafe → FingerCurler.cs:193; mesh-specific degree trim |
| [Hands] GhostHandOnFan | ACTIVE | NORMAL | HandGhost.cs:517; localized "Geisterhand bei Fächer"; MP-synced; not yet curated |
| [Hands] GhostHandOnHeldCard | ACTIVE | NORMAL | HandGhost.cs:501; localized "Geisterhand bei Karte"; not yet curated |
| [Hands] GhostHandStrength | ACTIVE | NORMAL | HandGhost.cs:535; localized "Geisterhand-Stärke"; not yet curated |
| [Hands] {Style}GripPitchDegrees (StyleSeatPitch, ×3) | ACTIVE | POWER | Via SeatPitchSafe → VRHand.cs:435 (SyncVisualOffset, every frame); Curated.cs itself rules these "calibration, not a choice" |
| [Hands] {Style}LateralOffset (×3) | ACTIVE | POWER | SeatLateralSafe → VRHand.cs:436 |
| [Hands] {Style}VerticalOffset (×3) | ACTIVE | POWER | SeatVerticalSafe → VRHand.cs:437 |
| [Hands] {Style}ForwardOffset (×3) | ACTIVE | POWER | SeatForwardSafe → VRHand.cs:438 |
| [Hands] {Style}GripRollDegrees (×3) | ACTIVE | POWER | SeatRollSafe → VRHand.cs:448 (mirrored left) |
| [Hands] {Style}GripYawDegrees (×3) | ACTIVE | POWER | SeatYawSafe → VRHand.cs:449 |
| [Hands] {Style}SpreadOffset (×3) | ACTIVE | POWER | SeatSpreadSafe → VRHand.cs:456 |
| [WristHud] {Style}PalmPitch / PalmYaw / PalmRoll (×3 each) | ACTIVE | POWER | WristHud.cs:166-177 (StyleGet, re-applied every Tick via ApplyPose:240); degree trims on the palm base |
| [WristHud] {Style}PalmSideOffset / PalmFingerOffset / PalmLiftOffset (×3 each) | ACTIVE | POWER | WristHud.cs:181-192 → ApplyPose:239; mm-scale plate offsets |

## src/GloomhavenVR/Board/HexHighlightFix.cs (file `dev.gloomhavenvr.hexhighlight.cfg`, section [HexHighlight])

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| SwapStableShader | ACTIVE | POWER | Read in-file :283; shader-swap master switch, ships correct — a player has no reason to touch it |
| StableZTest | ACTIVE | POWER | :363; CompareFunction int, on-device fallback knob |
| StableDepthBias | ACTIVE | POWER | :365; depth-buffer epsilon (0.0002) |
| KillBorderFlame | ACTIVE | POWER | :297; fallback path only (swap off/old bundle) |
| KillCrosshair | ACTIVE | POWER | :299; fallback path only |
| KillBorderLine | ACTIVE | POWER | :301; bisect/diagnosis knob |
| KillFill | ACTIVE | POWER | :303; diagnosis only, removes most of the highlight |
| LogMaterialDump | ACTIVE | POWER | :277; diagnostics logging |

## src/GloomhavenVR/Board/BoardConfig.cs (file `dev.gloomhavenvr.board.cfg`, section [Board])

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| ForceFarMode | DEAD | — | Marked "LEGACY — no longer used" at bind (:59); zero readers (grep: only comments in BoardModule.cs:160, BoardPick.cs:253 saying it is retired) |
| TouchTilesWithFingertip | ACTIVE | NORMAL | BoardPick.cs:257; ALREADY CURATED (Komfort ▸ Hände & Zielen) |
| TouchRange | ACTIVE | POWER | BoardPick.cs:299; meters threshold for near-pick takeover |
| SnapToHexCenter | ACTIVE | UNCERTAIN | BoardPick.cs:347; default false; understandable name ("Auf Hexmitte einrasten") but the effect (cursor/tooltip anchoring) is subtle. See question list |
| HoverHaptics | ACTIVE | NORMAL | TargetingUx.cs:67; haptics toggle, localized "Vibration bei Wechsel" |
| AoeFlickThreshold | ACTIVE | POWER | AoeControl.cs:76; stick deflection threshold |
| AoeRepeatInterval | ACTIVE | POWER | AoeControl.cs:86; timing, floor coupled to the game's 0.3 s direction latch |

## src/GloomhavenVR/Board/BoardModule.cs

No config entries of its own. Its three `.Bind(` sites (:68, :71, :76) are bind-once dispatch calls to
BoardConfig / FigureGrabConfig / SelectionReadyHighlighter — all audited elsewhere in this report.

## src/GloomhavenVR/Net/NetModule.cs (file `dev.gloomhavenvr.net.cfg`, section [Net])

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| Enabled | ACTIVE | NORMAL | NetModule.Init:162 (kill-switch, restart-scoped install); ALREADY CURATED (Multiplayer ▸ Präsenz) |
| MaskId | ACTIVE | NORMAL | LocalRigSampler.cs:129 + special dropdown row VROptionsTab.4.Curated.cs:455/482; ALREADY CURATED (Avatar + Multiplayer) |
| MaskSize | ACTIVE | NORMAL | LocalRigSampler.cs:139-141; synced; ALREADY CURATED (Avatar + Multiplayer) |
| NameTags | ACTIVE | NORMAL | RemoteNameTag.cs:164 (live every tick); ALREADY CURATED (Multiplayer) |
| MirrorEnabled | ACTIVE | NORMAL | AvatarMirror.cs:155; ALREADY CURATED (Avatar ▸ Spiegel) |
| VersionGuard | ACTIVE | UNCERTAIN | VersionGuard.cs:124; localized "Versionsabgleich" but it disables a safety dialog — net internals vs. legitimate player escape hatch. See question list |
| RemoteBoards | ACTIVE | NORMAL | RemoteBoardVisibility.cs:61 (single decision point); enum choice; ALREADY CURATED (Multiplayer) |

## src/GloomhavenVR/WorldUI/WristHud.cs

No config entries bound here. The six old global `[WorldUI] WristHud*` pose entries were removed from
this class (documented at :108-117 — "ONE DIAL, ONE OWNER") and marked LEGACY at their bind site in
WorldUIConfig (other auditor's scope). The live pose lives in HandsConfig `[WristHud]` per-style
arrays (audited above); the on/off toggle `[WorldUI] WristHud` is WorldUIConfig's (other auditor).

## src/GloomhavenVR/WorldUI/WorldUIModule.cs

No config entries of its own — :34 is the bind-once dispatch call to WorldUIConfig (other auditor).

## src/GloomhavenVR/WorldUI/ConfigCatalog.cs

No config entries of its own. Its `Bind("…", …)` lines (:242-258) are the EnsureBound dispatcher that
forces every module's bind-once so the browser shows the full surface — zero `config.Bind` entry
binds. (Context noted: retirement markers "LEGACY — no effect"/"RESERVED —"/"DEPRECATED —" drop an
entry from the menu automatically; the NotOffered table at :364 hides working-but-unreachable keys.)

## Stragglers found in the sweep

### src/GloomhavenVR/Board/SelectionReadyHighlighter.cs (file `dev.gloomhavenvr.selectionready.cfg`)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [SelectionReady] Enabled | ACTIVE | NORMAL | Bound :92, read in Tick gate (`_enabled is { Value: false }` :~138); localized "Auswahl-Erinnerung"; not yet curated |

### src/GloomhavenVR/WorldUI/ActorBars.cs (file `dev.gloomhavenvr.bars.cfg`)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [WorldUI] BarsOccluded | ACTIVE | NORMAL | Bound :260, read live every bar scan (:353 via property :275); localized "Balken hinter Wänden"; not curated — natural neighbor of the curated bar rows in Tafeln |

All other files with `.Bind(` hits (HandsModule, CardsModule, RigModule, ButtonCluster, RestControls,
PlayTray.1/6/7) contain only dispatch calls to configs owned by other auditors; ModuleConfig.cs,
Plugin.cs, SelectionReadyHighlighter aside, nothing binds entries outside the tables above.

---

## Summary

### DEAD

- `[FigureGrab] {Glove|Plate|Arcane}HeldScale` — marked "LEGACY — no effect"; sole reader is the retired accessor `FigureGrabConfig.ActiveHeldScale`, which has no code callers (grep hits are two comments in FigureGrabbable.cs). Held size is latched from the board by user ruling.
- `[Board] ForceFarMode` — marked "LEGACY — no longer used" at bind; no reader anywhere in src/ (BoardPick.cs:253 and BoardModule.cs:160 explicitly document its retirement).
- (Borderline) `[FigureGrab] HeldScale` — technically LEGACY-SEED, but it seeds only the dead {Style}HeldScale trio, so its value can never affect anything either.

All other legacy entries in scope are true LEGACY-SEED (read once as the bind default of a live
successor): HeldOffsetForward/Up/Side, HeldTiltDegrees, HeldFaceYawDegrees, and the per-style
{Style}HeldTiltDegrees/{Style}HeldFaceYawDegrees/{Style}HeldRollDegrees trios.

### NORMAL candidates

Already curated (confirm placement only):
- `[Board] TouchTilesWithFingertip` (Komfort ▸ Hände & Zielen)
- `[Net] Enabled`, `[Net] RemoteBoards`, `[Net] NameTags` (Multiplayer ▸ Präsenz)
- `[Net] MaskId` (dropdown special row), `[Net] MaskSize` (Avatar + Multiplayer), `[Net] MirrorEnabled` (Avatar ▸ Spiegel)

NOT yet curated — candidates for the everyday menu:
- `[FigureGrab] GrabFigures` — the feature master toggle; everything else in the section hangs off it
- `[FigureGrab] StretchLimits`, `StretchScaleMin`, `StretchScaleMax` — the brand-new user-requested size bounds (mission pre-classification confirmed)
- `[FigureGrab] HeldFigureInfo` — the brand-new user-requested info-panel toggle (confirmed)
- `[FigureGrab] HeldUpright`, `HeldUprightAtGrab` — understandable pose modes, localized player wording
- `[FigureGrab] PickRadiusMillimeters` — accidental-grab dial, written for players ("Lower it if you still pick figures up by accident")
- `[Hands] GhostHandOnFan`, `GhostHandOnHeldCard`, `GhostHandStrength` — visibility comfort trio, MP-synced, localized
- `[Board] HoverHaptics` — plain haptics toggle
- `[SelectionReady] Enabled` — gameplay-helper toggle ("Auswahl-Erinnerung")
- `[WorldUI] BarsOccluded` (ActorBars.cs) — belongs next to the curated bar rows in Tafeln

### UNCERTAIN (mit Fragen)

1. `[Board] SnapToHexCenter` — Soll "Auf Hexmitte einrasten" als normale Spieleroption erscheinen, oder bleibt es im Debug-Menü (es stabilisiert nur Cursor/Tooltip-Verankerung, Default ist aus)?
2. `[Net] VersionGuard` — Soll der Versionsabgleich-Schalter (blockierender MP-Dialog bei Build-Konflikt) ins normale Multiplayer-Menü, oder als Experten-Schalter nur ins Debug?
3. `[Hands] CurlInputFullAt` — Ist "Vollgriff ab Griffwert" eine Komfort-/Zugänglichkeitsoption für Spieler (schwacher Griff, Controller-Plateau), oder reine Hardware-Kalibrierung fürs Debug-Menü?
