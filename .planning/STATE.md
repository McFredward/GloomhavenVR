# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop, multiplayer-capable.** Current build:
  **`NetProtocol.ModBuild = 127`**, awaiting its hardware run (MP test still outstanding). Rounds are run as parallel agents on
  disjoint file sets; every diff reviewed before merge, cross-file changes applied by the integrator.
- **Last update:** 2026-08-12

---

## 0. READ THIS FIRST (successor briefing)

This file is the handover. Everything below is written for someone who has never seen the
project before and has to be productive in the first ten minutes.

### The one-paragraph description

A BepInEx 5 + HarmonyX mod that turns *Gloomhaven Digital* (Unity 2021.3.5f1, Mono, net472)
into a room-scale VR game on a Quest 3 over Virtual Desktop (VDXR), MultiPass stereo, 90 Hz
(11.1 ms). The board becomes a physical diorama on a table; the player's hand holds a real
card fan; the game's own 2D UI is *converted* into world-space panels docked on a wooden
control board. **Multiplayer is supported and is a first-class constraint, not an
afterthought** (see §3).

### The user

Frederik (`frederik@lissek.info`). **Always answer in German.** Worker/agent prompts may be
English. He tests on real hardware every round and reports numbered findings; each round is
"read the reports → dispatch workers → review their diffs → merge → bump → push → write him a
German report".

### Where the truth lives

| Question | Authority |
|---|---|
| What is patched, by whom | `docs/PATCH-INVENTORY.md` (**generated** — `scripts/patch-inventory.sh generate`) |
| Why a patch exists, what it costs | `docs/PATCH-NOTES.md` (hand prose, not checked) |
| Architecture, module boundaries | `.planning/ARCHITECTURE.md` |
| Refactor rules, invariants | `.planning/refactor/CHARTER.md`, `INVARIANTS-*.md` |
| Frame-order contracts | `.planning/refactor/FRAME-ORDER.lock` |
| Performance measurements | `.planning/perf/FINDINGS.md`, `.planning/perf-zoomed-out.md` |
| Multiplayer design | `.planning/multiplayer/PLAN.md`, `PLAN2.md`, `RESEARCH.md` |
| Parked/abandoned work | `.planning/static-batching-removed.md`, `.planning/wall-fade-stereo-rivalry.md` |
| Anything user-facing | `src/GloomhavenVR/Core/Loc.cs` (+ `Loc.Config*.cs`) |
| Every shipped default | `src/GloomhavenVR/Defaults/*.cs`, one annotated line each |

**The code is the documentation.** This project deliberately writes long block comments that
state the user report verbatim, the root cause, the evidence and the rejected alternatives.
When you change something, write that comment — several rounds have been saved by a comment
that said "do NOT swap this back, here is why".

---

## 1. Hard rules (violating any of these is a defect, not a style choice)

1. **Mod-side only.** Never modify game data. `ressources/` is a READ-ONLY symlink to the
   installed game (`Managed/*.dll`). Never `git add` it or `libs/`. Decompiled reference
   sources are under `decompiled/` (and `decompiled-demeo/`), also never edited.
2. **`NetProtocol.ModBuild` +1 on every build handed to another player.** It is the
   multiplayer handshake key; mismatched peers get a blocking dialog. Write a full build note
   next to it — the note history in `NetProtocol.cs` is the project's real changelog.
3. **Wire:** magic `GVR1`, `Version` byte stays **3**, all changes **additive TLV** only, old
   readers skip unknown records by length. **Card identity NEVER goes on the wire, ever** —
   reveals go only through `Net/RevealGate.cs`. Records 1–17 and 22–29 are used; 30+ free. The
   extras flag byte is FULL and record 4 has no free bits: claim a NEW record rather than
   squeezing (the reservation comment in `NetProtocol.cs` records a past double-claim
   incident).
4. **Lights are never hidden or written by any visibility system. Figures are never touched by
   any wall/visibility system.**
5. **Everything that fades or moves does so WITH the animation.** Popping is unacceptable.
   Exception granted by the user for one case only: the decision area's *collapse* is instant.
6. **The user's approved look outranks geometric or technical correctness.**
7. **Localisation:** every user-facing string via `Core/Loc.cs`, German **and** English.
8. **Config:** every default on one annotated line in `src/GloomhavenVR/Defaults/` with a
   `// => [Section] Key` comment. A new KEY must end in a unit word `ConfigSteps` recognises —
   an unrecognised suffix silently falls back to "a fiftieth of the shipped default" and
   produces an unusable stepper. This has been user-reported **twice**; there is now a test
   (`tests/GloomhavenVR.WireTests/ConfigStepVectors.cs`) that pins it.
9. **Never mention remaining context or advise a fresh session** (the readout is a harness
   bug). Do not talk about token budgets.

## 2. The five gates — every change must pass all of them

```
./scripts/build.sh                        # 0 errors (6 pre-existing nullability warnings are OK)
bash scripts/wire-tests.sh                # currently 1441 assertions
./scripts/refactor-guard.sh check --summary
python3 scripts/rebase-defaults.py check
python3 scripts/check-wire-coverage.py
```

Reading `refactor-guard`: only the **five gate lines** matter (patch surface, frame order,
mirrors, remote defaults, bundle). Its **exit code is 1 whenever the compiled form differs
from the stored baseline**, which is normal after any change — that is a report, not a gate.
The baseline lives in gitignored `.planning/refactor/.guard/` and can be regenerated from a
clean tree with `./scripts/refactor-guard.sh baseline`.

`refactor-guard` hard-fails if `docs/PATCH-INVENTORY.md` is stale. Fix with
`bash scripts/patch-inventory.sh generate`.

**Bundles are built ONLY with `/home/claw/unity-2021.3.5`** — not `unity-2021.3`. The wrong
editor produces a bundle that silently loads nothing.

## 3. The multiplayer 1:1 ruling (user, verbatim, standing)

> "generell gilt die Regel, das man alle Interaktionen, Animationen und Anzeigen des
> Controllboards in MP auch synchronisieren soll. Die einzige Ausnahme ist hier die geheime
> Quest des characters und während der Auswahlphase die tatsächlichen Oberseiten der Karten.
> Ansonsten soll alles 1:1 übertragen werden."

