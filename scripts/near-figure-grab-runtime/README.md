# Near figure grab runtime regression

Run `python3 -B scripts/check-near-figure-grab-runtime.py` with the .NET 8 SDK.
This portable suite requires no game references, Unity installation, scene,
graphics device, network service, or headset. It is suitable for local and CI use.
The optional `--case` selection is partial development evidence, not a whole-suite
pass. `--source-root` and `--producer-root` support explicitly recorded worker
source snapshots before integration; normal use has one checkout as its source.

The suite compiles the entire production `ProximityGrabber.cs`, including the
real election, rival stickiness, figure-tail removal, early returns, trigger and
grip arbitration, ForceGrab, callbacks, cancellation, held self-heal and logging.
It additionally compiles these exact production source regions:

| Production input | Executed source |
| --- | --- |
| RayInteractor | Contiguous hit/claim properties, timestamps, provenance fields, setters and `HasFreshUiHit` |
| IGrabbable | Complete interfaces, including board-kind and per-hand contracts |
| VRInteractables | Complete registry class, usable-shape predicate and refusal interface |
| FigureGrabbable | Constructor, SetPickVolume, CanGrab, AllowsHand and SetProximitySuppressed |
| FigureGrabDriver | Complete TryLaserGrab, SelectByOffsetAnchor and ApplySuppression; original reach/dwell constants |
| RayUguiDriver | Contiguous trigger-down block after its hit-picking boundary, including native-area exception, priority guard, claim and pointer callback |
| FlatScreen | Complete TickPointer preamble through TickPoke and the laser-entry priority checks; complete TickPoke, TryBeginPoke, EndPoke, YieldScreenPointer and physical press condition; original contact/release constants |
| BoardClickDriver | Complete TickFar, RequestClick and ConsumePendingClick, through the native bridge's pending flag |

Only accessibility, wrapper/type names and namespace aliases needed by the host
are adapted. The extracted figure driver suppresses CS8600 because .NET 8's
annotated Dictionary.TryGetValue/fixture component lookup signatures differ from
the original nullable-oblivious net472 Unity/BCL signatures. Compilation errors
and unexpected runtime errors never count as successful causal controls.

Explicit boundaries in `Boundaries.cs` provide deterministic clock and input,
tracking/mode, Unity-style destroyed-object identity, AABB ClosestPoint and
identity-transform geometry, native actor/turn/remote-ownership eligibility
inputs, scene picking, pointer/placement delivery, and log/haptic/tutorial sinks.
The real FigureGrabbable eligibility conjunction runs; its FigureBusy/remote-set
dependencies are supplied as inputs, rather than running SRL or Photon. No game
logic method is patched, and no gameplay or wire model is written by the test.
The host records the board bridge's actual pending flag; it does not execute the
game's Controller.CommonLoop/LateUpdate dispatch. The flat screen's laser geometry
after the entry preamble is a continuation sink; fingertip geometry, start,
active ownership and release/cancel logic all execute their original methods.

Cases cover both hands and producer update orders; current, previous-frame and
expired visual/committed claims; active UI presses whose timestamp has expired;
live offer loss before a producer reads it; busy/dead/remote-held actors; mode,
tracking and collider invalidity; a valid reach extension above an invalid native
collider; invalid previous winners during replacement dwell; trigger/grip
independence; existing card hover-tail behavior; native held-release safety;
actual UI/far-figure/board-click exclusivity; and early flat-screen fingertip
entry plus active/released/canceled pointer ownership. Six hundred repeated
scale/body-height/bottom-contact acquisitions check that every actual glow in the
modeled geometry acquires and releases exactly once.

Thirteen compiling causal controls restore the old beam priority, misleading
figure tail, lost native veto and constructor flag, old dwell retention, ignored
UI/far live offer guards, false glow after grip-bar acquisition, stolen early
screen poke, unclaimed initial/active poke, claim erasure by later hover, and
duplicate native board click. Each must fail its named runtime assertion.

Every run retains copied fixture and production source inputs, source hashes,
per-variant generated source/build logs/runtime logs, result and cleanup receipts
under `.planning/debug/near-figure-grab-runtime/`. The checker kills/reaps its own
children before calling the canonical `cleanup_completed_run` API on success,
failure or interruption. Generated project `bin`/`obj` caches are removed while
source, manifests, results and logs remain.

This proves the exercised source arbitration and callback paths. It does not
prove real Unity physics/collider scaling, native busy-state discovery, rendered
glow pixels, live multiplayer ownership, or headset outcomes.
