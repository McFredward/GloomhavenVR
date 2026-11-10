# Build768 independent Results picture review

Reviewed candidate: `c4683ad26b758b3540dcc889ae36af2297768f21`, Build768.
This is an independent source/evidence review of the complete picture consumer,
not a new headset, network or complete-suite pass. The worker owns this report
only. No production or fixture source, native reference, asset or expectation was
changed; no runtime/Unity test was rerun. The documentation change is class A.

## Evidence boundary and immediate conclusion

The newly supplied main and remote `LogOutput.log` both identify **Build767,
`5dff1344c`, built 2026-11-09 17:35:31 UTC**. These are different files from the
earlier Build768 implementation intake: current hashes start `1f599c82` and
`7846ff39`, respectively. The current remote log contains seven occurrences of
the exact `noise_01` ambiguity, at lines 49,51,54,56,57,59,604. Its two Results
scroll descriptors resolve at line 478. This supports the already identified
Build767 presentation refusal; it cannot establish a new Build768 defect.

Both supplied photographs were inspected. They show visible Victory/statistics,
achievement rows, Return and furniture, rather than the newly reported empty
body. Their hashes are `b3d00ef274b1265063de6d8872a8f612254df2b440a38a5ae601ecff254c7857`
and `a54d8231ca7eed50fca0ca602de0d31ae0c282970d34a1ae486104a5abede32d`.
The previous integration report identifies these as older Build766 photographs.
No photograph has an independently readable Build768 banner. The reported empty
window remains a user observation requiring matching Build768 evidence; it must
not be explained by substituting the Build767 warning or these older pictures.

A visible shared-window badge does not certify a received Results body.
`GrabbableModal.SyncSharedState` derives it from window classification and local
participation (`GrabbableModal.cs:1967`). The badge deliberately lives outside
the host subtree/private capture (`:1850`,`:2153`). It can therefore remain
visible when original-picture validation or body composition has failed.

The review found one deterministic asset-retirement blocker, one material-cache
contract gap and a previously documented exception-path boundary below. None is
currently proven to have occurred in the supplied
device run, and none was introduced by the Build768 catalogue patch. The
Build768 descriptor/provenance repair itself has no newly demonstrated cold
picture regression in this review.

## Finding 1: retired descriptor canonical cannot be replaced

Source: `Net/TownServices/TownServiceAssets.cs:51`,`:70`,`:104`,`:115`;
`Net/ScenarioResultsPresentation.cs:105`,`:617`,`:633`.

`Register` populates `_keys[id]`, but only adds `_assets[key]` when the dictionary
does not contain the key. An existing Unity object that has been destroyed still
occupies that dictionary key. `Resolve` correctly treats its value as fake-null,
then asks `Scan` to recover it; neither the uncached nor cached `Key` path repairs
the dead canonical. This is a deterministic source counterexample:

```csharp
var assets = new TownServiceAssets(ScenarioResultsOriginalTextures.Contains);
// A and B are equivalent wrappers of one admitted native texture; both are live.
string key = assets.Key(A);
Object.DestroyImmediate(A);
assets.Key(B);                    // Returns key, but _assets[key] still contains A.
assets.Resolve<Texture2D>(key);   // Scan cannot replace A; throws "not loaded".
```

The failure also occurs if B was discovered before A retired: its cached key
returns before `Register`. The two-second scan throttle only delays a scan; after
any number of scans the canonical remains dead. The catalogue's admission policy
does not change this lifecycle rule.

The Results registry survives `RetireOpening` and `RetirePicture`; only `Reset`
clears it. A new native opening with a newly loaded equivalent wrapper can thus
be refused despite valid metadata and a live matching native texture. On a cold
replacement picture, the independently generated furniture/badge can still be
present. If an older picture remains, it freezes under the existing retention
rule. The exact-root sprite is a special case: its `RegisterOriginal` path can
replace a dead canonical. Other descriptor textures have no corresponding repair.

The minimal counterexample is source-proven. Actual native texture retirement
while the same Results registry survives is an additional reachability condition,
not an observed fact in these logs. No speculative runtime repair is included in
this read-only lane. A focused canonical-retirement test should be coordinated
with the registry owner before any change to shared registry behavior.

## Known boundary: same-shape updates have no post-validation rollback

Source: `Net/ScenarioResultsPresentation.cs:346`,`:359`,`:363`,`:459`,`:139`;
`scripts/results-presentation-runtime/Program.cs:66`.

