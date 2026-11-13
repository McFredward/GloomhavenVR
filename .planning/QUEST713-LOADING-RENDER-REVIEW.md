# Quest initial loading indicator: renderer review (2026-10-19)

Base: `17cab93ae` (B712), worker `quest713-loading-render`. This review separates
actual device observations, source contracts, native fixture coverage and the
remaining Android pixel question. It does not establish a headset fix.

## Current hardware evidence

The newly supplied capture is extracted read-only at
`/home/claw/quest3-local/build/evidence/quest713-loading-review-20261019/capture/`.
The parent integrator binds it to installed B712 and the APK identity. The current
`quest-startup.log` SHA-256 is
`e59378799a210e03c7dec65cc44f7031acf2a8b7264d5230d1906df38c8a387d`.
The earlier B711 `160527` capture is corroborating evidence, not this test.
The B711 supplied screenshot `dev.gloomhavenvr.quest-20261019-175737.jpg` was
inspected; it depicts the ambient menu surface, not the loading interval, and
cannot prove loading visibility.

Current retained chronology:

- `16:49:29.263840Z`, frame961: the actual Bootstrap seam arms loading coverage.
  The head alone owns the XR backbuffer; native Main Camera owns the flat RT.
- `16:49:29.265032Z`: Intro's exact serialized PersistentOverlay Canvas is
  admitted with actor/animator scene `Intro`. This refutes an assumption that
  B712 never qualified that original Intro canvas.
- `16:49:29.267684Z`: native art is atlas15468 with the original trimmed UVs,
  a roughly0.22/0.25m disc at local `(0,-0.18,1.5)`, Sprites/Default and the
  expected initial fade/overlay minimum. The ordinary presentation lane caps
  immediately after this record.
- `16:49:47.229053Z`, frame1226: the ordinary native loader takes ownership
  after17.962s. The same root/material/atlas are alive, fade/basealpha1,
  overlayalpha0.28, head-local disc/view correct and both actual head-admission
  witnesses current at1225. Only the head is in the camera snapshot. This
  refutes an inactive-root, expired-C#-texture-reference or stale-head-only
  explanation at that boundary. It does not prove final eye pixels.
- `16:50:03.364002Z`, frame2044: the eventual fade retires with menu instance
  present. The menu-background probe is still black at its immediately preceding
  first sample; the later right-eye ambient capture contains pixels.
- The five added B712 continuity snapshots and unified loading-owner report are
  **absent**. They were not proof that the callbacks never ran.

## Actual draw chain

`WorldUIModule` ticks LoadingIndicator before FlatScreen. LoadingIndicator polls
native loading and the one-way Bootstrap seam, attaches a persistent owned root
to the current head, applies its0.30s fade, advances original stepped/glowing
artwork, then lets FlatScreen's current suppression policy run. The Quest menu
handoff keeps the indicator up independently while allowing menu capture to run;
otherwise waiting for a menu capture while suppressing its producer would deadlock.

Quest's boot art borrows the live native atlas directly. The owned material ->
texture -> live renderer path remains a native reachability edge across ordinary
`Resources.UnloadUnusedAssets`; it is not merely a retained managed wrapper. No
runtime Campaign `AssetBundle.Unload(true)` path was identified which invalidates
this exact atlas. Atlas15468 is still the same native object at handover and in
the later working scenario spinner. A source copy or atlas replacement is not
justified by this capture.

Both layers are ordinary MeshRenderers with private UV/color-bearing uploads;
base queue3000, overlay3001, coincident Quest plane, fixed ordinary Sprite shader.
The original trimmed drawing offsets rotate in mesh vertices, not in the root.
The head mask includes the mod layer, the disc is inside near/far clips, and the
retained current-head admission excludes a purely inactive/cull-mask hypothesis.
Sprites/Default still tests depth and premultiplies authored alpha. No captured
material/property-block state proves an erroneous alpha, flip or tint. The
later working consumer uses the same shader and atlas. Replacing this qualified
path with another shader is a new hardware hypothesis, not an established fix.

The native Intro PersistentOverlay and unified GlobalCanvas -> LoadingScreenGO
are independently authored opaque final UI draws. Such an overlay can cover a
MeshRenderer even if its OnWillRenderObject callback runs. Intro suppression is
reversible and ownership-qualified; unified suppression affects only that exact
loading widget's Graphic components, not sibling error UI, native animation,
load flags, callback or input ownership. The B712 current capture proves the
Intro owner was admitted. Its unified-suppression evidence was lost to the
logger defect below, so the actual intervening final overlay remains unresolved.

