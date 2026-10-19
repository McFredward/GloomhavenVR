# Concurrent Steam Link mask-test integration, 2026-10-15

The passthrough agent is integrating an opt-in, bounded source-carriage diagnostic
inside the existing GloomhavenVR plugin, following the maintainer's explicit
correction. This is not working Steam Link passthrough or a second plugin.

UI checkpoint78c575151 is already on local dev; the disjoint runtime follows
shortly. Owned paths are Core/SteamLink/, its CoreModule lifecycle calls, embedded
Assets/SteamLink/ resources, their csproj resource declarations, targeted Options
opening hooks/localization, settings/runtime tests and their runner registrations.
All are default-off; no network, NPC, water, haptic or healthbar changes.

The other integration's in-progress ModBuild675 block and FIX-675-REVIEW.md have
been noticed and will NOT be overwritten or committed by this agent. Trailer
changes likewise remain untouched. Please preserve the integrated Steam Link
commits when performing dev merges. The new targeted tests and final strict build
will be run after both UI/runtime commits are present. Build-note/version handling
will be reconciled with current dev before pushing; no Frame modification or
hardware handoff is authorized by this source integration.

Own evidence: .planning/debug/steamlink-mask675/. Current user-facing runtime
capability remains unchanged until source/native carriage and mapping are proved.

## Acknowledgement, 2026-10-15

FIX-675-INTEGRATION-COORDINATION.md has been read. Build675 and STATE remain
owned by the hover/tile/healthbar integrator. This agent will NOT push the main
repository independently. Supply the combined final source/build/wire/surface
receipts once ready; these will be reused rather than running duplicate gates.

Steam Link central registrations are now complete: steamlink-mask-settings
(real BepInEx persistence +2 causal controls) and steamlink-mask-runtime (portable
production policy/lifecycle/ownership controls). Both are hosted/local; actual
Unity-reference subset is explicitly opt-in and is locally proved. The manifest
will be committed after the runtime checkpoint arrives, then released for the
other integrator's disjoint native suites.

Proposed additional Build675 note: integrate an experimental Steam Link
source-carriage test into GloomhavenVR itself, under Advanced / Measurement and
diagnostics. No separate BepInEx plugin or sender hook. Default-off; boot clears
saved requests. Start only after VR options close; <=30 seconds. Ordinary menu
tap or VR-options reopen restores ordinary output synchronously. Stop/camera/
VR/fault guards close admission before destroying owned components/textures;
retired callbacks/drivers cannot cancel a fresh request. Four original tiny
fixture resources are embedded in GloomhavenVR.dll; no user-side source-path,
CLI, keyboard trigger or additional install. Standalone Frame rejects this PC
source diagnostic. Existing MR/native/chroma, gameplay, networking and shared
presentation are unchanged. This is NOT enabled native streamed passthrough:
actual XR/codec carriage, selected-frame, eye/UV/foveation and fractional alpha
remain unproved; no Frame installation/write/restart is done in this step.

## Latest source review and focused receipts

UI escape follow-up4bdb96d6a is integrated. The ordinary menu tap now restores
output BEFORE ResolveMenu, including the main menu where ESCMenu is absent,
without consuming or changing input. Options1801 assertions and two new causal
controls pass; prior seven close controls are retained. Actual BepInEx binder
113 assertions/two controls and localized player help2308 pass, including CI
metadata-reference compatibility. Runtime has607 portable checks plus successful
actual-reference targeted compile; final checkpoint/documentation is imminent.
Independent review found/fixed two deferred-owner hazards: old render callbacks
and old driver destruction must never clear a fresh current request. The reviewed
camera path remains native-passive except explicit, temporary app output.

No duplicate full gate is being run by this agent. Please wait for the imminent
runtime cherry-pick before compiling the combined tree: current UI hooks refer
to that runtime. Final review and source hashes will follow in this document.

## READY for combined final integration gate

Runtime585e12aaa and normal-menu escape4bdb96d6a are now integrated on dev.
Central suite registration is committed as b749b9bd8. The manifest is RELEASED
for the other integrator's two disjoint native suites. Its only later documentation
change clarifies that the existing module config automatically stores this toggle
(no separate plugin/manual config), correcting an imprecise sentence. Source code
hashes in core-source.json still match. No source tests were repeated needlessly.

All Steam Link source/UI work is now reviewable; no unresolved implementation
blocker is known. Independent final source-bound approval follows under
.planning/debug/steamlink-mask675/review/. Please include this exact source in
your final actual Debug/Release/source/surface/wire validation and combined push.
New main-mod declaration: one additional config key [SteamLink]PayloadTestEnabled,
no Harmony patch or wire record. ModBuild675 may include the proposed note above;
there is no separate676 reservation. We preserve your STATE/NetProtocol ownership.

## FINAL handoff: source approved, no outstanding Steam Link implementation work

Independent source review PASS at b749b9bd8, with38 exact committed-blob hashes
and11 retained evidence hashes:
.planning/debug/steamlink-mask675/review/FINAL-APPROVAL.json and SOURCE-REVIEW.md.
No remaining source blocker for this bounded diagnostic. This is not an XR or
passthrough hardware approval. The reviewer confirms all three repaired escape /
retired-owner hazards and the absence of Frame/native-sender changes.

All owned worker worktrees are removed; their Git checkpoints and source/log/
result evidence remain. Shared game references, the current deliverables, trailer
work and other agents' active runs were untouched. Cleanup receipt:
.planning/debug/steamlink-mask675/integration/cleanup.json.

The common integrator owns the remaining actual Debug/Release build, final
source/wire/surface checks, combined dev push and CI. Do not call the diagnostic
working passthrough. No new hardware test/readiness request has been made to the
maintainer and no new Frame write/restart is authorized. A future test requires
a fresh coordinated approval after the shared source is validated and pushed.

## Maintainer requests dev push and test instructions now (2026-10-15)

The maintainer explicitly asks this passthrough primary to coordinate with the
other running agents, push dev and give the exact test steps. The Steam Link
code/review/targeted checks are ready and unchanged. Please record current common
build/check progress and whether you are still performing the agreed combined
push. We will preserve your675 integration and avoid duplicate final gates or a
push while your source is half-integrated. If needed, hand over the finished
checks/push explicitly here. The next user test is PC GloomhavenVR streamed over
normal Steam Link, settings-controlled source pattern only; no new Frame helper
installation or restart is required/authorized for that first source-output test.

Current common Release build is confirmed0 errors/0warnings (release-final-build.log),
so the real integrated mod/config/menu/runtime types compile together. Read-only
inspection of runtime-final shows town-enhancement-handoff fails only at newly
introduced haptic fixture boundaries: HoverHapticTargets missing and VRHand stub
lacks SendHoverHaptic. No Steam Link source mutation is indicated. This agent is
not editing those675-owned fixtures; please retain their control semantics while
repairing the exact external dependency binding. Reuse the unchanged607 mask and
113 binder checks. We will verify the remote dev SHA after the coordinated push
and give PC-stream source-test instructions only once the common local gates pass.
