# Build736: character-window visibility, native text and menu pacing

## Report and immutable hardware inputs

The maintainer reports that the Build734 character window disappears while
pitching the head and survives only within a narrow viewing angle. Card names
and button text flicker strongly; other windows appear unaffected. A second
observation concerns trailing hands while waving in the main menu, with normal
motion on the map and in scenarios according to the maintainer.

Both fresh local logs identify Build734/c0f636c55, built 2026-10-28 07:08:30 UTC,
Windows D3D11 on an RTX4090, SteamVR/OpenXR MultiPass. The XR target is 4040x4040
ARGB32 with 1 sample; QualitySettings requests 8x MSAA. Remote717 logs are stale,
not paired evidence. No new screenshot/video accompanies this report. Six exact
log copies, sizes and SHA256 hashes are retained under ignored
`debug/menu736/hardware-inputs` and `round-start.json`.

The supplied logs show a main-menu plateau of 223 frames per 10 seconds, followed
by substantially faster map intervals. They contain no scenario phase; the
scenario comparison is the user's observation. GPU timing is unavailable, not
zero. The blocked frame interval alone cannot distinguish GPU work, engine work
and compositor pacing. Ordinary hand pose updates remain valid; there is no
source evidence for a special menu smoothing/filtering path or accumulating
hand materials.

## Three bounded presentation repairs

The inverse used to reconstruct sampled head depth included the render-target
projection's Y flip on D3D-like graphics APIs. The shader samples canonical
Unity texture UVs, so those rows describe different coordinates. A vertical
mirror can still intersect a symmetrical window at zero pitch; rotating the
head moves that reconstructed ray outside the real window and its frame clip.
Normalize only the GPU projection Y row before inversion in the display and
character surface. Preserve the native depth/reversed-Z row, original window
pose, original raster resolution and camera state. Shaders and asset bundles
are unchanged.

The new head-only card backgrounds write depth on the same authored plane as
their original TMP titles and action-button text. Distinct vertex programs then
fight over coplanar depth. An actual head-capture witness loses 153 of 419 bright
original TMP pixels before the final compositor. Move only the backing mesh
away from the current eye by 1/4096 of its physical width during its live head
lease. Include native XY-only fitting and rear viewing, then restore the ordinary
mesh. Original text/card/backing transforms, native materials, input and real
foreground depth remain authoritative.

Read-only native prefab enumeration confirms that both actual mouse/gamepad
card names and Full default-action labels belong to the moved Full/Mini roots.
The Full title is a child of the Header backing itself, which is why this repair
changes vertices rather than its Transform. The single outer breakdown TMP is
not a card-name label. The added controlled Button-label witness is explicitly
a synthetic coplanar case, not a complete native-prefab reproduction.

The original desktop capture stack can retain an active UI camera without a
physical Canvas mapping. Previously that late failure admitted and discarded
both large head-eye targets before restoring native mono UI, on every frame.
Validate the entire current stack before any extra target or partial source
preparation. Preserve all native cameras, callbacks, mono output and input;
resume stereo on the first ready frame when a real Canvas arrives. Do not drop
an unmapped camera, lower quality, delay readiness or throttle capture.

The logs also expose a destroyed preview effect entering BaseMeshEffect's native
Graphic getter during retirement. Source disposal now marks its lifetime first
and releases calibration and target reservations in finally; destroyed effect
wrappers never query their native component. This prevents a failed retirement
from leaking the source's scene resources.

## Main-menu hand investigation and rejected change

The main-menu allocation loop is a concrete unwanted workload in the reported
phase. Its removal is source/coordinator proven; whether it fully resolves
the reported hand trailing still requires headset timing and appearance.
No tracking, hand animation, capture-rate or resolution changes are made.

A candidate that prevented the owned menu head from inheriting Intro HDR was
withdrawn. The first pixel probe was misleading: correct native material values
and the supplied QAA8/ARGB32-RTAA1 configuration reveal 1015 differing pixels out
of 65536, maximum channel delta 223. HDR-permitted rendering retains fractional
MSAA coverage, while the proposed LDR route produces binary coverage. Keep the
original HDR/MSAA behavior. No quality tradeoff is authorized by the report.
The native video-camera feedback hypothesis was also rejected: the final culling
guard excludes mod visuals, and that authored camera faces away from the hands.

