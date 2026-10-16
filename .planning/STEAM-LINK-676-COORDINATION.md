# Steam Link manual-only source test, 2026-10-15

## Current checkpoint: final-draw bytes and restored Frame, 2026-10-16

The approved fork446027e capture e5138e4b1410ff2f888365cbe66659e5 retains
the exact final-draw native uniforms in addition to both original pixel paths.
All four files and their slice/plane hashes independently match. The actual
current-record consistency mismatch takes fallback0; selected VS/PS matrices
are bit-identical and R1=[0,.75,1,1]. The new bounded lookup helper was checked
against the original fragment instructions on24 asymmetric cases, maximum UV
discrepancy5.96e-8/RGB0. It takes post-reprojection q as input, not application
rays; physical eye, source/video registration and alpha remain unproven.

The user-approved manual rollback is complete. Certain scans find no owned
diagnostic mappings or activation/run library, and two fresh native UI samples
are healthy. The initial reported vrserver crash is independently attributed
to the old process during teardown, before the new service execution. Its
underlying cause remains unknown. The user separately confirms a normal visible,
usable surface and has been told the Frame may enter standby. No more Frame
access is needed for current offline work; any new activation/restart requires
fresh readiness agreement. No timers were introduced.

Fork a0141c2 integrates that evidence, the strict old-server identity/reuse fix
and the offline mapping helper/CMake registration. Composed health72 methods
and mapping12 methods plus independent instruction-level review pass; only the
new CMake registration was rerun at integration. Current source676 remains
unchanged; this is not a fresh complete mod gate or streamed-passthrough result.
See fork docs/NATIVE-UNIFORM-CAPTURE-20261016.md and retained ignored
build/private-uniform-final-draw-run-20261016-r1/.

Next source work reserves the SteamLink source lane only: a fresh app-owned
session, changing stereo-pair IDs and dense deterministic synthetic coverage.
The portable foundation worker owns only new RegistrationPairPolicy.cs,
RegistrationSource.cs and tests/steamlink-registration-source; primary owns
the Unity adapter, version/docs and integration. Pattern generation/upload must
stay outside eye callbacks and GPU resources immutable until actual completion.
This changes no native Steam sender and is not real game-alpha export. Do not
request another headset test before the complete software package is ready.

## Latest checkpoint: native sampled source recovered, 2026-10-16

The approved query-history diagnostic at fork a2893cb completed one paired
1152x4608 acquisition. All three files were transferred separately and verified:
15,925,248-byte P010 copy, 21,233,664-byte native sampled components, and
2,412-byte metadata. Shared image/view/command generations, owned fence and exact
reusable-generation retirement are independently verified. Native admission
observed the enabled YCbCr/statistics features and complete query coverage.

The unchanged strict production decoder now recovers all three complete original
CRC headers for each trusted static eye source from sampled G. Expected session,
content7, encoded eye and source digests match. Static coverage/calibration
interiors preserve fractional codes exactly; full-plane edge errors remain.
The same acquisition's raw-copy representation fails this recovery. Stop CPU
untile speculation and use logical native GPU sampling for the next proof.

These are fitted offline pixel-center coordinates, with multiple passing fits,
not native runtime eye/UV/foveation/reprojection or changing video-frame proof.
All native spatial/temporal and passthrough authority remains false. No real
scene alpha composition or native shader/sender replacement occurred. Mod676
and its DLL remain unchanged; no complete mod test suite is repeated.

The user separately approved rollback, which completed with fresh native UI
health ready, activation and run library absent, no diagnostic mappings and no
errors. The user was told standby is safe. No further Frame connection is needed
for offline work; a future install/restart needs fresh agreement. No automatic
expiry, rollback or restart was introduced.

