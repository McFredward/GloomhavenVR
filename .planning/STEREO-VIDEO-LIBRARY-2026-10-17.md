# Native stereo library and scenario review

## Authorized behavior

The maintainer explicitly approved Build687's GloomhavenVR intro in the headset
on2026-10-17 and requested a little more foreground depth for its wordmark, all
remaining films, and individual scenario review actions under Advanced > Test
triggers. The later explicit exclusion of `gameplay_trailer` overrides the
initial all-films request. It remains an unchanged native promotional source and
has no converted companion or review action.

The catalogue has20 subjects: the approved mod intro,11 hero unlock films,7
ambient films and the original TwinSails studio clip. The serialized promotional
handlers have `showAtStart=false`; no ordinary code or serialized menu event was
found to start the trailer. This is source/asset evidence, not an observed play.
See [the media contract](../docs/stereo-video-library.md) for source authority.

Normal native callers select their exact stereo revision before the first
Play/Prepare. The original native decoder retains audio, time and continuation.
The studio player and native ambient player both serialize playOnAwake=false.
Studio retains its original4s timer and subsequent independent sprite animation;
the complete8s film may be reviewed deliberately. Native ambient looping remains.

Deliberate review is scenario-only and uses the original common grabbable movie
window, per-eye aspect, shared clock and pose. Its owned cosmetic decoder has no
native game callback, unlock, promotion counter or save mutation. A genuine game
video always takes priority. No request is queued behind a busy source. Leaving
the scene or losing the actual world camera cancels owned playback/preparation.

Close and All-off express GUI intent separately from local lifecycle cleanup.
The finite `Review/` namespace identifies the selected canonical or exact stereo
revision on existing72. Review-close requests use existing Native=false/Closed=true
flags, the exact clip/token and author/opening pose key, with finite retries.
They cannot skip a replacement opening or a genuine native film. No byte layout
or record id changes. Mirrors cannot originate another review loop.

## Intro wordmark revision

The approved2D source stays SHA256
`22fc9f48d4da6783fed379122e8c5675c4149afd9c0bc90550a9e4e22696fec5`.
IntroSBSv2 is93,361,756B/SHA256
`5514e50b99c5c70cb57c7887eb97ed333371f04a9eb6dbc7d731b40a3817fcca`.
All21 scenic depth files and scenic geometry records match the approved687
revision exactly. Only the two existing GloomhavenVR wordmark appearances change
from the screen plane to a rigid+12pixel source-pixel foreground plane, feathered
outside the printed artwork and following the original fire-title reveal.

All2931 frames/PTS and2291 AAC packet payloads/timestamps pass verification.
The same34dB native-YUV420 picture threshold passes every left frame and78
independently reprojected right witnesses (minimum35.32/36.06dB). Thirteen wordmark
geometry checks include three negative controls. Actual decoded opening and
fire-title pictures were statically inspected. See
[the intro contract](../docs/stereo-video-intro.md) and adjacent provenance.

## Integration and delivery boundary

Independent workers were initialized from dev9f48730df with disjoint media,
runtime and localized UI ownership. The integrator owns native patch registration,
inventory, version notes, installer roster, private baseline and final gate.
Worker branches are not published. The independent688/689/691/693 identity
repairs and694 loading/window-bounds repair are retained through their separate
publication lane. Builds690 and692 are internal source candidates only.
Published694 is merged; final stereo handover uses695 or later. Its exact
unchanged focused source/artifact/raster evidence is composed separately; the
reported residual headset window softness remains open in that lane.

Movies are explicit separate Content beside the DLL. `packaging/movie-files.txt`
names22 files:20 stereo subjects, the approved mono intro and original mono
studio companion. Native hero/ambient originals remain read-only game inputs.
The normal installer requires all22 files before deployment or archive mutation.
Only the two exact retired calibration MP4s and introSBSv1 are retired after
success. A source-only build can omit unfinished optional media; that build is
not a complete installable library. No additional ZIP or HTTP publication is
part of this handover; the user's ordinary install script creates its archive.

