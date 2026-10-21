# Quest B717 initial loading presentation review

## What the hardware actually establishes

The current capture is the B716 / assembly1.2.0 startup in
`quest717-review-20261021/capture`. Its startup banner identifies owned input
`d4e76da00ec2`. The accompanying video starts inside the scenario; it is evidence
for the separate particle/performance reports and does not show this startup
interval. The previous log is B712 and must not be treated as another B716 run.

The current spinner arms at16:15:13.956Z/frame969. Native scene loading takes
over at16:15:31.064/frame1226. Five continuity snapshots show fade1, an active
renderer with the correct atlas/pivot/UVs, the current head admission and a valid
head-local position. The final snapshot is16:16:03.978/frame3225, fifty seconds
after arming. Actual `fully-faded-hide` is only16:16:44.268/frame5756, ninety
seconds after arming: the presentation safety bound, not early native loading
completion. Thus another longer lifetime flag cannot explain or repair the
reported early visual loss.

The same native atlas instance15468 and trimmed UVs are used by the later
hardware-working scenario spinner. Retention of a C# reference, a live material,
camera admission and nonzero alpha each establish only their own boundary.
None establish the final Quest image. The menu consumer already has nonblack
pixels at16:15:53.879, while no successful initial-menu receipt is retained.

## Prior attempts and what their tests did not prove

| Family / first relevant build | Attempt and evidence | Remaining failure |
| --- | --- | --- |
| Primitive upload, B680/B681 | Rebuild the owned quad with explicit COLOR and preserved native trim/pivot. This repairs a real missing shader input. | The initial spinner still disappears on hardware; working later loads do not qualify the initial path. |
| Persistent art, B685 | Skip the untrusted Quest PNG/ReadPixels cache and read the live Intro sprites. Desktop caching remains separate. | Cached art was an independent risk; avoiding it did not close the initial black gap. |
| Source lifetime, B686/B687 | A GPU-owned boot copy passed host tests but remained invisible on device. Return to the exact native Texture2D/UV consumer used by the working scenario spinner. | The live native atlas survives the unload. A merely expired wrapper is inconsistent with the later retained facts. No further speculative atlas/copy swap is warranted. |
| Head lifecycle, B688/B689 | Retain/reparent/rebuild across actual head replacement and add bounded lifecycle/admission facts. | Active root/current camera/full alpha remain true while the user sees no symbol. Admission is not fragment evidence. |
| Native final UI, B690 | Reversibly disable the exact Intro loading Canvas while the VR copy presents it. Real final GameView draws prove that its opaque ScreenSpaceOverlay covers an otherwise admitted spinner. | This repairs a source-proven covering draw, but later headset reports remain negative. |
| Native replacement UI, B691/B692 | Suppress only `GlobalCanvas -> LoadingScreenGO` graphics, preserving siblings, native animator/callbacks/loading and legitimate camera/nesting changes. | More owner qualification does not guarantee a visible world HUD throughout composition. |
| Lifetime handoff, B693/B712 | Keep a display-only latch past native `IsLoading`/menu-instance creation until an actual original window and head consumer submit. | Native flags genuinely precede the picture. Extending the latch alone did not close the device gap. |
| Pixel qualification, B713 | Require private source eligibility and two asynchronous consumer draws; reject blank, offscreen, overwritten and foreign-only captures. Reserve bounded loading logs. | Host fixtures prove their graphics graph, not Android MultiPass/Adreno/final eye composition. Private stencil and low-resolution sampling assumptions were still wrong. |
| Main1.2.0 rebase, B716 | Preserve those Quest changes while adopting the release's persistent rig/menu anchor. | The user again reports early visual loss. Current logs show the presentation remained alive until its safety cap. |

These are several independent defects and hypotheses, not evidence that every
preceding repair was unnecessary. Earlier green checks were overstated if read
as headset acceptance: they encoded narrower lifetime, native owner or mono
camera boundaries and did not observe the failing final device picture.

## Findings this round

### The previous source-pixel instrument can report a false zero

The old48x32 probe rendered the whole Quest eye. The0.25m disc at1.5m spans
about9.5degrees, only approximately three probe pixels at the device's field of
view. Real native-art rendering at110degrees and2728:2858 aspect reproduces
zero visible samples from an intact rotating thin ring. Therefore B716's
`visiblePixels=0` cannot be promoted to proof that its atlas or material died.
The source probe now fits the exact same consumer bounds into its private
target. Its log explicitly says `framing=exact-consumer-bounds` and still says
`finalXrPixelsObserved=false`.

### Private source eligibility lost the native stencil context

A genuine native Mask derives an Equal-stencil child Image material. Replaying
that derived material into a new empty stencil target, without its parent mask,
can reject every eligible pixel while the real menu is visible. The private
eligibility draw now clones that one derived material and neutralizes its
comparison only in the owned private target. The native material, CanvasRenderer
mask/cull/inherited alpha, actual capture and actual consumer proof remain
unchanged. Unknown or unrelated visible UI still cannot stand in for a blank
menu Image. This repairs a possible endless presentation receipt, not the
initial HUD's final-eye visibility by itself.

### Pending readback targets need native lifetime ownership

The actual lifecycle fixture emitted two MissingReferenceExceptions when
`Resources.UnloadUnusedAssets` collected a private target while its callback
held only a managed wrapper. These owned private targets now use
HideAndDontSave until their existing callback releases and destroys them.
No borrowed native atlas or game asset flags change.

## Best next visual attempt and its limit

Keep the exact native art, trimmed UVs, signed step/glow animation and fade.
Make only the initial Quest HUD's foreground composition explicit, through the
menu handoff: a late owned draw with depth writes off and depth test Always.
The small authored `QuestLoadingSpinner` shader preserves native Sprite
premultiplication and Blend One, including alpha for passthrough. Reusing the
ordinary straight-alpha Overlay shader would square coverage in the alpha
channel; an intermediate RGB-only comparison did not establish full equivalence
and is not the final implementation. Resolution uses the separately qualified
tiny embedded Android bank through `QuestLoadingAssets.ResolveShader`, not a
cold-load-sensitive bare Shader.Find. The bank is embedded in the updated mod
assembly so the original Campaign and all four existing authored mod banks
remain reusable. The Linux graphics fixture compiles the exact shader source;
Android bank loading is an explicit separate boundary.
This prevents a retained depth writer or a newly appearing blank menu capture
from covering the live symbol. Desktop and later native scenario spinners keep
their established path. No new timer, loading delay, camera clear, eye viewport
or continuation mutation is included.

This changes the relevant rendering boundary instead of adding another lifetime
flag. Real native-rendering controls establish that both depth and late blank
captures defeat the old draw while the foreground remains visible. Existing
actual ScreenSpaceOverlay controls remain necessary: a world HUD cannot outrank
a later native overlay simply by increasing its material queue.

It remains a targeted hardware hypothesis until a new Quest run confirms the
whole Intro-to-visible-menu interval. If that fails, the next evidence must be
an actual eye-region capture after final head composition, not another active
GameObject counter or timeout. The private source probe never claims to read
the XR compositor.

## Validation record

Focused source, production native-rendering, bad-composition controls, masked
native-menu proof and original Intro/Bootstrap/window/Single-load/asset-collection
boundaries are retained externally under
`/home/claw/quest3-local/build/evidence/quest717-review-20261021/loading/`.
Every process tree is stopped/reaped before `cleanup_completed_run`; preserve
the initial failed/intermediate evidence as well as final results. No full-game
import, depth generation, Campaign rebuild, complete gameplay suite or new
headset acceptance is claimed by this lane.