Read-only serialized scene review confirms Intro has one ordinary camera canvas
and its sorting1000 ScreenSpaceOverlay loading canvas; the unified serialized
loading object is a direct child of its own sorting1000 overlay canvas. Neither
qualified loading subtree has another authored nested canvas. Both original
native scripts and serialized scene records are preserved as evidence inputs;
there is no justification to disable every canvas or GlobalCanvas itself.

Native camera-plane video adapters target captured ordinary render textures;
they do not own the head backbuffer. FlatScreen's spectator end-of-frame clear
already excludes Quest. Current snapshots show the head's sole XR owner and
expected target, so another wholesale camera-clear rewrite is not justified.
The display distortion/full-viewport fixes, sound bank and both card guide
consumers must remain intact.

## Source-proven diagnostics defect repaired in this lane

`QuestGameStartupLog.IsPresentationDetail` accepted every `presentation ...`
record. Only exact `presentation spinner ` and `spinner-edge ` had a separate
admission path. Consequently new `spinner-continuity`, `spinner-intro-owner` and
`spinner-native-owner` went into the Intro-saturated general presentation lane.
The current B712 cap immediately after acquisition reproduces this blind spot.

The repair gives the exact six families (`spinner`, `spinner-edge`,
`spinner-continuity`, `spinner-intro-owner`, `spinner-native-owner`,
`spinner-final`) one finite16KiB/24record loading lane. Unknown spinner-like
streams do not qualify. General/provider/menu/loading presentation still sum to
64KiB/160records:8/20/20/16KiB and24/64/48/24records respectively. The
32KiB/96record startup and128KiB/64cause first-error reserves remain unchanged;
one lifecycle slot becomes the additional loading-lane cap marker. Global
256KiB/512record limits, deduplication, persistence-before-published counters and
thread safety remain enforced. No detailed stream was promoted to normal logs.

Use the exact `presentation spinner-final ` family for bounded final-eye output
facts; other new names require an explicit routing/test decision. The logger
bounds evidence, never the render path or loader.

Validation is external under
`/home/claw/quest3-local/build/evidence/quest713-loading-review-20261019/render/`:

- `scripts/quest-startup-log-tests.py`:111 production assertions and23 rejected
  causal defect controls (24 cases). Tests first saturate general, provider,
  menu, startup, ordinary and first-error lanes, then retain all exact loading
  families once. They cover finite identities/records/UTF8 bytes, unknown
  prefixes, independent later errors, concurrent callbacks and IO retry.
- Actual B712 noisy-log replay: baseline B712 source retains0/5 subsequent
  continuity records and loses native-owner/final-pixel facts; repaired source
  retains5/5 plus both additional facts. Baseline100629bytes versus repaired
  92979bytes, each within256KiB. These are logger results, not render outcomes.
- Every completed fixture case uses `cleanup_completed_run` after its synchronous
  console process has exited with reusable build/compiler servers disabled.
  Logs, source snapshots, results and cleanup receipts remain; generated
  bin/obj caches are removed. No Unity import, game conversion or APK ran here.

## Remaining proof required

Existing lifecycle tests have useful native causality: actual Bootstrap and
Intro callbacks, real scene destruction/asset collection, native Sprite inputs,
and final desktop overlay screenshots which show an opaque overlay cover the
same admitted disc. They do **not** prove Android MultiPass eye submission.
Their normal head uses a disabled mono camera manually rendering an RT and35deg
FOV, versus the device's110deg XR head. The readiness fixture also accepted
synthetic, nonempty Graphic state without requiring original native artwork to
contribute final pixels. These differences must not be described as device
visual success.

The integrator owns the separate readiness/frame-completion review. Its test
must falsify early retirement on transparent artwork, empty text or an offscreen
canvas, and distinguish capture production from actual final head submission.
For the long pre-menu gap, retain the exact original sprite sample and bounded
final eye-region observations across the actual native overlay/camera handoff.
A camera-admission counter alone cannot decide whether a later UI resolve or a
shader/depth/eye binding removed those pixels. Preserve normal native startup,
use no timer to declare the menu visible, and report unsupported pixel evidence
explicitly rather than silently substituting active-object state.