## Evidence status while conversion continues

Retained root evidence lives under `.planning/debug/stereo-library688/`; the
directory name is preserved across later build-number reservations. The exact
hardware687 logs/review, logo render/verification and decoded cue reviews live
there. The completed runtime and UI workers are archived in the main checkout as
`stereo-library-runtime-worker-b89af9256/` and
`stereo-library-ui-worker-e3a551635/`. The integrator verified every retained
file's SHA256 and size before removing either worker worktree. Active media
evidence remains in its worker until its producers and verification stop.

Completed source-side checks include22-file real-script fixture cases
(192Python/209PowerShell assertions), source-bound localized UI875 assertions and
four affected close controls in a combined strict net472 Release0W/0E tree, and
129 merged library geometry/inventory checks with33 refusal controls. Prior broad
options/close evidence is reused for unchanged areas; those focused checks are
not a complete gate. The frozen movie GPU fixture inventory is226local/100CI/16source.
The full integrated attempt against frozen499bdc157/692 has finished: all 16
source suites pass; the local attempt records 218 passes and eight failures
across all 226 suites in 6161.391 seconds. Its authoritative result remains FAIL.
Six subsequent tests-only fixture repairs have separate focused passing evidence;
two original suites await their required media/load conditions. The four separately checked694
additions make the current inventory230local/101CI/16source. Its explicit
scheduler inventory assertions are updated and pass19 cases (17 pass, two
platform skips); no scheduler logic or registry behavior changed in that repair.
The local group exposed an older town-service-options fixture missing explicit
SteamLink availability, TickGuard and Rebuild ports. Tests-only0796670ba repairs
that boundary; the affected suite passes173 assertions and9 causal controls.
Two further tests-only repairs are integrated: desktop-render-runtime now declares the explicit inactive
movie-plane ports, with fail-fast behavior for an unexpected active borrow;
145 real-Unity assertions and33 causal controls pass. The options-notes-runtime
fixture binds the exact current GameObject return/minLines method, forwards and
checks the returned row, and passes470 native Unity/TMP assertions with its
three unchanged causal controls. All953 desktop and1,623 note archive files were
independently verified by SHA256 and size before either worker was removed.
The four retained EN/DE narrow/wide note captures were also inspected. The
actual stereo-movie-render GPU fixture passes44 assertions and eight causal
controls in the frozen full attempt; this covers supplied decoded rasters under
Unity2021.3.5/OpenGLCore, not native video decoding or headset presentation.
The fourth old fixture boundary, frame-mr-options, now declares an explicitly
unavailable SteamLink input for its existing direct-Frame/legacy-PC cases.
Its unchanged production UI policy/callback/tint/tooltip sources pass84 actual
Unity assertions and all six original causal controls. The original missing-port
failure and an overbroad worker guard attempt remain retained; only the supported
named edit dispatches are forwarded. All342 indexed evidence files and343 archive
files were verified independently before removing the stopped worker. This proof
does not execute SteamLink discovery or whole-menu reconstruction.
The wall-options boundary likewise supplies unavailable SteamLink for its
existing direct-Frame/legacy configuration cases. Its unchanged program and
production inputs pass 77 real-BepInEx assertions and all 15 original controls;
all 357 indexed evidence files and 358 archive entries were independently checked.
The sixth repair, scenario-scenery-runtime, uses its existing independent queue
drain before checking loading-work readiness. A real four-millisecond/96-node
budget cannot guarantee a drained queue after a fixed 100 updates under load.
Controlled external process preemption reproduces the exact assertion with 1452
pending nodes. The corrected case drains 1432 to zero without advancing its
scenery clock, clearing watchers or changing production source/budgets. All 924
assertions and 51 original controls pass; the four generated production source
files and control output remain byte-identical. The historical frozen failure
has no queue dump, so its precise pending state remains unknown. All 6414 retained
evidence files were independently verified before removing the stopped worker.