The raw Player log contains 44160 oversized-RenderTexture messages after opening
New Party on the map. The preceding Build729 input already contains 38640 such
messages. Their allocation dimensions/caller are not recorded, so they are not
claimed as a new Build734 cause or as fixed by the hand audit.

## Validation and delivery record

Test class B: bounded repairs to the existing menu-depth and native desktop
presentation paths. Reuse source-bound Build734 coverage for unchanged scopes;
execute the containing renderer/lifecycle suites and native-playback consumers
against the integrated source. The previous failed complete invocation remains
failed; inherited aggregate coverage is not a new complete-gate pass.

The original whole-window motion case always looked back at the window while
changing position. New stationary-eye pitch samples explicitly vary head rotation
independently. Focused actual-coordinator checks pass 245 ordinary GL and 245
explicit top-origin projection-input assertions, plus 15 preflight assertions and causal
controls. The explicit backend port retains GL depth; it is not a D3D driver.
Destroyed RawImage/surface cleanup passes 40 assertions plus native-close checks
and removal controls. Native Vulkan figure portability is recorded separately;
an inherited/source diagnostic does not replace a passing pixel oracle.

The complete card worker suite passes 2696 assertions/20 controls on GL, retaining
the previous 1990/17 and adding actual head title/button pixels. The new affected
case passes 706/3 on Intel Vulkan with reversed-Z: 64 front/rear/pitched/subpixel
and style samples lose zero title/button pixels in both the private UI raster
and final head output. Background separation removal, wrong rear direction and
missing mesh restoration fail independently. Every completed attempt, including
fixture/API and Vulkan initialization failures, retains source/log/result/capture
evidence and a completion-cleanup receipt.

The final twelve-suite union records eleven passes and one fixture-anchor failure
in 506.6 seconds, with 2821 unchanged source/input pins. The decorative-plane negative
control still targeted the old expression before the separate backing bias was
added. Port only that anchor, retain the bias while removing the decorative depth,
and rerun its complete containing suite: all graphic cases and controls pass in
41.8 seconds. No product change or weakened oracle follows that union.

Strict actual-runtime Debug/Release and metadata Release pass with 0 warnings/0 errors.
All 16 source gates pass in 22.4 seconds. Compiled comparison against Build734 confines behavior to six
menu presentation types, with ten build identity/number differences and no added
or removed types. Rig source and the 73108-byte native card shader bank remain
byte-identical to the published baseline. The verified current 264-suite coverage
map is `debug/menu736/validation-final.json`: final affected 12 with the graphic
repair, fresh source gates, source-bound published c0f town-close 113/13 and
unchanged Build734 coverage for the remaining scopes. It is aggregate coverage,
not a new complete-gate pass. All 306431 prior goldens retain 261 unchanged compiled
source inputs; only protocol comments/whitespace/build number differ. No fresh
Build736-stamped golden executable is claimed.

An optional Vulkan full-figure checker still falls below its unchanged correlation
threshold (.958913 versus .97). Its first actual RGBA image is byte-identical to
the historical c0 colour path in a private diagnostic that disables only final
frame clipping to expose colour. This is retained as an inherited backend/
reference precision limitation, not a passing whole-figure Vulkan result. It
does not invalidate the separate actual Vulkan card-glyph passes, and no further
checker tuning, tolerance change or platform campaign is included.

Delivery is direct `origin/dev` source, Build736; Build735 is already used by the Quest
standalone branch. The Windows installer builds its archive. Preserve all
evidence while retiring the three clean worker worktrees, cleaning generated
project caches and stopping only this round's private ADB after its final run.
Actual D3D11/native card-effect pixels, headset window appearance and joystick/
hand pacing remain distinct hardware acceptance, not inferred from GL/Vulkan.