Narrowed and extended since:
- Card **fronts** of a remote player are visible in every phase **except** card selection.
- The **initiative NUMBER** stays masked to "?" during selection.
- The personal quest / battle goal is never mirrored.
- **The tuning guarantee:** "Ändert ein Spieler also die Positionen für sich selber, so sollen
  alle anderen diese Position bei seinem board auch sehen." Board-affecting dials therefore
  ride **record 28**, which is range-paged (a full lap is a snapshot; convergence ≤ 400 ms at
  two pages). `scripts/check-wire-coverage.py` fails the build when a board-affecting dial has
  neither a record-28 field nor an annotated EXEMPT.

**The trap to avoid** (it has been hit): a wire field with *no consumer*. `check-wire-coverage`
would call it covered while the peer still sees no difference. See the `[Cards]
FanCloseDuration` note in that script.

## 4. Where the project stands — recent rounds

Newest first. Each entry names the *root cause*, because that is what generalises.

- **ModBuild 127** — environments are SCENARIO-ONLY and built from the game's own art.
  SCOPE (user ruling: "Ich WILL garnicht das die Umgebung im Menu rendert - sondern nur im
  Szenario"): SkyAlternative gates on VRModeStateMachine.ScenarioBoardExists (Choreographer-
  alive, NOT the save-state phase which flips during loading); menu/world map untouched, leaving
  a scenario stands down next tick + cancels in-flight generation. GAME-BUILT ROOMS
  (SkyAlternative.MapGen.cs, new): Addressables 'Map A' (session-cached handle) staged 50 wu
  below the viewpoint in the ProcGen scene, ambience muted FIRST, styles written (Cellar =
  Dungeon/StoneRooms/Candlelight; SwampNight = Forest/Marsh/StillWaters/ForestMoonlight), walls
  populated, detail focus BORROWED onto the staging map for the ≤30 s build window (staging is
  outside detail range — round-3 evidence, not conditional), settle poll = renderer census
  stable + no busy entities (30 s timeout → FX shell only, one-shot warn), freeze = disable
  Apparance COMPONENTS never GameObjects (inactive entities destroy their native side), strip
  colliders, mod layer, normalize to ~9 real meters (n = 9/max(bounds.xz) at staging scale 1,
  clamp 0.02–10), floor = avg ProceduralMapTile height, center → frame origin (player inside).
  GameEnvProbe RETIRED (923 lines — mission complete; logic lives in MapGen). FX SHELLS rebuilt
  from own shaders, third-party low-poly art DELETED (folder 4.2 MB → 980 KB; bundle 29,639,808
  bytes — 127 NEEDS it): Swamp = star dome (baked moon+twinkle) + shooting stars (Stretch,
  cameraVelocityScale=0) + fireflies + ground fog as HORIZONTAL billboards (user fog ruling:
  never re-orient with head movement — PERMANENT CONSTRAINT in BuildEnvironments.cs); Cellar =
  dust motes + disabled GlowTemplate. UNVERIFIED ON HARDWARE: whether disabled components fully
  stop native re-tiering after placement; the 9 m normalization target may want tuning. First
  scenario start with a style selected is the real test — the engine dresses live, log records.
- **ModBuild 126** — environments become WORLD PLACES, the game-asset probe, ally banner round 3.
  FREE MOVEMENT: all locomotion writes RigRoot (Flight 213, SnapTurn 162, WorldGrab 304/362/401/
  403); the env now spawns world-anchored at the player's floor point + gaze yaw, re-seats on
  RigPoseVersion bumps, and mirrors rig-scale writes around the SAME pivot (NotifyRigScaled from
  WorldGrab/Comfort; algebraic no-drift proof in SkyAlternative) — fly/turn/drag/walk move
  THROUGH the room, zoom never changes its perceived size. STYLE PLAN: user wants Gloomhaven's
  own style, ideally game assets — investigation (.planning/game-env-assets.md) picked APPARANCE
  MICRO-GENERATION (Map A prefab + style enums Dungeon/StoneRooms/Candlelight resp. Forest/
  Marsh/StillWaters/ForestMoonlight); GameEnvProbe ships in 126: passive catalog/engine dumps
  every session + one-shot [Sky] EnvProbe menu experiment (detail-focus pointed at the probe map
  — distance-scaled synthesis would fake 'never built'; idempotent cleanup) → ENV PROBE VERDICT
  decides B vs scenario-time capture (C). Low-poly envs stay as placeholders. ALLY BANNER R3:
  round-2 lift WORKED (log 529); remaining occluder = WORLD DEPTH (the board's raised wooden
  rail in front of the canvas plane where the banner reaches up). Fix: OnTopUiGraphics — every
  Graphic in a SHOWN popup swaps to a session-cached ZTest-Always clone of its own material
  (ActorBars mechanism inverted, TMP via fontSharedMaterial, CardEffects subtrees excluded,
  reference-checked restore). NEXT after his log: read ENV PROBE VERDICT → build the styled
  environments via the winning approach.