See the fork's docs/PIXEL-SAMPLER-CAPTURE-20261016.md. Private captures and two
independent reviews are retained under its
build/private-query-admission-run-20261016-r1/; strict recovery, source hashes,
controls and measurements under build/private-paired-source-analysis-20261016/.
Our worker worktrees and regenerable build/test caches are removed; preserve
these small reports, original captures and current deliverables. The maintainer
clarified that cleanup stays within our passthrough area; do not clean another
agent's NPC/trailer/Quest worktrees or inputs.

The subsequent fork checkpoint6f44ef2 adds two inert uniform-evidence primitives
and their focused registration:1,261 metadata controls,193 copy controls and33
actual Intel GPU sequencing checks pass; normal/sanitizer and ARM syntax
evidence is retained separately. Complete hosted fork CI38865974112 is green.
They are not linked into the installed pixel diagnostic. Live final-draw,
buffer-sharing/alias/queue ownership and actual native parameter association
remain work before another device package; see the fork's
docs/NATIVE-UNIFORM-PROOF-PLAN-20261016.md. Mod676 remains byte-unchanged.
Both new worker worktrees and integration CMake caches are removed after push;
the extra audit also removes13MB of old owned test executables, retaining all
sources, logs, captures, native references and current deployment packages.

The next optional diagnostic is integrated and pushed in fork446027e. Complete
hosted fork CI38869685818 is green, including the explicitly enabled companion
build. The tracker selects the exact final supported draw, pins both original
stages and rechecks complete descriptors plus conservative allocation aliases.
After the unchanged native render-scope end, at most20,480 UBO bytes are copied
to unused owned staging tail and published only after the original fence and
exact reusable-generation retirement. No shader or native PC sender is replaced.

New resource118, final-draw541 and formatter81 controls pass normal/sanitized;
four actual formatter-to-reader bridge cases, strict ARM checks,14 default and
two enabled real-loader cases pass. Earlier unchanged foundation/legacy evidence
is inherited, not represented as another complete mod gate. Independent source,
ARM ELF, package/activation and explicit manual-restoration reviews have no
blockers. ARM299,792 bytes/SHAe21a5d5113305709843bdde3d02990a1058fecad090521c02e8b1753f047e75a
has only three private loader exports, no TLS/Vulkan dependency and NOW/RELRO.

All three completed worker worktrees and31MB of this integration's regenerable
build caches are removed; manifests, logs, original captures, source and current
deliverables remain under the fork's ignored build/private-uniform-*-20261016/.
Cleanup stays strictly in this passthrough area. Frame state remains the previously
verified restored original state: no new connection, install, capture or restart.
The ready package requires fresh Frame/readiness agreement before activation.
The wrapper refuses missing native UI/sampler/uniform readiness, failed tracking
or uncertain/foreign mappings before requesting a stream. No automatic expiry,
rollback or restart is installed. Source676/DLL remains unchanged; actual native
UBO capture, physical-eye/UV/video association and visible passthrough are unproven.

## Earlier integration history

The maintainer explicitly rejects the30-second automatic source-test timeout.
This primary reserves ModBuild676, STATE's new676 entry, Core/SteamLink, their
focused runtime controls, the SteamLink EN/DE help/config strings and
STEAM-LINK-MASK.md. The existing manual menu escape and switch are preserved;
no new Frame mutation/capture permission is requested or implied. No automatic
time-limit setting will replace the removed limit.

Worker uses a separate initialized worktree from current dev01ab8fdb9. Its scope
is policy/lifecycle/callback clock removal plus production controls. Primary owns
strings/docs/version/integration, focused affected checks, commit and dev push.
Existing unrelated trailer and CI-preflight test repairs remain untouched. The675
CI owner may commit its test-only followup independently; please preserve this
integration and do not overwrite its version or source. No repeat of unchanged
221-suite/native/golden gates is planned for this narrow request.

The675 CI-preflight repair01ab8fdb9 is included in the worker base. No changes
to tests/test_parallel_test_suites.py or675 fixtures are made by this primary.
The new source push will naturally supersede the older dev CI run; please keep
any additional CI-only fixes disjoint and do not reserve676 for another build.

