// GENERATED-AND-HAND-EDITABLE — the single home of every shipped config default.
//
// WHY THIS FILE EXISTS
// --------------------
// The mod binds ~550 config entries across 18 module config classes. Their default values
// were literals at the Bind call, which meant re-basing the shipped defaults onto a tuned
// setup was a hunt through the whole codebase. They all live here now, one per line, each
// tagged with the config identity it feeds:
//
//     internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward
//
// The `// => [Section] Key` annotation is the machine-readable part: scripts/rebase-defaults.py
// finds an entry by it, so a line may move but its annotation must stay exact.
//
// RULES
//   * one entry per line, initialiser a LITERAL (or a `new Vector3(...)`/`new Color(...)` of
//     literals) — never an expression that reads another entry;
//   * `const` wherever C# allows it, so the compiler inlines it and the compiled form is
//     identical to the old literal-at-the-bind;
//   * `static readonly` only for Vector2/Vector3/Color, which cannot be const;
//   * the *_ByBoard / *_ByStyle arrays exist so a per-variant bind inside a loop can index
//     them; they are assembled from the named entries above them and hold no literals.
//
// Editing: change the number, rebuild. Or drop a tuned cfg into .planning/debug/default/ and
// run `python3 scripts/rebase-defaults.py apply`.
//
// This preamble is the canonical copy: every other Defaults.*.cs carries a short pointer
// to it instead of repeating it.