- **ModBuild 125** — 3D ENVIRONMENTS (panoramas rejected + removed), mouse round 2 + INCIDENT
  RESTORE, ally banner. [Sky] Style = Default/Cellar/SwampNight ("Umgebung"): two bundled
  prefabs from CC0 Quaternius packs (Env_Cellar 53k tris — stone room, torches, chests, stairs
  into darkness; Env_Swamp 29k tris — 2200-star dome w/ baked moon + twinkle shader, moon-glint
  water, fog banks, fireflies, glowing mushrooms, shooting stars), ALL world-anchored (user
  rule), no bundle scripts, spawned under RigRoot at identity (stands still in real space,
  perceived-size-constant under zoom, zero per-frame writes), MR on = always off. Preview
  renders reviewed by builder AND integrator before shipping (6 iterations; renders in
  scratchpad env-previews/). Bundle rebuilt 30,060,345 bytes (panoramas deleted) — 125 NEEDS it.
  MOUSE: OS cursor hidden per frame (WarpCursorPosition moved the real cursor); virtual pointer
  parks at (-4096,-4096) unless the laser actively drives it. INCIDENT: the 8594ebd merge had
  silently reverted 123's MouseWorldSurfaceCut + grip fall-through (stale-base diff) — restored;
  hazard recorded in memory (restrict merge patches to owned paths; grep prior lanes' markers).
  ALLY BANNER: 'VERBÜNDETER' clip closed (branch lift + ancestor-chain flatten, both occluders).
- **ModBuild 124** — SKY ALTERNATIVES. [Sky] Style (Default/Night/Sunset/Cellar; DE Standard/
  Sternenhimmel/Abendrot/Gewölbekeller), curated in Grafik ▸ Darstellung, localized dropdown,
  live-switchable. Three CC0 Poly Haven panoramas (4096x2048) + GloomhavenVR/SkyPanoramic
  (view-direction equirect, non-occluding by construction) in the REBUILT 39 MB bundle
  (unity-2021.3.5; prebuilt/ updated — 124 NEEDS this bundle, old one = one-shot warn + game
  sky untouched). Runtime: SkyAlternative hides GH_SkySphere (renderer.enabled=false, the safe
  half of SkyBackdrop's trap), shows a mod-layer head-following inverted sphere; lazy bundle
  load, session-kept textures. MR precedence hard-wired: MR on stands the alternative down
  before the chroma sweep; dial re-applies on MR off. Local-only, nothing on the wire.
- **ModBuild 123** — the seven-item round after the border victory. MOUSE DEAD: game EventSystem
  pointer swept world UI with the head; MouseWorldSurfaceCut strips game-pointer hits on
  world-space root canvases at the shared source (EventSystem.RaycastAll postfix; mod pointer
  ids pass). GRIP FALL-THROUGH: trigger-only highlight no longer eats the grip — nearest
  GrabWithGrip target re-elected (tray bar beside a highlighted card). MENU: sliders show live
  values (donor caption + deferred-Destroy rebind bug), dependency layer folds children under
  parents (VROptionsTab.8), SyncPeerFades → Avatar & Mehrspieler. ELEVEN always-on dials removed
  (danger audit: PileViewer, ActivePile, Master, FlatScreen+AutoShow, UseBars, DoomPicker,
  DistributePanel, CatchAllModals, MenuPopupFloat, ManualScreenChord); three borderline
  questions queued for the user (ForceMouseMode, ClickLatch, the two map-fix diagnostics).
  HELD FIGURE: auto-release is PER-FIGURE (own bar/named wait/own turn/own animator leaves
  idle — Choreographer.IdleStates vocabulary, same predicate as the grab gate), global flows no
  longer end a hold, and every forced release takes the normal 0.28 s glide (instant-when-busy
  branch removed; authoritative-move release stays instant). Deadlock-safety argument in
  FigureBusy docs; watchdog stays.
- **ModBuild 122** — the border saga CLOSED (user on 121: "Großer Erfolg!"). Bottom fix: the
  contour derived from the v5 footprint (punched pixels ∩ frame-era CardOutline bands) — body
  stopped higher than the stock art draws. Capture now stamps the STOCK art's own alpha only
  (shadow trim / dark-border peel / OUTLINE CLIP deleted); CacheVersion 5→6 relearn; body and
  face coincide by construction. THE GREAT CLEANUP: net −7,200 LOC — deleted CardShapeMask (incl.
  Net call sites), CardDissolveFloor+dial, CardShaderProbe, CardBandPainter, CardBandPixelCapture,
  CardFaceCrop, CardOutline, punch/crop factories in CardFaceMipBake, FramePunch sweep + band
  diagnostics, cutout material bake, inert [Cards] GrabButton (+ dead ProximityGrabber branch +
  menu rows). KEPT: CardContour+AttachBody, stock-alpha capture v6, mip bake, emission floor,
  umber EdgeColor, SlotSeatLiner+mirror, FaceBlackout (stock-path). Tab captions wrap (explicit
  '&' breaks DE/EN; Erweitert chooser same). Saga history: build notes 105-121 + git. MP test of
  the shaped bodies still outstanding.
- **ModBuild 121** — border attempt 17: THE USER'S OWN DESIGN. His ruling: face must render
  completely correct again; the MESH must be punched out to the surface outline. (His "120" log
  was really a 119 run — the 120 deciders never executed; geometry moots the shader question.)
  ORDER A: punched/cropped sprite serving retired at the mint (PunchServingEnabled=false,
  CardFaceCrop.Enabled=false, restores via existing contracts) — stock art everywhere incl. the
  designed printed frame; the broken bottom dies with its cause; FramePunch sweep = outline
  derivation only; capture's OUTLINE CLIP now load-bearing. ORDER B: CardContour (marching
  squares 0.5 iso → closed-loop DP ≤120 verts → ear-clip front / mirrored back / extruded rim,
  bounds pinned to full box) + CardMesh.AttachBody serving shaped meshes to every registered
  body on contour-learn (rounded until then; warm cache = shaped from first draw); cutout
  baking retired (CutoutMaterialsEnabled=false); metrics consumers verified box-only. ALL SIX
  mirrors adopted AttachBody same build (RemoteHandFan/ItemFan/BrowserFan/CardFx/Avatar +
  AvatarMirror; two submeshes → back material twice; mirrors never destroy the shared cached
  meshes). If a band still shows on a seated tray card it can only be the deliberate LINER wood.
- **ModBuild 120** — border attempt 16: THE DIFFERENTIAL (instrumentation only, deliberately).
  119's partial success proved the painter chain (band = slab umber+light, opaque). The paradox:
  cutout texture alpha provably transparent in the bands + material state provably correct, yet
  the band renders. Code audit exonerates all local writers (emission floor touches only
  emission; _Cutoff 0.5; ConfigureCutout authoritative last on every path) → chief suspect: the
  game build's Standard shader may lack the _ALPHATEST_ON variant (EnableKeyword is a request;
  stripped variant never clips → slab draws its full envelope in EdgeColor = the measured band).
  Shipped deciders: CARD BAND DIFF (re-render with exactly one candidate suppressed per pass —
  Backing / edge submesh / liner / face canvas — per-strip verdict names the painter), CARD BAND
  TEX-VS-RENDER (live GPU texture alpha via Blit readback beside at-capture material state),
  CUTOUT CLIP PROBE (live edge material, UVs pinned to a verified alpha-0 texel → "clip
  EXECUTES" / "DOES NOT EXECUTE (variant stripped)"). If DOES NOT EXECUTE: fix lane = bundled
  clip-capable shader (BoardLit + clip(), bundle rebuild with /home/claw/unity-2021.3.5) or true
  mesh trimming to the outline. NO other changes in 120.
- **ModBuild 119** — border attempt 15: THE LIGHTING CONVICTION. Fourteen albedo/texture rounds
  were invisible because the card slab renders through stock 'Standard' in DARK scenes — albedo
  × ~0 light = black regardless of what we painted (karten4 band (4,4,3) ≈ 16 % of even the old
  EdgeColor's lit value). BoardLit (the board's own ambient-floored shader) provably cannot clip
  (bundle source: alpha hard-coded 1.0, no clip()) → fix is an EMISSION FLOOR
  (CardMesh.ApplyEmissionFloor: _EmissionMap = own albedo, tint × 1.0) on the shared card-body
  pairs + baked cutout textures; Net mirrors borrow the same instances → peers fixed free. Same
  floor on every Standard near-card surface (fallback board BoardLit-first, ItemsPile slab,
  PileViewer slabs, fallback caps). The 118 capture was BLIND (mask copied from game
  ScenarioCamera which excludes mod layer 27) → now HeadCamera mask ∪ card-subtree layers,
  centre-canary retry ~0, honest INSTRUMENT FAILURE verdict, and a mid-height RLE SCAN line
  (width + face-x + RGB per run) in every CARD BAND PIXELS log. Known caveat: a stripped
  _EMISSION shader variant would silently no-op — the scan line is the arbiter next run.
- **ModBuild 118** — border attempt 14 (two convictions + the pixel instrument), mirror ghost
  hands, trigger-only cards. THE BORDER: 117's liner was RIGHT in kind, WRONG in units — sized
  off the BASE card box while the seated card renders at the live SlotOverlayScale dial (~1.7 on
  his rig): ~1.10× coverage vs a ~1.30–1.40× well → "unverändert". Now seated-card ×
  SlotLinerSeatRatio (1.45) from the same dial, z from the tuning chain, on BOTH wire sides
  (RemoteBoardFurniture takes field 171 into the product; SlotLinerScale const deleted). Second
  conviction: the thin NEUTRAL near-black fan ring is CardMesh.EdgeColor (0.10,0.09,0.08) baked
  into every silhouette-surviving Edge texel → retinted warm umber. CardBandPixelCapture (new)
  renders the real card in situ at latch/re-arm and classifies band-strip means against known
  signatures — the remaining maroon PRINT band (91/264 punched-header + 27–28/36 unpunched
  action-default probes in the 117 log) gets convicted per placement in the next log. MIRROR:
  AvatarMirror read legacy single-side HandGhosts.LocalSide → now LocalLeft/Right per side
  (peers were already correct). TRIGGER-ONLY: grip proximity fallback removed for cards+figures
  (named throttled refusal); [Cards] GrabButton now INERT — retirement candidate next purge.
- **ModBuild 117** — the border SOLVED (attempt 13), the dead-dial purge, the menu restructure.
  THE BORDER: 116's CARD BAND PAINTER acquitted every card layer on the user's own rig while his
  screenshot showed the band — so the reach was wrong. Convicted: **the bundled tray's authored
  AO-dark RECESS FLOOR**, an ancestor mesh ~1.2x the card no card-root sweep could see; pixel
  proof warm-vs-cool (band r−b +7..+21 = tray wood; card layers −11..−18). Punch/crop/silhouette
  worked for rounds — every erased pixel just rendered recess-black, incl. the bottom "kaputt"
  (kept bright ornaments floating on black). The 116 log's 132 % slab spans were a world-AABB
  measurement artifact (tilted cards); the 111 "0.94 coincide" finding stands. Fix =
  **SlotSeatLiner** (opaque rounded CardMesh slab 1.78x card box, keycap grain wood, per recess;
  mirrored 1:1 on peer boards). Diagnostic upgraded: local-tight rects, subtree sweep with
  backdrop verdicts, throttled re-arm — a hand-fan residue would be NAMED in the next log.
  DEAD-DIAL PURGE: 48 keys removed (per-key re-verified; kept ScrollWithStickOnly — live reader
  UguiPointer.cs:505, the audit was wrong there — Experimental3DMap, WorldTilt family);
  ScreenLeftMirrorFallback description fixed (it gates the black-map probe). MENU RESTRUCTURE
  (all 20 integrator recommendations ruled in by the user): everyday tabs Komfort / Grafik /
  Brett & Karten / Tafeln / Avatar & Mehrspieler / Erweitert (ex-Debug); ~30 promotions;
  hand-built trees for the two oversized topics (VROptionsTab.7.TopicTrees.cs); GroupWordLabel
  localization; [Cards] CardSoundsEnabled master switch. Audit basis in `.planning/menu-audit/`.
  Wire tests now 1509.
- **ModBuild 116** — three lanes. BORDER ATTEMPT TWELVE: the 115 probe EXONERATED the card shader
  (verdict (a): alpha honored at rest) — eleven rounds edited layers that could not be painting the
  band. Convicted in source instead: **`FullAbilityCard.unfocusedMask`** — ShowCard loads the SAME
  background sprite into headerImage AND this full-card dimming copy at its own slightly different
  rect (the 115 two-placement warning), toggled by SetUnfocused, ACTIVE IN MULTIPLAYER for every
  presented character not under local control — and served the header-rect punch copy it erased
  ~2 % into the card bottom (the 115 "unterer Teil kaputt" regression). Fix: the mask is a sixth
  named plate (punched + cropped against ITS OWN rect); per-placement punch copies keyed
  (source, mapping); dissolve floor default 0 (probe: cures nothing; _Dissolve_VerticalGradient
  discards bottom-first — the other bottom suspect); NEW CardBandPainter latches a complete painter
  inventory (Graphics + Renderers + rest-output GPU probe per custom shader, incl. the never-probed
  120 %-of-face CardEffects fgFx 'UIFX_Overlay'). If the band survives 116: **read CARD BAND
  PAINTER — it names the painter.** STRETCH BOUNDS + INFO TOGGLE (items 2+3): total-based
  Min/Max with grab-time clamp, [FigureGrab] StretchLimits off-switch, [FigureGrab] HeldFigureInfo
  gating StatPanelSurface.ShowHeldFigure at the registration seam; all in VR settings, DE/EN.
  ALSO RUNNING: the VR options menu overhaul audit — five parallel read-only auditors writing
  `.planning/menu-audit/0*.md` (dead dials, NORMAL vs POWER audience, structure proposal); the
  synthesis + user decision list is the next deliverable after this round's report.
- **ModBuild 115** — two lanes. BORDER ATTEMPT ELEVEN, the first SHADER-side one: 114's log closed
  the texture-side case — the rect crop provably applied (294x450→291x436) and the user still saw
  an identical band, while CARD SHADER IDENTITY named the painter ('GUI/AbilityCard_Shd', a per-
  image material clone, no readable blend state). The user's own report is the tell: **the band
  turns transparent during the game's dissolve animation**, i.e. a clip of the shape
  clip(f(tex.a) − _Dissolve·..) that discards nothing at rest 0 and paints alpha-0 pixels opaque
  black — one mechanism explaining all nine texture rounds at once. Shipped: `CardShaderProbe`
  (once per session, renders the punch's exact alpha-0 pixels through a CLONE of the live material
  at _Dissolve 0 and 0.004 into an RT, reads pixels back, logs a CARD SHADER PROBE verdict:
  (a) alpha honored → another painter exists, (b) epsilon discards → floor proven by that log,
  (c) neither → shader replacement is the only road, (d) unproven; plus the full shader property
  table) and `CardDissolveFloor` (the fix: [Cards] DissolveFloorFraction = 0.004 held at rest,
  never lowering an animated value, on every card-FX material by SIGNATURE match _Dissolve +
  _PosAndBounds — ability faces via CardArtWatch, item cards via ItemsPile.ItemChip, remote fronts
  free because RemoteCardArt already rides CardArtWatch; full restore on yield/recycle). STRETCH
  FIXES from the 114 test: capture was a fixed 80 mm sphere around the mini's CENTRE, so a
  stretched mini sat outside its own zone and could not be shrunk — capture is now min
  closest-point over the held visual's renderer AABBs (cached per hold, 0.5 m sanity clamp,
  centre fallback), scaling with the figure by construction; d0/d stay centre-based on purpose.
  Plus the requested zone-entry haptic (HapticPreset.HoverTick, edge-triggered). If the border
  survives 115: **read CARD SHADER PROBE first** — verdict (a) means hunt the other painter,
  (c) means build a replacement shader; either way the guessing era is over. Wire tests 1529.