Structural replacements are prepared inactive: Build validates/applies every
candidate module before retiring the old picture. Same-shape updates instead
validate the live bindings, publish `_displayed = state`, then apply modules in
sequence to the currently visible picture. If Apply fails after validation on
the second module, the first module already contains the new output while the
remaining modules contain the old output. The catch schedules a retry but does
not restore the prior values or hide the partial picture.

The existing scheduled Apply-failure test changes an achievement Address to
force Build. It therefore proves rollback for structural replacement only. It
does not falsify the same-shape case, despite the broader "atomic public picture"
description and catch comment. The existing failure-injection port could check
this without relaxing a pixel expectation: preserve the structure, change an
early module, fail a later Apply, and compare the retained complete image.

This is already explicitly qualified in [MP763-REVIEW.md](MP763-REVIEW.md) and
[MP763-RESULTS.md](MP763-RESULTS.md): only structural replacement preparation is
transactional. It is not a new Build768 patch regression or an omitted disclosed
Build763 guarantee. The broader class/catch descriptions must be read with that
recorded boundary. A required widget/material failure after
Validate remains the additional live condition; the current device evidence does
not show it. It must not be relabeled the cause of the new empty-body report.

## Finding 2: material sample cache ignores registry identity

Source: `Net/TownServices/TownServiceMaterial.cs:22`,`:31`,`:51`,`:67`;
`WorldUI/TownServices/TownServiceTemplateAssets.cs:125`.

Material values contain keys supplied by a particular `TownServiceAssets`.
Nevertheless `_samples` is global and keyed only by Material; a repeated read in
the same Unity frame returns the previously published value without consulting
the registry. A deterministic counterexample is:

```csharp
a.RegisterOriginal("native-a", texture);
b.RegisterOriginal("native-b", texture);
var first = TownServiceMaterial.Read(materialUsingTexture, a);
var second = TownServiceMaterial.Read(materialUsingTexture, b); // Same frame.
// second still contains native-a, which b cannot resolve.
```

Town template provenance genuinely assigns registry-specific keys to material
textures. WorldUI's final publisher samples town before Results. However town
and Results normally occupy different scene/flow states; this review did not
prove concurrent production sampling of the same aliased material. Treat this
as a proven cache-contract issue with unproven relevance to the current report.
It does not justify an uncoordinated general material-cache refactor.

## Native guest waiting, reveal and opening retirement

The local-native visibility gates deserve separate review because a valid host
picture does not bypass them. Tick requires an active native Results window and
`IsOpen || IsVisible` before Accept, and again derives `_picture.active` from
these local terms plus `!RenderHidden && !OwnerRenderHidden`. The visual lease
also releases when the native window is no longer open/visible. A genuine local
close after a key was shown causes `ScenarioResultsPose.ObserveNativeBoundary`
to watermark that key as retired; later same-key host packets then refuse.
These are source facts, not evidence that the reported run took such a branch.

The original game flow does **not** close Results merely because a guest is
waiting for the host. `UIResultsManager.Show` schedules its own `myWindow.Show`
on clients too (`decompiled/GH.Runtime/UIResultsManager.cs:77`,`:159`).
`UINewAdventureResultsManager.SetResult` enters `ShowReadyUpToggles` for victory.
The latter shows the guest wait HelpBox, hides only native Return/Retry options,
initializes the original Ready and automatically readies the client
(`UINewAdventureResultsManager.cs:330`). Its waiting path contains no Results
Hide or Results GameObject deactivation. Additional-retry loss similarly hides
only the options while presenting the wait HelpBox (`:277`).

`EndScenarioResultsState.Exit` only toggles the stats input blocker and releases
the camera-input request. `CloseResultsPanel` unregisters options and returns the
navigation state; it does not itself hide the Results window. Explicit base
ReturnToMap calls Hide after its callback; completion runs the original EndGame
and unload flow. The explicit restart-round command also closes Results as part
of that genuine transition. None is an incidental private wait transition.

The native `UIWindow.IsOpen` is `VisualState.Shown`, while `IsVisible` reads its
CanvasGroup alpha (`decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:305`,`:317`).
Our Results visual lease changes CanvasRenderer alpha and renderer draw output,
not CanvasGroup alpha or the native visual state. It therefore cannot make its
own open/visible gate false. A native zero-alpha opening while still IsOpen also
passes the gate. `ObserveNativeBoundary` does not retire a future key merely
because its local native template has never appeared; retirement requires a
previously shown key or explicit succeeding native-show history.

