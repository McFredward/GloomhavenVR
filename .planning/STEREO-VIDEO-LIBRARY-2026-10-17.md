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
repairs are retained through their separate publication lane. Builds690 and692
are internal source candidates only; final stereo handover uses694 or later.

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
18 merged library geometry/inventory checks with11 refusal controls. Prior broad
options/close evidence is reused for unchanged areas; those focused checks are
not a complete gate. The new GPU fixture is registered as local-only, giving
226local/100CI/16source inventory. The full integrated attempt is running against
frozen499bdc157/692 in a private validation worktree. Its16 source suites pass.
The local group exposed an older town-service-options fixture missing explicit
SteamLink availability, TickGuard and Rebuild ports. Tests-only0796670ba repairs
that boundary; the affected suite passes173 assertions and9 causal controls.
The remaining full attempt continues unchanged. Its initial failure is retained;
later checks and focused repairs will be recorded as composed evidence, not a
new complete-gate pass.

At this checkpoint the intro, studio, two cave films and forest are verified;
15 worker subjects remain. Each subject must pass original native-clock/AAC,
all-left-picture, right reprojection, depth receipt/hash and decoded-cue gates
before its output enters Assets. Desktop cue inspection is distinct from actual
headset playback. Independent inspection of all11 original hero title frames
found that generic protected regions missed some printed symbol edges and banner
lines. Source-specific early/middle/late union regions are being corrected before
hero publication, preserving original neural inputs and completed ambient proofs.
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

Completed unpinned687 fixture evidence was compressed into
`stereo-video-intro687/frozen-worker/completed-suite-evidence.tar.zst`, compared
against every original byte before duplicate directories were removed. Pinned
evidence, current deliverables and actively borrowed neural model/depth inputs
remain unpacked. No other agent's active worktree or shared game reference was
swept. The archive SHA256 and compression receipt are in the root evidence tree.
