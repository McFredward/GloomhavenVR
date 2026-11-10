# Build765 shared Results scroll admission

Base: dev128b6b6ca / ModBuild764. Both supplied hardware banners identify the
same764 commit; host uses SteamVR/OpenXR and guest VirtualDesktopXR. The host
has Debug detail, guest Info. No screenshots were supplied for this report.

Risk B: the failure is bounded to the own metadata input canvas attached to an
inert Results picture and its existing whole-window joystick fallback. Existing author/request ordering, codec, native scroll
callbacks and actor/gameplay authority remain unchanged. Complete764 input-bound
coverage is inherited outside the directly affected shared-scroll fixture; root
owns final strict/source/integration checks. Do not rerun the full local gate.

Source finding: ScenarioResultsPresentation.Paint calls AttachResultsInputs while
its candidate picture is still inactive, and activates it afterward. The default
GetComponentInParent<Canvas>() skips inactive ancestors, finds the outer active
host or no canvas, and fails to register the actual nested input canvas. The
unchanged proxy-version fast path then preserves the missing admission forever.
The old fixture activated its picture before attaching metadata input, omitting
the failing native construction order. Reproduce that exact order first.

Concurrency rule remains the existing common-author reliable order: every wheel
or normalized-position request acts on the same original viewport. Opposed wheel
requests are both applied in that order, with native bounds. A new explicit
scrollbar press ends the old gesture and takes ownership; old-owner drag/release
cannot move or end the new gesture. There is no new timer, lease, queue or wire
schema. Focused tests will cover opposed requests, takeover, stale release and
convergence alongside original private-window/native-permission controls.

## Final implementation and evidence

The actual Results construction order is reproduced, including the inactive
candidate and unchanged proxy-version cache after activation. The original
production code fails the exact nested-input-admission assertion. Including
inactive ancestors registers the correct original copied canvas before reveal.

The observer's whole-window header/background joystick route now resolves an
active common-picture metadata proxy. It uses the existing Wheel mailbox and
native author callback, even if that observer has no private native list or an
inactive one. Generic viewport/proxy hover remains the sole owner of its normal
stick route. Live common travel alone claims scroll-versus-locomotion focus;
private row counts and inactive private controls cannot decide it. Source-side
native permission, actor authority and non-Results windows remain unchanged.

The fixture executes the actual coordinator, codec, bridge, original pointer
Scroll/Press/Drag, registered nested TryRaycast selection, generic stick response
and complete Results stick fallback. It uses three independent coordinators and
original Unity UI callbacks, event cameras, filters and masks. The guest's
private rows deliberately differ from the source. Its outer fitted frame is
also shortened so the unchanged common scrollbar is133pixels outside it; actual
registered nested picking resolves that exact scrollbar and its projected drag
still moves only the common native author. This is pointer-dispatch evidence
after surface admission, not a substitute for the existing converted-window
frame/native-depth fitting code. No supplied screenshot exists for this run.

Final validation was against the reserved765 protocol stamp, cherry-picked from
dev839358312 (local duplicate e72a947e5, not an additional worker deliverable).
All5completed runs are archived with SHA256 verification at
`.planning/validation/build765/scroll-lane` in the main checkout:

| Run | Result | Time |
| --- | --- | --- |
| run-s189iyao | Expected original-code causal failure: inactive-first canvas never registered |12.52s |
| run-arsmgp5c | Interim764:105production assertions and14compiling causal controls pass |39.48s |
| run-66tspqyx | Fixture compile failure: explicit focus port omitted original nullable target annotation; corrected port only |1.76s |
| run-69ecsweh | Final765production-only qualification:124assertions pass |12.96s |
| run-rgdvj63h | Final765affected suite:124production assertions and15compiling causal controls pass |42.98s |

Total measured validation time109.70seconds. The compile-failed invocation did
not reach execution/source-stability evaluation and is not a passing negative
control. Final build logs are0warnings/0errors and final source pins are stable.
The focused final suite covers fresh explicit pointer takeover, stale former
owner drag/up rejection, both opposing wheel requests and normalized-position
author order, three-peer convergence, close/reopen, author switch, native denial,
outside/private targets, exact outboard scrollbar, whole-header/background
joystick, absent/inactive private list, zero travel, and single generic delivery.
Each causal control compiles and must reach its exact intended failure marker.

One Unity2021.3.5f1/GLCore editor was active in this lane, private ADB5083 with
ADB_MDNS_AUTO_CONNECT=0. Every invocation reaped its editor/compiler processes
and called the canonical cleanup_completed_run API, retaining source manifests,
executed assemblies, logs, results and cleanup receipts. No full local gate or
wire-golden rerun was performed; unchanged Build764 full checkpoint evidence is
inherited and the integrator owns final strict/source checks for the union.

Hardware limit: these are executed Unity/coordinator/native-widget proofs, not
a claim of Photon delivery timing, a loaded Victory controller, physical headset
pixels or new hardware acceptance. Original initiative depth-picking and desktop
coordinate-transpiler scope, session/window registries and focus bookkeeping are
explicit fixture ports. The supplied764host/guest logs confirm observed behavior
and versions, but contain no dedicated scroll trace proving an alternative cause.