- **ModBuild 114** — two lanes. BORDER ATTEMPT TEN: heuristics retired after losing twice on the
  same two sprites — the five face layers resolve BY IDENTITY from CardEffects' serialized fields;
  a CARD SHADER IDENTITY line settles whether the custom card shader ignores alpha (the only
  theory left that explains nine invisible texture rounds at once); and the shader-agnostic rect
  crop removes the band's geometry entirely on local ability faces. HELD-FIGURE STRETCH: second
  hand + trigger near a held mini scales it ratio-based (s0 x d/d0), this-hold-only, wire record
  30 (first new record since the reservation note; additive, neutral-omitted, fail-closed decode),
  +40 wire vectors -> 1526. If the border survives 114: read CARD SHADER IDENTITY first — its
  verdict clause dictates whether the next move is shader-side or a sixth-layer hunt.
- **ModBuild 113** — the border, attempt NINE: the punch region is GEOMETRY now, not luma. 112's
  BAND INVENTORY named the culprits (punched background still 102/264 band probes — the luma-BFS is
  interrupted by decoration; the two ACTION-HALF plates 55/72, never punched). Screenshot profiling
  found the structural failure of every luma approach: content luma 55-65 vs threshold 48 vs frame
  <40 — a knife edge — while the gold TRIM sits at ~213. The card's true outline is now derived from
  the bright-trim contour once per kind, validated against the screenshot's band widths, and erased
  on EVERY layer mapping outside it. **The decisive find: the action halves fell at Image.Type.Simple
  — they are 9-SLICED Button plates, and the punch now replicates GenerateSlicedSprite's mapping.**
  First time in nine attempts they are touched. CacheVersion 4→5 (fourth load-bearing bump). If it
  fails: "CARD OUTLINE refused" = behaviour is exactly 112; otherwise BAND INVENTORY prints derived
  bands beside the culprit — disagreement indicts the derivation, agreement indicts an escaped
  consumer. Known pre-existing residual: hovered halves render Image.overrideSprite (§5e), uncovered
  by any bake.