Integration is ready: worker94cc36ba3 was cherry-picked as bc889f46b. Its1,225
production-policy controls and Unity-reference compile pass. Primary actual
Debug/Release builds pass with zero warnings/errors, player-help lookup passes
2,308 assertions, options coverage and docs i18n pass, and config/Harmony/log
surfaces remain667/236/4,797. Independent source review has no blockers. Test
caches are cleaned; retained evidence lives under `.planning/debug/steamlink676`.
No full-suite replay or hardware/device proof is claimed. This primary owns the
final676 commit and dev push; unrelated trailer files remain outside that commit.

## Confirmed source result and next preparation

The maintainer subsequently confirmed visible output in both eyes and immediate
normal game restoration on menu opening. The676 logs show both eye callbacks
without fallback. This is visible static source carriage, not numeric decoded
sample recovery or working streamed passthrough. Ignored hardware analysis and
independent reviews remain under `.planning/debug/steamlink676/`.

The next local preparation is integrated into the research fork's `dev` as
`2f5316e` (`/home/claw/steam-frame-chroma-key`). It adds a reversible public Vulkan
diagnostic for one immutable decoded P010 snapshot and offline source-header
recovery. Exact command/pool generations protect reusable native recordings;
actual local GPU, real-loader, causal discard/reset, sandbox staging/rollback and
independent source/package reviews pass. Detailed scope and inherited evidence
are in the fork's `docs/PIXEL-SNAPSHOT-PREPARATION-20261015.md` and ignored
`build/private-pixel-prep-20261015/`. This is not a complete mod-suite rerun or
positive native/headset readback proof.

No new GloomhavenVR DLL/build number is needed for this research preparation:
build676 remains the PC source input. No Frame SSH, staging, pixel capture or
restart has occurred in this round. Fresh explicit approval is required for
the pixel-capture scope; actual native provider resolution and UI health must
be checked before activation/arming. No automatic rollback/restart/test expiry
is introduced, and no native Steam sender hook is added. Preserve unrelated
trailer work and other agents' active test inputs during cleanup.

## First approved native image attempt and restoration

The maintainer subsequently approved the pixel package and manual restart. The
actual Frame runtime imports/isolated load passed and the public layer loaded
with a healthy native UI. A cold-stage process-map permission refusal was fixed
through explicit, bounded read-only protected-map checks (fork529e2ff;31 focused
tests and full fork CI pass). No native shader/sender/PC DLL change was involved.

After initial `prepared`, the writer reported `tracking_refused` before arming.
The maintainer confirmed the streamed pattern was visible, but the orchestrator
refused to create the arm token. No image copy, raw snapshot or snapshot metadata
was produced. The old record cannot identify its exact first failure; the briefly
open Steam menu is not a proven cause. The maintainer separately approved rollback;
native UI health returned ready, all diagnostic mappings and activation files
were absent, and owned run-library deletion completed with no errors. The Frame
may be placed in standby. See private fork evidence
`build/private-pixel-run-20261015/` and its documented attempt.

The fork now prepares exact first-refusal telemetry in9f3c7d8. First-winner enum,
static API site and bounded atomic Core counters are published once; admission
and refusal behavior are unchanged.1103 tracker controls normal/sanitized, six
actual formatter tests,14 final real-loader cases and independent review pass.
The revised ARM artifact remains local, not installed. No new PC build is needed:
source676 stays unchanged. A new Frame readiness/restart agreement is needed
before collecting the missing native cause; no successful pixel/passthrough
claim or speculative guard relaxation is made.

## Concrete native lock refusal and causal local repair

The approved revised Frame attempt now records the missing cause: failure1
`contention` at `traceBindImageMemory`, with all Core loss/reason/capacity counters
zero. The outer `LiveTracker` used `try_to_lock` and permanently refused a session
on an ordinary failed mutex acquisition. The streamed676 pattern was visible;
no arm token, GPU image copy, raw snapshot or snapshot metadata was produced.
The separately approved rollback completed: activation files absent, original
SteamVR native health ready, no owned/foreign mappings, run library removed,
no errors. The maintainer was explicitly told standby was safe. Small inactive
package/evidence files remain; no automatic restart/expiry was introduced.

