# Menu audit 01 — `src/GloomhavenVR/Cards/CardsConfig.cs` (`[Cards]`, dev.gloomhavenvr.cards.cfg)

Scope: every bind in CardsConfig.Bind(). Per-board `*_Oak/_Steel/_Bronze` triples and per-pile
`*_Items/_Discard/_Burnt` triples are listed ONCE as `Key_*` (tuning families). 150 rows =
102 scalar keys + 44 per-board families (×3) + 2 per-pile families (×3) + 2 internal markers
→ 242 raw cfg keys.

Method: full-repo grep (src/ only, decompiled/ and tests/ excluded) for `CardsConfig.<Name>`
plus the internal helper properties (`RevealAlways`, `TrayOffset`, `SpawnSeatOffset`,
`ClampedTrayScale`, `EffectiveTrayPitch`, `BoardPitchWindow`, `CardHeight`) and string-keyed
references. "curated" = already in the everyday view (`WorldUI/VROptionsTab.4.Curated.cs`
vr_sec_cards / cat_panels). All 10 LEGACY rows carry the `LEGACY —` description prefix, which
`ConfigCatalog.IsRetired` already hides from the in-VR browser.

| Key | Status | Audience | Evidence/Note (one line) |
|---|---|---|---|
| DevFakeHand | ACTIVE | POWER | CardsDriver.6.Flows.cs:1935 — dev tool, gated on `[Dev] Enabled`; loc name literally "Debug: test cards" |
| RevealMode | ACTIVE | NORMAL | read via `RevealAlways` (CardsDriver.2.Update.cs:822,1019); **curated** (vr_o_revealmode, enum choices in ConfigCatalog:874) |
| RevealEnterDegrees | ACTIVE | UNCERTAIN | CardsDriver.2.Update.cs:989 feeds the roll gate; comfort threshold vs. fine-tuning — see question U2 |
| RevealExitDegrees | ACTIVE | UNCERTAIN | CardsDriver.2.Update.cs:990 (hysteresis); pairs with Enter — question U2 |
| FanRadius | ACTIVE | POWER | now ONLY the pile-fan base radius (PileBrowser.cs:404, ItemsPile.cs:1173); hand fan uses FanEffectiveRadius — note: both share the SAME loc label "Fächer: Radius (m)" |
| FanArcDegrees | LEGACY-SEED | — | 0 code reads; Defaults.Cards.cs:41 marks it seed of FanArcSweepDegrees (default ×1.3); hidden by IsRetired |
| FanPalmOffset | ACTIVE | POWER | CardFan.cs:808,853; EmptyFanHint.cs:184 |
| CardWidth | ACTIVE | NORMAL | 49 reads (+CardHeight 32) — THE card size everywhere; mission-class "card size in hand"; not yet curated |
| InspectScale | ACTIVE | NORMAL | VRCard.cs:1243, ItemsPile.cs:5842; **curated** (vr_o_inspectscale) |
| HeldTiltDegrees | LEGACY-SEED | — | 0 code reads (FigureGrab's same-named key is the live one); Defaults.Cards.cs:45 seed marker; hidden by IsRetired |
| HeldFaceBias | ACTIVE | POWER | VRCard.cs:1247, ItemsPile.cs:5848 — held-pose angle, needs the grip-pose mental model |
| HeldForward | ACTIVE | POWER | VRCard.cs:1263 — FALLBACK only (rig without finger joints) |
| HeldOffPalm | ACTIVE | POWER | VRCard.cs:1263 — FALLBACK only |
| HeldPinchOffset | ACTIVE | POWER | VRCard.cs:1277, ItemsPile.cs:5884 — mm Vector3 fine-tune |
| TrayForward | ACTIVE | POWER | grab-WRITTEN state (PlayTray.3.Pose.cs:567) + read via TrayOffset (PlayTray.1.Core.cs:1192) and HalfSelection/PileBrowser/ItemsPile; "edit only to reset" |
| TrayDown | ACTIVE | POWER | same persisted-state trio as TrayForward |
| TrayRight | ACTIVE | POWER | same persisted-state trio as TrayForward |
| BoardMoveMode | ACTIVE | NORMAL | 11 reads incl. EffectiveTrayPitch; **curated** as special localized dropdown (Curated.cs:441) |
| TrayPitch | ACTIVE | POWER | grab-written (PlayTray.3.Pose.cs:594), applied via EffectiveTrayPitch (PlayTray.1.Core.cs:1251) — state, not a setting |
| BoardPitchMinDegrees | LEGACY-SEED | — | 0 code reads; superseded by per-board BoardPitchMin_* (seeded from this default −45); hidden by IsRetired |
| BoardPitchMaxDegrees | LEGACY-SEED | — | 0 code reads; superseded by per-board BoardPitchMax_* (seed 45); hidden by IsRetired |
| TrayTilt | LEGACY-SEED | — | 0 code reads; Defaults.Cards.cs:53 seed marker for BoardTilt_* (30); hidden by IsRetired |
| TrayYaw | ACTIVE | POWER | grab-written (PlayTray.3.Pose.cs:580), read in pose (PlayTray.1.Core.cs:1249) — persisted state, "edit only to reset" |
| TrayScale | ACTIVE | NORMAL | gesture-written, read via ClampedTrayScale (PlayTray.1.Core.cs:1255); **curated** (vr_o_trayscale) |
| TrayFollow | ACTIVE | NORMAL | 17 reads (pin/follow logic); **curated** (vr_o_trayfollow) |
| SlotCardInset | ACTIVE | POWER | PlayTray.4.Slots.cs:53 — mm seating depth |
| WantedSlotHint | ACTIVE | NORMAL | CardsDriver.6.Flows.cs:75 — plain "glow on expected slot" toggle; not yet curated |
| CardDust | ACTIVE | UNCERTAIN | CardDustFx.cs:24 gate (emitters live, VRCard.cs:1723) — OFF by user ruling 2026-08-03; question U4 |
| GameCardParticles | ACTIVE | UNCERTAIN | Compat/CardParticlesOff.cs:168 — game particle suppression, OFF by same ruling; question U4 |
| BoardMinWidthMeters | ACTIVE | POWER | PlayTray.2.Watchdog.cs:387 — safety clamp, per-frame |
| BoardMaxWidthMeters | ACTIVE | POWER | PlayTray.2.Watchdog.cs:382 — safety clamp |
| SpawnLeftOfHead | ACTIVE | NORMAL | PlayTray.1.Core.cs:1191 picks SpawnSeatOffset vs TrayOffset; understandable "board starts on the left"; not yet curated |
| SpawnSideMeters | ACTIVE | POWER | via SpawnSeatOffset (PlayTray.1.Core.cs:1191) — cm placement fine-tune |
| SpawnForwardMeters | ACTIVE | POWER | via SpawnSeatOffset |
| SpawnDownMeters | ACTIVE | POWER | via SpawnSeatOffset |
| RoundButtonDiameter | LEGACY-SEED | — | 0 code reads; Defaults.Cards.cs:70 seed marker for RestButtonDiameter_*; hidden by IsRetired |
| RoundButtonThickness | LEGACY-SEED | — | only a comment ref (ButtonTuning.cs:71); seed of `[RestButtons] Depth`; hidden by IsRetired |
| RestButtonInsetX | LEGACY-SEED | — | 0 code reads; seed of RestButtonOffset_*.X (Defaults.Cards.cs:72); hidden by IsRetired |
| ConfirmUndoInsetX | LEGACY-SEED | — | 0 code reads; seed of ConfirmUndoOffset_*.X (Defaults.Cards.cs:73); hidden by IsRetired |
| CardLerpSpeed | ACTIVE | UNCERTAIN | VRCard.cs:2089 + ItemsPile (5 read sites) — one global "card flight speed"; question U3 |
| PileViewer | ACTIVE | NORMAL | PileViewer/PileBrowser gate (3 reads) — "show discard stacks" toggle; not yet curated |
| ActivePile | ACTIVE | NORMAL | ActivePileViewer gate (2 reads) — "active-cards column" toggle; not yet curated |
| FaceMipBake | ACTIVE | POWER | 4 readers (CardFaceMipBake.cs:275 …) — render-quality/perf switch, mod-internals knowledge |
| DissolveFloorFraction | ACTIVE | POWER | CardDissolveFloor.cs:85 — shader workaround dial; has NO Loc.ConfigNames entry (never meant for the menu) |
| Board | ACTIVE | NORMAL | prefab pick (VRCardFactory) + `CurrentBoard` drives all 51 per-board resolver reads; **curated** (control_board) |
| FanCurveByFill | ACTIVE | POWER | CardFan (5 reads) — Demeo-parity shape dial |
| FanMaxHandForCurve | ACTIVE | POWER | CardFan (8 reads) |
| FanFlatCurvatureFactor | ACTIVE | POWER | CardFan (6 reads) |
| FanTiltFactor | ACTIVE | POWER | CardFan (5 reads) |
| FanSplitMultiplier | ACTIVE | POWER | CardFan hover split (4 reads) |
| FanSplitFalloff | ACTIVE | POWER | CardFan (6 reads) |
| FanSelectedPopForward | ACTIVE | POWER | CardFan/VRCard (10 reads) |
| GrabButton | ACTIVE | NORMAL | ProximityGrabber.cs:141 — Trigger vs Grip, an input choice; **curated** (vr_o_grabbutton) |
| FanFollowSmoothing | ACTIVE | POWER | CardFan.cs:~830 follow ease |
| FanFollowDeadzone | ACTIVE | POWER | CardFan.cs:835 |
| RevealIgnoreWhenGrabbing | ACTIVE | POWER | CardsDriver.2.Update.cs:993 — behavior nuance, default sensible |
| FanOpenDuration | ACTIVE | POWER | CardFan reveal anim (5 reads) — timing curve |
| FanOpenStagger | ACTIVE | POWER | CardFan (4 reads) |
| FanCloseDuration | ACTIVE | POWER | CardFan (2 reads) |
| FanSwapDuration | ACTIVE | POWER | CardFan exchange region (3 reads) — animation tuning |
| FanSwapStagger | ACTIVE | POWER | CardFan exchange |
| FanSwapOverlap | ACTIVE | POWER | CardFan exchange |
| FanSwapTravel | ACTIVE | POWER | CardFan exchange |
| FanSwapArc | ACTIVE | POWER | CardFan exchange |
| FanSwapSpinDegrees | ACTIVE | POWER | CardFan exchange |
| FanSwapSeedScale | ACTIVE | POWER | CardFan exchange |
| FanSwapSettleOvershoot | ACTIVE | POWER | CardFan exchange |
| ItemFanOpenDuration | ACTIVE | POWER | ItemsPile emerge/collapse (3 reads); values sync to peers via extension record 28 |
| ItemFanOpenStagger | ACTIVE | POWER | ItemsPile |
| ItemFanOpenArc | ACTIVE | POWER | ItemsPile |
| ItemFanOpenSpinDegrees | ACTIVE | POWER | ItemsPile |
| ItemFanSeedScale | ACTIVE | POWER | ItemsPile |
| ItemFanSettleOvershoot | ACTIVE | POWER | ItemsPile |
| ItemFanCloseDuration | ACTIVE | POWER | ItemsPile |
| ItemFanCloseStagger | ACTIVE | POWER | ItemsPile |
| ItemCueBeatSeconds | ACTIVE | POWER | ItemsPile cue clock (5 reads) — FX rhythm tuning |
| ItemCueRingReach | ACTIVE | POWER | ItemsPile |
| ItemCueRingAlpha | ACTIVE | POWER | ItemsPile |
| ItemCueEmberRate | ACTIVE | POWER | ItemsPile |
| ItemCueEmberSize | ACTIVE | POWER | ItemsPile |
| ItemBerthRingThickness | ACTIVE | POWER | ItemsPile use-berth visuals |
| ItemBerthGlow | ACTIVE | POWER | ItemsPile |
| ItemBerthPingSeconds | ACTIVE | POWER | ItemsPile |
| ItemBerthPingReach | ACTIVE | POWER | ItemsPile |
| ItemBerthRevealSeconds | ACTIVE | POWER | ItemsPile |
| FanRevealSound | ACTIVE | UNCERTAIN | played on fan reveal (1 read site) — free-text audio-item string, "" = silent; question U1 |
| FanHideSound | ACTIVE | UNCERTAIN | fan hide (1 read); question U1 |
| CardGrabSound | ACTIVE | UNCERTAIN | grab (2 reads); question U1 |
| CardPlaceSound | ACTIVE | UNCERTAIN | silent-path placements only (7 refs, CardsDriver flows); question U1 |
| CardTakeBackSound | ACTIVE | UNCERTAIN | pick-reopen take-back (1 read); question U1 |
| FanPerCardStepDegrees | ACTIVE | POWER | CardFan.Relayout (11 refs) — geometry dial, in-VR debug "Fan" category |
| FanArcSweepDegrees | ACTIVE | POWER | CardFan.Relayout (11 refs); successor of FanArcDegrees |
| FanEffectiveRadius | ACTIVE | POWER | CardFan.Relayout (11 refs); supersedes FanRadius for the HAND fan only |
| FanHoverSplitScale | ACTIVE | POWER | CardFan.SplitOffset (7 refs) |
| BrowseFanOffset | ACTIVE | POWER | PileBrowser re-reads per frame (7 refs) — board-local Vector3 nudge |
| FanSideDepthCurve | ACTIVE | POWER | CardFan depth bow (7 refs) |
| FanCurvePower | ACTIVE | POWER | CardFan (5 refs) |
| FanCurveMinCards | ACTIVE | POWER | CardFan (5 refs) |
| FanGazeBias | SUSPECT | POWER | read (CardFan.cs:875) and functional, but its own description: "SUPERSEDED by FanFaceViewer + FanGazeApexFollow … kept for config compatibility"; default OFF opt-in flourish |
| FanFaceViewer | ACTIVE | POWER | CardFan card presentation (8 refs) |
| FanGazeApexFollow | ACTIVE | POWER | CardFan (9 refs) |
| FanGazeSmoothing | ACTIVE | POWER | CardFan (2 refs) |
| FanStepDegrees_* (Items/Discard/Burnt) | ACTIVE | POWER | per-PILE triple; PileBrowser/ItemsPile fan layout, read live (accessor FanStepDegrees(PileKind)) |
| FanRadiusFactor_* (Items/Discard/Burnt) | ACTIVE | POWER | per-PILE triple; PileBrowser.cs:404, ItemsPile.cs:1173 |
| RestButtonOffset_* | ACTIVE | POWER | per-board triple; RestControls.EnsureBuilt + Net/BoardTuning mirror |
| RestButtonDiameter_* | ACTIVE | POWER | per-board; RestControls.EnsureBuilt |
| ConfirmUndoOffset_* | ACTIVE | POWER | per-board; PlayTray.BuildButtons |
| ItemUseSlotOffset_* | ACTIVE | POWER | per-board; ItemsPile use-slot seat (8 refs) |
| ItemCardOffset_* | ACTIVE | POWER | per-board; ItemsPile.cs:114 (fan + held pose), live SettingChanged hooks |
| SlotOverlayOffset_* | ACTIVE | POWER | per-board; PlayTray.4.Slots + glows (9 refs) |
| SlotOverlaySpacing_* | ACTIVE | POWER | per-board; slot-pair spread (9 refs) |
| SlotOverlayScale_* | ACTIVE | POWER | per-board; PlayTray.4.Slots.cs:75 — sizes overlay AND resting card (retired SlotCardFill's successor) |
| InitiativeOffset_* | ACTIVE | POWER | per-board; initiative mount (7 refs) |
| DecisionGap_* | ACTIVE | POWER | per-board; decision text↔buttons gap (6 refs) |
| PickBannerOffset_* | ACTIVE | POWER | per-board; pick placard, also mirrored to remote boards |
| HoverHintOffset_* | ACTIVE | POWER | per-board; WorldTooltips.cs:1351 reads live every LateUpdate |
| BoardTilt_* | ACTIVE | POWER | per-board; PlayTray.1.Core.cs:1251 pose math |
| BoardPitchMin_* | ACTIVE | NORMAL | per-board; via BoardPitchWindow/EffectiveTrayPitch; **curated** by user request (Curated.cs:265, per-variant filter) |
| BoardPitchMax_* | ACTIVE | NORMAL | per-board; **curated** (Curated.cs:268) |
| BoardYaw_* | ACTIVE | POWER | per-board; PlayTray.1.Core.cs:1249 |
| BoardScale_* | ACTIVE | POWER | per-board; PlayTray.1.Core.cs:1255 (+0.4/0.5 migration quirk — do not harmonise, see CardsConfig.cs:1252) |
| AssetOffset_* | ACTIVE | POWER | per-board; board-mesh-only slide (6 refs) |
| AssetRotation_* | LEGACY-SEED | — | 0 code reads (no accessor); Defaults.Cards.cs:297 seed marker; NOT value-seeded per bind comment (stepper-accident values); hidden by IsRetired |
| AssetPitchDegrees_* | ACTIVE | POWER | per-board; mesh pitch (5 refs) |
| AssetYawDegrees_* | ACTIVE | POWER | per-board; mesh yaw |
| AssetRollDegrees_* | ACTIVE | POWER | per-board; mesh roll |
| BoardPosOffset_* | ACTIVE | POWER | per-board; PlayTray.1.Core.cs:1194 |
| RestButtonSpacing_* | ACTIVE | POWER | per-board; RestControls (6 refs) |
| GenericButtonSpacing_* | ACTIVE | POWER | per-board; PlayTray.BuildButtons (7 refs) |
| RestButtonShape_* | ACTIVE | POWER | per-board; Round/Square cap shape (6 refs) |
| GenericButtonShape_* | ACTIVE | POWER | per-board; (6 refs) |
| ActiveOffset_* | ACTIVE | POWER | per-board; ActivePileViewer mount (6 refs) |
| ActiveCardScale_* | ACTIVE | POWER | per-board; ActivePileViewer (6 refs) |
| ActiveGridSpacing_* | ACTIVE | POWER | per-board; ActivePileViewer.cs:189 |
| PileOffset_* | ACTIVE | POWER | per-board; pile mount (6 refs) |
| PileScale_* | ACTIVE | POWER | per-board; (5 refs) |
| PileSpacing_* | ACTIVE | POWER | per-board; (6 refs) |
| ObjectivesOffset_* | ACTIVE | POWER | per-board; objectives dock mount (7 refs) |
| ObjectivesScale_* | ACTIVE | POWER | per-board; dock zoom (6 refs) |
| ObjectivesWidth_* | ACTIVE | POWER | per-board; TablePanelSurfaces.cs:1492 wrap column |
| ElementsOffset_* | ACTIVE | POWER | per-board; elements dock (6 refs) |
| ElementsScale_* | ACTIVE | POWER | per-board; (6 refs) |
| PinOffset_* | ACTIVE | POWER | per-board; follow/pin button (6 refs) |
| ReadoutOffset_* | ACTIVE | POWER | per-board; round readout (6 refs) |
| ClusterOffset_* | ACTIVE | POWER | per-board; button cluster (9 refs) |
| ClusterScale_* | ACTIVE | POWER | per-board; (6 refs) |
| DecisionOffset_* | ACTIVE | POWER | per-board; decision dock mount (7 refs; Y live since ModBuild 90 rebase) |
| DecisionScale_* | ACTIVE | POWER | per-board; (6 refs) |
| BoardScaleDefault04Applied | ACTIVE | — (hide) | internal one-shot migration marker, read/written only in Bind() (CardsConfig.cs:1266); "Do not edit" |
| DecisionOffsetYRebased | ACTIVE | — (hide) | internal one-shot migration marker (CardsConfig.cs:1301); "Do not edit" |

## DEAD

**None.** Every non-legacy entry has a live runtime reader (verified by full-src grep, incl. the
helper-property indirections). The 10 self-declared `LEGACY —` rows above are the closest thing;
they are classified LEGACY-SEED because the codebase marks them as the seeds their successors'
defaults were derived from (Defaults.Cards.cs:41–73, 297–299) and they are kept bound purely so
existing cfg files load unchanged. All 10 are already invisible in-VR (ConfigCatalog.IsRetired
matches the `LEGACY —` description prefix). Also of note: `SlotCardFill`, `DebugMenu` and
`ConfirmUndoSize_*` are already fully retired (unbound tombstones in CardsConfig — no action
needed). `CardShapeMask.Enabled` is hardcoded false but reads NO CardsConfig entry, so it kills
no dial here.

## NORMAL candidates

Already curated (WorldUI/VROptionsTab.4.Curated.cs, section vr_sec_cards):
- **Board** — control-board model choice.
- **TrayScale** — board size (also gesture-written).
- **TrayFollow** — board follows you / pinned.
- **BoardMoveMode** — movement scheme (special localized dropdown).
- **BoardPitchMin_\*/BoardPitchMax_\*** — pitch window, curated per explicit user request with per-variant filtering.
- **InspectScale** — close-up card size.
- **RevealMode** — fan opens by gesture/always.
- **GrabButton** — Trigger vs Grip.

Not yet curated — recommend promoting:
- **CardWidth** — the card size in hand/fan; the single most player-visible size dial in the section (49 read sites).
- **PileViewer** — show/hide the discard/burnt pile stacks (plain feature toggle).
- **ActivePile** — show/hide the active-cards column (plain feature toggle).
- **WantedSlotHint** — glow on the expected slot (plain visual toggle).
- **SpawnLeftOfHead** — board starts beside your head on the left (comprehensible comfort choice; its three fine-tune distances Spawn*Meters stay POWER).

## UNCERTAIN (German questions for the user)

- **U1 — Sounds** (FanRevealSound, FanHideSound, CardGrabSound, CardPlaceSound, CardTakeBackSound): Sollen die fünf Karten-Sound-Einträge (freie Audio-Item-Strings, leer = stumm) im Alltagsmenü stehen, oder genügt dort ein einfacher An/Aus-Schalter und die konkrete Sound-Auswahl bleibt im Power-/Debug-Bereich?
- **U2 — Fächer-Gestenschwellen** (RevealEnterDegrees, RevealExitDegrees): Sind die Öffnen-/Schließen-Winkel der Handgelenks-Geste eine Komfort-Einstellung für jeden Spieler (direkt neben RevealMode) oder Feintuning fürs Debug-Menü?
- **U3 — Kartenflug-Tempo** (CardLerpSpeed): Soll das globale Kartenflug-Tempo eine Alltagsoption sein oder als Animations-Feintuning im Power-Bereich bleiben?
- **U4 — Abgeschaltete Partikel** (CardDust, GameCardParticles): Beide sind seit deinem Ruling vom 2026-08-03 aus, weil der Effekt über das ganze Brett sprüht — sollen die zwei Schalter für normale Spieler sichtbar bleiben oder nur unter Power/Debug erreichbar sein?