- **ModBuild 112** — the border, attempt EIGHT: round 7's falsifier fired (ART RECT 100 % x
  100 % — no letterbox on his art), leaving the series' one POSITIVE measurement standing: the
  PEEL hit its depth cap twice (11/11, then 18/18 texels, mean luma 22). The band is a PRINTED
  near-black frame in the art's own opaque pixels — which is why mesh work was invisible (behind
  opaque art) and the verified stencil clip changed nothing (an opacity-captured outline INCLUDES
  the frame). Fix: pixel surgery on the mod-owned 'VR-mip' sprite copies the bake already swaps in
  — boundary-seeded BFS erosion with LEARNED depth (two guessed caps were both too small), alpha 0
  on the frame, gates logged. Punch runs BEFORE capture so the footprint is stamped from punched
  pixels — mesh and face agree by construction. CacheVersion 3→4 (third load-bearing bump). Round
  4 had declined exactly this as risky-against-speculation; the peel's measurements re-weighed it.
  **If it fails: grep CARD FRAME BAND INVENTORY — the culprit graphic is named, not guessed.**
- **ModBuild 111** — the black card border, attempt SEVEN, and **the lesson is about method, not
  about cards**. Six rounds reasoned about art that could not be read offline; the user supplied a
  SCREENSHOT and pixel measurement settled it in one pass. The band is TOP/BOTTOM (4.76 % / 6.6 %
  of the card height), not left/right (0.8 % / 2.4 %); its corner is a clean arc equal to
  `CardMesh.CornerRadius` and its colour a flat (4,4,3) identical across cards at different
  orientations — so it is the mod's MESH. Cause: slab and face rect coincide (round 5's "0.94
  refutation" stands), but the ability ART is poker-shaped and `Image.preserveAspect` letterboxes it
  to 90.54 % of the face rect's height, leaving 4.73 % dead top and bottom. Measured 4.76 %. The
  capture normalised by each candidate's LAYOUT rect, so the mask was stretched ~10 % vertically and
  declared the body "card" exactly where the art draws nothing — every one of six clips was correct
  and aimed 10 % away from the edge it sought. Fixed by replicating uGUI's `PreserveSpriteAspectRatio`
  (pivot re-anchoring included) from live values; nothing about size or proportions changes, which is
  what the user required ("Ich will es also so wie es jetzt ist … nur eben ohne die schwarzen
  Ränder"). `CacheVersion` 2→3, because a stale mask silently ships the previous round — that has
  already happened once. **Two integrator hypotheses were wrong in this series (sprite-less dark
  quads, and the aspect's axis); both were plausible, checkable and false. Measure before deducing.**
- **ModBuild 110** — attempt FIVE at the black card border, and the first that clips the layer the
  black is on. **The generalisable lesson: four attempts all acted on the card BODY, and the body was
  never what bounds the visible card.** The 109 log refuted the standing hypothesis with its own new
  diagnostic (exactly ONE sprite-less quad on a face, and it is WHITE) and reported a captured
  outline with real corner cuts (bbox 95.5 % of the face, 0.928 fill) — so if the clipped body bound
  the card, the cards would have stopped looking rectangular in attempt 2. They did not. Therefore
  the FACE paints over the body's whole footprint. `Cards/CardShapeMask.cs` now stencil-clips the
  adopted face itself. The reason that was not trivial: `MaskUtilities.FindRootSortOverrideCanvas`
  stops at `FullAbilityCard`'s own Canvas, whose sorting the game TOGGLES — so a mask above it can
  render a plain rectangle while every log line claims success, exactly how attempt 1 failed. It
  therefore neutralises `overrideSorting` on every canvas inside the face (recording each original,
  because a toggle can flip one after install) and VERIFIES itself by making the same two calls
  `MaskableGraphic` makes, refusing and removing itself if the stencil would not resolve.
  Integrator-verified against the local uGUI package: `Mask.IsRaycastLocationValid` filters by RECT
  only, so input is untouched, and `StencilMaterial` really does set `useAlphaClip`.
  A 0.94 coordinate-mismatch hypothesis (the integrator's) was raised and REFUTED by arithmetic —
  recorded as do-not-re-test.
- **ModBuild 109** — the card SILHOUETTE round. **The black card border took four attempts, and
  every wrong turn was a layer error**, which is why it is worth reading in full.
  Attempt 1 shipped an alpha clip that never executed (`isActiveAndEnabled` against cards adopting
  art under an inactive pool root; the rejecting branch logged nothing). Attempt 2 made it execute
  — and the border did not move. Attempt 3 found why, from the user's own sentence: during a
  character switch the loader takes the art down, nothing paints, and the border VANISHES — so the
  mesh behind it is already clipped, and **the remaining black is painted by the adopted uGUI FACE,
  not by the card body**. A clipped mesh cannot un-clip itself. The suspects are sprite-less
  `Image`s (22 enabled ones on a single face), which uGUI draws as plain colour quads — rectangles,
  incapable of carrying the card's shape. Attempt 4 mutes them in the same frame the art arrives,
  on the seam `CardFaceMipBake` already uses to swap sprites before a card's first drawn pixel.
  User then narrowed it: every placement, **no visible transition**, item cards, and every card a
  PEER draws. The transition is answered by not learning the shape in that session — the footprint
  is persisted to `BepInEx/config` and re-applied from inside the body's own material factory,
  before any renderer that will draw it has a material. Two uncovered local paths and two missed
  mirror sites were found; the peer FRONT was the bigger hole and needed no wire (the pump passes
  the CANVAS, whose GameObject carries no card component — the clone is its child).
  Also in 109: held figures keep the size they had at the grab (`TickHeldScale` pinned WORLD size,
  and the diorama zoom IS the rig scale the anchor hangs under — log: `boardWorld=1` at anchorScale
  41.368 vs 10.149); the slot a card lands in is ranked by the CARD, not the hand (an eligibility
  test had been used as a ranking key); and the boot spinner is the GAME's symbol (the previous
  round's "it is serialized inside the scene being loaded" was true of that OBJECT and wrong as a
  conclusion — a second copy lives in the Intro scene, switched on a frame before our arming edge).
- **ModBuild 108** — six reports, one round. **The generalisable lesson: three of the six were
  defects in a layer nobody had looked at, not in the layer the symptom pointed at.**
  (1) **THE DEADLOCK** (critical, user-flagged): lifting a figure mid-attack killed the turn
  machine permanently. No exception — a wait that never completes, hence nothing logged.
  `ActorBars` hides the figure's panel HOST while its mini is held; `AttackModBar`'s flow is a
  coroutine started ON that controller; Unity kills a coroutine when its GameObject is
  deactivated; `FinalizeFlow` (sole writer of `IsFlowActive = false`) is its last statement, so
  the flag latches; and `WaitingForPlayerIdle` waits on it with NO timeout. The find that shaped
  the fix: the game waits on the attack's **TARGET**, not the acting figure — a guard on "whose
  turn is it" would have missed it. Prevented structurally (`FigureBusy`), mechanism removed
  (`ActorBars` no longer hides a live-flow bar), watchdog behind both.
  (2) **MR plate flicker** on the pile captions — 107's per-registrant fix does not scale, because
  the order writer is usually not the registrant. `MrBacking` re-syncs plates phase-blind now.
  (3) **Pile symbols** — a second defect at the same place: `PileViewer.SetVisible` hit by the
  pooled-hand re-bind window. Hide debounced 2 frames. Closed rather than distinguished on a run.
  (4) **Card X spacing** — `HalfSelection` used the spread-free slot-home accessor (its only
  caller); a docked pair sat 5.5 mm wider, a 42 % change in the gap the eye judges. Accessor
  retired; the peer mirror fixed by converting through the board root (local slots carry
  `SlotScale`, remote prefab anchors do not).
  (5) **No turning while scrolling** — the axes never contended, the thumb did. Releases on the
  turn axis returning to rest, not on the scroll ending.
  (6) **Card silhouette** — the alpha clip had shipped in `6d7f1bb` and had NEVER RUN
  (`isActiveAndEnabled` against cards adopting art under an inactive pool root, rejecting branch
  logged nothing, retry budget burnt blind).
  Startup freeze: measured, not assumed — 21.3 ms of 2667 ms is the mod, 62 % is game-side YML
  parsing on the main thread. Spinner now covers the window; the mod's own 981 ms bundle inflate
  is prewarmed. See `.planning/startup-freeze.md`, incl. the bundle rebuild the user declined.
- **ModBuild 107** — two defects ModBuild 106 had already claimed to fix, both of which had been
  fixed ONE LAYER AWAY from where they live. That generalises, so it is the entry's headline.
  (1) The figure highlight is not raised by `FigureGrabDriver` at all — `ProximityGrabber` raises
  it, on the nearest grabbable inside its CARD-sized 13 cm palm reach. The driver's election is
  only a VETO, and the veto was distance-gated (`suppressed = inReach && !winner`), so a figure
  outside the sphere was actively written back to ALLOWED; the grabber reads those flags a frame
  late, so the figure CROSSING IN was momentarily the only allowed candidate and won "nearest" by
  default. One frame of amber at the far edge of the reach, walking figure to figure — "verschiedene
  Figuren", and "zu weit weg" because the leak fired at 130 mm, not at the 40 mm pick radius. Log:
  137 highlight lines against 9 elections. The 106 hysteresis/dwell stabilise the ELECTION and were
  never in this path. Veto is unconditional now and fails closed.
  (2) The quest text's one-frame blank was in the RENDER path; 106 had hardened the POLL path,
  which cannot produce a one-frame anything (the goal re-derives every 0.5 s). Two render paths
  were open and BOTH were closed rather than distinguished by a diagnostic on the next run: the
  label's draw order was written in Update *and* LateUpdate while its opaque MR plate copies it in
  Update only (plate paints over the glyphs in the frame between), and a single `!activeInHierarchy`
  frame on the tray mount takes the whole host panel down. Rank is Update-only now, guarded by a
  once-per-session assertion; the mount HIDE is debounced 2 frames, the SHOW is untouched.
- **ModBuild 106** — the two blinking slot rectangles and the card that lands in them were sized by
  two unrelated numbers with no dial between them: the card took `[Cards] SlotCardFill` = 1.45, the
  overlays took CODE LITERALS off the card metric (teal wanted-pulse 1.36, gold snap glow 1.24), so
  the card overhung the rectangle that had just marked its spot by 6.6 % (119.7 mm inside 112.3 mm,
  board metres, shipped defaults). The dial the user found, `[Cards] ActiveCardScale_*`, sizes the
  ACTIVE PILE via `ActivePileViewer`, which does not exist during selection — hence "no effect". The
  POSITION half of the coupling already existed (`SlotOverlayOffset`/`Spacing` feed both the glows
  and `SlotHomeOffsetFor`); only SIZE was missing. Now one per-board dial,
  `[Cards] SlotOverlayScale_{board}`, seeded with SlotCardFill's 1.45: the card is untouched, the
  teal grew to meet it exactly (user: "exakt ausfüllen"), the gold keeps its shipped 0.912 ratio so
  it still reads inside the teal when both show. `SlotCardFill` retired (ConfirmUndoSize pattern) —
  no migration marker, the successor key is new. Wire: field **171** (FACTOR range), needed because
  the peer's glows are not cards — `RemoteBoardFurniture` builds them from the recess metric × a
  factor, and record 11 only carries the finished card WIDTH. Ratio is now one shared constant
  `PlayTray.SnapGlowRatio` instead of a literal on each side. Side finding: `Fill` is in no
  `ConfigSteps` unit row, so the retired dial had been stepping off its own magnitude all along —
  the successor's `Scale` ending fixes that and a step vector pins it.
- **`76daf29`** (folded into the 106 bump) — quest text flickered because ONE empty poll blanked it: the goal
  resolves through `CardsGameApi.ActiveHand()`, which is null while the game re-binds a pooled
  hand, so "no goal" and "ask again in a moment" were indistinguishable. Now told apart at the
  source. Figure-grab highlight flashed because a bare radius is a step function and the hand
  was drifting *on* the boundary (log: 39 → 38 → 37 mm against a 40 mm radius) — now
  enter/exit hysteresis plus a 6-frame dwell.
- **ModBuild 105 `e4ccf8c`** — a peer's board was being **described** instead of shown. The
  decision row crossed as a label string; the receiver rebuilt flat plates. Fixed by realising
  the game already builds the whole widget tree on every client (`TakeDamagePanel` is a
  per-client Singleton, `ShowOtherPlayer`), so only ROLES need to travel — new record 29. Also:
  a peer's board sat on a static sub-ladder (0/4/8) below the panel ladder's base (100), so it
  could never rank against anything; card aliasing was a 1 s rescan timer, not a slow bake; the
  refusal sound was keyed on ownership after free focus had made "look at another player's
  character" a success.
- **ModBuild 104 `0064307`** — performance. `FindObjectsOfType<T>()` is O(*every loaded
  object*); three independent measurements price ONE call at 10–15 ms in a big room, and the
  mod made ~7 per second (~89 ms/s, delivered as 20–100 ms hitches). Replaced by
  `Core/SceneRegistry.cs`. The perf instrument also gained a **zoom axis** (`FRAME`/`SPLIT`
  now bucket a window's frames into near/middle/far thirds).
- **ModBuild 103 `06cb184`** — six reports, three of which were one defect class (a
  transparent deciding its paint order from something that moves). Includes the **invisible
  buttons** root cause, found after three failed rounds: the cap FACE is multiplied by the
  player's `[ButtonColors]` tint, the WELL it sits in is not, so at tint 0.5 a disabled face
  (0.105) is darker than its own hole (0.15) — pure arithmetic, hence a steady state.
- **ModBuild 102 `f200f7f`** — the wrist HUD. A uGUI canvas is read from its **-Z** side; an
  identity base therefore aimed the readable face out of the back of the hand while the gate
  revealed it from the palm, so the player saw the mirrored back face.
- **ModBuild 101 `a1623e1`** — `ConfigSteps` resolver: a key ending in a bare axis letter never
  matched a unit word, so its step fell to a fiftieth of the shipped default (0.05 mm per
  press). 73 of 351 dials changed step.

## 5. Open items — the successor's queue

### 5a. Committed to the user, not yet done

*(Empty. The slot-overlay unification that stood here shipped in ModBuild 106 — see §4. Note for
next time: "overlay" in the user's vocabulary means the two BLINKING RECTANGLES on the board where
a hand card may be laid, not the active pile off its right edge. The predecessor's entry here read
it the other way and that cost a clarification round.)*

### 5b. Awaiting the next hardware session (diagnostics are already in place)

- **The glasses among the symbols** (`[WorldUI] USE BARS: bonus-bar split KEPT …`). The
  predicate `CardsGameApi.BonusIsPlaceable` is a four-way conjunction that **fails open**, which
  is also the shape a host/client divergence takes. The new line names WHICH condition kept each
  row — compare the host's and the peer's logs for the same bonus.
- **Fog-tile shimmer**: grep `UNSEEN TILE ORDER` and read the **apply count**. A rewrite is the
  only event that driver has which can change anything on screen, so "the tiles moved" and "the
  count did not move" cannot both be true.
- **Perf**: the next capture should run with `[Optimize] QuietDiagnostics = true` (about a third
  of the ModBuild 104 saving is behind that switch) and `[Perf] SceneProfile = true` (feeds the
  zoom clause). Then read the new `ZOOM` clause on `[Perf] SPLIT`.
- **Figure pick radius**: `[FigureGrab] PickRadiusMillimeters` (5…130; **130 restores the old
  palm-wide reach exactly**). At the user's zoom 40 mm ≈ 1.1 hex widths — if the flashing
  persists after the hysteresis, the radius itself is the next lever.

### 5c. Declined by the user — do not re-propose without new information

The zoomed-out overview cannot reach 90 Hz without changing what the head camera declares
visible. The user was presented with the options and chose **none of them**:
- narrowing the head camera's culling mask (`[Optimize] HeadMaskFromScenarioCamera`),
- distance-culling VFX/particles,
- animation LOD for distant figures,
- bar material de-duplication (small but real look risk).

Expected outcome of what WAS built: close-in should reach a stable 90 Hz; the far overview
should be a **hitch-free 45**, not 90. Say so plainly rather than implying more.

### 5d. Parked by user ruling

- **Per-eye wall-fade stereo rivalry** — proven unfixable on the game's masonry shader without
  losing the dissolve (`.planning/wall-fade-stereo-rivalry.md`).
- **Static batching ("Bündelung")** — tried and completely removed; Apparance reveal-clones of
  batched sources are born without material slots (`.planning/static-batching-removed.md`).

### 5e. Known gaps, carried

- `Net/BoardVisual.cs`'s sub-ladder was fixed in 105, but mod-owned free plates near a peer's
  board (fan hints, ping labels) are still at order 0 and lose to it.
- 16 PENDING wire-coverage debts (`check-wire-coverage.py` lists them with reasons).
- `Selectable` sprite-swap transitions write `Image.overrideSprite`, which the mip bake has
  never covered — hovering an action half can still sample a mipless state sprite.
- A receiver whose own take-damage prompt is docked falls back to plates for that window
  (self-healing, logged).

## 6. Working practice that actually matters

### Your role: INTEGRATOR (user ruling, 2026-08-11, standing)

> "Ich will, dass du hauptsächlich dafür da bist die Ergebnisse der agenten zu mergen und deine
> Aufgaben an spezialisierte Agenten abgibst die auf feature branches arbeiten. So sollst du deine
> Anstrengen auch parallelisieren. In Begründeten Außnahmefällen kannst du auch mal direkt etwas
> implementieren, sonst an Implementierungsagenten auslagern."

Implementation goes to specialised agents on feature branches, in parallel. Implementing yourself
is the exception and needs a stated reason. **"The files are coupled" is not one** — that was the
argument used in ModBuild 106 and the user rejected it, because coupling is something the
integrator designs away *before* dispatching:

1. **Land the shared contract first.** Any symbol several workers need (a config accessor, a Tune
   id, an interface) is a thin slice you write and commit to `main` yourself BEFORE fanning out.
   Minutes of work, and every worker then compiles on its own.
2. **Split by FILE OWNERSHIP, not by topic** — name each worker's exclusive file set in its brief.
   Overlapping FILES cause collisions; overlapping subject matter does not.
3. Typical lanes for a board feature: local/render · net mirror · localisation + options UI ·
   tests + checkers.
4. **Always pass `isolation: worktree`** — without it they collide in the shared checkout.
5. Brief every worker with: the hard rules (§1), the five gates (§2), "do NOT bump ModBuild",
   "do NOT commit or push", "never `git stash`", and the exact user report **verbatim in German**.
6. **You** review every diff, merge, bump ModBuild, write the build note, push, and report.

Worker first command: `bash scripts/worktree-setup.sh` (links `ressources`, `libs`,
`.planning/debug/default` and the refactor-guard baseline).

### Hazards that have actually bitten (all of these cost a round)

1. **`git stash` is NOT worktree-isolated.** `refs/stash` is shared by every worktree; two
   agents stashing concurrently pop each other's work. Forbid it in worker briefs.
2. **The shell's working directory persists between tool calls**, and `cd`-ing into a worktree
   to inspect a diff leaves you there. Two commits landed on a worker branch instead of `main`
   this way. **Use absolute paths, and verify with `git log --oneline -1 origin/main` after
   every push.**
3. **`git apply` is atomic.** It prints "Applied patch to X cleanly" per file and then rolls
   *everything* back if a later file conflicts. A green build afterwards proves nothing — a new
   file compiles on its own. Verify a merged symbol with `grep`.
4. **`grep -c` returning 0 exits non-zero** and breaks `&&` chains. Use `;`.
5. Bash heredoc + Python triple-quote collide — write the script to the scratchpad first.

### Logs

The user drops hardware logs in `.planning/debug/` (gitignored); a peer's logs go to
`.planning/debug/remote/`. Tuned config snapshots land in `.planning/debug/default/` and are
taken over with `python3 scripts/rebase-defaults.py apply`. **Those cfg values are always tuned
against the NEWEST build — take them over verbatim, never re-express them for a frame change
that shipped in the same round.** (Learned the hard way; see the do-not-swap note in
`WristHud.ApplyPose`.)

Always check the build stamp at the top of a dropped log before reasoning about it:
`[Core] GloomhavenVR ModBuild N` and `v0.1.0 build <sha> [main]`.

### Never invite a test before the round is pushed (user ruling, 2026-08-11, standing)

> "Ich hatte aber gepulled und kompiliert. Sorge dafür das nach einer Runde wenn ich zum tsten
> aufgefordert bin die änderungen auch immer im master gepusht sind!"

He pulls and compiles from `main`; an open worker lane is invisible to him. So:

- **Do not name log strings to grep, expected visual changes, or things to watch for** for any
  item whose lane is not yet merged AND pushed. Describing what to look for reads as an
  invitation to test even without the words "please test" — that is exactly how this was broken.
- **End every round-closing report with two explicit lines:** the commit to test (verified with
  `git log --oneline -1 origin/main` *after* pushing), and which reported items are NOT in it.
- While lanes are still running, either say plainly "noch nicht testen" or scope the test to the
  merged items and name them.

Same economics as the test-confidence rule: a run whose outcome is already known costs him a
full game launch plus headset time.

### Reporting to the user

German, prose, no bullet-point dumps. State the **root cause**, separate what was **proven from
the log/source** from what is **inferred**, and say plainly what you did *not* do and why. He
values a stated limitation far above a smoothed-over one, and he has corrected over-confident
claims more than once.

---

## Standing technical decisions

- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs; OpenXR 1.10.0 + XR Management 4.5.0;
  MultiPass.
- Never patch `ScenarioRuleLibrary`/Bolt; commit through UI seams only.
- License GPL-3.0 (LCVR/RepoXR pattern reuse, credited); SteamVR hands BSD-3.
- Module config: `dev.gloomhavenvr.<module>.cfg` via `ModuleConfig.Create`.
- Distance ladder (`WorldUI/CanvasConversion.8.Order.cs`): base 100, step 16, nearer = higher
  order; `OrderAboveDistance(eyeDistance, lift)` is the seam for non-panel plates.
- `HandRig.Wrist` is **not** in `HandRig.Root`'s frame: `Anchor_Wrist` carries +90° about X, so
  wrist +Y is along the fingers and wrist +Z is out of the palm. A uGUI canvas parented there is
  read from its **-Z** side.