Private exact evidence is in the fork's `build/private-refusal-run-20261015/`
(capture221bea80a49fd76a80e9b4223ec4e86b). A targeted local repair serializes finite
own CPU metadata instead of losing/refusing legitimate parallel observations;
same-thread recursive entry and real unsupported/lost/stale/capacity/generation
conditions still fail closed. Native driver calls, GPU work and file I/O remain
outside that lock, and no new TLS storage or deferred journal is introduced.
The fork documents the actual attempt in
`docs/PIXEL-TRACKER-CONCURRENCY-20261015.md`.

The new actual production Vulkan-loader concurrency fixture reproduces the OLD
policy's failure locally (`contention` at `traceAllocateMemory`, Core counters0)
with three threads and distinct native images/memory/views. This is a causal
negative control, not another speculative Frame retry. The corrected tracker,
its actual layer integration and final ARM package are being validated before
any fresh device readiness/restart request. The main DLL/source676 and its
unchanged full-suite/wire evidence are not rebuilt or repeated for this work.

The corrected fork integration4f84db9 now passes5,225 tracker controls normally
and under ASan/UBSan, all14 actual production-loader cases (each exercising72
parallel native resource lifecycles), six formatter tests and independent
source review. TSan is explicitly unavailable on this host (unexpected memory
mapping before test execution), not passed. The strict ARM library is229,232
bytes, SHAdef9445f8ec5690150026a9b35e782e424efe0b61bc13dc90dd067f5efdef5e4;
three private exports/no TLS/no Vulkan dependency/BIND_NOW/RELRO remain verified.
See `build/private-concurrency-prep-20261015/` in the fork for retained evidence
and the current package. No successful Frame image or passthrough is claimed.

## First complete native image capture and verified restoration

The maintainer subsequently approved the corrected package, confirmed the676
pattern in both eyes, and separately approved manual rollback. This attempt
passes actual provider/load/startup checks and progresses through prepared,
armed, commands_recorded and complete. One immutable1152x4608 P010 artifact
(15,925,248bytes) is secured locally, raw SHA
1eb48d4cff6480c5983193558a9b3711ec061d05ddab554e3f413fc2fba3a850.
Metadata1251bytes SHA410ec9f619478902c859ff4e689010a5d93ed3e1988b6cd12c127aedcfd36670
records owned-fence completion and exact reusable-command generation31088
retirement. Both raw/plane and metadata digests are independently verified.
The initial multi-source SCP exit1 was not accepted; the missing small metadata
file was copied exactly via bounded read-only SSH, and both final files match.

Rollback is complete: new native UI health invocation ready, activation files
and owned run library absent, no owned/foreign diagnostic mappings, no errors.
The user was told standby is safe. Small inactive packages/evidence and the
captured image remain; no automatic timers/restarts were introduced. Exact
private evidence is fork `build/private-concurrency-run-20261015/`, capture
f6267827c7462ea93972a012278288e5, with independent evidence review.

The fork's `docs/PIXEL-SNAPSHOT-CAPTURE-20261015.md` separates actual GPU image
acquisition from still-unproved payload/source-frame/eye/UV/foveation recovery.
Stored-Y diagnostics have discontinuous structured regions; do not infer a
simple image projection, guessed RGB transform or passthrough-ready association.
Offline interpretation is now underway without further Frame access. The main
mod DLL/source676 is unchanged and no complete local test gate is repeated.

## Local same-image sampler comparison preparation, 2026-10-16

The first saved native components contain block discontinuities and recover no
CRC source header under the bounded tested mappings; original left/right source
PNGs pass all three header positive controls. Historical compressed Qualcomm
memory metadata is not proof of the current image layout or a driver defect.
Never substitute a physical DRM pitch for a logical Vulkan buffer-copy stride.