The unrelated NPC639 packed
atlas proof reaches its cold895-member resolution budget and fails the1s bound
under concurrent inference/full-gate load; its exact elapsed time was not logged.
Its production source is unchanged, and no cause is assigned. Retain the failure
and repeat only that suite after the neural producers stop, without weakening
the budget.
The unchanged figure-mesh-bank-packaging suite passes 19 of its 20 cases; the
whole-installer case correctly stops at the production missing-movie preflight
before reaching its figure-bank assertions. Repeat that original suite only
after all 22 real media files are present. No placeholder asset, reordered
preflight or weakened packaging assertion is authorized.

Required independent continuation checks pass: exact wire vectors300597
assertions, bundle-format/bank checks and the surface comparison with no removed
config keys, log markers or patch registrations. The actual frozen692 Release
DLL was decompiled and its21 changed/9 added files reviewed. Eight unrelated
Net/Plugin types differ only by the inlined build number, BuildInfo differs only
by branch metadata, and remaining changes match the movie work or separately
published identity fixes/resources. Private continuation invocation errors and
their corrected remaining stages are retained. These are composed results, not
a new complete-gate pass.

At this checkpoint the intro, studio, all seven ambient films, Beast Tyrant,
Doomstalker and Plagueherald are verified: 12 subjects complete, eight hero films
remain. Crypt retains all 2252 native frames at 60 fps, silence and zero-disparity
loop endpoints; all left pictures and 21 right witnesses pass with minima
43.46/45.08 dB. Worker and integrator inspected three actual encoded cue pairs.
Each subject must pass original native-clock/AAC,
all-left-picture, right reprojection, depth receipt/hash and decoded-cue gates
before its output enters Assets. Desktop cue inspection is distinct from actual
headset playback. Independent inspection of all11 original hero title frames
found that generic protected regions missed some printed symbol edges and banner
lines, and flattened scenery before delayed printed reveals. Integrated timed,
source-specific banner/symbol regions now preserve oversized original zoom
reveals and smooth preparation/release. Main geometry/inventory checks pass129
with33 refusal controls. All seven ambient movie objects and all neural inputs
match the retained pre-repair manifest. Forty-nine deterministic numerical
ambient witnesses and four actual pinned Beast Tyrant source/depth witnesses
confirm the bounded change; these expected reprojections are separate from the
encoded-deliverable inspection. The approved mono intro's source byte count was
also corrected, retaining the separate native original byte count and both hashes.

The first Beast Tyrant encode was correctly refused before asset publication:
its747 video frames were complete, but simultaneous frame-bounded encoding had
copied583 of the native584 AAC packets. The integrated audio finalizer copies the
existing H264 bitstream and all original AAC packets, then restores the native
MOV edit-list presentation boundary without changing packet payloads, timestamps
or durations. A plain copy mux would otherwise extend the ending by8.667ms.
Main-checkout real-media proof verifies584 unchanged AAC packets,747 unchanged
video packets and the original249/20s presentation, with four causal refusal
controls. All11 original hero sources also pass the strict supported native
edit-layout/clock preflight, retaining their7,312 actual AAC packets. The refused
candidate, raw mux, source snapshots, interrupted-child reaping proof and final
receipts are preserved. No picture or neural input was changed by this repair.
The delivered Beast Tyrant passes all747 left pictures/timestamps and44 encoded
right witnesses, with minima39.75/44.63dB. Both worker and integrator inspected its
three normal decoded cue pairs and actual enlarged-emblem pictures at604/606/612;
the native606 picture was also compared. These are static encoded-picture checks.