No source-proven normal guest-wait mismatch was found. A separate native Hide,
ancestor deactivation, conversion reveal failure, or missed native Show can
still suppress/retire an accepted host opening and deserves direct evidence if
Build768 logs show it. The fixture's permanently open UIWindow port and fake
panel reveal fields cannot adjudicate that combined real-coordinator case.
Changing these gates speculatively could display a stale/future episode or
interfere with native retirement and is not justified by the current Build767
ambiguity warning.

## Complete path reviewed

| Boundary | Source behavior and review result | Remaining proof boundary |
| --- | --- | --- |
| Native production | `ScenarioResultsNative` collects original Window/Header/Results/Stats/Buttons/Ready and dynamic native row modules. It reads phase output; it never drives the received phase into native controllers. `EnsureSources` partitions the original graph and rebuilds on detected topology changes. | The fixture constructs native bootstrap/rows and their layout; it does not execute the complete original results factory or animations. |
| Final sample | `WorldUIModule.LateTail` runs `PublishTownServicesFinal` after panel ordering; Results reads widget outputs and clones reused node arrays. Publisher takeover resets the old observer lease before sampling. | This is the last WorldUI writer, not every possible later uGUI Canvas callback. The image suite replaces sample time/frame and calls Capture directly. |
| Asset/source graph | Results alone supplies the readonly catalogue predicate. `RegisterRootImageSprite` runs before source discovery and before same-shape Accept. It unwraps the mip-baked sprite and binds the audited root slot rather than either ambiguous backdrop texture. | Descriptor admission is trusted-original identity, not runtime GPU pixel hashing or a loader. Canonical retirement remains Finding1. |
| Send/delta | The sender emits cumulative deltas against an immutable full basis; full structural/participant changes create a new basis. The queue retains the full dependency before its latest delta. | No production queue/Photon scheduling executes inside this image fixture. Dedicated transport proof must remain separately input-bound. |
| Receiver ordering | Sender/author checks precede coalescing. A delta expands against its exact retained basis before QueueResultsReady keeps the newest complete picture. Pose acceptance precedes picture Apply. | Direct `DeliverResults` does codec -> Apply and bypasses `QueueResults`, actor transport and pose acceptance. A codec pass alone is not receiver integration. |
| Original lookup/clone | Native row lookups fall back to their exact native prefab. Candidate modules keep original names, prune other module roots and mod-owned children, and neutralize gameplay/input/layout/animation controllers while retaining allowed engine graphics and masks. | Imported reference graphs contain no serialized Canvas; conversion-created/nested Canvas histories are not represented by Make. Unsupported runtime custom graphics still refuse strictly. |
| Apply/retention | Build prepares inactive candidates before retiring valid output; same-shape Validate refuses missing dependencies without applying them. Actual accepted nodes supply active flags, transforms, material values, text, group alpha and mask state. | Post-validation same-shape exceptions retain the known Build763 boundary above. The source does not prove every future native generated mesh/effect is represented. |
| Native suppression | `ScenarioResultsVisualLease` suppresses only original CanvasRenderer/Renderer output, including the exact client wait HelpBox, while preserving controllers/callbacks and restoring its receipts. It reasserts in Update and final WorldUI publishing. | The fixture tests draw-only restoration but not every late Canvas/native write in a real player. |
| Capture/layers | The observer candidate mounts under HostRect, registers ExtraRenderRoots and copies the target layer. `PanelSupersample.ApplyCaptureLayer` walks the complete HostGo subtree; therefore an accepted observer is inside that capture walk. The head excludes the private layer and draws its resolved photograph. | Actual pool ownership, capture camera, display quad, reveal gate, MenuDepth branch and head composition do not execute in the image fixture. A raw camera picture of the subject is not this chain. |
| Shared geometry/order | Received FrameSize controls HostRect and fitting; the root pose and detached Ready sibling ordering are applied explicitly. The common Results handle uses the frame. Shared pose/reference goes through the existing world/claim register. | Make simulates private scales/rows and directly compares rectangles. It does not execute native reveal/spawn authority or the actual pose applier and final head sampling together. |
| Scroll input | Common-picture proxies resolve exact author paths and viewports; generated transparent metadata surfaces preserve masks and use the admitted nested Canvas lease. Private client rows do not define the common range. | The image fixture stubs `SharedWindowScrollSync.AttachResultsInputs`; delivered host/guest scroll calls the original ScrollRect directly. Actual laser -> proxy -> transport -> author is separate coverage. |
| Continuations | Observer surfaces carry operation/opening metadata. The host request path checks current native opening/host/version/roster and invokes the original permitted pointer callback. Playback does not replay a vote/ACK/game callback. | Fake submit callbacks and original method excerpts cannot establish real network/native waiter progression by themselves. Existing dedicated continuation proof must remain separate. |
| Retirement | Native closure retires only its matching public opening; accepted close hides private native/wait output through a draw lease. Future-opening retention and author changes have explicit paths. Copies/material receipts and fit state retire, while registry lifetime persists until Reset. | Native close/reconvert/scene retirement with complete real coordinator and retained assets is not a combined fixture case; Finding1 exploits this boundary. |