The research fork integrates a paired comparison at4e74494: tight P010 plus
matching native-view RGB_IDENTITY sampled components in disjoint regions of the
same64MiB staging allocation. Both share the original command, fence and exact
retirement proof. All GPU objects are prepared cold; native graphics/push state,
shader and Steam PC sender remain unchanged. Exact device enablement, public
format/modifier support, converted view, compute queue and clean command history
are mandatory. Unobserved conditional/inherited/heap/tile-dispatch routes and
enabled pipeline statistics are refused. Compute reads are ordered before later
native GPU work as well as host publication. One bounded background refusal
record explains rejected sampler admission without render-thread logging.

Focused controls, real software/hardware Vulkan fixture evidence and independent
source review are retained in the fork's private sampler evidence. The local
GPUs do not support the exact native MIDPOINT/LINEAR P010 profile: the separate
supported-profile software fixture must not be described as that proof. Final
strict ARM build is291248bytes, SHA
61c8016596da18047d5d85a8ae8eeb75c44b6c9bdfd8a6124ecb11d37a8cc8b3;
three private exports, no TLS/direct Vulkan dependency and BIND_NOW/RELRO pass.
All14 final original-loader smoke cases pass; they do not issue PS_DISTORT.

This remains local, not a new Frame installation. Preparation and future paired
transfer/rollback rules are in the fork's
docs/PIXEL-SAMPLER-PREPARATION-20261016.md. Fresh device approval/readiness and
actual current-provider/native-health gates remain required before activation.
There is no automatic expiry/rollback/restart. Source-frame/eye/UV/foveation and
working streamed passthrough are still open. Source676, mod defaults, gameplay,
wire and unrelated concurrent trailer edits remain untouched.

## Sampler cold refusal, complete restoration and local query repair

The approved291248-byte sampler package reached healthy native UI startup but
reported device_admission_complete=false and sampler_ready=false before a
stream was requested or the run armed. No image/metadata output was created.
The old sampler result-8 is its unattempted sentinel, not a returned driver
failure; default YCbCr/compute flags do not identify which input was refused.
The precise native input cause remains unknown. Native evidence is pinned in
fork build/private-sampler-run-20261016-r1; the fork documents the attempt in
docs/PIXEL-SAMPLER-ATTEMPT-20261016.md.

The maintainer separately approved rollback. All activation files and run
library were removed; fresh original native UI health returned ready, complete
protected-map scan found no owned/foreign mappings or uncertainty, and rollback
had no errors. The user was immediately told standby is safe. No automatic
expiry/restart, PC sender mutation, source676 change or capture occurred.

The fork's repaired deva2893cb now reports all21 incomplete-admission branches
with bounded reason/input context, actual feature-observed distinctions and
whether sampler preparation was attempted. Per-recording core/indexed query-
begin history replaces a blanket enabled-statistics-feature refusal, preserving
the original native query calls/results. Unknown future routes are excluded;
at most two background route observations are possible per device.

Focused helper180 normal/sanitized, six route tests, strict host/ARM builds and
independent review pass. Two new actual software-Vulkan query-loader cases
exercise both hooks and preserve native zero/available results. Oldb16 fails
the same minimal feature-only input specifically at incomplete admission;
this proves the old policy overly broad locally, not the old Frame's cause.
Unchanged tracker/shader/GPU/14-loader evidence is inherited locally. Full fork
CI38861075778 passes the exact final source, without a repeated main-mod gate.

New local ARM291776bytes SHA
01d371a143bbf77b60a1d89cec1693e657c12cbcad728916cd3c3abb539445ed
retains three private exports, no TLS/direct Vulkan dependency and BIND_NOW/
RELRO. Its package/source/CI hashes match. Owned worker trees and build caches
are removed; native evidence/results/current deliverables remain. This package
has not been deployed: fresh Frame readiness/restart approval and actual native
provider/health checks remain required. Working passthrough and native source/
frame/eye/UV association are still unproved. Main source676 remains unchanged.
