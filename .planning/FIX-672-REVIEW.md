# Build672: Frame launch setup and healthbar foreground occlusion

Integration base: `0bd3cb27dbd35f1038848901d4772783613f7e35` (`dev`).
Semantic version remains the already bumped development version `1.1.1`.
This is a development repair, not publication of another release or modification
of the published `1.1.0` archive.

## Frame setup

The reported installation created the `GloomhavenVR` shortcut but left the
original Gloomhaven launch options absent. Both the published archive and source
generate the complete `WINEDLLOVERRIDES="winhttp=n,b" %command%` line. A replay
using the production configuration helper reproduces the missing-options state:
running Steam flushes its cached old `localconfig.vdf` after the installer has
written and verified the new file; the separate shortcut file remains intact.
The reporter's actual Frame setup log is unavailable. The supplied hardware
files are earlier Windows/flat-host evidence, so this is a reproduced failure
mechanism and a source-proven repair, not attribution of an observed Frame log.

Never write original-app launch options while Steam is running. Offline setup
retains the automatic, atomic, backed-up edit. During a live Frame session, setup
prepares the shortcut and exact merged launch-option line, persists a pending
receipt, and directs the player to save the line in Steam's own Gloomhaven
properties before restarting the Frame. It no longer terminates Steam and
claims an external edit survived. The generated launcher validates the options
and session transition before launching App780290 with `--gloomhavenvr`; pending
or invalid setup produces actionable guidance rather than silently starting flat.

The receipt records the Linux boot identity and Steam PID/start ticks. A verified
new boot retires the old session even if PIDs are reused. Valid equivalent quoting
and surrounding whitespace are accepted without rewriting the saved file.
Unknown, corrupt and abandoned receipts remain recoverable through setup and
the native save/restart flow. Existing custom options, account selection,
artwork, saves, preferences and the opt-in marker behavior are preserved.

Live setup therefore includes one native Steam UI completion step. It is not
advertised as fully automatic on a running Steam client. Previous attempts to
finish in a user service or detached worker after the Frame UI restart were
unreliable and are not reused.

## Healthbar contract and defaults

`Defaults.WorldUI.BarsOccluded` is already `true` on both PC and Frame, including
the published `1.1.0` source. The UI label is **Hide bars behind walls**: enabled
means through-wall visibility is off. ActorBars uses the common binding, with no
Frame/profile override. Existing saved choices remain the user's choices; no
default flip or preference migration is required.

The optional through-wall mode previously assigned `ZTest Always` to cloned
native bar materials. That ignores every depth blocker, including cards, hands
and the wrist board. Ordinary wall-hidden mode retains its native depth behavior.
Through-wall mode must keep the whole native widget's circle, text and inline
symbols visible through world walls while respecting nearer solid VR foreground.
It must also preserve authored transparent/ghost behavior, masking, font
appearance, native material replacements and restoration.

The repair uses the actual foreground geometry and its original visible shader
passes, without a world-renderer census or replacement silhouette geometry.
Native held figures and props are selected only while held, using their existing
read-only hold registries. MR capture registers existing authored backing meshes;
it creates no new room or play-area geometry. Native UI/TMP program derivatives
add only the selective foreground-depth comparison on owned bar material clones.
Shared font materials and other native widgets remain untouched.

Native TMP reparses its inline sprite bindings after health updates. A listener
owned by each adopted bar reapplies the existing material clones after that
native update and is removed on restore. New native material sources remain
authoritative. Missing shader/capture resources retain ordinary depth until
recovery; they never reactivate an unconditional overlay.

The shipped main bank was rebuilt with game-exact Unity2021.3.5f1. It grows from
533 to539 assets with no removals: four bar shaders, their source provenance and
the existing WorldSimpleMaterial source included by the canonical baker. All
other prebuilt banks are byte-unchanged. This repair needs the updated main
bundle together with the plugin, not a DLL-only installation.

## Validation scope

Workers use separate initialized worktrees from the integration base. Evidence,
failed controls and source manifests are retained; regenerable owned caches are
removed after each completed/reaped run through `cleanup_completed_run`.

This bounded repair uses focused checks. The unchanged areas inherit Build669's
composed 216/216 local gate, Build670's focused repairs, Build671's source/golden
checks and the trusted source-tree CI evidence for the integration base. A new
focused `--suite` pass is not a new complete local gate.

Final integration source16/16, strict Debug/Release0 errors/0 warnings, bilingual
docs5 pairs, all bank-format checks and1594 figure derivatives pass. Direct
original golden vectors pass300518 assertions; eight additions verify the four
new bank mappings. The six focused existing suites pass (pose, MR backing,
wrist board, shared UI, Frame MR presentation/options); the final unchanged
player-help lookup passes2305 assertions and all eight controls. Their passing
evidence is retained rather than repeating unchanged broad suites.

The final selective-depth GL fixture passes90 runtime/pixel assertions and all11
causal negative controls (12 variants). It uses the shipped game's original
TMP assembly, glyph/atlas/sprite data and all49 float/10 color/5 texture bindings
of native Sarala material202, independently verified against the original game.
Original matching upstream shader programs provide the GL rendering reference;
this GL comparison does not execute the game's Windows-only native shader bank.
The four derivatives independently reduce to their exact upstream source hashes
after undoing only the documented depth addition and runtime variant retention.
Independent real-GPU subset controls verify19 assertions, including fractional
viewports at origin0 and11/7 with byte-identical native RGBA results. Perspective,
near/far/partial occlusion, TMP reparsing, original transparent/ghost behavior,
native held objects and failure/recovery are covered. Earlier failed diagnostic
and fixture-binding attempts remain recorded, not counted as product passes.

Compiled comparison is1255 ->1257 C# types, no removals: only
ActorBarDepthShaders and HealthbarForegroundDepth are added. Thirteen existing
types differ: eight inlined ModBuild consumers and five implementation types
(BundleShaders mappings, Loc help, VRLayers/MrBacking registration, ActorBars
material/capture lifecycle). The pose arithmetic and native gameplay paths
remain unchanged. Public surfaces are exactly666 config keys/236 patches/4795
log tokens; wire Version3/next-free118 remain unchanged.

Evidence lives under `.planning/debug/fix672/`: source-final, focused-checkpoint,
help-final, direct run-cibv9e7g golden output, strict build logs, compiled-delta,
surface-final and hash-verified worker archives. The source runner's first final
invocation rejected a pre-created output log before executing any suite; the
empty-directory retry completed all16 without changing the test contracts.

Final Windows/D3D11 execution passes the same90 product assertions against the
actual shipped bank SHA256
`4bf4c81a28df158ba3620e6de2fff4480f31ab6ec0f86fd0872985b6ac05d0f6`.
The game-exact2021.3.5f1 Windows player runs under private Wine/software rendering
and positively reports Direct3D11, reversed Z, clear0 and near depth0.92964828.
This is product rendering evidence, separate from the earlier four environment
assertions. The GL eleven controls are retained rather than duplicated on D3D.
Missing engine AssetBundle support and a stripped fixture-only Unlit/Color shader
caused two earlier infrastructure attempts to fail before product coverage;
the final source-bound case and production bank are unchanged. See the archived
`health-d3d/run-o680amk2/product-receipt.json` and native PNG/result evidence.

Fresh source-changing dev CI will run after push. Its exact source/status is
recorded separately; this local focused evidence does not claim a hosted CI pass.

Actual Frame installation and headset foreground/stereo pictures still require
hardware verification. Automated source and native-render fixtures do not prove
an HMD image or headset frame rate.