No source branch was found in this review that makes the new catalogue predicate
itself clear an accepted image, disable its body, alter native permission, rewrite
wire geometry, or conceal content. No mask/layer/reveal fix should be inferred
from an old ambiguity warning alone.

## Why previous green counts missed failures

The four-prefab model exported eight materials/fourteen texture dependencies.
Native UIFX can assign runtime materials from EffectDataBase, so passing every
assertion about those four roots could not establish the runtime dependency
closure. The repeated 3DNoise/flowmap/noise_01 discoveries follow directly from
that omitted producer. Increasing assertions over the same selected bank would
not have repaired it. Build768 now audits the complete static native corpus and
explicitly executes the reported runtime noise assignment; that is a meaningful
improvement with a narrower claim than real multiplayer acceptance.

The 2407 assertions include substantial independent format/payload/property and
policy census checks. They are not 2407 independent native flow or headset image
cases. The 22 compiling controls falsify selected defect paths; the final record
honestly describes a 23-case containing-suite union, not one complete invocation.
The source-generated catalogue expectation is frozen before mutations, so a
broad-admission mutation cannot rewrite its own expected classification. The
independent native audit supplies the separate corpus oracle.

The two live subjects still share process-wide Resources, NativeMaterials,
textures/fonts/material dictionaries, shader objects, material sample caches and
shared material reuse tables. `NativeMaterials.AssertContract` eagerly loads the
render bank before either receiver. Select swaps global native singletons and
the list of panels. Camera-layer isolation fixed artificial Selectable disable/
enable cycles in the new noise test; it did not create two isolated clients.
It cannot detect a texture available only in the author's process, a material
key published under another registry, or a receiver asset canonical that retires
independently. Most older/footer cases still toggle subjects or create fresh
observers, so they must not inherit the new test's always-active claim.

Make also forces every imported CanvasGroup to alpha1 and enables all major
panes before each case applies selected phase changes. The fixture intentionally
ports native bootstrap, animation clocks and layout. The XML import reconstructs
rendering components, not every native behavior, and the GL shader fragment
programs remain translations. ConvertedPanel, CanvasConversion, ModalFallback,
seated-subtree transfer and general scroll attachment are explicit ports. Their
fields allow the production picture class to run; they do not establish the
capture/reveal/order state that the real coordinator will provide.

Consequently the evidence establishes original metadata/payload preservation,
strict catalogue admission and the exercised capture/codec/inert/raw-camera
images. It does not establish split-process cold loading, Windows D3D11 shader
execution, real Photon delivery, actual coordinator final head pixels, device
readability, or the missing Build768 hardware outcome.

## Verification and handoff

The existing Results archive was independently streamed and verified against
every `members.json` entry, resolving content hardlinks to their real targets:
**7715 exact members, 1043 regular files, 6672 hardlinks, zero discrepancies**.
Its 23,71,506 bytes hash to
`f31bb7823d0150f187e3d5125c5e1bac25b5053eb7dcd6aafd772b3d8bcd28c7`.
The verification took 1.869 seconds. Existing proof remains valid for its actual
declared scope; this review does not relabel it hardware acceptance or a new
runtime pass.

Own evidence is in the main checkout's ignored
`.planning/validation/build768-review/picture-lane/`: source hashes, current
hardware hashes/bounded excerpts, complete archive verification and this report.
No runtime process/cache was created, so no completion-run cache cleanup was
necessary; no shared or foreign cache was removed. Original input logs, native
data, captures, results and archives were preserved.

Test class: A — independent read-only review and developer prose. Executed:
source/path review, two supplied photographs inspected, current peer build
banners/hashes checked, all existing Results archive member bytes/hashes checked,
final diff/local-link review. Inherited: the Build768 Results archive's actual
2407-assertion/22-control union, with the explicit boundaries above. Pending:
matching Build768 hardware evidence and any root-coordinated focused reproduction
of the newly identified lifecycle/exception/cache counterexamples. No full local
gate, golden, Unity or HMD execution is claimed by this lane.