The initial Doomstalker candidate is also correctly refused before publication:
its 17 Mbps encoder ceiling is saturated (16.837 Mbps average), and native left
frames 360–362 reach only 33.09/33.64/33.75 dB against the unchanged 34 dB floor.
The bounded encoder repair raises only future hero renders to a 34 Mbps maximum
and 68 Mbit buffer at the same CRF17. Ambient/studio retain 17/34; every completed
asset, canonical manifest, neural input, transform, clock, AAC rule, quality floor
and strict 100 MiB limit remains unchanged. Actual encoder policy is recorded in
new render receipts and tracked provenance. The integrator verified all 74
retained failed-render/verification files and unchanged completed outputs before
authorizing the single serial scheduler restart. The replacement passes all 868
native frames, 679 original AAC packets, its exact 217/15-second presentation and
44 right witnesses, with minima 38.66/41.82 dB. The three previously failing frames
now reach 38.66/39.49/39.44 dB. The exact encoder commands differ only in their two
VBV arguments; all 46 native snapshot, geometry and uncompressed-source witness
files match byte-for-byte. Worker and integrator inspected actual cues144/359/805.
Plagueherald also retains all 906 frames/708 AAC packets and its exact 15.1-second
presentation, with minima 44.10/45.00 dB and three inspected decoded cue pairs.
Dungeon closes the seven ambient subjects: all 1126 frames, silence, zero-disparity
loop endpoints and 21 right witnesses pass, with minima43.02/44.58 dB and three
independently inspected actual encoded pairs. These remain static desktop checks.

Only empty future-work waiters were rebalanced: the existing ambient and hero
neural producers retain their exact profile and continue untouched. Three
unstarted heroes move to a disjoint directory after the existing parallel hero
producer; three others follow the ambient producer. No third concurrent neural
process or duplicate output writer is introduced. The exact prior/new commands,
aliases, disjoint subjects, source pins and process identities are retained in
the media worker's hero-rebalance.json and concurrency-ledger.json.
The original five-hero producer has completed successfully and is reaped; its
authorized six-thread, nice+15 successor now computes only Berserker, Elementalist
and Nightshroud. The original ambient producer has also completed successfully
and is reaped; its authorized successor now computes only Quartermaster,
Soothsinger and Summoner at the same six-thread profile. Exactly two model processes
run concurrently. At an owned completed-child boundary, a separate ignored scheduler selects
subjects whose complete depth receipts are ready. It runs only one immutable
normal batch child at a time with the complete canonical manifest and explicit
skips; it changes neither a movie's order/timing nor renderer/verifier inputs.
The restarted run-sched002 freezes the reviewed helper checkpoint20c7fa13a and
canonical manifestf10b245d; no renderer overlaps another output writer.
The documentation i18n checker passes all five guide pairs/ten files.

The new logo and library headset pictures, comfort and native spawn paths remain
unverified until the maintainer's requested in-scenario review.

The complete library and final binary are not handed over at this checkpoint.
No Frame write, receiver installation or runtime restart is performed here.

## Storage and reproducibility

After owned test processes stop/reap, the shared completion-cleanup API removes
regenerable caches and retains logs/results/source/manifests/captures. A clean687
baseline was built in a temporary private worktree, copied byte-for-byte with its
actual net472 DLL and1309-file receipt, then the owned worktree was removed. The
main checkout's shared refactor baseline was never overwritten.

The completed frozen692 attempt and composed continuations are archived in
`.planning/debug/stereo-library-frozen692-gate-499bdc157.tar.zst` with 253348 indexed
files/links and 16609187638 summed regular-file bytes. The 4649312700-byte archive
has SHA256 `fa948e2e9042a0c910486329b7e5fe109772e95ceb600a5a97dce51b9d89e3d1`;
every archive entry was compared with its original, and all original source pins
were rechecked. Nineteen selected reports and the exact actual Release DLL are
also independently byte-verified in the adjacent directory. Preserved SDK source
folders and 402 borrowed-input links were moved outside project caches before
completion cleanup; all 67 bank/index targets remain byte-identical in main.
Only then was the clean, stopped validation worktree removed. The frozen branch,
manifest, initial failures and full evidence remain reproducible in the archive.

Completed unpinned687 fixture evidence was compressed into
`stereo-video-intro687/frozen-worker/completed-suite-evidence.tar.zst`, compared
against every original byte before duplicate directories were removed. Pinned
evidence, current deliverables and actively borrowed neural model/depth inputs
remain unpacked. No other agent's active worktree or shared game reference was
swept. The archive SHA256 and compression receipt are in the root evidence tree.