using UnityEngine;
using GloomhavenVR.Core;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Core/MixedReality.cs ------------------------------------------------------
    internal const bool MixedReality_Enabled = false;                     // => [MixedReality] Enabled  (pinned: user ruling 2026-08-04 — MR see-through is the OWNER's personal setup, a fresh install must start in full VR; the tuned cfg's `true` briefly shipped in ModBuild 55 by accident and was reverted the same day)
    internal static readonly Color KeyColor = new Color(0f, 1f, 0f, 1f);  // => [MixedReality] KeyColor
    internal const bool HideSkyMeshes = true;                             // => [MixedReality] HideSkyMeshes
    internal const bool OpaquePreviewTiles = true;                        // => [MixedReality] OpaquePreviewTiles
    internal const float UnseenSkirtScale = 1.2f;                         // => [MixedReality] UnseenSkirtScale  (round 7: XZ widening of the GROOVE-FILL copy so neighboring fills overlap under the bevel channels; the primary underlay is exact 1:1)
    internal const bool UnseenRegionMembership = true;                    // => [MixedReality] UnseenRegionMembership  (round 16: back every mesh renderer that merely STANDS INSIDE a matched fog-of-war piece's AABB, below that piece's own top plane — the route that does not ask the material anything, and the only one that catches 'Simple Tile', the full-height block forming the region's outer cliff; rails: no figures, no mod objects, no non-meshes, nothing above the top plane, nothing wider than 3x the host hex)
    internal const bool UnseenBackingDebugColors = false;                 // => [MixedReality] UnseenBackingDebugColors  (round 16 DIAGNOSTIC: paints each mod-built backing class in a flat signal colour — coplanar underlay blue, gap wafer magenta, rim curtain red — with identical shader/queue/blend/depth/layer/geometry, so one MR screenshot shows which of the mod's surfaces reach the screen and where they land; OFF because it deliberately makes the fog-of-war region look wrong)
    internal const float UnseenRimInset = 0.03f;                          // => [MixedReality] UnseenRimInset  (round 15: world-units the mod-BUILT rim-curtain prism stands inside each unseen piece's authored vertical side faces; 0.03 on a piece whose whole height is 0.3 wu — deep enough that a damaged/notched edge cannot expose it, shallow enough that the leak band at the outer silhouette stays sub-pixel)
    internal const float UnseenRimTopClearance = 0.025f;                   // => [MixedReality] UnseenRimTopClearance  (round 15: world-units the rim curtain's cap stays below each piece's mesh-top plane; > UnseenWaferDrop by design, and re-forced to waferDrop + 0.005 at build time, so the curtain always hides behind the XZ x1.2 wider wafer and no authored top face is ever painted over)
    internal const float UnseenWaferDrop = 0.02f;                         // => [MixedReality] UnseenWaferDrop  (round 12: FRESH KEY replacing UnseenFillDrop — the old key's persisted 0.35 deep-fill value survived round 11's default change and re-opened the seam canyon (ModBuild-69 log: 'wafer = mesh-top − 0.35 wu'); world-units the wafer sits below each piece's mesh-top plane, 0.02 hugs the top without z-fighting)

    // ---- Core/PerfConfig.cs --------------------------------------------------------
    internal const bool Perf_Enabled = true;                 // => [Perf] Enabled
    internal const float SummaryIntervalSeconds = 30f;       // => [Perf] SummaryIntervalSeconds
    internal const bool Attribution = true;                  // => [Perf] Attribution
    internal const int TopSteps = 6;                         // => [Perf] TopSteps
    internal const bool SpikeLines = true;                   // => [Perf] SpikeLines
    internal const float SpikeBudgetFactor = 2f;             // => [Perf] SpikeBudgetFactor
    internal const float SpikeMaxPerSecond = 2f;             // => [Perf] SpikeMaxPerSecond
    internal const bool Allocations = true;                  // => [Perf] Allocations
    internal const bool XrStats = true;                      // => [Perf] XrStats
    internal const bool FrameSplit = true;                   // => [Perf] FrameSplit
    internal const bool SceneCensus = true;                  // => [Perf] SceneCensus
    internal const bool SceneProfile = true;                 // => [Perf] SceneProfile  (ON since ModBuild 227. The 226 log caught the reported symptom — "deutliche Laggs wenn ich alle Räume von oben anschaue", 8,600 renderers, 43ms mean against an 11.11ms budget, ~50% of it in main-thread LOGIC — and every instrument that could name what that logic IS was switched off, so eight windows of renderer counts named nothing. The walk is a one-frame hitch per 30s window; it TIMES ITSELF and rations itself against a 4ms/window budget, refuses to run pre-menu, and PerfMonitor latches it off after one throw)
    internal const bool CullSubmitSplit = true;              // => [Perf] CullSubmitSplit  (ON since ModBuild 227. Pure measurement, changes no pixel: two timer reads per camera render. It decides whether the head camera's 6–9ms/frame over 8,600 renderers is CULL — in which case the blanket 0xFFFFFFFF head mask vs the game's own 0x700FFF17 ScenarioCamera mask is the next lever — or SUBMIT, in which case it is not)
    internal const bool ProfileDefaultsMigrated227 = false;   // => [Perf] ProfileDefaultsMigrated227  (pinned: one-shot migration marker — a fresh install must start false, or the ModBuild 227 SceneProfile/CullSubmitSplit flip never reaches an existing cfg)
    internal const bool LodCensus = true;                    // => [Perf] LodCensus  (ON: pure measurement, one line per scene. The 227 SIM reading put 2560 of 2986 Update entries — 86% — in ONE third-party type, and the analysis that named it also asserted "the game has no LODGroup at all" while the GFX line in the same log read "LOD groups: 1277 active". This walk settles both, names which camera decides the level, prints the level distribution the scene is actually running at, and prints QualitySettings.masterTextureLimit — the competing mechanism for the separate "matschige Texturen" report, which no line in this log has ever carried)
    internal const bool CacheTickDelegates = true;           // => [Optimize] CacheTickDelegates
    internal const bool MapIconCache = true;                 // => [Optimize] MapIconCache
    internal const bool FigureScanCache = true;              // => [Optimize] FigureScanCache
    internal const bool LeanLogStrings = true;               // => [Optimize] LeanLogStrings
    internal const bool TooltipScanGate = true;              // => [Optimize] TooltipScanGate
    internal const float FanRelayoutMinInterval = 0f;        // => [Optimize] FanRelayoutMinInterval
    internal const float WallFadeEvalInterval = 0f;          // => [Optimize] WallFadeEvalInterval
    internal const float InitiativeDepthEvalInterval = 0f;   // => [Optimize] InitiativeDepthEvalInterval
    internal const bool QuietDiagnostics = false;            // => [Optimize] QuietDiagnostics
    internal const float RemoteContentInterval = 0f;         // => [Optimize] RemoteContentInterval
    // ModBuild 251 — THIS `false` IS A REGRESSION AND THE VALUE IS DELIBERATELY LEFT AS IT IS
    // UNTIL ONE HARDWARE MEASUREMENT DECIDES IT. Read this before touching the line below.
    //  * It was bound `true` at 19b120cb, and every piece of prose still attached to it says so:
    //    PerfConfig's description opens "ON is today's behaviour", and VRRigDriver.HeadCamera.cs
    //    documents the OFF state's failure in the PAST TENSE — the game's VFX shaders (torch and
    //    candle glow, clouds, distortion) soft-fade against _CameraDepthTexture, and with
    //    DepthTextureMode.None "the fade sampled nothing and FAILED OPEN".
    //  * It became `false` in c5d6bbc9 — the refactor that moved every default onto one line in
    //    this folder, whose own message says "Descriptions, ordering, ranges, seeding — untouched".
    //    That commit joined the defaults against .planning/debug/default/*.cfg, and
    //    dev.gloomhavenvr.perf.cfg carried `false` because it was a dump of a session in which the
    //    value was being A/B'd. A measurement state was promoted to a shipped default by a
    //    refactor that believed it was changing nothing.
    //  * THE CLAIM THIS BLOCK USED TO MAKE IS DEAD, AND IT IS CORRECTED HERE RATHER THAN DELETED
    //    SO NOBODY RE-DERIVES IT. ModBuild 251 wrote that schwebende_lichter.jpg / walls_gone.jpg —
    //    hard-edged pale rectangles at the gate, "dauerhaft so egal was ein oder ausgeblendet
    //    wird" — ARE this regression, reasoning that under D3D11's reversed depth an unwritten
    //    depth texture reads as the FAR PLANE, so a saturate((sceneZ - fragZ) * _InvFade) term
    //    saturates to full authored opacity independently of the wall fade. The mechanism is real;
    //    it is not what he is photographing. He ran the A/B for ModBuild 253: second_logs/Player.log
    //    shows depthTextureMode=Depth with [Optimize] HeadDepthPrepass=true AND THE RECTANGLES
    //    UNCHANGED. ModBuild 251's own GLOW CARDS census had already refuted it before that — the
    //    three cards it counted as depth-fade dependent are offscreen, not-submitted shield clouds
    //    on stone golems, while every gate-area card printed "depth-fade props: NONE".
    //    So this entry is a REGRESSION WITH NO KNOWN SYMPTOM: still worth correcting one day, no
    //    longer evidence about the gate.
    //  * AND THE GATE IS NOW CLOSED, AGAINST ModBuild 259's DISMISSAL. 259 acquitted the gate's
    //    'Door_Light_*_Mesh' plates because their bounds are an 8:1 strip 0.257 wu tall while the
    //    photographed regions are taller than wide. Both halves are true and the conclusion does not
    //    follow: 8:1 is the UNION of a row of small coplanar quads and each quad in it is taller than
    //    wide. Measured on both photographs, the trio spans ~2.1 wu at exactly 0.257 wu tall, which
    //    IS that bounding box. They are the game's own opaque LIT plates a few cm from the door's own
    //    light, which is why no fade and no depth term ever touched them. Core/DoorLightPlates.cs
    //    hides the RENDERER game-wide (user, 2026-08-25) and carries the full measurement.
    //  * WHY IT IS NOT SIMPLY FLIPPED HERE. (a) Turning it on makes Unity build the depth texture
    //    on the built-in forward path by submitting every opaque renderer a SECOND time through
    //    its shadow-caster pass — four full submissions per frame under MultiPass instead of two,
    //    the largest single piece of submission volume the mod adds — and this room already runs
    //    11.65-12.00 ms against an 11.11 ms budget. Nobody has measured that trade on this
    //    hardware. (b) Flipping it would change NOTHING on an existing install anyway: BepInEx
    //    keeps the value already in the cfg. The honest sequence is one line in
    //    dev.gloomhavenvr.perf.cfg, [Perf] FRAME read on both sides of it, and THEN this default
    //    set deliberately — with the answer in hand instead of a guess. Note that the A/B above
    //    was run for the RECTANGLES and not for the frame time, so the perf half of that
    //    measurement still does not exist.
    internal const bool HeadDepthPrepass = false;            // => [Optimize] HeadDepthPrepass
    internal const string HeadCullingMaskDrop = "";          // => [Optimize] HeadCullingMaskDrop
    internal const bool HeadMaskFromScenarioCamera = false;  // => [Optimize] HeadMaskFromScenarioCamera
    internal const bool AutomaticLodIdleSkip = true;         // => [Optimize] AutomaticLodIdleSkip  (ON, VR only. Not a judgement call: the sweep disables an instance only after READING its own LODSwitchMode as UnityLODGroup, which is the exact condition under which AutomaticLOD.Update returns on its FIRST statement — the component's own test, run per instance. 2560 of 2986 entries on Unity's Update list on the 2026-08-22 log. The LODGroup that actually picks the level is a different component and is untouched; every disabled instance is restored on teardown, on hot-reload and on a dial flip)
    internal const float AutomaticLodSweepSeconds = 15f;     // => [Optimize] AutomaticLodSweepSeconds  (a revealed room instantiates new AutomaticLOD; 15s is the worst-case latency before those come off the list too. One typed FindObjectsOfType per sweep, which the [Perf] LOD line times and prints — at a single-digit-ms scan this is far under a tenth of a percent of a frame amortised, against a saving paid on all ~1350 frames in that window)
    internal const float LodBias = 0f;                       // => [Optimize] LodBias  (0 = leave QualitySettings.lodBias at the quality level's own value = TODAY'S BEHAVIOUR, changes no pixel. The VR correction factor is tan(fovVR/2)/tan(fovFlat/2), two numbers this project has never measured — the [Perf] LOD line prints them and the exact value that would restore the flat game's LOD choice. Nothing gets tuned here before that reading exists)

    // ---- Core/SkyAlternative.cs ----------------------------------------------------
    internal const SkyStyle SkyStyle = Core.SkyStyle.SwampNight;  // => [Sky] Style  (SwampNight since the user's 2026-09-03 cfg drop; Default is the game's own sky; Cellar/SwampNight spawn the mod's own bundled 3D atmosphere around the play space, SCENARIO-ONLY by user ruling 2026-08-12 — game-asset room generation was removed by ruling 2026-08-13, see .planning/game-env-postmortem.md — OffBlack shows no surroundings at all, loading and spawning nothing, and is NOT mixed reality by user ruling 2026-08-13 — and mixed reality always overrides every sky/environment to OFF)

    // ---- Core/ElementMood.cs -------------------------------------------------------
    internal const bool ElementMoodEnabled = true;             // => [Elements] EnvironmentResponse  (ON: the channel is inert until a material reads it, so a fresh install looks exactly like today — and the day the environment art lands it is already live without the player hunting for a switch)
    internal const float ElementMoodStrength = 1f;             // => [Elements] ResponseStrength  (1 = the authored strength; the dial is folded into the published master factor, so 0 is exactly the same picture as switching the reaction off)

    // ---- Core/EnvSound.cs ----------------------------------------------------------
    internal const bool EnvSoundEnabled = true;                // => [EnvSound] Enabled  (ON: the user asked for the feature and it only runs inside the two bundled environments he has to choose deliberately, exactly like the easter eggs below — a player who never picks Cellar or Night forest never hears a thing, so an off-by-default switch would only hide it from the people who did choose)
    internal const float EnvSoundGain = 1f;                    // => [EnvSound] Gain  (1 = the designed level, which is already deliberately quiet: every source is capped well under a game cue, the whole ambience ducks while the game makes any sound, and the player's own master/effects sliders multiply on top. The key ends in 'Gain' rather than 'Volume' because ConfigSteps only recognises unit SUFFIXES and 'Volume' is not one of them)
    // NO EnvSoundAmbienceBed / EnvSoundAmbienceBedGain. Both dials went at ModBuild 223 with the two CONTINUOUS ROOM TONES they existed to switch and scale (user, 2026-08-22: "Im Keller hören sich die Geräusche an wie Rauschen bei nem Fernseher" and "Statt generrell durchgehende sounds zu machen lieber die Tierrufe"). A toggle with nothing behind it and a volume for silence are worse than no dial; what plays at rest now is the window/canopy DRAUGHT, which the same report asks for by name. See Core/EnvSound.cs's THE ROOM TONES, DELETED.

    // ---- Core/Haunt.cs -------------------------------------------------------------
    internal const bool HauntEasterEggs = true;                // => [Haunt] EasterEggs  (ON: the user asked for the feature and it only ever runs inside the two bundled environments he has to choose deliberately — a player who never picks Cellar or Night forest never sees one, so an off-by-default switch would only hide it from the people who did choose)
    internal const float HauntFrequency = 0.5f;                // => [Haunt] Frequency  (half of the schedule, i.e. roughly one easter egg every three minutes. NOT 1.0: the dial is a monotone SUBSET selector — it can only ever remove events, never invent them, because inventing one would break "every player sees the same event in the same place" — so the shipped value has to sit in the middle for the dial to have room in both directions)

    // ---- Core/StereoModeConfig.cs --------------------------------------------------
    // [Stereo] RenderMode was here and is UNBOUND since the 2026-08-22 settings audit: the
    // stereo render mode is not a preference — MultiPass is what every one of this mod's
    // per-eye paths is written against, and the alternatives produced a broken picture rather
    // than a different one. The value now lives as a constant at its own use site.

    // ---- Core/WallSegmentFade.cs ---------------------------------------------------
    // A Schmitt trigger needs Off < On. These two shipped TRANSPOSED — On 0.1 with Off 0.2, the
    // low bar above the high one — so WallFadeTuning.Off had to clamp it back down and the pair
    // collapsed to a single shared threshold with no band at all. That is the mechanism behind
    // the group churn in the ModBuild 250/251 hardware logs, where every wall flips together:
    // several coverages sitting on one bar with nothing between them. Corrected at source here,
    // which leaves the degenerate-pair fallback in WallFadeTuning.Off as a pure guard.
    // 0.25/0.10 was the pair those accessors always named as their own fallback, and ModBuild
    // 256 moved it again — this time from the MEASURED distribution rather than from a
    // fallback constant. Every green wall's coverage in the ModBuild 255 log is bimodal, and
    // the two modes are far apart: a wall that is genuinely in the way reads 0.44-0.95
    // ('Wall 1' 84 samples at 0.44, 'Wall 2' 88 at 0.75, 'Wall 4' at 0.50/0.95), and the same
    // wall at rest reads 0.13-0.25 ('Wall 1' 20 samples pinned at 0.13, 'Wall 2' 22 and
    // 'Wall 4' 119 pinned at exactly 0.25). Nothing at all lives between 0.25 and 0.44.
    // The old band sat BELOW that gap: enter 0.25 is exactly a resting wall's coverage, so it
    // latched on sight, and exit 0.10 is below anything a wall at rest can reach on a 16-cell
    // grid (quantum 0.0625 — "under 0.10" means "at most ONE cell"), so it could never release.
    // 0.35/0.20 puts both bars inside the empty gap: a resting wall cannot enter, and a wall
    // that stops blocking falls out. See the user report of 2026-08-24, which is this ratchet
    // described from the outside ("einmal ausgeblendet ist es super schwer sie wieder
    // einzublenden, egal welche Position ich einnehme").
    // PINNED against the cfg drop on purpose: the drop only ever carries what a previous build
    // wrote there, never a value the user tuned — see the one-shot markers below.
    internal const float OnFraction = 0.35f;                 // => [WallFade] OnFraction  (pinned: cfg drop holds a previous build's value, never tuned)
    internal const float OffFraction = 0.2f;                 // => [WallFade] OffFraction  (pinned: cfg drop holds a previous build's value, never tuned)
    // ModBuild 468 — THE ENTER BAR AS A CELL COUNT, CAPPED. OnFraction is a fraction of the
    // room's WHOLE sample set, and until ModBuild 465 that set was min(grid^2, hexes) <= 16, so
    // the enter bar was at most ceil(0.35 * 16) = 6 cells for every room the mod had ever
    // measured. Whole-set sampling let a one-room scenario carry 24 samples, and the same 0.35
    // then reads NINE hexes — a wall must hide more than a third of every playable hex in the
    // scenario before it fades. 6 is that historical ceiling, written down: no room's enter bar
    // may be stricter, IN CELLS, than the strictest bar the lattice could ever produce.
    internal const int MaxEnterCells = 6;                     // => [WallFade] MaxEnterCells
    // BepInEx keeps whatever is already in the cfg, so correcting the two constants above only
    // ever reaches a FRESH install — every existing install would keep the transposed pair and
    // the group churn with it. This marker carries the correction across exactly once; see the
    // migration block in WallFadeTuning.Bind for why it only fires on the degenerate shape.
    internal const bool WallFadeBarsMigrated252 = false;      // => [WallFade] WallFadeBarsMigrated252  (pinned: one-shot migration marker — a fresh install must start false)
    // The 252 one-shot is SPENT on every install that has run that build — it wrote 0.25/0.10
    // and set itself true. So the 0.35/0.20 correction above needs its own carrier, or it
    // reaches nobody who is already testing. Fires only on the exact pair 252 itself wrote, so
    // anything tuned since is left alone; see the migration block in WallFadeTuning.Bind.
    internal const bool WallFadeBarsMigrated256 = false;      // => [WallFade] WallFadeBarsMigrated256  (pinned: one-shot migration marker — a fresh install must start false)
    internal const float ExitDwellMovedSeconds = 0.5f;       // => [WallFade] ExitDwellMovedSeconds
    internal const float ExitDwellStationarySeconds = 3.6f;  // => [WallFade] ExitDwellStationarySeconds
    internal const bool StackedShellFade = true;             // => [WallFade] StackedShellFade
    // Shipped ON so the next MP hardware test shows the peer-synced fades without cfg fiddling
    // (receiver-side gate; own fades are always broadcast — WallSegmentFade.Net.cs).
    internal const bool SyncPeerFades = true;                // => [WallFade] SyncPeerFades
    // ModBuild 259 (user ruling 2026-08-24): "Entweder verschwindet die ganze Wand mit ALLEM was
    // dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da." A NEW key, so BepInEx's
    // keep-existing-values rule is not in the way — every install gets this default. Shipped ON:
    // OFF is the ModBuild 258 behaviour the ruling rejects, and the dial exists only so the
    // mechanism can be turned off without a build if the next hardware log falsifies it.
    internal const bool SplitRunUnified = true;              // => [WallFade] SplitRunUnified
    // ModBuild 261. The ModBuild 260 hardware log: 99 of 'Wall 1''s 140 fade-shaded renderers are
    // refused at the wall choke point by the standing-prop FLOOR arm and therefore own no segment
    // geometry at all — the standing hedge in wände_problem4.mp4. They ride their run as
    // PASSENGERS: they take its verdict, they never vote on it, so WHEN a wall fades is unchanged.
    //
    // SHIPPED OFF, and the ruling that decides it is the user's REFINEMENT of the same day: "Es
    // gibt Dinge die stehen bleiben dürfen. zB der Brunnen … oder auch dieses Steingebilde … weil
    // es auch niedrig ist und nicht die Sicht verdeckt." This dial recruits the WHOLE FLOOR-arm-
    // refused class, and that class is exactly the low floor-standing scenery he has just said may
    // stay — a well and a low stone formation would vanish with the wall. It is the blunt lever,
    // kept so the population can be moved with one cfg line and no build. The SHARP lever is to
    // recruit only the refused pieces that BLOCK A PLAYABLE-TILE SAMPLE, which needs no height
    // constant at all; the SPLIT-RUN LEFTOVER line now classifies every leftover ALLOWED /
    // FLOATING / OBSTRUCTING with its blocked-sample count, so the next log sizes that rule before
    // it is written. NEW key ⇒ BepInEx cannot keep an old value ⇒ no migration marker needed.
    internal const bool SplitRunAdoptGroundScenery = false;  // => [WallFade] SplitRunAdoptGroundScenery

    // ModBuild 271 — THE WALK-IN STAND-DOWN (user request 2026-08-25: "Wenn ein Spieler IN das
    // Spielfeld geht weil er so nah ranzoomed und dann im Spielfeld ist will ich, dass ein
    // spezieller Modus aktiviert wird in dem ausnahmslos alle Wände sichtbar sind und nichts
    // mehr faded. Das soll in Erweitert deaktivierbar sein."). Shipped ON: it IS the requested
    // feature, and the dial is the "deaktivierbar" half of the same sentence. NEW keys, so
    // BepInEx's keep-existing-values rule is not in the way and no migration marker is needed.
    internal const bool WalkInStandDown = true;              // => [WallFade] WalkInStandDown
    // THE TERM THE WHOLE DESIGN RESTS ON, and the one the two retired attempts lacked. The
    // ModBuild 251 log fired "INSIDE THE MAP: YES" on a board whose walls were 0.61 m tall in
    // real metres — the player was leaning over his own tabletop diorama, not standing in a
    // room. The ModBuild 270 log, where he really had zoomed himself in, reads 1.62 m and
    // 1.88 m. 1.20 m sits between the two populations with ~2x margin below and ~1.35x above,
    // and the latch releases only under 0.85 x this (1.02 m) so it cannot chatter on the bar.
    internal const float WalkInMinCrestMetres = 1.2f;        // => [WallFade] WalkInMinCrestMetres

    // ModBuild 272 — THE WALK-IN TRIGGER PROMOTED TO A TUNING SURFACE (user request 2026-08-25:
    // "Bitte gebe mir eine Einstellmöglich in dem ich die parameter selber tunen kann wann der
    // Modus aktiv wird, in dem man IN einem Spielfeld ist und die Wände nicht mehr faden.").
    // Every value below was a `private const` in WallSegmentFade.Inside.cs and is bound here at
    // EXACTLY the number that const held, so a fresh install and an install that never opens the
    // menu behave bit-for-bit as ModBuild 271 did. This is a tuning surface, not a retune. NEW
    // keys, so BepInEx's keep-existing-values rule is not in the way and no migration marker is
    // needed.
    //
    // The hardware that motivates the numbers he will type: the ModBuild 271 session's crest
    // readings were 2.31 m (x9) and 1.42 m (x1) — both over the 1.20 m bar, and the mode engaged
    // twice — against 0.94 m (x3) and 0.82 m (x2), both under it, where it refused. The boundary
    // he is being handed therefore sits in the gap between 0.94 and 1.42.
    internal const float WalkInCrestReleaseFraction = 0.85f; // => [WallFade] WalkInCrestReleaseFraction
    internal const float InsideEnterDepthFraction = 0.1f;    // => [WallFade] InsideEnterDepthFraction
    internal const float InsideExitDepthFraction = 0.35f;    // => [WallFade] InsideExitDepthFraction
    // EnterDwellSeconds (WallSegmentFade.cs) is the constant the latch borrowed; 0.20 is its value.
    internal const float WalkInEnterDwellSeconds = 0.2f;     // => [WallFade] WalkInEnterDwellSeconds
    // ExitDwellMovedSeconds above is the dial the latch borrowed; 2.5 is its shipped value, and
    // the two are kept equal on purpose so the promotion is invisible at the defaults.
    internal const float WalkInExitDwellSeconds = 2.5f;      // => [WallFade] WalkInExitDwellSeconds
    // 0 IS LOAD-BEARING, not a placeholder: the shipped test is `_lastInsideMarginY < 0`, and
    // `margin < -0 * crest` is that same test. Any other default would be a retune.
    internal const float WalkInHeadBelowCrestFraction = 0f;  // => [WallFade] WalkInHeadBelowCrestFraction

    // ModBuild 278 — THE TWO SAMPLING CADENCES, both promoted on the user's request of
    // 2026-08-25: "Würde es helfen hier die Abtastrate, also Frequenz in dem gecheckt wird ob
    // eine Wand etwas verdeckt, etwas zu verringern? Am Besten lass sie in den Einstellungen
    // selber einstellen können."
    //
    // THERE ARE TWO OF THEM AND HIS SENTENCE NAMES ONE WHILE HIS SYMPTOM IS THE OTHER, so both
    // are dials now and both descriptions say which is which:
    //
    //  * RescanIntervalSeconds is the RESCAN CYCLE — classify, survey, prepare and (when the
    //    skip refuses) a COMMIT that is one atomic frame. ModBuild 277 measured 33 commits at
    //    a mean worst-commit of 85.6 ms and a max of 134.0 ms. THOSE are the Ruckler. Raising
    //    this number divides how MANY of them happen and makes not one of them shorter.
    //  * EvalIntervalSeconds is the DECISION — UpdateSampleVisibility + BlockedFraction, i.e.
    //    literally "wird gecheckt ob eine Wand etwas verdeckt". That is the one he named.
    //
    // 2.0 IS THE CONSTANT THAT SHIPPED. It was `private const float RescanIntervalSeconds = 2f`
    // in WallSegmentFade.cs from the subsystem's first build until this one, so a fresh install
    // and an install that never opens the menu behave exactly as ModBuild 277 did. This is a
    // tuning surface, not a retune — the same rule the ModBuild 272 walk-in promotion followed.
    internal const float RescanIntervalSeconds = 2f;         // => [WallFade] RescanIntervalSeconds
    // 0 = every frame, which is what [Optimize] WallFadeEvalInterval has shipped as since the
    // 2026-07 perf pass and therefore what this must ship as: a non-zero default here would be
    // a silent behaviour change smuggled in on a surfacing commit, and it would also override
    // whatever a returning tester already has in his perf.cfg. The measured recommendation and
    // its derivation live in .planning/perf/FINDINGS.md; the number is a human's to choose.
    internal const float EvalIntervalSeconds = 0f;           // => [WallFade] EvalIntervalSeconds
    // ModBuild 437 — THE CADENCE THAT APPLIES WHEN NEITHER DIAL IS SET, and it is a new
    // constant rather than a new default for the two above BECAUSE A DEFAULT WOULD NOT HAVE
    // REACHED HIM. BepInEx writes every bound key into the cfg on first run and then keeps
    // what the file says: his dev.gloomhavenvr.wallfade.cfg already carries
    // 'EvalIntervalSeconds = 0' and his perf.cfg 'WallFadeEvalInterval = 0', so raising
    // Defaults.EvalIntervalSeconds would have shipped a number that every existing install
    // overrides back to 0 — a fix that runs only on a machine nobody is testing on. The
    // sentinel is what had to move: 0 has always meant "not set HERE" (see
    // WallFadeTuning.EffectiveEvalIntervalSeconds), and now that both doors read "not set"
    // the answer is this number instead of "every frame".
    //
    // 0.05 s = 20 Hz, and it is the user's own decision, taken on the measurement rather than
    // on a description (2026-09-05, offered with the cost and the price in one sentence and
    // answered "Auf 20 Hz stellen"): WallFade.Late cost 4.786 ms of an 11.11 ms frame budget
    // on EVERY frame of his large scenario, of which SplitRuns 2.03 + Decide 1.83 are the
    // decision this cadence gates. The bound it may not cross is the 0.20 s fade-in dwell —
    // at 0.05 four checks in a row must still agree before a wall goes transparent, and the
    // start of a fade moves by at most 45 ms, which is inside the fade's own animation.
    //
    // EVERY FRAME IS STILL REACHABLE and the description says so: any value at or below one
    // display frame (0.01 at 90 Hz) makes the gate open on every tick, because the gate is a
    // 'now >= next' compare and not a frame counter. That is why this could be done without
    // taking a control away from anyone.
    internal const float EvalCadenceWhenUnsetSeconds = 0.05f;
    // ModBuild 278 — the walk-in suspension (user request 2026-08-25: "In dem Modus in dem man
    // IM dem Level ist, kann das 'Abtasten' komplett deaktiviert werden so lange man in dem
    // Modus ist um hier auch Performance zu sparen."). Shipped ON: it IS the requested feature,
    // and it is safe by construction — while the walk-in latch holds, every wall is forced solid
    // by decree, so the decision, the coverage sampling and the rescan cadence are all computing
    // an answer the segment loop throws away one branch later.
    internal const bool WalkInSuspendSampling = true;        // => [WallFade] WalkInSuspendSampling
    // ModBuild 278 — the WHICH-RENDERERS census (see WallSegmentFadeCulprits.cs). It shipped ON
    // "because it is the whole point of the build": 28 of 33 commits in the ModBuild 277 log
    // fired on "the SCENE signature moved" and nothing shipped could say what moved it.
    //
    // ModBuild 284 — OFF, BECAUSE IT ANSWERED. The ModBuild 281 log gave the answer and it is
    // written down: of 38 refusals carrying named groups, 12 were triggered by nothing but the
    // MOD'S OWN OBJECTS — 'GloomhavenVR.Reticle_Right' (24), 'GloomhavenVR.Laser_Right' (11),
    // the loading indicator (2). The mod's own hand laser appearing costs a ~73-90 ms wall-table
    // rebuild, that is 32% of refusals, and the narrowing that removes it is derived in
    // .planning/perf/WALL-FADE-CLOSEOUT.md §7.2. The question this census exists to ask has been
    // asked and answered; leaving it on is the "a probe that answered is spent" entry, where a
    // readiness probe blitted every frame for 44,200 ticks after it had answered.
    //
    // THE INSTRUMENT AND ITS FOUR CI-VALIDATED CONTROLS ARE KEPT INTACT — this is a default, not
    // a deletion. Turn the key back on the day §7.2 is implemented: that is precisely the run
    // where it has to name, renderer by renderer, what the narrowing dropped.
    internal const bool SignatureCulpritCensus = false;      // => [WallFade] SignatureCulpritCensus  (pinned: ModBuild 284 turned this OFF deliberately — the question it exists to ask has been asked and answered, and it is the "a probe that answered is spent" entry. The 2026-08-26 cfg snapshot carries a value that PREDATES that decision, not a chosen one; turn it back on the day WALL-FADE-CLOSEOUT §7.2 is implemented)
    // ModBuild 281 (PERF B step 3) — the CHURN gate (see WallSegmentFade.CommitGate.cs). It
    // shipped ON for the same reason SignatureCulpritCensus above it did: PERF B removes the
    // ~95 ms commit frame by spreading it over ~63 frames, and the one thing that can go wrong
    // when it does is the old table — which is the UNDO LOG for every MaterialPropertyBlock this
    // subsystem has written — being dropped while a wall is still half-faded. This line counts
    // how often a commit actually does that, on his hardware, in his scenarios, read-only.
    // "Shipping it OFF would hand the tester a build that measures nothing."
    //
    // ModBuild 284 — OFF, BECAUSE THE BUILD IT MEASURES FOR IS NOT BEING BUILT. It measured: the
    // churn population is in .planning/perf/WALL-COMMIT-B-BUILD2.md with its cost (0.136 ms per
    // gate run at 127 segments / 2540 owned renderers, 0.346 ms at 4x), and PERF B build 2 stays
    // a PLAN — the wall topic is closed on the user's own report against ModBuild 283. The
    // sentence above is still true and is still the reason to flip this key back on: the day
    // anyone starts build 2, this is the first thing they turn on, and it is why the gate and
    // its controls are kept rather than deleted.
    internal const bool CommitTableGate = false;             // => [WallFade] CommitTableGate  (pinned: ModBuild 284 turned this OFF deliberately — PERF B build 2 stays a PLAN and the build it measures for is not being built. The 2026-08-26 cfg snapshot carries a value that PREDATES that decision)
    // ModBuild 281 (PERF B) — the per-frame budget of every SLICED rescan stage.
    //
    // 1.5 IS THE CONSTANT THAT SHIPPED, THREE TIMES OVER. It was `ClassifyBudgetMillis` in
    // WallSegmentFade.cs and `PrepareBudgetMillis` + `SurveyBudgetMillis` in
    // WallSegmentFade.Prepare.cs, three private consts whose own doc comments each said they
    // were deliberately the same number for the same reason ("the same shape of work — a
    // per-renderer derivation over a fixed population with no externally visible effect — at
    // the value this project has already validated on hardware for that shape"). Promoting them
    // to ONE dial is that statement made enforceable; the value does not move, so a fresh
    // install and an install that never opens the menu behave exactly as ModBuild 280 did. This
    // is a tuning surface, not a retune — the same rule the ModBuild 272 walk-in promotion and
    // the ModBuild 278 cadence promotion followed.
    //
    // WHY IT IS NAMED FOR THE SLICE AND NOT FOR A STAGE: it is also the budget the sliced COMMIT
    // will spend when PERF B lands, and 1.5 ms is the figure the ModBuild 228 and 271 logs show
    // the census holding at over 8-18 frames with no stall attributed to it. At that budget the
    // 94.8 ms commit becomes ~63 frames, about 0.70 s — well inside the 7.8 s of staleness the
    // shipped SKIP path already tolerates.
    internal const float SliceBudgetMillis = 1.5f;           // => [WallFade] SliceBudgetMillis
    // ModBuild 279 (Option A) — the FIGURE EXEMPTION on the skip signature. Shipped OFF, and the
    // OFF is the finding rather than caution: the design this came from rests on
    // PurgeFigureRenderers guaranteeing that no figure survives the commit, and read against the
    // source that guarantee has a hole — WallSegmentFade.PropUnit.cs:1212 reads a prop-unit
    // MEMBER's activeInHierarchy five lines before the figure refusal at :1267, so a figure
    // member's liveness really can decide whether a whole unit is refused. The narrowed
    // signature, its shadow comparison against the full one, its three counters and the
    // per-renderer culprit naming are ALL ON regardless: this build measures the narrowing's
    // yield and its safety on his hardware, and the dial is flipped in the build after, from the
    // log. See WallFadeTuning.FigureExemptSkipOn for the full argument.
    internal const bool FigureExemptSkip = false;            // => [WallFade] FigureExemptSkip
}
